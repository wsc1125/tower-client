using System;
using System.Collections.Generic;

public sealed class Inventory
{
    public const int BagCapacity = 72;

    /// <summary>出征携带背包的格数：进战前从仓库挑出来要带走的物品放在这里。</summary>
    public const int CarryCapacity = 20;

    private readonly ItemDefinition[] bag = new ItemDefinition[BagCapacity];
    private readonly ItemDefinition[] carry = new ItemDefinition[CarryCapacity];
    private readonly Dictionary<EquipmentSlotType, ItemDefinition> equipped = new Dictionary<EquipmentSlotType, ItemDefinition>();

    public event Action Changed;

    public ItemDefinition GetBagItem(int index)
    {
        return IsBagIndex(index) ? bag[index] : null;
    }

    public ItemDefinition GetCarryItem(int index)
    {
        return IsCarryIndex(index) ? carry[index] : null;
    }

    public ItemDefinition GetEquippedItem(EquipmentSlotType slot)
    {
        return equipped.TryGetValue(slot, out var item) ? item : null;
    }

    public static bool IsSlotUsable(EquipmentSlotType slot)
    {
        return slot == EquipmentSlotType.MeleeWeapon || slot == EquipmentSlotType.RangedWeapon;
    }

    public bool TryAdd(ItemDefinition item)
    {
        if (item == null)
            return false;

        for (var i = 0; i < bag.Length; i++)
        {
            if (bag[i] != null)
                continue;

            bag[i] = item;
            Changed?.Invoke();
            return true;
        }

        return false;
    }

    public bool TryEquipFromBag(int bagIndex, EquipmentSlotType targetSlot)
    {
        if (!IsBagIndex(bagIndex) || !IsSlotUsable(targetSlot))
            return false;

        var item = bag[bagIndex];
        if (item == null || item.EquipSlotType != targetSlot || GetEquippedItem(targetSlot) != null)
            return false;

        bag[bagIndex] = null;
        equipped[targetSlot] = item;
        Changed?.Invoke();
        return true;
    }

    public bool TryUnequipToBag(EquipmentSlotType slot, int bagIndex)
    {
        if (!IsSlotUsable(slot) || !IsBagIndex(bagIndex) || bag[bagIndex] != null)
            return false;

        var item = GetEquippedItem(slot);
        if (item == null)
            return false;

        equipped.Remove(slot);
        bag[bagIndex] = item;
        Changed?.Invoke();
        return true;
    }

    /// <summary>仓库（bag）→ 携带背包。目标格必须为空，否则失败。</summary>
    public bool TryMoveBagToCarry(int bagIndex, int carryIndex)
    {
        if (!IsBagIndex(bagIndex) || !IsCarryIndex(carryIndex))
            return false;

        var item = bag[bagIndex];
        if (item == null || carry[carryIndex] != null)
            return false;

        bag[bagIndex] = null;
        carry[carryIndex] = item;
        Changed?.Invoke();
        return true;
    }

    /// <summary>携带背包 → 仓库（bag）。目标格必须为空，否则失败。</summary>
    public bool TryMoveCarryToBag(int carryIndex, int bagIndex)
    {
        if (!IsCarryIndex(carryIndex) || !IsBagIndex(bagIndex))
            return false;

        var item = carry[carryIndex];
        if (item == null || bag[bagIndex] != null)
            return false;

        carry[carryIndex] = null;
        bag[bagIndex] = item;
        Changed?.Invoke();
        return true;
    }

    /// <summary>携带背包 → 装备槽。物品的目标槽必须匹配且该槽当前为空。</summary>
    public bool TryEquipFromCarry(int carryIndex, EquipmentSlotType targetSlot)
    {
        if (!IsCarryIndex(carryIndex) || !IsSlotUsable(targetSlot))
            return false;

        var item = carry[carryIndex];
        if (item == null || item.EquipSlotType != targetSlot || GetEquippedItem(targetSlot) != null)
            return false;

        carry[carryIndex] = null;
        equipped[targetSlot] = item;
        Changed?.Invoke();
        return true;
    }

