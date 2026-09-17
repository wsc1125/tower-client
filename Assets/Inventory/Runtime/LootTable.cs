using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Loot Table", fileName = "LootTable")]
public sealed class LootTable : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public ItemDefinition item;
        [Range(0f, 1f)] public float dropChance;
    }

    [SerializeField] private Entry[] entries;

    public Entry[] Entries => entries;

    /// 测试与运行时接线用；不改动 Inspector 里已序列化的数据以外的行为。
    public void Configure(Entry[] source)
    {
        entries = source;
    }

    /// 每条独立掷点，互不影响，因此一次死亡可能命中多件。
    /// 接收 System.Random 而非直接用 UnityEngine.Random：既可固定种子验证可复现性，
    /// 也为将来把「权威随机」移到服务器留下唯一改动点。
    public IReadOnlyList<ItemDefinition> Roll(System.Random random)
    {
        var result = new List<ItemDefinition>();
        if (entries == null || random == null)
            return result;

        foreach (var entry in entries)
        {
            if (entry.item == null)
                continue;

            if (!ShouldDrop(entry.dropChance, random.NextDouble()))
                continue;

            result.Add(entry.item);
        }

        return result;
    }

    public static bool ShouldDrop(float dropChance, double roll)
    {
        if (dropChance <= 0f)
            return false;

        if (dropChance >= 1f)
            return true;

        return roll < dropChance;
    }
}
