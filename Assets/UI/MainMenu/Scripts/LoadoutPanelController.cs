using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>装备配置面板的三个分区。</summary>
public enum LoadoutSection
{
    Warehouse = 0,
    Equipment = 1,
    Carry = 2,
}

/// <summary>
/// 「装备配置」面板：左侧仓库（分页网格），右侧装备槽，右下为物品详情区。
/// 支持点击选中、拖拽搬运（仓库 ↔ 装备槽任意互换）与出售。
/// 数据来自 <see cref="GameSession.Loadout"/> —— 局外资产，死亡不会丢失。
/// </summary>
public sealed class LoadoutPanelController : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalGroup;
    [SerializeField] private RectTransform panelBody;
    [SerializeField] private Button maskButton;
    [SerializeField] private Button closeButton;

    [Header("格子")]
    [Tooltip("仓库格子视图，数量等于每页格数。")]
    [SerializeField] private LoadoutSlotView[] warehouseSlots;
    [SerializeField] private LoadoutSlotView[] equipmentSlots;

    [Tooltip("携带背包格子视图。与仓库格共用同一套视图，但读写的是局内携带（RaidBag）。")]
    [SerializeField] private LoadoutSlotView[] carrySlots;

    [Header("计数")]
    [SerializeField] private TMP_Text warehouseCountText;
    [SerializeField] private TMP_Text carryCountText;

    [Header("分页")]
    [SerializeField] private Button prevPageButton;
    [SerializeField] private Button nextPageButton;
    [SerializeField] private TMP_Text pageLabel;

    [Header("详情")]
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailMeta;
    [SerializeField] private Button sellButton;
    [SerializeField] private Color sellDisabledColor = new Color(0.45f, 0.55f, 0.62f, 1f);
    [SerializeField] private string emptyNameText = "未选择";
    [SerializeField] private string emptyMetaText = "—";

    [Header("拖拽")]
    [SerializeField] private Image dragGhost;

    [Header("数据")]
    [SerializeField] private ItemDatabase database;
    [SerializeField] private PlayerCurrency currency;

    [Header("动画")]
    [SerializeField] private float openDuration = 0.15f;
    [SerializeField] private float closeDuration = 0.1f;
    [SerializeField] private float bodyStartScale = 1f;
    [SerializeField] private float bodyCloseScale = 1f;

    [Header("适配")]
    [Tooltip("面板设计尺寸；画布比它小时按比例整体缩小。")]
    [SerializeField] private Vector2 designSize = new Vector2(1920f, 1080f);

    private Coroutine animationRoutine;
    private float fitScale = 1f;
    private SlotRef selection = SlotRef.None;

    private SlotRef? activeDrag;
    private Image dimmedIcon;
    private Image sellButtonImage;
    private LoadoutSlotView highlightedSlot;

    /// <summary>当前读取的局外资产。</summary>
    public PlayerLoadout Loadout => GameSession.Loadout;

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    /// <summary>当前选中的分区；未选中时无意义。</summary>
    public LoadoutSection SelectedSection
    {
        get
        {
            if (!selection.IsValid)
                return LoadoutSection.Warehouse;

            switch (selection.Area)
            {
                case SlotArea.Equipment:
                    return LoadoutSection.Equipment;
                case SlotArea.Carry:
                    return LoadoutSection.Carry;
                default:
                    return LoadoutSection.Warehouse;
            }
        }
    }

    /// <summary>当前选中的格号；语义为「对应分区内的索引」，未选中时为 -1。</summary>
    public int SelectedIndex => selection.IsValid ? selection.Index : -1;

    /// <summary>当前仓库页，从 0 开始。</summary>
    public int CurrentPage { get; private set; }

    private int PageSlotCount => warehouseSlots != null && warehouseSlots.Length > 0 ? warehouseSlots.Length : 1;

    /// <summary>仓库总页数，至少为 1。</summary>
    public int TotalPages => Mathf.Max(1, Mathf.CeilToInt(PlayerLoadout.WarehouseCapacity / (float)PageSlotCount));

    private void Awake()
    {
        GameSession.EnsureInitialized(database);
        BindSlots();
        BindButtons();
        UpdateFitScale();

        // 面板默认隐藏：即使 prefab 里忘了关，也在首帧前收起来。
        if (modalRoot != null)
            modalRoot.SetActive(false);

        ClearSelection();
        Refresh();
    }

    private void OnEnable()
    {
        GameSession.LoadoutChanged += Refresh;
    }

    private void OnDisable()
    {
        GameSession.LoadoutChanged -= Refresh;
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdateFitScale();
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (modalRoot == null || IsOpen)
            return;

        CurrentPage = 0;
        ClearSelection();
        modalRoot.SetActive(true);
        PlayAnimation(open: true);
        Refresh();
    }

    public void Close()
    {
        if (modalRoot == null || !IsOpen)
            return;

        EndDrag();
        ClearSelection();
        PlayAnimation(open: false);
    }

    /// <summary>翻到指定页（从 0 开始）；越界会被夹到有效范围，切页后清空选中与详情。</summary>
    public void GoToPage(int page)
    {
        CurrentPage = Mathf.Clamp(page, 0, TotalPages - 1);
        ClearSelection();
        Refresh();
    }

    public void PrevPage()
    {
        GoToPage(CurrentPage - 1);
    }

    public void NextPage()
    {
        GoToPage(CurrentPage + 1);
    }

    /// <summary>选中某个分区里的某个格子并刷新详情；index 传 -1 表示清空选中。</summary>
    public void Select(LoadoutSection section, int index)
    {
        SlotRef target;
        switch (section)
        {
            case LoadoutSection.Equipment:
                target = SlotRef.Equipment(SlotTypeAt(index));
                break;
            case LoadoutSection.Carry:
                target = SlotRef.Carry(index);
                break;
            default:
                target = SlotRef.Warehouse(ToWarehouseIndex(index));
                break;
        }

        selection = target.IsValid && ItemAt(target) != null ? target : SlotRef.None;

        RefreshDetail();
        RefreshSelectionVisual();
    }

    // ==================== 拖拽搬运 ====================

    public void BeginDrag(SlotRef source, Image sourceIcon)
    {
        if (!source.IsValid || ItemAt(source) == null)
            return;

        activeDrag = source;
        dimmedIcon = sourceIcon;
        if (dimmedIcon != null)
            dimmedIcon.color = new Color(1f, 1f, 1f, 0.35f);

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

    /// <summary>拖拽能否落在目标格：装备槽要类型匹配，仓库格 / 携带格要在容量内，不允许原地拖放。</summary>
    public bool CanDrop(SlotRef from, SlotRef to)
    {
        if (!from.IsValid || !to.IsValid || from.Equals(to))
            return false;

        var item = ItemAt(from);
        if (item == null)
            return false;

        if (to.Area == SlotArea.Equipment)
            return EquipmentSlots.CanAccept(item, to.EquipmentSlot);

        if (to.Area == SlotArea.Carry)
            return RaidBag.IsCarryIndex(to.Index);

        return to.Area == SlotArea.Warehouse && to.Index >= 0;
    }

    /// <summary>执行搬运：仓库 ↔ 装备槽 ↔ 携带格，以及同区互换。</summary>
    public bool TryDrop(SlotRef from, SlotRef to)
    {
        if (!CanDrop(from, to))
            return false;

        // 局外是唯一允许「仓库 ↔ 携带」自由搬运的地方，因此走 MoveInLoadout。
        // 局内的 Move 重载会拒绝一切涉及仓库的搬运（携带只能在撤离时并入仓库）。
        return LoadoutService.MoveInLoadout(GameSession.Raid, Loadout, from, to);
    }

    public void Drop(SlotRef target)
    {
        if (activeDrag.HasValue)
            TryDrop(activeDrag.Value, target);

        EndDrag();
    }

    public void EndDrag()
    {
        if (dimmedIcon != null)
            dimmedIcon.color = Color.white;

        dimmedIcon = null;
        activeDrag = null;

        if (dragGhost != null)
            dragGhost.enabled = false;

        if (highlightedSlot != null)
        {
            highlightedSlot.SetDropHighlight(false);
            highlightedSlot = null;
        }
    }

    /// <summary>拖拽悬停时询问能否放下；只有可放下的格才亮起高亮。</summary>
    public void SetTargetHighlight(LoadoutSlotView slot, SlotRef target, bool highlighted)
    {
        if (slot == null)
            return;

        var canDrop = highlighted && activeDrag.HasValue && CanDrop(activeDrag.Value, target);
        slot.SetDropHighlight(canDrop);

        if (canDrop)
            highlightedSlot = slot;
        else if (highlightedSlot == slot)
            highlightedSlot = null;
    }

    // ==================== 出售 ====================

    /// <summary>是否能出售当前选中项：装备槽上的物品不参与出售，避免误卖身上装备。</summary>
    public bool CanSellSelected()
    {
        if (currency == null || !selection.IsValid || selection.Area != SlotArea.Warehouse)
            return false;

        return ItemAt(selection) != null;
    }

    /// <summary>出售当前选中物品：扣物品、到账。仓库变更由 GameSession 自动落盘。</summary>
    public void SellSelected()
    {
        if (!CanSellSelected())
            return;

        var instance = ItemAt(selection);
        if (instance == null || instance.Definition == null)
            return;

        if (!Loadout.TryRemoveFromWarehouse(selection.Index))
            return;

        currency.Add(instance.Definition.Value);

        ClearSelection();
        Refresh();
    }

    // ==================== 内部 ====================

    /// <summary>画布比设计尺寸小时整体等比缩小，保证面板始终完整可见。</summary>
    private void UpdateFitScale()
    {
        var rect = transform as RectTransform;
        if (rect == null || rect.rect.width <= 0f || rect.rect.height <= 0f
            || designSize.x <= 0f || designSize.y <= 0f)
        {
            fitScale = 1f;
        }
        else
        {
            fitScale = Mathf.Min(1f, rect.rect.width / designSize.x, rect.rect.height / designSize.y);
        }

        if (panelBody != null && animationRoutine == null)
            panelBody.localScale = Vector3.one * fitScale;
    }

    private void BindButtons()
    {
        if (maskButton != null)
            maskButton.onClick.AddListener(Close);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (prevPageButton != null)
            prevPageButton.onClick.AddListener(PrevPage);

        if (nextPageButton != null)
            nextPageButton.onClick.AddListener(NextPage);

        if (sellButton != null)
        {
            sellButtonImage = sellButton.targetGraphic as Image;
            if (sellButtonImage == null)
                sellButtonImage = sellButton.GetComponent<Image>();

            sellButton.onClick.AddListener(SellSelected);
        }
    }

    private void BindSlots()
    {
        BindGroup(warehouseSlots, LoadoutSection.Warehouse);
        BindGroup(equipmentSlots, LoadoutSection.Equipment);
        BindGroup(carrySlots, LoadoutSection.Carry);
    }

    private void BindGroup(LoadoutSlotView[] slots, LoadoutSection section)
    {
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
            slots[i]?.Bind(this, section, i);
    }

    private void ClearSelection()
    {
        selection = SlotRef.None;
    }

    private void Refresh()
    {
        if (warehouseSlots != null)
        {
            for (var i = 0; i < warehouseSlots.Length; i++)
            {
                if (warehouseSlots[i] == null)
                    continue;

                // 仓库分页复用同一批格子：必须把真实索引推给 View，
                // 否则它的 BuildSlotRef 仍指向上一页，拖拽 / 点击会串页。
                var warehouseIndex = ToWarehouseIndex(i);
                warehouseSlots[i].SetSlotIndex(warehouseIndex);
                warehouseSlots[i].SetItem(warehouseIndex >= 0 ? Loadout.GetWarehouse(warehouseIndex) : null);
            }
        }

        if (carrySlots != null)
        {
            for (var i = 0; i < carrySlots.Length; i++)
                carrySlots[i]?.SetItem(GameSession.Raid.GetCarry(i));
        }

        if (equipmentSlots != null)
        {
            foreach (var slot in equipmentSlots)
            {
                if (slot == null)
                    continue;

                slot.SetInteractable(EquipmentSlots.IsUsable(slot.EquipmentSlot));
                slot.SetItem(Loadout.Equipment.Get(slot.EquipmentSlot));
            }
        }

        RefreshCounts();
        RefreshPager();

        // 选中的物品可能已被拖走 / 卖掉，刷新后要重新校验。
        if (selection.IsValid && ItemAt(selection) == null)
            selection = SlotRef.None;

        RefreshDetail();
        RefreshSelectionVisual();
    }

    private void RefreshCounts()
    {
        if (warehouseCountText != null)
            warehouseCountText.text = Loadout.WarehouseCount() + " / " + PlayerLoadout.WarehouseCapacity;

        if (carryCountText != null)
            carryCountText.text = GameSession.Raid.CarryCount() + " / " + RaidBag.CarryCapacity;
    }

    private void RefreshPager()
    {
        if (prevPageButton != null)
            prevPageButton.interactable = CurrentPage > 0;

        if (nextPageButton != null)
            nextPageButton.interactable = CurrentPage < TotalPages - 1;

        if (pageLabel != null)
            pageLabel.text = (CurrentPage + 1) + " / " + TotalPages;
    }

    private void RefreshSelectionVisual()
    {
        ApplySelection(warehouseSlots, LoadoutSection.Warehouse);
        ApplySelection(equipmentSlots, LoadoutSection.Equipment);
        ApplySelection(carrySlots, LoadoutSection.Carry);
    }

    private void ApplySelection(LoadoutSlotView[] slots, LoadoutSection section)
    {
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
                continue;

            // 仓库格用 View 上的真实索引比较（页内格号 i 与 selection.Index 语义不同，
            // 直接比 i 会在翻页后把选中态画到错误的格子上）。
            var isSelected = selection.IsValid
                && SelectedSection == section
                && (section == LoadoutSection.Equipment
                    ? slots[i].EquipmentSlot == selection.EquipmentSlot
                    : slots[i].Index == selection.Index);

            slots[i].SetSelected(isSelected);
        }
    }

    private void RefreshDetail()
    {
        var instance = ItemAt(selection);
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

        RefreshSellButton();
    }

    private void RefreshSellButton()
    {
        if (sellButton == null)
            return;

        var canSell = CanSellSelected();
        sellButton.interactable = canSell;

        if (sellButtonImage != null)
            sellButtonImage.color = canSell ? Color.white : sellDisabledColor;
    }

    private ItemInstance ItemAt(SlotRef slot)
    {
        if (!slot.IsValid)
            return null;

        switch (slot.Area)
        {
            case SlotArea.Warehouse:
                return Loadout.GetWarehouse(slot.Index);
            case SlotArea.Carry:
                return GameSession.Raid.GetCarry(slot.Index);
            default:
                return Loadout.Equipment.Get(slot.EquipmentSlot);
        }
    }

    private EquipmentSlotType SlotTypeAt(int index)
    {
        if (equipmentSlots == null || index < 0 || index >= equipmentSlots.Length)
            return EquipmentSlotType.None;

        return equipmentSlots[index] != null ? equipmentSlots[index].EquipmentSlot : EquipmentSlotType.None;
    }

    /// <summary>页内格号换算为仓库真实索引；超出总容量的格子返回 -1。</summary>
    private int ToWarehouseIndex(int slotIndex)
    {
        if (slotIndex < 0)
            return -1;

        var index = CurrentPage * PageSlotCount + slotIndex;
        return index < PlayerLoadout.WarehouseCapacity ? index : -1;
    }

    private void PlayAnimation(bool open)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(Animate(open));
    }

    private IEnumerator Animate(bool open)
    {
        var duration = open ? openDuration : closeDuration;
        var fromAlpha = modalGroup != null ? modalGroup.alpha : 1f;
        var toAlpha = open ? 1f : 0f;
        var fromScale = panelBody != null ? panelBody.localScale.x : fitScale;
        var toScale = open ? fitScale : bodyCloseScale * fitScale;

        if (open)
        {
            fromAlpha = 0f;
            fromScale = bodyStartScale * fitScale;
            Apply(fromAlpha, fromScale);
        }

        if (duration <= 0f)
        {
            Apply(toAlpha, toScale);
            FinishAnimation(open);
            yield break;
        }

        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var progress = Mathf.Clamp01(elapsed / duration);

            // 打开用 ease-out（收尾变慢，读起来像「弹入」）；关闭用线性收得干脆。
            var eased = open ? 1f - (1f - progress) * (1f - progress) : progress;
            Apply(Mathf.Lerp(fromAlpha, toAlpha, eased), Mathf.Lerp(fromScale, toScale, eased));
            yield return null;
        }

        Apply(toAlpha, toScale);
        FinishAnimation(open);
    }

    private void Apply(float alpha, float scale)
    {
        if (modalGroup != null)
            modalGroup.alpha = alpha;

        if (panelBody != null)
            panelBody.localScale = Vector3.one * scale;
    }

    private void FinishAnimation(bool open)
    {
        animationRoutine = null;

        if (!open && modalRoot != null)
            modalRoot.SetActive(false);
    }
}
