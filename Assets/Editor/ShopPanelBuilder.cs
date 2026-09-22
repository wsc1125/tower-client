using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按界面原型重建商店面板：生成独立的 ShopPanel.prefab，再从 MainMenu.prefab 移除旧层级并接入新面板。
/// 几何按原型逐像素测量（设计尺寸 1533×1026）；所有素材按原图直接使用，不染色。
/// </summary>
public static class ShopPanelBuilder
{
    private const string SpriteDir = "Assets/UI/MainMenu/Sprites/";
    private const string ShopSpriteDir = SpriteDir + "Shop/";
    private const string ShopPanelPrefabPath = "Assets/UI/MainMenu/Prefabs/ShopPanel.prefab";
    private const string MainMenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string ItemDatabasePath = "Assets/Inventory/Items/ItemDatabase.asset";

    /// <summary>含中文字形的动态字体；静态图集只有少量字形，不足以显示商品名与价格。</summary>
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    // 面板设计尺寸：与「底图」素材的原始像素一致（按原型逐像素测量）。
    private static readonly Vector2 BodySize = new Vector2(1533f, 1026f);

    /// <summary>可见商品卡片列数，与原型一致。</summary>
    private const int VisibleColumns = 4;

    private static readonly Vector2 CardSize = new Vector2(316f, 441f);
    private const float CardPitchX = 355f;

    /// <summary>卡片之间的间隙。</summary>
    private const float CardGap = CardPitchX - 316f;

    /// <summary>卡片带左边缘与中线（原型像素坐标）。</summary>
    private const float CardBandLeft = 78f;
    private const float CardBandCenterY = 428.5f;

    /// <summary>可视区宽度：正好容纳 4 列卡片；商品更多时横向滑动。</summary>
    private const float ViewportWidth = VisibleColumns * CardPitchX - CardGap;

    private static readonly Color DividerColor = new Color(0.25f, 0.78f, 1f, 0.65f);

    [MenuItem("Tools/商店/重建商店面板")]
    public static void Rebuild()
    {
        var font = LoadProjectFont();
        if (font == null)
            Debug.LogWarning("[ShopPanelBuilder] 未找到 TMP 字体资源，面板文字可能无法显示中文。");

        var catalog = LoadCatalog();

        // 至少铺满一屏 4 格（与原型一致），商品更多时按实际数量生成并可横向滑动。
        var slotCount = Mathf.Max(catalog.Count, VisibleColumns);
        var panelPrefab = BuildPanelPrefab(font, slotCount);
        AttachToMainMenu(panelPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ShopPanelBuilder] 商店面板重建完成，共 {catalog.Count} 件商品、{slotCount} 个格子。");
    }

    private static List<ItemDefinition> LoadCatalog()
    {
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
        if (database == null)
            Debug.LogWarning($"[ShopPanelBuilder] 未找到 ItemDatabase：{ItemDatabasePath}，商店将为空。");

        return ShopService.GetCatalog(database);
    }

    /// <summary>原型像素坐标（左上原点）换算为以面板中心为原点的 anchoredPosition。</summary>
    private static Vector2 ToAnchor(float pixelX, float pixelY)
    {
        return new Vector2(pixelX - BodySize.x * 0.5f, BodySize.y * 0.5f - pixelY);
    }

