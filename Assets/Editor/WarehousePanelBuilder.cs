using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按原型尺寸重建仓库面板：生成独立的 WarehousePanel.prefab，
/// 再从 MainMenu.prefab 中移除旧仓库层级并接入新面板。
/// 所有素材按原图直接使用（白色 tint），不染色也不做非等比拉伸以外的缩放。
/// </summary>
public static class WarehousePanelBuilder
{
    private const string SpriteDir = "Assets/UI/MainMenu/Sprites/";
    private const string WarehouseSpriteDir = SpriteDir + "Warehouse/";
    private const string PanelPrefabPath = "Assets/UI/MainMenu/Prefabs/WarehousePanel.prefab";
    private const string MainMenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string ItemDatabasePath = "Assets/Inventory/Items/ItemDatabase.asset";

    /// <summary>含中文字形的动态字体；静态图集只有少量字形，不足以显示物品名与金额。</summary>
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    // 主体尺寸：让「底图」的青色外框落在与原型一致的比例上（1521×850 缩放后）。
    private static readonly Vector2 BodySize = new Vector2(1603f, 938f);

    // 网格与右侧栏的几何（基于原型的逐像素测量结果）。
    // 为给底部翻页控件腾出空间，格子整体等比缩小到原尺寸的 0.87；
    // 由此空出的横向空间用第 6 列填满，网格右边缘仍停在分隔线左侧。
    private const int GridColumns = 6;
    private const int GridRows = 4;
    private static readonly Vector2 SlotRect = new Vector2(155.5f, 158.8f);
    private static readonly Vector2 SlotContent = new Vector2(130.5f, 133.8f);
    private static readonly Vector2 SlotHighlight = new Vector2(156.9f, 154f);
    private static readonly Vector2 SlotIcon = new Vector2(104.4f, 104.4f);
    private const float SlotGap = 18.3f;
    private static readonly Vector2 GridContentOrigin = new Vector2(102.4f, 182.5f);

    // 翻页控件：纯文字，水平居中于网格正下方，垂直落在底图暗色区内。
    private static readonly Vector2 PagerSize = new Vector2(700f, 56f);
    private static readonly Vector2 PagerPosition = new Vector2(-261.9f, -356.1f);
    private const float PagerButtonWidth = 200f;
    private const float PagerButtonOffset = 250f;
    private const float PagerFontSize = 36f;

    private static readonly Color DividerColor = new Color(0.45f, 0.92f, 1f, 1f);

    [MenuItem("Tools/仓库/[已弃用] 重建旧仓库面板")]
    public static void Rebuild()
    {
        Debug.LogWarning("[WarehousePanelBuilder] 旧仓库已弃用：继续执行会重新添加 WarehousePanel 实例，并把 Btn_Storage 改回旧控制器。");

        var font = LoadProjectFont();
        if (font == null)
            Debug.LogWarning("[WarehousePanelBuilder] 未找到 TMP 字体资源，面板文字可能无法显示中文。");

        var panelPrefab = BuildPanelPrefab(font);
        AttachToMainMenu(panelPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WarehousePanelBuilder] 仓库面板重建完成。");
    }

    /// <summary>生成独立的 WarehousePanel.prefab。</summary>
    private static GameObject BuildPanelPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("WarehousePanel", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var controller = root.AddComponent<WarehousePanelController>();

        // ---- ModalRoot：统一控制整块弹窗的显隐与淡入淡出 ----
        var modalRoot = CreateStretch("ModalRoot", root.transform);
        var modalGroup = modalRoot.gameObject.AddComponent<CanvasGroup>();

        // ---- Mask：全屏遮罩，点击关闭 ----
        var mask = CreateStretch("Mask", modalRoot);
        var maskImage = mask.gameObject.AddComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.55f);
        var maskButton = mask.gameObject.AddComponent<Button>();
        maskButton.targetGraphic = maskImage;

