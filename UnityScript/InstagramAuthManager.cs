using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Instagram OAuth Manager cho Unity
/// Hỗ trợ 3 mode: DeepLink (Mobile), Polling, WebSocket
/// 
/// Cách dùng:
///   InstagramAuthManager.Instance.Login(OnLoginSuccess, OnLoginFail);
/// </summary>
public class InstagramAuthManager : MonoBehaviour
{
    public static InstagramAuthManager Instance { get; private set; }

    [Header("Server Config")]
    [SerializeField] private string serverUrl = "https://shasta-interpenetrant-nonabstemiously.ngrok-free.dev";

    [Header("Callback Mode")]
    [SerializeField] private CallbackMode callbackMode = CallbackMode.Polling;

    [Header("Deep Link (Mobile only)")]
    [SerializeField] private string deepLinkScheme = "myunityapp";

    [Header("Polling Config")]
    [SerializeField] private float pollInterval = 2f;
    [SerializeField] private float pollTimeout = 120f;

    [Header("Saved Auth")]
    [SerializeField] private bool autoLoginOnStart = true;

    // ── Events ──────────────────────────────────────────────────
    public static event Action<InstagramUser> OnLoginSuccess;
    public static event Action<string> OnLoginFailed;
    public static event Action OnLogout;

    // ── Internal State ──────────────────────────────────────────
    private string _sessionId;
    private string _savedToken;
    private Coroutine _pollCoroutine;
    private bool _isLoginInProgress;

    // ── Constants ───────────────────────────────────────────────
    private const string TOKEN_KEY = "instagram_app_token";
    private const string USER_KEY = "instagram_user_json";

    public enum CallbackMode { DeepLink, Polling, WebSocket }

    // ── Lifecycle ───────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Đăng ký nhận deep link
        Application.deepLinkActivated += OnDeepLinkActivated;

        // Xử lý nếu app được mở từ deep link
        if (!string.IsNullOrEmpty(Application.absoluteURL))
            OnDeepLinkActivated(Application.absoluteURL);

