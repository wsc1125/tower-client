using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 重建局内背包面板（BagPanel.prefab）：一张新底图打底，左侧上半 5 个装备槽 + 下半 5×4 背包格，
/// 右侧为物品详情区（图标 + 名称 + 品质 / 类型 / 价值）与「丢弃」按钮。
/// 格子沿用原有「背包格子 / 装备格」素材，品质底板与选中态与局外装备配置面板同一套规则。
/// 重建后把 BattleHUD.prefab 里 BagButton 的点击重新接到 BagPanelController.Toggle。
/// </summary>
public static class BagPanelBuilder
{
    private const string PanelPrefabPath = "Assets/UI/BattleHUD/Prefabs/BagPanel.prefab";
    private const string HudPrefabPath = "Assets/UI/BattleHUD/Prefabs/BattleHUD.prefab";
    private const string SpriteDir = "Assets/UI/BattleHUD/Sprites/";

    /// <summary>含中文字形的动态字体；与局外装备配置面板共用。</summary>
    private const string DynamicFontPath = "Assets/UI/MainMenu/Fonts/simhei SDF Dynamic.asset";

    /// <summary>背包按钮节点名；重建后需要重新接线。</summary>
    private const string BagButtonName = "BagButton";

    // ---- 底图：1671×941，按原始比例居中显示，绝不拉伸 ----
    private const string BackgroundSpriteName = "背包底图-新.png";

    /// <summary>底图原始像素尺寸；所有内容坐标都以它为基准换算，保证与底图内框严格对齐。</summary>
    private static readonly Vector2 BackgroundSize = new Vector2(1671f, 941f);

    // ---- 网格：背包格与装备槽尺寸 ----
    // 格子素材改用局外仓库的「仓库格.png」（原图 143×146，不透明区 123×128），
    // 按原图比例放大到 140，保证描边不被拉伸变形。
    private const float SlotSize = 140f;
    /// <summary>品质底板的边长占格子的比例。仅盖住内板，不压到外圈描边。</summary>
    private const float QualityPlateRatio = 0.76f;
    private const float SlotGap = 14f;

    private const int BagColumns = 5;
    private const int BagRows = 4;

    // ---- 底图内框实测坐标（以底图左上角为原点，单位=底图像素） ----
    // 由 背包底图-新.png 逐像素扫描得到：内框上边 y≈57、下边 y≈883；
    // 左框 x∈[47,1100]，右框 x∈[1104,1627]。
    private const float InnerTop = 57f;
    private const float InnerBottom = 883f;
    private const float LeftBoxLeft = 47f;
    private const float LeftBoxRight = 1100f;
    private const float RightBoxLeft = 1104f;
    private const float RightBoxRight = 1627f;

    /// <summary>装备槽行距内框顶部的距离。</summary>
    private const float EquipTop = 60f;

    /// <summary>背包格行距内框顶部的距离。取 224 使 4 行 140 格正好贴到内框下沿（224+602=826）。</summary>
    private const float BagTop = 224f;

    /// <summary>详情框内边距。</summary>
    private const float DetailPadding = 44f;

    /// <summary>装备槽从左到右的顺序：先武器（主手 / 副手），再防具（头盔 / 护甲 / 靴）。</summary>
    private static readonly EquipmentSlotType[] EquipOrder =
    {
        EquipmentSlotType.MainHand,
        EquipmentSlotType.OffHand,
        EquipmentSlotType.Helmet,
        EquipmentSlotType.BodyArmor,
        EquipmentSlotType.Boots,
    };

    /// <summary>与 <see cref="EquipOrder"/> 一一对应的槽位底图；复用局外装备配置面板的类型图标。</summary>
    private static readonly string[] EquipSprites =
    {
        "近战武器图标.png",
        "远程武器图标.png",
        "头盔图标.png",
        "护甲图标.png",
        "战靴图标.png",
    };

    /// <summary>局外装备配置面板的槽位素材目录。</summary>
    private const string PrototypeSpriteDir = "Assets/UI/MainMenu/Sprites/WarehousePrototype/";

    /// <summary>局外仓库面板的素材目录；背包格与仓库格保持同一套视觉。</summary>
    private const string WarehouseSpriteDir = "Assets/UI/MainMenu/Sprites/Warehouse/";

    private static float GridWidth(int columns)
    {
        return columns * SlotSize + (columns - 1) * SlotGap;
    }