        // ---- Window：仓库主体（底图） ----
        var window = CreateRect("Window", modalRoot, BodySize, Vector2.zero);
        var windowImage = window.gameObject.AddComponent<Image>();
        windowImage.sprite = LoadSprite(WarehouseSpriteDir + "底图.png");
        windowImage.color = Color.white;
        windowImage.raycastTarget = true;

        // 顶部中央标题：整张素材自带边框与「仓库」文字。
        var title = CreateRect("TitleBanner", window, new Vector2(456f, 120f), new Vector2(0f, 363.7f));
        AttachImage(title, WarehouseSpriteDir + "上方仓库标签.png");

        // 右上角关闭按钮。
        var closeRect = CreateRect("CloseButton", window, new Vector2(121f, 116f), new Vector2(693.3f, 344.7f));
        var closeImage = AttachImage(closeRect, WarehouseSpriteDir + "关闭按钮.png", raycastTarget: true);
        var closeButton = closeRect.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.transition = Selectable.Transition.None;

        // ---- 物品格 5×4 ----
        var grid = CreateRect("Grid", window, Vector2.zero, Vector2.zero);
        var slots = new WarehouseSlotView[GridColumns * GridRows];
        for (var row = 0; row < GridRows; row++)
        {
            for (var column = 0; column < GridColumns; column++)
            {
                var index = row * GridColumns + column;
                var contentX = GridContentOrigin.x + column * (SlotContent.x + SlotGap);
                var contentY = GridContentOrigin.y + row * (SlotContent.y + SlotGap);
                var centerX = contentX + SlotContent.x * 0.5f - BodySize.x * 0.5f;

                // 原型中的 y 轴向下，Unity 的 anchoredPosition 向上。
                var centerY = BodySize.y * 0.5f - (contentY + SlotContent.y * 0.5f);

                var slotRect = CreateRect($"Slot_{index:00}", grid, SlotRect, new Vector2(centerX, centerY));
                var frameImage = AttachImage(slotRect, WarehouseSpriteDir + "仓库格.png", raycastTarget: true);

                var highlightRect = CreateRect("Highlight", slotRect, SlotHighlight, Vector2.zero);
                var highlightImage = AttachImage(highlightRect, WarehouseSpriteDir + "仓库格-高亮.png");
                highlightImage.raycastTarget = false;
                highlightImage.enabled = false;

                var iconRect = CreateRect("Icon", slotRect, SlotIcon, Vector2.zero);
                var iconImage = iconRect.gameObject.AddComponent<Image>();
                iconImage.color = Color.white;
                iconImage.raycastTarget = false;
                iconImage.preserveAspect = true;
                iconImage.enabled = false;

                var slot = slotRect.gameObject.AddComponent<WarehouseSlotView>();
                slots[index] = slot;
                BindSlotView(slot, frameImage, iconImage, highlightImage);
            }
        }

        // ---- 翻页控件：纯文字按钮 + 页码，水平居中于网格正下方 ----
        var pager = CreateRect("Pager", window, PagerSize, PagerPosition);
        var prevPageButton = CreateTextButton("PrevButton", pager, "上一页", new Vector2(-PagerButtonOffset, 0f), font, out var prevPageLabel);
        var nextPageButton = CreateTextButton("NextButton", pager, "下一页", new Vector2(PagerButtonOffset, 0f), font, out var nextPageLabel);
        var pageLabel = CreateText("PageLabel", pager, new Vector2(PagerButtonWidth, PagerSize.y), Vector2.zero, font, PagerFontSize);
        pageLabel.alignment = TextAlignmentOptions.Center;

        // ---- 左右分隔线（素材未提供，用纯色细线还原原型） ----
        var divider = CreateRect("Divider", window, new Vector2(6f, 620f), new Vector2(224.3f, -49.3f));
        var dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = DividerColor;
        dividerImage.raycastTarget = false;

