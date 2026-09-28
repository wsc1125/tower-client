using System;
using UnityEngine;

/// <summary>
/// 单个品质的掉落概率随层数变化的曲线。
/// 用「层数 → 概率」的关键点线性插值，层数越高概率越高（或越低）由关键点决定。
/// 纯数据 + 纯函数，便于在 EditMode 直接断言。
/// </summary>
[Serializable]
public struct LootRarityCurve
{
    [Serializable]
    public struct Point
    {
        [Tooltip("层数，从 1 开始。")]
        public int floor;

        [Tooltip("该层数下的掉落概率（0~1）。")]
        [Range(0f, 1f)]
        public float chance;
    }

    [Tooltip("品质。物品按自身 rarity 归入对应曲线。")]
    public ItemRarity rarity;

    [Tooltip("按 floor 升序填写。层数落在两点之间时线性插值；低于首点取首点，高于末点取末点。")]
    public Point[] points;

    /// <summary>
    /// 解析指定层数下的概率。
    /// 关键点为空时返回 0；层数低于首点取首点概率，高于末点取末点概率，中间线性插值。
    /// </summary>
    public float ResolveChance(int floor)
    {
        if (points == null || points.Length == 0)
            return 0f;

        var resolvedFloor = Mathf.Max(1, floor);

        // 关键点按 floor 升序排列，先找到第一个 floor >= 目标层数的点。
        var first = points[0];
        if (resolvedFloor <= first.floor)
            return Mathf.Clamp01(first.chance);

        for (var i = 1; i < points.Length; i++)
        {
            var previous = points[i - 1];
            var current = points[i];

            if (resolvedFloor > current.floor)
                continue;

            // 两点 floor 相同（或乱序）时无法插值，直接取后一点，避免除零。
            var span = current.floor - previous.floor;
            if (span <= 0)
                return Mathf.Clamp01(current.chance);

            var t = (resolvedFloor - previous.floor) / (float)span;
            return Mathf.Clamp01(Mathf.Lerp(previous.chance, current.chance, t));
        }

        return Mathf.Clamp01(points[points.Length - 1].chance);
    }
}
