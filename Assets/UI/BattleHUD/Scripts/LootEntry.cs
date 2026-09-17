using UnityEngine;

/// <summary>
/// 掉落列表的纯展示数据。刻意不引用 Gameplay 程序集，避免 BattleHUD → Gameplay 的循环依赖。
/// </summary>
public struct LootEntry
{
    public string DisplayName;
    public int Value;
    public Sprite Icon;
}
