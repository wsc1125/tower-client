using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 收藏图鉴的配置：排除名单 + 奖励表。
///
/// 图鉴条目**不在这里列举**，而是运行时从 <see cref="ItemDatabase"/> 动态计算
/// （见 <see cref="Collectibles"/>）：新增材料自动进图鉴，无需改配置。
/// 这里只负责「排除哪些」与「奖励多少」。
/// </summary>
[CreateAssetMenu(menuName = "Inventory/Collection Catalog", fileName = "CollectionCatalog")]
public sealed class CollectionCatalog : ScriptableObject
{
    [Tooltip("不参与收藏的物品（任务道具、钥匙等）。武器防具无需登记，会自动排除。")]
    [SerializeField] private ItemDefinition[] excluded;

    [Header("单件奖励（按品质）")]
    [SerializeField] private int commonReward = 30;
    [SerializeField] private int fineReward = 60;
    [SerializeField] private int rareReward = 150;
    [SerializeField] private int epicReward = 400;
    [SerializeField] private int legendaryReward = 1000;

    [Header("全收集额外奖励")]
    [SerializeField] private int allCollectedReward = 3000;

    /// <summary>全收集额外奖励金额。</summary>
    public int AllCollectedReward => allCollectedReward;

    /// <summary>
    /// 计算当前图鉴条目：登记表里所有非装备物品，减去排除名单。
    /// 顺序与 <see cref="ItemDatabase.Items"/> 一致，保证 UI 排列稳定。
    /// </summary>
    public IReadOnlyList<ItemDefinition> Collectibles(ItemDatabase database)
    {
        var result = new List<ItemDefinition>();
        if (database == null)
            return result;

        foreach (var item in database.Items)
        {
            if (item == null)
                continue;

            // 武器防具不参与收藏。
            if (item.EquipSlotType != EquipmentSlotType.None)
                continue;

            if (IsExcluded(item))
                continue;

            result.Add(item);
        }

        return result;
    }

    /// <summary>该品质对应的单件点亮奖励。</summary>
    public int RewardFor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Fine:
                return fineReward;
            case ItemRarity.Rare:
                return rareReward;
            case ItemRarity.Epic:
                return epicReward;
            case ItemRarity.Legendary:
                return legendaryReward;
            default:
                return commonReward;
        }
    }

    private bool IsExcluded(ItemDefinition item)
    {
        if (excluded == null)
            return false;

        foreach (var entry in excluded)
        {
            if (entry == item)
                return true;
        }

        return false;
    }
}