    /// <summary>装备槽 → 携带背包。目标格必须为空，否则失败。</summary>
    public bool TryUnequipToCarry(EquipmentSlotType slot, int carryIndex)
    {
        if (!IsSlotUsable(slot) || !IsCarryIndex(carryIndex) || carry[carryIndex] != null)
            return false;

        var item = GetEquippedItem(slot);
        if (item == null)
            return false;

        equipped.Remove(slot);
        carry[carryIndex] = item;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 互换仓库与携带背包两格的内容（任意一格为空时等价于单向移动）。
    /// 拖拽搬运的统一入口，避免「目标格已占用就失败」的交互阻力。
    /// </summary>
    public bool TrySwapBagAndCarry(int bagIndex, int carryIndex)
    {
        if (!IsBagIndex(bagIndex) || !IsCarryIndex(carryIndex))
            return false;

        var bagItem = bag[bagIndex];
        var carryItem = carry[carryIndex];
        if (bagItem == null && carryItem == null)
            return false;

        bag[bagIndex] = carryItem;
        carry[carryIndex] = bagItem;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 互换仓库格与装备槽的内容（任意一侧为空时等价于装备 / 卸下）。
    /// 放入装备槽的物品必须匹配槽类型；被换下的装备回到原仓库格。
    /// </summary>
    public bool TrySwapBagAndEquipment(int bagIndex, EquipmentSlotType slot)
    {
        if (!IsBagIndex(bagIndex) || !IsSlotUsable(slot))
            return false;

        var incoming = bag[bagIndex];
        if (incoming != null && incoming.EquipSlotType != slot)
            return false;

        var outgoing = GetEquippedItem(slot);
        if (incoming == null && outgoing == null)
            return false;

        bag[bagIndex] = outgoing;
        if (incoming == null)
            equipped.Remove(slot);
        else
            equipped[slot] = incoming;

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 互换携带背包格与装备槽的内容（任意一侧为空时等价于装备 / 卸下）。
    /// 放入装备槽的物品必须匹配槽类型；被换下的装备回到原携带格。
    /// </summary>
    public bool TrySwapCarryAndEquipment(int carryIndex, EquipmentSlotType slot)
    {
        if (!IsCarryIndex(carryIndex) || !IsSlotUsable(slot))
            return false;

        var incoming = carry[carryIndex];
        if (incoming != null && incoming.EquipSlotType != slot)
            return false;

        var outgoing = GetEquippedItem(slot);
        if (incoming == null && outgoing == null)
            return false;

        carry[carryIndex] = outgoing;
        if (incoming == null)
            equipped.Remove(slot);
        else
            equipped[slot] = incoming;

        Changed?.Invoke();
        return true;
    }

    public bool TrySwapInBag(int firstIndex, int secondIndex)
    {
        if (!IsBagIndex(firstIndex) || !IsBagIndex(secondIndex))
            return false;

        var first = bag[firstIndex];
        bag[firstIndex] = bag[secondIndex];
        bag[secondIndex] = first;
        Changed?.Invoke();
        return true;
    }

    /// <summary>从背包指定索引移除一件物品（出售/丢弃等）。移除成功触发 <see cref="Changed"/>。</summary>
    public bool TryRemoveFromBag(int index)
    {
        if (!IsBagIndex(index) || bag[index] == null)
            return false;

        bag[index] = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>把物品放进出征携带背包的第一个空格（拾取、购买等）。成功触发 <see cref="Changed"/>。</summary>
    public bool TryAddToCarry(ItemDefinition item)
    {
        if (item == null)
            return false;

        for (var i = 0; i < carry.Length; i++)
        {
            if (carry[i] != null)
                continue;

            carry[i] = item;
            Changed?.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>从携带背包指定索引移除一件物品。移除成功触发 <see cref="Changed"/>。</summary>
    public bool TryRemoveFromCarry(int index)
    {
        if (!IsCarryIndex(index) || carry[index] == null)
            return false;

        carry[index] = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>交换携带背包内两格的位置。越界直接失败，不触发 <see cref="Changed"/>。</summary>
    public bool TrySwapInCarry(int firstIndex, int secondIndex)
    {
        if (!IsCarryIndex(firstIndex) || !IsCarryIndex(secondIndex))
            return false;

        var first = carry[firstIndex];
        carry[firstIndex] = carry[secondIndex];
        carry[secondIndex] = first;
        Changed?.Invoke();
        return true;
    }

    /// <summary>清空背包、携带与装备，供存档还原前重置使用；不触发 <see cref="Changed"/>。</summary>
    public void ClearAll()
    {
        for (var i = 0; i < bag.Length; i++)
            bag[i] = null;

        for (var i = 0; i < carry.Length; i++)
            carry[i] = null;

        equipped.Clear();
    }

    /// <summary>直接写入背包某一格（存档还原专用）；越界直接忽略，不触发 <see cref="Changed"/>。</summary>
    public void RestoreBagItem(int index, ItemDefinition item)
    {
        if (!IsBagIndex(index))
            return;

        bag[index] = item;
    }

    /// <summary>直接写入携带背包某一格（存档还原专用）；越界直接忽略，不触发 <see cref="Changed"/>。</summary>
    public void RestoreCarryItem(int index, ItemDefinition item)
    {
        if (!IsCarryIndex(index))
            return;

        carry[index] = item;
    }

    /// <summary>直接写入装备槽（存档还原专用）；不触发 <see cref="Changed"/>。</summary>
    public void RestoreEquippedItem(EquipmentSlotType slot, ItemDefinition item)
    {
        if (item == null)
            equipped.Remove(slot);
        else
            equipped[slot] = item;
    }

    /// <summary>手动触发 <see cref="Changed"/>，供批量还原结束后统一通知。</summary>
    public void NotifyChanged()
    {
        Changed?.Invoke();
    }

    private static bool IsBagIndex(int index)
    {
        return index >= 0 && index < BagCapacity;
    }

    private static bool IsCarryIndex(int index)
    {
        return index >= 0 && index < CarryCapacity;
    }
}