    /// <summary>生成独立的 ShopPanel.prefab。</summary>
    private static GameObject BuildPanelPrefab(TMP_FontAsset font, int slotCount)
    {
        var root = new GameObject("ShopPanel", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var controller = root.AddComponent<ShopPanelController>();

        // ---- ModalRoot：统一控制整块弹窗的显隐与淡入淡出 ----
        var modalRoot = CreateStretch("ModalRoot", root.transform);
        var modalGroup = modalRoot.gameObject.AddComponent<CanvasGroup>();

        // ---- Mask：全屏遮罩，点击关闭 ----
        var mask = CreateStretch("Mask", modalRoot);
        var maskImage = mask.gameObject.AddComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.55f);
        var maskButton = mask.gameObject.AddComponent<Button>();
        maskButton.targetGraphic = maskImage;

        // ---- Window：商店主体（底图自带顶部「商店」标题横幅） ----
        var window = CreateRect("Window", modalRoot, BodySize, Vector2.zero);
        var windowImage = window.gameObject.AddComponent<Image>();
        windowImage.sprite = LoadSprite(ShopSpriteDir + "底图.png");
        windowImage.color = Color.white;
        windowImage.raycastTarget = true;

        // 右上角关闭按钮。
        var closeRect = CreateRect("CloseButton", window, new Vector2(96f, 96f), ToAnchor(1452f, 116f));
        var closeImage = AttachImage(closeRect, ShopSpriteDir + "取消按钮.png", raycastTarget: true);
        var closeButton = closeRect.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.transition = Selectable.Transition.None;

        // ---- 商品卡片：可视区 4 列，商品更多时横向滑动 ----
        var scrollArea = CreateRect("ScrollArea", window, new Vector2(ViewportWidth, CardSize.y),
            ToAnchor(CardBandLeft + ViewportWidth * 0.5f, CardBandCenterY));
        // 全透明底图：让卡片间隙处也能拖动滑动。
        var scrollHitArea = scrollArea.gameObject.AddComponent<Image>();
        scrollHitArea.color = new Color(0f, 0f, 0f, 0f);
        scrollHitArea.raycastTarget = true;
        scrollArea.gameObject.AddComponent<RectMask2D>();

        // 内容宽度按格子数量增长；等于可视区宽度时不会产生滑动。
        var contentWidth = slotCount * CardPitchX - CardGap;
        var content = CreateRect("Content", scrollArea, new Vector2(contentWidth, CardSize.y), Vector2.zero);
        content.anchorMin = new Vector2(0f, 0.5f);
        content.anchorMax = new Vector2(0f, 0.5f);
        content.pivot = new Vector2(0f, 0.5f);
        content.anchoredPosition = Vector2.zero;

        var scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = scrollArea;
        scrollRect.content = content;
        scrollRect.horizontal = true;
        scrollRect.vertical = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.135f;
        scrollRect.scrollSensitivity = 30f;

        var slots = new ShopSlotView[slotCount];
        for (var i = 0; i < slotCount; i++)
        {
            var slotRect = CreateRect($"Slot_{i:00}", content, CardSize,
                new Vector2(CardSize.x * 0.5f + i * CardPitchX, 0f));
            slotRect.anchorMin = new Vector2(0f, 0.5f);
            slotRect.anchorMax = new Vector2(0f, 0.5f);
            slotRect.anchoredPosition = new Vector2(CardSize.x * 0.5f + i * CardPitchX, 0f);

            var frame = CreateStretch("Frame", slotRect);
            var frameImage = AttachImage(frame, ShopSpriteDir + "商品卡片.png", raycastTarget: true);

            var highlight = CreateStretch("Highlight", slotRect);
            var highlightImage = AttachImage(highlight, ShopSpriteDir + "商品卡片高亮.png");
            highlightImage.raycastTarget = false;
            highlightImage.enabled = false;

            // 以下三个 y 值按「商品卡片」素材实测换算（素材 272px 高 ↔ 卡片 441 单位，比例 1.6213）：
            // 素材价格框 y 184..237、框内金币中心 y 210.5，卡片内可用区约 y 34..237。
            // 图标上移让出名称空间，名称落在图标与价格框之间，价格居中对齐框内金币。
            var iconRect = CreateRect("Icon", slotRect, new Vector2(160f, 160f), new Vector2(0f, 66.5f));
            var iconImage = iconRect.gameObject.AddComponent<Image>();
            iconImage.color = Color.white;
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.enabled = false;

            // 名称位于图标与价格框之间。
            var nameText = CreateText("ItemName", slotRect, new Vector2(300f, 56f), new Vector2(0f, -44.5f), font, 36f);
            nameText.alignment = TextAlignmentOptions.Center;

            // 价格文字与名称纵向同轴；横向居中于价格框内「金币右侧」的空白区，
            // 而不是卡片中线——否则数字变长会向两侧扩张，先撞上左侧金币。
            // 实测素材金币右缘 x=81、框右内缘 x≈195（→ 卡片单位中心 36、宽 160，约容 9 个字符）。
            var priceText = CreateText("PriceText", slotRect, new Vector2(160f, 56f), new Vector2(36f, -121f), font, 40f);
            priceText.alignment = TextAlignmentOptions.Center;
            EnableAutoSize(priceText);

            var slot = slotRect.gameObject.AddComponent<ShopSlotView>();
            slots[i] = slot;
            BindSlotView(slot, frameImage, highlightImage, iconImage, nameText, priceText);
        }

        // ---- 底部购买栏 ----
        var barRect = CreateRect("BottomBar", window, new Vector2(1357f, 194f), ToAnchor(768.5f, 817f));
        AttachImage(barRect, ShopSpriteDir + "下方购买框.png");

        // 选中商品图标框。
        var iconFrame = CreateRect("IconFrame", barRect, new Vector2(211f, 142.5f), new Vector2(-525.5f, 0f));
        AttachImage(iconFrame, ShopSpriteDir + "下方图标框.png");
        var selectedIconRect = CreateRect("SelectedIcon", iconFrame, new Vector2(110f, 110f), Vector2.zero);
        var selectedIcon = selectedIconRect.gameObject.AddComponent<Image>();
        selectedIcon.color = Color.white;
        selectedIcon.raycastTarget = false;
        selectedIcon.preserveAspect = true;
        selectedIcon.enabled = false;

        // 选中商品价格框（素材自带金币图标）。实测素材金币右缘 x=108、框右内缘 x≈261，
        // 换算到框内单位即 x -23..85；取略窄的 100 宽，给最长文本留出左右余量。
        var priceFrame = CreateRect("PriceFrame", barRect, new Vector2(198f, 91f), new Vector2(-282.5f, 0f));
        AttachImage(priceFrame, ShopSpriteDir + "下方价格显示框.png");
        var selectedPrice = CreateText("SelectedPrice", priceFrame, new Vector2(100f, 56f), new Vector2(31f, 0f), font, 40f);
        selectedPrice.alignment = TextAlignmentOptions.Center;
        EnableAutoSize(selectedPrice);

        // 竖分隔线（素材未提供，用纯色细线还原原型）。
        var divider = CreateRect("Divider", barRect, new Vector2(4f, 124f), new Vector2(184f, 0f));
        var dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = DividerColor;
        dividerImage.raycastTarget = false;

        // 选中商品名称。
        var selectedName = CreateText("SelectedName", barRect, new Vector2(340f, 70f), Vector2.zero, font, 52f);
        selectedName.alignment = TextAlignmentOptions.Center;

        // 购买按钮：素材自带 BUY 字样，用「高亮/常态」两张图做可用状态切换。
        var buyRect = CreateRect("BuyButton", barRect, new Vector2(380f, 150f), new Vector2(447.5f, 0f));
        var buyImage = AttachImage(buyRect, ShopSpriteDir + "购买按钮高亮.png", raycastTarget: true);
        var buyButton = buyRect.gameObject.AddComponent<Button>();
        buyButton.targetGraphic = buyImage;
        buyButton.transition = Selectable.Transition.SpriteSwap;
        var buySprites = buyButton.spriteState;
        buySprites.highlightedSprite = LoadSprite(ShopSpriteDir + "购买按钮高亮.png");
        buySprites.pressedSprite = LoadSprite(ShopSpriteDir + "购买按钮.png");
        buySprites.selectedSprite = LoadSprite(ShopSpriteDir + "购买按钮高亮.png");
        buySprites.disabledSprite = LoadSprite(ShopSpriteDir + "购买按钮.png");
        buyButton.spriteState = buySprites;

        BindController(controller, modalRoot, modalGroup, window, maskButton, closeButton, scrollRect,
            slots, selectedIcon, selectedName, selectedPrice, buyButton);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, ShopPanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把新面板接入 MainMenu.prefab：删除旧层级、挂载新面板、接线商店按钮。</summary>
    private static void AttachToMainMenu(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        var saved = false;
        try
        {
            var old = contents.transform.Find("ShopPanel");
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

            var controller = instance.GetComponent<ShopPanelController>();
            var currency = contents.GetComponentInChildren<PlayerCurrency>(true);

            var controllerSerialized = new SerializedObject(controller);
            controllerSerialized.FindProperty("holder").objectReferenceValue = holder;
            controllerSerialized.FindProperty("currency").objectReferenceValue = currency;
            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

            if (currency == null)
                Debug.LogWarning("[ShopPanelBuilder] MainMenu 中未找到 PlayerCurrency，购买将无法扣款。");

            WireShopButton(contents, controller);

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

    private static void WireShopButton(GameObject contents, ShopPanelController controller)
    {
        foreach (var button in contents.GetComponentsInChildren<Button>(true))
        {
            if (button.name != "Btn_Shop")
                continue;

            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, controller.Toggle);
            EditorUtility.SetDirty(button);
            return;
        }

        Debug.LogWarning("[ShopPanelBuilder] 未找到 Btn_Shop，商店按钮未接线。");
    }

    private static void BindController(
        ShopPanelController controller,
        RectTransform modalRoot,
        CanvasGroup modalGroup,
        RectTransform window,
        Button maskButton,
        Button closeButton,
        ScrollRect scrollRect,
        ShopSlotView[] slots,
        Image selectedIcon,
        TMP_Text selectedName,
        TMP_Text selectedPrice,
        Button buyButton)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("modalRoot").objectReferenceValue = modalRoot.gameObject;
        serialized.FindProperty("modalGroup").objectReferenceValue = modalGroup;
        serialized.FindProperty("panelBody").objectReferenceValue = window;
        serialized.FindProperty("maskButton").objectReferenceValue = maskButton;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.FindProperty("scrollRect").objectReferenceValue = scrollRect;

        var slotsProperty = serialized.FindProperty("slots");
        slotsProperty.arraySize = slots.Length;
        for (var i = 0; i < slots.Length; i++)
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];

        serialized.FindProperty("selectedIcon").objectReferenceValue = selectedIcon;
        serialized.FindProperty("selectedName").objectReferenceValue = selectedName;
        serialized.FindProperty("selectedPrice").objectReferenceValue = selectedPrice;
        serialized.FindProperty("buyButton").objectReferenceValue = buyButton;
        serialized.FindProperty("designSize").vector2Value = BodySize;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BindSlotView(
        ShopSlotView slot, Image frame, Image highlight, Image icon, TMP_Text nameText, TMP_Text priceText)
    {
        var serialized = new SerializedObject(slot);
        serialized.FindProperty("frame").objectReferenceValue = frame;
        serialized.FindProperty("highlight").objectReferenceValue = highlight;
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.FindProperty("itemName").objectReferenceValue = nameText;
        serialized.FindProperty("priceText").objectReferenceValue = priceText;
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

    /// <summary>
    /// 价格文字允许自动缩小：常态保持 40 号，只有当内容超出价格框留给数字的宽度时才逐级变小，
    /// 避免长数字（如 999.9k）顶到左侧金币或溢出框外。
    /// </summary>
    private static void EnableAutoSize(TMP_Text text, float min = 24f)
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = min;
        text.fontSizeMax = text.fontSize;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[ShopPanelBuilder] 找不到素材：{path}");

        return sprite;
    }

    /// <summary>复用主菜单已有的 TMP 字体（含中文字形），避免中文显示为方块。</summary>
    private static TMP_FontAsset LoadProjectFont()
    {
        var dynamicFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (dynamicFont != null)
            return dynamicFont;

        Debug.LogWarning($"[ShopPanelBuilder] 未找到动态字体 {DynamicFontPath}，改用主菜单现有字体。");

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
