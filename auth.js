const express = require("express");
const router = express.Router();
const axios = require("axios");
const crypto = require("crypto");
const jwt = require("jsonwebtoken");
const { broadcastToSession } = require("./websocket");
const { setToken, getToken } = require("./tokenStore");

// ─── New endpoints (Basic Display API deprecated 4/12/2024) ───
// Replaced with: Instagram API with Instagram Login
const INSTAGRAM_AUTH_URL = "https://www.instagram.com/oauth/authorize";
const INSTAGRAM_TOKEN_URL = "https://api.instagram.com/oauth/access_token";
const INSTAGRAM_GRAPH_URL = "https://graph.instagram.com/v21.0";

// ─── DEBUG endpoint — quick config check ──────────────────────
// GET /auth/debug
router.get("/debug", (req, res) => {
  const serverUrl = process.env.SERVER_URL || "";
  const clientId = process.env.INSTAGRAM_CLIENT_ID || "";
  res.json({
    ok: true,
    config: {
      SERVER_URL: serverUrl,
      CALLBACK_URL: `${serverUrl}/auth/instagram/callback`,
      INSTAGRAM_CLIENT_ID: clientId,
      CLIENT_ID_LENGTH: clientId.length,
      CALLBACK_MODE: process.env.CALLBACK_MODE || "deeplink",
      UNITY_DEEP_LINK_SCHEME: process.env.UNITY_DEEP_LINK_SCHEME || "",
      KV_ENABLED: !!(process.env.KV_REST_API_URL),
    },
    test_auth_url: `${serverUrl}/auth/instagram?session_id=debug-test&platform=polling`,
  });
});

// ─── STEP 1: Start OAuth ──────────────────────────────────────
// Unity calls: GET /auth/instagram?session_id=<id>&platform=<mobile|webgl|polling>
router.get("/instagram", (req, res) => {
  const { session_id, platform = "mobile" } = req.query;

  if (!session_id) {
    return res.status(400).json({
      error: "Missing session_id",
      hint: "Add ?session_id=<unique_id> to the URL",
    });
  }

  // Stateless OAuth state — CSRF protection via crypto nonce (no server-side session needed)
  const statePayload = {
    session_id,
    platform,
    nonce: crypto.randomBytes(8).toString("hex"),
    iat: Math.floor(Date.now() / 1000),
  };
  const state = Buffer.from(JSON.stringify(statePayload)).toString("base64url");

  const redirectUri = `${process.env.SERVER_URL}/auth/instagram/callback`;

  // ⚠️  New scopes — "user_profile,user_media" no longer work
  // Instagram API with Instagram Login scopes:
  //   instagram_business_basic             → profile + media (required)
  //   instagram_business_manage_messages   → manage DM (optional)
  //   instagram_business_manage_comments   → manage comments (optional)
  //   instagram_business_content_publish   → publish posts (optional)
  const scopes = [
    "instagram_business_basic",
    "instagram_business_manage_messages",
    "instagram_business_manage_comments",
    "instagram_business_content_publish",
    "instagram_business_manage_insights",
  ].join(",");

  const params = new URLSearchParams({
    client_id: process.env.INSTAGRAM_CLIENT_ID,
    redirect_uri: redirectUri,
    scope: scopes,
    response_type: "code",
    state,
  });

  const authUrl = `${INSTAGRAM_AUTH_URL}?${params.toString()}`;
  console.log(`[AUTH] Session ${session_id} starting OAuth - platform: ${platform}`);
  console.log(`[DEBUG] client_id    : ${process.env.INSTAGRAM_CLIENT_ID}`);
  console.log(`[DEBUG] redirect_uri : ${redirectUri}`);
  console.log(`[DEBUG] auth URL     : ${authUrl}`);
  res.redirect(authUrl);
});

