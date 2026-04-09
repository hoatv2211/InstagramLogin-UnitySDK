# Instagram OAuth Server + Unity Integration

[![GitHub](https://img.shields.io/badge/GitHub-hoatv2211%2FInstagramLogin--UnitySDK-181717?logo=github)](https://github.com/hoatv2211/InstagramLogin-UnitySDK)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Node.js](https://img.shields.io/badge/Node.js-18%2B-green?logo=node.js)](https://nodejs.org)
[![Vercel](https://img.shields.io/badge/Deploy-Vercel-black?logo=vercel)](https://vercel.com)

**EN** | Complete Instagram OAuth system for Unity (Mobile, WebGL, Desktop). Uses **Instagram API with Instagram Login** (Basic Display API was deprecated 4/12/2024).

**VI** | Hệ thống OAuth Instagram hoàn chỉnh cho Unity (Mobile, WebGL, Desktop). Sử dụng **Instagram API with Instagram Login** (Basic Display API đã khai tử 4/12/2024).

---

## Architecture / Kiến trúc

```
Unity App
  │
  ├─ 1. Open browser: GET /auth/instagram?session_id=xyz&platform=polling
  │     Mở trình duyệt
  │
  └─ Instagram Login Page (instagram.com/oauth/authorize)
        │
        └─ Instagram redirect: GET /auth/instagram/callback?code=...
              │
              ├─ Exchange code → short-lived token
              ├─ Exchange short-lived → long-lived token (60 days / 60 ngày)
              ├─ Fetch user info from Graph API v21.0
              ├─ Sign JWT app_token
              │
              └─ Send token to Unity (3 modes / 3 chế độ):
                   ├─ Polling    → Unity poll GET /token/poll/:session_id
                   ├─ Deep Link  → myunityapp://oauth/instagram/callback?token=...
                   └─ WebSocket  → ws://server/ws?session_id=xyz
```

---

## Server Setup / Cài đặt Server

### 1. Requirements / Yêu cầu

- Node.js 18+
- Python 3.8+ (for tests / để chạy test)

### 2. Install dependencies / Cài dependencies

```bash
npm install
```

### 3. Configure `.env` / Cấu hình `.env`

```env
INSTAGRAM_CLIENT_ID=<Instagram App ID>         # From Instagram product settings / Lấy ở Instagram product settings
INSTAGRAM_CLIENT_SECRET=<Instagram Secret>
PORT=3000
SERVER_URL=http://localhost:3000
JWT_SECRET=<random 64+ char string>
SESSION_SECRET=<random string>
CALLBACK_MODE=polling                          # polling | deeplink | websocket | webgl
UNITY_DEEP_LINK_SCHEME=myunityapp
UNITY_WEBGL_URL=http://localhost:8080
ALLOWED_ORIGINS=http://localhost:8080,http://localhost:3000
```

### 4. Run server / Chạy server

```bash
# Start the server / Khởi động server
npm start

# Run tests / Chạy test
run_test.bat
# or / hoặc
npm test
```

---

## Deploy to Vercel / Triển khai lên Vercel

> ⚡ **Vercel = static domain, zero cost.** Perfect for production OAuth callbacks.  
> ⚡ **Vercel = domain tĩnh miễn phí** cho production.

### Limitation / Lưu ý quan trọng

> [!WARNING]
> **WebSocket is NOT supported on Vercel serverless.**  
> You must use `CALLBACK_MODE=polling` when deploying to Vercel.  
> **WebSocket không hoạt động trên Vercel serverless.**  
> Bắt buộc dùng `CALLBACK_MODE=polling`.

### Step-by-step / Các bước triển khai

#### 1. Push to GitHub

```bash
git init
git add .
git commit -m "initial commit"
git remote add origin https://github.com/hoatv2211/InstagramLogin-UnitySDK.git
git push -u origin main
```

#### 2. Connect to Vercel

1. Go to [vercel.com](https://vercel.com) → **Add New Project**  
   Vào [vercel.com](https://vercel.com) → **Add New Project**
2. Import your GitHub repo / Import repo GitHub của bạn
3. Framework Preset: **Other** (not Next.js)
4. Deploy — Vercel auto-detects `vercel.json`

#### 3. Set Environment Variables / Cài đặt biến môi trường

In Vercel Dashboard → **Settings → Environment Variables**, add:  
Vào Vercel Dashboard → **Settings → Environment Variables** → thêm:

```
INSTAGRAM_CLIENT_ID        = your_instagram_app_id
INSTAGRAM_CLIENT_SECRET    = your_instagram_app_secret
JWT_SECRET                 = random_64_char_string
SESSION_SECRET             = random_string
CALLBACK_MODE              = polling
UNITY_DEEP_LINK_SCHEME     = myunityapp
SERVER_URL                 = https://your-project.vercel.app
ALLOWED_ORIGINS            = https://your-project.vercel.app
```

> 💡 `SERVER_URL` = your Vercel project URL  
> 💡 `SERVER_URL` = URL project Vercel của bạn

#### 4. Add Upstash Redis (for polling token store) / Thêm Upstash Redis

Polling mode requires persistent storage across serverless invocations.  
Polling mode cần lưu token giữa các serverless invocations.

1. Vercel Dashboard → **Integrations** → search **"Upstash"** → **Add Integration**  
   Hoặc vào [console.upstash.com](https://console.upstash.com) → Create Database → Copy REST URL & Token
2. **Connect** the Upstash database to your project  
   **Kết nối** database Upstash với project
3. Vercel auto-injects these env vars:  
   Vercel tự inject các biến:
   ```
   UPSTASH_REDIS_REST_URL
   UPSTASH_REDIS_REST_TOKEN
   ```
4. **Redeploy** the project / **Redeploy** lại project

#### 5. Update Facebook Developers / Cập nhật Facebook Developers

Replace your local URL with your Vercel URL:  
Thay URL local bằng URL Vercel:

```
https://your-project.vercel.app/auth/instagram/callback
```

#### 6. Update Unity / Cập nhật Unity

Change `serverUrl` in `InstagramAuthManager.cs` Inspector:
```
https://your-project.vercel.app
```

### Verify deployment / Kiểm tra sau khi deploy

```bash
# Health check — should show runtime: "vercel", kv: true
curl https://your-project.vercel.app/health

# Config check
curl https://your-project.vercel.app/auth/debug
```

Expected health response / Kết quả mong đợi:
```json
{
  "status": "ok",
  "mode": "polling",
  "runtime": "vercel",
  "kv": true,
  "timestamp": "..."
}
```

---

**`start.bat` auto-handles / tự động xử lý:**
- Starts ngrok with static domain / Khởi ngrok với static domain
- Updates `SERVER_URL` & `ALLOWED_ORIGINS` in `.env` / Cập nhật `.env`
- Prints Callback URL to paste into Facebook Developers / In URL callback
- Starts Express server / Khởi server

---

## Facebook Developer App Setup / Cài đặt Facebook Developer App

### 1. Create App / Tạo app

- Go to [developers.facebook.com](https://developers.facebook.com) → **My Apps** → **Create App**
- App type: **Business** / Loại app: **Business**
- Add product: **Instagram** → **API settings with Instagram Login**
- Thêm product: **Instagram** → **API settings with Instagram Login**

### 2. Get credentials / Lấy credentials

In **Instagram → API Settings with Instagram Login** page:

| Field | Source / Lấy từ đâu |
|-------|---------------------|
| `INSTAGRAM_CLIENT_ID` | **Instagram App ID** (NOT Facebook App ID / không phải Facebook App ID) |
| `INSTAGRAM_CLIENT_SECRET` | **Instagram App Secret** |

> **⚠️ Note / Lưu ý:** Facebook App ID (shown at top corner / hiển thị ở góc trên) is different from Instagram App ID. You must use the Instagram App ID. / Phải dùng Instagram App ID.

### 3. Add Website platform / Thêm platform Website

**App Settings → Basic** → scroll down → **Add Platform** → **Website**  
**Cài đặt ứng dụng → Thông tin cơ bản** → kéo xuống → **Thêm nền tảng** → **Website**

Enter your server URL (e.g., let Facebook know about your local domain). / Điền URL server vào ô **URL trang web**.

### 4. Add OAuth Redirect URI / Thêm OAuth Redirect URI

In section **3. Set up login settings** → **Login settings for business**:

```
http://localhost:3000/auth/instagram/callback
```

### 5. Add Tester (Development mode) / Thêm Tester (chế độ Development)

App in **Development** mode only allows added accounts: / App ở chế độ Development chỉ cho phép các tài khoản được thêm:

1. **App Roles → Roles** → **Add Instagram Testers** / **Vai trò → Thêm người thử nghiệm Instagram**
2. Enter Instagram username / Nhập username Instagram
3. Open Instagram app → **Settings → Apps and websites → Tester Invites → Accept**

---

## API Endpoints

| Method | Endpoint | Description / Mô tả |
|--------|----------|---------------------|
| `GET` | `/auth/instagram` | Start OAuth flow / Bắt đầu OAuth flow |
| `GET` | `/auth/instagram/callback` | Instagram redirects here / Instagram redirect về đây |
| `GET` | `/auth/debug` | Quick config check / Kiểm tra config nhanh |
| `GET` | `/token/poll/:session_id` | Unity polls for result / Unity poll kết quả |
| `GET` | `/token/verify` | Verify JWT token / Kiểm tra JWT token |
| `POST` | `/token/refresh` | Refresh token / Làm mới token |
| `POST` | `/token/logout` | Logout / Đăng xuất |
| `GET` | `/health` | Health check |
| `WS` | `/ws?session_id=xxx` | WebSocket connection |

---

## Unity Setup / Cài đặt Unity

### Step 1: Import Package / Bước 1: Import Package

**Option A (Recommended):**  
Download and import `instagramlogin.unitypackage` from the repository into your Unity project (`Assets -> Import Package -> Custom Package...`).  
Tải và import file `instagramlogin.unitypackage` vào thư mục dự án Unity của bạn.

**Option B (Manual copy):**  
Copy the `UnityScript/` folder directly into your `Assets/` directory. / Copy trực tiếp thư mục `UnityScript/` vào `Assets/`:
```
Assets/
├── Scripts/
│   ├── InstagramAuthManager.cs
│   ├── GUIManager.cs
│   └── LoginUI.cs              (example / ví dụ)
└── Editor/
    └── InstagramSceneBuilder.cs
```

### Step 2: Auto-generate scene / Bước 2: Tạo scene tự động

Menu bar → **Tools → Instagram → Build Scene**

Auto-creates / Tạo sẵn toàn bộ:
- Canvas with 4 panels (Login, Loading, Profile, Error)
- All buttons and labels with wired references / Tất cả buttons, labels đã wire references
- `InstagramAuthManager` & `GUIManager` GameObjects

### Step 3: Configure per platform / Bước 3: Cấu hình theo platform

#### Android (Deep Link)

`AndroidManifest.xml`:
```xml
<activity android:name="com.unity3d.player.UnityPlayerActivity">
  <intent-filter>
    <action android:name="android.intent.action.VIEW" />
    <category android:name="android.intent.category.DEFAULT" />
    <category android:name="android.intent.category.BROWSABLE" />
    <data android:scheme="myunityapp" android:host="oauth" />
  </intent-filter>
</activity>
```

Set `callbackMode = CallbackMode.DeepLink` in Inspector.

#### iOS (Deep Link)

**Player Settings → Other Settings → Supported URL schemes**: `myunityapp`

#### WebGL / Desktop (Polling)

Set `callbackMode = CallbackMode.Polling` in Inspector.  
Set `CALLBACK_MODE=polling` in `.env`.

---

## Usage in Unity / Sử dụng trong Unity

```csharp
// Login / Đăng nhập
InstagramAuthManager.Instance.Login();

// Listen for results / Lắng nghe kết quả
InstagramAuthManager.OnLoginSuccess += (user) => {
    Debug.Log($"Hello @{user.username}!");
    Debug.Log($"ID: {user.id}");
    Debug.Log($"Followers: {user.followers_count}");
    Debug.Log($"Token expires: {user.token_expires_at}");
};

InstagramAuthManager.OnLoginFailed += (error) => {
    Debug.LogError($"Error: {error}");
};

// Check if logged in / Kiểm tra đã login chưa
if (InstagramAuthManager.Instance.IsLoggedIn) {
    var user = InstagramAuthManager.Instance.GetCurrentUser();
    string token = InstagramAuthManager.Instance.GetToken();
    // Header: Authorization: Bearer {token}
}

// Logout / Đăng xuất
InstagramAuthManager.Instance.Logout();
```

### InstagramUser model

```csharp
public class InstagramUser {
    public string id;
    public string username;
    public string name;
    public string account_type;      // BUSINESS | MEDIA_CREATOR
    public int    followers_count;
    public int    media_count;
    public string app_token;         // JWT for server API calls
    public string token_expires_at;  // ISO 8601, ~60 days / ~60 ngày
}
```

---

## File Structure / Cấu trúc files

```
├── server.js               # Express main app / App Express chính
├── auth.js                 # OAuth flow (step 1 & 2 / bước 1 & 2)
├── token.js                # Poll / verify / refresh / logout
├── websocket.js            # WebSocket handler
├── tokenStore.js           # In-memory token store
├── start-dev.js            # Start ngrok + server (used by start.bat)
├── start.bat               # Run server + ngrok (Windows)
├── run_test.bat            # Run Python test suite (Windows)
├── test_server.py          # Test all endpoints
├── package.json            # Node.js config & scripts
├── .env                    # Config (do not commit / không commit)
│
└── UnityScript/            # Unity C# Scripts
    ├── InstagramAuthManager.cs   # OAuth manager (Singleton)
    ├── GUIManager.cs             # UI management / Quản lý UI
    ├── LoginUI.cs                # Example usage / Ví dụ sử dụng
    └── InstagramSceneBuilder.cs  # Editor: auto-build scene
```

---

## Troubleshooting / Xử lý lỗi

**"Invalid redirect_uri"**  
→ URL in Facebook Developers must exactly match `SERVER_URL/auth/instagram/callback`  
→ URL trong Facebook Developers phải khớp chính xác với `SERVER_URL/auth/instagram/callback`

**"Invalid platform app"**  
→ Haven't added **Website platform** in App Settings → Basic  
→ Chưa thêm **Website platform** trong App Settings → Thông tin cơ bản

**"Insufficient developer role"**  
→ Instagram account hasn't been added as Tester. See [Add Tester](#5-add-tester-development-mode--thêm-tester-chế-độ-development)  
→ Tài khoản Instagram chưa được thêm làm Tester

**"INSTAGRAM_CLIENT_ID wrong"**  
→ Use **Instagram App ID** (in Instagram product settings), NOT Facebook App ID  
→ Dùng **Instagram App ID**, không phải Facebook App ID

**Polling timeout**  
→ Increase `pollTimeout` in Inspector. Default 120s  
→ Tăng `pollTimeout` trong Inspector. Mặc định 120s

**CORS error (WebGL)**  
→ Add WebGL build origin to `ALLOWED_ORIGINS` in `.env`  
→ Thêm origin WebGL build vào `ALLOWED_ORIGINS` trong `.env`

**DNS_PROBE_FINISHED_NXDOMAIN ("%20" error in URL)**  
→ You have a trailing space at the end of the `serverUrl` field in Unity Inspector. Remove it.  
→ Bạn copy/paste bị dư một khoảng trắng (dấu cách) ở cuối chuỗi `Server Url` trong Unity. Xóa khoảng trắng này đi.

---

## Production Checklist

- [ ] Change `JWT_SECRET` to a random 64+ character key / Đổi thành key ngẫu nhiên 64+ ký tự
- [ ] Use a fixed domain or real server instead of ngrok / Dùng domain cố định thay ngrok
- [ ] Switch app from Development → Live on Facebook Developers / Chuyển app từ Development → Live
- [ ] Replace in-memory `pendingTokens` with Redis / Thay bằng Redis
- [ ] Add rate limiting (`express-rate-limit`)
- [ ] Encrypt `ig_token` in JWT payload / Mã hóa `ig_token` trong JWT
- [ ] Add JWT blacklist on logout / Thêm JWT blacklist khi logout

---

## License

MIT
