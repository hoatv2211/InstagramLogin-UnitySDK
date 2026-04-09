using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Ví dụ sử dụng InstagramAuthManager trong UI
/// Gắn vào GameObject trong scene Login của bạn
/// </summary>
public class LoginUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button logoutButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI usernameText;
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject profilePanel;

    void OnEnable()
    {
        // Đăng ký events
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
        loginButton.onClick.AddListener(OnLoginClicked);
        logoutButton.onClick.AddListener(OnLogoutClicked);

        // Kiểm tra đã đăng nhập chưa
        if (InstagramAuthManager.Instance.IsLoggedIn)
        {
            var user = InstagramAuthManager.Instance.GetCurrentUser();
            if (user != null) ShowProfile(user);
        }
        else
        {
            ShowLoginPanel();
        }
    }

    // ── Button Handlers ──────────────────────────────────────────

    void OnLoginClicked()
    {
        statusText.text = "Đang mở Instagram...";
        loginButton.interactable = false;
        InstagramAuthManager.Instance.Login();
    }

    void OnLogoutClicked()
    {
        InstagramAuthManager.Instance.Logout();
    }

    // ── Event Handlers ───────────────────────────────────────────

    void HandleLoginSuccess(InstagramUser user)
    {
        // Có thể gọi từ thread khác, dùng UnityMainThreadDispatcher
        // hoặc set flag và xử lý trong Update()
        ShowProfile(user);
    }

    void HandleLoginFailed(string error)
    {
        loginButton.interactable = true;
        statusText.text = $"❌ {error}";
        Debug.LogError($"Login failed: {error}");
    }

    void HandleLogout()
    {
        ShowLoginPanel();
    }

    // ── UI Helpers ───────────────────────────────────────────────

    void ShowProfile(InstagramUser user)
    {
        loginPanel.SetActive(false);
        profilePanel.SetActive(true);
        usernameText.text = $"@{user.username}";
        statusText.text = $"✅ Đăng nhập thành công!";
        Debug.Log($"User ID: {user.id}, Token: {user.app_token.Substring(0, 20)}...");
    }

    void ShowLoginPanel()
    {
        loginPanel.SetActive(true);
        profilePanel.SetActive(false);
        loginButton.interactable = true;
        statusText.text = "";
    }
}
