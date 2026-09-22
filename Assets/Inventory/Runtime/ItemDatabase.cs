using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物品登记表：以 <see cref="ItemDefinition.Id"/> 为键，供存档按 ID 还原物品。
/// </summary>
[CreateAssetMenu(menuName = "Inventory/Item Database", fileName = "ItemDatabase")]
public sealed class ItemDatabase : ScriptableObject
{
    [SerializeField] private ItemDefinition[] items;

    private Dictionary<string, ItemDefinition> lookup;

    public IReadOnlyList<ItemDefinition> Items => items ?? System.Array.Empty<ItemDefinition>();

    /// <summary>按 ID 查找物品；ID 为空或未登记时返回 null，不抛异常。</summary>
    public ItemDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        EnsureLookup();
        return lookup.TryGetValue(id, out var item) ? item : null;
    }

    private void EnsureLookup()
    {
        if (lookup != null)
            return;

        lookup = new Dictionary<string, ItemDefinition>();
        if (items == null)
            return;

        foreach (var item in items)
        {
            if (item == null)
                continue;

            var id = item.Id;
            if (string.IsNullOrEmpty(id))
                continue;

            if (lookup.ContainsKey(id))
            {
                Debug.LogWarning($"ItemDatabase 中存在重复 Id「{id}」，已保留第一个登记的条目。", this);
                continue;
            }

            lookup.Add(id, item);
        }
    }
}
