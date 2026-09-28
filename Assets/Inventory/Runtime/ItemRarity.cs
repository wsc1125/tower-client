/// <summary>
/// 物品品质。值从 0 递增，未显式标注的物品默认为 <see cref="Common"/>。
/// 品质影响展示（格子底板描边色等），也参与修甲成本折算。
/// </summary>
public enum ItemRarity
{
    Common = 0,
    Fine = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
}

/// <summary>
/// 品质相关的数值折算。集中放在这里，避免「修甲成本」这类规则散落到各处。
/// </summary>
public static class ItemRarityRules
{
    /// <summary>
    /// 修甲成本倍率：品质越高，修同样多的耐久越费甲修。
    /// 以普通为 1.0 线性递增到传说 2.2。
    /// </summary>
    public static float RepairCostMultiplier(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Fine: return 1.2f;
            case ItemRarity.Rare: return 1.5f;
            case ItemRarity.Epic: return 1.8f;
            case ItemRarity.Legendary: return 2.2f;
            default: return 1.0f;
        }
    }
}
