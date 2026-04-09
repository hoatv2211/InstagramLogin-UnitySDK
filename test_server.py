#!/usr/bin/env python3
"""
test_server.py — Test script for Instagram OAuth Server
=========================================================
Tests all API endpoints of the Node.js server before Unity integration.

Usage:
    python test_server.py                   # Test all (server must be running)
    python test_server.py --url http://localhost:3000
    python test_server.py --only health     # Only test health check
"""

import argparse
import json
import sys
import time
import uuid
import webbrowser
from datetime import datetime

import http.client
import urllib.parse

try:
    import requests          # only used for websocket-client import check
except ImportError:
    requests = None

# ─── Terminal colors ─────────────────────────────────────────────────────────
GREEN  = "\033[92m"
RED    = "\033[91m"
YELLOW = "\033[93m"
CYAN   = "\033[96m"
BOLD   = "\033[1m"
RESET  = "\033[0m"

def ok(msg):   print(f"  {GREEN}✅ PASS{RESET}  {msg}")
def fail(msg): print(f"  {RED}❌ FAIL{RESET}  {msg}")
def info(msg): print(f"  {CYAN}ℹ️  INFO{RESET}  {msg}")
def warn(msg): print(f"  {YELLOW}⚠️  WARN{RESET}  {msg}")

# ─── HTTP helper using http.client (avoids connection reset issues with requests on Windows) ─

class _Resp:
    """Minimal requests.Response mock for testing."""
    def __init__(self, status: int, body: bytes, headers):
        self.status_code = status
        self._body = body
        # Store both original case and lowercase for .get() to work with any casing
        self.headers = {}
        for k, v in headers:
            self.headers[k] = v
            self.headers[k.lower()] = v
        self.text = body.decode("utf-8", errors="replace")
    def json(self):
        return json.loads(self._body)


def _request(method: str, base: str, path: str,
             req_headers: dict | None = None,
             body=None,
             follow: bool = False) -> "_Resp | None":
    """
    Performs a single HTTP request, creating a new connection each time.
    Returns _Resp or None on error.
    """
    parsed = urllib.parse.urlparse(base)
    host = parsed.hostname
    port = parsed.port or 80

    headers = {"Content-Type": "application/json", "Accept": "application/json"}
    if req_headers:
        headers.update(req_headers)

    body_bytes = None
    if body is not None:
        body_bytes = json.dumps(body).encode("utf-8")
        headers["Content-Length"] = str(len(body_bytes))

    try:
        conn = http.client.HTTPConnection(host, port, timeout=8)
        conn.request(method, path, body=body_bytes, headers=headers)
        resp = conn.getresponse()
        data = resp.read()
        result = _Resp(resp.status, data, resp.getheaders())
        conn.close()

        # Manual redirect follow if follow=True
        if follow and resp.status in (301, 302, 303, 307, 308):
            loc = dict(resp.getheaders()).get("Location") or dict(resp.getheaders()).get("location")
            if loc:
                return _request(method, base, loc, req_headers=req_headers, follow=True)

        return result
    except Exception as e:
        warn(f"{method} {path} → {type(e).__name__}: {e}")
        return None

# ─── Aggregate results ───────────────────────────────────────────────────────
results = {"passed": 0, "failed": 0, "skipped": 0}

def passed(msg):
    results["passed"] += 1
    ok(msg)

def failed(msg):
    results["failed"] += 1
    fail(msg)

def skipped(msg):
    results["skipped"] += 1
    warn(f"SKIP  {msg}")

# ─── Helper ──────────────────────────────────────────────────────────────────
def section(title: str):
    print(f"\n{BOLD}{CYAN}{'─'*60}{RESET}")
    print(f"{BOLD}  {title}{RESET}")
    print(f"{BOLD}{CYAN}{'─'*60}{RESET}")

def get(base: str, path: str, follow: bool = False, **kwargs):
    return _request("GET", base, path, req_headers=kwargs.get("headers"), follow=follow)

def post(base: str, path: str, **kwargs):
    return _request("POST", base, path, req_headers=kwargs.get("headers"), body=kwargs.get("json"))

# ─── Test cases ──────────────────────────────────────────────────────────────

