using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 生成「装备配置」试验区面板（新仓库原型）：一张完整底图打底，左侧仓库分页网格，
/// 右侧顶部 5 个装备槽 + 下方 5×4 携带背包，右下为物品详情区。
/// 仓库格与背包格共用原有「仓库格 / 仓库格-高亮」素材，装备格使用新导入的装备类型图标。
/// 重建 LoadoutPanel.prefab 后接线 Btn_Storage（仓库主入口）与 Btn_Loadout，
/// 并移除已弃用的 WarehousePanel 实例（prefab 与脚本文件仍保留在磁盘上，不删除）。
/// </summary>
public static class LoadoutPanelBuilder
{
    private const string PanelPrefabPath = "Assets/UI/MainMenu/Prefabs/LoadoutPanel.prefab";
    private const string MainMenuPrefabPath = "Assets/UI/MainMenu/Prefabs/MainMenu.prefab";
    private const string ItemDatabasePath = "Assets/Inventory/Items/ItemDatabase.asset";

    private const string PrototypeSpriteDir = "Assets/UI/MainMenu/Sprites/WarehousePrototype/";
    private const string WarehouseSpriteDir = "Assets/UI/MainMenu/Sprites/Warehouse/";

    /// <summary>含中文字形的动态字体；静态图集只有少量字形，不足以显示物品名与计数。</summary>
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    /// <summary>仓库主入口按钮。</summary>
    private const string StorageButtonName = "Btn_Storage";

    /// <summary>装备配置按钮；新仓库上线后仍暂时保留为第二入口。</summary>
    private const string LoadoutButtonName = "Btn_Loadout";

    // ---- 设计尺寸：与项目画布参考分辨率一致，底图可 1:1 铺满 ----
    private static readonly Vector2 BodySize = new Vector2(1920f, 1080f);

    // ---- 网格：仓库格与背包格尺寸完全一致 ----
    private const float SlotSize = 121f;
    /// <summary>品质底板的边长占格子的比例。仅盖住内板，不压到外圈描边。</summary>
    private const float QualityPlateRatio = 0.76f;
    private const float SlotGap = 14f;

    private const int WarehouseColumns = 5;
    private const int WarehouseRows = 5;
    private const float WarehouseCenterX = 476f;
    private const float WarehouseTop = 160f;

    private const int CarryColumns = 5;
    private const int CarryRows = 4;
    private const float RightCenterX = 1390f;
    private const float CarryTop = 250f;

    private const float EquipTop = 116f;

    // ---- 详情区（对应底图右下角的详情框） ----
    private const float DetailLeft = 946f;
    private const float DetailTop = 797f;
    private const float DetailWidth = 887f;
    private const float DetailHeight = 188f;

    // ---- 详情区右侧的出售按钮 ----
    private const float SellButtonLeft = 1616f;
    private const float SellButtonTop = 851f;
    private const float SellButtonWidth = 192f;
    private const float SellButtonHeight = 82f;

    // ---- 翻页（对应底图左下角的 ‹ › 箭头） ----
    private const float PagerCenterY = 947f;
    private const float PrevArrowCenterX = 307f;
    private const float NextArrowCenterX = 637f;
    private const float PageLabelCenterX = 472f;
    private const float PagerHitSize = 88f;

    // ---- 右上角关闭点击区（叠在底图自带的角标上） ----
    private static readonly Vector2 CloseCenter = new Vector2(1842f, 82f);
    private const float CloseHitSize = 104f;

    /// <summary>装备槽从左到右的顺序：先武器（近战 / 远程），再防具（头盔 / 护甲 / 靴）。</summary>
    private static readonly EquipmentSlotType[] EquipOrder =
    {
        EquipmentSlotType.MeleeWeapon,
        EquipmentSlotType.RangedWeapon,
        EquipmentSlotType.Helmet,
        EquipmentSlotType.BodyArmor,
        EquipmentSlotType.Boots,
    };

    /// <summary>与 <see cref="EquipOrder"/> 一一对应的装备类型图标素材。</summary>
    private static readonly string[] EquipSprites = { "近战武器图标.png", "远程武器图标.png", "头盔图标.png", "护甲图标.png", "战靴图标.png" };

    private static float GridWidth(int columns)
    {
        return columns * SlotSize + (columns - 1) * SlotGap;
    }