    [MenuItem("Tools/局内背包/重建局内背包面板")]
    public static void Rebuild()
    {
        var font = LoadProjectFont();
        if (font == null)
            Debug.LogWarning("[BagPanelBuilder] 未找到 TMP 字体资源，面板文字可能无法显示中文。");

        var panelPrefab = BuildPanelPrefab(font);
        RewireBagButton(panelPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BagPanelBuilder] 局内背包面板重建完成。");
    }

    private static GameObject BuildPanelPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("BagPanel", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var controller = root.AddComponent<BagPanelController>();

        // ---- Mask：全屏半透明遮罩，点击空白处关闭背包；同时挡住底下的 HUD 交互 ----
        var mask = CreateStretch("Mask", root.transform);
        var maskImage = mask.gameObject.AddComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.55f);
        maskImage.raycastTarget = true;
        var maskButton = mask.gameObject.AddComponent<Button>();
        maskButton.targetGraphic = maskImage;
        maskButton.transition = Selectable.Transition.None;
        UnityEventTools.AddPersistentListener(maskButton.onClick, controller.Close);
        mask.gameObject.SetActive(false);

        // ---- PanelContent：整块面板的显隐载体（默认隐藏） ----
        // 尺寸锁定为底图原始像素尺寸，居中显示；绝不拉伸，避免底图变形。
        var content = CreateCentered("PanelContent", root.transform, BackgroundSize);
        var contentImage = content.gameObject.AddComponent<Image>();
        contentImage.sprite = LoadSprite(SpriteDir + BackgroundSpriteName);
        contentImage.color = Color.white;
        contentImage.type = Image.Type.Simple;
        contentImage.preserveAspect = true;
        contentImage.raycastTarget = true;

        // 必须默认隐藏：否则一进战斗背包就会全屏铺开，把整个游戏画面盖住。
        content.gameObject.SetActive(false);

        // 背包格与装备槽统一使用局外仓库的格子素材，保证局内外视觉一致。
        var bagFrame = LoadSprite(WarehouseSpriteDir + "仓库格.png");

        // ---- 左侧容器：LeftColumn（装备槽）/ RightColumn/Grid（背包格）----
        // 这两个节点本身不参与布局，只作为层级锚点，供测试与后续查找定位。
        var leftColumn = CreateStretch("LeftColumn", content);
        var rightColumn = CreateStretch("RightColumn", content);
        var grid = CreateStretch("Grid", rightColumn);

        // 装备槽与背包格同宽同间距，整体在左侧内框里水平居中，视觉上成一整块。
        var gridX0 = (LeftBoxLeft + LeftBoxRight) * 0.5f - GridWidth(BagColumns) * 0.5f;

        // ================= 左上：装备槽（一行 5 个，各用对应类型的槽位底图） =================
        var equipSlots = new EquipmentSlotView[EquipOrder.Length];
        for (var i = 0; i < EquipOrder.Length; i++)
        {
            var topLeft = new Vector2(gridX0 + i * (SlotSize + SlotGap), InnerTop + EquipTop);
            equipSlots[i] = CreateSlot<EquipmentSlotView>($"Slot_{EquipOrder[i]}", leftColumn, topLeft, SlotSize,
                LoadSprite(PrototypeSpriteDir + EquipSprites[i]), EquipOrder[i]);
        }

        // ================= 左下：背包格（5 × 4） =================
        var bagSlots = new BagItemView[BagColumns * BagRows];
        for (var row = 0; row < BagRows; row++)
        {
            for (var column = 0; column < BagColumns; column++)
            {
                var index = row * BagColumns + column;
                var topLeft = new Vector2(
                    gridX0 + column * (SlotSize + SlotGap),
                    InnerTop + BagTop + row * (SlotSize + SlotGap));

                bagSlots[index] = CreateSlot<BagItemView>($"BagSlot_{index + 1:00}", grid, topLeft, SlotSize,
                    bagFrame, EquipmentSlotType.None);
            }
        }

        // ================= 右侧：物品详情区（对齐底图右侧内框） =================
        var detailInnerLeft = RightBoxLeft + DetailPadding;
        var detailInnerWidth = (RightBoxRight - RightBoxLeft) - DetailPadding * 2f;
        var detailInnerTop = InnerTop + DetailPadding;
        var detailInnerBottom = InnerBottom - DetailPadding;

