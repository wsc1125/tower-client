using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按新底图重建收藏展馆面板：生成独立的 CollectionPanel.prefab，再从 MainMenu.prefab 接入。
///
/// 设计要点：
///   - 底图（1672×941）自带标题「收藏展馆」与右上角关闭按钮，无需另画。
///   - 无格子底框，展品图标直接浮在底板星空区上。
///   - 4 列网格，图标 240px，纵向滚动。
///   - 不显示任何进度文字。
///
/// 几何按底图像素逐像素测量：
///   底板不透明区 x∈[68,1604] y∈[63,845]
///   内部星空区   x∈[112,1561] y∈[171,800]  （1449 × 629）
/// </summary>
public static class CollectionPanelBuilder
{
    private const string SpriteDir = "Assets/UI/MainMenu/Sprites/Collection/";
    private const string PanelPrefabPath = "Assets/UI/MainMenu/Prefabs/CollectionPanel.prefab";
    private const string MainMenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string ItemDatabasePath = "Assets/Inventory/Items/ItemDatabase.asset";
    private const string CatalogPath = "Assets/Inventory/Items/CollectionCatalog.asset";

    /// <summary>含中文字形的动态字体。</summary>
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    /// <summary>主菜单上的收藏入口按钮名（历史命名，贴图已换成「收藏按钮」）。</summary>
    private const string CollectionButtonName = "Btn_Storage";

    /// <summary>面板设计尺寸 = 底图原始像素。</summary>
    private static readonly Vector2 BodySize = new Vector2(1672f, 941f);

    // ---- 星空内容区（底图像素坐标） ----
    private const float ContentLeft = 112f;
    private const float ContentTop = 171f;
    private const float ContentRight = 1561f;
    private const float ContentBottom = 800f;

    /// <summary>内容区内边距，避免展品贴到发光边框。</summary>
    private const float ContentPadding = 30f;

    // ---- 网格 ----
    private const int Columns = 4;
    private const float IconSize = 240f;
    private const float CellGap = 24f;

    /// <summary>可视区尺寸（含内边距）。</summary>
    private static float ViewportWidth => ContentRight - ContentLeft - ContentPadding * 2f;
    private static float ViewportHeight => ContentBottom - ContentTop - ContentPadding * 2f;

    /// <summary>网格内容宽度：4 列图标 + 3 个间距。</summary>
    private static float GridWidth => Columns * IconSize + (Columns - 1) * CellGap;

    /// <summary>右上角关闭按钮热区中心（底图像素坐标，对齐底图自带的橙色 X）。</summary>
    private static readonly Vector2 CloseCenter = new Vector2(1543f, 130f);
    private const float CloseHitSize = 110f;

