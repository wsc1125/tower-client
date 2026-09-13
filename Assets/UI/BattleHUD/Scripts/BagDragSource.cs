public enum BagLocationKind
{
    Bag,
    Equipment,
}

public readonly struct BagDragSource
{
    public BagLocationKind Kind { get; }
    public int BagIndex { get; }
    public EquipmentSlotType EquipmentSlot { get; }
    public ItemDefinition Item { get; }

    private BagDragSource(BagLocationKind kind, int bagIndex, EquipmentSlotType equipmentSlot, ItemDefinition item)
    {
        Kind = kind;
        BagIndex = bagIndex;
        EquipmentSlot = equipmentSlot;
        Item = item;
    }

    public static BagDragSource FromBag(int index, ItemDefinition item)
    {
        return new BagDragSource(BagLocationKind.Bag, index, EquipmentSlotType.None, item);
    }

    public static BagDragSource FromEquipment(EquipmentSlotType slot, ItemDefinition item)
    {
        return new BagDragSource(BagLocationKind.Equipment, -1, slot, item);
    }
}
