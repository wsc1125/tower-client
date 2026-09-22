/// <summary>拖拽来源 / 目标的所在分区。</summary>
public enum LoadoutLocationKind
{
    Warehouse = 0,
    Carry = 1,
    Equipment = 2,
}

/// <summary>
/// 拖拽中的物品及其出处。<see cref="Index"/> 是该分区内的格号：
/// 仓库为「页内格号」，背包为携带背包索引，装备槽时为 -1。
/// </summary>
public readonly struct LoadoutDragSource
{
    public LoadoutLocationKind Kind { get; }
    public int Index { get; }
    public EquipmentSlotType EquipmentSlot { get; }
    public ItemDefinition Item { get; }

    private LoadoutDragSource(LoadoutLocationKind kind, int index, EquipmentSlotType slot, ItemDefinition item)
    {
        Kind = kind;
        Index = index;
        EquipmentSlot = slot;
        Item = item;
    }

    public static LoadoutDragSource FromWarehouse(int slotIndex, ItemDefinition item)
    {
        return new LoadoutDragSource(LoadoutLocationKind.Warehouse, slotIndex, EquipmentSlotType.None, item);
    }

    public static LoadoutDragSource FromCarry(int carryIndex, ItemDefinition item)
    {
        return new LoadoutDragSource(LoadoutLocationKind.Carry, carryIndex, EquipmentSlotType.None, item);
    }

    public static LoadoutDragSource FromEquipment(EquipmentSlotType slot, ItemDefinition item)
    {
        return new LoadoutDragSource(LoadoutLocationKind.Equipment, -1, slot, item);
    }
}

/// <summary>
/// 拖拽落点。<see cref="Index"/> 语义与 <see cref="LoadoutDragSource"/> 一致。
/// </summary>
public readonly struct LoadoutDropTarget
{
    public LoadoutLocationKind Kind { get; }
    public int Index { get; }
    public EquipmentSlotType EquipmentSlot { get; }

    private LoadoutDropTarget(LoadoutLocationKind kind, int index, EquipmentSlotType slot)
    {
        Kind = kind;
        Index = index;
        EquipmentSlot = slot;
    }

    public static LoadoutDropTarget Warehouse(int slotIndex)
    {
        return new LoadoutDropTarget(LoadoutLocationKind.Warehouse, slotIndex, EquipmentSlotType.None);
    }

    public static LoadoutDropTarget Carry(int carryIndex)
    {
        return new LoadoutDropTarget(LoadoutLocationKind.Carry, carryIndex, EquipmentSlotType.None);
    }

    public static LoadoutDropTarget Equipment(EquipmentSlotType slot)
    {
        return new LoadoutDropTarget(LoadoutLocationKind.Equipment, -1, slot);
    }
}
