// Global public/issues chat connection, initialized from _Layout.cshtml so it runs on every
// authenticated page of the site - not just Games/MyGames, where the visible chat/online-users
// widget (Pages/Shared/_ChatWidget.cshtml) happens to be rendered today. This keeps a user's
// "online" presence accurate no matter which page they're browsing.
//
// Previously each page that rendered the widget owned its own WebSockets and eagerly disconnected
// on document.visibilitychange -> "hidden" (e.g. backgrounding the tab on mobile, or switching
// tabs) to work around Django/Daphne not reliably detecting closed sockets. ASP.NET Core's
// WebSocket middleware sends its own protocol-level keep-alive pings and promptly detects
// genuinely dead connections (see Program.cs `app.UseWebSockets()`), so that workaround is no
// longer needed: connections here only close on an actual `pagehide` (tab/window closing or
// navigating to another page), so a user backgrounding the tab - or just not looking at it - keeps
// showing up as online, and the chat history/scroll position they had isn't lost either.
const TABS = ["chat", "issues"];

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
        this._connected = false;

        this.connectAll();
        window.addEventListener("pagehide", () => this.disconnectAll());
        // Restored from the back-forward cache (bfcache) with sockets already torn down by the browser.
        window.addEventListener("pageshow", e => { if (e.persisted) this.connectAll(); });
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
        this.dispatchEvent(new CustomEvent("connectedUsersUpdated", { detail: {} }));
        TABS.forEach(tab => {
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
    }

    connectRoom(tab, roomId) {
        const existing = this.websockets[tab];
        if (existing) {
            existing.onclose = null;
            existing.close();
        }
        const url = window.location;
        const wsProto = url.protocol === "http:" ? "ws:" : "wss:";
        const ws = new WebSocket(`${wsProto}//${url.host}/ws/chat/room/${roomId}`);
        this.websockets[tab] = ws;
        ws.onopen = () => {
            this.rooms[tab].wsState = 1;
            this.dispatchEvent(new CustomEvent("roomStateChanged", { detail: { tab } }));
            window.setTimeout(() => {
                if (ws.readyState === WebSocket.OPEN) {
                    ws.send(JSON.stringify({ type: "chat_retrieve", count: 20, first_message_id: null, faceless: false }));
                }
            }, 100);
        };
        ws.onclose = () => {
            this.rooms[tab].wsState = 2;
            this.dispatchEvent(new CustomEvent("roomStateChanged", { detail: { tab } }));
        };
        ws.onmessage = e => this.handleMessage(tab, JSON.parse(e.data));
    }

    handleMessage(tab, data) {
        if (tab === "chat" && data.type === "connected_users") {
            if (!this._connected) return;
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
