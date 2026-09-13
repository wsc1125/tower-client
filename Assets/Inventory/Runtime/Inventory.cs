using System;
using System.Collections.Generic;

public sealed class Inventory
{
    public const int BagCapacity = 20;

    private readonly ItemDefinition[] bag = new ItemDefinition[BagCapacity];
    private readonly Dictionary<EquipmentSlotType, ItemDefinition> equipped = new Dictionary<EquipmentSlotType, ItemDefinition>();

    public event Action Changed;

    public ItemDefinition GetBagItem(int index)
    {
        return IsBagIndex(index) ? bag[index] : null;
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

    private static bool IsBagIndex(int index)
    {
        return index >= 0 && index < BagCapacity;
    }
}
