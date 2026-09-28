using System.Collections.Generic;

/// <summary>
/// 商店纯逻辑：从物品登记表生成可售目录（武器 + 护甲 + 消耗品），并执行购买交易。
/// 不依赖 UI 与货币组件，便于在 EditMode 测试中直接验证。
/// </summary>
public static class ShopService
{
    /// <summary>
    /// 生成商店目录：登记表中所有**已定价**（<see cref="ItemDefinition.BuyPrice"/> &gt; 0）的物品。
    ///
    /// 判定用「是否定价」而非「槽位是否为 None」—— 后者会把血包 / 甲修这类
    /// 不占装备槽的消耗品一并排除掉。纯战利品（废合金块等）天然 buyPrice = 0，
    /// 因此不需要额外维护一张「可售清单」。
    /// </summary>
    public static List<ItemDefinition> GetCatalog(ItemDatabase database)
    {
        var catalog = new List<ItemDefinition>();
        if (database == null)
            return catalog;

        foreach (var item in database.Items)
        {
            if (item == null || item.BuyPrice <= 0)
                continue;

            catalog.Add(item);
        }

        return catalog;
    }

    /// <summary>
    /// 执行购买：余额充足且仓库有空位时，把物品放入仓库并扣除余额。
    /// 返回是否成功；成功时 <paramref name="remaining"/> 为剩余余额，失败时保持 <paramref name="balance"/> 不变。
    ///
    /// 商店卖的是**模板**，入库时现场创建一个满耐久的物品实例 —— 因此买两件同款会得到两个独立实例。
    /// </summary>
    public static bool TryPurchase(ItemDefinition item, PlayerLoadout loadout, int balance, out int remaining)
    {
        remaining = balance;
        if (item == null || loadout == null || item.BuyPrice <= 0)
            return false;

        if (balance < item.BuyPrice)
            return false;

        if (!loadout.TryAddToWarehouse(ItemInstance.Create(item)))
            return false;

        remaining = balance - item.BuyPrice;
        return true;
    }
}