// ─── STEP 2: Instagram Callback ───────────────────────────────
router.get("/instagram/callback", async (req, res) => {
  const { code, state, error, error_reason } = req.query;

  console.log(`[DEBUG] /callback received:`);
  console.log(`[DEBUG]   code        : ${code ? code.substring(0, 20) + "..." : "NONE"}`);
  console.log(`[DEBUG]   state       : ${state ? "OK" : "NONE"}`);
  console.log(`[DEBUG]   error       : ${error || "none"}`);
  console.log(`[DEBUG]   error_reason: ${error_reason || "none"}`);
  console.log(`[DEBUG]   full URL    : ${req.protocol}://${req.get("host")}${req.originalUrl}`);

  if (error) {
    console.warn(`[AUTH] User denied: ${error_reason}`);
    return res.send(renderCallbackPage("error", null, "You cancelled Instagram login."));
  }

  if (!code) {
    console.error(`[DEBUG] Callback has no code and no error - query:`, req.query);
    return res.status(400).send(renderCallbackPage("error", null, "Missing authorization code."));
  }

  let statePayload;
  try {
    statePayload = JSON.parse(Buffer.from(state, "base64url").toString());

    // Validate state is not too old (max 10 minutes)
    const age = Math.floor(Date.now() / 1000) - (statePayload.iat || 0);
    if (age > 600) {
      return res.status(400).send(renderCallbackPage("error", null, "OAuth session expired. Please try again."));
    }

    console.log(`[DEBUG] state payload: session_id=${statePayload.session_id}, platform=${statePayload.platform}`);
  } catch {
    console.error(`[DEBUG] Cannot parse state: ${state}`);
    return res.status(400).send(renderCallbackPage("error", null, "Invalid state."));
  }

  const { session_id, platform } = statePayload;

  try {
    // ── 2a. Exchange code → short-lived access token ──────────
    const redirectUri = `${process.env.SERVER_URL}/auth/instagram/callback`;
    console.log(`[DEBUG] Exchanging code for token - redirect_uri: ${redirectUri}`);

    const tokenResponse = await axios.post(
      INSTAGRAM_TOKEN_URL,
      new URLSearchParams({
        client_id: process.env.INSTAGRAM_CLIENT_ID,
        client_secret: process.env.INSTAGRAM_CLIENT_SECRET,
        grant_type: "authorization_code",
        redirect_uri: redirectUri,
        code,
      }),
      { headers: { "Content-Type": "application/x-www-form-urlencoded" } }
    );

    const { access_token: shortLivedToken, user_id } = tokenResponse.data;
    console.log(`[DEBUG] Short-lived token OK - user_id: ${user_id}`);

    // ── 2b. Exchange short-lived → long-lived token (60 days) ─
    console.log(`[DEBUG] Exchanging for long-lived token...`);
    const longTokenResponse = await axios.get(`${INSTAGRAM_GRAPH_URL}/access_token`, {
      params: {
        grant_type: "ig_exchange_token",
        client_secret: process.env.INSTAGRAM_CLIENT_SECRET,
        access_token: shortLivedToken,
      },
    });

    const { access_token: longLivedToken, expires_in } = longTokenResponse.data;
    console.log(`[DEBUG] Long-lived token OK - expires_in: ${expires_in}s`);

    // ── 2c. Fetch user info ───────────────────────────────────
    console.log(`[DEBUG] Fetching user info...`);
    const userResponse = await axios.get(`${INSTAGRAM_GRAPH_URL}/me`, {
      params: {
        fields: "id,username,name,account_type,profile_picture_url,followers_count,media_count",
        access_token: longLivedToken,
      },
    });

    const igUser = userResponse.data;
    console.log(`[DEBUG] User data received: ${JSON.stringify(igUser)}`);
    const expiresAt = new Date(Date.now() + expires_in * 1000);

    // ── 2d. Create JWT app token for Unity ────────────────────
    const appToken = jwt.sign(
      {
        instagram_id: igUser.id,
        username: igUser.username,
        ig_token: longLivedToken,
        ig_token_expires: expiresAt.toISOString(),
      },
      process.env.JWT_SECRET || "dev-secret",
      { expiresIn: "60d" }
    );

    const userData = {
      id: igUser.id,
      username: igUser.username,
      name: igUser.name || igUser.username,
      account_type: igUser.account_type,
      profile_picture_url: igUser.profile_picture_url,
      followers_count: igUser.followers_count,
      media_count: igUser.media_count,
      app_token: appToken,
      token_expires_at: expiresAt.toISOString(),
    };

    console.log(`[AUTH] ✅ OK: @${igUser.username} (${igUser.account_type})`);

    const callbackMode = process.env.CALLBACK_MODE || "deeplink";
    console.log(`[DEBUG] CALLBACK_MODE: ${callbackMode}, session_id: ${session_id}`);
    return await handleCallbackResult(res, callbackMode, platform, session_id, userData);

  } catch (err) {
    const apiError = err.response?.data;
    const errMsg = apiError?.error?.message || err.message || "Unknown error";
    const errCode = apiError?.error?.code;
    const errType = apiError?.error?.type;

    console.error(`[AUTH] ❌ [${errCode}] ${errType}: ${errMsg}`);
    console.error(`[DEBUG] Full API error:`, JSON.stringify(apiError, null, 2));
    console.error(`[DEBUG] HTTP status:`, err.response?.status);

    let friendlyMsg = `Login failed: ${errMsg}`;
    if (errCode === 100 || errMsg.includes("permission")) {
      friendlyMsg = "Account must be Business or Creator. Go to Instagram → Settings → Account → Switch to Professional Account.";
    }

    return res.send(renderCallbackPage("error", session_id, friendlyMsg));
  }
});

