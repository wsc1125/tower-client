/// <summary>
/// 物品品质。值从 0 递增，未显式标注的物品默认为 <see cref="Common"/>。
/// 品质只影响展示（格子底板描边色等），不参与数值计算。
/// </summary>
public enum ItemRarity
{
    Common = 0,
    Fine = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
}
