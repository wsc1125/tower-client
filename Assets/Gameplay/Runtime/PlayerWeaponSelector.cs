using System;
using UnityEngine;

/// <summary>
/// 「当前使用的武器」的唯一来源：同一时间只有一把武器会显示，也只有它能攻击。
/// 主手与副手在逻辑上完全等价 —— 武器可以装备在任意一个武器槽里，
/// 这里只负责在「已装备的武器」之间切换，不关心它落在哪个槽。
/// 当前武器只在两种情况下改变 —— 玩家点了「切换攻击」按钮，或当前武器被卸下后自动落到另一把。
/// 这里只是本地状态，不涉及数值结算；将来接联机时把 <see cref="Toggle"/> 改成发请求即可。
/// </summary>
public sealed class PlayerWeaponSelector : MonoBehaviour
{
    [SerializeField] private AttackSwitchButton switchButton;

    private EquipmentSlotType? activeSlot;

    /// <summary>
    /// 当前使用的武器槽位。缓存值失效时（未装备 / 尚未解析）按「主手 → 副手」重新解析，
    /// 否则首次访问发生在装备之前时会永久停在 None，表现为「装了枪但手上没有」。
    /// </summary>
    public EquipmentSlotType ActiveSlot
    {
        get
        {
            if (activeSlot.HasValue && IsEquipped(activeSlot.Value))
                return activeSlot.Value;

            var resolved = ResolveInitialSlot();
            if (resolved != activeSlot)
                activeSlot = resolved;

            return resolved;
        }
    }

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
        if (other == EquipmentSlotType.None)
            return;

        SetActive(other);
    }

    /// <summary>另一把已装备武器所在的槽；没有则返回 <see cref="EquipmentSlotType.None"/>。</summary>
    private EquipmentSlotType Other(EquipmentSlotType slot)
    {
        return GameSession.LocalPlayer.Loadout.Equipment.OtherWeaponSlot(slot);
    }

    private void OnEnable()
    {
        if (switchButton != null)
            switchButton.Clicked += Toggle;

        GameSession.RaidChanged += HandleRaidChanged;
    }

    /// <summary>
    /// 运行时接线。联机动态生成的玩家来自 prefab，而 prefab 无法序列化场景内引用
    /// （切换按钮在场景 HUD 里），因此必须在生成后由 <c>PlayerNetworkSync</c> 补接。
    /// 不接线会导致「切武器按钮无反应」。
    /// </summary>
    public void BindRuntime(AttackSwitchButton button)
    {
        if (button == null || button == switchButton)
            return;

        // 先退订旧按钮，避免重复订阅导致一次点击触发两次切换。
        if (switchButton != null)
            switchButton.Clicked -= Toggle;

        switchButton = button;
        switchButton.Clicked += Toggle;
    }

    private void OnDisable()
    {
        if (switchButton != null)
            switchButton.Clicked -= Toggle;

        GameSession.RaidChanged -= HandleRaidChanged;
    }

    /// <summary>按「主手 → 副手」顺序取第一把已装备的武器；两把都没装备时结果为 None（此时手上不会显示任何武器）。</summary>
    private EquipmentSlotType ResolveInitialSlot()
    {
        return GameSession.LocalPlayer.Loadout.Equipment.FirstWeaponSlot();
    }

    /// <summary>
    /// 装备另一把武器不改变当前武器（只有点按钮才切）；
    /// 但当前武器被卸下时必须自动落到另一把，否则会出现「手上什么都没有、攻击键没反应」。
    /// 尚未解析过当前武器时不做任何事 —— 否则「先装副手、再装主手」会被这里抢先解析成主手。
    /// </summary>
    private void HandleRaidChanged()
    {
        if (!activeSlot.HasValue)
            return;

        if (IsEquipped(activeSlot.Value))
            return;

        var other = Other(activeSlot.Value);
        if (other == EquipmentSlotType.None)
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
        return slot != EquipmentSlotType.None && GameSession.LocalPlayer.Loadout.Equipment.Get(slot) != null;
    }
}