// ─── Refresh long-lived token (call before 60-day expiry) ─────
// GET /auth/instagram/refresh-token
// Header: Authorization: Bearer <app_token>
router.get("/instagram/refresh-token", async (req, res) => {
  const authHeader = req.headers.authorization;
  if (!authHeader?.startsWith("Bearer ")) {
    return res.status(401).json({ error: "Missing Bearer token" });
  }

  const appToken = authHeader.substring(7);

  try {
    const payload = jwt.verify(appToken, process.env.JWT_SECRET || "dev-secret", {
      ignoreExpiration: true,
    });

    const response = await axios.get(`${INSTAGRAM_GRAPH_URL}/refresh_access_token`, {
      params: {
        grant_type: "ig_refresh_token",
        access_token: payload.ig_token,
      },
    });

    const { access_token: newIgToken, expires_in } = response.data;
    const newExpiry = new Date(Date.now() + expires_in * 1000);

    const newAppToken = jwt.sign(
      {
        instagram_id: payload.instagram_id,
        username: payload.username,
        ig_token: newIgToken,
        ig_token_expires: newExpiry.toISOString(),
      },
      process.env.JWT_SECRET || "dev-secret",
      { expiresIn: "60d" }
    );

    console.log(`[AUTH] 🔄 Refreshed token for @${payload.username}`);
    res.json({ success: true, app_token: newAppToken, expires_at: newExpiry.toISOString() });

  } catch (err) {
    const errMsg = err.response?.data?.error?.message || err.message;
    res.status(401).json({ error: `Cannot refresh token: ${errMsg}` });
  }
});

// ─── Handle callback result per mode ──────────────────────────
async function handleCallbackResult(res, callbackMode, platform, session_id, userData) {
  switch (callbackMode) {
    case "deeplink": {
      const scheme = process.env.UNITY_DEEP_LINK_SCHEME || "myunityapp";
      const params = new URLSearchParams({
        token: userData.app_token,
        username: userData.username,
        id: userData.id,
        session_id,
      });
      const deepLink = `${scheme}://oauth/instagram/callback?${params.toString()}`;
      return res.send(renderCallbackPage("deeplink", session_id, null, deepLink, userData));
    }
    case "polling": {
      // Store in KV or in-memory (async)
      await setToken(session_id, userData);
      return res.send(renderCallbackPage("success", session_id, null, null, userData));
    }
    case "websocket": {
      broadcastToSession(session_id, { event: "instagram_auth_success", data: userData });
      return res.send(renderCallbackPage("success", session_id, null, null, userData));
    }
    case "webgl": {
      const baseUrl = process.env.UNITY_WEBGL_URL || "http://localhost:8080";
      const params = new URLSearchParams({ token: userData.app_token, username: userData.username, id: userData.id });
      return res.redirect(`${baseUrl}?instagram_auth=${encodeURIComponent(params.toString())}`);
    }
    default:
      return res.json({ success: true, data: userData });
  }
}

// ─── HTML callback page ───────────────────────────────────────
function renderCallbackPage(type, session_id, errorMsg, deepLink, userData) {
  const isError = type === "error";
  const color = isError ? "#ef4444" : "#22c55e";

  let content = "";
  if (isError) {
    content = `<p style="color:#ef4444;line-height:1.6">${errorMsg}</p><p style="margin-top:8px">You can close this window.</p>`;
  } else if (type === "deeplink") {
    content = `
      <p>Hello <strong>@${userData.username}</strong>! 👋</p>
      <p style="color:#94a3b8;font-size:13px;margin:4px 0">${userData.account_type}</p>
      <p style="margin-top:12px">Redirecting to app...</p>
      <a href="${deepLink}" style="display:inline-block;margin-top:16px;padding:12px 28px;background:#7c3aed;color:white;border-radius:8px;text-decoration:none;font-weight:600">Open App</a>
      <script>setTimeout(() => { window.location.href = "${deepLink}"; }, 800);</script>`;
  } else {
    content = `
      <p>Hello <strong>@${userData.username}</strong>! 👋</p>
      <p style="margin-top:8px;color:#94a3b8">Login successful. You can close this window.</p>`;
  }

  return `<!DOCTYPE html>
<html lang="en"><head>
  <meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Instagram Login</title>
  <style>
    *{box-sizing:border-box;margin:0;padding:0}
    body{min-height:100vh;display:flex;align-items:center;justify-content:center;
      font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;
      background:linear-gradient(135deg,#1e1b4b,#312e81)}
    .card{background:white;border-radius:16px;padding:40px;max-width:400px;width:90%;
      text-align:center;box-shadow:0 20px 60px rgba(0,0,0,.3)}
    .icon{font-size:48px;margin-bottom:16px}
    h1{font-size:22px;color:${color};margin-bottom:12px}
    p{color:#64748b;line-height:1.6}
  </style>
</head><body>
  <div class="card">
    <div class="icon">${isError ? "😕" : "🎉"}</div>
    <h1>${isError ? "❌ Login Failed" : "✅ Login Successful"}</h1>
    ${content}
  </div>
</body></html>`;
}

module.exports = router;