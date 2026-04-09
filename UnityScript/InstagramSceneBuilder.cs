#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor script — tự động tạo toàn bộ scene Instagram OAuth
/// Menu: Tools → Instagram → Build Scene
/// </summary>
public static class InstagramSceneBuilder
{
    [MenuItem("Tools/Instagram/Build Scene")]
    public static void BuildScene()
    {
        if (!EditorUtility.DisplayDialog(
            "Build Instagram Scene",
            "Tạo mới toàn bộ Instagram UI trong scene hiện tại?\n(Không xóa objects có sẵn)",
            "Tạo", "Hủy")) return;

        // ── Managers ─────────────────────────────────────────────
        var authGO = CreateGO("InstagramAuthManager");
        var authMgr = authGO.AddComponent<InstagramAuthManager>();

        // ── Canvas ───────────────────────────────────────────────
        var canvasGO = CreateCanvasRoot();
        var canvas = canvasGO.GetComponent<Canvas>();

        // ── Panels ───────────────────────────────────────────────
        var loginPanel   = CreatePanel(canvasGO, "LoginPanel",   new Color(0.08f, 0.08f, 0.12f));
        var loadingPanel = CreatePanel(canvasGO, "LoadingPanel", new Color(0.08f, 0.08f, 0.12f));
        var profilePanel = CreatePanel(canvasGO, "ProfilePanel", new Color(0.05f, 0.05f, 0.10f));
        var errorPanel   = CreatePanel(canvasGO, "ErrorPanel",   new Color(0.12f, 0.05f, 0.05f));

        // ── GUIManager GO ─────────────────────────────────────────
        var guiGO  = CreateGO("GUIManager");
        var guiMgr = guiGO.AddComponent<GUIManager>();

        // ── Populate Login Panel ──────────────────────────────────
        var loginTitle  = CreateLabel(loginPanel, "TitleText",  "Đăng nhập Instagram",
                            24, FontStyles.Bold, Color.white, new Vector2(0, 80));
        var loginSub    = CreateLabel(loginPanel, "SubText",    "Kết nối tài khoản của bạn",
                            14, FontStyles.Normal, new Color(0.6f,0.6f,0.6f), new Vector2(0, 40));
        var loginBtn    = CreateButton(loginPanel, "LoginButton", "🔐  Đăng nhập với Instagram",
                            new Vector2(0, -40), new Vector2(280, 52), new Color(0.85f, 0.20f, 0.53f));
        var loginStatus = CreateLabel(loginPanel, "StatusText", "",
                            12, FontStyles.Normal, new Color(0.7f,0.7f,0.7f), new Vector2(0, -110));

        // ── Populate Loading Panel ────────────────────────────────
        var spinner      = CreateSpinnerImage(loadingPanel, "Spinner");
        var loadingTxt   = CreateLabel(loadingPanel, "LoadingText", "Đang đăng nhập...",
                            16, FontStyles.Normal, Color.white, new Vector2(0, -80));

        // ── Populate Profile Panel ────────────────────────────────
        var avatarBg    = CreateImageBox(profilePanel, "AvatarBg",  new Vector2(0, 110), new Vector2(90, 90),
                            new Color(0.85f, 0.20f, 0.53f));
        var avatarLbl   = CreateLabel(profilePanel, "AvatarIcon", "📷",
                            32, FontStyles.Normal, Color.white, new Vector2(0, 110));
        var uname       = CreateLabel(profilePanel, "UsernameText", "@username",
                            22, FontStyles.Bold, Color.white, new Vector2(0, 50));
        var accType     = CreateLabel(profilePanel, "AccountTypeText", "Business",
                            13, FontStyles.Normal, new Color(0.85f,0.6f,0.9f), new Vector2(0, 24));

        // Stats row
        var followersLbl = CreateLabel(profilePanel, "FollowersLabel", "Followers",
                            11, FontStyles.Normal, new Color(0.5f,0.5f,0.5f), new Vector2(-80, -20));
        var followersTxt = CreateLabel(profilePanel, "FollowersText",  "—",
                            18, FontStyles.Bold,   Color.white,             new Vector2(-80, -44));
        var mediaLbl    = CreateLabel(profilePanel, "MediaLabel", "Posts",
                            11, FontStyles.Normal, new Color(0.5f,0.5f,0.5f), new Vector2(80, -20));
        var mediaTxt    = CreateLabel(profilePanel, "MediaCountText", "—",
                            18, FontStyles.Bold,   Color.white,              new Vector2(80, -44));

        var expiryTxt   = CreateLabel(profilePanel, "TokenExpiryText", "",
                            11, FontStyles.Normal, new Color(0.4f,0.7f,0.4f), new Vector2(0, -85));
        var logoutBtn   = CreateButton(profilePanel, "LogoutButton", "Đăng xuất",
                            new Vector2(0, -130), new Vector2(160, 40), new Color(0.25f, 0.25f, 0.3f));

        // ── Populate Error Panel ──────────────────────────────────
        var errorIcon  = CreateLabel(errorPanel, "ErrorIcon", "😕",
                            48, FontStyles.Normal, Color.white, new Vector2(0, 80));
        var errorTxt   = CreateLabel(errorPanel, "ErrorText", "Đã xảy ra lỗi",
                            15, FontStyles.Normal, new Color(1f, 0.5f, 0.5f), new Vector2(0, 10));
        var retryBtn   = CreateButton(errorPanel, "RetryButton", "Thử lại",
                            new Vector2(0, -60), new Vector2(160, 44), new Color(0.2f, 0.5f, 0.8f));

        // ── Wire up GUIManager via SerializedObject ───────────────
        var so = new SerializedObject(guiMgr);
        so.FindProperty("loginPanel").objectReferenceValue   = loginPanel;
        so.FindProperty("loadingPanel").objectReferenceValue = loadingPanel;
        so.FindProperty("profilePanel").objectReferenceValue  = profilePanel;
        so.FindProperty("errorPanel").objectReferenceValue   = errorPanel;

        so.FindProperty("loginButton").objectReferenceValue  = loginBtn.GetComponent<Button>();
        so.FindProperty("loginStatusText").objectReferenceValue = loginStatus.GetComponent<TextMeshProUGUI>();

        so.FindProperty("loadingText").objectReferenceValue   = loadingTxt.GetComponent<TextMeshProUGUI>();
        so.FindProperty("loadingSpinner").objectReferenceValue = spinner.GetComponent<Image>();

        so.FindProperty("usernameText").objectReferenceValue    = uname.GetComponent<TextMeshProUGUI>();
        so.FindProperty("accountTypeText").objectReferenceValue = accType.GetComponent<TextMeshProUGUI>();
        so.FindProperty("followersText").objectReferenceValue   = followersTxt.GetComponent<TextMeshProUGUI>();
        so.FindProperty("mediaCountText").objectReferenceValue  = mediaTxt.GetComponent<TextMeshProUGUI>();
        so.FindProperty("tokenExpiryText").objectReferenceValue = expiryTxt.GetComponent<TextMeshProUGUI>();
        so.FindProperty("logoutButton").objectReferenceValue   = logoutBtn.GetComponent<Button>();

        so.FindProperty("errorText").objectReferenceValue  = errorTxt.GetComponent<TextMeshProUGUI>();
        so.FindProperty("retryButton").objectReferenceValue = retryBtn.GetComponent<Button>();
        so.ApplyModifiedProperties();

        // Chỉ Login panel hiện mặc định
        loadingPanel.SetActive(false);
        profilePanel.SetActive(false);
        errorPanel.SetActive(false);

        // Đánh dấu scene dirty
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Selection.activeGameObject = canvasGO;
        Debug.Log("[InstagramSceneBuilder] ✅ Scene đã được tạo thành công!");
        EditorUtility.DisplayDialog("Hoàn tất",
            "Scene Instagram OAuth đã được tạo!\n\nChỉnh serverUrl trong InstagramAuthManager nếu cần.",
            "OK");
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    static GameObject CreateGO(string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return go;
    }

    static GameObject CreateCanvasRoot()
    {
        var go = new GameObject("InstagramCanvas");
        Undo.RegisterCreatedObjectUndo(go, "Create Canvas");

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    static GameObject CreatePanel(GameObject parent, string name, Color bgColor)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        go.transform.SetParent(parent.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = bgColor;
        return go;
    }

    static GameObject CreateLabel(GameObject parent, string name, string text,
        int fontSize, FontStyles style, Color color, Vector2 anchoredPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(320, 40);
        rect.anchoredPosition = anchoredPos;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        return go;
    }

    static GameObject CreateButton(GameObject parent, string name, string label,
        Vector2 pos, Vector2 size, Color bgColor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        img.color = bgColor;

        // Rounded corners via sprite (dùng default Unity sprite)
        img.type = Image.Type.Sliced;

        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(
            bgColor.r * 1.2f, bgColor.g * 1.2f, bgColor.b * 1.2f);
        colors.pressedColor = new Color(
            bgColor.r * 0.8f, bgColor.g * 0.8f, bgColor.b * 0.8f);
        btn.colors = colors;

        // Label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        var labelRect = labelGO.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        return go;
    }

    static GameObject CreateSpinnerImage(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(64, 64);
        rect.anchoredPosition = new Vector2(0, 30);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.85f, 0.20f, 0.53f);
        return go;
    }

    static GameObject CreateImageBox(GameObject parent, string name,
        Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }
}
#endif
