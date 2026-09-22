using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成「出发」加载页面并接入主菜单：先产出独立的 LoadingScreen.prefab（全屏遮罩 + 进度条 + 百分比），
/// 再挂进 MainMenu.prefab 的最上层，并把 DepartFlow 接到 Btn_Depart 的 onClick 上。
/// 界面按 UiTokens 扁平风格用纯色绘制（素材库没有进度条素材）。
/// </summary>
public static class LoadingScreenBuilder
{
    private const string MainMenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string LoadingScreenPrefabPath = "Assets/UI/MainMenu/Prefabs/LoadingScreen.prefab";
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    private const string DepartButtonName = "Btn_Depart";
    private const string DefaultStatusText = "正在出发…";

    /// <summary>出发后进入的场景，需已在 Build Settings 中登记。</summary>
    private const string TargetSceneName = "BattleScene";

    // 画布参考分辨率 1920×1080，以下坐标都以画布中心为原点。
    private static readonly Vector2 StatusSize = new Vector2(720f, 90f);
    private static readonly Vector2 StatusPosition = new Vector2(0f, 50f);
    private static readonly Vector2 TrackSize = new Vector2(720f, 22f);
    private static readonly Vector2 TrackPosition = new Vector2(0f, -40f);
    private static readonly Vector2 PercentSize = new Vector2(300f, 60f);
    private static readonly Vector2 PercentPosition = new Vector2(0f, -105f);

    [MenuItem("Tools/主菜单/重建加载页面")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
            Debug.LogWarning($"[LoadingScreenBuilder] 未找到动态字体 {DynamicFontPath}，加载页文字可能无法显示中文。");

        var prefab = BuildPrefab(font);
        AttachToMainMenu(prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[LoadingScreenBuilder] 加载页面重建完成，并已接入 Btn_Depart。");
    }

    private static GameObject BuildPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("LoadingScreen", typeof(RectTransform));
        Stretch((RectTransform)root.transform);

        var screen = root.AddComponent<LoadingScreen>();

        // 遮罩单独一层：整块显隐它，根节点始终保持激活，方便在编辑器里查看结构。
        var overlay = CreateStretch("Overlay", root.transform);
        var backdrop = overlay.gameObject.AddComponent<Image>();
        backdrop.color = UiTokens.LoadingBackdrop;
        // 遮罩必须吃掉全部点击，否则加载期间还能点到下面的主菜单。
        backdrop.raycastTarget = true;

        var status = CreateText("StatusText", overlay, StatusSize, StatusPosition, font, 56f);
        status.color = UiTokens.TextPrimary;
        status.text = DefaultStatusText;

        var track = CreateRect("ProgressTrack", overlay, TrackSize, TrackPosition);
        var trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = UiTokens.LoadingTrack;
        trackImage.raycastTarget = false;

        var fill = CreateRect("ProgressFill", track, Vector2.zero, Vector2.zero);
        Stretch(fill);
        var fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = UiTokens.AccentGold;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0f;
        fillImage.raycastTarget = false;

        var percent = CreateText("PercentText", overlay, PercentSize, PercentPosition, font, 40f);
        percent.color = UiTokens.TextSecondary;
        percent.text = "0%";

        BindLoadingScreen(screen, overlay.gameObject, fillImage, percent, status);

        // 默认隐藏：既避免主菜单一进来就被遮罩挡住，也方便在编辑器里调主菜单。
        overlay.gameObject.SetActive(false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, LoadingScreenPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把加载页挂到 MainMenu.prefab 最上层，并接好出发按钮。</summary>
    private static void AttachToMainMenu(GameObject loadingPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        var saved = false;
        try
        {
            var old = contents.transform.Find("LoadingScreen");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(loadingPrefab, contents.transform);
            // 加载页要盖在所有面板（含商店等弹窗）之上。
            instance.transform.SetAsLastSibling();

            var flow = contents.GetComponent<DepartFlow>();
            if (flow == null)
                flow = contents.gameObject.AddComponent<DepartFlow>();

            var button = FindButton(contents, DepartButtonName);
            var screen = instance.GetComponent<LoadingScreen>();

            var serialized = new SerializedObject(flow);
            serialized.FindProperty("departButton").objectReferenceValue = button;
            serialized.FindProperty("loadingScreen").objectReferenceValue = screen;
            serialized.FindProperty("targetSceneName").stringValue = TargetSceneName;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            WireDepartButton(button, flow);

            PrefabUtility.SaveAsPrefabAsset(contents, MainMenuPrefabPath);
            saved = true;
        }
        finally
        {
            // 保存成功后不能再卸载，否则会把刚写入的内容一起销毁。
            if (!saved)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void WireDepartButton(Button button, DepartFlow flow)
    {
        if (button == null)
        {
            Debug.LogWarning($"[LoadingScreenBuilder] 未找到 {DepartButtonName}，出发按钮未接线。");
            return;
        }

        button.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddPersistentListener(button.onClick, flow.Depart);
        EditorUtility.SetDirty(button);
    }

    private static Button FindButton(GameObject root, string name)
    {
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            if (button.name == name)
                return button;
        }

        return null;
    }

    private static void BindLoadingScreen(
        LoadingScreen screen, GameObject overlay, Image progressFill, TMP_Text percentText, TMP_Text statusText)
    {
        var serialized = new SerializedObject(screen);
        serialized.FindProperty("overlay").objectReferenceValue = overlay;
        serialized.FindProperty("progressFill").objectReferenceValue = progressFill;
        serialized.FindProperty("percentText").objectReferenceValue = percentText;
        serialized.FindProperty("statusText").objectReferenceValue = statusText;
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
        var rect = CreateRect(name, parent, Vector2.zero, Vector2.zero);
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
}
