/// <summary>
/// 装备槽类型。武器不再区分近战 / 远程：主手与副手在逻辑上完全等价，
/// 任意武器都能放进任意一个武器槽，具体哪把在用由 <see cref="Inventory"/> 按槽位顺序遍历决定。
/// </summary>
public enum EquipmentSlotType
{
    None = 0,
    MainHand = 1,
    OffHand = 2,
    Helmet = 3,
    BodyArmor = 4,
    Boots = 5,
}
