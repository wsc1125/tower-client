/// <summary>
/// 物品详情区第二行的统一格式化：装备槽类型 + 品质 + 价值（+ 耐久）。
/// 局外装备配置面板与局内背包面板共用，避免两处文案漂移。
/// </summary>
public static class ItemMetaFormatter
{
    /// <summary>
    /// 格式化物品详情。
    /// <paramref name="currentDurability"/> 传入当前剩余耐久时，防具会额外显示「耐久 x/y」；
    /// 传 -1（默认）表示不展示耐久（如商店里的模板）。
    /// </summary>
    public static string Format(ItemDefinition item, int currentDurability = -1)
    {
        if (item == null)
            return string.Empty;

        var rarity = UiTokens.RarityName(item.Rarity);
        var slot = FormatSlotType(item.EquipSlotType);
        var meta = string.IsNullOrEmpty(slot)
            ? rarity + "   ·   价值 " + item.Value
            : slot + "   ·   " + rarity + "   ·   价值 " + item.Value;

        if (item.Durability > 0 && currentDurability >= 0)
            meta += "   ·   耐久 " + currentDurability + "/" + item.Durability;

        return meta;
    }

    /// <summary>
    /// 主手与副手在逻辑上完全等价，任意武器都能装进任意一个武器槽，
    /// 因此这里统一显示「武器」，避免玩家误以为某把枪只能装副手。
    /// </summary>
    public static string FormatSlotType(EquipmentSlotType slot)
    {
        if (EquipmentSlots.IsWeaponSlot(slot))
            return "武器";

        switch (slot)
        {
            case EquipmentSlotType.Helmet:
                return "头盔";
            case EquipmentSlotType.BodyArmor:
                return "护甲";
            case EquipmentSlotType.Boots:
                return "战靴";
            default:
                return string.Empty;
        }
    }
}
