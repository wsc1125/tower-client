using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 构建联机房间面板（RoomPanel.prefab）。
///
/// 复用登录面板的素材（Assets/UI/MainMenu/Sprites/Login/），保持视觉一致：
///   背景.png        全屏背景
///   面板底图.png     面板主体（自带六边形标题牌）
///   页签底图.png     创建 / 加入 两个页签
///   输入框.png       房间名 / 房间码输入框
///   主按钮-普通.png   主按钮
///
/// 几何沿用 LoginPanelBuilder 的测量结果（原型图 1672×941），
/// 面板尺寸与内容区完全一致，两个面板叠在一起时不会错位。
/// </summary>
public static class RoomPanelBuilder
{
    private const string SpriteDir = "Assets/UI/MainMenu/Sprites/Login/";
    private const string PanelPrefabPath = "Assets/UI/MainMenu/Prefabs/RoomPanel.prefab";
    private const string MenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    /// <summary>原型图画布尺寸，与 LoginPanelBuilder 保持一致。</summary>
    private static readonly Vector2 ProtoSize = new Vector2(1672f, 941f);

    private static readonly Vector2 PanelSize = new Vector2(633f, 702f);
    private static readonly Vector2 PanelCenter = new Vector2(862f, 459f);
    private static readonly Vector2 InnerCenter = new Vector2(-1f, -25f);
    private static readonly Vector2 InnerSize = new Vector2(572f, 602f);

    private const float ContentPadding = 30f;
    private static float ContentWidth => InnerSize.x - ContentPadding * 2f;

    private static float InnerTop => InnerCenter.y + InnerSize.y * 0.5f;

    /// <summary>页签底图裁切后比例 6.28。</summary>
    private static readonly Vector2 TabBarSize = new Vector2(ContentWidth, ContentWidth / 6.28f);

    /// <summary>输入框底图裁切后比例 4.96。</summary>
    private static readonly Vector2 FieldSize = new Vector2(ContentWidth, ContentWidth / 4.96f);

    /// <summary>主按钮底图裁切后比例 4.34。</summary>
    private static readonly Vector2 SubmitSize = new Vector2(ContentWidth, ContentWidth / 4.34f);

    private static readonly Vector2 ErrorSize = new Vector2(ContentWidth, 40f);

    /// <summary>房间码展示区高度。</summary>
    private const float ResultCodeHeight = 96f;

    /// <summary>结果区提示文字高度。</summary>
    private const float ResultHintHeight = 34f;

    /// <summary>结果区按钮高度。</summary>
    private const float ResultButtonHeight = 64f;

    /// <summary>结果区按钮宽度（两个并排）。</summary>
    private static float ResultButtonWidth => (ContentWidth - 20f) * 0.5f;

    /// <summary>
    /// 人数档位。必须与 <see cref="RoomPanelController"/> 的 MaxPlayersOptions 一致 ——
    /// 顺序即索引，错位会导致「点 3 人却创建 4 人房间」。
    /// </summary>
    private static readonly int[] MaxPlayersOptions = { 2, 3, 4 };

    // ---- 纵向布局 ----
    // 创建页：页签 / 房间名 / 人数 / 错误 / 主按钮 / 结果区
    // 加入页：页签 / 房间码 / 错误 / 主按钮 / 结果区
    // 两页共用同一套 y 坐标，保证切换时元素不跳动。
    //
    // 内容区高 602，元素总高约 103+103+40+118 = 364，余 238 分给 4 段间隙（约 59）。
    // 间隙必须按剩余空间均分：固定间距会让错误提示与主按钮重叠。

    private static float TabBarY => InnerTop - TabBarSize.y * 0.5f;

    /// <summary>第一行输入框（创建页=房间名，加入页=房间码）。</summary>
    private static float Row1Y => TabBarY - TabBarSize.y * 0.5f - RowGap - FieldSize.y * 0.5f;

    /// <summary>第二行（创建页=人数选择）。</summary>
    private static float Row2Y => Row1Y - FieldSize.y * 0.5f - RowGap - FieldSize.y * 0.5f;

    /// <summary>错误提示。</summary>
    private static float ErrorY => Row2Y - FieldSize.y * 0.5f - RowGap - ErrorSize.y * 0.5f;

    /// <summary>主按钮。</summary>
    private static float SubmitY => ErrorY - ErrorSize.y * 0.5f - RowGap - SubmitSize.y * 0.5f;

