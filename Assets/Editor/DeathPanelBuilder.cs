using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成死亡提示面板（DeathPanel.prefab）：全屏遮罩 + 「你已死亡」标题 + 提示文案 + 返回主菜单按钮。
/// 素材库没有对应底图，因此按 <see cref="UiTokens"/> 扁平风格用纯色绘制（与加载页同一套做法）。
/// 生成后把 prefab 挂进 BattleScene 的 HUDCanvas，并接线 <see cref="PlayerDeathFlow2D"/>。
/// </summary>
public static class DeathPanelBuilder
{
    private const string PanelPrefabPath = "Assets/UI/BattleHUD/Prefabs/DeathPanel.prefab";
    private const string BattleScenePath = "Assets/Scenes/BattleScene.unity";
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    /// <summary>HUDCanvas 下已有的 TowerResultPanel 实例名；死亡面板要挂在它之后（更靠上层）。</summary>
    private const string HudCanvasName = "HUDCanvas";

    // 画布参考分辨率 1920×1080，以下坐标都以画布中心为原点。
    private static readonly Vector2 TitleSize = new Vector2(900f, 140f);
    private static readonly Vector2 TitlePosition = new Vector2(0f, 120f);
    private static readonly Vector2 HintSize = new Vector2(900f, 60f);
    private static readonly Vector2 HintPosition = new Vector2(0f, 20f);
    private static readonly Vector2 ButtonSize = new Vector2(420f, 110f);
    private static readonly Vector2 ButtonPosition = new Vector2(0f, -120f);

    [MenuItem("Tools/战斗HUD/重建死亡提示面板")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
            Debug.LogWarning($"[DeathPanelBuilder] 未找到动态字体 {DynamicFontPath}，死亡提示可能无法显示中文。");

        var prefab = BuildPrefab(font);
        AttachToBattleScene(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[DeathPanelBuilder] 死亡提示面板重建完成，并已接入 BattleScene。");
    }

    private static GameObject BuildPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("DeathPanel", typeof(RectTransform));
        Stretch((RectTransform)root.transform);

        var controller = root.AddComponent<DeathPanelController>();

        // ---- PanelRoot：整块显隐的载体，默认隐藏 ----
        var panelRoot = CreateStretch("PanelRoot", root.transform);

        // 遮罩必须吃掉全部点击：死亡后不应再点到 HUD 上的任何按钮。
        var backdrop = panelRoot.gameObject.AddComponent<Image>();
        backdrop.color = UiTokens.LoadingBackdrop;
        backdrop.raycastTarget = true;

        var title = CreateText("TitleText", panelRoot, TitleSize, TitlePosition, font, 96f);
        title.color = UiTokens.RarityColor(ItemRarity.Legendary);
        title.text = "你已死亡";

        var hint = CreateText("HintText", panelRoot, HintSize, HintPosition, font, 36f);
        hint.color = UiTokens.TextSecondary;
        hint.text = "本局携带的战利品已全部丢失";

        var button = CreateFlatTextButton("ConfirmButton", panelRoot, ButtonPosition, ButtonSize,
            font, "返回主菜单", UiTokens.AccentWeapon, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        BindController(controller, panelRoot.gameObject, title, hint, button);

        // 默认隐藏：死亡前不该露出任何痕迹。
        panelRoot.gameObject.SetActive(false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把死亡面板挂到 BattleScene 的 HUDCanvas 下，并接线 PlayerDeathFlow2D。</summary>
    private static void AttachToBattleScene(GameObject panelPrefab)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            BattleScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);

        try
        {
            var canvas = FindInScene(scene, HudCanvasName);
            if (canvas == null)
            {
                Debug.LogError($"[DeathPanelBuilder] BattleScene 中未找到 {HudCanvasName}，死亡面板未接入。");
                return;
            }

            // 先清掉上一次生成的实例，避免重复挂载。
            var existing = canvas.transform.Find("DeathPanel");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, canvas.transform);
            // 死亡提示要盖在结算面板等所有 HUD 之上。
            instance.transform.SetAsLastSibling();

            var controller = instance.GetComponent<DeathPanelController>();
            var flow = FindComponentInScene<PlayerDeathFlow2D>(scene);
            if (flow == null)
            {
                Debug.LogError("[DeathPanelBuilder] BattleScene 中未找到 PlayerDeathFlow2D，死亡面板未接线。");
            }
            else
            {
                var serialized = new SerializedObject(flow);
                serialized.FindProperty("deathPanel").objectReferenceValue = controller;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(flow);
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
        DeathPanelController controller, GameObject panelRoot, TMP_Text title, TMP_Text hint, Button confirmButton)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelRoot").objectReferenceValue = panelRoot;
        serialized.FindProperty("titleText").objectReferenceValue = title;
        serialized.FindProperty("hintText").objectReferenceValue = hint;
        serialized.FindProperty("confirmButton").objectReferenceValue = confirmButton;
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

    /// <summary>生成一个描边 + 内填充的纯文字按钮（与装备配置面板的出售按钮同构）。</summary>
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
