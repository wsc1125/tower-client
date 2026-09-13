using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class EquipmentSlotView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image frame;
    [SerializeField] private EquipmentSlotType slotType;

    private BagPanelController controller;
    private ItemDefinition item;

    public EquipmentSlotType SlotType => slotType;
    public bool IsEnabled => Inventory.IsSlotUsable(slotType);

    public void Bind(BagPanelController owner, EquipmentSlotType slot)
    {
        controller = owner;
        slotType = slot;
    }

    public void SetItem(ItemDefinition value)
    {
        item = value;
        if (icon == null)
            return;

        icon.sprite = value != null ? value.Icon : null;
        icon.enabled = value != null && value.Icon != null;
    }

    public bool TryGetDragSource(out BagDragSource source)
    {
        source = BagDragSource.FromEquipment(slotType, item);
        return IsEnabled && item != null;
    }

    public bool CanAccept(BagDragSource source, Inventory inventory)
    {
        return IsEnabled && inventory != null && source.Kind == BagLocationKind.Bag && source.Item != null &&
               source.Item.EquipSlotType == slotType && inventory.GetEquippedItem(slotType) == null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (TryGetDragSource(out var source))
            controller?.BeginDrag(source, icon);
    }

    public void OnDrag(PointerEventData eventData) => controller?.UpdateDrag(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => controller?.EndDrag();
    public void OnDrop(PointerEventData eventData) => controller?.Drop(BagDropTarget.Equipment(slotType));
    public void OnPointerEnter(PointerEventData eventData) => controller?.SetTargetHighlight(frame, BagDropTarget.Equipment(slotType), true);
    public void OnPointerExit(PointerEventData eventData) => controller?.SetTargetHighlight(frame, BagDropTarget.Equipment(slotType), false);
}
