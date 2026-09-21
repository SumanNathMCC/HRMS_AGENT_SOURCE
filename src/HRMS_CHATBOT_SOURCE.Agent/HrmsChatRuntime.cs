using System.Diagnostics;
using HRMS_CHATBOT_SOURCE.Agent.History;
using HRMS_CHATBOT_SOURCE.Agent.Notifications;
using HRMS_CHATBOT_SOURCE.Agent.Skills;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Domain.Interfaces;
using HRMS_CHATBOT_SOURCE.Logic.Common;
using MCC.Foundation.Guardrails;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRMS_CHATBOT_SOURCE.Agent;

public sealed class HrmsChatRuntime : IHrmsChatRuntime
{
    private const string GuardrailBlockedReply =
        "I can't help with that request. If you need assistance, please contact HR directly.";

    private readonly HrmsHandoffWorkflowFactory _workflowFactory;
    private readonly CheckpointManager _checkpointManager;
    private readonly IConversationHistoryStore _historyStore;
    private readonly ILeaveStatusNotificationStore _leaveStatusNotificationStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HrmsChatRuntime> _logger;

    public HrmsChatRuntime(
        HrmsHandoffWorkflowFactory workflowFactory,
        CheckpointManager checkpointManager,
        IConversationHistoryStore historyStore,
        ILeaveStatusNotificationStore leaveStatusNotificationStore,
        IServiceScopeFactory scopeFactory,
        ILogger<HrmsChatRuntime> logger)
    {
        _workflowFactory = workflowFactory;
        _checkpointManager = checkpointManager;
        _historyStore = historyStore;
        _leaveStatusNotificationStore = leaveStatusNotificationStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ChatTurnResponse> RunAsync(
        IReadOnlyCollection<string> enabledAgentNames,
        string? conversationId,
        string message,
        string? authenticatedMobile = null,
        CancellationToken cancellationToken = default)
    {
        ChatTurnResponse? result = null;

        await foreach (var chunk in RunStreamAsync(
            enabledAgentNames,
            conversationId,
            message,
            authenticatedMobile,
            cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(chunk.Type, "done", StringComparison.OrdinalIgnoreCase))
            {
                result = new ChatTurnResponse
                {
                    ConversationId = chunk.ConversationId ?? string.Empty,
                    Reply = chunk.Reply ?? string.Empty,
                    EnabledAgents = chunk.EnabledAgents ?? [],
                    LastSpeaker = chunk.LastSpeaker
                };
            }
            else if (string.Equals(chunk.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(chunk.Message ?? "Chat stream failed.");
            }
        }

        return result ?? throw new InvalidOperationException("Chat stream ended without a done chunk.");
    }

    public async IAsyncEnumerable<ChatStreamChunk> RunStreamAsync(
        IReadOnlyCollection<string> enabledAgentNames,
        string? conversationId,
        string message,
        string? authenticatedMobile = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new System.ComponentModel.DataAnnotations.ValidationException("Message is required.");
        }

        var id = string.IsNullOrWhiteSpace(conversationId)
            ? Guid.NewGuid().ToString("N")
            : conversationId.Trim();

        // Phase timings, logged as one summary line at the end - added because a slow
        // turn was otherwise silent in the logs (no per-phase output means no way to
        // tell whether the LLM, Cosmos, or something else is the actual bottleneck).
        var stopwatch = Stopwatch.StartNew();
        var phaseTimings = new List<(string Phase, long ElapsedMs)>();
        long lastElapsed = 0;

        void RecordPhase(string phase)
        {
            var now = stopwatch.ElapsedMilliseconds;
            phaseTimings.Add((phase, now - lastElapsed));
            lastElapsed = now;
        }

        // Read-append-write against the external store rather than an in-process cache -
        // this is what makes the API stateless: any instance can serve any turn of a
        // conversation, and a restart mid-conversation does not lose history. There is no
        // cross-instance locking here, same as the checkpoint store above; a genuine
        // double-submit race on the same conversation is not guarded against.
        var existingHistory = await _historyStore.GetHistoryAsync(id, cancellationToken).ConfigureAwait(false);
        RecordPhase("history.get");

        var history = new List<ChatMessage>(existingHistory)
        {
            new ChatMessage(ChatRole.User, message.Trim())
        };

        var enabled = enabledAgentNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var workflow = _workflowFactory.Build(enabled);
        RecordPhase("workflow.build");

        using var scope = _scopeFactory.CreateScope();
        var commonLogic = scope.ServiceProvider.GetRequiredService<ICommonLogic>();
        var turnMessages = ApplyCurrentAccessOverride(history, enabled, commonLogic, authenticatedMobile);

        var noticePrefix = await GetPendingLeaveNoticePrefixAsync(authenticatedMobile, cancellationToken)
            .ConfigureAwait(false);
        RecordPhase("notifications.check");
        if (!string.IsNullOrEmpty(noticePrefix))
        {
            yield return ChatStreamChunk.Delta(noticePrefix);
        }

        var agentReplyBuilder = new System.Text.StringBuilder();
        string? lastSpeaker = null;

        await foreach (var delta in StreamTurnAsync(workflow, turnMessages, id, cancellationToken).ConfigureAwait(false))
        {
            if (delta.IsGuardrailBlock)
            {
                if (agentReplyBuilder.Length == 0)
                {
                    agentReplyBuilder.Append(GuardrailBlockedReply);
                    yield return ChatStreamChunk.Delta(GuardrailBlockedReply);
                }
                else
                {
                    _logger.LogWarning(
                        "Guardrail blocked further generation; preserving {Length} characters already streamed.",
                        agentReplyBuilder.Length);
                }

                break;
            }

            if (!string.IsNullOrEmpty(delta.Text))
            {
                agentReplyBuilder.Append(delta.Text);
                lastSpeaker = delta.LastSpeaker ?? lastSpeaker;
                yield return ChatStreamChunk.Delta(delta.Text);
            }
        }

        var agentReply = agentReplyBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(agentReply))
        {
            _logger.LogWarning("Handoff workflow produced an empty reply.");
            agentReply = "I could not produce a response. Please try again.";
            yield return ChatStreamChunk.Delta(agentReply);
        }

        RecordPhase("turn.execute");

        var fullReply = string.IsNullOrEmpty(noticePrefix)
            ? agentReply
            : noticePrefix + agentReply;

        history.Add(new ChatMessage(ChatRole.Assistant, fullReply));
        await _historyStore.SaveHistoryAsync(id, history, cancellationToken).ConfigureAwait(false);
        RecordPhase("history.save");

        _logger.LogInformation(
            "Chat turn timing for {ConversationId}: {Timings} (total {TotalMs}ms)",
            id,
            string.Join(", ", phaseTimings.Select(t => $"{t.Phase}={t.ElapsedMs}ms")),
            stopwatch.ElapsedMilliseconds);

        yield return ChatStreamChunk.Done(id, fullReply, enabled, lastSpeaker);
    }

