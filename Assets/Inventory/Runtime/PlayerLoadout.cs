using System;

/// <summary>
/// 局外装备配置：仓库（长期存放）+ 装备槽（出征时穿在身上的）。
/// 这是玩家「资产」的唯一归属地 —— 局内拾取的战利品只有在撤离成功后才并入仓库，
/// 死亡不会动到这里，因此仓库是安全的。
///
/// 容器里存的是 <see cref="ItemInstance"/>（物品实例），不是 <see cref="ItemDefinition"/>（模板）：
/// 同款装备可以有多件，各自带自己的耐久。
/// </summary>
public sealed class PlayerLoadout
{
    /// <summary>仓库格数。</summary>
    public const int WarehouseCapacity = 72;

    private readonly ItemInstance[] warehouse = new ItemInstance[WarehouseCapacity];

    /// <summary>装备槽。局内外共用同一份，出征时不搬运、不复制。</summary>
    public EquipmentSlots Equipment { get; } = new EquipmentSlots();

    /// <summary>仓库或装备槽发生变化时触发。</summary>
    public event Action Changed;

    public ItemInstance GetWarehouse(int index)
    {
        return IsWarehouseIndex(index) ? warehouse[index] : null;
    }

    public void SetWarehouse(int index, ItemInstance item)
    {
        if (IsWarehouseIndex(index))
            warehouse[index] = item;
    }

    /// <summary>仓库已用格数。</summary>
    public int WarehouseCount()
    {
        var count = 0;
        for (var i = 0; i < warehouse.Length; i++)
        {
            if (warehouse[i] != null)
                count++;
        }

        return count;
    }

    /// <summary>把物品放进仓库第一个空格。仓库满时返回 false。</summary>
    public bool TryAddToWarehouse(ItemInstance item)
    {
        if (item == null)
            return false;

        for (var i = 0; i < warehouse.Length; i++)
        {
            if (warehouse[i] != null)
                continue;

            warehouse[i] = item;
            NotifyChanged();
            return true;
        }

        return false;
    }

    /// <summary>移除仓库某一格的物品（出售 / 丢弃）。</summary>
    public bool TryRemoveFromWarehouse(int index)
    {
        if (!IsWarehouseIndex(index) || warehouse[index] == null)
            return false;

        warehouse[index] = null;
        NotifyChanged();
        return true;
    }

    /// <summary>交换仓库内两格。越界或两格都为空时失败。</summary>
    public bool TrySwapInWarehouse(int first, int second)
    {
        if (!IsWarehouseIndex(first) || !IsWarehouseIndex(second))
            return false;

        if (warehouse[first] == null && warehouse[second] == null)
            return false;

        var temp = warehouse[first];
        warehouse[first] = warehouse[second];
        warehouse[second] = temp;
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// 交换仓库格与装备槽（任意一侧为空时等价于装备 / 卸下）。
    /// 放进装备槽的物品必须匹配槽类型；被换下的装备回到原仓库格。
    /// </summary>
    public bool TrySwapWarehouseAndEquipment(int warehouseIndex, EquipmentSlotType slot)
    {
        if (!IsWarehouseIndex(warehouseIndex) || !EquipmentSlots.IsUsable(slot))
            return false;

        var incoming = warehouse[warehouseIndex];
        if (incoming != null && !EquipmentSlots.CanAccept(incoming, slot))
            return false;

        var outgoing = Equipment.Get(slot);
        if (incoming == null && outgoing == null)
            return false;

        warehouse[warehouseIndex] = outgoing;
        Equipment.Set(slot, incoming);
        NotifyChanged();
        return true;
    }

    /// <summary>
    /// 交换两个装备槽（主手 ↔ 副手等）。两侧物品都必须能放进对方槽位，
    /// 因此武器可在两个武器槽之间自由对调，防具只能与同类型槽互换。
    /// </summary>
    public bool TrySwapEquipment(EquipmentSlotType first, EquipmentSlotType second)
    {
        if (!EquipmentSlots.IsUsable(first) || !EquipmentSlots.IsUsable(second) || first == second)
            return false;

        var firstItem = Equipment.Get(first);
        var secondItem = Equipment.Get(second);
        if (firstItem == null && secondItem == null)
            return false;

        if (firstItem != null && !EquipmentSlots.CanAccept(firstItem, second))
            return false;

        if (secondItem != null && !EquipmentSlots.CanAccept(secondItem, first))
            return false;

        Equipment.Set(first, secondItem);
        Equipment.Set(second, firstItem);
        NotifyChanged();
        return true;
    }

    /// <summary>清空仓库与装备槽。仅用于「重新开始」等显式重置，死亡不走这里。</summary>
    public void ClearAll()
    {
        for (var i = 0; i < warehouse.Length; i++)
            warehouse[i] = null;

        Equipment.Clear();
    }

    /// <summary>手动触发变更通知，供批量还原结束后统一通知。</summary>
    public void NotifyChanged() => Changed?.Invoke();

    public static bool IsWarehouseIndex(int index) => index >= 0 && index < WarehouseCapacity;
}
