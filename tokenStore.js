// tokenStore.js
// Dual-backend token store:
//   - Upstash Redis when UPSTASH_REDIS_REST_URL is set (production on Vercel)
//   - In-memory Map as fallback (local development)

const TTL_SECONDS = 10 * 60; // 10 minutes

// ─── Try to load Upstash Redis ───────────────────────────────
let kv = null;
if (process.env.UPSTASH_REDIS_REST_URL && process.env.UPSTASH_REDIS_REST_TOKEN) {
  try {
    const { Redis } = require("@upstash/redis");
    kv = new Redis({
      url: process.env.UPSTASH_REDIS_REST_URL,
      token: process.env.UPSTASH_REDIS_REST_TOKEN,
    });
    console.log("[STORE] Using Upstash Redis as token store");
  } catch {
    console.warn("[STORE] @upstash/redis not installed, falling back to in-memory store");
  }
}

// ─── In-memory fallback (local dev only) ─────────────────────
const localMap = new Map();

// Auto-cleanup expired tokens every 5 minutes (local only)
if (!kv) {
  setInterval(() => {
    const now = Date.now();
    let cleaned = 0;
    for (const [key, value] of localMap.entries()) {
      if (now - value.created_at > TTL_SECONDS * 1000) {
        localMap.delete(key);
        cleaned++;
      }
    }
    if (cleaned > 0) {
      console.log(`[STORE] 🧹 Cleaned ${cleaned} expired session(s)`);
    }
  }, 5 * 60 * 1000);
}

// ─── Public async API ─────────────────────────────────────────

async function setToken(session_id, data) {
  if (kv) {
    // Upstash Redis: set with EX (seconds TTL)
    await kv.set(`ig:${session_id}`, { ...data, created_at: Date.now() }, {
      ex: TTL_SECONDS,
    });
  } else {
    localMap.set(session_id, { ...data, created_at: Date.now() });
  }
}

async function getToken(session_id) {
  if (kv) {
    // Upstash Redis returns parsed JSON automatically
    return await kv.get(`ig:${session_id}`);
  } else {
    return localMap.get(session_id) || null;
  }
}

async function deleteToken(session_id) {
  if (kv) {
    await kv.del(`ig:${session_id}`);
  } else {
    localMap.delete(session_id);
  }
}

module.exports = { setToken, getToken, deleteToken };
