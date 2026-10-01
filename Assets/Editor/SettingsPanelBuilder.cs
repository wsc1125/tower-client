using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成局内设置面板（SettingsPanel.prefab）：全屏遮罩 + 面板底 + 标题 + 「退出游戏」按钮 + 关闭按钮，
/// 以及叠在其上的「是否退出游戏」二次确认弹窗。
/// 素材库没有对应底图，因此按 <see cref="UiTokens"/> 扁平风格用纯色绘制（与传送确认面板同一套做法）。
///
/// 同时负责：
/// 1. 把设置按钮（SettingsButton）挂进 BattleHUD.prefab 右上角，并把背包按钮（BagButton）左移让位；
/// 2. 把面板实例挂进 BattleHUD.prefab，并接线 <see cref="SettingsPanelController"/> 的按钮。
/// </summary>
public static class SettingsPanelBuilder
{
    private const string PanelPrefabPath = "Assets/UI/BattleHUD/Prefabs/SettingsPanel.prefab";
    private const string HudPrefabPath = "Assets/UI/BattleHUD/Prefabs/BattleHUD.prefab";
    private const string SpriteDir = "Assets/UI/BattleHUD/Sprites/";
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    private const string SettingsButtonName = "SettingsButton";
    private const string BagButtonName = "BagButton";

    /// <summary>设置按钮素材（与主菜单右上角同一个图标）。</summary>
    private const string SettingsSpriteName = "设置按钮.png";

    // ---- 右上角按钮布局（画布参考分辨率 1920×1080，锚点 (1,1)、pivot (1,1)） ----
    /// <summary>按钮尺寸。与背包按钮保持一致，视觉上成一对。</summary>
    private static readonly Vector2 CornerButtonSize = new Vector2(148f, 145.67f);

    /// <summary>距屏幕右上角的边距。</summary>
    private const float CornerMargin = 28f;

    /// <summary>两个按钮之间的水平间距。</summary>
    private const float CornerButtonGap = 16f;

    // ---- 面板几何（画布中心为原点） ----
    private static readonly Vector2 PanelSize = new Vector2(760f, 560f);
    private static readonly Vector2 TitleSize = new Vector2(600f, 90f);
    private static readonly Vector2 TitlePosition = new Vector2(0f, 190f);
    private static readonly Vector2 QuitButtonSize = new Vector2(420f, 110f);
    private static readonly Vector2 QuitButtonPosition = new Vector2(0f, 20f);
    private static readonly Vector2 CloseButtonSize = new Vector2(96f, 96f);
    private static readonly Vector2 CloseButtonPosition = new Vector2(320f, 232f);

    // ---- 确认弹窗几何 ----
    private static readonly Vector2 ConfirmTitleSize = new Vector2(900f, 120f);
    private static readonly Vector2 ConfirmTitlePosition = new Vector2(0f, 130f);
    private static readonly Vector2 ConfirmHintSize = new Vector2(1000f, 140f);
    private static readonly Vector2 ConfirmHintPosition = new Vector2(0f, 20f);
    private static readonly Vector2 ConfirmButtonSize = new Vector2(360f, 100f);
    private static readonly Vector2 ConfirmButtonPosition = new Vector2(200f, -130f);
    private static readonly Vector2 CancelButtonPosition = new Vector2(-200f, -130f);

    [MenuItem("Tools/战斗HUD/重建设置面板")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
            Debug.LogWarning($"[SettingsPanelBuilder] 未找到动态字体 {DynamicFontPath}，设置面板可能无法显示中文。");

        var prefab = BuildPrefab(font);
        AttachToHud(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SettingsPanelBuilder] 设置面板重建完成，并已接入 BattleHUD。");
    }

    private static GameObject BuildPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("SettingsPanel", typeof(RectTransform));
        Stretch((RectTransform)root.transform);

        var controller = root.AddComponent<SettingsPanelController>();

        // ---- PanelRoot：设置面板主体，默认隐藏 ----
        var panelRoot = CreateStretch("PanelRoot", root.transform);

        // 遮罩吃掉全部点击：面板打开期间不应再点到 HUD 上的任何按钮。
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

