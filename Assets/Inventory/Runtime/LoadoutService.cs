/// <summary>
/// 搬运规则的唯一实现处：把「从哪一格拖到哪一格」翻译成具体的容器操作。
/// 旧实现把 20 多个 Try* 方法堆在 Inventory 上，调用方要自己判断分区组合；
/// 这里改为「一个入口 + 一张分区组合表」，新增分区时只改这一处。
///
/// 容器里存的是 <see cref="ItemInstance"/> 引用，因此搬运引用即搬运实例状态（耐久），
/// 不需要任何额外的状态同步。
/// </summary>
public static class LoadoutService
{
    /// <summary>
    /// 在局外把物品从 <paramref name="from"/> 搬到 <paramref name="to"/>。
    /// 任意一侧为空时等价于单向移动；两侧都有物品时等价于互换。
    /// 返回是否发生了实际变更。
    /// </summary>
    public static bool Move(PlayerLoadout loadout, SlotRef from, SlotRef to)
    {
        if (loadout == null || !from.IsValid || !to.IsValid || from.Equals(to))
            return false;

        switch (from.Area)
        {
            case SlotArea.Warehouse:
                switch (to.Area)
                {
                    case SlotArea.Warehouse:
                        return loadout.TrySwapInWarehouse(from.Index, to.Index);
                    case SlotArea.Equipment:
                        return loadout.TrySwapWarehouseAndEquipment(from.Index, to.EquipmentSlot);
                    default:
                        return false;
                }

            case SlotArea.Equipment:
                switch (to.Area)
                {
                    case SlotArea.Warehouse:
                        return loadout.TrySwapWarehouseAndEquipment(to.Index, from.EquipmentSlot);
                    case SlotArea.Equipment:
                        return loadout.TrySwapEquipment(from.EquipmentSlot, to.EquipmentSlot);
                    default:
                        return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// 在局内把物品从 <paramref name="from"/> 搬到 <paramref name="to"/>。
    /// 局内只有「携带」与「装备槽」两个分区，**仓库不参与** ——
    /// 携带里的东西只有在撤离成功时才由 <see cref="ExtractToLoadout"/> 并入仓库，
    /// 局内任何时刻都不能直接把携带搬进仓库（否则等于随身携带一个安全仓库）。
    ///
    /// 装备槽与局外是**同一份**（<see cref="PlayerLoadout.Equipment"/>），
    /// 因此这里对装备槽的操作直接落到局外容器上。
    /// </summary>
    public static bool Move(RaidBag bag, PlayerLoadout loadout, SlotRef from, SlotRef to)
    {
        if (bag == null || loadout == null || !from.IsValid || !to.IsValid || from.Equals(to))
            return false;

        // 局内不允许触碰仓库：涉及仓库的搬运一律拒绝。
        if (from.Area == SlotArea.Warehouse || to.Area == SlotArea.Warehouse)
            return false;

        switch (from.Area)
        {
            case SlotArea.Carry:
                switch (to.Area)
                {
                    case SlotArea.Carry:
                        return bag.TrySwapInCarry(from.Index, to.Index);
                    case SlotArea.Equipment:
                        return TrySwapCarryAndEquipment(bag, loadout, from.Index, to.EquipmentSlot);
                    default:
                        return false;
                }

            case SlotArea.Equipment:
                switch (to.Area)
                {
                    case SlotArea.Carry:
                        return TrySwapCarryAndEquipment(bag, loadout, to.Index, from.EquipmentSlot);
                    case SlotArea.Equipment:
                        return loadout.TrySwapEquipment(from.EquipmentSlot, to.EquipmentSlot);
                    default:
                        return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// 在局外装备配置面板里搬运：仓库 ↔ 装备槽 ↔ 携带格，以及同区互换。
    /// 局外是唯一允许「仓库 ↔ 携带」自由搬运的地方 —— 玩家在这里决定带什么进地牢。
    /// </summary>
    public static bool MoveInLoadout(RaidBag bag, PlayerLoadout loadout, SlotRef from, SlotRef to)
    {
        if (bag == null || loadout == null || !from.IsValid || !to.IsValid || from.Equals(to))
            return false;

        switch (from.Area)
        {
            case SlotArea.Carry:
                switch (to.Area)
                {
                    case SlotArea.Carry:
                        return bag.TrySwapInCarry(from.Index, to.Index);
                    case SlotArea.Equipment:
                        return TrySwapCarryAndEquipment(bag, loadout, from.Index, to.EquipmentSlot);
                    case SlotArea.Warehouse:
                        return TrySwapCarryAndWarehouse(bag, loadout, from.Index, to.Index);
                    default:
                        return false;
                }

            case SlotArea.Equipment:
                switch (to.Area)
                {
                    case SlotArea.Carry:
                        return TrySwapCarryAndEquipment(bag, loadout, to.Index, from.EquipmentSlot);
                    case SlotArea.Equipment:
                        return loadout.TrySwapEquipment(from.EquipmentSlot, to.EquipmentSlot);
                    case SlotArea.Warehouse:
                        return loadout.TrySwapWarehouseAndEquipment(to.Index, from.EquipmentSlot);
                    default:
                        return false;
                }

            case SlotArea.Warehouse:
                switch (to.Area)
                {
                    case SlotArea.Carry:
                        return TrySwapCarryAndWarehouse(bag, loadout, to.Index, from.Index);
                    case SlotArea.Equipment:
                        return loadout.TrySwapWarehouseAndEquipment(from.Index, to.EquipmentSlot);
                    case SlotArea.Warehouse:
                        return loadout.TrySwapInWarehouse(from.Index, to.Index);
                    default:
                        return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// 交换携带格与仓库格（任意一侧为空时等价于存入 / 取出）。
    /// 装备配置面板同时展示仓库与携带，玩家需要在这两者之间自由搬运，
    /// 因此这里把两个容器接起来；两侧都为空时不做任何事。
    /// </summary>
    public static bool TrySwapCarryAndWarehouse(RaidBag bag, PlayerLoadout loadout, int carryIndex, int warehouseIndex)
    {
        if (bag == null || loadout == null
            || !RaidBag.IsCarryIndex(carryIndex) || !PlayerLoadout.IsWarehouseIndex(warehouseIndex))
            return false;

        var incoming = bag.GetCarry(carryIndex);
        var outgoing = loadout.GetWarehouse(warehouseIndex);
        if (incoming == null && outgoing == null)
            return false;

        bag.SetCarry(carryIndex, outgoing);
        loadout.SetWarehouse(warehouseIndex, incoming);

        bag.NotifyChanged();
        loadout.NotifyChanged();
        return true;
    }

    /// <summary>
    /// 交换携带格与装备槽（任意一侧为空时等价于装备 / 卸下）。
    /// 放进装备槽的物品必须匹配槽类型；被换下的装备回到原携带格。
    /// 装备槽是局外那一份，因此变更会同时触发局内与局外的变更通知。
    /// </summary>
    public static bool TrySwapCarryAndEquipment(RaidBag bag, PlayerLoadout loadout, int carryIndex, EquipmentSlotType slot)
    {
        if (bag == null || loadout == null || !RaidBag.IsCarryIndex(carryIndex) || !EquipmentSlots.IsUsable(slot))
            return false;

        var incoming = bag.GetCarry(carryIndex);
        if (incoming != null && !EquipmentSlots.CanAccept(incoming, slot))
            return false;

        var outgoing = loadout.Equipment.Get(slot);
        if (incoming == null && outgoing == null)
            return false;

        bag.SetCarry(carryIndex, outgoing);
        loadout.Equipment.Set(slot, incoming);

        bag.NotifyChanged();
        loadout.NotifyChanged();
        return true;
    }

    /// <summary>
    /// 出征：清空上一局的战利品清单，**保留携带格**。
    ///
    /// 携带格是「玩家在主菜单配好的携带清单」，出征时原样带进局内，因此不能清空。
    /// 上一局的残留不需要在这里处理 —— 死亡走 <see cref="GameSession.AbandonRaid"/>、
    /// 撤离走 <see cref="ExtractToLoadout"/>，两者都会清空携带格，
    /// 所以出征时携带格里剩下的只可能是本次配置的内容。
    ///
    /// 装备槽**不需要搬运** —— 局内外共用同一份 <see cref="PlayerLoadout.Equipment"/>，
    /// 玩家在主菜单穿好的装备直接就是局内那身，局内磨损也直接落在同一实例上。
    /// </summary>
    public static void DeployToRaid(PlayerLoadout loadout, RaidBag bag)
    {
        if (loadout == null || bag == null)
            return;

        bag.ClearLoot();
        bag.NotifyChanged();
    }

    /// <summary>
    /// 撤离结算：把局内携带并入局外仓库。
    /// 装备槽不需要写回 —— 它本来就是局外那一份。
    /// 仓库放不下的物品会被丢弃并返回数量，由调用方决定是否提示玩家。
    /// </summary>
    public static int ExtractToLoadout(RaidBag bag, PlayerLoadout loadout)
    {
        if (bag == null || loadout == null)
            return 0;

        var overflow = 0;

        // 携带里的东西（含战利品）全部并入仓库。
        for (var i = 0; i < RaidBag.CarryCapacity; i++)
        {
            var item = bag.GetCarry(i);
            if (item == null)
                continue;

            if (!loadout.TryAddToWarehouse(item))
                overflow++;
        }

        bag.ClearAll();
        loadout.NotifyChanged();
        return overflow;
    }
}
