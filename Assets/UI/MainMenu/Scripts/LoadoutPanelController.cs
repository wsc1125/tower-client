using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「装备配置」合并面板（试验区）：左侧仓库（分页网格），右侧顶部 5 个装备槽 + 下方携带背包（5×4），
/// 右下为物品详情区。支持点击选中、拖拽搬运（仓库 / 背包 / 装备槽之间任意互换）与出售。
/// 数据来自场景中唯一的 <see cref="InventoryHolder"/>。
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
    [SerializeField] private LoadoutSlotView[] carrySlots;
    [SerializeField] private LoadoutSlotView[] equipmentSlots;

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
    [SerializeField] private InventoryHolder holder;
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
    private LoadoutSection selectedSection = LoadoutSection.Warehouse;
    private int selectedIndex = -1;

    private LoadoutDragSource? activeDrag;
    private Image dimmedIcon;
    private Image sellButtonImage;
    private LoadoutSlotView highlightedSlot;

    /// <summary>当前读取的玩家库存；未接上 <see cref="InventoryHolder"/> 时为 null。</summary>
    public Inventory Model { get; private set; }

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    public LoadoutSection SelectedSection => selectedSection;

    /// <summary>当前选中的格号；语义为「对应分区内的索引」，未选中时为 -1。</summary>
    public int SelectedIndex => selectedIndex;

    /// <summary>当前仓库页，从 0 开始。</summary>
    public int CurrentPage { get; private set; }

    private int PageSlotCount => warehouseSlots != null && warehouseSlots.Length > 0 ? warehouseSlots.Length : 1;

    /// <summary>仓库总页数，至少为 1。</summary>
    public int TotalPages => Mathf.Max(1, Mathf.CeilToInt(Inventory.BagCapacity / (float)PageSlotCount));

    private void Awake()
    {
        ResolveModel();
        BindSlots();
        BindButtons();
        UpdateFitScale();

        // 面板默认隐藏：即使 prefab 里忘了关，也在首帧前收起来。
        if (modalRoot != null)
            modalRoot.SetActive(false);

        ClearSelection();
        Refresh();
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdateFitScale();
    }

    private void OnDestroy()
    {
        if (Model != null)
            Model.Changed -= Refresh;
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
        if (index < 0 || !IsSelectable(section, index))
        {
            ClearSelection();
        }
        else
        {
            selectedSection = section;
            selectedIndex = index;
        }

        RefreshDetail();
        RefreshSelectionVisual();
    }

    // ==================== 拖拽搬运 ====================

    public void BeginDrag(LoadoutDragSource source, Image sourceIcon)
    {
        if (source.Item == null)
            return;

        activeDrag = source;
        dimmedIcon = sourceIcon;
        if (dimmedIcon != null)
            dimmedIcon.color = new Color(1f, 1f, 1f, 0.35f);

        if (dragGhost != null)
        {
            dragGhost.sprite = source.Item.Icon;
            dragGhost.enabled = dragGhost.sprite != null;
            dragGhost.raycastTarget = false;
        }
    }

    public void UpdateDrag(Vector2 screenPosition)
    {
        if (activeDrag.HasValue && dragGhost != null)
            dragGhost.rectTransform.position = screenPosition;
    }

    /// <summary>拖拽能否落在目标格：装备槽要类型匹配且已启用，仓库格要在总容量内，不允许原地拖放。</summary>
    public bool CanDrop(LoadoutDragSource source, LoadoutDropTarget target)
    {
        if (Model == null || source.Item == null)
            return false;

        if (IsSameCell(source, target))
            return false;

        switch (target.Kind)
        {
            case LoadoutLocationKind.Equipment:
                return Inventory.IsSlotUsable(target.EquipmentSlot)
                    && source.Item.EquipSlotType == target.EquipmentSlot;
            case LoadoutLocationKind.Warehouse:
                return ToBagIndex(target.Index) >= 0;
            default:
                return target.Index >= 0 && target.Index < Inventory.CarryCapacity;
        }
    }

    /// <summary>执行搬运：仓库↔背包、仓库↔装备、背包↔装备、以及同区互换。</summary>
    public bool TryDrop(LoadoutDragSource source, LoadoutDropTarget target)
    {
        if (!CanDrop(source, target))
            return false;

        var sourceBag = source.Kind == LoadoutLocationKind.Warehouse ? ToBagIndex(source.Index) : -1;
        var targetBag = target.Kind == LoadoutLocationKind.Warehouse ? ToBagIndex(target.Index) : -1;

        switch (source.Kind)
        {
            case LoadoutLocationKind.Warehouse:
                switch (target.Kind)
                {
                    case LoadoutLocationKind.Warehouse:
                        return Model.TrySwapInBag(sourceBag, targetBag);
                    case LoadoutLocationKind.Carry:
                        return Model.TrySwapBagAndCarry(sourceBag, target.Index);
                    default:
                        return Model.TrySwapBagAndEquipment(sourceBag, target.EquipmentSlot);
                }
            case LoadoutLocationKind.Carry:
                switch (target.Kind)
                {
                    case LoadoutLocationKind.Warehouse:
                        return Model.TrySwapBagAndCarry(targetBag, source.Index);
                    case LoadoutLocationKind.Carry:
                        return Model.TrySwapInCarry(source.Index, target.Index);
                    default:
                        return Model.TrySwapCarryAndEquipment(source.Index, target.EquipmentSlot);
                }
            default:
                switch (target.Kind)
                {
                    case LoadoutLocationKind.Warehouse:
                        return Model.TrySwapBagAndEquipment(targetBag, source.EquipmentSlot);
                    case LoadoutLocationKind.Carry:
                        return Model.TrySwapCarryAndEquipment(target.Index, source.EquipmentSlot);
                    default:
                        return false;
                }
        }
    }

    public void Drop(LoadoutDropTarget target)
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
    public void SetTargetHighlight(LoadoutSlotView slot, LoadoutDropTarget target, bool highlighted)
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

    /// <summary>源与目标是同一格（原地拖回），不视为有效放置。</summary>
    private static bool IsSameCell(LoadoutDragSource source, LoadoutDropTarget target)
    {
        if (source.Kind != target.Kind)
            return false;

        if (source.Kind == LoadoutLocationKind.Equipment)
            return source.EquipmentSlot == target.EquipmentSlot;

        return source.Index == target.Index;
    }

    // ==================== 出售 ====================

    /// <summary>是否能出售当前选中项：装备槽上的物品不参与出售，避免误卖身上装备。</summary>
    public bool CanSellSelected()
    {
        if (Model == null || currency == null || selectedIndex < 0)
            return false;

        if (selectedSection == LoadoutSection.Equipment)
            return false;

        if (selectedSection == LoadoutSection.Warehouse && ToBagIndex(selectedIndex) < 0)
            return false;

        return GetSelectedItem() != null;
    }

    /// <summary>出售当前选中物品：扣物品、存档、到账。</summary>
    public void SellSelected()
    {
        if (!CanSellSelected())
            return;

        var item = GetSelectedItem();
        if (item == null)
            return;

        var removed = selectedSection == LoadoutSection.Warehouse
            ? Model.TryRemoveFromBag(ToBagIndex(selectedIndex))
            : Model.TryRemoveFromCarry(selectedIndex);

        if (!removed)
            return;

        // 先扣物品再立即存档，再入账，保证两侧都持久化。
        PlayerInventoryStore.Save(Model, holder != null ? holder.Database : null);
        currency.Add(item.Value);

        ClearSelection();
        Refresh();
    }

    // ==================== 内部 ====================

    /// <summary>选中格子是否有效：仓库要看该页格号是否落在容量内，装备槽要看是否已启用。</summary>
    private bool IsSelectable(LoadoutSection section, int index)
    {
        switch (section)
        {
            case LoadoutSection.Warehouse:
                return ToBagIndex(index) >= 0;
            case LoadoutSection.Carry:
                return index >= 0 && index < Inventory.CarryCapacity;
            case LoadoutSection.Equipment:
                return Inventory.IsSlotUsable(SlotTypeAt(index));
            default:
                return false;
        }
    }

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

    private void ResolveModel()
    {
        if (Model != null)
            return;

        var source = holder != null ? holder : FindObjectOfType<InventoryHolder>(true);
        if (source == null)
        {
            Debug.LogError("场景中没有 InventoryHolder，装备配置面板将无法读取玩家库存。", this);
            return;
        }

        holder = source;
        Model = source.Model;
        Model.Changed += Refresh;
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
        BindGroup(carrySlots, LoadoutSection.Carry);
        BindGroup(equipmentSlots, LoadoutSection.Equipment);
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
        selectedIndex = -1;
    }

    private void Refresh()
    {
        if (warehouseSlots != null)
        {
            for (var i = 0; i < warehouseSlots.Length; i++)
                warehouseSlots[i]?.SetItem(Model?.GetBagItem(ToBagIndex(i)));
        }

        if (carrySlots != null)
        {
            for (var i = 0; i < carrySlots.Length; i++)
                carrySlots[i]?.SetItem(Model?.GetCarryItem(i));
        }

        if (equipmentSlots != null)
        {
            foreach (var slot in equipmentSlots)
            {
                if (slot == null)
                    continue;

                slot.SetInteractable(Inventory.IsSlotUsable(slot.EquipmentSlot));
                slot.SetItem(Model?.GetEquippedItem(slot.EquipmentSlot));
            }
        }

        RefreshCounts();
        RefreshPager();
        RefreshDetail();

        if (!IsSelectable(selectedSection, selectedIndex))
            ClearSelection();

        RefreshSelectionVisual();
    }

    private void RefreshCounts()
    {
        if (warehouseCountText != null)
            warehouseCountText.text = Model == null
                ? "—"
                : CountWarehouse() + " / " + Inventory.BagCapacity;

        if (carryCountText != null)
            carryCountText.text = Model == null
                ? "—"
                : CountCarry() + " / " + Inventory.CarryCapacity;
    }

    private int CountWarehouse()
    {
        var count = 0;
        for (var i = 0; i < Inventory.BagCapacity; i++)
        {
            if (Model.GetBagItem(i) != null)
                count++;
        }

        return count;
    }

    private int CountCarry()
    {
        var count = 0;
        for (var i = 0; i < Inventory.CarryCapacity; i++)
        {
            if (Model.GetCarryItem(i) != null)
                count++;
        }

        return count;
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
        ApplySelection(carrySlots, LoadoutSection.Carry);
        ApplySelection(equipmentSlots, LoadoutSection.Equipment);
    }

    private void ApplySelection(LoadoutSlotView[] slots, LoadoutSection section)
    {
        if (slots == null)
            return;

        for (var i = 0; i < slots.Length; i++)
            slots[i]?.SetSelected(section == selectedSection && i == selectedIndex);
    }

    private void RefreshDetail()
    {
        var item = GetSelectedItem();
        var hasItem = item != null;

        if (detailIcon != null)
        {
            var hasIcon = hasItem && item.Icon != null;
            detailIcon.sprite = hasIcon ? item.Icon : null;
            detailIcon.enabled = hasIcon;
        }

        if (detailName != null)
            detailName.text = hasItem ? item.DisplayName : emptyNameText;

        if (detailMeta != null)
            detailMeta.text = hasItem ? FormatMeta(item) : emptyMetaText;

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

    /// <summary>详情区的第二行：装备槽类型 + 品质 + 价值。</summary>
    private static string FormatMeta(ItemDefinition item)
    {
        var rarity = UiTokens.RarityName(item.Rarity);
        var slot = FormatSlotType(item.EquipSlotType);
        return string.IsNullOrEmpty(slot)
            ? rarity + "   ·   价值 " + item.Value
            : slot + "   ·   " + rarity + "   ·   价值 " + item.Value;
    }

    private static string FormatSlotType(EquipmentSlotType slot)
    {
        switch (slot)
        {
            case EquipmentSlotType.MeleeWeapon:
                return "近战武器";
            case EquipmentSlotType.RangedWeapon:
                return "远程武器";
            case EquipmentSlotType.Helmet:
                return "头盔";
            case EquipmentSlotType.BodyArmor:
                return "护甲";
            case EquipmentSlotType.Boots:
                return "战靴";
            default:
                return string.Empty;
        }
    }

    private ItemDefinition GetSelectedItem()
    {
        if (Model == null || selectedIndex < 0)
            return null;

        switch (selectedSection)
        {
            case LoadoutSection.Warehouse:
                return Model.GetBagItem(ToBagIndex(selectedIndex));
            case LoadoutSection.Carry:
                return Model.GetCarryItem(selectedIndex);
            case LoadoutSection.Equipment:
                return Model.GetEquippedItem(SlotTypeAt(selectedIndex));
            default:
                return null;
        }
    }

    private EquipmentSlotType SlotTypeAt(int index)
    {
        if (equipmentSlots == null || index < 0 || index >= equipmentSlots.Length)
            return EquipmentSlotType.None;

        return equipmentSlots[index] != null ? equipmentSlots[index].EquipmentSlot : EquipmentSlotType.None;
    }

    /// <summary>页内格号换算为仓库真实索引；超出总容量的格子返回 -1。</summary>
    private int ToBagIndex(int slotIndex)
    {
        if (slotIndex < 0)
            return -1;

        var index = CurrentPage * PageSlotCount + slotIndex;
        return index < Inventory.BagCapacity ? index : -1;
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
