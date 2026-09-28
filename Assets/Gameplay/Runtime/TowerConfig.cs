using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 爬塔的难度与掉落配置：所有数值都在这里调，改曲线不用重编译。
/// 层数从 1 开始；难度按「每层线性递增」计算，掉落由全局掉落池按层数掷点。
/// </summary>
[CreateAssetMenu(menuName = "Gameplay/Tower Config", fileName = "TowerConfig")]
public sealed class TowerConfig : ScriptableObject
{
    [Serializable]
    public struct EnemyWeight
    {
        public GameObject prefab;
        public int weight;
    }

    [Header("难度曲线")]
    [Tooltip("每高一层，敌人血量增加的比例。0.15 表示第 10 层为 1 + 0.15 × 9 = 2.35 倍。")]
    [SerializeField] private float healthGrowthPerFloor = 0.15f;

    [Tooltip("每高一层，敌人伤害增加的比例。")]
    [SerializeField] private float damageGrowthPerFloor = 0.10f;

    [Tooltip("第 1 层的敌人数量。")]
    [SerializeField] private int startingEnemiesPerFloor = 3;

    [Tooltip("每多少层多刷一只敌人。")]
    [SerializeField] private int floorsPerExtraEnemy = 1;

    [Tooltip("单层敌人数量上限。")]
    [SerializeField] private int maxEnemiesPerFloor = 12;

    [Header("撤退层")]
    [Tooltip("每隔多少层出现一个撤退层。5 表示第 5、10、15… 层。")]
    [SerializeField] private int retreatEveryFloors = 5;

    [Header("敌人池")]
    [Tooltip("按权重随机抽取。prefab 为空或 weight <= 0 的条目会被跳过。")]
    [SerializeField] private EnemyWeight[] enemyPool;

    [Header("掉落池")]
    [Tooltip("全局掉落池。所有敌人共用；掉落概率由池内的品质曲线按层数解析。留空则不掉落。")]
    [SerializeField] private LootPool lootPool;

    public float HealthGrowthPerFloor => healthGrowthPerFloor;
    public float DamageGrowthPerFloor => damageGrowthPerFloor;
    public int StartingEnemiesPerFloor => startingEnemiesPerFloor > 0 ? startingEnemiesPerFloor : 1;
    public int FloorsPerExtraEnemy => floorsPerExtraEnemy > 0 ? floorsPerExtraEnemy : 1;
    public int MaxEnemiesPerFloor => maxEnemiesPerFloor > 0 ? maxEnemiesPerFloor : 1;
    public int RetreatEveryFloors => retreatEveryFloors;

    public IReadOnlyList<EnemyWeight> EnemyPool => enemyPool ?? Array.Empty<EnemyWeight>();

    /// <summary>全局掉落池；未配置时为 null，此时敌人不掉落。</summary>
    public LootPool LootPool => lootPool;

    /// <summary>测试与运行时接线用；不改动 Inspector 里已序列化的数据以外的行为。</summary>
    public void Configure(EnemyWeight[] pool, LootPool loot)
    {
        enemyPool = pool;
        lootPool = loot;
    }
}