        // 详情图标：在右侧内框里横向居中，尺寸放大到 260。
        var detailIconSize = 260f;
        var detailIconLeft = (RightBoxLeft + RightBoxRight) * 0.5f - detailIconSize * 0.5f;
        var detailIcon = CreateRect("DetailIcon", content, new Vector2(detailIconSize, detailIconSize),
            new Vector2(detailIconLeft, detailInnerTop), BackgroundSize);
        var detailIconImage = detailIcon.gameObject.AddComponent<Image>();
        detailIconImage.color = Color.white;
        detailIconImage.raycastTarget = false;
        detailIconImage.preserveAspect = true;
        detailIconImage.enabled = false;

        var detailName = CreateText("DetailName", content, new Vector2(detailInnerWidth, 52f),
            new Vector2(detailInnerLeft, detailInnerTop + 300f), BackgroundSize, font, 38f);
        detailName.text = "未选择";
        detailName.color = UiTokens.TextPrimary;
        detailName.alignment = TextAlignmentOptions.Left;

        var detailMeta = CreateText("DetailMeta", content, new Vector2(detailInnerWidth, 40f),
            new Vector2(detailInnerLeft, detailInnerTop + 372f), BackgroundSize, font, 26f);
        detailMeta.text = "—";
        detailMeta.color = UiTokens.TextSecondary;
        detailMeta.alignment = TextAlignmentOptions.Left;

        // 丢弃按钮贴在右侧内框底部居中。
        var discardSize = new Vector2(240f, 96f);
        var discardButton = CreateSpriteButton("DiscardButton", content,
            new Vector2(RightBoxLeft + (RightBoxRight - RightBoxLeft - discardSize.x) * 0.5f,
                detailInnerBottom - discardSize.y),
            discardSize,
            LoadSprite(SpriteDir + "丢弃按钮-普通.png"), LoadSprite(SpriteDir + "丢弃按钮-按下.png"));

        // ---- 关闭按钮：透明热区，正好盖住底图右上角自带的 X 图标 ----
        // 底图右上角 X 图标实测中心 ≈ (1604, 58)（以底图左上角为原点）。
        var closeSize = new Vector2(96f, 96f);
        var closeButton = CreateTransparentButton("CloseButton", content,
            new Vector2(1604f - closeSize.x * 0.5f, 58f - closeSize.y * 0.5f),
            closeSize, BackgroundSize);

        // ---- 拖拽层：拖拽图标跟随指针 ----
        var dragLayer = CreateStretch("DragLayer", root.transform);
        var dragGhost = CreateCentered("DragGhost", dragLayer, new Vector2(110f, 110f));
        var dragGhostImage = dragGhost.gameObject.AddComponent<Image>();
        dragGhostImage.raycastTarget = false;
        dragGhostImage.preserveAspect = true;
        dragGhostImage.enabled = false;