    /// <summary>
    /// 行间距：按内容区剩余空间均分。
    /// 元素总高 = 页签 + 输入框×2 + 错误 + 主按钮，剩余空间分给 4 段间隙。
    /// </summary>
    private static float RowGap => (InnerSize.y
        - (TabBarSize.y + FieldSize.y * 2f + ErrorSize.y + SubmitSize.y)) / 4f;

    /// <summary>结果区顶部（主按钮下方）。</summary>
    private static float ResultTopY => SubmitY - SubmitSize.y * 0.5f - 20f;

    [MenuItem("Tools/主菜单/重建房间面板")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
            Debug.LogWarning($"[RoomPanelBuilder] 未找到动态字体 {DynamicFontPath}，房间面板可能无法显示中文。");

        var prefab = BuildPrefab(font);
        AttachToMenu(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RoomPanelBuilder] 房间面板重建完成，并已接入 MainMenu。");
    }

    private static GameObject BuildPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("RoomPanel", typeof(RectTransform));
        Stretch((RectTransform)root.transform);

        var controller = root.AddComponent<RoomPanelController>();

        // ---- 全屏背景 ----
        var background = CreateStretch("Background", root.transform);
        var backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.sprite = LoadSprite(SpriteDir + "背景.png");
        backgroundImage.color = Color.white;
        backgroundImage.raycastTarget = true;

        // ---- 面板底图 ----
        var body = CreateRect("Body", root.transform, PanelSize, ToAnchor(PanelCenter));
        var bodyImage = body.gameObject.AddComponent<Image>();
        bodyImage.sprite = LoadSprite(SpriteDir + "面板底图.png");
        bodyImage.color = Color.white;
        bodyImage.preserveAspect = true;
        bodyImage.raycastTarget = true;

        // ---- 页签：创建房间 / 加入房间 ----
        var tabBar = CreateRect("TabBar", body, TabBarSize, new Vector2(InnerCenter.x, TabBarY));
        var tabBarImage = tabBar.gameObject.AddComponent<Image>();
        tabBarImage.sprite = LoadSprite(SpriteDir + "页签底图.png");
        tabBarImage.color = Color.white;
        tabBarImage.raycastTarget = false;

        var createTab = CreateHotzone("CreateTab", tabBar, new Vector2(-TabBarSize.x * 0.25f, 0f), TabBarSize * 0.5f);
        var joinTab = CreateHotzone("JoinTab", tabBar, new Vector2(TabBarSize.x * 0.25f, 0f), TabBarSize * 0.5f);

        var createTabLabel = CreateText("Label", createTab.transform, TabBarSize * 0.5f, Vector2.zero, font, 32f);
        createTabLabel.text = "创建房间";
        createTabLabel.color = UiTokens.AccentCyan;

        var joinTabLabel = CreateText("Label", joinTab.transform, TabBarSize * 0.5f, Vector2.zero, font, 32f);
        joinTabLabel.text = "加入房间";
        joinTabLabel.color = UiTokens.TextSecondary;

        var createHighlight = CreateRect("CreateTabHighlight", tabBar, new Vector2(TabBarSize.x * 0.5f - 40f, 4f),
            new Vector2(-TabBarSize.x * 0.25f, -TabBarSize.y * 0.5f + 6f));
        var createHighlightImage = createHighlight.gameObject.AddComponent<Image>();
        createHighlightImage.color = UiTokens.AccentCyan;
        createHighlightImage.raycastTarget = false;

        var joinHighlight = CreateRect("JoinTabHighlight", tabBar, new Vector2(TabBarSize.x * 0.5f - 40f, 4f),
            new Vector2(TabBarSize.x * 0.25f, -TabBarSize.y * 0.5f + 6f));
        var joinHighlightImage = joinHighlight.gameObject.AddComponent<Image>();
        joinHighlightImage.color = UiTokens.AccentCyan;
        joinHighlightImage.raycastTarget = false;

        // ---- 创建页 ----
        var createPage = CreateRect("CreatePage", body, InnerSize, InnerCenter);

        var roomNameInput = CreateInputField("RoomNameInput", createPage,
            new Vector2(0f, Row1Y - InnerCenter.y), FieldSize, font, "房间名");