        // ---- 右侧详情区 ----
        var detailRect = CreateRect("DetailBox", window, new Vector2(370f, 382.5f), new Vector2(475.45f, 96.25f));
        AttachImage(detailRect, WarehouseSpriteDir + "详情展示.png");
        var previewRect = CreateRect("ItemPreview", detailRect, new Vector2(200f, 200f), Vector2.zero);
        var previewImage = previewRect.gameObject.AddComponent<Image>();
        previewImage.color = Color.white;
        previewImage.raycastTarget = false;
        previewImage.preserveAspect = true;
        previewImage.enabled = false;

        // ---- 名称展示框与物品名 ----
        var namePlate = CreateRect("NamePlate", window, new Vector2(426f, 112f), new Vector2(475.5f, -139.3f));
        AttachImage(namePlate, WarehouseSpriteDir + "名称展示.png");
        var nameText = CreateText("ItemName", namePlate, new Vector2(370f, 70f), Vector2.zero, font, 40f);
        nameText.alignment = TextAlignmentOptions.Center;

        // ---- 金币行：金币素材 + 价格文字 ----
        var coinRect = CreateRect("CoinIcon", window, new Vector2(56.2f, 57.3f), new Vector2(341.4f, -218.3f));
        AttachImage(coinRect, SpriteDir + "金币样式.png");
        var priceText = CreateText("PriceText", window, new Vector2(200f, 60f), new Vector2(524f, -218.3f), font, 36f);
        priceText.alignment = TextAlignmentOptions.Left;

        // ---- 出售按钮（素材自带「出售」文字） ----
        var sellRect = CreateRect("SellButton", window, new Vector2(413.4f, 149f), new Vector2(475.3f, -318.7f));
        var sellImage = AttachImage(sellRect, WarehouseSpriteDir + "出售按钮.png", raycastTarget: true);
        var sellButton = sellRect.gameObject.AddComponent<Button>();
        sellButton.targetGraphic = sellImage;
        sellButton.transition = Selectable.Transition.None;

        BindController(controller, modalRoot, modalGroup, window, maskButton, closeButton,
            slots, previewImage, nameText, priceText, sellButton,
            prevPageButton, nextPageButton, prevPageLabel, nextPageLabel, pageLabel);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把新面板接入 MainMenu.prefab：删除旧层级、挂载新面板、接线仓库按钮。</summary>
    private static void AttachToMainMenu(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        var saved = false;
        try
        {
            var old = contents.transform.Find("WarehousePanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            // 玩家库存宿主：主菜单与战斗场景各自持有一个，但都从同一份存档读写。
            var context = contents.transform.Find("PlayerContext");
            if (context == null)
            {
                context = new GameObject("PlayerContext", typeof(RectTransform)).transform;
                context.SetParent(contents.transform, false);
            }

            var holder = context.GetComponent<InventoryHolder>();
            if (holder == null)
                holder = context.gameObject.AddComponent<InventoryHolder>();

            var holderSerialized = new SerializedObject(holder);
            holderSerialized.FindProperty("loadFromSave").boolValue = true;
            holderSerialized.FindProperty("database").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
            holderSerialized.ApplyModifiedPropertiesWithoutUndo();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, contents.transform);
            instance.transform.SetAsLastSibling();

            var controller = instance.GetComponent<WarehousePanelController>();
            var currency = contents.GetComponentInChildren<PlayerCurrency>(true);

            var controllerSerialized = new SerializedObject(controller);
            controllerSerialized.FindProperty("holder").objectReferenceValue = holder;
            controllerSerialized.FindProperty("currency").objectReferenceValue = currency;
            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

            if (currency == null)
                Debug.LogWarning("[WarehousePanelBuilder] MainMenu 中未找到 PlayerCurrency，出售将无法到账。");

            WireStorageButton(contents, controller);

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

    private static void WireStorageButton(GameObject contents, WarehousePanelController controller)
    {
        foreach (var button in contents.GetComponentsInChildren<Button>(true))
        {
            if (button.name != "Btn_Storage")
                continue;

            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, controller.Toggle);
            EditorUtility.SetDirty(button);
            return;
        }

        Debug.LogWarning("[WarehousePanelBuilder] 未找到 Btn_Storage，仓库按钮未接线。");
    }

    private static void BindController(
        WarehousePanelController controller,
        RectTransform modalRoot,
        CanvasGroup modalGroup,
        RectTransform window,
        Button maskButton,
        Button closeButton,
        WarehouseSlotView[] slots,
        Image preview,
        TMP_Text nameText,
        TMP_Text priceText,
        Button sellButton,
        Button prevPageButton,
        Button nextPageButton,
        TMP_Text prevPageLabel,
        TMP_Text nextPageLabel,
        TMP_Text pageLabel)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("modalRoot").objectReferenceValue = modalRoot.gameObject;
        serialized.FindProperty("modalGroup").objectReferenceValue = modalGroup;
        serialized.FindProperty("panelBody").objectReferenceValue = window;
        serialized.FindProperty("maskButton").objectReferenceValue = maskButton;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;

        var slotsProperty = serialized.FindProperty("slots");
        slotsProperty.arraySize = slots.Length;
        for (var i = 0; i < slots.Length; i++)
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];

