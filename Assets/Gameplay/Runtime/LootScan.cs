using System.Collections.Generic;
using UnityEngine;

public static class LootScan
{
    /// 半径内是否存在存活敌人（已死亡的敌人不算）。
    public static bool HasAliveEnemyWithin(IDamageable[] enemies, Vector2 origin, float radius)
    {
        if (enemies == null)
            return false;

        var sqrRadius = radius * radius;
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.IsDead)
                continue;

            var delta = (Vector2)enemy.transform.position - origin;
            if (delta.sqrMagnitude < sqrRadius)
                return true;
        }

        return false;
    }

    /// 按到 origin 的距离升序排序，截断到 max 个。
    public static IReadOnlyList<DroppedItem2D> SelectNearest(DroppedItem2D[] candidates, Vector2 origin, int max)
    {
        var result = new List<DroppedItem2D>();
        if (candidates == null || max <= 0)
            return result;

        var sorted = new List<DroppedItem2D>();
        foreach (var candidate in candidates)
        {
            if (candidate != null)
                sorted.Add(candidate);
        }

        sorted.Sort((a, b) =>
            ((Vector2)a.transform.position - origin).sqrMagnitude
                .CompareTo(((Vector2)b.transform.position - origin).sqrMagnitude));

        for (var i = 0; i < sorted.Count && i < max; i++)
            result.Add(sorted[i]);

        return result;
    }
}
