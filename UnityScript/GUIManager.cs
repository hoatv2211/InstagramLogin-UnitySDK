using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// GUIManager — quản lý toàn bộ UI cho Instagram OAuth
///
/// Setup:
///   1. Gắn script này vào 1 GameObject (ví dụ: "GUIManager")
///   2. Tạo Canvas với 3 panel: LoginPanel, LoadingPanel, ProfilePanel
///   3. Gắn các UI references trong Inspector
///
/// Dependency: InstagramAuthManager phải tồn tại trong cùng scene
/// </summary>
public class GUIManager : MonoBehaviour
{
    public static GUIManager Instance { get; private set; }

    // ─── Panels ───────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private GameObject profilePanel;
    [SerializeField] private GameObject errorPanel;

    // ─── Login Panel ──────────────────────────────────────────────
    [Header("Login Panel")]
    [SerializeField] private Button loginButton;
    [SerializeField] private TextMeshProUGUI loginStatusText;

    // ─── Loading Panel ────────────────────────────────────────────
    [Header("Loading Panel")]
    [SerializeField] private TextMeshProUGUI loadingText;
    [SerializeField] private Image loadingSpinner;

    // ─── Profile Panel ────────────────────────────────────────────
    [Header("Profile Panel")]
    [SerializeField] private TextMeshProUGUI usernameText;
    [SerializeField] private TextMeshProUGUI accountTypeText;
    [SerializeField] private TextMeshProUGUI followersText;
    [SerializeField] private TextMeshProUGUI mediaCountText;
    [SerializeField] private TextMeshProUGUI tokenExpiryText;
    [SerializeField] private Button logoutButton;

    // ─── Error Panel ──────────────────────────────────────────────
    [Header("Error Panel")]
    [SerializeField] private TextMeshProUGUI errorText;
    [SerializeField] private Button retryButton;

    // ─── Settings ─────────────────────────────────────────────────
    [Header("Settings")]
    [SerializeField] private float spinnerSpeed = 180f;

    // ─── State ────────────────────────────────────────────────────
    public enum UIState { Login, Loading, Profile, Error }
    private UIState _currentState;
    private Coroutine _loadingDotsCoroutine;

    // ─── Lifecycle ────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        InstagramAuthManager.OnLoginSuccess += HandleLoginSuccess;
        InstagramAuthManager.OnLoginFailed  += HandleLoginFailed;
        InstagramAuthManager.OnLogout       += HandleLogout;
    }

    void OnDisable()
    {
        InstagramAuthManager.OnLoginSuccess -= HandleLoginSuccess;
        InstagramAuthManager.OnLoginFailed  -= HandleLoginFailed;
        InstagramAuthManager.OnLogout       -= HandleLogout;
    }

    void Start()
    {
        loginButton?.onClick.AddListener(OnLoginClicked);
        logoutButton?.onClick.AddListener(OnLogoutClicked);
        retryButton?.onClick.AddListener(OnRetryClicked);

        // Hiển thị đúng state ban đầu
        if (InstagramAuthManager.Instance != null && InstagramAuthManager.Instance.IsLoggedIn)
        {
            var user = InstagramAuthManager.Instance.GetCurrentUser();
            if (user != null)
                ShowProfile(user);
            else
                SetState(UIState.Login);
        }
        else
        {
            SetState(UIState.Login);
        }
    }

    void Update()
    {
        // Quay spinner khi đang loading
        if (_currentState == UIState.Loading && loadingSpinner != null)
            loadingSpinner.transform.Rotate(0f, 0f, -spinnerSpeed * Time.deltaTime);
    }

    // ─── Public API ───────────────────────────────────────────────

    public void SetState(UIState state)
    {
        _currentState = state;

        loginPanel?.SetActive(state == UIState.Login);
        loadingPanel?.SetActive(state == UIState.Loading);
        profilePanel?.SetActive(state == UIState.Profile);
        errorPanel?.SetActive(state == UIState.Error);

        if (state != UIState.Loading && _loadingDotsCoroutine != null)
        {
            StopCoroutine(_loadingDotsCoroutine);
            _loadingDotsCoroutine = null;
        }
    }

    public void ShowError(string message)
    {
        SetState(UIState.Error);
        if (errorText != null)
            errorText.text = message;
        Debug.LogError($"[GUIManager] {message}");
    }

    public void ShowLoading(string message = "Đang đăng nhập...")
    {
        SetState(UIState.Loading);
        if (loadingText != null)
        {
            _loadingDotsCoroutine = StartCoroutine(AnimateLoadingDots(message));
        }
    }

    // ─── Button Handlers ──────────────────────────────────────────

    void OnLoginClicked()
    {
        if (InstagramAuthManager.Instance == null)
        {
            ShowError("InstagramAuthManager chưa được khởi tạo.");
            return;
        }

        ShowLoading("Đang mở Instagram");
        InstagramAuthManager.Instance.Login();
    }

    void OnLogoutClicked()
    {
        InstagramAuthManager.Instance?.Logout();
    }

    void OnRetryClicked()
    {
        SetState(UIState.Login);
        if (loginStatusText != null)
            loginStatusText.text = "";
    }

    // ─── Auth Event Handlers ──────────────────────────────────────

    void HandleLoginSuccess(InstagramUser user)
    {
        ShowProfile(user);
    }

    void HandleLoginFailed(string error)
    {
        ShowError($"Đăng nhập thất bại:\n{error}");
    }

    void HandleLogout()
    {
        SetState(UIState.Login);
        if (loginStatusText != null)
            loginStatusText.text = "Đã đăng xuất.";
    }

    // ─── UI Helpers ───────────────────────────────────────────────

    void ShowProfile(InstagramUser user)
    {
        SetState(UIState.Profile);

        if (usernameText != null)
            usernameText.text = $"@{user.username}";

        if (accountTypeText != null)
            accountTypeText.text = user.account_type switch
            {
                "BUSINESS"      => "Business",
                "MEDIA_CREATOR" => "Creator",
                _               => user.account_type
            };

        if (followersText != null)
            followersText.text = FormatNumber(user.followers_count);

        if (mediaCountText != null)
            mediaCountText.text = user.media_count.ToString();

        if (tokenExpiryText != null && !string.IsNullOrEmpty(user.token_expires_at))
        {
            if (System.DateTime.TryParse(user.token_expires_at, out var expiry))
            {
                int daysLeft = (int)(expiry - System.DateTime.UtcNow).TotalDays;
                tokenExpiryText.text = $"Token còn {daysLeft} ngày";
            }
        }

        Debug.Log($"[GUIManager] Hiển thị profile: @{user.username} ({user.account_type})");
    }

    IEnumerator AnimateLoadingDots(string baseText)
    {
        string[] dots = { "", ".", "..", "..." };
        int i = 0;
        while (true)
        {
            if (loadingText != null)
                loadingText.text = baseText + dots[i % dots.Length];
            i++;
            yield return new WaitForSeconds(0.4f);
        }
    }

    static string FormatNumber(int n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000f:0.#}M";
        if (n >= 1_000)     return $"{n / 1_000f:0.#}K";
        return n.ToString();
    }
}
