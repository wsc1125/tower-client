using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 每层随机撒掩体。由 <see cref="TowerRunManager"/> 在房间与敌人就位后调用一次。
/// 只负责「选位置 + 实例化」，不参与战斗结算；换层时由 Manager 统一销毁。
/// 位置约束全部集中在这里，避免散落到 Manager 里：
/// ① 必须落在房间可活动区域内（内缩 margin）；
/// ② 不能压住玩家出生点（否则玩家一出生就被卡在掩体里）；
/// ③ 不能压住传送点（否则传送点被埋住、玩家走不到）；
/// ④ 不能压住敌人（否则敌人被卡住或直接被挤穿墙）；
/// ⑤ 掩体之间不能重叠（否则会堆成一团）。
///
/// 掩体种类由 prefab 池按权重抽取，新增掩体只需往池里加一条，
/// 尺寸与碰撞体由各 prefab 上的 <see cref="Obstacle2D"/> 自行决定，这里不做假设。
/// </summary>
public sealed class ObstaclePlacer : MonoBehaviour
{
    /// <summary>掩体池的一条：prefab + 抽取权重。与 <see cref="TowerConfig.EnemyWeight"/> 同构。</summary>
    [Serializable]
    public struct ObstacleWeight
    {
        public GameObject prefab;
        public int weight;
    }

    [Header("种类")]
    [Tooltip("按权重随机抽取。prefab 为空或 weight <= 0 的条目会被跳过。")]
    [SerializeField] private ObstacleWeight[] obstaclePool;

    [Header("数量")]
    [Tooltip("第 1 层的掩体数量。")]
    [SerializeField] private int baseCount = 3;

    [Tooltip("每高一层额外增加的掩体数量。0 表示各层数量相同。")]
    [SerializeField] private float countGrowthPerFloor = 0.5f;

    [Tooltip("单层掩体数量上限。")]
    [SerializeField] private int maxCount = 8;

    [Header("尺寸")]
    [Tooltip("掩体整体尺寸倍率（乘在 prefab 自身尺寸上）。所有掩体统一使用该倍率，不做随机缩放。")]
    [SerializeField] private float sizeScale = 1f;

    [Header("间距")]
    [Tooltip("掩体边缘距房间边界的最小距离。注意这是「边缘」不是「中心」——掩体尺寸随机，按中心算会让大掩体插进墙里。")]
    [SerializeField] private float wallMargin = 0.35f;

    [Tooltip("掩体与玩家出生点的最小距离。")]
    [SerializeField] private float playerClearance = 1.6f;

    [Tooltip("掩体与传送点的最小距离。")]
    [SerializeField] private float portalClearance = 1.4f;

    [Tooltip("掩体与敌人的最小距离。")]
    [SerializeField] private float enemyClearance = 1.2f;

    [Tooltip("掩体之间的最小中心距。")]
    [SerializeField] private float obstacleSpacing = 1.6f;

    [Tooltip("每个掩体最多尝试多少个随机位置；试满仍冲突则放弃这一个。")]
    [SerializeField] private int maxAttemptsPerObstacle = 24;

    private readonly List<Obstacle2D> spawned = new List<Obstacle2D>();

    /// <summary>本层已生成的掩体，供 Manager 清理。</summary>
    public IReadOnlyList<Obstacle2D> Spawned => spawned;

    /// <summary>掩体池。测试与运行时接线用。</summary>
    public IReadOnlyList<ObstacleWeight> ObstaclePool => obstaclePool ?? Array.Empty<ObstacleWeight>();

    /// <summary>本层掩体数量：baseCount + 层数增长，钳到 [0, maxCount]。</summary>
    public int ResolveCount(int floor)
    {
        var resolvedFloor = Mathf.Max(1, floor);
        var count = baseCount + Mathf.FloorToInt(countGrowthPerFloor * (resolvedFloor - 1));
        return Mathf.Clamp(count, 0, Mathf.Max(0, maxCount));
    }