        // 人数选择：一行「人数上限」标签 + 三个档位按钮（2/3/4）。
        // 用按钮组而非循环切换：档位只有三个，直接点选比反复点击更直观，
        // 也能一眼看出当前选中项。
        var maxPlayersRow = CreateRect("MaxPlayersRow", createPage, FieldSize, new Vector2(0f, Row2Y - InnerCenter.y));
        var maxPlayersBg = maxPlayersRow.gameObject.AddComponent<Image>();
        maxPlayersBg.sprite = LoadSprite(SpriteDir + "输入框.png");
        maxPlayersBg.color = Color.white;
        maxPlayersBg.raycastTarget = true;

        // 标签：靠左，用 stretch 锚点 + 左右内边距，避免 sizeDelta 与 offset 互相干扰。
        var maxPlayersTitle = CreateText("Title", maxPlayersRow, FieldSize, Vector2.zero, font, 28f);
        maxPlayersTitle.text = "人数上限";
        maxPlayersTitle.color = UiTokens.TextMuted;
        maxPlayersTitle.alignment = TextAlignmentOptions.Left;
        StretchWithPadding(maxPlayersTitle.rectTransform, 30f, 0f);

        // 档位按钮组：靠右排列。
        var optionButtons = new Button[MaxPlayersOptions.Length];
        var optionLabels = new TMP_Text[MaxPlayersOptions.Length];

        const float optionWidth = 62f;
        const float optionHeight = 62f;
        const float optionGap = 10f;

        // 整组右对齐：从右往左算每个按钮的中心 x。
        var groupRight = FieldSize.x * 0.5f - 30f;
        var groupWidth = MaxPlayersOptions.Length * optionWidth + (MaxPlayersOptions.Length - 1) * optionGap;

        for (var i = 0; i < MaxPlayersOptions.Length; i++)
        {
            var centerX = groupRight - groupWidth + optionWidth * 0.5f + i * (optionWidth + optionGap);

            var option = CreateRect($"Option{MaxPlayersOptions[i]}", maxPlayersRow,
                new Vector2(optionWidth, optionHeight), new Vector2(centerX, 0f));

            var optionImage = option.gameObject.AddComponent<Image>();
            optionImage.color = UiTokens.SlotBackground;
            optionImage.raycastTarget = true;

            var optionButton = option.gameObject.AddComponent<Button>();
            optionButton.targetGraphic = optionImage;
            optionButton.transition = Selectable.Transition.None;

            var optionLabel = CreateText("Label", option, new Vector2(optionWidth, optionHeight), Vector2.zero, font, 30f);
            optionLabel.text = MaxPlayersOptions[i].ToString();
            optionLabel.color = UiTokens.TextSecondary;

            optionButtons[i] = optionButton;
            optionLabels[i] = optionLabel;
        }

        var createSubmit = CreateImageButton("CreateSubmit", createPage,
            new Vector2(0f, SubmitY - InnerCenter.y), SubmitSize, LoadSprite(SpriteDir + "主按钮-普通.png"));

        var createSubmitLabel = CreateText("Label", createSubmit.transform, SubmitSize, Vector2.zero, font, 40f);
        createSubmitLabel.text = "创建房间";
        createSubmitLabel.color = Color.white;

        // ---- 加入页 ----
        var joinPage = CreateRect("JoinPage", body, InnerSize, InnerCenter);

        var roomCodeInput = CreateInputField("RoomCodeInput", joinPage,
            new Vector2(0f, Row1Y - InnerCenter.y), FieldSize, font, "输入 8 位房间码");
        roomCodeInput.characterLimit = 8;
        roomCodeInput.pointSize = 40f;

        var joinSubmit = CreateImageButton("JoinSubmit", joinPage,
            new Vector2(0f, SubmitY - InnerCenter.y), SubmitSize, LoadSprite(SpriteDir + "主按钮-普通.png"));

        var joinSubmitLabel = CreateText("Label", joinSubmit.transform, SubmitSize, Vector2.zero, font, 40f);
        joinSubmitLabel.text = "加入房间";
        joinSubmitLabel.color = Color.white;

        // ---- 错误提示（两页共用，挂在 body 上）----
        var error = CreateText("ErrorText", body, ErrorSize, new Vector2(InnerCenter.x, ErrorY), font, 26f);
        error.color = UiTokens.AccentWeapon;
        error.text = string.Empty;

