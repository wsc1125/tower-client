using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 局内背包里的装备槽：底图（frame）+ 品质底板 + 物品图标。
/// 品质底板仅在槽内有物品时显示，用于盖住底图上的类型剪影，与局外装备配置面板同一套规则。
/// 支持点击选中（详情区据此刷新）与拖拽搬运。
/// </summary>
public sealed class EquipmentSlotView : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image frame;
    [SerializeField] private EquipmentSlotType slotType;

    [Header("品质底板")]
    [Tooltip("底板根节点；槽内有物品时显示。")]
    [SerializeField] private GameObject qualityPlate;
    [Tooltip("品质描边层，按品质着色。")]
    [SerializeField] private Image qualityBorder;
    [Tooltip("底板内层，深底与品质色的混合。")]
    [SerializeField] private Image qualityFill;

    private BagPanelController controller;
    private ItemDefinition item;
    private bool selected;

    public EquipmentSlotType SlotType => slotType;
    public bool IsEnabled => Inventory.IsSlotUsable(slotType);

    /// <summary>当前槽内的物品；空槽时为 null。</summary>
    public ItemDefinition Item => item;

    public void Bind(BagPanelController owner, EquipmentSlotType slot)
    {
        controller = owner;
        slotType = slot;
        SetSelected(false);
    }

    public void SetItem(ItemDefinition value)
    {
        item = value;
        ApplyQualityPlate(value);

        if (icon == null)
            return;

        icon.sprite = value != null ? value.Icon : null;
        icon.enabled = value != null && value.Icon != null;
    }

    /// <summary>有物品才显示品质底板；颜色按物品品质取。</summary>
    private void ApplyQualityPlate(ItemDefinition value)
    {
        if (qualityPlate == null)
            return;

        var hasItem = value != null;
        qualityPlate.SetActive(hasItem);

        if (!hasItem)
            return;

        var rarityColor = UiTokens.RarityColor(value.Rarity);
        if (qualityBorder != null)
            qualityBorder.color = rarityColor;

        if (qualityFill != null)
            qualityFill.color = Color.Lerp(UiTokens.RarityPlateBackground, rarityColor, UiTokens.RarityPlateTintAlpha);
    }

    public void SetSelected(bool value)
    {
        selected = value;
        ApplyVisual();
    }

    /// <summary>拖拽悬停反馈；离开时调用 false 会恢复选中 / 禁用态。</summary>
    public void SetDropHighlight(bool value)
    {
        if (frame == null)
            return;

        frame.color = value ? DropTint : NormalTint;
        if (!value)
            ApplyVisual();
    }

    public bool TryGetDragSource(out BagDragSource source)
    {
        source = BagDragSource.FromEquipment(slotType, item);
        return IsEnabled && item != null;
    }

    public bool CanAccept(BagDragSource source, Inventory inventory)
    {
        if (!IsEnabled || inventory == null || source.Item == null)
            return false;

        if (!Inventory.CanEquipToSlot(source.Item, slotType))
            return false;

        // 装备槽之间互换：两侧物品都要能放进对方槽位（武器可在主手 / 副手间对调）。
        if (source.Kind == BagLocationKind.Equipment)
        {
            if (source.EquipmentSlot == slotType)
                return false;

            var outgoing = inventory.GetEquippedItem(slotType);
            return outgoing == null || Inventory.CanEquipToSlot(outgoing, source.EquipmentSlot);
        }

        return inventory.GetEquippedItem(slotType) == null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        controller?.SelectEquipment(slotType);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (TryGetDragSource(out var source))
            controller?.BeginDrag(source, icon);
    }

    public void OnDrag(PointerEventData eventData) => controller?.UpdateDrag(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => controller?.EndDrag();
    public void OnDrop(PointerEventData eventData) => controller?.Drop(BagDropTarget.Equipment(slotType));
    public void OnPointerEnter(PointerEventData eventData) => controller?.SetTargetHighlight(this, BagDropTarget.Equipment(slotType), true);
    public void OnPointerExit(PointerEventData eventData) => controller?.SetTargetHighlight(this, BagDropTarget.Equipment(slotType), false);

    private void ApplyVisual()
    {
        if (frame == null)
            return;

        frame.color = !IsEnabled ? DisabledTint : (selected ? SelectedTint : NormalTint);
    }

    private static readonly Color NormalTint = Color.white;

    /// <summary>选中态着色：青蓝，与局外装备配置面板一致。</summary>
    private static readonly Color SelectedTint = new Color(0.55f, 0.92f, 1f, 1f);

    /// <summary>未启用的装备槽（如未解锁的副手）整体压暗。</summary>
    private static readonly Color DisabledTint = new Color(1f, 1f, 1f, 0.32f);

    /// <summary>拖拽悬停时的高亮色。刻意用暖金，与任何品质色都不重叠。</summary>
    private static readonly Color DropTint = new Color(1f, 0.82f, 0.42f, 1f);
}