    /// <summary>
    /// 按权重加权随机抽一个掩体 prefab。池为空、总权重为 0 或 random 为 null 时返回 null。
    /// 与 <see cref="FloorGenerator.PickEnemy"/> 同构，保证两处抽取行为一致。
    /// </summary>
    public GameObject PickPrefab(System.Random random)
    {
        if (random == null)
            return null;

        var pool = obstaclePool;
        if (pool == null)
            return null;

        var total = 0;
        for (var i = 0; i < pool.Length; i++)
        {
            var entry = pool[i];
            if (entry.prefab == null || entry.weight <= 0)
                continue;

            total += entry.weight;
        }

        if (total <= 0)
            return null;

        var roll = random.Next(total);
        for (var i = 0; i < pool.Length; i++)
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

    /// <summary>
    /// 生成一层掩体。roomBounds 为房间可活动区域，playerPosition 为玩家出生点，
    /// portalPositions 为本层传送点位置（可能为空），enemyPositions 为敌人出生点。
    /// 返回本层实际生成的掩体数量。
    /// </summary>
    public int Place(
        Rect roomBounds,
        Vector2 playerPosition,
        IReadOnlyList<Vector2> portalPositions,
        IReadOnlyList<Vector2> enemyPositions,
        int floor,
        System.Random random)
    {
        Clear();

        if (random == null)
            return 0;

        var count = ResolveCount(floor);
        if (count <= 0)
            return 0;

        var placed = 0;
        for (var i = 0; i < count; i++)
        {
            var prefab = PickPrefab(random);
            if (prefab == null)
                continue;

            var size = sizeScale;

            // 关键：边距必须按「这个掩体自己的实际半宽/半高」算，而不是用固定值。
            // 掩体尺寸由 prefab 决定（可能各不相同），若用固定 margin，大掩体必然插进墙里。
            // 半宽取 prefab 的碰撞体尺寸 × 本次缩放，再留 wallMargin 的余量。
            var halfExtent = ResolveHalfExtent(prefab, size);
            var xMin = roomBounds.xMin + halfExtent.x + wallMargin;
            var xMax = roomBounds.xMax - halfExtent.x - wallMargin;
            var yMin = roomBounds.yMin + halfExtent.y + wallMargin;
            var yMax = roomBounds.yMax - halfExtent.y - wallMargin;

            // 房间放不下这个掩体时跳过，而不是硬塞进墙里。
            if (xMin >= xMax || yMin >= yMax)
                continue;

            if (!TryFindPosition(
                    xMin, xMax, yMin, yMax,
                    playerPosition, portalPositions, enemyPositions,
                    random, out var position))
            {
                continue;
            }

            var instance = Instantiate(prefab, position, Quaternion.identity, transform);
            instance.transform.localScale = new Vector3(size, size, 1f);

            var obstacle = instance.GetComponent<Obstacle2D>();
            if (obstacle == null)
                obstacle = instance.AddComponent<Obstacle2D>();

            spawned.Add(obstacle);
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// 求掩体在给定缩放下的半宽 / 半高（世界单位）。
    /// 优先读 prefab 上碰撞体的实际尺寸 —— 那才是真正会挡住玩家的范围；
    /// 碰撞体尺寸由 <see cref="Obstacle2D"/> 在 Awake 时按贴图不透明区域摆正，
    /// 因此这里读的是 prefab 资产上的值，可能与运行时略有出入，故再留 wallMargin 兜底。
    /// </summary>
    private static Vector2 ResolveHalfExtent(GameObject prefab, float size)
    {
        var collider = prefab.GetComponent<Collider2D>();
        if (collider == null)
            collider = prefab.GetComponentInChildren<Collider2D>();

        var extent = collider != null ? collider.bounds.extents : new Vector3(0.5f, 0.5f, 0f);

        // prefab 资产上的 bounds 已含 prefab 自身缩放，这里再乘本次随机缩放。
        return new Vector2(Mathf.Abs(extent.x) * size, Mathf.Abs(extent.y) * size);
    }

    /// <summary>销毁本层全部掩体。</summary>
    public void Clear()
    {
        foreach (var obstacle in spawned)
        {
            if (obstacle != null)
                Destroy(obstacle.gameObject);
        }

        spawned.Clear();
    }

    /// <summary>
    /// 随机试位：命中一个与所有已有掩体、玩家、传送点、敌人都保持足够距离的点。
    /// 试满 maxAttemptsPerObstacle 次仍冲突则返回 false（宁可少放一个，也不重叠）。
    /// </summary>
    private bool TryFindPosition(
        float xMin, float xMax, float yMin, float yMax,
        Vector2 playerPosition,
        IReadOnlyList<Vector2> portalPositions,
        IReadOnlyList<Vector2> enemyPositions,
        System.Random random,
        out Vector2 position)
    {
        var attempts = Mathf.Max(1, maxAttemptsPerObstacle);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var candidate = new Vector2(
                Mathf.Lerp(xMin, xMax, (float)random.NextDouble()),
                Mathf.Lerp(yMin, yMax, (float)random.NextDouble()));

            if (IsTooClose(candidate, playerPosition, playerClearance))
                continue;

            if (IsTooCloseAny(candidate, portalPositions, portalClearance))
                continue;

            if (IsTooCloseAny(candidate, enemyPositions, enemyClearance))
                continue;

            if (IsTooCloseToSpawned(candidate))
                continue;

            position = candidate;
            return true;
        }

        position = Vector2.zero;
        return false;
    }

    private static bool IsTooClose(Vector2 candidate, Vector2 other, float minDistance)
    {
        return (candidate - other).sqrMagnitude < minDistance * minDistance;
    }

    private static bool IsTooCloseAny(Vector2 candidate, IReadOnlyList<Vector2> others, float minDistance)
    {
        if (others == null)
            return false;

        for (var i = 0; i < others.Count; i++)
        {
            if (IsTooClose(candidate, others[i], minDistance))
                return true;
        }

        return false;
    }

    private bool IsTooCloseToSpawned(Vector2 candidate)
    {
        for (var i = 0; i < spawned.Count; i++)
        {
            var obstacle = spawned[i];
            if (obstacle == null)
                continue;

            if (IsTooClose(candidate, obstacle.transform.position, obstacleSpacing))
                return true;
        }

        return false;
    }
}
