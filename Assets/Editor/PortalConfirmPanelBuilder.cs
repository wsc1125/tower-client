using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成传送确认面板（PortalConfirmPanel.prefab）：全屏遮罩 + 标题 + 提示文案 + 确认 / 取消按钮。
/// 素材库没有对应底图，因此按 <see cref="UiTokens"/> 扁平风格用纯色绘制（与死亡提示面板同一套做法）。
/// 生成后把 prefab 挂进 BattleScene 的 HUDCanvas，并接线 <see cref="TowerRunManager"/>。
/// </summary>
public static class PortalConfirmPanelBuilder
{
    private const string PanelPrefabPath = "Assets/UI/BattleHUD/Prefabs/PortalConfirmPanel.prefab";
    private const string BattleScenePath = "Assets/Scenes/BattleScene.unity";
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    private const string HudCanvasName = "HUDCanvas";

    // 画布参考分辨率 1920×1080，以下坐标都以画布中心为原点。
    private static readonly Vector2 TitleSize = new Vector2(900f, 120f);
    private static readonly Vector2 TitlePosition = new Vector2(0f, 130f);
    private static readonly Vector2 HintSize = new Vector2(1000f, 120f);
    private static readonly Vector2 HintPosition = new Vector2(0f, 20f);
    private static readonly Vector2 ButtonSize = new Vector2(360f, 100f);
    private static readonly Vector2 ConfirmPosition = new Vector2(200f, -130f);
    private static readonly Vector2 CancelPosition = new Vector2(-200f, -130f);

    [MenuItem("Tools/战斗HUD/重建传送确认面板")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
            Debug.LogWarning($"[PortalConfirmPanelBuilder] 未找到动态字体 {DynamicFontPath}，传送确认面板可能无法显示中文。");

        var prefab = BuildPrefab(font);
        AttachToBattleScene(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PortalConfirmPanelBuilder] 传送确认面板重建完成，并已接入 BattleScene。");
    }

    private static GameObject BuildPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("PortalConfirmPanel", typeof(RectTransform));
        Stretch((RectTransform)root.transform);

        var controller = root.AddComponent<PortalConfirmPanelController>();

        // ---- PanelRoot：整块显隐的载体，默认隐藏 ----
        var panelRoot = CreateStretch("PanelRoot", root.transform);

        // 遮罩必须吃掉全部点击：弹窗期间不应再点到 HUD 上的任何按钮。
        var backdrop = panelRoot.gameObject.AddComponent<Image>();
        backdrop.color = UiTokens.LoadingBackdrop;
        backdrop.raycastTarget = true;

        var title = CreateText("TitleText", panelRoot, TitleSize, TitlePosition, font, 72f);
        title.color = UiTokens.AccentCyan;
        title.text = "进入下一层";

        var hint = CreateText("HintText", panelRoot, HintSize, HintPosition, font, 34f);
        hint.color = UiTokens.TextSecondary;
        hint.enableWordWrapping = true;
        hint.text = "确认后将前往更深的一层，本层未拾取的战利品会留在原地。";

        var confirm = CreateFlatTextButton("ConfirmButton", panelRoot, ConfirmPosition, ButtonSize,
            font, "确认", UiTokens.AccentWeapon, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        var cancel = CreateFlatTextButton("CancelButton", panelRoot, CancelPosition, ButtonSize,
            font, "取消", UiTokens.LineStrong, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        BindController(controller, panelRoot.gameObject, title, hint, confirm, cancel);

        // 默认隐藏：踩到传送点前不该露出任何痕迹。
        panelRoot.gameObject.SetActive(false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把传送确认面板挂到 BattleScene 的 HUDCanvas 下，并接线 TowerRunManager。</summary>
    private static void AttachToBattleScene(GameObject panelPrefab)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            BattleScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);

        try
        {
            var canvas = FindInScene(scene, HudCanvasName);
            if (canvas == null)
            {
                Debug.LogError($"[PortalConfirmPanelBuilder] BattleScene 中未找到 {HudCanvasName}，传送确认面板未接入。");
                return;
            }

            // 先清掉上一次生成的实例，避免重复挂载。
            var existing = canvas.transform.Find("PortalConfirmPanel");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, canvas.transform);
            // 确认弹窗要盖在 HUD 之上，但排在死亡面板之前（死亡优先级最高）。
            instance.transform.SetAsLastSibling();

            var controller = instance.GetComponent<PortalConfirmPanelController>();
            var manager = FindComponentInScene<TowerRunManager>(scene);
            if (manager == null)
            {
                Debug.LogError("[PortalConfirmPanelBuilder] BattleScene 中未找到 TowerRunManager，传送确认面板未接线。");
            }
            else
            {
                var serialized = new SerializedObject(manager);
                serialized.FindProperty("portalConfirmPanel").objectReferenceValue = controller;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static GameObject FindInScene(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                    return transform.gameObject;
            }
        }

        return null;
    }

    private static T FindComponentInScene<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = root.GetComponentInChildren<T>(true);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void BindController(
        PortalConfirmPanelController controller, GameObject panelRoot,
        TMP_Text title, TMP_Text hint, Button confirmButton, Button cancelButton)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        serialized.FindProperty("titleText").objectReferenceValue = title;
        serialized.FindProperty("hintText").objectReferenceValue = hint;
        serialized.FindProperty("confirmButton").objectReferenceValue = confirmButton;
        serialized.FindProperty("cancelButton").objectReferenceValue = cancelButton;
        serialized.ApplyModifiedPropertiesWithoutUndo();
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

    /// <summary>生成一个描边 + 内填充的纯文字按钮（与死亡提示面板的按钮同构）。</summary>
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
