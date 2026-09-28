using System;
using UnityEngine;

/// <summary>
/// 物品实例：一件具体的物品。
/// <see cref="Definition"/> 描述「这是什么」（类型 / 模板，同款装备共享），
/// <see cref="InstanceId"/> 区分「这是哪一件」（全局唯一，跨存档稳定）。
///
/// 实例级状态（当前只有耐久）挂在实例上，随物品在仓库 / 携带 / 装备槽之间移动而移动，
/// 因此搬运逻辑不需要额外同步任何状态 —— 搬引用即搬状态。
/// </summary>
public sealed class ItemInstance
{
    public ItemInstance(string instanceId, ItemDefinition definition)
    {
        InstanceId = string.IsNullOrEmpty(instanceId) ? NewId() : instanceId;
        Definition = definition;
        CurrentDurability = definition != null ? definition.Durability : 0;
    }

    /// <summary>用模板创建一个全新实例（满耐久）。</summary>
    public static ItemInstance Create(ItemDefinition definition)
    {
        return new ItemInstance(NewId(), definition);
    }

    /// <summary>全局唯一实例 ID。用 Guid 保证离线可生成、跨存档稳定。</summary>
    public string InstanceId { get; }

    /// <summary>物品模板。为 null 表示这是一个空实例（不应出现，仅作防御）。</summary>
    public ItemDefinition Definition { get; }

    /// <summary>剩余耐久。防具是「还能挡几次」，消耗品是「还能回多少量」。非耐久物品恒为 0。</summary>
    public int CurrentDurability { get; private set; }

    /// <summary>耐久上限。非耐久物品为 0。</summary>
    public int MaximumDurability => Definition != null ? Definition.Durability : 0;

    /// <summary>是否是带耐久的物品（防具或消耗品）。</summary>
    public bool HasDurability => MaximumDurability > 0;

    /// <summary>耐久归零即失效：防具不再提供减伤，消耗品不可再使用。非耐久物品恒为 false。</summary>
    public bool IsBroken => HasDurability && CurrentDurability <= 0;

    /// <summary>消耗 1 点耐久。已归零或非耐久物品时不变。</summary>
    public void ConsumeDurability()
    {
        if (CurrentDurability > 0)
            CurrentDurability--;
    }

    /// <summary>
    /// 消耗指定数量的耐久，返回实际消耗量（受剩余量限制）。
    /// 消耗品按「本次实际生效量」扣耐久，因此不会出现「只差 10 血却扣掉整瓶」的浪费。
    /// </summary>
    public int ConsumeDurability(int amount)
    {
        if (amount <= 0)
            return 0;

        var consumed = Mathf.Min(amount, CurrentDurability);
        CurrentDurability -= consumed;
        return consumed;
    }

    /// <summary>把剩余耐久设为指定值，自动钳进 [0, 上限]。</summary>
    public void RestoreDurability(int value)
    {
        CurrentDurability = Mathf.Clamp(value, 0, MaximumDurability);
    }

    /// <summary>
    /// 恢复指定数量的耐久，返回实际恢复量。已满时返回 0。
    /// 仅防具可被修复 —— 消耗品的耐久是「剩余量池」，用掉就没了，不可恢复。
    /// </summary>
    public int AddDurability(int amount)
    {
        if (amount <= 0 || !IsRepairable)
            return 0;

        var before = CurrentDurability;
        RestoreDurability(CurrentDurability + amount);
        return CurrentDurability - before;
    }

    /// <summary>
    /// 该物品的耐久是否可被修复。只有防具可以 —— 消耗品（血包 / 甲修）的耐久
    /// 表示「还剩多少可用量」，用掉即永久减少，甲修也不能修甲修自己。
    /// </summary>
    public bool IsRepairable => Definition != null
                             && Definition.Durability > 0
                             && !Definition.IsConsumable;

    private static string NewId() => Guid.NewGuid().ToString("N");
}