        // Auto login nếu có token đã lưu
        if (autoLoginOnStart)
            TryAutoLogin();
    }

    void OnDestroy()
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
        if (_pollCoroutine != null) StopCoroutine(_pollCoroutine);
    }

    // ─────────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────────

    /// <summary>Bắt đầu đăng nhập Instagram</summary>
    public void Login()
    {
        if (_isLoginInProgress)
        {
            Debug.LogWarning("[Instagram] Đang trong quá trình đăng nhập.");
            return;
        }

        _isLoginInProgress = true;
        _sessionId = GenerateSessionId();

        string platform = GetPlatformString();
        string loginUrl = $"{serverUrl}/auth/instagram?session_id={_sessionId}&platform={platform}";

        Debug.Log($"[Instagram] 🔐 Bắt đầu OAuth - Session: {_sessionId}");
        Debug.Log($"[Instagram] 🌐 URL: {loginUrl}");

        // Mở browser
        Application.OpenURL(loginUrl);

        // Bắt đầu lắng nghe kết quả
        switch (callbackMode)
        {
            case CallbackMode.Polling:
                _pollCoroutine = StartCoroutine(PollForToken());
                break;
            case CallbackMode.DeepLink:
                // Kết quả đến qua OnDeepLinkActivated
                Debug.Log("[Instagram] 📱 Chờ deep link callback...");
                break;
        }
    }

    /// <summary>Đăng xuất</summary>
    public void Logout()
    {
        PlayerPrefs.DeleteKey(TOKEN_KEY);
        PlayerPrefs.DeleteKey(USER_KEY);
        PlayerPrefs.Save();
        _savedToken = null;
        _sessionId = null;
        _isLoginInProgress = false;
        Debug.Log("[Instagram] 👋 Đã đăng xuất.");
        OnLogout?.Invoke();
        StartCoroutine(CallLogoutAPI());
    }

    /// <summary>Lấy user hiện tại từ cache</summary>
    public InstagramUser GetCurrentUser()
    {
        string json = PlayerPrefs.GetString(USER_KEY, null);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonUtility.FromJson<InstagramUser>(json);
    }

    /// <summary>Lấy token hiện tại</summary>
    public string GetToken() => PlayerPrefs.GetString(TOKEN_KEY, null);

    /// <summary>Kiểm tra đã đăng nhập chưa</summary>
    public bool IsLoggedIn => !string.IsNullOrEmpty(GetToken());

    // ─────────────────────────────────────────────────────────────
    // DEEP LINK HANDLER
    // ─────────────────────────────────────────────────────────────

    private void OnDeepLinkActivated(string url)
    {
        // Format: myunityapp://oauth/instagram/callback?token=...&username=...
        if (!url.Contains("/oauth/instagram/callback")) return;

        Debug.Log($"[Instagram] 🔗 Deep link nhận được: {url.Substring(0, Mathf.Min(url.Length, 80))}...");

        try
        {
            Uri uri = new Uri(url);
            Dictionary<string, string> query = ParseQueryString(uri.Query);

            if (query.TryGetValue("token", out string token) &&
                query.TryGetValue("username", out string username) &&
                query.TryGetValue("id", out string id))
            {
                var user = new InstagramUser
                {
                    app_token = token,
                    username = username,
                    id = id,
                };

                HandleAuthSuccess(user);
            }
            else
            {
                HandleAuthFailed("Deep link thiếu thông tin cần thiết.");
            }
        }
        catch (Exception e)
        {
            HandleAuthFailed($"Lỗi parse deep link: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // POLLING
    // ─────────────────────────────────────────────────────────────

    private IEnumerator PollForToken()
    {
        float elapsed = 0;
        Debug.Log($"[Instagram] 🔄 Bắt đầu polling cho session: {_sessionId}");

        while (elapsed < pollTimeout)
        {
            yield return new WaitForSeconds(pollInterval);
            elapsed += pollInterval;

            string pollUrl = $"{serverUrl}/token/poll/{_sessionId}";
            using var request = UnityWebRequest.Get(pollUrl);
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                Debug.LogWarning($"[Instagram] Lỗi kết nối khi polling: {request.error}");
                continue;
            }

            string responseText = request.downloadHandler.text;
            var response = JsonUtility.FromJson<PollResponse>(responseText);

            if (response.status == "success" && response.data != null)
            {
                Debug.Log($"[Instagram] ✅ Polling thành công!");
                HandleAuthSuccess(response.data);
                yield break;
            }
            else if (response.status == "expired")
            {
                HandleAuthFailed("Phiên đăng nhập đã hết hạn.");
                yield break;
            }
            // status == "pending" → tiếp tục poll
        }

        HandleAuthFailed("Hết thời gian chờ đăng nhập (2 phút).");
    }

    // ─────────────────────────────────────────────────────────────
    // AUTO LOGIN
    // ─────────────────────────────────────────────────────────────

    private void TryAutoLogin()
    {
        string token = GetToken();
        if (string.IsNullOrEmpty(token)) return;

        StartCoroutine(VerifyTokenCoroutine(token));
    }

    private IEnumerator VerifyTokenCoroutine(string token)
    {
        string verifyUrl = $"{serverUrl}/token/verify";
        using var request = UnityWebRequest.Get(verifyUrl);
        request.SetRequestHeader("Authorization", $"Bearer {token}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success) yield break;

        var response = JsonUtility.FromJson<VerifyResponse>(request.downloadHandler.text);
        if (response.valid)
        {
            Debug.Log($"[Instagram] 🔑 Auto login: @{response.user.username}");
            var user = GetCurrentUser();
            if (user != null) OnLoginSuccess?.Invoke(user);
        }
        else
        {
            Debug.Log("[Instagram] Token không còn hợp lệ, xóa.");
            PlayerPrefs.DeleteKey(TOKEN_KEY);
            PlayerPrefs.DeleteKey(USER_KEY);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // INTERNAL HELPERS
    // ─────────────────────────────────────────────────────────────

    private void HandleAuthSuccess(InstagramUser user)
    {
        _isLoginInProgress = false;
        if (_pollCoroutine != null) { StopCoroutine(_pollCoroutine); _pollCoroutine = null; }

        // Lưu token vào PlayerPrefs
        PlayerPrefs.SetString(TOKEN_KEY, user.app_token);
        PlayerPrefs.SetString(USER_KEY, JsonUtility.ToJson(user));
        PlayerPrefs.Save();

        Debug.Log($"[Instagram] 🎉 Đăng nhập thành công: @{user.username}");
        OnLoginSuccess?.Invoke(user);
    }

    private void HandleAuthFailed(string error)
    {
        _isLoginInProgress = false;
        if (_pollCoroutine != null) { StopCoroutine(_pollCoroutine); _pollCoroutine = null; }

        Debug.LogError($"[Instagram] ❌ Đăng nhập thất bại: {error}");
        OnLoginFailed?.Invoke(error);
    }

    private IEnumerator CallLogoutAPI()
    {
        string token = GetToken();
        string logoutUrl = $"{serverUrl}/auth/logout";
        using var request = new UnityWebRequest(logoutUrl, "POST");
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrEmpty(token))
            request.SetRequestHeader("Authorization", $"Bearer {token}");
        request.downloadHandler = new DownloadHandlerBuffer();
        yield return request.SendWebRequest();
    }

    private string GenerateSessionId()
    {
        return $"unity_{SystemInfo.deviceUniqueIdentifier.Substring(0, 8)}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private string GetPlatformString()
    {
#if UNITY_ANDROID || UNITY_IOS
        return "mobile";
#elif UNITY_WEBGL
        return "webgl";
#else
        return "polling";
#endif
    }

    private Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(query)) return result;

        string q = query.TrimStart('?');
        foreach (string pair in q.Split('&'))
        {
            string[] parts = pair.Split('=');
            if (parts.Length == 2)
                result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
        }
        return result;
    }
}

// ─── Data Models ───────────────────────────────────────────────

[Serializable]
public class InstagramUser
{
    public string id;
    public string username;
    public string name;
    public string account_type;
    public int followers_count;
    public int media_count;
    public string app_token;
    public string token_expires_at;
}

[Serializable]
class PollResponse
{
    public string status;
    public string message;
    public InstagramUser data;
}

[Serializable]
class VerifyResponse
{
    public bool valid;
    public string error;
    public VerifyUser user;
}

[Serializable]
class VerifyUser
{
    public string instagram_id;
    public string username;
    public string expires_at;
}