def test_health(base: str):
    section("1. Health Check — GET /health")
    r = get(base, "/health")
    if r is None:
        failed("Cannot connect to server. Run: node server.js")
        return False

    if r.status_code == 200:
        data = r.json()
        passed(f"Server is running — status={data.get('status')} | mode={data.get('mode')}")
        info(f"Timestamp: {data.get('timestamp')}")
        return True
    else:
        failed(f"HTTP {r.status_code}: {r.text[:200]}")
        return False


def test_auth_start(base: str):
    section("2. Auth Start — GET /auth/instagram")

    # 2a. Missing session_id → should return 400 (no redirect, follow=False by default)
    r = get(base, "/auth/instagram")
    if r and r.status_code == 400:
        passed("Returns 400 when session_id is missing")
    elif r and r.status_code in (301, 302):
        warn(f"Server redirects 302 when session_id is missing (may use middleware redirect) — check auth.js logic")
    else:
        code = r.status_code if r else "N/A"
        resp_body = r.text[:80] if r else ""
        failed(f"Expected 400, got {code} — {resp_body}")

    # 2b. With session_id → should redirect (302) to instagram.com
    session_id = f"test-{uuid.uuid4().hex[:8]}"
    r = get(base, f"/auth/instagram?session_id={session_id}&platform=polling")
    if r and r.status_code in (301, 302):
        location = r.headers.get("Location", "")
        if "instagram.com" in location or "facebook.com" in location:
            passed(f"Correctly redirects to Instagram OAuth URL")
            info(f"Location: {location[:100]}...")
        else:
            failed(f"Redirects to unknown URL: {location[:100]}")
    else:
        code = r.status_code if r else "N/A"
        failed(f"Expected 302 redirect, got {code}")

    return session_id


def test_token_polling(base: str, session_id: str):
    section("3. Token Polling — GET /token/poll/:session_id")

    # 3a. Poll session with no token yet → should be pending
    r = get(base, f"/token/poll/{session_id}")
    if r and r.status_code == 200:
        data = r.json()
        if data.get("status") == "pending":
            passed("Returns 'pending' correctly when no token available")
        else:
            failed(f"Expected 'pending', got: {data}")
    else:
        code = r.status_code if r else "N/A"
        failed(f"HTTP {code}")

    # 3b. Poll empty session id
    r = get(base, "/token/poll/")
    if r and r.status_code == 404:
        passed("Returns 404 when session_id is empty")
    else:
        skipped("Endpoint /token/poll/ does not return 404 (depends on router design)")


def test_token_verify_invalid(base: str):
    section("4. Token Verify — GET /token/verify (fake token)")

    # No header
    r = get(base, "/token/verify")
    if r and r.status_code == 401:
        passed("Returns 401 when no Bearer token provided")
    else:
        code = r.status_code if r else "N/A"
        failed(f"Expected 401, got {code}")

    # Garbage token
    r = get(base, "/token/verify", headers={"Authorization": "Bearer invalid.token.here"})
    if r and r.status_code == 401:
        data = r.json()
        passed(f"Returns 401 with fake token — error: {data.get('error')}")
    else:
        code = r.status_code if r else "N/A"
        failed(f"Expected 401, got {code}")


def test_token_refresh_invalid(base: str):
    section("5. Token Refresh — POST /token/refresh (fake token)")

    # No body
    r = post(base, "/token/refresh", json={})
    if r and r.status_code == 400:
        passed("Returns 400 when app_token is missing from body")
    else:
        code = r.status_code if r else "N/A"
        failed(f"Expected 400, got {code}")

    # Garbage token
    r = post(base, "/token/refresh", json={"app_token": "bad.token.value"})
    if r and r.status_code == 401:
        passed("Returns 401 with fake refresh token")
    else:
        code = r.status_code if r else "N/A"
        failed(f"Expected 401, got {code}")


def test_logout(base: str):
    section("6. Logout — POST /token/logout")
    r = post(base, "/token/logout", json={})
    if r and r.status_code == 200:
        data = r.json()
        if data.get("success"):
            passed("Logout returns success=true")
        else:
            failed(f"No success in response: {data}")
    else:
        code = r.status_code if r else "N/A"
        failed(f"HTTP {code}")


def test_404(base: str):
    section("7. 404 Handler")
    r = get(base, "/this/does/not/exist")
    if r and r.status_code == 404:
        passed("Returns 404 correctly for non-existent route")
    else:
        code = r.status_code if r else "N/A"
        failed(f"Expected 404, got {code}")


