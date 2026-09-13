using UnityEngine;
using UnityEngine.UI;

public sealed class BagPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelContent;
    [SerializeField] private BagItemView[] bagSlots;
    [SerializeField] private EquipmentSlotView[] equipmentSlots;
    [SerializeField] private Image dragGhost;
    [SerializeField] private ItemDefinition[] startingItems;

    private BagDragSource? activeDrag;
    private Image sourceIcon;
    private Image highlightedFrame;
    private readonly Color highlightColor = new Color(0.78f, 0.64f, 0.29f, 1f);

    public Inventory Model { get; private set; }
    public bool IsOpen => panelContent != null && panelContent.activeSelf;

    private void Awake()
    {
        EnsureModel();
        if (startingItems != null)
        {
            foreach (var item in startingItems)
                Model.TryAdd(item);
        }
        Refresh();
    }

    private void OnDestroy()
    {
        if (Model != null)
            Model.Changed -= Refresh;
    }

    public void Configure(GameObject content, BagItemView[] bags, EquipmentSlotView[] equipment, Image ghost, ItemDefinition[] initialItems)
    {
        panelContent = content;
        bagSlots = bags;
        equipmentSlots = equipment;
        dragGhost = ghost;
        startingItems = initialItems;
        EnsureModel();
        BindViews();
        Refresh();
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        if (panelContent != null)
            panelContent.SetActive(true);
    }

    public void Close()
    {
        EndDrag();
        if (panelContent != null)
            panelContent.SetActive(false);
    }

    public bool CanDrop(BagDragSource source, BagDropTarget target)
    {
        EnsureModel();
        if (target.Kind == BagLocationKind.Bag)
            return source.Kind == BagLocationKind.Bag || Model.GetBagItem(target.BagIndex) == null;

        return source.Kind == BagLocationKind.Bag && source.Item != null &&
               Inventory.IsSlotUsable(target.EquipmentSlot) &&
               source.Item.EquipSlotType == target.EquipmentSlot &&
               Model.GetEquippedItem(target.EquipmentSlot) == null;
    }

    public bool TryDrop(BagDragSource source, BagDropTarget target)
    {
        EnsureModel();
        if (!CanDrop(source, target))
            return false;

        if (source.Kind == BagLocationKind.Bag && target.Kind == BagLocationKind.Bag)
            return Model.TrySwapInBag(source.BagIndex, target.BagIndex);
        if (source.Kind == BagLocationKind.Bag && target.Kind == BagLocationKind.Equipment)
            return Model.TryEquipFromBag(source.BagIndex, target.EquipmentSlot);
        if (source.Kind == BagLocationKind.Equipment && target.Kind == BagLocationKind.Bag)
            return Model.TryUnequipToBag(source.EquipmentSlot, target.BagIndex);
        return false;
    }

    public void BeginDrag(BagDragSource source, Image icon)
    {
        activeDrag = source;
        sourceIcon = icon;
        if (sourceIcon != null)
            sourceIcon.color = new Color(1f, 1f, 1f, 0.4f);
        if (dragGhost != null)
        {
            dragGhost.sprite = source.Item != null ? source.Item.Icon : null;
            dragGhost.enabled = dragGhost.sprite != null;
            dragGhost.raycastTarget = false;
        }
    }

    public void UpdateDrag(Vector2 screenPosition)
    {
        if (activeDrag.HasValue && dragGhost != null)
            dragGhost.rectTransform.position = screenPosition;
    }

    public void Drop(BagDropTarget target)
    {
        if (activeDrag.HasValue)
            TryDrop(activeDrag.Value, target);
        EndDrag();
    }

    public void EndDrag()
    {
        if (sourceIcon != null)
            sourceIcon.color = Color.white;
        sourceIcon = null;
        activeDrag = null;
        if (dragGhost != null)
            dragGhost.enabled = false;
        if (highlightedFrame != null)
            highlightedFrame.color = Color.white;
        highlightedFrame = null;
    }

    public void SetTargetHighlight(Image frame, BagDropTarget target, bool highlighted)
    {
        if (frame == null)
            return;

        var canHighlight = highlighted && activeDrag.HasValue && CanDrop(activeDrag.Value, target);
        frame.color = canHighlight ? highlightColor : Color.white;
        if (canHighlight)
            highlightedFrame = frame;
        else if (highlightedFrame == frame)
            highlightedFrame = null;
    }

    private void EnsureModel()
    {
        if (Model != null)
            return;
        Model = new Inventory();
        Model.Changed += Refresh;
        BindViews();
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

    private void Refresh()
    {
        if (Model == null)
            return;
        if (bagSlots != null)
        {
            for (var i = 0; i < bagSlots.Length; i++)
                bagSlots[i]?.SetItem(Model.GetBagItem(i));
        }
        if (equipmentSlots != null)
        {
            foreach (var slot in equipmentSlots)
                slot?.SetItem(Model.GetEquippedItem(slot.SlotType));
        }
    }
}