    [MenuItem("Tools/装备配置/生成装备配置面板")]
    public static void Rebuild()
    {
        var font = LoadProjectFont();
        if (font == null)
            Debug.LogWarning("[LoadoutPanelBuilder] 未找到 TMP 字体资源，面板文字可能无法显示中文。");

        var panelPrefab = BuildPanelPrefab(font);
        AttachToMainMenu(panelPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[LoadoutPanelBuilder] 装备配置面板生成完成。");
    }

    private static GameObject BuildPanelPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("LoadoutPanel", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var controller = root.AddComponent<LoadoutPanelController>();

        // ---- ModalRoot：统一控制整块弹窗的显隐与淡入淡出 ----
        var modalRoot = CreateStretch("ModalRoot", root.transform);
        var modalGroup = modalRoot.gameObject.AddComponent<CanvasGroup>();

        // ---- Mask：底图铺满时不可见，仅在面板整体缩小时作为背景遮蔽 ----
        var mask = CreateStretch("Mask", modalRoot);
        var maskImage = mask.gameObject.AddComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.6f);
        var maskButton = mask.gameObject.AddComponent<Button>();
        maskButton.targetGraphic = maskImage;

        // ---- Window：面板主体（缩放动画载体） ----
        var window = CreateRect("Window", modalRoot, BodySize, Vector2.zero, BodySize);
        var windowImage = window.gameObject.AddComponent<Image>();
        windowImage.color = new Color(0f, 0f, 0f, 0f);
        windowImage.raycastTarget = false;

        // ---- 原图底图：铺满全屏，自身已含标题、分隔线、翻页与详情框 ----
        var background = CreateStretch("Background", window);
        var backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.sprite = LoadSprite(PrototypeSpriteDir + "底图.png");
        backgroundImage.color = Color.white;
        backgroundImage.type = Image.Type.Simple;
        backgroundImage.preserveAspect = false;
        backgroundImage.raycastTarget = true;

        var warehouseFrame = LoadSprite(WarehouseSpriteDir + "仓库格.png");
        var slotHighlight = LoadSprite(WarehouseSpriteDir + "仓库格-高亮.png");

        // ================= 左区：仓库（5 × 5 分页） =================
        var warehouseSlots = new LoadoutSlotView[WarehouseColumns * WarehouseRows];
        var warehouseX0 = WarehouseCenterX - GridWidth(WarehouseColumns) * 0.5f;
        for (var row = 0; row < WarehouseRows; row++)
        {
            for (var column = 0; column < WarehouseColumns; column++)
            {
                var index = row * WarehouseColumns + column;
                var topLeft = new Vector2(
                    warehouseX0 + column * (SlotSize + SlotGap),
                    WarehouseTop + row * (SlotSize + SlotGap));

                warehouseSlots[index] = CreateSlot($"WarehouseSlot_{index:00}", window, topLeft, SlotSize,
                    warehouseFrame, slotHighlight, EquipmentSlotType.None);
            }
        }

        var warehouseCount = CreateText("WarehouseCount", window, new Vector2(410f, 32f),
            new Vector2(480f, 116f), BodySize, font, 26f);
        warehouseCount.text = "0 / 72";
        warehouseCount.color = Color.white;
        warehouseCount.alignment = TextAlignmentOptions.Right;

        // ================= 右区：装备槽（顶行） =================
        var rightX0 = RightCenterX - GridWidth(CarryColumns) * 0.5f;
        var equipSlots = new LoadoutSlotView[EquipOrder.Length];
        for (var i = 0; i < EquipOrder.Length; i++)
        {
            var topLeft = new Vector2(rightX0 + i * (SlotSize + SlotGap), EquipTop);
            equipSlots[i] = CreateSlot($"EquipSlot_{EquipOrder[i]}", window, topLeft, SlotSize,
                LoadSprite(PrototypeSpriteDir + EquipSprites[i]), null, EquipOrder[i]);
        }

        var carryCount = CreateText("CarryCount", window, new Vector2(180f, 32f),
            new Vector2(1680f, 165f), BodySize, font, 24f);
        carryCount.text = "0 / 20";
        carryCount.color = Color.white;
        carryCount.alignment = TextAlignmentOptions.Right;

        // ================= 右区：携带背包（下方 5 × 4） =================
        var carrySlots = new LoadoutSlotView[CarryColumns * CarryRows];
        for (var row = 0; row < CarryRows; row++)
        {
            for (var column = 0; column < CarryColumns; column++)
            {
                var index = row * CarryColumns + column;
                var topLeft = new Vector2(
                    rightX0 + column * (SlotSize + SlotGap),
                    CarryTop + row * (SlotSize + SlotGap));

                carrySlots[index] = CreateSlot($"CarrySlot_{index:00}", window, topLeft, SlotSize,
                    warehouseFrame, slotHighlight, EquipmentSlotType.None);
            }
        }

        // ================= 翻页：透明点击区叠在底图箭头上 =================
        var prevPageButton = CreateHitArea("PrevPageHit", window,
            new Vector2(PrevArrowCenterX - PagerHitSize * 0.5f, PagerCenterY - PagerHitSize * 0.5f),
            Vector2.one * PagerHitSize);
        var nextPageButton = CreateHitArea("NextPageHit", window,
            new Vector2(NextArrowCenterX - PagerHitSize * 0.5f, PagerCenterY - PagerHitSize * 0.5f),
            Vector2.one * PagerHitSize);

        var pageLabel = CreateText("PageLabel", window, new Vector2(200f, 44f),
            new Vector2(PageLabelCenterX - 100f, PagerCenterY - 22f), BodySize, font, 28f);
        pageLabel.text = "1 / 3";
        pageLabel.color = Color.white;
        pageLabel.alignment = TextAlignmentOptions.Center;

        // ================= 关闭：叠在底图右上角标上 =================
        var closeButton = CreateHitArea("CloseHit", window,
            CloseCenter - Vector2.one * (CloseHitSize * 0.5f), Vector2.one * CloseHitSize);

        // ================= 右下：物品详情区 =================
        var detailIcon = CreateRect("DetailIcon", window, new Vector2(120f, 120f),
            new Vector2(DetailLeft + 34f, DetailTop + 34f), BodySize);
        var detailIconImage = detailIcon.gameObject.AddComponent<Image>();
        detailIconImage.color = Color.white;
        detailIconImage.raycastTarget = false;
        detailIconImage.preserveAspect = true;
        detailIconImage.enabled = false;

        var detailTextWidth = 1600f - (DetailLeft + 34f + 120f + 28f);
        var detailTextLeft = DetailLeft + 34f + 120f + 28f;
        var detailName = CreateText("DetailName", window, new Vector2(detailTextWidth, 46f),
            new Vector2(detailTextLeft, DetailTop + 46f), BodySize, font, 34f);
        detailName.text = "未选择";
        detailName.color = Color.white;
        detailName.alignment = TextAlignmentOptions.Left;

        var detailMeta = CreateText("DetailMeta", window, new Vector2(detailTextWidth, 36f),
            new Vector2(detailTextLeft, DetailTop + 104f), BodySize, font, 24f);
        detailMeta.text = "—";
        detailMeta.color = new Color(0.75f, 0.9f, 1f, 1f);
        detailMeta.alignment = TextAlignmentOptions.Left;

        var sellButton = CreateFlatTextButton("SellButton", window,
            new Vector2(SellButtonLeft, SellButtonTop), new Vector2(SellButtonWidth, SellButtonHeight),
            font, "出售", UiTokens.AccentWeapon, new Color(0.06f, 0.1f, 0.13f, 0.92f));

        // ---- 拖拽层：拖拽图标跟随指针 ----
        var dragLayer = CreateStretch("DragLayer", root.transform);
        var dragGhost = CreateCentered("DragGhost", dragLayer, new Vector2(110f, 110f));
        var dragGhostImage = dragGhost.gameObject.AddComponent<Image>();
        dragGhostImage.raycastTarget = false;
        dragGhostImage.preserveAspect = true;
        dragGhostImage.enabled = false;

        BindController(controller, modalRoot, modalGroup, window, maskButton, closeButton,
            warehouseSlots, carrySlots, equipSlots,
            warehouseCount, carryCount, prevPageButton, nextPageButton, pageLabel,
            detailIconImage, detailName, detailMeta, sellButton, dragGhostImage);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把新面板接入 MainMenu.prefab：挂到 PlayerContext 同级、接线 Btn_Loadout。</summary>
    private static void AttachToMainMenu(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(MainMenuPrefabPath);
        var saved = false;
        try
        {
            var old = contents.transform.Find("LoadoutPanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            // 弃用旧仓库：只移除 MainMenu 中的实例，WarehousePanel.prefab 与脚本文件均保留。
            var legacyWarehouse = contents.transform.Find("WarehousePanel");
            if (legacyWarehouse != null)
                Object.DestroyImmediate(legacyWarehouse.gameObject);

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

            var controller = instance.GetComponent<LoadoutPanelController>();
            var currency = contents.GetComponentInChildren<PlayerCurrency>(true);
            if (currency == null)
                Debug.LogWarning("[LoadoutPanelBuilder] MainMenu 中未找到 PlayerCurrency，出售将无法到账。");

            var controllerSerialized = new SerializedObject(controller);
            controllerSerialized.FindProperty("holder").objectReferenceValue = holder;
            controllerSerialized.FindProperty("currency").objectReferenceValue = currency;
            controllerSerialized.FindProperty("designSize").vector2Value = BodySize;
            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

            WirePanelButton(contents, StorageButtonName, controller);
            WirePanelButton(contents, LoadoutButtonName, controller);

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

    /// <summary>把主菜单上的某个入口按钮接到仓库面板的开关上。</summary>
    private static void WirePanelButton(GameObject contents, string buttonName, LoadoutPanelController controller)
    {
        foreach (var button in contents.GetComponentsInChildren<Button>(true))
        {
            if (button.name != buttonName)
                continue;

            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, controller.Toggle);
            EditorUtility.SetDirty(button);
            return;
        }

        Debug.LogWarning($"[LoadoutPanelBuilder] 未找到 {buttonName}，该入口按钮未接线。");
    }

    private static void BindController(
        LoadoutPanelController controller,
        RectTransform modalRoot,
        CanvasGroup modalGroup,
        RectTransform window,
        Button maskButton,
        Button closeButton,
        LoadoutSlotView[] warehouseSlots,
        LoadoutSlotView[] carrySlots,
        LoadoutSlotView[] equipSlots,
        TMP_Text warehouseCount,
        TMP_Text carryCount,
        Button prevPageButton,
        Button nextPageButton,
        TMP_Text pageLabel,
        Image detailIcon,
        TMP_Text detailName,
        TMP_Text detailMeta,
        Button sellButton,
        Image dragGhost)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("modalRoot").objectReferenceValue = modalRoot.gameObject;
        serialized.FindProperty("modalGroup").objectReferenceValue = modalGroup;
        serialized.FindProperty("panelBody").objectReferenceValue = window;
        serialized.FindProperty("maskButton").objectReferenceValue = maskButton;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;

        AssignArray(serialized, "warehouseSlots", warehouseSlots);
        AssignArray(serialized, "carrySlots", carrySlots);
        AssignArray(serialized, "equipmentSlots", equipSlots);

        serialized.FindProperty("warehouseCountText").objectReferenceValue = warehouseCount;
        serialized.FindProperty("carryCountText").objectReferenceValue = carryCount;
        serialized.FindProperty("prevPageButton").objectReferenceValue = prevPageButton;
        serialized.FindProperty("nextPageButton").objectReferenceValue = nextPageButton;
        serialized.FindProperty("pageLabel").objectReferenceValue = pageLabel;
        serialized.FindProperty("detailIcon").objectReferenceValue = detailIcon;
        serialized.FindProperty("detailName").objectReferenceValue = detailName;
        serialized.FindProperty("detailMeta").objectReferenceValue = detailMeta;
        serialized.FindProperty("sellButton").objectReferenceValue = sellButton;
        serialized.FindProperty("dragGhost").objectReferenceValue = dragGhost;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignArray(SerializedObject owner, string property, LoadoutSlotView[] values)
    {
        var array = owner.FindProperty(property);
        array.arraySize = values.Length;
        for (var i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>生成一个格子：底图（仓库格 / 装备类型图标）+ 可选高亮 + 居中物品图标。</summary>
    private static LoadoutSlotView CreateSlot(string name, Transform parent, Vector2 topLeft, float size,
        Sprite frameSprite, Sprite highlightSprite, EquipmentSlotType slotType)
    {
        var rect = CreateRect(name, parent, Vector2.one * size, topLeft, BodySize);

        var frameImage = rect.gameObject.AddComponent<Image>();
        frameImage.sprite = frameSprite;
        frameImage.color = Color.white;
        frameImage.raycastTarget = true;

        // ---- 品质底板：外层品质色描边 + 内层深底（仅在格内有物品时显示） ----
        var qualityPlate = CreateCentered("QualityPlate", rect, Vector2.one * (size * QualityPlateRatio));
        var qualityBorderImage = qualityPlate.gameObject.AddComponent<Image>();
        qualityBorderImage.color = Color.white;
        qualityBorderImage.raycastTarget = false;

        var qualityFill = CreateStretch("Fill", qualityPlate);
        qualityFill.offsetMin = Vector2.one * UiTokens.RarityPlateBorderWidth;
        qualityFill.offsetMax = Vector2.one * -UiTokens.RarityPlateBorderWidth;
        var qualityFillImage = qualityFill.gameObject.AddComponent<Image>();
        qualityFillImage.color = Color.white;
        qualityFillImage.raycastTarget = false;

        qualityPlate.gameObject.SetActive(false);

        Image highlightImage = null;
        if (highlightSprite != null)
        {
            var highlight = CreateCentered("Highlight", rect, Vector2.one * size);
            highlightImage = highlight.gameObject.AddComponent<Image>();
            highlightImage.sprite = highlightSprite;
            highlightImage.color = Color.white;
            highlightImage.raycastTarget = false;
            highlightImage.enabled = false;
        }

        var icon = CreateCentered("Icon", rect, Vector2.one * (size - 30f));
        var iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.color = Color.white;
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;
        iconImage.enabled = false;

        var view = rect.gameObject.AddComponent<LoadoutSlotView>();
        var serialized = new SerializedObject(view);
        serialized.FindProperty("frame").objectReferenceValue = frameImage;
        serialized.FindProperty("highlight").objectReferenceValue = highlightImage;
        serialized.FindProperty("icon").objectReferenceValue = iconImage;
        serialized.FindProperty("qualityPlate").objectReferenceValue = qualityPlate.gameObject;
        serialized.FindProperty("qualityBorder").objectReferenceValue = qualityBorderImage;
        serialized.FindProperty("qualityFill").objectReferenceValue = qualityFillImage;
        serialized.FindProperty("equipmentSlot").enumValueIndex = (int)slotType;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    /// <summary>生成一个完全透明的点击区（叠在底图自带的按钮图形上）。</summary>
    private static Button CreateHitArea(string name, Transform parent, Vector2 topLeft, Vector2 size)
    {
        var rect = CreateRect(name, parent, size, topLeft, BodySize);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        return button;
    }

    /// <summary><paramref name="topLeft"/> 为「距设计区左上角」的偏移；anchoredPosition 以中心为原点，这里做一次换算。</summary>
    private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 topLeft, Vector2 canvas)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(
            topLeft.x + size.x * 0.5f - canvas.x * 0.5f,
            canvas.y * 0.5f - topLeft.y - size.y * 0.5f);
        return rect;
    }

    private static RectTransform CreateCentered(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
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

    private static TMP_Text CreateText(string name, Transform parent, Vector2 size, Vector2 topLeft, Vector2 canvas,
        TMP_FontAsset font, float fontSize)
    {
        var rect = CreateRect(name, parent, size, topLeft, canvas);
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

    /// <summary>生成一个描边 + 内填充的纯文字按钮（出售等）。</summary>
    private static Button CreateFlatTextButton(string name, Transform parent, Vector2 topLeft, Vector2 size,
        TMP_FontAsset font, string caption, Color borderColor, Color fillColor)
    {
        var rect = CreateRect(name, parent, size, topLeft, BodySize);
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

        var labelRect = CreateCentered("Label", innerRect, size);
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            label.font = font;

        label.text = caption;
        label.fontSize = 32f;
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
            Debug.LogError($"[LoadoutPanelBuilder] 找不到素材：{path}");

        return sprite;
    }

    /// <summary>复用主菜单已有的 TMP 字体（含中文字形），避免中文显示为方块。</summary>
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