    [MenuItem("Tools/收藏/重建收藏展馆面板")]
    public static void Rebuild()
    {
        var font = LoadProjectFont();
        if (font == null)
            Debug.LogWarning("[CollectionPanelBuilder] 未找到 TMP 字体资源。");

        var panelPrefab = BuildPanelPrefab(font);
        AttachToMainMenu(panelPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[CollectionPanelBuilder] 收藏展馆面板重建完成。");
    }

    /// <summary>底图像素坐标（左上原点）换算为以面板中心为原点的 anchoredPosition。</summary>
    private static Vector2 ToAnchor(float pixelX, float pixelY)
    {
        return new Vector2(pixelX - BodySize.x * 0.5f, BodySize.y * 0.5f - pixelY);
    }

    private static GameObject BuildPanelPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("CollectionPanel", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var controller = root.AddComponent<CollectionPanelController>();

        // ---- ModalRoot：统一控制整块弹窗的显隐与淡入淡出 ----
        var modalRoot = CreateStretch("ModalRoot", root.transform);
        var modalGroup = modalRoot.gameObject.AddComponent<CanvasGroup>();

        // ---- Mask：全屏遮罩，点击关闭 ----
        var mask = CreateStretch("Mask", modalRoot);
        var maskImage = mask.gameObject.AddComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.6f);
        var maskButton = mask.gameObject.AddComponent<Button>();
        maskButton.targetGraphic = maskImage;

        // ---- Window：面板主体（等比缩放载体，尺寸 = 底图像素） ----
        var window = CreateRect("Window", modalRoot, BodySize, Vector2.zero);
        var windowImage = window.gameObject.AddComponent<Image>();
        windowImage.sprite = LoadSprite(SpriteDir + "底板.png");
        windowImage.color = Color.white;
        windowImage.type = Image.Type.Simple;
        windowImage.preserveAspect = false;
        windowImage.raycastTarget = true;

        // 等比缩放：保持底图比例，居中于父级。
        var fitter = window.gameObject.AddComponent<AspectRatioPanel>();
        SetPrivateField(fitter, "designSize", BodySize);

        // ---- 关闭按钮：透明热区，对齐底图自带的橙色 X ----
        var closeRect = CreateRect("CloseButton", window, Vector2.one * CloseHitSize, ToAnchor(CloseCenter.x, CloseCenter.y));
        var closeImage = closeRect.gameObject.AddComponent<Image>();
        closeImage.color = Color.clear;
        closeImage.raycastTarget = true;
        var closeButton = closeRect.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.transition = Selectable.Transition.None;

        // ---- 展品滚动区：位于星空内容区 ----
        var viewportCenter = new Vector2(
            (ContentLeft + ContentRight) * 0.5f,
            (ContentTop + ContentBottom) * 0.5f);
        var scrollArea = CreateRect("ScrollArea", window, new Vector2(ViewportWidth, ViewportHeight), ToAnchor(viewportCenter.x, viewportCenter.y));
        // 全透明底图：让图标间隙处也能拖动滑动。
        var scrollHitArea = scrollArea.gameObject.AddComponent<Image>();
        scrollHitArea.color = new Color(0f, 0f, 0f, 0f);
        scrollHitArea.raycastTarget = true;
        scrollArea.gameObject.AddComponent<RectMask2D>();

        // ---- Content：网格容器，宽度固定为 4 列，高度由控制器按 preferredHeight 驱动 ----
        var content = CreateRect("Content", scrollArea, new Vector2(GridWidth, IconSize), Vector2.zero);
        content.anchorMin = new Vector2(0.5f, 1f);
        content.anchorMax = new Vector2(0.5f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;

        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(IconSize, IconSize);
        grid.spacing = new Vector2(CellGap, CellGap);
        grid.padding = new RectOffset(0, 0, 0, 0);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;

        var scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = scrollArea;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.135f;
        scrollRect.scrollSensitivity = 40f;

        // ---- 预置展位：一屏可见行数向上取整，至少 2 行 ----
        var visibleRows = Mathf.Max(2, Mathf.CeilToInt((ViewportHeight + CellGap) / (IconSize + CellGap)));
        var presetCount = Columns * visibleRows;
        var slots = new CollectionSlotView[presetCount];
        for (var i = 0; i < presetCount; i++)
        {
            var slotRect = CreateRect($"Slot_{i:00}", content, Vector2.one * IconSize, Vector2.zero);
            var iconRect = CreateStretch("Icon", slotRect);
            var iconImage = iconRect.gameObject.AddComponent<Image>();
            iconImage.color = Color.white;
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.enabled = false;

            var slot = slotRect.gameObject.AddComponent<CollectionSlotView>();
            SetPrivateField(slot, "icon", iconImage);
            slots[i] = slot;
        }

        BindController(controller, modalRoot, modalGroup, maskButton, closeButton, scrollRect, content, grid, slots);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把新面板接入 MainMenu.prefab：删除旧实例、挂载新面板、接线收藏入口按钮。</summary>
    private static void AttachToMainMenu(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        var saved = false;
        try
        {
            var old = contents.transform.Find("CollectionPanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var context = contents.transform.Find("PlayerContext");
            if (context == null)
            {
                context = new GameObject("PlayerContext", typeof(RectTransform)).transform;
                context.SetParent(contents.transform, false);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, contents.transform);
            instance.transform.SetAsLastSibling();

            var controller = instance.GetComponent<CollectionPanelController>();
            var currency = contents.GetComponentInChildren<PlayerCurrency>(true);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("database").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
            serialized.FindProperty("catalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<CollectionCatalog>(CatalogPath);
            serialized.FindProperty("currency").objectReferenceValue = currency;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (currency == null)
                Debug.LogWarning("[CollectionPanelBuilder] MainMenu 中未找到 PlayerCurrency，点亮奖励将无法到账。");

            WireCollectionButton(contents, controller);

            PrefabUtility.SaveAsPrefabAsset(contents, MainMenuPrefabPath);
            saved = true;
        }
        finally
        {
            if (!saved)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// 把主菜单上的收藏入口按钮接到面板开关上。
    ///
    /// 注意：入口按钮在 MainMenu 里叫 <c>Btn_Storage</c>（历史命名，贴图已换成「收藏按钮」），
    /// 不是 Btn_Collection。改名会导致接线失败、点击无反应。
    /// </summary>
    private static void WireCollectionButton(GameObject contents, CollectionPanelController controller)
    {
        foreach (var button in contents.GetComponentsInChildren<Button>(true))
        {
            if (button.name != CollectionButtonName)
                continue;

            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, controller.Toggle);
            EditorUtility.SetDirty(button);
            return;
        }

        Debug.LogWarning($"[CollectionPanelBuilder] 未找到 {CollectionButtonName}，收藏入口按钮未接线。");
    }

    private static void BindController(
        CollectionPanelController controller,
        RectTransform modalRoot,
        CanvasGroup modalGroup,
        Button maskButton,
        Button closeButton,
        ScrollRect scrollRect,
        RectTransform content,
        GridLayoutGroup grid,
        CollectionSlotView[] slots)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("modalRoot").objectReferenceValue = modalRoot.gameObject;
        serialized.FindProperty("modalGroup").objectReferenceValue = modalGroup;
        serialized.FindProperty("maskButton").objectReferenceValue = maskButton;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.FindProperty("slotContainer").objectReferenceValue = content;
        serialized.FindProperty("slotGrid").objectReferenceValue = grid;

        var slotsProperty = serialized.FindProperty("slots");
        slotsProperty.arraySize = slots.Length;
        for (var i = 0; i < slots.Length; i++)
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];

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
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[CollectionPanelBuilder] 找不到素材：{path}");

        return sprite;
    }

    /// <summary>通过反射写入私有序列化字段（Builder 内部使用）。</summary>
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var serialized = new SerializedObject(target as Object);
        var property = serialized.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogError($"[CollectionPanelBuilder] 字段不存在：{fieldName}");
            return;
        }

        if (value is Vector2 vector)
            property.vector2Value = vector;
        else if (value is Object obj)
            property.objectReferenceValue = obj;
        else if (value is float f)
            property.floatValue = f;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>复用主菜单已有的 TMP 字体（含中文字形）。</summary>
    private static TMP_FontAsset LoadProjectFont()
    {
        var dynamicFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (dynamicFont != null)
            return dynamicFont;

        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        try
        {
            foreach (var text in contents.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font != null)
                    return text.font;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return TMP_Settings.defaultFontAsset;
    }
}
