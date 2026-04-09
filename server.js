require("dotenv").config();
const express = require("express");
const cors = require("cors");
const { createServer } = require("http");
const authRoutes = require("./auth");
const tokenRoutes = require("./token");

const app = express();

// ─── Middleware ────────────────────────────────────────────────
app.use(express.json());
app.use(express.urlencoded({ extended: true }));

// CORS — allow Unity WebGL and dev tools
const allowedOrigins = (process.env.ALLOWED_ORIGINS || "").split(",").filter(Boolean);
app.use(
  cors({
    origin: (origin, callback) => {
      // Allow requests without origin (mobile app, Postman)
      if (!origin) return callback(null, true);
      if (allowedOrigins.includes(origin)) return callback(null, true);
      callback(new Error(`CORS blocked: ${origin}`));
    },
    credentials: true,
  })
);

// ─── WebSocket (only in non-Vercel / local environments) ──────
// WebSocket is NOT supported on Vercel serverless.
// Use CALLBACK_MODE=polling when deploying to Vercel.
let wss = null;
if (process.env.VERCEL !== "1") {
  const { setupWebSocket } = require("./websocket");
  const server = createServer(app);
  wss = setupWebSocket(server);
  app.set("wss", wss);

  // Start HTTP server locally
  const PORT = process.env.PORT || 3000;
  server.listen(PORT, () => {
    console.log(`\n🚀 Instagram OAuth Server running on port ${PORT}`);
    console.log(`📋 Callback mode: ${process.env.CALLBACK_MODE || "deeplink"}`);
    console.log(`🔗 Server URL: ${process.env.SERVER_URL || `http://localhost:${PORT}`}`);
    console.log(`\n📌 Instagram Redirect URI (add to Facebook Developers):`);
    console.log(`   ${process.env.SERVER_URL || `http://localhost:${PORT}`}/auth/instagram/callback\n`);
  });
}

// ─── Routes ───────────────────────────────────────────────────
app.use("/auth", authRoutes);
app.use("/token", tokenRoutes);

// Health check
app.get("/health", (req, res) => {
  res.json({
    status: "ok",
    mode: process.env.CALLBACK_MODE || "deeplink",
    runtime: process.env.VERCEL === "1" ? "vercel" : "node",
    kv: !!(process.env.KV_REST_API_URL),
    timestamp: new Date().toISOString(),
  });
});

// 404 handler
app.use((req, res) => {
  res.status(404).json({ error: "Not found" });
});

// Error handler
app.use((err, req, res, next) => {
  console.error("[ERROR]", err.message);
  res.status(500).json({ error: err.message || "Internal server error" });
});

// ─── Export for Vercel serverless ─────────────────────────────
module.exports = app;
