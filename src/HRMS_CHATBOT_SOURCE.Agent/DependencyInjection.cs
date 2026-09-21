using System.Text.Json;
using HRMS_CHATBOT_SOURCE.Agent.Checkpointing;
using HRMS_CHATBOT_SOURCE.Agent.History;
using HRMS_CHATBOT_SOURCE.Agent.Notifications;
using HRMS_CHATBOT_SOURCE.Agent.Tools;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;
using HRMS_CHATBOT_SOURCE.Domain.Interfaces;
using MCC.Foundation.Guardrails.Configuration;
using MCC.Foundation.Guardrails.Extensions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS_CHATBOT_SOURCE.Agent;

public static class DependencyInjection
{
    public static IServiceCollection AddHrmsAgentFramework(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AgentFoundrySettings>(configuration.GetSection(AgentFoundrySettings.SectionName));
        services.AddSingleton<HandoffWorkflowTemplate>();

        // Recommended minus Sexual: POSH / harassment-policy RAG chunks are legitimate HR
        // content and were being blocked after PolicyKnowledgeTools returned document text.
        services.AddMccGuardrails(
            GuardrailCategory.SlangProfanity
            | GuardrailCategory.Hate
            | GuardrailCategory.Violence
            | GuardrailCategory.SelfHarm
            | GuardrailCategory.PromptInjection
            | GuardrailCategory.Pii,
            options =>
        {
            options.FailOpen = false;
            options.LocalPromptInjection.DetectionMode = MlNetDetectionMode.BuiltIn;
            options.LocalContentSafety.DetectionMode = MlNetDetectionMode.BuiltIn;
            options.MlNetSlang.DetectionMode = MlNetDetectionMode.BuiltIn;
        });

        // Cosmos when configured, in-memory otherwise. Both AddAzureCosmosService
        // (Infrastructure) and this check key off the same secret, so they agree: if
        // ConnectionStrings:CosmosDb is present, ICosmosService is registered too.
        // Falling back to in-memory rather than throwing keeps local/dev usable without
        // Cosmos configured - but it does not satisfy the "API instances must be
        // stateless, no session affinity" requirement, and that gap is logged loudly.
        services.Configure<CosmosSettings>(configuration.GetSection(CosmosSettings.SectionName));
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("CheckpointManager");
            var cosmosConfigured = !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:CosmosDb"]);

            if (!cosmosConfigured)
            {
                logger.LogWarning(
                    "ConnectionStrings:CosmosDb is not configured; chat checkpoints are held in "
                    + "process memory. This does not survive a restart and is not safe for more "
                    + "than one app instance - configure Cosmos before running with multiple replicas.");
                return CheckpointManager.CreateInMemory();
            }

            var store = new CosmosCheckpointStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<CosmosSettings>>(),
                sp.GetRequiredService<ILogger<CosmosCheckpointStore>>());

            logger.LogInformation("Cosmos-backed checkpoint store enabled.");
            return CheckpointManager.CreateJson((ICheckpointStore<JsonElement>)store);
        });

        // Same Cosmos-or-in-memory decision as the checkpoint manager above, and for the
        // same reason: an in-process dictionary here would make API instances stateful,
        // which fails "no session affinity" the moment there is more than one replica.
        services.Configure<ConversationHistorySettings>(configuration.GetSection(ConversationHistorySettings.SectionName));
        services.AddSingleton<IConversationHistoryStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("ConversationHistoryStore");
            var cosmosConfigured = !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:CosmosDb"]);

            if (!cosmosConfigured)
            {
                logger.LogWarning(
                    "ConnectionStrings:CosmosDb is not configured; conversation history is held in "
                    + "process memory. This does not survive a restart and is not safe for more "
                    + "than one app instance - configure Cosmos before running with multiple replicas.");
                return new InMemoryConversationHistoryStore();
            }

            logger.LogInformation("Cosmos-backed conversation history store enabled.");
            return new CosmosConversationHistoryStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<CosmosSettings>>(),
                sp.GetRequiredService<IOptions<ConversationHistorySettings>>(),
                sp.GetRequiredService<ILogger<CosmosConversationHistoryStore>>());
        });

        // Same Cosmos-or-in-memory decision as the checkpoint manager and conversation
        // history store above. Two containers, one settings section - notifications
        // are cheap, short documents, unlike the other two stores.
        services.Configure<NotificationSettings>(configuration.GetSection(NotificationSettings.SectionName));
        services.AddSingleton<ILeaveStatusNotificationStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("LeaveStatusNotificationStore");
            var cosmosConfigured = !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:CosmosDb"]);

            if (!cosmosConfigured)
            {
                logger.LogWarning(
                    "ConnectionStrings:CosmosDb is not configured; leave-status notifications are held in "
                    + "process memory. This does not survive a restart and is not safe for more "
                    + "than one app instance - configure Cosmos before running with multiple replicas.");
                return new InMemoryLeaveStatusNotificationStore();
            }

            logger.LogInformation("Cosmos-backed leave-status notification store enabled.");
            return new CosmosLeaveStatusNotificationStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<CosmosSettings>>(),
                sp.GetRequiredService<IOptions<NotificationSettings>>(),
                sp.GetRequiredService<ILogger<CosmosLeaveStatusNotificationStore>>());
        });
        services.AddSingleton<ILeaveStatusChangeNotifier, LeaveStatusChangeNotifier>();

        services.AddSingleton<IAdminNotificationStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AdminNotificationStore");
            var cosmosConfigured = !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:CosmosDb"]);

            if (!cosmosConfigured)
            {
                logger.LogWarning(
                    "ConnectionStrings:CosmosDb is not configured; admin notifications are held in "
                    + "process memory. This does not survive a restart and is not safe for more "
                    + "than one app instance - configure Cosmos before running with multiple replicas.");
                return new InMemoryAdminNotificationStore();
            }

            logger.LogInformation("Cosmos-backed admin notification store enabled.");
            return new CosmosAdminNotificationStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<CosmosSettings>>(),
                sp.GetRequiredService<IOptions<NotificationSettings>>(),
                sp.GetRequiredService<ILogger<CosmosAdminNotificationStore>>());
        });
        services.AddSingleton<IAdminLeaveNotifier, AdminLeaveNotifier>();

        services.AddSingleton<IChatClient>(sp => FoundryChatClientFactory.Create(
            sp,
            sp.GetRequiredService<IOptions<AgentFoundrySettings>>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("FoundryChatClient")));
        services.AddSingleton<PolicyKnowledgeTools>();
        services.AddSingleton<LeaveApplicationTools>();
        services.AddSingleton<LeaveApprovalTools>();
        services.AddSingleton<DocumentAgentTools>();
        services.AddSingleton<RelativeDateParsingTools>();
        services.AddSingleton<HrmsHandoffWorkflowFactory>();
        services.AddSingleton<IHrmsChatRuntime, HrmsChatRuntime>();

        return services;
    }
}
