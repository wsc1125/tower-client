using UnityEngine;

/// <summary>
/// 敌人「当前该追谁」的解析入口。
///
/// 抽成接口是为了让敌人不再硬编码 <c>FindObjectOfType&lt;PlayerMovement2D&gt;()</c>：
/// 单机下只有一个玩家，直接找场景里那一个即可；
/// 联机下需要按「最近存活玩家」等规则解析。
///
/// 默认实现 <see cref="NearestPlayerTargetProvider"/> 在单机下与旧的
/// <c>FindObjectOfType&lt;PlayerMovement2D&gt;()</c> 行为等价，因此单机表现不变。
/// </summary>
public interface ITargetProvider
{
    /// <summary>
    /// 取当前目标的位置。返回 false 表示当前没有可用目标（如玩家已死亡或尚未生成），
    /// 调用方应据此退化为「无目标」行为。
    /// </summary>
    bool TryGetTargetPosition(out Vector2 position);
}

/// <summary>
/// 默认目标提供者：取场景中最近的存活玩家。
///
/// 单机下场景里只有一个玩家，结果与旧的 <c>FindObjectOfType&lt;PlayerMovement2D&gt;()</c> 等价。
/// 联机下会按距离选最近的玩家，但**这只是本地表现**——
/// 权威判定仍由主机负责，客户端不需要与主机算出完全相同的目标。
/// </summary>
public sealed class NearestPlayerTargetProvider : ITargetProvider
{
    /// <summary>显式指定的目标。非空时优先使用它，不再扫描场景。</summary>
    private Transform explicitTarget;

    /// <summary>扫描到的玩家缓存。被销毁后会自动重新解析。</summary>
    private PlayerMovement2D cachedPlayer;

    /// <summary>是否把已死亡的玩家视为无效目标。</summary>
    private readonly bool ignoreDead;

    /// <summary>解析目标时参考的位置。用于「取最近玩家」，由调用方在构造后设置。</summary>
    private Transform self;

    public NearestPlayerTargetProvider(bool ignoreDead = true)
    {
        this.ignoreDead = ignoreDead;
    }

    /// <summary>直接指定目标（测试与手工摆放的场景用）。传 null 则恢复为自动扫描。</summary>
    public void SetExplicitTarget(Transform target)
    {
        explicitTarget = target;
        cachedPlayer = null;
    }

    /// <summary>设置「自身」位置，用于按距离选最近的玩家。传 null 则退化为取第一个玩家。</summary>
    public void SetSelf(Transform selfTransform)
    {
        self = selfTransform;
    }

    public bool TryGetTargetPosition(out Vector2 position)
    {
        position = Vector2.zero;

        if (explicitTarget != null)
        {
            position = explicitTarget.position;
            return true;
        }

        var player = ResolvePlayer();
        if (player == null)
            return false;

        position = player.transform.position;
        return true;
    }

    /// <summary>
    /// 解析当前玩家。缓存失效（被销毁）时重新解析一次。
    /// 玩家列表来自 <see cref="WorldRegistry.Players"/>（场景注册表），
    /// 不再用 <c>FindObjectsOfType</c> 全量扫描 —— 联机下场景里有多个玩家，需要按距离选最近的。
    /// </summary>
    private PlayerMovement2D ResolvePlayer()
    {
        if (cachedPlayer != null)
            return cachedPlayer;

        var candidates = WorldRegistry.Players;
        if (candidates == null || candidates.Count == 0)
            return null;

        // 单机下只有一个玩家，直接取它 —— 与旧的 FindObjectOfType 行为一致。
        if (candidates.Count == 1 || self == null)
        {
            foreach (var only in candidates)
            {
                cachedPlayer = only;
                break;
            }

            return cachedPlayer;
        }

        // 联机下按距离取最近的存活玩家。
        var origin = (Vector2)self.position;
        PlayerMovement2D best = null;
        var bestSqr = float.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if (candidate == null)
                continue;

            if (ignoreDead && IsDead(candidate))
                continue;

            var sqr = ((Vector2)candidate.transform.position - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        cachedPlayer = best;
        return cachedPlayer;
    }

    /// <summary>玩家是否已死亡。玩家身上没有血量组件时视为存活。</summary>
    private static bool IsDead(PlayerMovement2D player)
    {
        var health = player.GetComponent<PlayerHealth2D>();
        return health != null && health.IsDead;
    }
}