        // 「退出游戏」：目前设置面板里唯一的项。
        var quit = CreateFlatTextButton("QuitButton", body, QuitButtonPosition, QuitButtonSize,
            font, "退出游戏", UiTokens.AccentGold, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        // 关闭按钮：面板右上角的 X。
        var close = CreateFlatTextButton("CloseButton", body, CloseButtonPosition, CloseButtonSize,
            font, "X", UiTokens.LineStrong, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        // ---- ConfirmRoot：二次确认弹窗，叠在设置面板之上，默认隐藏 ----
        var confirmRoot = CreateStretch("ConfirmRoot", root.transform);
        var confirmBackdrop = confirmRoot.gameObject.AddComponent<Image>();
        confirmBackdrop.color = UiTokens.LoadingBackdrop;
        confirmBackdrop.raycastTarget = true;

        var confirmTitle = CreateText("ConfirmTitleText", confirmRoot, ConfirmTitleSize, ConfirmTitlePosition, font, 72f);
        confirmTitle.color = UiTokens.AccentCyan;
        confirmTitle.text = "退出游戏";

        var confirmHint = CreateText("ConfirmHintText", confirmRoot, ConfirmHintSize, ConfirmHintPosition, font, 34f);
        confirmHint.color = UiTokens.TextSecondary;
        confirmHint.enableWordWrapping = true;
        confirmHint.text = "退出后将返回主界面，并丢失所有携带物（装备槽内的物品也会一并丢失）。";

        var confirm = CreateFlatTextButton("ConfirmButton", confirmRoot, ConfirmButtonPosition, ConfirmButtonSize,
            font, "确认退出", UiTokens.AccentWeapon, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        var cancel = CreateFlatTextButton("CancelButton", confirmRoot, CancelButtonPosition, ConfirmButtonSize,
            font, "取消", UiTokens.LineStrong, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        BindController(controller, panelRoot.gameObject, close, quit,
            confirmRoot.gameObject, confirmTitle, confirmHint, confirm, cancel);

        // 默认全部隐藏：进战斗时不该露出任何痕迹。
        panelRoot.gameObject.SetActive(false);
        confirmRoot.gameObject.SetActive(false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// 把设置面板挂进 BattleHUD.prefab，并完成三件事：
    /// 1. 在右上角新增设置按钮（SettingsButton），点击接到 SettingsPanelController.Toggle；
    /// 2. 把背包按钮（BagButton）左移一个按钮位，给设置按钮让出右上角；
    /// 3. 把面板实例挂到 HUD 下并接线按钮。
    /// </summary>
    private static void AttachToHud(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        var saved = false;
        try
        {
            // ---- 1. 清掉上一次生成的实例，避免重复挂载 ----
            var oldPanel = contents.transform.Find("SettingsPanel");
            if (oldPanel != null)
                Object.DestroyImmediate(oldPanel.gameObject);

            var oldButton = FindChild(contents.transform, SettingsButtonName);
            if (oldButton != null)
                Object.DestroyImmediate(oldButton);

            // ---- 2. 背包按钮左移一个按钮位 ----
            var bagButton = FindChild(contents.transform, BagButtonName);
            if (bagButton != null)
            {
                var bagRect = (RectTransform)bagButton.transform;
                bagRect.anchoredPosition = new Vector2(
                    -(CornerMargin + CornerButtonSize.x + CornerButtonGap), -CornerMargin);
                EditorUtility.SetDirty(bagRect);
            }
            else
            {
                Debug.LogWarning($"[SettingsPanelBuilder] 未找到 {BagButtonName}，背包按钮未左移。");
            }

            // ---- 3. 新增设置按钮（放在背包按钮之后，保证绘制在上层） ----
            var settingsButton = CreateSettingsButton(contents.transform);

            // ---- 4. 挂面板实例并接线 ----
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, contents.transform);
            instance.transform.SetAsLastSibling();

            var controller = instance.GetComponent<SettingsPanelController>();
            if (controller == null)
            {
                Debug.LogError("[SettingsPanelBuilder] 面板实例上未找到 SettingsPanelController，设置按钮未接线。");
            }
            else
            {
                var button = settingsButton.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick = new Button.ButtonClickedEvent();
                    UnityEventTools.AddPersistentListener(button.onClick, controller.Toggle);
                    EditorUtility.SetDirty(button);
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contents, HudPrefabPath);
            saved = true;
        }
        finally
        {
            // 保存成功后不能再卸载，否则会把刚写入的内容一起销毁。
            if (!saved)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>在 HUD 右上角生成设置按钮：图标 + Button，锚点 (1,1)、pivot (1,1)。</summary>
    private static GameObject CreateSettingsButton(Transform parent)
    {
        var go = new GameObject(SettingsButtonName, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = CornerButtonSize;
        rect.anchoredPosition = new Vector2(-CornerMargin, -CornerMargin);

        var image = go.AddComponent<Image>();
        image.sprite = LoadSprite(SpriteDir + SettingsSpriteName);
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        return go;
    }

    private static void BindController(
        SettingsPanelController controller, GameObject panelRoot, Button closeButton, Button quitButton,
        GameObject confirmRoot, TMP_Text confirmTitle, TMP_Text confirmHint,
        Button confirmButton, Button cancelButton)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.FindProperty("quitButton").objectReferenceValue = quitButton;
        serialized.FindProperty("confirmRoot").objectReferenceValue = confirmRoot;
        serialized.FindProperty("confirmTitleText").objectReferenceValue = confirmTitle;
        serialized.FindProperty("confirmHintText").objectReferenceValue = confirmHint;
        serialized.FindProperty("confirmButton").objectReferenceValue = confirmButton;
        serialized.FindProperty("cancelButton").objectReferenceValue = cancelButton;
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

    /// <summary>生成一个描边 + 内填充的纯文字按钮（与传送确认面板的按钮同构）。</summary>
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

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[SettingsPanelBuilder] 找不到素材：{path}");

        return sprite;
    }
}
