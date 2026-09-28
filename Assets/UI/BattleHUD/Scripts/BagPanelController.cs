using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战斗内的背包面板：显示「携带」与局内装备槽，支持在两者之间拖拽。
/// 携带是出征时从局外带进来的物品 + 局内拾取的战利品，因此战斗里看到的、能用的就是这一份；
/// 仓库只在主菜单里管理，战斗内不展示。
/// 右侧为物品详情区：点击格子选中后显示图标、名称与品质 / 类型 / 价值 / 耐久，并可丢弃。
/// </summary>
public sealed class BagPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelContent;
    [SerializeField] private GameObject mask;
    [SerializeField] private BagItemView[] bagSlots;
    [SerializeField] private EquipmentSlotView[] equipmentSlots;
    [SerializeField] private Image dragGhost;

    [Tooltip("物品登记表。局内不读存档，但保留引用以便统一配置入口。")]
    [SerializeField] private ItemDatabase database;

    [Header("打开背包时隐藏的 HUD 元素")]
    [SerializeField] private GameObject statusPanel;
    [SerializeField] private GameObject topResourceBar;

    [Header("详情")]
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailMeta;
    [SerializeField] private Button useButton;
    [SerializeField] private Button discardButton;
    [SerializeField] private Color useDisabledColor = new Color(0.45f, 0.55f, 0.62f, 1f);
    [SerializeField] private Color discardDisabledColor = new Color(0.45f, 0.55f, 0.62f, 1f);
    [SerializeField] private string emptyNameText = "未选择";
    [SerializeField] private string emptyMetaText = "—";

    [Tooltip("消耗品的作用目标。留空则自动从场景里找实现了 IConsumableTarget 的组件。")]
    [SerializeField] private MonoBehaviour consumableTargetBehaviour;

    [Tooltip("地面掉落物生成器。留空则自动从场景里找实现了 IDroppedItemSpawner 的组件。")]
    [SerializeField] private MonoBehaviour droppedItemSpawnerBehaviour;

    [Tooltip("甲修蓄力时长（秒）。拖到防具槽上按住不放，进度条涨满才真正修复。")]
    [SerializeField] private float repairHoldDuration = 1f;

    private IConsumableTarget consumableTarget;
    private IDroppedItemSpawner droppedItemSpawner;

    private SlotRef? activeDrag;
    private Image sourceIcon;
    private BagItemView highlightedBagSlot;
    private EquipmentSlotView highlightedEquipmentSlot;
    private bool statusPanelWasActive = true;
    private bool topResourceBarWasActive = true;

    /// <summary>正在蓄力的修甲目标槽；未蓄力时为 None。</summary>
    private EquipmentSlotType repairHoldSlot = EquipmentSlotType.None;

    /// <summary>蓄力已进行的秒数。</summary>
    private float repairHoldElapsed;

    /// <summary>正在蓄力的甲修所在携带格；用于松手时校验物品没被换走。</summary>
    private int repairHoldBagIndex = -1;

    private SlotRef selection = SlotRef.None;
    private Image discardButtonImage;
    private Image useButtonImage;

    /// <summary>当前局内携带。</summary>
    public RaidBag Bag => GameSession.Raid;

    /// <summary>装备槽。与局外是同一份，因此局内换装会直接改到局外资产上。</summary>
    public PlayerLoadout Loadout => GameSession.Loadout;

    public bool IsOpen => panelContent != null && panelContent.activeSelf;

    /// <summary>当前选中的携带格号；未选中携带格时为 -1。</summary>
    public int SelectedBagIndex =>
        selection.IsValid && selection.Area == SlotArea.Carry ? selection.Index : -1;

    /// <summary>当前选中的装备槽；未选中装备槽时为 <see cref="EquipmentSlotType.None"/>。</summary>
    public EquipmentSlotType SelectedEquipmentSlot =>
        selection.IsValid && selection.Area == SlotArea.Equipment ? selection.EquipmentSlot : EquipmentSlotType.None;

    private void Awake()
    {
        GameSession.EnsureInitialized(database);
        BindDiscardButton();
        BindViews();
        Refresh();

        // 保险：无论 prefab 里 PanelContent 是什么状态，进战斗一律先收起背包。
        if (panelContent != null)
            panelContent.SetActive(false);
        if (mask != null)
            mask.SetActive(false);
    }

    private void OnEnable()
    {
        GameSession.RaidChanged += Refresh;
        // 装备槽与局外是同一份，局外变更（含受击磨损落盘）也要刷新耐久条。
        GameSession.LoadoutChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        GameSession.RaidChanged -= Refresh;
        GameSession.LoadoutChanged -= Refresh;
        CancelRepairHold();
    }

    /// <summary>
    /// 推进甲修蓄力。拖拽期间鼠标停在防具槽上即开始蓄力，涨满才真正修复；
    /// 中途移开或松手都会取消，不消耗甲修 —— 这是「防误用」的核心。
    /// </summary>
    private void Update()
    {
        if (repairHoldSlot == EquipmentSlotType.None)
            return;

        // 拖拽已结束（松手）→ 取消蓄力，不修复。
        if (!activeDrag.HasValue)
        {
            CancelRepairHold();
            return;
        }

        // 蓄力期间物品被换走 / 目标槽不再可修 → 取消。
        if (!CanRepairHoldContinue())
        {
            CancelRepairHold();
            return;
        }

        repairHoldElapsed += Time.unscaledDeltaTime;
        var progress = repairHoldDuration <= 0f ? 1f : Mathf.Clamp01(repairHoldElapsed / repairHoldDuration);
        FindEquipmentSlot(repairHoldSlot)?.SetRepairProgress(progress);

        if (progress >= 1f)
            CompleteRepairHold();
    }

    /// <summary>
    /// 拖拽悬停在防具槽上时开始 / 继续蓄力。由 <see cref="EquipmentSlotView"/> 的
    /// OnPointerEnter 转发 —— 这样「按住不放」的语义就是「指针停在槽上」。
    /// </summary>
    public void BeginRepairHoldIfRepairing(SlotRef target)
    {
        if (!activeDrag.HasValue || target.Area != SlotArea.Equipment)
            return;

        var from = activeDrag.Value;
        if (from.Area != SlotArea.Carry)
            return;

        var instance = Bag.GetCarry(from.Index);
        if (instance == null || instance.Definition == null || !instance.Definition.IsRepairing)
            return;

        if (!ConsumableUseService.CanRepair(instance, Target, target.EquipmentSlot))
            return;

        // 已经在同一个槽上蓄力就不重开，避免指针抖动把进度清零。
        if (repairHoldSlot == target.EquipmentSlot)
            return;

        BeginRepairHold(from.Index, target.EquipmentSlot);
    }

    /// <summary>指针离开防具槽时取消蓄力（未涨满则不修复）。</summary>
    public void CancelRepairHoldFor(SlotRef target)
    {
        if (target.Area == SlotArea.Equipment && repairHoldSlot == target.EquipmentSlot)
            CancelRepairHold();
    }

    public void Configure(GameObject content, GameObject maskObject, BagItemView[] bags,
        EquipmentSlotView[] equipment, Image ghost)
    {
        panelContent = content;
        mask = maskObject;
        bagSlots = bags;
        equipmentSlots = equipment;
        dragGhost = ghost;
        BindViews();
        BindDiscardButton();
        Refresh();
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        if (mask != null)
            mask.SetActive(true);
        if (panelContent != null)
            panelContent.SetActive(true);

        SetHudChromeVisible(false);
    }

    public void Close()
    {
        EndDrag();
        ClearSelection();
        if (panelContent != null)
            panelContent.SetActive(false);
        if (mask != null)
            mask.SetActive(false);

        SetHudChromeVisible(true);
    }

    private void SetHudChromeVisible(bool visible)
    {
        if (statusPanel != null)
        {
            if (!visible)
                statusPanelWasActive = statusPanel.activeSelf;

            statusPanel.SetActive(visible && statusPanelWasActive);
        }

        if (topResourceBar != null)
        {
            if (!visible)
                topResourceBarWasActive = topResourceBar.activeSelf;

            topResourceBar.SetActive(visible && topResourceBarWasActive);
        }
    }

    // ==================== 选中 ====================

    public void SelectBag(int index)
    {
        ToggleSelection(SlotRef.Carry(index));
    }

    public void SelectEquipment(EquipmentSlotType slot)
    {
        ToggleSelection(SlotRef.Equipment(slot));
    }

    public void ClearSelection()
    {
        selection = SlotRef.None;
        RefreshDetail();
        RefreshSelectionVisual();
    }

    /// <summary>当前选中的物品实例；未选中或格内为空时为 null。</summary>
    public ItemInstance GetSelectedItem()
    {
        if (!selection.IsValid)
            return null;

        return selection.Area == SlotArea.Carry
            ? Bag.GetCarry(selection.Index)
            : Loadout.Equipment.Get(selection.EquipmentSlot);
    }

    /// <summary>能否丢弃当前选中项：装备槽上的物品不参与丢弃，避免误丢身上装备。</summary>
    public bool CanDiscardSelected()
    {
        return selection.IsValid
            && selection.Area == SlotArea.Carry
            && Bag.GetCarry(selection.Index) != null;
    }

    /// <summary>
    /// 丢弃当前选中的携带物品：从携带格取出后放回地面成掉落物，可再次拾取。
    /// 物品实例原样带走（耐久不重置），因此丢出再捡回状态不变。
    /// </summary>
    public void DiscardSelected()
    {
        if (!CanDiscardSelected())
            return;

        // 先取出实例再移除 —— 移除后格子里就是 null 了，拿不到要丢的东西。
        var instance = Bag.GetCarry(selection.Index);
        if (!Bag.TryRemoveFromCarry(selection.Index))
            return;

        // 放回地面。生成器未接线时物品仍会被移除（与旧行为一致），只是地上看不到。
        Spawner?.Spawn(instance, DiscardPosition);

        ClearSelection();
        Refresh();
    }

    /// <summary>丢弃落点：玩家脚下。取不到玩家时退回世界原点。</summary>
    private Vector2 DiscardPosition
    {
        get
        {
            var player = Target as Component;
            return player != null ? (Vector2)player.transform.position : Vector2.zero;
        }
    }

    // ==================== 使用消耗品 ====================

    /// <summary>
    /// 消耗品的作用目标。未接线时从场景里找一次并缓存。
    /// 用接口而非具体类型，避免 BattleHUD 反向依赖 Gameplay 程序集。
    /// </summary>
    public IConsumableTarget Target
    {
        get
        {
            if (consumableTarget == null)
            {
                if (consumableTargetBehaviour != null)
                    consumableTarget = consumableTargetBehaviour as IConsumableTarget;

                if (consumableTarget == null)
                {
                    foreach (var candidate in FindObjectsOfType<MonoBehaviour>())
                    {
                        if (candidate is IConsumableTarget found)
                        {
                            consumableTarget = found;
                            break;
                        }
                    }
                }
            }

            return consumableTarget;
        }
    }

    /// <summary>
    /// 地面掉落物生成器。未接线时从场景里找一次并缓存。
    /// 同样用接口而非具体类型，避免 BattleHUD 反向依赖 Gameplay 程序集。
    /// </summary>
    public IDroppedItemSpawner Spawner
    {
        get
        {
            if (droppedItemSpawner == null)
            {
                if (droppedItemSpawnerBehaviour != null)
                    droppedItemSpawner = droppedItemSpawnerBehaviour as IDroppedItemSpawner;

                if (droppedItemSpawner == null)
                {
                    foreach (var candidate in FindObjectsOfType<MonoBehaviour>())
                    {
                        if (candidate is IDroppedItemSpawner found)
                        {
                            droppedItemSpawner = found;
                            break;
                        }
                    }
                }
            }

            return droppedItemSpawner;
        }
    }

    /// <summary>
    /// 能否使用当前选中项：必须是携带格里的补血道具，且目标未死亡、已脱战、当前确实缺血。
    /// 修甲类道具不走这里 —— 它靠拖到防具槽上使用。
    /// </summary>
    public bool CanUseSelected()
    {
        if (!selection.IsValid || selection.Area != SlotArea.Carry)
            return false;

        return ConsumableUseService.CanHeal(Bag.GetCarry(selection.Index), Target);
    }

    /// <summary>使用当前选中的补血道具。耐久按实际回血量扣减，用尽后保留空壳需手动丢弃。</summary>
    public void UseSelected()
    {
        if (!CanUseSelected())
            return;

        var instance = Bag.GetCarry(selection.Index);
        if (!ConsumableUseService.TryHeal(instance, Target, out var result))
            return;

        // 耐久变了，通知携带变更（背包格耐久条据此刷新）。
        Bag.NotifyChanged();
        Refresh();
    }

    // ==================== 拖拽搬运 ====================

    /// <summary>
    /// 拖拽能否落在目标格。
    /// 装备槽要类型匹配；携带格要为空或同区互换。
    /// 特例：甲修拖到防具槽上不是「装备」而是「使用」—— 只要该槽有可修的防具就允许。
    /// </summary>
    public bool CanDrop(SlotRef from, SlotRef to)
    {
        if (!from.IsValid || !to.IsValid || from.Equals(to))
            return false;

        var item = ItemAt(from);
        if (item == null)
            return false;

        if (to.Area == SlotArea.Equipment)
        {
            // 甲修拖到防具槽 = 使用（修那一件），而不是装备。
            if (item.Definition != null && item.Definition.IsRepairing)
                return ConsumableUseService.CanRepair(item, Target, to.EquipmentSlot);

            return EquipmentSlots.CanAccept(item, to.EquipmentSlot);
        }

        return to.Area == SlotArea.Carry;
    }

    /// <summary>
    /// 执行搬运：携带↔装备、以及同区互换。
    /// 甲修拖到防具槽上时改为「蓄力修复」—— 蓄力由悬停推进（见 <see cref="BeginRepairHoldIfRepairing"/>），
    /// 涨满才真正修复，因此这里既不搬运物品也不结算。
    /// </summary>
    public bool TryDrop(SlotRef from, SlotRef to)
    {
        if (!CanDrop(from, to))
            return false;

        // 甲修拖到防具槽 = 蓄力修复：不搬运物品，也不在这里结算。
        if (to.Area == SlotArea.Equipment && from.Area == SlotArea.Carry)
        {
            var instance = Bag.GetCarry(from.Index);
            if (instance != null && instance.Definition != null && instance.Definition.IsRepairing)
                return true;
        }

        return LoadoutService.Move(Bag, Loadout, from, to);
    }

    public void BeginDrag(SlotRef source, Image icon)
    {
        if (!source.IsValid)
            return;

        activeDrag = source;
        sourceIcon = icon;
        if (sourceIcon != null)
            sourceIcon.color = new Color(1f, 1f, 1f, 0.4f);

        if (dragGhost != null)
        {
            var instance = ItemAt(source);
            var definition = instance != null ? instance.Definition : null;
            dragGhost.sprite = definition != null ? definition.Icon : null;
            dragGhost.enabled = dragGhost.sprite != null;
            dragGhost.raycastTarget = false;
        }
    }

    public void UpdateDrag(Vector2 screenPosition)
    {
        if (activeDrag.HasValue && dragGhost != null)
            dragGhost.rectTransform.position = screenPosition;
    }

    public void EndDrag()
    {
        if (sourceIcon != null)
            sourceIcon.color = Color.white;

        sourceIcon = null;
        activeDrag = null;

        if (dragGhost != null)
            dragGhost.enabled = false;

        // 松手即取消未完成的蓄力 —— 只有涨满才会真正修复。
        CancelRepairHold();

        ClearHighlights();
    }

    public void Drop(SlotRef target)
    {
        if (!activeDrag.HasValue)
            return;

        var from = activeDrag.Value;
        EndDrag();

        if (TryDrop(from, target))
            Refresh();
    }

    public void SetTargetHighlight(BagItemView slot, SlotRef target, bool highlighted)
    {
        if (slot == null)
            return;

        var canHighlight = highlighted && activeDrag.HasValue && CanDrop(activeDrag.Value, target);
        slot.SetDropHighlight(canHighlight);

        if (canHighlight)
            highlightedBagSlot = slot;
        else if (highlightedBagSlot == slot)
            highlightedBagSlot = null;
    }

    public void SetTargetHighlight(EquipmentSlotView slot, SlotRef target, bool highlighted)
    {
        if (slot == null)
            return;

        var canHighlight = highlighted && activeDrag.HasValue && CanDrop(activeDrag.Value, target);
        slot.SetDropHighlight(canHighlight);

        if (canHighlight)
            highlightedEquipmentSlot = slot;
        else if (highlightedEquipmentSlot == slot)
            highlightedEquipmentSlot = null;
    }

    // ==================== 甲修蓄力 ====================

    /// <summary>
    /// 开始蓄力修复：记录甲修所在格与目标防具槽，进度条从 0 起。
    /// 真正修复发生在 <see cref="Update"/> 里进度涨满时。
    /// </summary>
    private void BeginRepairHold(int bagIndex, EquipmentSlotType slot)
    {
        CancelRepairHold();

        repairHoldBagIndex = bagIndex;
        repairHoldSlot = slot;
        repairHoldElapsed = 0f;

        FindEquipmentSlot(slot)?.SetRepairProgress(0f);
    }

    /// <summary>蓄力是否还能继续：甲修仍在原格、目标槽仍可修。</summary>
    private bool CanRepairHoldContinue()
    {
        if (repairHoldBagIndex < 0 || repairHoldSlot == EquipmentSlotType.None)
            return false;

        var instance = Bag.GetCarry(repairHoldBagIndex);
        return ConsumableUseService.CanRepair(instance, Target, repairHoldSlot);
    }

    /// <summary>进度涨满：真正结算修复，然后收起进度条。</summary>
    private void CompleteRepairHold()
    {
        var bagIndex = repairHoldBagIndex;
        var slot = repairHoldSlot;

        // 先清状态，避免结算过程中触发的事件重入。
        repairHoldSlot = EquipmentSlotType.None;
        repairHoldBagIndex = -1;
        repairHoldElapsed = 0f;

        var instance = Bag.GetCarry(bagIndex);
        if (ConsumableUseService.TryRepair(instance, Target, slot, out _))
        {
            Bag.NotifyChanged();
            Refresh();
        }

        FindEquipmentSlot(slot)?.SetRepairProgress(null);
    }

    /// <summary>取消蓄力：收起进度条，不消耗甲修。</summary>
    private void CancelRepairHold()
    {
        if (repairHoldSlot == EquipmentSlotType.None)
            return;

        var slot = repairHoldSlot;
        repairHoldSlot = EquipmentSlotType.None;
        repairHoldBagIndex = -1;
        repairHoldElapsed = 0f;

        FindEquipmentSlot(slot)?.SetRepairProgress(null);
    }

    private EquipmentSlotView FindEquipmentSlot(EquipmentSlotType slot)
    {
        if (equipmentSlots == null)
            return null;

        foreach (var view in equipmentSlots)
        {
            if (view != null && view.SlotType == slot)
                return view;
        }

        return null;
    }

    // ==================== 内部 ====================

    private void ToggleSelection(SlotRef target)
    {
        selection = selection.Equals(target) ? SlotRef.None : target;
        RefreshDetail();
        RefreshSelectionVisual();
    }

    private ItemInstance ItemAt(SlotRef slot)
    {
        if (!slot.IsValid)
            return null;

        return slot.Area == SlotArea.Carry
            ? Bag.GetCarry(slot.Index)
            : Loadout.Equipment.Get(slot.EquipmentSlot);
    }

    private void BindViews()
    {
        if (bagSlots != null)
        {
            for (var i = 0; i < bagSlots.Length; i++)
                bagSlots[i]?.Bind(this, i);
        }

        if (equipmentSlots != null)
        {
            foreach (var slot in equipmentSlots)
                slot?.Bind(this, slot.SlotType);
        }
    }

    private void BindDiscardButton()
    {
        if (useButton != null)
        {
            useButtonImage = useButton.targetGraphic as Image;
            if (useButtonImage == null)
                useButtonImage = useButton.GetComponent<Image>();

            useButton.onClick.RemoveListener(UseSelected);
            useButton.onClick.AddListener(UseSelected);
        }

        if (discardButton == null)
            return;

        discardButtonImage = discardButton.targetGraphic as Image;
        if (discardButtonImage == null)
            discardButtonImage = discardButton.GetComponent<Image>();

        discardButton.onClick.RemoveListener(DiscardSelected);
        discardButton.onClick.AddListener(DiscardSelected);
    }

    private void Refresh()
    {
        if (bagSlots != null)
        {
            for (var i = 0; i < bagSlots.Length; i++)
                bagSlots[i]?.SetItem(Bag.GetCarry(i));
        }

        if (equipmentSlots != null)
        {
            foreach (var slot in equipmentSlots)
            {
                var instance = Loadout.Equipment.Get(slot.SlotType);
                slot?.SetItem(instance);
                slot?.SetDurability(
                    instance != null ? instance.CurrentDurability : 0,
                    instance != null ? instance.MaximumDurability : 0);
            }
        }

        // 选中的物品可能已被拖走 / 丢弃，刷新后要重新校验。
        if (selection.IsValid && GetSelectedItem() == null)
            selection = SlotRef.None;

        RefreshDetail();
        RefreshSelectionVisual();
    }

    private void RefreshSelectionVisual()
    {
        if (bagSlots != null)
        {
            for (var i = 0; i < bagSlots.Length; i++)
                bagSlots[i]?.SetSelected(selection.IsValid && selection.Area == SlotArea.Carry && i == selection.Index);
        }

        if (equipmentSlots != null)
        {
            foreach (var slot in equipmentSlots)
            {
                if (slot == null)
                    continue;

                slot.SetSelected(selection.IsValid
                    && selection.Area == SlotArea.Equipment
                    && slot.SlotType == selection.EquipmentSlot);
            }
        }
    }

    private void RefreshDetail()
    {
        var instance = GetSelectedItem();
        var definition = instance != null ? instance.Definition : null;
        var hasItem = definition != null;

        if (detailIcon != null)
        {
            var hasIcon = hasItem && definition.Icon != null;
            detailIcon.sprite = hasIcon ? definition.Icon : null;
            detailIcon.enabled = hasIcon;
        }

        if (detailName != null)
            detailName.text = hasItem ? definition.DisplayName : emptyNameText;

        if (detailMeta != null)
            detailMeta.text = hasItem
                ? ItemMetaFormatter.Format(definition, instance.CurrentDurability)
                : emptyMetaText;

        RefreshDiscardButton();
        RefreshUseButton();
    }

    private void RefreshDiscardButton()
    {
        if (discardButton == null)
            return;

        var canDiscard = CanDiscardSelected();
        discardButton.interactable = canDiscard;

        if (discardButtonImage != null)
            discardButtonImage.color = canDiscard ? Color.white : discardDisabledColor;
    }

    /// <summary>
    /// 刷新「使用」按钮：只有补血道具且当前确实能回血时才可点。
    /// 修甲道具不显示可用态 —— 它靠拖到防具槽上使用。
    /// </summary>
    private void RefreshUseButton()
    {
        if (useButton == null)
            return;

        var canUse = CanUseSelected();
        useButton.interactable = canUse;

        if (useButtonImage != null)
            useButtonImage.color = canUse ? Color.white : useDisabledColor;
    }

    private void ClearHighlights()
    {
        highlightedBagSlot?.SetDropHighlight(false);
        highlightedEquipmentSlot?.SetDropHighlight(false);
        highlightedBagSlot = null;
        highlightedEquipmentSlot = null;
    }
}
