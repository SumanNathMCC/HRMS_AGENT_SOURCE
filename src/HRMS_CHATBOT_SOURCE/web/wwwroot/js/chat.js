(function () {
    "use strict";

    var VOICE_DISABLED_MESSAGE = "Voice feature is disabled for you.";

    // Root-absolute paths — relative "api/..." breaks on /chat and /Chat (becomes /chat/api/...).
    var API = {
        activeMobileNumbers: "/api/GetActiveMobileNumbersAsync",
        voiceInputEnabled: "/api/GetVoiceInputEnabledAsync",
        transcribeVoice: "/api/TranscribeVoiceAsync",
        chatStream: "/api/ChatStreamAsync"
    };

    var MobileDirectoryApiService = {
        getActiveMobileNumbers: function (signal) {
            return fetch(API.activeMobileNumbers, { method: "GET", signal: signal })
                .then(function (response) {
                    if (!response.ok) {
                        throw new Error("HTTP " + response.status);
                    }
                    return response.json();
                });
        }
    };

    var VoiceApiService = {
        getVoiceInputEnabled: function (mobile, signal) {
            var url = API.voiceInputEnabled + "?mobile=" + encodeURIComponent(mobile);
            return fetch(url, { method: "GET", signal: signal })
                .then(function (response) {
                    if (!response.ok) {
                        throw new Error("HTTP " + response.status);
                    }
                    return response.json();
                });
        },

        transcribeVoice: function (mobile, wavBlob, signal) {
            var formData = new FormData();
            formData.append("mobile", mobile);
            formData.append("audio", wavBlob, "voice.wav");

            return fetch(API.transcribeVoice, {
                method: "POST",
                signal: signal,
                body: formData
            }).then(function (response) {
                return response.json().then(function (data) {
                    if (!response.ok) {
                        throw new Error(data.error_message || data.message || "Transcription failed.");
                    }
                    return data;
                });
            });
        }
    };

    var ChatApiService = {
        sendMessage: function (mobile, message, conversationId, signal, onDelta) {
            return fetch(API.chatStream, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    Accept: "application/x-ndjson"
                },
                signal: signal,
                body: JSON.stringify({
                    mobile: mobile,
                    message: message,
                    conversation_id: conversationId
                })
            }).then(function (response) {
                if (!response.ok) {
                    return response.text().then(function (text) {
                        var errMsg = "HTTP " + response.status;
                        try {
                            var parsed = JSON.parse(text);
                            errMsg = parsed.error_message || parsed.message || errMsg;
                        } catch (e) { /* ignore */ }
                        throw new Error(errMsg);
                    });
                }

                if (!response.body || !response.body.getReader) {
                    throw new Error("Streaming is not supported in this browser.");
                }

                var reader = response.body.getReader();
                var decoder = new TextDecoder();
                var buffer = "";
                var finalData = null;

                function pump() {
                    return reader.read().then(function (result) {
                        if (result.done) {
                            return finalData;
                        }

                        buffer += decoder.decode(result.value, { stream: true });
                        var lines = buffer.split("\n");
                        buffer = lines.pop() || "";

                        lines.forEach(function (line) {
                            if (!line.trim()) return;
                            var chunk = JSON.parse(line);
                            if (chunk.type === "delta" && chunk.text && onDelta) {
                                onDelta(chunk.text);
                            } else if (chunk.type === "done") {
                                finalData = chunk;
                            } else if (chunk.type === "error") {
                                throw new Error(chunk.message || "Chat stream failed.");
                            }
                        });

                        return pump();
                    });
                }

                return pump();
            });
        }
    };

    var state = {
        mobileNumbers: [],
        selectedMobile: null,
        activeConversation: null,
        conversations: {},
        conversationOrder: [],
        loadAbort: null,
        sendAbort: null,
        voiceEnabled: false,
        voiceDisabledMessage: VOICE_DISABLED_MESSAGE,
        isRecording: false,
        isTranscribing: false,
        isSending: false,
        recorder: null
    };

    var els = {
        mobileSelect: document.getElementById("waMobileSelect"),
        refreshBtn: document.getElementById("waRefreshBtn"),
        startChatBtn: document.getElementById("waStartChatBtn"),
        searchInput: document.getElementById("waSearchInput"),
        list: document.getElementById("waList"),
        chatPlaceholder: document.getElementById("waChatPlaceholder"),
        chatActive: document.getElementById("waChatActive"),
        activeAvatar: document.getElementById("waActiveAvatar"),
        activeName: document.getElementById("waActiveName"),
        activeStatus: document.getElementById("waActiveStatus"),
        messages: document.getElementById("waMessages"),
        input: document.getElementById("waInput"),
        sendBtn: document.getElementById("waSendBtn"),
        micBtn: document.getElementById("waMicBtn")
    };

    function initials(name) {
        return (name || "?").trim().charAt(0).toUpperCase();
    }

    function formatTime(date) {
        return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    }

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function loadMobileNumbers() {
        els.mobileSelect.disabled = true;
        els.mobileSelect.innerHTML = '<option value="">Loading mobile numbers…</option>';
        els.startChatBtn.hidden = true;

        if (state.loadAbort) state.loadAbort.abort();
        state.loadAbort = new AbortController();

        MobileDirectoryApiService.getActiveMobileNumbers(state.loadAbort.signal)
            .then(function (data) {
                state.mobileNumbers = (data && data.mobile_numbers) || [];
                populateMobileSelect();
            })
            .catch(function (err) {
                if (err.name === "AbortError") return;
                els.mobileSelect.innerHTML = '<option value="">Couldn’t load numbers — retry ↻</option>';
                els.mobileSelect.disabled = true;
            });
    }

    function populateMobileSelect() {
        els.mobileSelect.innerHTML = "";

        if (state.mobileNumbers.length === 0) {
            var emptyOpt = document.createElement("option");
            emptyOpt.value = "";
            emptyOpt.textContent = "No active mobile numbers found";
            els.mobileSelect.appendChild(emptyOpt);
            els.mobileSelect.disabled = true;
            return;
        }

        var placeholder = document.createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Select Mobile Number";
        els.mobileSelect.appendChild(placeholder);

        state.mobileNumbers.forEach(function (mobile) {
            var option = document.createElement("option");
            option.value = mobile;
            option.textContent = mobile;
            els.mobileSelect.appendChild(option);
        });

        els.mobileSelect.disabled = false;
    }

    function onMobileSelectChanged() {
        state.selectedMobile = els.mobileSelect.value || null;
        var alreadyStarted = state.selectedMobile && state.conversationOrder.indexOf(state.selectedMobile) !== -1;
        els.startChatBtn.hidden = !state.selectedMobile || alreadyStarted;

        if (alreadyStarted) {
            openConversation(state.selectedMobile);
        }
    }

    function conversationFor(mobile) {
        if (!state.conversations[mobile]) {
            state.conversations[mobile] = { conversationId: null, messages: [] };
        }
        return state.conversations[mobile];
    }

    function startChat() {
        var mobile = state.selectedMobile;
        if (!mobile) return;

        if (state.conversationOrder.indexOf(mobile) === -1) {
            state.conversationOrder.unshift(mobile);
        }
        conversationFor(mobile);

        els.startChatBtn.hidden = true;
        renderList(els.searchInput.value.trim());
        openConversation(mobile);
    }

    function renderEmpty(message) {
        els.list.innerHTML = '<div class="wa-empty-state"><p>' + escapeHtml(message) + "</p></div>";
    }

    function renderList(filterText) {
        var mobiles = state.conversationOrder.filter(function (mobile) {
            return !filterText || mobile.toLowerCase().indexOf(filterText.toLowerCase()) !== -1;
        });

        if (mobiles.length === 0) {
            renderEmpty(filterText
                ? "No conversations match your search."
                : "Select a mobile number above and click Start Chat to begin.");
            return;
        }

        els.list.innerHTML = "";
        mobiles.forEach(function (mobile) {
            var convo = conversationFor(mobile);
            var lastMessage = convo.messages[convo.messages.length - 1];

            var item = document.createElement("div");
            item.className = "wa-list-item" + (mobile === state.activeConversation ? " active" : "");
            item.setAttribute("role", "button");
            item.setAttribute("tabindex", "0");

            item.innerHTML =
                '<div class="wa-avatar">' + escapeHtml(initials(mobile)) + "</div>" +
                '<div class="wa-list-item-body">' +
                '<div class="wa-list-item-top">' +
                '<span class="wa-list-item-name">' + escapeHtml(mobile) + "</span>" +
                '<span class="wa-list-item-time">' + (lastMessage ? formatTime(lastMessage.time) : "") + "</span>" +
                "</div>" +
                '<div class="wa-list-item-preview">' +
                (lastMessage ? escapeHtml(lastMessage.preview) : "Start chatting…") +
                "</div>" +
                "</div>";

            item.addEventListener("click", function () { openConversation(mobile); });
            item.addEventListener("keydown", function (e) {
                if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openConversation(mobile); }
            });

            els.list.appendChild(item);
        });
    }

    function bumpConversationToTop(mobile) {
        var idx = state.conversationOrder.indexOf(mobile);
        if (idx > 0) {
            state.conversationOrder.splice(idx, 1);
            state.conversationOrder.unshift(mobile);
        }
    }

    function updateSendButtonState() {
        if (!els.sendBtn) return;

        var hasText = els.input.value.trim().length > 0;
        els.sendBtn.classList.toggle("active", hasText);
        els.sendBtn.disabled = state.isSending || state.isTranscribing || !hasText;
    }

    function updateMicButtonState() {
        if (!els.micBtn) return;

        els.micBtn.classList.toggle("wa-mic-recording", state.isRecording);
        els.micBtn.classList.toggle("wa-mic-transcribing", state.isTranscribing);
        els.micBtn.classList.toggle("wa-mic-disabled", !state.voiceEnabled);

        var blocked = !state.voiceEnabled || state.isSending || state.isTranscribing;

        els.micBtn.disabled = blocked && !state.isRecording;
        els.micBtn.title = state.voiceEnabled
            ? (state.isRecording ? "Tap to stop recording" : "Tap to record voice message")
            : state.voiceDisabledMessage;
    }

    function refreshVoiceAccess(mobile) {
        if (!mobile) {
            state.voiceEnabled = false;
            updateMicButtonState();
            return Promise.resolve();
        }

        return VoiceApiService.getVoiceInputEnabled(mobile)
            .then(function (data) {
                state.voiceEnabled = !!(data && data.voice_enabled);
                state.voiceDisabledMessage = (data && data.disabled_message) || VOICE_DISABLED_MESSAGE;
                updateMicButtonState();
            })
            .catch(function () {
                state.voiceEnabled = false;
                state.voiceDisabledMessage = VOICE_DISABLED_MESSAGE;
                updateMicButtonState();
            });
    }

    function openConversation(mobile) {
        state.activeConversation = mobile;
        renderList(els.searchInput.value.trim());

        els.chatPlaceholder.hidden = true;
        els.chatActive.hidden = false;
        els.activeAvatar.textContent = initials(mobile);
        els.activeName.textContent = mobile;
        els.activeStatus.textContent = "online";

        renderMessages();
        refreshVoiceAccess(mobile);
        els.input.focus();
    }

    function renderMessages() {
        var convo = conversationFor(state.activeConversation);
        els.messages.innerHTML = "";
        convo.messages.forEach(function (msg) {
            appendBubbleElement(msg);
        });
        scrollToBottom();
    }

    function appendBubbleElement(msg) {
        var row = document.createElement("div");
        row.className = "wa-bubble-row " + msg.kind;

        var bubble = document.createElement("div");
        bubble.className = "wa-bubble " + msg.kind + (msg.isError ? " error" : "");
        bubble.dataset.msgId = msg.id;

        if (msg.pending) {
            bubble.innerHTML = '<span class="wa-typing-dots"><span></span><span></span><span></span></span>';
        } else {
            bubble.textContent = msg.text;
            if (msg.kind !== "system") {
                var meta = document.createElement("span");
                meta.className = "wa-bubble-meta";
                meta.textContent = formatTime(msg.time);
                bubble.appendChild(meta);
            }
        }

        row.appendChild(bubble);
        els.messages.appendChild(row);
        return bubble;
    }

    function scrollToBottom() {
        els.messages.scrollTop = els.messages.scrollHeight;
    }

    var msgCounter = 0;
    function nextId() { return "m" + (++msgCounter); }

    function setSending(isSending) {
        state.isSending = isSending;
        updateSendButtonState();
        updateMicButtonState();
    }

    function appendToBubble(msg, extraText) {
        msg.text = (msg.text || "") + extraText;
        msg.pending = false;

        var bubble = els.messages.querySelector('[data-msg-id="' + msg.id + '"]');
        if (!bubble) return;

        bubble.innerHTML = "";
        bubble.appendChild(document.createTextNode(msg.text));

        var meta = document.createElement("span");
        meta.className = "wa-bubble-meta";
        meta.textContent = formatTime(msg.time);
        bubble.appendChild(meta);

        scrollToBottom();
    }

    function sendChatTurn(mobile, text) {
        var convo = conversationFor(mobile);

        var userMsg = { id: nextId(), kind: "out", text: text, time: new Date(), preview: text };
        convo.messages.push(userMsg);

        var pendingMsg = { id: nextId(), kind: "in", text: "", time: new Date(), pending: true };
        convo.messages.push(pendingMsg);

        if (state.activeConversation === mobile) {
            appendBubbleElement(userMsg);
            appendBubbleElement(pendingMsg);
            scrollToBottom();
        }

        bumpConversationToTop(mobile);
        renderList(els.searchInput.value.trim());

        setSending(true);
        if (state.sendAbort) state.sendAbort.abort();
        state.sendAbort = new AbortController();

        return ChatApiService.sendMessage(
            mobile,
            text,
            convo.conversationId,
            state.sendAbort.signal,
            function (delta) {
                pendingMsg.pending = false;
                if (state.activeConversation === mobile) {
                    appendToBubble(pendingMsg, delta);
                } else {
                    pendingMsg.text = (pendingMsg.text || "") + delta;
                }
            }
        )
            .then(function (data) {
                convo.conversationId = (data && data.conversation_id) || convo.conversationId;
                pendingMsg.pending = false;

                // Keep streamed deltas when present. The done chunk can be guardrail-only
                // even though useful RAG text was already streamed (same as test-chat-widget.js).
                if (!pendingMsg.text || !pendingMsg.text.trim()) {
                    pendingMsg.text = (data && data.reply) || "(no reply)";
                }

                pendingMsg.time = new Date();
                pendingMsg.preview = pendingMsg.text;

                if (state.activeConversation === mobile) {
                    replaceBubble(pendingMsg);
                }
                renderList(els.searchInput.value.trim());
            })
            .catch(function (err) {
                if (err.name === "AbortError") return;
                pendingMsg.pending = false;
                pendingMsg.isError = true;
                pendingMsg.text = err.message || "Message failed to send. Please try again.";
                pendingMsg.time = new Date();

                if (state.activeConversation === mobile) {
                    replaceBubble(pendingMsg);
                }
            })
            .finally(function () {
                setSending(false);
                els.input.focus();
            });
    }

    function sendMessage() {
        var text = els.input.value.trim();
        var mobile = state.activeConversation;
        if (!text || !mobile) return;

        els.input.value = "";
        autosizeInput();
        updateSendButtonState();

        sendChatTurn(mobile, text);
    }

    function replaceBubble(msg) {
        var bubble = els.messages.querySelector('[data-msg-id="' + msg.id + '"]');
        if (!bubble) return;

        bubble.classList.toggle("error", !!msg.isError);
        bubble.innerHTML = "";
        bubble.appendChild(document.createTextNode(msg.text));

        var meta = document.createElement("span");
        meta.className = "wa-bubble-meta";
        meta.textContent = formatTime(msg.time);
        bubble.appendChild(meta);

        scrollToBottom();
    }

    function autosizeInput() {
        els.input.style.height = "auto";
        els.input.style.height = Math.min(els.input.scrollHeight, 120) + "px";
    }

    function encodeWav(samples, sampleRate) {
        var buffer = new ArrayBuffer(44 + samples.length * 2);
        var view = new DataView(buffer);

        function writeString(offset, str) {
            for (var i = 0; i < str.length; i++) {
                view.setUint8(offset + i, str.charCodeAt(i));
            }
        }

        writeString(0, "RIFF");
        view.setUint32(4, 36 + samples.length * 2, true);
        writeString(8, "WAVE");
        writeString(12, "fmt ");
        view.setUint32(16, 16, true);
        view.setUint16(20, 1, true);
        view.setUint16(22, 1, true);
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);
        view.setUint16(32, 2, true);
        view.setUint16(34, 16, true);
        writeString(36, "data");
        view.setUint32(40, samples.length * 2, true);

        var offset = 44;
        for (var j = 0; j < samples.length; j++) {
            var s = Math.max(-1, Math.min(1, samples[j]));
            view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7fff, true);
            offset += 2;
        }

        return new Blob([view], { type: "audio/wav" });
    }

    function createVoiceRecorder() {
        var audioContext = null;
        var mediaStream = null;
        var processor = null;
        var source = null;
        var chunks = [];

        return {
            start: function () {
                chunks = [];
                return navigator.mediaDevices.getUserMedia({ audio: true })
                    .then(function (stream) {
                        mediaStream = stream;
                        audioContext = new (window.AudioContext || window.webkitAudioContext)({ sampleRate: 16000 });
                        source = audioContext.createMediaStreamSource(stream);
                        processor = audioContext.createScriptProcessor(4096, 1, 1);

                        processor.onaudioprocess = function (event) {
                            chunks.push(new Float32Array(event.inputBuffer.getChannelData(0)));
                        };

                        source.connect(processor);
                        processor.connect(audioContext.destination);
                    });
            },

            stop: function () {
                if (processor) {
                    processor.disconnect();
                    processor.onaudioprocess = null;
                }
                if (source) source.disconnect();
                if (mediaStream) {
                    mediaStream.getTracks().forEach(function (track) { track.stop(); });
                }

                var sampleRate = audioContext ? audioContext.sampleRate : 16000;
                if (audioContext) {
                    audioContext.close();
                }

                var totalLength = chunks.reduce(function (sum, chunk) { return sum + chunk.length; }, 0);
                var samples = new Float32Array(totalLength);
                var offset = 0;
                chunks.forEach(function (chunk) {
                    samples.set(chunk, offset);
                    offset += chunk.length;
                });

                mediaStream = null;
                audioContext = null;
                processor = null;
                source = null;
                chunks = [];

                return encodeWav(samples, sampleRate);
            }
        };
    }

    function toggleVoiceRecording() {
        if (!state.voiceEnabled || state.isTranscribing || state.isSending) {
            return;
        }

        var mobile = state.activeConversation;
        if (!mobile) return;

        if (!state.isRecording) {
            state.recorder = createVoiceRecorder();
            state.recorder.start()
                .then(function () {
                    state.isRecording = true;
                    updateMicButtonState();
                })
                .catch(function () {
                    state.isRecording = false;
                    state.recorder = null;
                    updateMicButtonState();
                    alert("Microphone access is required for voice input.");
                });
            return;
        }

        state.isRecording = false;
        updateMicButtonState();

        var recorder = state.recorder;
        state.recorder = null;
        if (!recorder) return;

        var wavBlob = recorder.stop();
        if (!wavBlob || wavBlob.size <= 44) {
            updateSendButtonState();
            updateMicButtonState();
            return;
        }

        state.isTranscribing = true;
        updateSendButtonState();
        updateMicButtonState();

        if (state.sendAbort) state.sendAbort.abort();
        state.sendAbort = new AbortController();

        VoiceApiService.transcribeVoice(mobile, wavBlob, state.sendAbort.signal)
            .then(function (data) {
                var englishText = (data && data.english_text) || "";
                if (!englishText.trim()) {
                    throw new Error("Could not transcribe the audio.");
                }
                return sendChatTurn(mobile, englishText.trim());
            })
            .catch(function (err) {
                if (err.name === "AbortError") return;

                var convo = conversationFor(mobile);
                var errMsg = { id: nextId(), kind: "out", text: err.message || "Voice transcription failed.", time: new Date(), preview: "Voice failed", isError: true };
                convo.messages.push(errMsg);

                if (state.activeConversation === mobile) {
                    appendBubbleElement(errMsg);
                    scrollToBottom();
                }
            })
            .finally(function () {
                state.isTranscribing = false;
                updateSendButtonState();
                updateMicButtonState();
            });
    }

    els.mobileSelect.addEventListener("change", onMobileSelectChanged);
    els.refreshBtn.addEventListener("click", loadMobileNumbers);
    els.startChatBtn.addEventListener("click", startChat);

    els.searchInput.addEventListener("input", function () {
        renderList(els.searchInput.value.trim());
    });

    els.sendBtn.addEventListener("click", function (e) {
        e.preventDefault();
        sendMessage();
    });

    if (els.micBtn) {
        els.micBtn.addEventListener("click", function (e) {
            e.preventDefault();
            toggleVoiceRecording();
        });
    }

    els.input.addEventListener("input", function () {
        autosizeInput();
        updateSendButtonState();
    });

    els.input.addEventListener("keydown", function (e) {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    updateSendButtonState();
    updateMicButtonState();
    loadMobileNumbers();
}());
