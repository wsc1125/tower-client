using UnityEngine;

/// <summary>一次使用的结算结果，供 UI 提示「恢复 40 血 / 修复 30 耐久」。</summary>
public readonly struct ConsumableUseResult
{
    public ConsumableUseResult(float healed, int repaired, int durabilitySpent)
    {
        Healed = healed;
        Repaired = repaired;
        DurabilitySpent = durabilitySpent;
    }

    /// <summary>实际恢复的血量。</summary>
    public float Healed { get; }

    /// <summary>实际修复的护甲耐久。</summary>
    public int Repaired { get; }

    /// <summary>本次从消耗品耐久池里扣掉的量。</summary>
    public int DurabilitySpent { get; }

    /// <summary>是否产生了实际效果。</summary>
    public bool HasEffect => Healed > 0f || Repaired > 0;
}

/// <summary>
/// 消耗品使用的唯一实现处。两条路径：
/// <list type="bullet">
/// <item>补血类（血包）：点背包详情区的「使用」按钮，一次把能回的都回。</item>
/// <item>修甲类（甲修）：拖到具体防具槽上，把那一件修满。</item>
/// </list>
///
/// 耐久语义：消耗品的耐久是「剩余可用量池」——
/// 血包耐久 = 还能回多少血，甲修耐久 = 还能修多少耐久。
/// 因此扣减量取「本次实际生效量」，只差 10 血就只扣 10，不会浪费整瓶。
/// 用掉即永久减少，<see cref="ItemInstance.AddDurability"/> 对消耗品直接返回 0。
/// </summary>
public static class ConsumableUseService
{
    /// <summary>
    /// 能否使用（补血路径）：是补血道具、目标未死亡、已脱战、且当前确实缺血。
    /// 满血时返回 false —— 避免玩家白吃一瓶。
    /// </summary>
    public static bool CanHeal(ItemInstance instance, IConsumableTarget target)
    {
        if (!IsUsable(instance, target))
            return false;

        if (!instance.Definition.IsHealing)
            return false;

        // 耐久池已空，或目标已满血，都视为「无效果」。
        return instance.CurrentDurability > 0 && target.MissingHealth() > 0f;
    }

    /// <summary>
    /// 能否把甲修拖到该防具槽上：是修甲道具、目标未死亡、已脱战、该槽有可修的防具。
    /// </summary>
    public static bool CanRepair(ItemInstance instance, IConsumableTarget target, EquipmentSlotType slot)
    {
        if (!IsUsable(instance, target))
            return false;

        if (!instance.Definition.IsRepairing)
            return false;

        if (!EquipmentSlots.IsArmorSlot(slot))
            return false;

        return instance.CurrentDurability > 0 && target.MissingDurabilityAt(slot) > 0;
    }

    /// <summary>
    /// 使用补血道具：一次把能回的都回（受限于血量上限与耐久池）。
    /// 只有真正回了血才扣耐久；扣减量 = 实际回血量。
    /// </summary>
    public static bool TryHeal(ItemInstance instance, IConsumableTarget target, out ConsumableUseResult result)
    {
        result = default;
        if (!CanHeal(instance, target))
            return false;

        // 单次上限 = min(道具能回的量, 耐久池剩余, 目标缺失量)。
        var wanted = Mathf.Min(instance.Definition.HealAmount, instance.CurrentDurability);
        var healed = target.Heal(wanted);
        if (healed <= 0f)
            return false;

        var spent = instance.ConsumeDurability(Mathf.CeilToInt(healed));
        result = new ConsumableUseResult(healed, 0, spent);
        return true;
    }

    /// <summary>
    /// 把甲修用在指定防具槽上：把那一件修满（受限于甲修耐久池）。
    /// 扣减量 = 实际修复量 × 该件品质的成本倍率（品质越高越费甲修）。
    /// </summary>
    public static bool TryRepair(ItemInstance instance, IConsumableTarget target, EquipmentSlotType slot,
        out ConsumableUseResult result)
    {
        result = default;
        if (!CanRepair(instance, target, slot))
            return false;

        // 甲修池是「可修复的耐久点数」，但高品质装备每点耐久更贵，
        // 因此先按成本倍率把池换算成「这件装备能修多少点」，再取缺失量。
        var costMultiplier = ItemRarityRules.RepairCostMultiplier(target.RarityAt(slot));
        var affordable = costMultiplier > 0f
            ? Mathf.FloorToInt(instance.CurrentDurability / costMultiplier)
            : instance.CurrentDurability;

        var wanted = Mathf.Min(instance.Definition.RepairAmount, affordable);
        if (wanted <= 0)
            return false;

        var repaired = target.RepairArmorSlot(slot, wanted);
        if (repaired <= 0)
            return false;

        // 按实际修复量 × 倍率扣池，向上取整避免「修了却几乎不扣」。
        var cost = Mathf.CeilToInt(repaired * costMultiplier);
        var spent = instance.ConsumeDurability(cost);
        result = new ConsumableUseResult(0f, repaired, spent);
        return true;
    }

    /// <summary>两条路径共用的前置校验：道具有效、目标存活、已脱战。</summary>
    private static bool IsUsable(ItemInstance instance, IConsumableTarget target)
    {
        if (instance == null || instance.Definition == null || target == null)
            return false;

        if (!instance.Definition.IsConsumable)
            return false;

        if (target.IsDead)
            return false;

        // 受击后一段时间内禁止使用 —— 参考「脱战才能打药」。
        return !target.IsInCombatLock;
    }
}
