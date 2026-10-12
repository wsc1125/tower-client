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

    /// <summary>是否把已死亡的玩家视为无效目标。</summary>
    private readonly bool ignoreDead;

    /// <summary>解析目标时参考的位置。用于「取最近玩家」，由调用方在构造后设置。</summary>
    private Transform self;

    /// <summary>上次选中的目标。距离相同时优先保持它。</summary>
    private PlayerMovement2D lockedTarget;

    private float loseRange = float.PositiveInfinity;
    private bool loggedFirstHostSelection;

    public NearestPlayerTargetProvider(bool ignoreDead = true)
    {
        this.ignoreDead = ignoreDead;
    }

    /// <summary>直接指定目标（测试与手工摆放的场景用）。传 null 则恢复为自动扫描。</summary>
    public void SetExplicitTarget(Transform target)
    {
        explicitTarget = target;
    }

    /// <summary>设置「自身」位置，用于按距离选最近的玩家。传 null 则退化为取第一个玩家。</summary>
    public void SetSelf(Transform selfTransform)
    {
        self = selfTransform;
    }

    public void SetLoseRange(float value)
    {
        loseRange = Mathf.Max(0f, value);
    }

    public bool TryGetTargetPosition(out Vector2 position)
    {
        position = Vector2.zero;

        if (explicitTarget != null)
        {
            position = explicitTarget.position;
            LogFirstHostSelection(explicitTarget);
            return true;
        }

        var player = ResolvePlayer();
        LogFirstHostSelection(player != null ? player.transform : null);
        if (player == null)
            return false;

        position = player.transform.position;
        return true;
    }

    private void LogFirstHostSelection(Transform selected)
    {
        var manager = Unity.Netcode.NetworkManager.Singleton;
        if (loggedFirstHostSelection || manager == null || !manager.IsServer || !manager.IsListening)
            return;

        loggedFirstHostSelection = true;
        var builder = new System.Text.StringBuilder();
        builder.Append("[TargetSelection] enemy=").Append(self != null ? self.name : "<no self>")
            .Append(" position=").Append(self != null ? self.position.ToString() : "<no self>");
        foreach (var candidate in WorldRegistry.Players)
        {
            if (candidate == null)
            {
                builder.Append("\n  candidate=<destroyed>");
                continue;
            }

            var sync = candidate.GetComponent<Tower.Netcode.PlayerNetworkSync>();
            builder.Append("\n  candidate=").Append(candidate.name)
                .Append(" id=").Append(candidate.GetInstanceID())
                .Append(" OwnerClientId=").Append(sync != null ? sync.OwnerClientId.ToString() : "<no sync>")
                .Append(" IsSpawned=").Append(sync != null ? sync.IsSpawned.ToString() : "<no sync>")
                .Append(" IsAlive=").Append(sync != null ? sync.IsAlive.ToString() : "<no sync>")
                .Append(" activeInHierarchy=").Append(candidate.gameObject.activeInHierarchy)
                .Append(" movement.enabled=").Append(candidate.enabled)
                .Append(" position=").Append(candidate.transform.position);
        }

        var selectedPlayer = selected != null ? selected.GetComponent<PlayerMovement2D>() : null;
        builder.Append("\n  selected id=").Append(selectedPlayer != null ? selectedPlayer.GetInstanceID().ToString() : selected != null ? selected.GetInstanceID().ToString() : "<none>")
            .Append(" position=").Append(selected != null ? selected.position.ToString() : "<none>");
        Debug.LogWarning(builder.ToString());
    }

    /// <summary>
    /// 解析当前最近的存活玩家；距离相同时保持上次选中的目标。
    /// 玩家列表来自 <see cref="WorldRegistry.Players"/>（场景注册表），
    /// 不再用 <c>FindObjectsOfType</c> 全量扫描。
    /// </summary>
    private PlayerMovement2D ResolvePlayer()
    {
        var candidates = WorldRegistry.Players;
        if (candidates == null || candidates.Count == 0)
        {
            lockedTarget = null;
            return null;
        }

        var hasValidLockedTarget = IsLockedTargetValid(candidates);
        if (self == null && hasValidLockedTarget)
            return lockedTarget;

        var origin = self != null ? (Vector2)self.position : Vector2.zero;
        var best = self != null && hasValidLockedTarget ? lockedTarget : null;
        var bestSqr = best != null ? ((Vector2)best.transform.position - origin).sqrMagnitude : float.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if (candidate == null || (ignoreDead && IsDead(candidate)))
                continue;

            if (self == null)
            {
                best = candidate;
                break;
            }

            var sqr = ((Vector2)candidate.transform.position - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }

        lockedTarget = best;
        return lockedTarget;
    }

    private bool IsLockedTargetValid(System.Collections.Generic.IReadOnlyCollection<PlayerMovement2D> candidates)
    {
        if (lockedTarget == null || (ignoreDead && IsDead(lockedTarget)))
            return false;

        var registered = false;
        foreach (var candidate in candidates)
        {
            if (candidate == lockedTarget)
            {
                registered = true;
                break;
            }
        }

        if (!registered)
            return false;

        if (self == null || float.IsPositiveInfinity(loseRange))
            return true;

        var offset = (Vector2)lockedTarget.transform.position - (Vector2)self.position;
        return offset.sqrMagnitude <= loseRange * loseRange;
    }

    /// <summary>玩家是否已死亡。玩家身上没有血量组件时视为存活。</summary>
    private static bool IsDead(PlayerMovement2D player)
    {
        var sync = player.GetComponent<Tower.Netcode.PlayerNetworkSync>();
        if (sync != null && sync.IsSpawned)
            return !sync.IsAlive;

        var health = player.GetComponent<PlayerHealth2D>();
        return health != null && health.IsDead;
    }
}
