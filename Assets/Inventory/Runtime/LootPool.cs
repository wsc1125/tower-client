using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局掉落池：所有敌人共用一份物品清单，掉落概率由「品质曲线 + 层数」决定。
/// 层数越高，高品质物品的概率越高（曲线由 <see cref="LootRarityCurve"/> 配置）。
/// 掷点接收 <see cref="System.Random"/> 而非 UnityEngine.Random：
/// 既可固定种子验证可复现性，也为将来把「权威随机」移到服务器留下唯一改动点。
/// </summary>
[CreateAssetMenu(menuName = "Inventory/Loot Pool", fileName = "LootPool")]
public sealed class LootPool : ScriptableObject
{
    [Tooltip("可掉落的物品。物品按自身 rarity 归入对应品质曲线。")]
    [SerializeField] private ItemDefinition[] items;

    [Tooltip("每个品质一条概率曲线。未配置的品质概率视为 0。")]
    [SerializeField] private LootRarityCurve[] rarityCurves;

    public IReadOnlyList<ItemDefinition> Items => items ?? Array.Empty<ItemDefinition>();
    public IReadOnlyList<LootRarityCurve> RarityCurves => rarityCurves ?? Array.Empty<LootRarityCurve>();

    /// <summary>测试与运行时接线用；不改动 Inspector 里已序列化的数据以外的行为。</summary>
    public void Configure(ItemDefinition[] sourceItems, LootRarityCurve[] sourceCurves)
    {
        items = sourceItems;
        rarityCurves = sourceCurves;
    }

    /// <summary>取指定品质在指定层数下的掉落概率；未配置该品质时返回 0。</summary>
    public float ResolveChance(ItemRarity rarity, int floor)
    {
        if (rarityCurves == null)
            return 0f;

        for (var i = 0; i < rarityCurves.Length; i++)
        {
            if (rarityCurves[i].rarity == rarity)
                return rarityCurves[i].ResolveChance(floor);
        }

        return 0f;
    }

    /// <summary>
    /// 按层数掷点：逐件物品独立判定，概率取该物品品质在当层的曲线值。
    /// 因此一次死亡可能命中多件，也可能一件不掉。
    /// </summary>
    public IReadOnlyList<ItemDefinition> Roll(int floor, System.Random random)
    {
        var result = new List<ItemDefinition>();
        if (items == null || random == null)
            return result;

        foreach (var item in items)
        {
            if (item == null)
                continue;

            var chance = ResolveChance(item.Rarity, floor);
            if (!ShouldDrop(chance, random.NextDouble()))
                continue;

            result.Add(item);
        }

        return result;
    }

    /// <summary>概率 &lt;= 0 恒不掉；&gt;= 1 恒掉；否则 roll &lt; 概率时掉落。</summary>
    public static bool ShouldDrop(float chance, double roll)
    {
        if (chance <= 0f)
            return false;

        if (chance >= 1f)
            return true;

        return roll < chance;
    }
}
