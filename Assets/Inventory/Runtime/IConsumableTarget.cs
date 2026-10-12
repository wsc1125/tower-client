/// <summary>
/// 消耗品的作用目标。由 <c>PlayerHealth2D</c> 实现。
///
/// 抽成接口是为了让 <see cref="ConsumableUseService"/> 留在 Inventory 程序集：
/// 局内背包（BattleHUD）与携带容器（Inventory）都要用它，而 Inventory 不能反向依赖 Gameplay。
/// </summary>
public interface ILocalConsumableTarget
{
    bool IsLocalConsumableTarget { get; }
}

public interface IRemoteHealingTarget
{
    bool RequiresRemoteHeal { get; }
    bool RequestHeal(float amount, System.Action<float> completed);
}

public interface IConsumableTarget
{
    /// <summary>已死亡时任何消耗品都不生效。</summary>
    bool IsDead { get; }

    /// <summary>当前缺多少血（上限 - 当前）。满血时返回 0。</summary>
    float MissingHealth();

    /// <summary>恢复血量，返回实际恢复量。已死亡或已满时返回 0。</summary>
    float Heal(float amount);

    /// <summary>
    /// 修复护甲耐久，返回实际修复量。按「最破损优先」逐件恢复。
    /// 未穿防具或全部满耐久时返回 0。
    /// </summary>
    int RepairArmor(int amount);

    /// <summary>
    /// 修复指定防具槽那一件的耐久，返回实际修复量。
    /// 该槽为空、非防具或已满时返回 0。
    /// </summary>
    int RepairArmorSlot(EquipmentSlotType slot, int amount);

    /// <summary>指定防具槽当前缺多少耐久（上限 - 当前）。该槽无防具时返回 0。</summary>
    int MissingDurabilityAt(EquipmentSlotType slot);

    /// <summary>
    /// 指定防具槽那件装备的品质。用于折算修甲成本（品质越高越费甲修）。
    /// 该槽无防具时返回 <see cref="ItemRarity.Common"/>。
    /// </summary>
    ItemRarity RarityAt(EquipmentSlotType slot);

    /// <summary>当前是否处于「受击后禁止使用消耗品」的窗口内。</summary>
    bool IsInCombatLock { get; }
}