    private async Task<string?> GetPendingLeaveNoticePrefixAsync(
        string? authenticatedMobile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authenticatedMobile))
        {
            return null;
        }

        // Bounded like LogCheckpointAsync: a Cosmos hiccup here should cost the user a
        // few seconds and a deferred notification (it will surface next turn), never a
        // stalled reply.
        try
        {
            var pending = await WithTimeoutAsync(
                _leaveStatusNotificationStore.GetPendingAsync(authenticatedMobile, cancellationToken),
                BestEffortCosmosTimeout,
                "pending leave notifications lookup").ConfigureAwait(false);

            if (pending.Count == 0)
            {
                return null;
            }

            var notices = pending.Select(n => $"Your leave request ({n.ApplicationReference}) was {n.NewStatus}."
                + (string.IsNullOrWhiteSpace(n.Note) ? string.Empty : $" Note: {n.Note}"));

            await WithTimeoutAsync(
                _leaveStatusNotificationStore.MarkDeliveredAsync(authenticatedMobile, pending.Select(n => n.Id).ToList(), cancellationToken),
                BestEffortCosmosTimeout,
                "mark leave notifications delivered").ConfigureAwait(false);

            return string.Join(Environment.NewLine, notices) + Environment.NewLine + Environment.NewLine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not check pending leave notifications for {Mobile}.", authenticatedMobile);
            return null;
        }
    }

    /// <summary>
    /// Races an operation against a timeout. Needed because MCC.Foundation.CosmosHelper's
    /// ICosmosService methods take no CancellationToken, so a token passed down is
    /// simply not observed by the SDK's internal retry loop - a linked
    /// CancellationTokenSource would expire without actually unblocking anything. On
    /// timeout the slow task is abandoned (it finishes or fails on its own in the
    /// background, with its exception observed so it never surfaces as an
    /// UnobservedTaskException) and the caller moves on.
    /// </summary>
    private async Task<T> WithTimeoutAsync<T>(Task<T> operation, TimeSpan timeout, string description)
    {
        var completed = await Task.WhenAny(operation, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed == operation)
        {
            return await operation.ConfigureAwait(false);
        }

        ObserveAbandoned(operation, description);
        throw new TimeoutException($"{description} exceeded {timeout.TotalSeconds:0}s and was abandoned.");
    }

    private async Task WithTimeoutAsync(Task operation, TimeSpan timeout, string description)
    {
        var completed = await Task.WhenAny(operation, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed == operation)
        {
            await operation.ConfigureAwait(false);
            return;
        }

        ObserveAbandoned(operation, description);
        throw new TimeoutException($"{description} exceeded {timeout.TotalSeconds:0}s and was abandoned.");
    }

    private void ObserveAbandoned(Task operation, string description)
    {
        _ = operation.ContinueWith(
            t => _logger.LogWarning(t.Exception?.GetBaseException(), "Abandoned {Description} eventually failed in the background.", description),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// Wraps turn execution so a guardrail block degrades into a safe reply instead of
    /// an unhandled exception. The shared IChatClient is wrapped with UseMccGuardrails,
    /// so every model call this turn makes - the routing turn, each specialist turn,
    /// and the tool-result round-trip after PolicyKnowledgeTools returns - is screened
    /// on both the way in and the way out; a violation throws from inside whichever
    /// call tripped it, which can be anywhere in ExecuteTurnEventStreamAsync.
    /// </summary>
    private async IAsyncEnumerable<(string? Text, string? LastSpeaker, bool IsGuardrailBlock)> StreamTurnAsync(
        Workflow workflow,
        IReadOnlyList<ChatMessage> messages,
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var evt in ExecuteTurnEventStreamAsync(workflow, messages, sessionId, cancellationToken)
            .ConfigureAwait(false))
        {
            switch (evt)
            {
                case GuardrailBlockedMarker:
                    yield return (null, null, true);
                    yield break;
                case TurnDelta delta:
                    yield return (delta.Text, delta.LastSpeaker, false);
                    break;
            }
        }
    }

    private async IAsyncEnumerable<object> ExecuteTurnEventStreamAsync(
        Workflow workflow,
        IReadOnlyList<ChatMessage> messages,
        string sessionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Passing the checkpoint manager and a stable session id (the conversation id)
        // makes this a genuine MAF-tracked run rather than a session-less one-off call:
        // MAF externalises a real, queryable checkpoint per run under this session,
        // which is the audit trail SP10 calls for and the exact seam ResumeStreamingAsync
        // will need once a RequestPort exists.
        var innerStopwatch = Stopwatch.StartNew();
        await using var run = await InProcessExecution
            .RunStreamingAsync(workflow, messages, _checkpointManager, sessionId, cancellationToken)
            .ConfigureAwait(false);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);
        var startElapsedMs = innerStopwatch.ElapsedMilliseconds;
        _logger.LogInformation(
            "RunStreamingAsync + TrySendMessageAsync for {SessionId} took {ElapsedMs}ms.",
            sessionId,
            startElapsedMs);

        var text = new System.Text.StringBuilder();

        await foreach (var evt in run.WatchStreamAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (evt is ExecutorFailedEvent failed)
            {
                if (GuardrailViolationException.TryUnwrap(failed.Data, out var blocked))
                {
                    yield return new GuardrailBlockedMarker();
                    yield break;
                }

                throw failed.Data ?? new InvalidOperationException($"Executor '{failed.ExecutorId}' failed.");
            }

            if (evt is WorkflowErrorEvent workflowError)
            {
                if (GuardrailViolationException.TryUnwrap(workflowError.Exception, out var blocked))
                {
                    yield return new GuardrailBlockedMarker();
                    yield break;
                }

                throw workflowError.Exception ?? new InvalidOperationException("Workflow error.");
            }

            if (evt is AgentResponseUpdateEvent update)
            {
                if (!string.IsNullOrEmpty(update.Update.Text))
                {
                    text.Append(update.Update.Text);
                    yield return new TurnDelta(update.Update.Text, update.ExecutorId);
                }
            }
            else if (evt is WorkflowOutputEvent output)
            {
                if (output.As<List<ChatMessage>>() is { Count: > 0 } outputMessages)
                {
                    var lastAssistant = outputMessages.LastOrDefault(message => message.Role == ChatRole.Assistant);
                    if (lastAssistant != null && text.Length == 0 && !string.IsNullOrEmpty(lastAssistant.Text))
                    {
                        text.Append(lastAssistant.Text);
                        yield return new TurnDelta(lastAssistant.Text, null);
                    }
                }

                break;
            }
        }

        var watchLoopElapsedMs = innerStopwatch.ElapsedMilliseconds;
        _logger.LogInformation(
            "WatchStreamAsync loop for {SessionId} took {ElapsedMs}ms (includes the LLM/tool calls).",
            sessionId,
            watchLoopElapsedMs - startElapsedMs);

        await LogCheckpointAsync(sessionId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Checkpoint read-back for {SessionId} took {ElapsedMs}ms. Total ExecuteTurnEventStreamAsync: {TotalMs}ms.",
            sessionId,
            innerStopwatch.ElapsedMilliseconds - watchLoopElapsedMs,
            innerStopwatch.ElapsedMilliseconds);
    }

    private sealed record TurnDelta(string? Text, string? LastSpeaker);

    private sealed class GuardrailBlockedMarker;

    /// <summary>
    /// Bounded wait for the two best-effort Cosmos calls in a turn. Both already
    /// degrade gracefully on failure, but "gracefully" only helps if the failure is
    /// fast: the Cosmos SDK retries internally on connectivity errors (GoneException /
    /// ServiceUnavailable) for tens of seconds before it throws, and that whole wait
    /// was landing on the user's reply. Measured: ~36s of a 52s turn was the checkpoint
    /// read-back alone, for a call whose only job is to log an id.
    /// </summary>
    private static readonly TimeSpan BestEffortCosmosTimeout = TimeSpan.FromSeconds(3);

    private async Task LogCheckpointAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var checkpoint = await WithTimeoutAsync(
                _checkpointManager.GetLatestCheckpointAsync(sessionId, cancellationToken).AsTask(),
                BestEffortCosmosTimeout,
                "checkpoint read-back").ConfigureAwait(false);

            if (checkpoint != null)
            {
                _logger.LogInformation(
                    "Checkpointed conversation {SessionId} at {CheckpointId}.",
                    checkpoint.SessionId,
                    checkpoint.CheckpointId);
            }
        }
        catch (Exception ex)
        {
            // Observability only - a checkpoint lookup failure must not fail the turn.
            // Logged at Warning (not Debug) so a slow/failing Cosmos call here is
            // actually visible at the app's default log level, instead of silently
            // vanishing - this was previously invisible even when it was the
            // dominant cost of a turn.
            _logger.LogWarning(ex, "Could not read the latest checkpoint for conversation {SessionId}.", sessionId);
        }
    }

    internal IReadOnlyList<ChatMessage> ApplyCurrentAccessOverride(
        IReadOnlyList<ChatMessage> history,
        IReadOnlyCollection<string> enabledAgentNames,
        ICommonLogic commonLogic,
        string? authenticatedMobile = null)
    {
        var noticeText = SupervisorSkillComposer.BuildCurrentAccessNotice(enabledAgentNames);

        if (!string.IsNullOrWhiteSpace(authenticatedMobile))
        {
            noticeText += Environment.NewLine + Environment.NewLine
                + SupervisorSkillComposer.BuildAuthenticatedEmployeeNotice(authenticatedMobile);
        }

        var latestUserMessage = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        var parsedDateNotice = SupervisorSkillComposer.BuildParsedDateNotice(latestUserMessage, commonLogic);
        if (!string.IsNullOrWhiteSpace(parsedDateNotice))
        {
            noticeText += Environment.NewLine + Environment.NewLine + parsedDateNotice;
        }

        var notice = new ChatMessage(ChatRole.System, noticeText);

        if (history.Count == 0)
        {
            return [notice];
        }

        var turnMessages = new List<ChatMessage>(history.Count + 1);
        turnMessages.AddRange(history.Take(history.Count - 1));
        turnMessages.Add(notice);
        turnMessages.Add(history[^1]);
        return turnMessages;
    }
}
