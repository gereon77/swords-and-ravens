// Global public/issues chat connection, initialized from _Layout.cshtml so it runs on every
// authenticated page of the site - not just Games/MyGames, where the visible chat/online-users
// widget (Pages/Shared/_ChatWidget.cshtml) happens to be rendered today. This keeps a user's
// "online" presence accurate no matter which page they're browsing.
//
// Previously each page that rendered the widget owned its own WebSockets and eagerly disconnected
// on document.visibilitychange -> "hidden" (e.g. backgrounding the tab on mobile, or switching
// tabs) to work around Django/Daphne not reliably detecting closed sockets. ASP.NET Core's
// The global connection sends an application heartbeat while the page remains open and retries
// unexpected socket closures. Intentional pagehide shutdown still closes cleanly, while merely
// backgrounding the tab keeps the user online without relying on chat-message activity.
const TABS = ["chat", "issues"];
const PRESENCE_HEARTBEAT_INTERVAL_MS = 5 * 60 * 1000;
const MAX_RECONNECT_DELAY_MS = 30 * 1000;

function emptyRoomState() {
    return { wsState: 0, messages: [], noMoreMessages: false, lastViewedMessageId: null };
}

class ChatPresence extends EventTarget {
    constructor(roomIds) {
        super();
        this.roomIds = roomIds; // { chat: <guid>, issues: <guid> }
        this.connectedUsers = {};
        this.rooms = { chat: emptyRoomState(), issues: emptyRoomState() };
        this.websockets = {};
        this.retryAttempts = { chat: 0, issues: 0 };
        this.retryTimers = { chat: null, issues: null };
        this.heartbeatTimer = null;
        this.presenceVersion = -1;
        this._connected = false;

        this.connectAll();
        window.addEventListener("pagehide", () => this.disconnectAll());
        // Restored from the back-forward cache (bfcache) with sockets already torn down by the browser.
        window.addEventListener("pageshow", e => { if (e.persisted) this.connectAll(); });
        document.addEventListener("visibilitychange", () => {
            if (document.visibilityState === "visible") {
                this.sendPresenceHeartbeat();
            }
        });
    }

    connectAll() {
        if (this._connected) return;
        this._connected = true;
        TABS.forEach(tab => {
            if (this.roomIds[tab]) {
                this.rooms[tab] = emptyRoomState();
                this.connectRoom(tab, this.roomIds[tab]);
            }
        });
    }

    disconnectAll() {
        if (!this._connected) return;
        this._connected = false;
        this.connectedUsers = {};
        this.presenceVersion = -1;
        this.dispatchEvent(new CustomEvent("connectedUsersUpdated", { detail: {} }));
        TABS.forEach(tab => {
            this.clearRetryTimer(tab);
            const ws = this.websockets[tab];
            if (ws) {
                ws.onclose = null;
                ws.onmessage = null;
                ws.close();
                this.websockets[tab] = null;
            }
            this.rooms[tab].wsState = 0;
            this.dispatchEvent(new CustomEvent("roomStateChanged", { detail: { tab } }));
        });
        this.clearHeartbeatTimer();
    }

    connectRoom(tab, roomId) {
        this.clearRetryTimer(tab);
        const existing = this.websockets[tab];
        if (existing) {
            existing.onclose = null;
            existing.onmessage = null;
            existing.close();
        }
        this.rooms[tab] = emptyRoomState();
        if (tab === "chat") {
            this.presenceVersion = -1;
        }
        this.dispatchEvent(new CustomEvent("roomStateChanged", { detail: { tab } }));

        const url = window.location;
        const wsProto = url.protocol === "http:" ? "ws:" : "wss:";
        const ws = new WebSocket(`${wsProto}//${url.host}/ws/chat/room/${roomId}`);
        this.websockets[tab] = ws;
        ws.onopen = () => {
            if (this.websockets[tab] !== ws || !this._connected) return;
            this.retryAttempts[tab] = 0;
            this.rooms[tab].wsState = 1;
            this.dispatchEvent(new CustomEvent("roomStateChanged", { detail: { tab } }));
            if (tab === "chat") {
                this.sendPresenceHeartbeat();
                this.schedulePresenceHeartbeat(ws);
            }
            window.setTimeout(() => {
                if (this.websockets[tab] === ws && ws.readyState === WebSocket.OPEN) {
                    ws.send(JSON.stringify({ type: "chat_retrieve", count: 20, first_message_id: null, faceless: false }));
                }
            }, 100);
        };
        ws.onclose = () => {
            if (this.websockets[tab] !== ws) return;
            this.websockets[tab] = null;
            if (tab === "chat") {
                this.clearHeartbeatTimer();
            }
            this.rooms[tab].wsState = 2;
            this.dispatchEvent(new CustomEvent("roomStateChanged", { detail: { tab } }));
            if (this._connected) {
                this.scheduleReconnect(tab);
            }
        };
        ws.onmessage = e => {
            if (this.websockets[tab] === ws && this._connected) {
                this.handleMessage(tab, JSON.parse(e.data));
            }
        };
    }

