using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>收藏图鉴中一件物品的三态。</summary>
public enum CollectionState
{
    /// <summary>未获得：黑色剪影。</summary>
    Locked = 0,

    /// <summary>已获得待点亮：灰色剪影。</summary>
    Obtained = 1,

    /// <summary>已点亮：彩图。</summary>
    Lit = 2,
}

/// <summary>奖励类型，供 UI 区分提示方式。</summary>
public enum CollectionRewardKind
{
    /// <summary>点亮单件物品的奖励。</summary>
    Item = 0,

    /// <summary>全收集额外奖励。</summary>
    AllCollected = 1,
}

/// <summary>
/// 收藏成就的业务门面：撤离结算、点亮、发奖、对账。
///
/// 奖励通过 <see cref="RewardGranted"/> 事件对外广播，**不直接依赖 PlayerCurrency**
/// —— 后者在 Assembly-CSharp，本程序集（Inventory）不可引用。
/// UI 层订阅该事件后调用 PlayerCurrency.Add。
/// </summary>
public static class CollectionService
{
    private static ItemDatabase database;
    private static CollectionCatalog catalog;
    private static CollectionProgress progress;
    private static List<ItemDefinition> collectibles;

    /// <summary>
    /// 发放奖励时触发，参数为（星币数量, 奖励类型）。
    /// UI 层据此调用 PlayerCurrency.Add，并按类型决定是否弹全收集提示。
    /// </summary>
    public static event Action<int, CollectionRewardKind> RewardGranted;

    /// <summary>进度写入缓存后触发（云同步据此标脏上传）。</summary>
    public static event Action ProgressSaved;

    /// <summary>当前进度。首次访问时按当前身份从缓存加载。</summary>
    public static CollectionProgress Progress => progress ??= CollectionStore.Load(GameSession.LocalPlayerIdValue);

    /// <summary>当前图鉴条目（动态计算，已缓存）。</summary>
    public static IReadOnlyList<ItemDefinition> Collectibles
    {
        get
        {
            if (collectibles == null)
                collectibles = new List<ItemDefinition>(catalog != null ? catalog.Collectibles(database) : Array.Empty<ItemDefinition>());

            return collectibles;
        }
    }

    /// <summary>图鉴条目总数。</summary>
    public static int TotalCount => Collectibles.Count;

    /// <summary>已点亮数量（忽略孤儿 ID）。</summary>
    public static int LitCount => Progress.LitCountWithin(ValidIds());

    /// <summary>已获得数量（忽略孤儿 ID）。</summary>
    public static int ObtainedCount => Progress.ObtainedCountWithin(ValidIds());

    /// <summary>注入依赖。场景初始化时调用，重复调用安全。</summary>
    public static void Configure(ItemDatabase itemDatabase, CollectionCatalog collectionCatalog)
    {
        database = itemDatabase;
        catalog = collectionCatalog;
        collectibles = null;
    }

    /// <summary>
    /// 撤离结算：把本局拾取的战利品计入收藏。
    /// 返回本次新获得资格的数量。
    ///
    /// 只应传入 <c>RaidBag.Loot</c>（本局拾取清单），**不要传 carry 数组**
    /// —— 后者含玩家带入地牢的初始装备，不算「从局内带出」。
    /// </summary>
    public static int RecordRaidLoot(IReadOnlyList<ItemInstance> loot)
    {
        if (loot == null || loot.Count == 0)
            return 0;

        var added = 0;
        foreach (var instance in loot)
        {
            var definition = instance?.Definition;
            if (definition == null)
                continue;

            if (!IsCollectible(definition))
                continue;

            if (Progress.Obtain(definition.Id))
                added++;
        }

        if (added > 0)
        {
            CollectionStore.Save(Progress);
            CollectionStore.Flush();
            ProgressSaved?.Invoke();
        }

        return added;
    }

    /// <summary>
    /// 点亮一件已获得的收藏品并发放奖励。
    /// 成功时 <paramref name="reward"/> 为本次发放的星币总额（含可能的全收集奖励）。
    ///
    /// 单件奖励与全收集奖励**分两次**广播 <see cref="RewardGranted"/>，
    /// 便于 UI 分别提示（点亮获得 / 全收集达成）。
    /// </summary>
    public static bool TryLight(string id, out int reward)
    {
        reward = 0;

        if (!Progress.TryLight(id))
            return false;

        var definition = FindCollectible(id);
        var itemReward = definition != null && catalog != null ? catalog.RewardFor(definition.Rarity) : 0;

        // 全收集判定：以当前图鉴总数为基准（动态），而非写死的 16。
        var allCollected = false;
        if (!Progress.AllCollectedRewarded && TotalCount > 0 && LitCount >= TotalCount)
        {
            Progress.AllCollectedRewarded = true;
            allCollected = true;
        }

        CollectionStore.Save(Progress);
        CollectionStore.Flush();
        ProgressSaved?.Invoke();

        // 先发单件奖励，再单独发全收集奖励。
        if (itemReward > 0)
        {
            reward += itemReward;
            RewardGranted?.Invoke(itemReward, CollectionRewardKind.Item);
        }

        if (allCollected && catalog != null && catalog.AllCollectedReward > 0)
        {
            reward += catalog.AllCollectedReward;
            RewardGranted?.Invoke(catalog.AllCollectedReward, CollectionRewardKind.AllCollected);
        }

        return true;
    }

    /// <summary>查询某件物品当前的三态。</summary>
    public static CollectionState StateOf(string id)
    {
        if (Progress.IsLit(id))
            return CollectionState.Lit;

        return Progress.IsObtained(id) ? CollectionState.Obtained : CollectionState.Locked;
    }

    /// <summary>
    /// 丢弃内存进度，下次访问按当前身份重新从缓存加载。
    /// 登录切号 / 服务端快照落地后由云同步服务调用。
    /// </summary>
    public static void InvalidateProgress() => progress = null;

    /// <summary>测试用：丢弃全部运行时状态（存档不受影响）。</summary>
    public static void ResetForTests()
    {
        database = null;
        catalog = null;
        progress = null;
        collectibles = null;
        RewardGranted = null;
        ProgressSaved = null;
    }

    private static bool IsCollectible(ItemDefinition definition)
    {
        foreach (var entry in Collectibles)
        {
            if (entry == definition)
                return true;
        }

        return false;
    }

    private static ItemDefinition FindCollectible(string id)
    {
        foreach (var entry in Collectibles)
        {
            if (entry != null && entry.Id == id)
                return entry;
        }

        return null;
    }

    private static IEnumerable<string> ValidIds()
    {
        foreach (var entry in Collectibles)
        {
            if (entry != null)
                yield return entry.Id;
        }
    }
}
