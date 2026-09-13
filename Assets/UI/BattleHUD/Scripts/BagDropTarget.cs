public readonly struct BagDropTarget
{
    public BagLocationKind Kind { get; }
    public int BagIndex { get; }
    public EquipmentSlotType EquipmentSlot { get; }

    private BagDropTarget(BagLocationKind kind, int bagIndex, EquipmentSlotType equipmentSlot)
    {
        Kind = kind;
        BagIndex = bagIndex;
        EquipmentSlot = equipmentSlot;
    }

    public static BagDropTarget Bag(int index)
    {
        return new BagDropTarget(BagLocationKind.Bag, index, EquipmentSlotType.None);
    }

    public static BagDropTarget Equipment(EquipmentSlotType slot)
    {
        return new BagDropTarget(BagLocationKind.Equipment, -1, slot);
    }
}