        serialized.FindProperty("itemPreview").objectReferenceValue = preview;
        serialized.FindProperty("itemName").objectReferenceValue = nameText;
        serialized.FindProperty("priceText").objectReferenceValue = priceText;
        serialized.FindProperty("sellButton").objectReferenceValue = sellButton;
        serialized.FindProperty("pageSlotCount").intValue = slots.Length;
        serialized.FindProperty("prevPageButton").objectReferenceValue = prevPageButton;
        serialized.FindProperty("nextPageButton").objectReferenceValue = nextPageButton;
        serialized.FindProperty("prevPageLabel").objectReferenceValue = prevPageLabel;
        serialized.FindProperty("nextPageLabel").objectReferenceValue = nextPageLabel;
        serialized.FindProperty("pageLabel").objectReferenceValue = pageLabel;
        serialized.FindProperty("designSize").vector2Value = BodySize;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>生成一个「透明点击区 + 居中文字」的翻页按钮，避免为零散文字额外引入素材。</summary>
    private static Button CreateTextButton(string name, Transform parent, string caption, Vector2 anchoredPosition, TMP_FontAsset font, out TMP_Text label)
    {
        var size = new Vector2(PagerButtonWidth, PagerSize.y);
        var rect = CreateRect(name, parent, size, anchoredPosition);

        var hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;

        label = CreateText("Label", rect, size, Vector2.zero, font, PagerFontSize);
        label.text = caption;
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static void BindSlotView(WarehouseSlotView slot, Image frame, Image icon, Image highlight)
    {
        var serialized = new SerializedObject(slot);
        serialized.FindProperty("frame").objectReferenceValue = frame;
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.FindProperty("highlight").objectReferenceValue = highlight;
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

    /// <summary>挂上素材图。默认只做显示，需要接收点击时把 <paramref name="raycastTarget"/> 设为 true。</summary>
    private static Image AttachImage(RectTransform target, string spritePath, bool raycastTarget = false)
    {
        var image = target.gameObject.AddComponent<Image>();
        image.sprite = LoadSprite(spritePath);
        image.color = Color.white;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, Vector2 size, Vector2 anchoredPosition, TMP_FontAsset font, float fontSize)
    {
        var rect = CreateRect(name, parent, size, anchoredPosition);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;

        text.fontSize = fontSize;
        text.color = Color.white;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[WarehousePanelBuilder] 找不到素材：{path}");

        return sprite;
    }

    /// <summary>复用主菜单已有的 TMP 字体（含中文字形），避免中文显示为方块。</summary>
    private static TMP_FontAsset LoadProjectFont()
    {
        var dynamicFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (dynamicFont != null)
            return dynamicFont;

        Debug.LogWarning($"[WarehousePanelBuilder] 未找到动态字体 {DynamicFontPath}，改用主菜单现有字体。");

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
