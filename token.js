const express = require("express");
const router = express.Router();
const jwt = require("jsonwebtoken");
const { getToken, deleteToken } = require("./tokenStore");

// ─── Polling Endpoint ─────────────────────────────────────────
// Unity calls every 2 seconds: GET /token/poll/:session_id
// Returns token if available, or { pending: true } if not ready
router.get("/poll/:session_id", async (req, res) => {
  const { session_id } = req.params;

  const tokenData = await getToken(session_id);

  if (!tokenData) {
    return res.json({ status: "pending", message: "No result yet, please retry." });
  }

  // Check timeout (10 minutes) — only relevant for in-memory fallback; KV handles TTL natively
  const age = Date.now() - tokenData.created_at;
  if (age > 10 * 60 * 1000) {
    await deleteToken(session_id);
    return res.status(410).json({ status: "expired", message: "Login session has expired." });
  }

  // Return token and remove from store (one-time use)
  await deleteToken(session_id);
  console.log(`[TOKEN] ✅ Polling succeeded for session: ${session_id}`);

  return res.json({
    status: "success",
    data: tokenData,
  });
});

// ─── Verify Token ─────────────────────────────────────────────
// Unity calls to check if JWT token is still valid
// GET /token/verify
// Header: Authorization: Bearer <app_token>
router.get("/verify", (req, res) => {
  const authHeader = req.headers.authorization;
  if (!authHeader?.startsWith("Bearer ")) {
    return res.status(401).json({ valid: false, error: "Missing Bearer token" });
  }

  const token = authHeader.substring(7);

  try {
    const payload = jwt.verify(token, process.env.JWT_SECRET || "dev-secret");
    return res.json({
      valid: true,
      user: {
        instagram_id: payload.instagram_id,
        username: payload.username,
        expires_at: new Date(payload.exp * 1000).toISOString(),
      },
    });
  } catch (err) {
    return res.status(401).json({
      valid: false,
      error: err.name === "TokenExpiredError" ? "Token has expired" : "Invalid token",
    });
  }
});

// ─── Refresh Token ────────────────────────────────────────────
// POST /token/refresh
// Body: { app_token: "..." }
router.post("/refresh", (req, res) => {
  const { app_token } = req.body;
  if (!app_token) {
    return res.status(400).json({ error: "Missing app_token" });
  }

  try {
    // Verify old token (allow tokens expired less than 1 day)
    const payload = jwt.verify(app_token, process.env.JWT_SECRET || "dev-secret", {
      ignoreExpiration: true,
    });

    // Check not too old (max 1 day after expiry)
    const now = Math.floor(Date.now() / 1000);
    if (now - payload.exp > 86400) {
      return res.status(401).json({ error: "Token too old, please login again." });
    }

    // Create new token
    const newToken = jwt.sign(
      {
        instagram_id: payload.instagram_id,
        username: payload.username,
        access_token: payload.access_token,
      },
      process.env.JWT_SECRET || "dev-secret",
      { expiresIn: "7d" }
    );

    return res.json({ success: true, app_token: newToken });
  } catch (err) {
    return res.status(401).json({ error: "Invalid token." });
  }
});

// ─── Logout ───────────────────────────────────────────────────
// POST /token/logout
router.post("/logout", (req, res) => {
  // With stateless JWT, logout only needs to delete token on client
  // If blacklist is needed, add Redis/DB here
  res.json({ success: true, message: "Logged out. Delete token on client side." });
});

module.exports = router;