        BindController(controller, content.gameObject, mask.gameObject, bagSlots, equipSlots,
            detailIconImage, detailName, detailMeta, discardButton, dragGhostImage);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>把 BattleHUD.prefab 里 BagButton 的点击重新接到新面板的 Toggle 上。</summary>
    private static void RewireBagButton(GameObject panelPrefab)
    {
        var contents = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        var saved = false;
        try
        {
            var old = contents.transform.Find("BagPanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(panelPrefab, contents.transform);
            instance.transform.SetAsLastSibling();

            var controller = instance.GetComponent<BagPanelController>();

            // 背包打开时要收起的 HUD 元素：状态栏（血/蓝条、徽章）与顶部资源条。
            var controllerSerialized = new SerializedObject(controller);
            controllerSerialized.FindProperty("statusPanel").objectReferenceValue =
                FindChild(contents.transform, "StatusPanel");
            controllerSerialized.FindProperty("topResourceBar").objectReferenceValue =
                FindChild(contents.transform, "TopResourceBar");
            controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // 关闭按钮：面板内右上角的透明热区，接到 Close。
            var closeButton = FindChild(instance.transform, "CloseButton");
            if (closeButton != null)
            {
                var close = closeButton.GetComponent<Button>();
                if (close != null)
                {
                    close.onClick = new Button.ButtonClickedEvent();
                    UnityEventTools.AddPersistentListener(close.onClick, controller.Close);
                    EditorUtility.SetDirty(close);
                }
            }
            else
            {
                Debug.LogWarning("[BagPanelBuilder] 未找到 CloseButton，背包关闭按钮未接线。");
            }

            var wired = false;
            foreach (var button in contents.GetComponentsInChildren<Button>(true))
            {
                if (button.name != BagButtonName)
                    continue;

                button.onClick = new Button.ButtonClickedEvent();
                UnityEventTools.AddPersistentListener(button.onClick, controller.Toggle);
                EditorUtility.SetDirty(button);
                wired = true;
                break;
            }

            if (!wired)
                Debug.LogWarning($"[BagPanelBuilder] 未找到 {BagButtonName}，背包入口按钮未接线。");

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

    private static void BindController(
        BagPanelController controller,
        GameObject content,
        GameObject mask,
        BagItemView[] bagSlots,
        EquipmentSlotView[] equipSlots,
        Image detailIcon,
        TMP_Text detailName,
        TMP_Text detailMeta,
        Button discardButton,
        Image dragGhost)
    {
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("panelContent").objectReferenceValue = content;
        serialized.FindProperty("mask").objectReferenceValue = mask;
        AssignArray(serialized, "bagSlots", bagSlots);
        AssignArray(serialized, "equipmentSlots", equipSlots);
        serialized.FindProperty("dragGhost").objectReferenceValue = dragGhost;
        serialized.FindProperty("detailIcon").objectReferenceValue = detailIcon;
        serialized.FindProperty("detailName").objectReferenceValue = detailName;
        serialized.FindProperty("detailMeta").objectReferenceValue = detailMeta;
        serialized.FindProperty("discardButton").objectReferenceValue = discardButton;
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

    private static void AssignArray<T>(SerializedObject owner, string property, T[] values) where T : Object
    {
        var array = owner.FindProperty(property);
        array.arraySize = values.Length;
        for (var i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>生成一个格子：底图 + 品质底板 + 居中物品图标，并挂上对应的视图组件。</summary>
    private static T CreateSlot<T>(string name, Transform parent, Vector2 topLeft, float size,
        Sprite frameSprite, EquipmentSlotType slotType) where T : MonoBehaviour
    {
        var rect = CreateRect(name, parent, Vector2.one * size, topLeft, BackgroundSize);

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

        var icon = CreateCentered("Icon", rect, Vector2.one * (size - 30f));
        var iconImage = icon.gameObject.AddComponent<Image>();
        iconImage.color = Color.white;
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;
        iconImage.enabled = false;

        var view = rect.gameObject.AddComponent<T>();
        var serialized = new SerializedObject(view);
        serialized.FindProperty("frame").objectReferenceValue = frameImage;
        serialized.FindProperty("icon").objectReferenceValue = iconImage;
        serialized.FindProperty("qualityPlate").objectReferenceValue = qualityPlate.gameObject;
        serialized.FindProperty("qualityBorder").objectReferenceValue = qualityBorderImage;
        serialized.FindProperty("qualityFill").objectReferenceValue = qualityFillImage;

        if (view is EquipmentSlotView)
            serialized.FindProperty("slotType").enumValueIndex = (int)slotType;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    /// <summary>生成一个用两张素材做普通 / 按下状态的按钮（丢弃）。</summary>
    private static Button CreateSpriteButton(string name, Transform parent, Vector2 topLeft, Vector2 size,
        Sprite normalSprite, Sprite pressedSprite)
    {
        var rect = CreateRect(name, parent, size, topLeft, BackgroundSize);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = normalSprite;
        image.color = Color.white;
        image.raycastTarget = true;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.SpriteSwap;

        var swap = new SpriteState { pressedSprite = pressedSprite, selectedSprite = normalSprite };
        button.spriteState = swap;
        return button;
    }

    /// <summary>生成一个完全透明的按钮热区（用于盖住底图自带的图标，如右上角关闭 X）。</summary>
    private static Button CreateTransparentButton(string name, Transform parent, Vector2 topLeft, Vector2 size,
        Vector2 canvas)
    {
        var rect = CreateRect(name, parent, size, topLeft, canvas);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
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

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[BagPanelBuilder] 找不到素材：{path}");

        return sprite;
    }

    /// <summary>复用主菜单已有的 TMP 字体（含中文字形），避免中文显示为方块。</summary>
    private static TMP_FontAsset LoadProjectFont()
    {
        var dynamicFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (dynamicFont != null)
            return dynamicFont;

        return TMP_Settings.defaultFontAsset;
    }
}
