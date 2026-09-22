using System.Collections.Generic;

/// <summary>
/// 商店纯逻辑：从物品登记表生成可售目录（武器 + 护甲），并执行购买交易。
/// 不依赖 UI 与货币组件，便于在 EditMode 测试中直接验证。
/// </summary>
public static class ShopService
{
    /// <summary>
    /// 生成商店目录：登记表中所有「可装备」物品（排除 <see cref="EquipmentSlotType.None"/> 的消耗品）。
    /// 当前即武器与护甲，未来新增护甲会自动进入目录。
    /// </summary>
    public static List<ItemDefinition> GetCatalog(ItemDatabase database)
    {
        var catalog = new List<ItemDefinition>();
        if (database == null)
            return catalog;

        foreach (var item in database.Items)
        {
            if (item == null || item.EquipSlotType == EquipmentSlotType.None)
                continue;

            catalog.Add(item);
        }

        return catalog;
    }

    /// <summary>
    /// 执行购买：余额充足且背包有空位时，把物品放入背包并扣除余额。
    /// 返回是否成功；成功时 <paramref name="remaining"/> 为剩余余额，失败时保持 <paramref name="balance"/> 不变。
    /// </summary>
    public static bool TryPurchase(ItemDefinition item, Inventory model, int balance, out int remaining)
    {
        remaining = balance;
        if (item == null || model == null || item.BuyPrice <= 0)
            return false;

        if (balance < item.BuyPrice)
            return false;

        if (!model.TryAdd(item))
            return false;

        remaining = balance - item.BuyPrice;
        return true;
    }
}
