using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗场景对象注册表：集中维护可受伤目标与地面掉落物，替代各系统反复全局扫描。
///
/// 对象在 Awake / OnEnable 注册、OnDisable 注销；查询方只读取当前场景已注册对象。
/// 为兼容「Awake 未触发」的场景（如 EditMode 直接 AddComponent），查询时会补一次
/// 全场景发现，保证结果与旧的 FindObjectsOfType 行为一致。
/// </summary>
public static class WorldRegistry
{
    private static readonly HashSet<IDamageable> damageables = new HashSet<IDamageable>();
    private static readonly HashSet<DroppedItem2D> droppedItems = new HashSet<DroppedItem2D>();

    /// <summary>
    /// 被显式注销过的对象。兜底发现必须跳过它们，
    /// 否则「注销」会被下一次查询重新加回来（表现为注销无效）。
    /// </summary>
    private static readonly HashSet<IDamageable> unregisteredDamageables = new HashSet<IDamageable>();
    private static readonly HashSet<DroppedItem2D> unregisteredDroppedItems = new HashSet<DroppedItem2D>();

    public static IReadOnlyCollection<IDamageable> Damageables
    {
        get
        {
            RemoveInvalidDamageables();
            DiscoverDamageables();
            return damageables;
        }
    }

    public static IReadOnlyCollection<DroppedItem2D> DroppedItems
    {
        get
        {
            RemoveInvalidDroppedItems();
            DiscoverDroppedItems();
            return droppedItems;
        }
    }

    public static void Register(IDamageable target)
    {
        if (target == null)
            return;

        unregisteredDamageables.Remove(target);
        damageables.Add(target);
    }

    public static void Unregister(IDamageable target)
    {
        damageables.Remove(target);

        if (target != null)
            unregisteredDamageables.Add(target);
    }

    public static void Register(DroppedItem2D droppedItem)
    {
        if (droppedItem == null)
            return;

        unregisteredDroppedItems.Remove(droppedItem);
        droppedItems.Add(droppedItem);
    }

    public static void Unregister(DroppedItem2D droppedItem)
    {
        droppedItems.Remove(droppedItem);

        if (droppedItem != null)
            unregisteredDroppedItems.Add(droppedItem);
    }

    /// <summary>清空注册表。测试用，保证用例之间互不污染。</summary>
    public static void ClearForTests()
    {
        damageables.Clear();
        droppedItems.Clear();
        unregisteredDamageables.Clear();
        unregisteredDroppedItems.Clear();
    }

    /// <summary>
    /// 补一次全场景发现。注册表是「当前场景对象」的视图，
    /// 因此每次查询都重新发现，避免把已销毁对象或跨用例残留当成有效数据。
    /// 被显式注销过的对象会被跳过，保证注销语义不被兜底发现覆盖。
    /// </summary>
    private static void DiscoverDamageables()
    {
        foreach (var enemy in Object.FindObjectsOfType<EnemyBrain2D>(false))
        {
            if (!unregisteredDamageables.Contains(enemy))
                damageables.Add(enemy);
        }

        foreach (var swarm in Object.FindObjectsOfType<SwarmChaser2D>(false))
        {
            if (!unregisteredDamageables.Contains(swarm))
                damageables.Add(swarm);
        }

        foreach (var boss in Object.FindObjectsOfType<BossBrain2D>(false))
        {
            if (!unregisteredDamageables.Contains(boss))
                damageables.Add(boss);
        }
    }

    private static void DiscoverDroppedItems()
    {
        foreach (var item in Object.FindObjectsOfType<DroppedItem2D>(false))
        {
            if (!unregisteredDroppedItems.Contains(item))
                droppedItems.Add(item);
        }
    }

    private static void RemoveInvalidDamageables()
    {
        // 只按 Unity 对象的「已销毁」语义判空，不访问 transform ——
        // 已销毁对象上访问 transform 会抛 MissingReferenceException。
        damageables.RemoveWhere(target => target as Object == null);
    }

    private static void RemoveInvalidDroppedItems()
    {
        droppedItems.RemoveWhere(item => item == null);
    }
}
