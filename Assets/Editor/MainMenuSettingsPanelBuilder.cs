using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成主菜单设置面板（MainMenuSettingsPanel.prefab）：全屏遮罩 + 面板底 + 标题 + 「退出登录」按钮 + 关闭按钮。
/// 视觉完全复用局内 <see cref="SettingsPanelBuilder"/> 的扁平风格（同一套 <see cref="UiTokens"/>）。
///
/// 同时负责：
/// 1. 给主菜单右上角的设置按钮（SettingsButton）补上 Button 组件，点击接到面板 Toggle；
/// 2. 把面板实例挂进 MainMenu.prefab 并接线 <see cref="MainMenuSettingsPanelController"/> 的按钮。
/// </summary>
public static class MainMenuSettingsPanelBuilder
{
    private const string PanelPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenuSettingsPanel.prefab";
    private const string MenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    private const string SettingsButtonName = "SettingsButton";

    // ---- 面板几何（画布中心为原点，与局内设置面板一致） ----
    private static readonly Vector2 PanelSize = new Vector2(760f, 560f);
    private static readonly Vector2 TitleSize = new Vector2(600f, 90f);
    private static readonly Vector2 TitlePosition = new Vector2(0f, 190f);
    private static readonly Vector2 LogoutButtonSize = new Vector2(420f, 110f);
    private static readonly Vector2 LogoutButtonPosition = new Vector2(0f, 20f);
    private static readonly Vector2 CloseButtonSize = new Vector2(96f, 96f);
    private static readonly Vector2 CloseButtonPosition = new Vector2(320f, 232f);

    [MenuItem("Tools/主菜单/重建设置面板")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
            Debug.LogWarning($"[MainMenuSettingsPanelBuilder] 未找到动态字体 {DynamicFontPath}，设置面板可能无法显示中文。");

        var prefab = BuildPrefab(font);
        AttachToMenu(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MainMenuSettingsPanelBuilder] 主菜单设置面板重建完成，并已接入 MainMenu。");
    }

    private static GameObject BuildPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("MainMenuSettingsPanel", typeof(RectTransform));
        Stretch((RectTransform)root.transform);

        var controller = root.AddComponent<MainMenuSettingsPanelController>();

        // ---- PanelRoot：设置面板主体，默认隐藏 ----
        var panelRoot = CreateStretch("PanelRoot", root.transform);

        // 遮罩吃掉全部点击：面板打开期间不应再点到主菜单上的任何按钮。
        var backdrop = panelRoot.gameObject.AddComponent<Image>();
        backdrop.color = UiTokens.LoadingBackdrop;
        backdrop.raycastTarget = true;

        // 面板底：纯色 + 描边，居中显示。
        var body = CreateRect("Body", panelRoot, PanelSize, Vector2.zero);
        var bodyImage = body.gameObject.AddComponent<Image>();
        bodyImage.color = UiTokens.PanelBackground;
        bodyImage.raycastTarget = true;

        var bodyBorder = CreateStretch("Border", body);
        bodyBorder.offsetMin = Vector2.zero;
        bodyBorder.offsetMax = Vector2.zero;
        var bodyBorderImage = bodyBorder.gameObject.AddComponent<Image>();
        bodyBorderImage.color = UiTokens.LineStrong;
        bodyBorderImage.raycastTarget = false;

        var bodyFill = CreateStretch("Fill", body);
        bodyFill.offsetMin = Vector2.one * UiTokens.BorderWidth;
        bodyFill.offsetMax = Vector2.one * -UiTokens.BorderWidth;
        var bodyFillImage = bodyFill.gameObject.AddComponent<Image>();
        bodyFillImage.color = UiTokens.PanelBackground;
        bodyFillImage.raycastTarget = false;

        var title = CreateText("TitleText", body, TitleSize, TitlePosition, font, 56f);
        title.color = UiTokens.AccentCyan;
        title.text = "设置";

