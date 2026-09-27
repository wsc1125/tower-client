using System.Collections.Generic;

/// <summary>
/// 收藏进度：三态流转的状态机。
///
/// 未获得 → 已获得（<see cref="Obtain"/>，撤离带出时调用）
///        → 已点亮（<see cref="TryLight"/>，玩家在展馆手动点亮）
///
/// 只存 ID 字符串，不存物品引用 —— 物品增删不会导致存档解析失败。
/// 纯 C#，不依赖 Unity，可独立测试。
/// </summary>
public sealed class CollectionProgress
{
    private readonly HashSet<string> obtainedIds = new HashSet<string>();
    private readonly HashSet<string> litIds = new HashSet<string>();

    /// <summary>已获得资格的物品 ID（含已点亮的）。</summary>
    public IReadOnlyCollection<string> ObtainedIds => obtainedIds;

    /// <summary>已点亮的物品 ID。</summary>
    public IReadOnlyCollection<string> LitIds => litIds;

    /// <summary>全收集额外奖励是否已发放。</summary>
    public bool AllCollectedRewarded { get; set; }

    /// <summary>标记为已获得。首次获得返回 true，重复获得返回 false。</summary>
    public bool Obtain(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        return obtainedIds.Add(id);
    }

    public bool IsObtained(string id)
    {
        return !string.IsNullOrEmpty(id) && obtainedIds.Contains(id);
    }

    /// <summary>点亮。仅「已获得且未点亮」时成功，不可撤销。</summary>
    public bool TryLight(string id)
    {
        if (!IsObtained(id))
            return false;

        return litIds.Add(id);
    }

    public bool IsLit(string id)
    {
        return !string.IsNullOrEmpty(id) && litIds.Contains(id);
    }

    /// <summary>在给定 ID 集合内统计已点亮数量。用于忽略孤儿 ID。</summary>
    public int LitCountWithin(IEnumerable<string> validIds)
    {
        return CountWithin(litIds, validIds);
    }

    /// <summary>在给定 ID 集合内统计已获得数量。用于忽略孤儿 ID。</summary>
    public int ObtainedCountWithin(IEnumerable<string> validIds)
    {
        return CountWithin(obtainedIds, validIds);
    }

    /// <summary>
    /// 校正数据一致性：把 litIds 里但 obtainedIds 里没有的 ID 补进 obtainedIds。
    /// 加载存档后调用一次。
    /// </summary>
    public void Reconcile()
    {
        foreach (var id in litIds)
            obtainedIds.Add(id);
    }

    private static int CountWithin(HashSet<string> source, IEnumerable<string> validIds)
    {
        if (validIds == null)
            return 0;

        var count = 0;
        foreach (var id in validIds)
        {
            if (source.Contains(id))
                count++;
        }

        return count;
    }
}
