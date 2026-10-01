using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按原型图重建登录 / 注册面板（LoginPanel.prefab）。
///
/// 素材（Assets/UI/MainMenu/Sprites/Login/）：
///   背景.png        1672×941  全屏背景（飞船 + 星云）
///   面板底图.png     1230×1278 面板主体，自带六边形标题牌「身份验证」
///   页签底图.png     2095×751  页签条，中间有斜线分隔（左登录 / 右注册）
///   输入框.png       1672×941  输入框底图
///   主按钮-普通.png   1672×941  主按钮常态
///   主按钮-按下.png   1774×887  主按钮按下态
///   睁眼.png / 闭眼.png        密码可见切换图标
///
/// 几何按原型图（1672×941）逐像素测量，再换算到画布坐标。
/// 原型图里面板约 x∈[471,1253] y∈[108,810]，即 782×702。
/// </summary>
public static class LoginPanelBuilder
{
    private const string SpriteDir = "Assets/UI/MainMenu/Sprites/Login/";
    private const string PanelPrefabPath = "Assets/UI/MainMenu/Prefabs/LoginPanel.prefab";
    private const string MenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    /// <summary>原型图画布尺寸，所有像素测量都基于它。</summary>
    private static readonly Vector2 ProtoSize = new Vector2(1672f, 941f);

    // ---- 面板（原型图像素坐标） ----
    /// <summary>
    /// 面板底图显示尺寸。底图裁切后为 1098×1218（比例 0.9015），
    /// 按原型图里面板高度 702 反推宽度：702 × 0.9015 ≈ 633。
    /// </summary>
    private static readonly Vector2 PanelSize = new Vector2(633f, 702f);

    /// <summary>面板中心（原型图像素坐标）。</summary>
    private static readonly Vector2 PanelCenter = new Vector2(862f, 459f);

    /// <summary>
    /// 面板底图内部可用区（相对面板中心）。
    /// 底图裁切后 1098×1218，深色内容区为 x∈[51,1044] y∈[130,1175]，
    /// 按 scale=0.5764 换算到面板局部坐标：中心 (-0.9, -25.1)，尺寸 572×602。
    /// 所有内容元素必须落在此范围内。
    /// </summary>
    private static readonly Vector2 InnerCenter = new Vector2(-1f, -25f);
    private static readonly Vector2 InnerSize = new Vector2(572f, 602f);

    /// <summary>内容区左右内边距，避免贴到发光边框。</summary>
    private const float ContentPadding = 30f;

    /// <summary>内容元素统一宽度。</summary>
    private static float ContentWidth => InnerSize.x - ContentPadding * 2f;

    // ---- 纵向布局：在内部可用区内均匀分布 ----
    /// <summary>内容区顶部（相对面板中心）。</summary>
    private static float InnerTop => InnerCenter.y + InnerSize.y * 0.5f;

    /// <summary>内容区底部（相对面板中心）。</summary>
    private static float InnerBottom => InnerCenter.y - InnerSize.y * 0.5f;

    // ---- 页签 ----
    /// <summary>页签底图裁切后 1803×287（比例 6.28）。</summary>
    private static readonly Vector2 TabBarSize = new Vector2(ContentWidth, ContentWidth / 6.28f);

    // ---- 输入框 ----
    /// <summary>输入框底图裁切后 1310×264（比例 4.96）。</summary>
    private static readonly Vector2 FieldSize = new Vector2(ContentWidth, ContentWidth / 4.96f);

    /// <summary>密码可见切换按钮（输入框右端）。</summary>
    private static readonly Vector2 EyeButtonSize = new Vector2(46f, 46f);
    private static readonly Vector2 EyeButtonOffset = new Vector2(-36f, 0f);

    /// <summary>头像槽宽度：昵称输入框右侧预留的整块区域。</summary>
    private const float AvatarSlotWidth = 96f;

    /// <summary>头像预览按钮边长（正方形）。</summary>
    private const float AvatarButtonSide = 72f;

    /// <summary>预览按钮与「上传头像」小字的间距。</summary>
    private const float AvatarHintGap = 4f;

    /// <summary>「上传头像」小字高度。</summary>
    private const float AvatarHintHeight = 20f;

    /// <summary>
    /// 头像组（预览按钮 + 小字）整体在昵称行内垂直居中后的顶部 y。
    /// 组高 96，行高 103.2，上下各留 3.6。
    /// </summary>
    private static float AvatarGroupTop => FieldSize.y * 0.5f
        - (FieldSize.y - (AvatarButtonSide + AvatarHintGap + AvatarHintHeight)) * 0.5f;

