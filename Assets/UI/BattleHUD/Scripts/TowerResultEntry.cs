using UnityEngine;

/// <summary>
/// 撤退结算面板的纯展示数据。刻意不引用 Gameplay 程序集，
/// 避免 BattleHUD → Gameplay 的循环依赖（与 LootEntry 同构）。
/// </summary>
public struct TowerResultEntry
{
    public string DisplayName;
    public int Value;
    public Sprite Icon;
}