        // ---- 结果区（创建成功后展示房间码）----
        // 与输入区**互斥显示**：创建成功后隐藏输入行，结果区占据同一块空间。
        // 若两者同时显示，结果区会被挤出面板外（内容区只有 602 高）。
        var resultGroup = CreateRect("ResultGroup", body, new Vector2(ContentWidth, 380f),
            new Vector2(InnerCenter.x, InnerCenter.y + 20f));

        var resultCode = CreateText("RoomCode", resultGroup, new Vector2(ContentWidth, ResultCodeHeight),
            new Vector2(0f, 190f - ResultCodeHeight * 0.5f), font, 64f);
        resultCode.text = "--------";
        resultCode.color = UiTokens.AccentCyan;
        resultCode.characterSpacing = 8f;

        var resultHint = CreateText("Hint", resultGroup, new Vector2(ContentWidth, ResultHintHeight),
            new Vector2(0f, 190f - ResultCodeHeight - 16f - ResultHintHeight * 0.5f), font, 22f);
        resultHint.text = "把房间码发给好友";
        resultHint.color = UiTokens.TextMuted;

        var resultButtonY = 190f - ResultCodeHeight - 16f - ResultHintHeight - 24f - ResultButtonHeight * 0.5f;

        var copyButton = CreateImageButton("CopyButton", resultGroup,
            new Vector2(-ResultButtonWidth * 0.5f - 5f, resultButtonY),
            new Vector2(ResultButtonWidth, ResultButtonHeight), LoadSprite(SpriteDir + "主按钮-普通.png"));

        var copyLabel = CreateText("Label", copyButton.transform, new Vector2(ResultButtonWidth, ResultButtonHeight),
            Vector2.zero, font, 26f);
        copyLabel.text = "复制房间码";
        copyLabel.color = Color.white;

        var leaveButton = CreateImageButton("LeaveButton", resultGroup,
            new Vector2(ResultButtonWidth * 0.5f + 5f, resultButtonY),
            new Vector2(ResultButtonWidth, ResultButtonHeight), LoadSprite(SpriteDir + "主按钮-普通.png"));

        var leaveLabel = CreateText("Label", leaveButton.transform, new Vector2(ResultButtonWidth, ResultButtonHeight),
            Vector2.zero, font, 26f);
        leaveLabel.text = "离开房间";
        leaveLabel.color = Color.white;

        // ---- 关闭按钮（面板右上角内侧）----
        // 挂在 body 上，坐标相对 body 中心。body 半宽 316.5、半高 351。
        // 面板底图右上角有六边形标题牌，按钮需避开它，故取 (半宽-70, 半高-70)。
        var closeButton = CreateHotzone("CloseButton", body,
            new Vector2(PanelSize.x * 0.5f - 70f, PanelSize.y * 0.5f - 70f),
            new Vector2(56f, 56f));
        var closeLabel = CreateText("Label", closeButton.transform, new Vector2(56f, 56f), Vector2.zero, font, 40f);
        closeLabel.text = "×";
        closeLabel.color = UiTokens.TextSecondary;

        BindController(controller, root,
            createTab, joinTab, createHighlight, joinHighlight,
            createPage.gameObject, roomNameInput, optionButtons, optionLabels, createSubmit, createSubmitLabel,
            joinPage.gameObject, roomCodeInput, joinSubmit, joinSubmitLabel,
            resultGroup.gameObject, resultCode, resultHint, copyButton, leaveButton,
            error, closeButton);