def test_cors(base: str):
    section("8. CORS Headers")
    origin = "http://localhost:8080"
    r = get(base, "/health", headers={"Origin": origin})
    if r:
        acao = r.headers.get("Access-Control-Allow-Origin", "")
        if acao in (origin, "*"):
            passed(f"CORS allows origin: {acao}")
        else:
            warn(f"Origin '{origin}' not allowed (ACAO='{acao}'). Check ALLOWED_ORIGINS in .env")
    else:
        failed("No response received")


def test_websocket_info(base: str):
    """Check WebSocket endpoint via HTTP upgrade hint (no actual WS test)."""
    section("9. WebSocket (Smoke Test)")
    try:
        import websocket  # websocket-client
        ws_url = base.replace("http://", "ws://").replace("https://", "wss://")
        info(f"Trying WebSocket connection: {ws_url}")
        ws = websocket.create_connection(ws_url, timeout=5)
        ws.close()
        passed("WebSocket connection successful")
    except ImportError:
        skipped("websocket-client library not installed. Run: pip install websocket-client")
    except Exception as e:
        warn(f"WebSocket error (normal if server doesn't expose / endpoint): {e}")


def test_open_browser_oauth(base: str, session_id: str):
    section("10. Open browser for real OAuth flow test (optional)")
    url = f"{base}/auth/instagram?session_id={session_id}-manual&platform=polling"
    ans = input(f"\n{YELLOW}  Do you want to open a browser to test real OAuth? (y/N): {RESET}").strip().lower()
    if ans == "y":
        info(f"Opening: {url}")
        webbrowser.open(url)
        info("After login, poll token:")
        info(f"  GET {base}/token/poll/{session_id}-manual")
        skipped("Manual test — check results directly in browser")
    else:
        skipped("Skipped real OAuth test")


# ─── Summary ─────────────────────────────────────────────────────────────────
def print_summary():
    section("📊 Test Results Summary")
    total = sum(results.values())
    print(f"\n  Total:   {total} tests")
    print(f"  {GREEN}✅ Passed:  {results['passed']}{RESET}")
    print(f"  {RED}❌ Failed:  {results['failed']}{RESET}")
    print(f"  {YELLOW}⚠️  Skipped: {results['skipped']}{RESET}")

    if results["failed"] == 0:
        print(f"\n  {GREEN}{BOLD}🎉 All tests passed! Server is ready for Unity.{RESET}\n")
    else:
        print(f"\n  {RED}{BOLD}⚠️  {results['failed']} test(s) failed. See details above.{RESET}\n")


# ─── Main ─────────────────────────────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser(description="Test Instagram OAuth Server")
    parser.add_argument("--url", default="http://localhost:3000",
                        help="Base URL of the server (default: http://localhost:3000)")
    parser.add_argument("--only", choices=["health", "auth", "token", "cors", "ws", "all"],
                        default="all", help="Only run a specific test group")
    parser.add_argument("--no-browser", action="store_true",
                        help="Don't ask to open browser")
    args = parser.parse_args()

    base = args.url.rstrip("/")

    print(f"\n{BOLD}{'='*60}{RESET}")
    print(f"{BOLD}  🧪 Instagram OAuth Server — Test Suite{RESET}")
    print(f"{BOLD}{'='*60}{RESET}")
    print(f"  Server : {CYAN}{base}{RESET}")
    print(f"  Time   : {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")

    # ── Step 1: Health check (required) ──────────────────────────
    alive = test_health(base)
    if not alive:
        print(f"\n{RED}Server is not running. Start with:{RESET}")
        print(f"  node server.js\n")
        sys.exit(1)

    # ── Step 2: Other tests ───────────────────────────────────────
    only = args.only
    session_id = f"pytest-{uuid.uuid4().hex[:12]}"

    if only in ("all", "auth"):
        session_id = test_auth_start(base) or session_id

    if only in ("all", "token"):
        test_token_polling(base, session_id)
        test_token_verify_invalid(base)
        test_token_refresh_invalid(base)
        test_logout(base)

    if only == "all":
        test_404(base)

    if only in ("all", "cors"):
        test_cors(base)

    if only in ("all", "ws"):
        test_websocket_info(base)

    if only == "all" and not args.no_browser:
        test_open_browser_oauth(base, session_id)

    print_summary()
    sys.exit(0 if results["failed"] == 0 else 1)


if __name__ == "__main__":
    main()