    scheduleReconnect(tab) {
        if (this.retryTimers[tab] || !this._connected) return;
        const attempt = this.retryAttempts[tab]++;
        const baseDelay = Math.min(1000 * (2 ** attempt), MAX_RECONNECT_DELAY_MS);
        const jitteredDelay = baseDelay * (0.75 + Math.random() * 0.5);
        this.retryTimers[tab] = window.setTimeout(() => {
            this.retryTimers[tab] = null;
            if (this._connected && !this.websockets[tab]) {
                this.connectRoom(tab, this.roomIds[tab]);
            }
        }, jitteredDelay);
    }

    clearRetryTimer(tab) {
        if (this.retryTimers[tab]) {
            window.clearTimeout(this.retryTimers[tab]);
            this.retryTimers[tab] = null;
        }
    }

    sendPresenceHeartbeat() {
        const ws = this.websockets.chat;
        if (this._connected && ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify({ type: "presence_heartbeat" }));
        }
    }

    schedulePresenceHeartbeat(ws) {
        this.clearHeartbeatTimer();
        const jitteredDelay = PRESENCE_HEARTBEAT_INTERVAL_MS * (0.9 + Math.random() * 0.2);
        this.heartbeatTimer = window.setTimeout(() => {
            if (
                this._connected &&
                this.websockets.chat === ws &&
                ws.readyState === WebSocket.OPEN
            ) {
                this.sendPresenceHeartbeat();
                this.schedulePresenceHeartbeat(ws);
            }
        }, jitteredDelay);
    }

    clearHeartbeatTimer() {
        if (this.heartbeatTimer) {
            window.clearTimeout(this.heartbeatTimer);
            this.heartbeatTimer = null;
        }
    }

    handleMessage(tab, data) {
        if (tab === "chat" && data.type === "connected_users") {
            if (!this._connected) return;
            if (data.version < this.presenceVersion) return;
            this.presenceVersion = data.version;
            this.connectedUsers = data.users;
            this.dispatchEvent(new CustomEvent("connectedUsersUpdated", { detail: data.users }));
            return;
        }
        if (data.type === "force_disconnect") {
            // Server pruned this connection as stale; reconnect immediately - the disconnect
            // hack this replaces was only ever needed for sockets the server didn't know were
            // dead, not for ones it's explicitly telling us to close.
            this.disconnectAll();
            this.connectAll();
            return;
        }
        if (data.type === "chat_message") {
            this.addMessage(tab, data, false);
            this.dispatchEvent(new CustomEvent("chatMessage", { detail: { tab, message: data } }));
        } else if (data.type === "chat_messages_retrieved") {
            data.messages.forEach(d => this.addMessage(tab, d, false));
            this.rooms[tab].lastViewedMessageId = data.last_viewed_message ?? null;
            this.dispatchEvent(new CustomEvent("chatMessagesRetrieved", { detail: { tab } }));
        } else if (data.type === "more_chat_messages_retrieved") {
            if (data.messages.length === 0) {
                this.rooms[tab].noMoreMessages = true;
            } else {
                data.messages.forEach(d => this.addMessage(tab, d, true));
            }
            this.dispatchEvent(new CustomEvent("moreChatMessagesRetrieved", { detail: { tab } }));
        }
    }

    addMessage(tab, data, prepend) {
        const msg = {
            id: data.id,
            username: data.user_username,
            user_id: data.user_id,
            text: data.text,
            created_at: new Date(Date.parse(data.created_at))
        };
        const msgs = this.rooms[tab].messages;
        this.rooms[tab].messages = prepend ? [msg, ...msgs] : [...msgs, msg];
    }

    sendChatMessage(tab, text) {
        const ws = this.websockets[tab];
        if (ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify({ type: "chat_message", text, faceless: false }));
        }
    }

    sendViewMessage(tab, messageId) {
        const ws = this.websockets[tab];
        if (ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify({ type: "chat_view_message", message_id: messageId }));
        }
        this.rooms[tab].lastViewedMessageId = messageId;
    }

    loadMore(tab) {
        const ws = this.websockets[tab];
        const room = this.rooms[tab];
        if (!ws || ws.readyState !== WebSocket.OPEN || room.messages.length === 0) {
            return false;
        }
        ws.send(JSON.stringify({ type: "chat_retrieve", count: 50, first_message_id: room.messages[0].id, faceless: false }));
        return true;
    }
}

/** Idempotent: safe to call from multiple script tags/partials on the same page. */
export function initChatPresence(roomIds) {
    if (!window.SnrChatPresence) {
        window.SnrChatPresence = new ChatPresence(roomIds);
    }
    return window.SnrChatPresence;
}