        // 默认隐藏，由 MainMenuRoomBinder 在切到联机模式时弹出。
        root.SetActive(false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把房间面板挂进 MainMenu.prefab，并接线 MainMenuRoomBinder。</summary>
    private static void AttachToMenu(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
        var saved = false;
        try
        {
            var oldPanel = FindChild(contents.transform, "RoomPanel");
            if (oldPanel != null)
                Object.DestroyImmediate(oldPanel.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, contents.transform);
            instance.transform.SetAsLastSibling();

            var panel = instance.GetComponent<RoomPanelController>();

            var modeTab = contents.GetComponentInChildren<ModeTabSelector>(true);
            if (modeTab == null)
                Debug.LogWarning("[RoomPanelBuilder] 未找到 ModeTabSelector，联机模式切换不会自动弹出房间面板。");

            var binder = contents.GetComponent<MainMenuRoomBinder>();
            if (binder == null)
                binder = contents.AddComponent<MainMenuRoomBinder>();

            var serialized = new SerializedObject(binder);
            serialized.FindProperty("roomPanel").objectReferenceValue = panel;
            serialized.FindProperty("modeTab").objectReferenceValue = modeTab;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 把模式选择器与房间接线交给 DepartFlow，实现「单机直接出发 / 联机先进房」。
            var departFlow = contents.GetComponent<DepartFlow>();
            if (departFlow == null)
            {
                Debug.LogWarning("[RoomPanelBuilder] 未找到 DepartFlow，联机模式下点出发不会先弹房间面板。");
            }
            else
            {
                var flowSerialized = new SerializedObject(departFlow);
                flowSerialized.FindProperty("modeTab").objectReferenceValue = modeTab;
                flowSerialized.FindProperty("roomBinder").objectReferenceValue = binder;
                flowSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(contents, MenuPrefabPath);
            saved = true;
        }
        finally
        {
            if (!saved)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void BindController(
        RoomPanelController controller, GameObject panelRoot,
        Button createTab, Button joinTab, RectTransform createHighlight, RectTransform joinHighlight,
        GameObject createPage, TMP_InputField roomNameInput, Button[] maxPlayersButtons, TMP_Text[] maxPlayersLabels,
        Button createSubmit, TMP_Text createSubmitLabel,
        GameObject joinPage, TMP_InputField roomCodeInput, Button joinSubmit, TMP_Text joinSubmitLabel,
        GameObject resultGroup, TMP_Text resultCode, TMP_Text resultHint, Button copyButton, Button leaveButton,
        TMP_Text error, Button closeButton)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        serialized.FindProperty("createTabButton").objectReferenceValue = createTab;
        serialized.FindProperty("joinTabButton").objectReferenceValue = joinTab;
        serialized.FindProperty("createTabHighlight").objectReferenceValue = createHighlight;
        serialized.FindProperty("joinTabHighlight").objectReferenceValue = joinHighlight;
        serialized.FindProperty("createPage").objectReferenceValue = createPage;
        serialized.FindProperty("roomNameInput").objectReferenceValue = roomNameInput;
        BindArray(serialized.FindProperty("maxPlayersButtons"), maxPlayersButtons);
        BindArray(serialized.FindProperty("maxPlayersLabels"), maxPlayersLabels);
        serialized.FindProperty("createSubmitButton").objectReferenceValue = createSubmit;
        serialized.FindProperty("createSubmitLabel").objectReferenceValue = createSubmitLabel;
        serialized.FindProperty("joinPage").objectReferenceValue = joinPage;
        serialized.FindProperty("roomCodeInput").objectReferenceValue = roomCodeInput;
        serialized.FindProperty("joinSubmitButton").objectReferenceValue = joinSubmit;
        serialized.FindProperty("joinSubmitLabel").objectReferenceValue = joinSubmitLabel;
        serialized.FindProperty("resultGroup").objectReferenceValue = resultGroup;
        serialized.FindProperty("resultCodeText").objectReferenceValue = resultCode;
        serialized.FindProperty("resultHintText").objectReferenceValue = resultHint;
        serialized.FindProperty("copyCodeButton").objectReferenceValue = copyButton;
        serialized.FindProperty("leaveButton").objectReferenceValue = leaveButton;
        serialized.FindProperty("errorText").objectReferenceValue = error;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>把对象数组写入 SerializedProperty（数组字段不能用 objectReferenceValue 直接赋值）。</summary>
    private static void BindArray<T>(SerializedProperty property, T[] values) where T : UnityEngine.Object
    {
        if (property == null)
            return;

        var count = values?.Length ?? 0;
        property.arraySize = count;

        for (var i = 0; i < count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    // ---- 构建辅助（与 LoginPanelBuilder 同款）----

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

    private static Button CreateHotzone(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
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

    /// <summary>
    /// 让 rect 撑满父级并留出左右内边距。
    ///
    /// 必须同时改 anchor 与 offset：只设 offsetMin/offsetMax 而不改 anchor，
    /// 会与已有的 sizeDelta 冲突，导致元素被推离原位（文字错位的根因）。
    /// </summary>
    private static void StretchWithPadding(RectTransform rect, float horizontalPadding, float verticalPadding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
        rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
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
            Debug.LogError($"[RoomPanelBuilder] 找不到素材：{path}");

        return sprite;
    }
}
