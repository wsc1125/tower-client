using System;
using UnityEngine;

/// <summary>
/// 「当前使用的武器」的唯一来源：同一时间只有一把武器会显示，也只有它能攻击。
/// 当前武器只在两种情况下改变 —— 玩家点了「切换攻击」按钮，或当前武器被卸下后自动落到另一把。
/// 这里只是本地状态，不涉及数值结算；将来接联机时把 <see cref="Toggle"/> 改成发请求即可。
/// </summary>
public sealed class PlayerWeaponSelector : MonoBehaviour
{
    private static readonly EquipmentSlotType[] WeaponSlots =
    {
        EquipmentSlotType.MeleeWeapon,
        EquipmentSlotType.RangedWeapon,
    };

    [SerializeField] private InventoryHolder holder;
    [SerializeField] private AttackSwitchButton switchButton;

    private Inventory subscribed;
    private EquipmentSlotType? activeSlot;

    /// <summary>当前使用的武器槽位。首次访问时按「槽位顺序取第一把已装备的武器」解析。</summary>
    public EquipmentSlotType ActiveSlot => activeSlot ??= ResolveInitialSlot();

    /// <summary>当前武器发生变化时触发一次。</summary>
    public event Action Changed;

    public bool IsActive(EquipmentSlotType slot)
    {
        return slot == ActiveSlot;
    }

    /// <summary>切换攻击。另一把武器没装备时不做任何事（保持当前武器）。</summary>
    public void Toggle()
    {
        var other = Other(ActiveSlot);
        if (!IsEquipped(other))
            return;

        SetActive(other);
    }

    private static EquipmentSlotType Other(EquipmentSlotType slot)
    {
        return slot == EquipmentSlotType.MeleeWeapon
            ? EquipmentSlotType.RangedWeapon
            : EquipmentSlotType.MeleeWeapon;
    }

    private void OnEnable()
    {
        if (switchButton != null)
            switchButton.Clicked += Toggle;

        Subscribe();
    }

    private void OnDisable()
    {
        if (switchButton != null)
            switchButton.Clicked -= Toggle;

        Unsubscribe();
    }

    /// <summary>按槽位顺序取第一把已装备的武器；两把都没装备时结果为远程槽（此时手上不会显示任何武器）。</summary>
    private EquipmentSlotType ResolveInitialSlot()
    {
        foreach (var slot in WeaponSlots)
        {
            if (IsEquipped(slot))
                return slot;
        }

        return EquipmentSlotType.RangedWeapon;
    }

    /// <summary>
    /// 装备另一把武器不改变当前武器（只有点按钮才切）；
    /// 但当前武器被卸下时必须自动落到另一把，否则会出现「手上什么都没有、攻击键没反应」。
    /// </summary>
    private void HandleInventoryChanged()
    {
        if (IsEquipped(ActiveSlot))
            return;

        var other = Other(ActiveSlot);
        if (!IsEquipped(other))
            return;

        SetActive(other);
    }

    private void SetActive(EquipmentSlotType slot)
    {
        activeSlot = slot;
        Changed?.Invoke();
    }

    private bool IsEquipped(EquipmentSlotType slot)
    {
        return holder != null
            && Inventory.IsSlotUsable(slot)
            && holder.Model.GetEquippedItem(slot) != null;
    }

    private void Subscribe()
    {
        if (subscribed != null || holder == null)
            return;

        subscribed = holder.Model;
        subscribed.Changed += HandleInventoryChanged;
    }

    private void Unsubscribe()
    {
        if (subscribed == null)
            return;

        subscribed.Changed -= HandleInventoryChanged;
        subscribed = null;
    }
}