        // 「退出登录」：目前设置面板里唯一的项。
        var logout = CreateFlatTextButton("LogoutButton", body, LogoutButtonPosition, LogoutButtonSize,
            font, "退出登录", UiTokens.AccentGold, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        // 关闭按钮：面板右上角的 X。
        var close = CreateFlatTextButton("CloseButton", body, CloseButtonPosition, CloseButtonSize,
            font, "X", UiTokens.LineStrong, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        BindController(controller, panelRoot.gameObject, close, logout);

        // 默认隐藏：进主菜单时不该露出任何痕迹。
        panelRoot.gameObject.SetActive(false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// 把设置面板挂进 MainMenu.prefab，并完成两件事：
    /// 1. 给右上角设置按钮（SettingsButton）补 Button 组件，点击接到面板 Toggle；
    /// 2. 把面板实例挂到 MainMenu 下并接线按钮。
    /// </summary>
    private static void AttachToMenu(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
        var saved = false;
        try
        {
            // ---- 1. 清掉上一次生成的实例，避免重复挂载 ----
            // 按名字 + 预制体来源双重匹配：面板实例可能被改名，或残留旧版本的移除覆盖。
            for (var i = contents.transform.childCount - 1; i >= 0; i--)
            {
                var child = contents.transform.GetChild(i);
                if (child.name == "MainMenuSettingsPanel"
                    || PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == panelPrefab)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            // ---- 2. 挂面板实例并接线 ----
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, contents.transform);
            instance.transform.SetAsLastSibling();

            var controller = instance.GetComponent<MainMenuSettingsPanelController>();
            if (controller == null)
            {
                Debug.LogError("[MainMenuSettingsPanelBuilder] 面板实例上未找到 MainMenuSettingsPanelController，设置按钮未接线。");
            }
            else
            {
                // ---- 3. 给设置按钮补 Button 组件（原节点只有 Image，点了没反应） ----
                var settingsButton = FindChild(contents.transform, SettingsButtonName);
                if (settingsButton == null)
                {
                    Debug.LogWarning($"[MainMenuSettingsPanelBuilder] 未找到 {SettingsButtonName}，设置按钮未接线。");
                }
                else
                {
                    var button = settingsButton.GetComponent<Button>();
                    if (button == null)
                        button = settingsButton.AddComponent<Button>();

                    var image = settingsButton.GetComponent<Image>();
                    if (image != null)
                    {
                        image.raycastTarget = true;
                        button.targetGraphic = image;
                    }

                    button.transition = Selectable.Transition.None;
                    button.onClick = new Button.ButtonClickedEvent();
                    UnityEventTools.AddPersistentListener(button.onClick, controller.Toggle);
                    EditorUtility.SetDirty(button);
                }

                // ---- 4. 接线 MainMenuAccountBinder.settingsPanel（面板实例由本 Builder 拥有） ----
                var binder = contents.GetComponent<MainMenuAccountBinder>();
                if (binder != null)
                {
                    var serialized = new SerializedObject(binder);
                    serialized.FindProperty("settingsPanel").objectReferenceValue = controller;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contents, MenuPrefabPath);
            saved = true;
        }
        finally
        {
            // 保存成功后不能再卸载，否则会把刚写入的内容一起销毁。
            if (!saved)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void BindController(
        MainMenuSettingsPanelController controller, GameObject panelRoot, Button closeButton, Button logoutButton)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.FindProperty("logoutButton").objectReferenceValue = logoutButton;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>按名字在子层级里找节点；找不到返回 null（不报错，便于可选接线）。</summary>
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

    /// <summary>生成一个描边 + 内填充的纯文字按钮（与局内设置面板的按钮同构）。</summary>
    private static Button CreateFlatTextButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size,
        TMP_FontAsset font, string caption, Color borderColor, Color fillColor)
    {
        var rect = CreateRect(name, parent, size, anchoredPosition);
        var borderImage = rect.gameObject.AddComponent<Image>();
        borderImage.color = borderColor;
        borderImage.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = borderImage;
        button.transition = Selectable.Transition.None;

        var inner = new GameObject("Fill", typeof(RectTransform));
        var innerRect = (RectTransform)inner.transform;
        innerRect.SetParent(rect, false);
        innerRect.anchorMin = Vector2.zero;
        innerRect.anchorMax = Vector2.one;
        innerRect.offsetMin = new Vector2(3f, 3f);
        innerRect.offsetMax = new Vector2(-3f, -3f);
        var innerImage = inner.AddComponent<Image>();
        innerImage.color = fillColor;
        innerImage.raycastTarget = false;

        var labelRect = CreateRect("Label", innerRect, size, Vector2.zero);
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            label.font = font;

        label.text = caption;
        label.fontSize = 40f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;

        return button;
    }
}
