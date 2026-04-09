const { WebSocketServer } = require("ws");

// Map: session_id → WebSocket connection
const sessionConnections = new Map();

function setupWebSocket(server) {
  const wss = new WebSocketServer({ server, path: "/ws" });

  wss.on("connection", (ws, req) => {
    const url = new URL(req.url, "ws://localhost");
    const session_id = url.searchParams.get("session_id");

    if (!session_id) {
      ws.close(4000, "Missing session_id");
      return;
    }

    console.log(`[WS] ✅ New connection: session ${session_id}`);
    sessionConnections.set(session_id, ws);

    // Ping every 30 seconds to keep connection alive
    const pingInterval = setInterval(() => {
      if (ws.readyState === ws.OPEN) {
        ws.ping();
      } else {
        clearInterval(pingInterval);
      }
    }, 30000);

    ws.on("message", (data) => {
      try {
        const msg = JSON.parse(data.toString());
        if (msg.type === "ping") {
          ws.send(JSON.stringify({ type: "pong" }));
        }
      } catch {}
    });

    ws.on("close", () => {
      console.log(`[WS] Disconnected: session ${session_id}`);
      sessionConnections.delete(session_id);
      clearInterval(pingInterval);
    });

    ws.on("error", (err) => {
      console.error(`[WS] Error session ${session_id}:`, err.message);
    });

    // Notify client of successful connection
    ws.send(JSON.stringify({ type: "connected", session_id }));
  });

  return wss;
}

function broadcastToSession(session_id, payload) {
  const ws = sessionConnections.get(session_id);
  if (!ws || ws.readyState !== ws.OPEN) {
    console.warn(`[WS] No connection found for session: ${session_id}`);
    return false;
  }

  ws.send(JSON.stringify(payload));
  console.log(`[WS] 📤 Sent to session ${session_id}`);

  // Close after sending (one-time use)
  setTimeout(() => ws.close(1000, "Auth complete"), 2000);
  return true;
}

module.exports = { setupWebSocket, broadcastToSession };
