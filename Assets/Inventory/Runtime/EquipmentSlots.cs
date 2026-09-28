using System;
using System.Collections.Generic;

/// <summary>
/// 装备槽容器：主手 / 副手 / 头盔 / 胸甲 / 战靴。
/// 只负责「哪个槽里放了哪件物品实例」，不关心物品从哪来、要到哪去 —— 搬运逻辑在 <see cref="LoadoutService"/>。
/// </summary>
public sealed class EquipmentSlots
{
    /// <summary>武器槽，按「主手优先」顺序排列。遍历找武器时以本数组为准。</summary>
    public static readonly EquipmentSlotType[] WeaponSlots =
    {
        EquipmentSlotType.MainHand,
        EquipmentSlotType.OffHand,
    };

    /// <summary>防具槽，按「头盔 → 胸甲 → 战靴」顺序排列。护甲上限按本数组累加。</summary>
    public static readonly EquipmentSlotType[] ArmorSlots =
    {
        EquipmentSlotType.Helmet,
        EquipmentSlotType.BodyArmor,
        EquipmentSlotType.Boots,
    };

    /// <summary>全部可用槽位，顺序固定，供存档与 UI 遍历。</summary>
    public static readonly EquipmentSlotType[] AllSlots =
    {
        EquipmentSlotType.MainHand,
        EquipmentSlotType.OffHand,
        EquipmentSlotType.Helmet,
        EquipmentSlotType.BodyArmor,
        EquipmentSlotType.Boots,
    };

    private readonly Dictionary<EquipmentSlotType, ItemInstance> slots =
        new Dictionary<EquipmentSlotType, ItemInstance>();

    public ItemInstance Get(EquipmentSlotType slot)
    {
        return slots.TryGetValue(slot, out var item) ? item : null;
    }

    public void Set(EquipmentSlotType slot, ItemInstance item)
    {
        if (item == null)
            slots.Remove(slot);
        else
            slots[slot] = item;
    }

    public void Clear() => slots.Clear();

    /// <summary>该槽是否可用（主手 / 副手 / 三个防具槽）。</summary>
    public static bool IsUsable(EquipmentSlotType slot)
    {
        return slot == EquipmentSlotType.MainHand
            || slot == EquipmentSlotType.OffHand
            || IsArmorSlot(slot);
    }

    public static bool IsArmorSlot(EquipmentSlotType slot)
    {
        foreach (var candidate in ArmorSlots)
        {
            if (candidate == slot)
                return true;
        }

        return false;
    }

    public static bool IsWeaponSlot(EquipmentSlotType slot)
    {
        return slot == EquipmentSlotType.MainHand || slot == EquipmentSlotType.OffHand;
    }

    /// <summary>该物品是否是武器（可装备到主手或副手）。</summary>
    public static bool IsWeapon(ItemInstance item)
    {
        return item != null && item.Definition != null && IsWeaponSlot(item.Definition.EquipSlotType);
    }

    /// <summary>物品能否放进该槽：武器可放进任意武器槽，防具必须与槽类型一致。</summary>
    public static bool CanAccept(ItemInstance item, EquipmentSlotType slot)
    {
        if (item == null || item.Definition == null || !IsUsable(slot))
            return false;

        return IsWeaponSlot(slot) ? IsWeapon(item) : item.Definition.EquipSlotType == slot;
    }

    /// <summary>按「主手 → 副手」顺序返回第一把已装备武器所在的槽；没有则返回 None。</summary>
    public EquipmentSlotType FirstWeaponSlot()
    {
        foreach (var slot in WeaponSlots)
        {
            if (Get(slot) != null)
                return slot;
        }

        return EquipmentSlotType.None;
    }

    /// <summary>按「主手 → 副手」顺序返回另一把已装备武器所在的槽；没有则返回 None。</summary>
    public EquipmentSlotType OtherWeaponSlot(EquipmentSlotType slot)
    {
        foreach (var candidate in WeaponSlots)
        {
            if (candidate != slot && Get(candidate) != null)
                return candidate;
        }

        return EquipmentSlotType.None;
    }

    /// <summary>已装备防具提供的减伤率，按「1-(1-a)(1-b)(1-c)」递减叠加。未穿防具时为 0。</summary>
    public float TotalDamageReduction()
    {
        var remaining = 1f;
        foreach (var slot in ArmorSlots)
        {
            var item = Get(slot);
            if (item != null && item.Definition != null)
                remaining *= 1f - item.Definition.DamageReduction;
        }

        return 1f - remaining;
    }

    /// <summary>已装备防具的耐久上限总和。未穿防具时为 0。</summary>
    public int TotalDurability()
    {
        var total = 0;
        foreach (var slot in ArmorSlots)
        {
            var item = Get(slot);
            if (item != null)
                total += item.MaximumDurability;
        }

        return total;
    }
}
