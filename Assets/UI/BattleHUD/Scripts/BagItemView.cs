using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class BagItemView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image frame;

    private BagPanelController controller;
    private ItemDefinition item;
    private int bagIndex;

    public int BagIndex => bagIndex;

    public void Bind(BagPanelController owner, int index)
    {
        controller = owner;
        bagIndex = index;
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
        source = BagDragSource.FromBag(bagIndex, item);
        return item != null;
    }

    public bool CanAccept(BagDragSource source, Inventory inventory)
    {
        if (inventory == null)
            return false;
        return source.Kind == BagLocationKind.Bag || inventory.GetBagItem(bagIndex) == null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (TryGetDragSource(out var source))
            controller?.BeginDrag(source, icon);
    }

    public void OnDrag(PointerEventData eventData) => controller?.UpdateDrag(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => controller?.EndDrag();
    public void OnDrop(PointerEventData eventData) => controller?.Drop(BagDropTarget.Bag(bagIndex));
    public void OnPointerEnter(PointerEventData eventData) => controller?.SetTargetHighlight(frame, BagDropTarget.Bag(bagIndex), true);
    public void OnPointerExit(PointerEventData eventData) => controller?.SetTargetHighlight(frame, BagDropTarget.Bag(bagIndex), false);
}
