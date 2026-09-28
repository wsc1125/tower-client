using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 由层数推导该层内容：敌人数量与种类、难度系数、掉落池、是否撤退层。
/// 全部是静态纯函数，接收 <see cref="System.Random"/> 而非 UnityEngine.Random，
/// 因此 EditMode 可用固定种子验证可复现性，也为将来把权威随机移到服务器留下唯一改动点。
/// </summary>
public static class FloorGenerator
{
    /// <summary>生成一层的完整描述。config 为 null 时返回空层（不抛异常）。</summary>
    public static FloorPlan Generate(int floor, TowerConfig config, System.Random random)
    {
        var resolvedFloor = Mathf.Max(1, floor);
        if (config == null)
            return new FloorPlan(resolvedFloor, false, System.Array.Empty<GameObject>(), null);

        var count = ResolveEnemyCount(resolvedFloor, config.StartingEnemiesPerFloor, config.FloorsPerExtraEnemy, config.MaxEnemiesPerFloor);
        var enemies = new GameObject[count];
        for (var i = 0; i < count; i++)
            enemies[i] = PickEnemy(config.EnemyPool, random);

        return new FloorPlan(
            resolvedFloor,
            IsRetreatFloor(resolvedFloor, config.RetreatEveryFloors),
            enemies,
            config.LootPool);
    }

    /// <summary>敌人数量：第 1 层 starting 只，每 floorsPerExtraEnemy 层多 1 只，上限 max。</summary>
    public static int ResolveEnemyCount(int floor, int starting, int floorsPerExtraEnemy, int max)
    {
        var resolvedFloor = Mathf.Max(1, floor);
        var step = Mathf.Max(1, floorsPerExtraEnemy);
        var start = Mathf.Max(1, starting);
        var count = start + (resolvedFloor - 1) / step;
        return Mathf.Clamp(count, 1, Mathf.Max(1, max));
    }

    /// <summary>敌人数量（起始 1 只）。保留旧签名，等价于 starting = 1。</summary>
    public static int ResolveEnemyCount(int floor, int floorsPerExtraEnemy, int max)
    {
        return ResolveEnemyCount(floor, 1, floorsPerExtraEnemy, max);
    }

    /// <summary>血量系数：1 + growth × (层数 - 1)。第 1 层恒为 1。</summary>
    public static float ResolveHealthMultiplier(int floor, float growthPerFloor)
    {
        return 1f + growthPerFloor * (Mathf.Max(1, floor) - 1);
    }

    /// <summary>伤害系数：1 + growth × (层数 - 1)。第 1 层恒为 1。</summary>
    public static float ResolveDamageMultiplier(int floor, float growthPerFloor)
    {
        return 1f + growthPerFloor * (Mathf.Max(1, floor) - 1);
    }

    /// <summary>是否是撤退层。间隔非正数时恒为 false。</summary>
    public static bool IsRetreatFloor(int floor, int everyFloors)
    {
        if (everyFloors <= 0)
            return false;

        return Mathf.Max(1, floor) % everyFloors == 0;
    }

    /// <summary>按权重加权随机抽一个敌人。池为空、总权重为 0 或 random 为 null 时返回 null。</summary>
    public static GameObject PickEnemy(IReadOnlyList<TowerConfig.EnemyWeight> pool, System.Random random)
    {
        if (pool == null || random == null)
            return null;

        var total = 0;
        for (var i = 0; i < pool.Count; i++)
        {
            var entry = pool[i];
            if (entry.prefab == null || entry.weight <= 0)
                continue;

            total += entry.weight;
        }

        if (total <= 0)
            return null;

        var roll = random.Next(total);
        for (var i = 0; i < pool.Count; i++)
        {
            var entry = pool[i];
            if (entry.prefab == null || entry.weight <= 0)
                continue;

            if (roll < entry.weight)
                return entry.prefab;

            roll -= entry.weight;
        }

        return null;
    }
}