    /// <summary>头像预览按钮中心 y（相对昵称行）。</summary>
    private static float AvatarPreviewY => AvatarGroupTop - AvatarButtonSide * 0.5f;

    /// <summary>「上传头像」小字中心 y（相对昵称行）。</summary>
    private static float AvatarHintY => AvatarGroupTop - AvatarButtonSide - AvatarHintGap - AvatarHintHeight * 0.5f;

    // ---- 错误提示 ----
    private static readonly Vector2 ErrorSize = new Vector2(ContentWidth, 40f);

    // ---- 主按钮 ----
    /// <summary>主按钮底图裁切后 1263×291（比例 4.34）。</summary>
    private static readonly Vector2 SubmitSize = new Vector2(ContentWidth, ContentWidth / 4.34f);

    /// <summary>
    /// 注册页纵向排布：页签 / 用户名 / 密码 / 昵称 / 错误提示 / 主按钮，共 6 个元素。
    ///
    /// 元素总高 549.2，内容区高 602，只剩 52.8 给 5 段间隙。
    /// 因此间隙必须按剩余空间均分（约 10.6），不能再用固定间距 ——
    /// 固定间距会让昵称行底边 (-201.2) 低于按钮顶边 (-178)，错误提示无处可放。
    /// </summary>
    private static float RegisterGap => (InnerSize.y - (TabBarSize.y + FieldSize.y * 3f + ErrorSize.y + SubmitSize.y)) / 5f;

    /// <summary>页签中心 y。</summary>
    private static float TabBarY => InnerTop - TabBarSize.y * 0.5f;

    /// <summary>用户名输入框中心 y。</summary>
    private static float UsernameY => TabBarY - TabBarSize.y * 0.5f - RegisterGap - FieldSize.y * 0.5f;

    /// <summary>密码输入框中心 y。</summary>
    private static float PasswordY => UsernameY - FieldSize.y * 0.5f - RegisterGap - FieldSize.y * 0.5f;

    /// <summary>昵称输入框中心 y（仅注册页显示）。</summary>
    private static float DisplayNameY => PasswordY - FieldSize.y * 0.5f - RegisterGap - FieldSize.y * 0.5f;

    /// <summary>注册页昵称输入框尺寸：右侧预留头像槽。</summary>
    private static readonly Vector2 RegisterDisplayNameSize = new Vector2(ContentWidth - AvatarSlotWidth - 20f, FieldSize.y);

    /// <summary>注册页昵称输入框中心 x。</summary>
    private static float RegisterDisplayNameX => -ContentWidth * 0.5f + RegisterDisplayNameSize.x * 0.5f;

    /// <summary>错误提示中心 y。</summary>
    private static float ErrorY => DisplayNameY - FieldSize.y * 0.5f - RegisterGap - ErrorSize.y * 0.5f;

    /// <summary>主按钮中心 y。</summary>
    private static float SubmitY => ErrorY - ErrorSize.y * 0.5f - RegisterGap - SubmitSize.y * 0.5f;

