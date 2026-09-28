using UnityEngine;

/// <summary>
/// 一层的纯数据描述：由 <see cref="FloorGenerator"/> 生成，<see cref="TowerRunManager"/> 消费。
/// 刻意不含任何行为，便于在 EditMode 直接断言生成结果。
/// </summary>
public readonly struct FloorPlan
{
    public FloorPlan(int floor, bool isRetreatFloor, GameObject[] enemies, LootPool lootPool)
    {
        Floor = floor;
        IsRetreatFloor = isRetreatFloor;
        Enemies = enemies ?? System.Array.Empty<GameObject>();
        LootPool = lootPool;
    }

    /// <summary>层数，从 1 开始。</summary>
    public int Floor { get; }

    /// <summary>是否是撤退层（层数为撤退间隔的整数倍）。</summary>
    public bool IsRetreatFloor { get; }

    /// <summary>本层要生成的敌人预制体，长度即敌人数量。</summary>
    public GameObject[] Enemies { get; }

    /// <summary>本层敌人使用的全局掉落池；未配置时为 null。</summary>
    public LootPool LootPool { get; }
}
