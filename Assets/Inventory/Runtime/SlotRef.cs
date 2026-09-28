using System;

/// <summary>
/// 物品所在位置的统一引用。取代旧实现里「一个 int Index + 一个 Kind 枚举」的隐式约定，
/// 让「仓库第 5 格」「携带第 2 格」「主手槽」在类型层面就是三种不同的东西，无法互相误传。
/// </summary>
public readonly struct SlotRef : IEquatable<SlotRef>
{
    public SlotArea Area { get; }

    /// <summary>格号。仅 <see cref="SlotArea.Warehouse"/> 与 <see cref="SlotArea.Carry"/> 有意义。</summary>
    public int Index { get; }

    /// <summary>装备槽。仅 <see cref="SlotArea.Equipment"/> 有意义。</summary>
    public EquipmentSlotType EquipmentSlot { get; }

    private SlotRef(SlotArea area, int index, EquipmentSlotType equipmentSlot)
    {
        Area = area;
        Index = index;
        EquipmentSlot = equipmentSlot;
    }

    public static SlotRef Warehouse(int index) => new SlotRef(SlotArea.Warehouse, index, EquipmentSlotType.None);

    public static SlotRef Carry(int index) => new SlotRef(SlotArea.Carry, index, EquipmentSlotType.None);

    public static SlotRef Equipment(EquipmentSlotType slot) => new SlotRef(SlotArea.Equipment, -1, slot);

    /// <summary>无效引用。用于「未选中」「拖拽落空」等场景，避免到处用 null 或 -1 表达。</summary>
    public static SlotRef None => new SlotRef(SlotArea.None, -1, EquipmentSlotType.None);

    public bool IsValid => Area != SlotArea.None;

    public bool Equals(SlotRef other)
    {
        if (Area != other.Area)
            return false;

        return Area == SlotArea.Equipment ? EquipmentSlot == other.EquipmentSlot : Index == other.Index;
    }

    public override bool Equals(object obj) => obj is SlotRef other && Equals(other);

    public override int GetHashCode()
    {
        return Area == SlotArea.Equipment
            ? ((int)Area * 397) ^ (int)EquipmentSlot
            : ((int)Area * 397) ^ Index;
    }

    public override string ToString()
    {
        switch (Area)
        {
            case SlotArea.Warehouse:
                return $"仓库[{Index}]";
            case SlotArea.Carry:
                return $"携带[{Index}]";
            case SlotArea.Equipment:
                return $"装备[{EquipmentSlot}]";
            default:
                return "无";
        }
    }
}

/// <summary>物品所在的分区。</summary>
public enum SlotArea
{
    None = 0,
    Warehouse = 1,
    Carry = 2,
    Equipment = 3,
}