    [MenuItem("Tools/主菜单/重建登录面板")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
            Debug.LogWarning($"[LoginPanelBuilder] 未找到动态字体 {DynamicFontPath}，登录面板可能无法显示中文。");

        var prefab = BuildPrefab(font);
        AttachToMenu(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[LoginPanelBuilder] 登录面板重建完成，并已接入 MainMenu。");
    }

    private static GameObject BuildPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("LoginPanel", typeof(RectTransform));
        Stretch((RectTransform)root.transform);

        var controller = root.AddComponent<LoginPanelController>();

        // ---- 全屏背景：飞船 + 星云 ----
        var background = CreateStretch("Background", root.transform);
        var backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.sprite = LoadSprite(SpriteDir + "背景.png");
        backgroundImage.color = Color.white;
        backgroundImage.raycastTarget = true;

        // ---- 面板底图：自带六边形标题牌「身份验证」 ----
        var body = CreateRect("Body", root.transform, PanelSize, ToAnchor(PanelCenter));
        var bodyImage = body.gameObject.AddComponent<Image>();
        bodyImage.sprite = LoadSprite(SpriteDir + "面板底图.png");
        bodyImage.color = Color.white;
        bodyImage.preserveAspect = true;
        bodyImage.raycastTarget = true;

        // ---- 页签：一整张底图 + 两个透明热区 ----
        var tabBar = CreateRect("TabBar", body, TabBarSize, new Vector2(InnerCenter.x, TabBarY));
        var tabBarImage = tabBar.gameObject.AddComponent<Image>();
        tabBarImage.sprite = LoadSprite(SpriteDir + "页签底图.png");
        tabBarImage.color = Color.white;
        tabBarImage.raycastTarget = false;

        var loginTab = CreateTabHotzone("LoginTab", tabBar, new Vector2(-TabBarSize.x * 0.25f, 0f), TabBarSize * 0.5f);
        var registerTab = CreateTabHotzone("RegisterTab", tabBar, new Vector2(TabBarSize.x * 0.25f, 0f), TabBarSize * 0.5f);

        var loginTabLabel = CreateText("Label", loginTab.transform, TabBarSize * 0.5f, Vector2.zero, font, 32f);
        loginTabLabel.text = "登录";
        loginTabLabel.color = UiTokens.AccentCyan;

        var registerTabLabel = CreateText("Label", registerTab.transform, TabBarSize * 0.5f, Vector2.zero, font, 32f);
        registerTabLabel.text = "注册";
        registerTabLabel.color = UiTokens.TextSecondary;

        // 选中态：页签底部的青色下划线。
        var loginHighlight = CreateRect("LoginTabHighlight", tabBar, new Vector2(TabBarSize.x * 0.5f - 40f, 4f),
            new Vector2(-TabBarSize.x * 0.25f, -TabBarSize.y * 0.5f + 6f));
        var loginHighlightImage = loginHighlight.gameObject.AddComponent<Image>();
        loginHighlightImage.color = UiTokens.AccentCyan;
        loginHighlightImage.raycastTarget = false;

        var registerHighlight = CreateRect("RegisterTabHighlight", tabBar, new Vector2(TabBarSize.x * 0.5f - 40f, 4f),
            new Vector2(TabBarSize.x * 0.25f, -TabBarSize.y * 0.5f + 6f));
        var registerHighlightImage = registerHighlight.gameObject.AddComponent<Image>();
        registerHighlightImage.color = UiTokens.AccentCyan;
        registerHighlightImage.raycastTarget = false;

        // ---- 输入框 ----
        var username = CreateInputField("UsernameInput", body, new Vector2(InnerCenter.x, UsernameY), FieldSize, font, "用户名");
        var password = CreateInputField("PasswordInput", body, new Vector2(InnerCenter.x, PasswordY), FieldSize, font, "密码");
        password.contentType = TMP_InputField.ContentType.Password;

        // 密码可见切换：叠在密码框右端。
        var eyeButton = CreateIconButton("PasswordToggle", password.transform, EyeButtonOffset, EyeButtonSize,
            LoadSprite(SpriteDir + "闭眼.png"));
        var eyeIcon = eyeButton.GetComponent<Image>();

        // 昵称行：仅注册页显示。右侧同时放头像预览，避免头像单独占用一整行导致按钮重叠。
        var displayNameRow = CreateRect("DisplayNameRow", body, FieldSize, new Vector2(InnerCenter.x, DisplayNameY));
        var displayName = CreateInputField("DisplayNameInput", displayNameRow,
            new Vector2(RegisterDisplayNameX, 0f), RegisterDisplayNameSize, font, "昵称");

        // 头像槽：与昵称输入框并排，竖向排「预览按钮 + 上传头像小字」。
        var avatarRow = CreateRect("AvatarRow", displayNameRow,
            new Vector2(AvatarSlotWidth, FieldSize.y),
            new Vector2(ContentWidth * 0.5f - AvatarSlotWidth * 0.5f, 0f));

        // 预览按钮：点击后打开文件选择，位于槽上部。
        var avatarPreview = CreateRect("AvatarPreview", avatarRow, new Vector2(AvatarButtonSide, AvatarButtonSide),
            new Vector2(0f, AvatarPreviewY));
        var avatarImage = avatarPreview.gameObject.AddComponent<Image>();
        avatarImage.color = Color.white;
        avatarImage.preserveAspect = true;
        avatarImage.raycastTarget = true;

        var avatarButton = avatarPreview.gameObject.AddComponent<Button>();
        avatarButton.targetGraphic = avatarImage;
        avatarButton.transition = Selectable.Transition.None;

        // 「上传头像」小字：位于预览按钮下方，提示点击区域。
        var avatarHint = CreateText("AvatarHint", avatarRow,
            new Vector2(AvatarSlotWidth, AvatarHintHeight), new Vector2(0f, AvatarHintY), font, 18f);
        avatarHint.text = "上传头像";
        avatarHint.color = UiTokens.TextMuted;

        // ---- 错误提示 ----
        var error = CreateText("ErrorText", body, ErrorSize, new Vector2(InnerCenter.x, ErrorY), font, 28f);
        error.color = UiTokens.AccentWeapon;
        error.text = string.Empty;

        // ---- 主按钮 ----
        var submit = CreateImageButton("SubmitButton", body, new Vector2(InnerCenter.x, SubmitY), SubmitSize,
            LoadSprite(SpriteDir + "主按钮-普通.png"));
        var submitBackground = submit.GetComponent<Image>();

        var submitLabel = CreateText("Label", submit.transform, SubmitSize, Vector2.zero, font, 44f);
        submitLabel.text = "登 录";
        submitLabel.color = Color.white;

        BindController(controller, root, loginTab, registerTab, loginHighlight, registerHighlight,
            username, password, displayName, displayNameRow.gameObject,
            avatarRow.gameObject, avatarButton, avatarImage,
            eyeButton, eyeIcon, LoadSprite(SpriteDir + "睁眼.png"), LoadSprite(SpriteDir + "闭眼.png"),
            submit, submitBackground, LoadSprite(SpriteDir + "主按钮-普通.png"), LoadSprite(SpriteDir + "主按钮-按下.png"),
            submitLabel, error);

        // 默认隐藏：进主菜单时不该露出任何痕迹，由 MainMenuAccountBinder 决定何时弹出。
        root.SetActive(false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// 把登录面板挂进 MainMenu.prefab，并完成三件事：
    /// 1. 挂面板实例（放在最上层，遮住主菜单）；
    /// 2. 在 MainMenu 根节点上挂 <see cref="MainMenuAccountBinder"/>，接线面板与昵称栏；
    /// 3. 昵称栏右侧新增登出按钮（默认隐藏，登录后才显示）。
    /// </summary>
    private static void AttachToMenu(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
        var saved = false;
        try
        {
            var oldPanel = FindChild(contents.transform, "LoginPanel");
            if (oldPanel != null)
                Object.DestroyImmediate(oldPanel.gameObject);

            var oldLogout = FindChild(contents.transform, "LogoutButton");
            if (oldLogout != null)
                Object.DestroyImmediate(oldLogout.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, contents.transform);
            instance.transform.SetAsLastSibling();

            var panel = instance.GetComponent<LoginPanelController>();

            var playerName = FindChild(contents.transform, "PlayerName");
            var playerNameText = playerName != null ? playerName.GetComponent<TMP_Text>() : null;
            if (playerNameText == null)
                Debug.LogWarning("[LoginPanelBuilder] 未找到 PlayerName 文本，登录后昵称不会更新。");

            var logoutButton = CreateLogoutButton(contents.transform, playerName);

            var binder = contents.GetComponent<MainMenuAccountBinder>();
            if (binder == null)
                binder = contents.AddComponent<MainMenuAccountBinder>();

            var serialized = new SerializedObject(binder);
            serialized.FindProperty("loginPanel").objectReferenceValue = panel;
            serialized.FindProperty("playerNameText").objectReferenceValue = playerNameText;
            serialized.FindProperty("logoutButton").objectReferenceValue = logoutButton;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, MenuPrefabPath);
            saved = true;
        }
        finally
        {
            if (!saved)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>在昵称栏右侧生成登出按钮（默认隐藏，登录后才显示）。</summary>
    private static Button CreateLogoutButton(Transform menuRoot, GameObject playerName)
    {
        var parent = playerName != null ? playerName.transform.parent : menuRoot;

        var go = new GameObject("LogoutButton", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(150f, 60f);
        rect.anchoredPosition = new Vector2(520f, -98.2f);

        var image = go.AddComponent<Image>();
        image.color = UiTokens.LineSoft;
        image.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        var labelRect = CreateRect("Label", rect, new Vector2(150f, 60f), Vector2.zero);
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = "登出";
        label.fontSize = 30f;
        label.color = UiTokens.TextPrimary;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.enableWordWrapping = false;

        go.SetActive(false);
        return button;
    }

    private static void BindController(
        LoginPanelController controller, GameObject panelRoot,
        Button loginTab, Button registerTab, RectTransform loginHighlight, RectTransform registerHighlight,
        TMP_InputField username, TMP_InputField password, TMP_InputField displayName, GameObject displayNameRow,
        GameObject avatarRow, Button avatarButton, Image avatarPreview,
        Button eyeButton, Image eyeIcon, Sprite eyeOpen, Sprite eyeClosed,
        Button submit, Image submitBackground, Sprite submitNormal, Sprite submitPressed,
        TMP_Text submitLabel, TMP_Text error)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        serialized.FindProperty("loginTabButton").objectReferenceValue = loginTab;
        serialized.FindProperty("registerTabButton").objectReferenceValue = registerTab;
        serialized.FindProperty("loginTabHighlight").objectReferenceValue = loginHighlight;
        serialized.FindProperty("registerTabHighlight").objectReferenceValue = registerHighlight;
        serialized.FindProperty("usernameInput").objectReferenceValue = username;
        serialized.FindProperty("passwordInput").objectReferenceValue = password;
        serialized.FindProperty("displayNameInput").objectReferenceValue = displayName;
        serialized.FindProperty("displayNameRow").objectReferenceValue = displayNameRow;
        serialized.FindProperty("avatarRow").objectReferenceValue = avatarRow;
        serialized.FindProperty("avatarButton").objectReferenceValue = avatarButton;
        serialized.FindProperty("avatarPreview").objectReferenceValue = avatarPreview;
        serialized.FindProperty("passwordToggleButton").objectReferenceValue = eyeButton;
        serialized.FindProperty("passwordToggleIcon").objectReferenceValue = eyeIcon;
        serialized.FindProperty("eyeOpenSprite").objectReferenceValue = eyeOpen;
        serialized.FindProperty("eyeClosedSprite").objectReferenceValue = eyeClosed;
        serialized.FindProperty("submitButton").objectReferenceValue = submit;
        serialized.FindProperty("submitBackground").objectReferenceValue = submitBackground;
        serialized.FindProperty("submitNormalSprite").objectReferenceValue = submitNormal;
        serialized.FindProperty("submitPressedSprite").objectReferenceValue = submitPressed;
        serialized.FindProperty("submitLabel").objectReferenceValue = submitLabel;
        serialized.FindProperty("errorText").objectReferenceValue = error;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>生成一个带占位符的输入框（TMP_InputField + 底图 + 文本 + 占位符）。</summary>
    private static TMP_InputField CreateInputField(
        string name, Transform parent, Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font, string placeholder)
    {
        var rect = CreateRect(name, parent, size, anchoredPosition);

        var background = rect.gameObject.AddComponent<Image>();
        background.sprite = LoadSprite(SpriteDir + "输入框.png");
        background.color = Color.white;
        background.raycastTarget = true;

        var textRect = CreateStretch("Text", rect);
        textRect.offsetMin = new Vector2(30f, 0f);
        textRect.offsetMax = new Vector2(-30f, 0f);
        var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;

        text.fontSize = 30f;
        text.color = UiTokens.TextPrimary;
        text.alignment = TextAlignmentOptions.Left;
        text.enableWordWrapping = false;
        text.raycastTarget = false;

        var placeholderRect = CreateStretch("Placeholder", rect);
        placeholderRect.offsetMin = new Vector2(30f, 0f);
        placeholderRect.offsetMax = new Vector2(-30f, 0f);
        var placeholderText = placeholderRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            placeholderText.font = font;

        placeholderText.text = placeholder;
        placeholderText.fontSize = 30f;
        placeholderText.color = UiTokens.TextMuted;
        placeholderText.alignment = TextAlignmentOptions.Left;
        placeholderText.enableWordWrapping = false;
        placeholderText.raycastTarget = false;

        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = rect;
        input.textComponent = text;
        input.placeholder = placeholderText;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;
        input.caretWidth = 2;
        return input;
    }

    /// <summary>生成页签透明热区（无图，只吃点击）。</summary>
    private static Button CreateTabHotzone(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
    {
        var rect = CreateRect(name, parent, size, anchoredPosition);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        return button;
    }

    /// <summary>生成图标按钮（透明底 + 图标）。</summary>
    private static Button CreateIconButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Sprite icon)
    {
        var rect = CreateRect(name, parent, size, anchoredPosition);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = icon;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        return button;
    }

    /// <summary>生成图片按钮（底图 + Button）。</summary>
    private static Button CreateImageButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Sprite sprite)
    {
        var rect = CreateRect(name, parent, size, anchoredPosition);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        return button;
    }

    /// <summary>原型图像素坐标（左上原点）换算为以画布中心为原点的 anchoredPosition。</summary>
    private static Vector2 ToAnchor(Vector2 pixel)
    {
        return new Vector2(pixel.x - ProtoSize.x * 0.5f, ProtoSize.y * 0.5f - pixel.y);
    }

    private static GameObject FindChild(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child.gameObject;
        }

        return null;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 anchoredPosition)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private static RectTransform CreateStretch(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Stretch(rect);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TMP_Text CreateText(
        string name, Transform parent, Vector2 size, Vector2 anchoredPosition, TMP_FontAsset font, float fontSize)
    {
        var rect = CreateRect(name, parent, size, anchoredPosition);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;

        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[LoginPanelBuilder] 找不到素材：{path}");

        return sprite;
    }
}
