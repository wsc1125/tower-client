using NUnit.Framework;

/// <summary>
/// <see cref="LoadoutService"/> 的搬运与出征 / 撤离结算。
/// 这是「武器带进局内」与「战利品带出局外」两条链路的唯一实现处，因此覆盖最重。
/// </summary>
public sealed class LoadoutServiceTests
{
    // ==================== 局外搬运 ====================

    [Test]
    public void Move_WarehouseToEquipment_Equips()
    {
        var loadout = new PlayerLoadout();
        var weapon = TestItems.WeaponInstance();
        loadout.SetWarehouse(3, weapon);

        Assert.That(LoadoutService.Move(loadout, SlotRef.Warehouse(3), SlotRef.Equipment(EquipmentSlotType.MainHand)), Is.True);
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(weapon));
        Assert.That(loadout.GetWarehouse(3), Is.Null);
    }

    [Test]
    public void Move_EquipmentToWarehouse_Unequips()
    {
        var loadout = new PlayerLoadout();
        var weapon = TestItems.WeaponInstance();
        loadout.Equipment.Set(EquipmentSlotType.MainHand, weapon);

        Assert.That(LoadoutService.Move(loadout, SlotRef.Equipment(EquipmentSlotType.MainHand), SlotRef.Warehouse(5)), Is.True);
        Assert.That(loadout.GetWarehouse(5), Is.SameAs(weapon));
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
    }

    [Test]
    public void Move_SameSlot_IsRejected()
    {
        var loadout = new PlayerLoadout();
        loadout.SetWarehouse(0, TestItems.WeaponInstance());

        Assert.That(LoadoutService.Move(loadout, SlotRef.Warehouse(0), SlotRef.Warehouse(0)), Is.False);
    }

    [Test]
    public void Move_EmptySource_IsRejected()
    {
        var loadout = new PlayerLoadout();

        Assert.That(LoadoutService.Move(loadout, SlotRef.Warehouse(0), SlotRef.Warehouse(1)), Is.False);
    }

    [Test]
    public void Move_CarryIsNotValidOutOfRaid()
    {
        var loadout = new PlayerLoadout();
        loadout.SetWarehouse(0, TestItems.WeaponInstance());

        // 局外没有「携带」分区，跨分区搬运必须失败。
        Assert.That(LoadoutService.Move(loadout, SlotRef.Warehouse(0), SlotRef.Carry(0)), Is.False);
    }

    // ==================== 局内搬运 ====================

    [Test]
    public void Move_CarryToEquipment_EquipsInRaid()
    {
        var bag = new RaidBag();
        var loadout = new PlayerLoadout();
        var weapon = TestItems.WeaponInstance();
        bag.SetCarry(2, weapon);

        Assert.That(LoadoutService.Move(bag, loadout, SlotRef.Carry(2), SlotRef.Equipment(EquipmentSlotType.OffHand)), Is.True);
        // 装备槽与局外是同一份，因此装备后落在 loadout 上。
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.OffHand), Is.SameAs(weapon));
        Assert.That(bag.GetCarry(2), Is.Null);
    }

    [Test]
    public void Move_WarehouseIsNotValidInRaid()
    {
        var bag = new RaidBag();
        var loadout = new PlayerLoadout();
        bag.SetCarry(0, TestItems.WeaponInstance());

        Assert.That(LoadoutService.Move(bag, loadout, SlotRef.Carry(0), SlotRef.Warehouse(0)), Is.False);
    }

    // ==================== 出征 ====================

    [Test]
    public void DeployToRaid_KeepsEquipmentSharedWithLoadout()
    {
        var loadout = new PlayerLoadout();
        var weapon = TestItems.WeaponInstance();
        var helmet = TestItems.ArmorInstance(EquipmentSlotType.Helmet);
        loadout.Equipment.Set(EquipmentSlotType.MainHand, weapon);
        loadout.Equipment.Set(EquipmentSlotType.Helmet, helmet);

        var bag = new RaidBag();
        LoadoutService.DeployToRaid(loadout, bag);

        // 装备槽不搬运：局内外是同一份，出征后局外装备槽原样保留。
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(weapon));
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.Helmet), Is.SameAs(helmet));
    }

    [Test]
    public void DeployToRaid_KeepsLoadoutEquipmentIntact()
    {
        var loadout = new PlayerLoadout();
        var weapon = TestItems.WeaponInstance();
        loadout.Equipment.Set(EquipmentSlotType.MainHand, weapon);

        var bag = new RaidBag();
        LoadoutService.DeployToRaid(loadout, bag);

        // 装备是「穿在身上出征」，不是从局外拿走。
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(weapon));
    }

    [Test]
    public void DeployToRaid_KeepsConfiguredCarryAndClearsLoot()
    {
        var loadout = new PlayerLoadout();
        var bag = new RaidBag();
        var configured = TestItems.WeaponInstance("主菜单配好的携带");
        bag.TryAddToCarry(configured);
        bag.RecordLoot(TestItems.WeaponInstance("上一局战利品"));

        LoadoutService.DeployToRaid(loadout, bag);

        // 携带格是玩家在主菜单配好的清单，出征时必须原样带进局内。
        Assert.That(bag.CarryCount(), Is.EqualTo(1));
        Assert.That(bag.GetCarry(0), Is.SameAs(configured));

        // 战利品清单属于上一局，出征时清空。
        Assert.That(bag.Loot, Is.Empty);
    }

    // ==================== 撤离结算 ====================

    [Test]
    public void ExtractToLoadout_MovesCarryIntoWarehouse()
    {
        var loadout = new PlayerLoadout();
        var bag = new RaidBag();
        var loot = TestItems.WeaponInstance("战利品");
        bag.TryAddToCarry(loot);

        var overflow = LoadoutService.ExtractToLoadout(bag, loadout);

        Assert.That(overflow, Is.Zero);
        Assert.That(loadout.GetWarehouse(0), Is.SameAs(loot));
        Assert.That(bag.CarryCount(), Is.Zero);
    }

    [Test]
    public void ExtractToLoadout_KeepsEquipmentAlreadyOnLoadout()
    {
        var loadout = new PlayerLoadout();
        var bag = new RaidBag();
        var equipped = TestItems.WeaponInstance("身上这把枪");
        loadout.Equipment.Set(EquipmentSlotType.MainHand, equipped);

        LoadoutService.ExtractToLoadout(bag, loadout);

        // 装备槽本来就是局外那一份，撤离不需要写回，且不会被清掉。
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(equipped));
    }

    [Test]
    public void ExtractToLoadout_ReportsOverflowWhenWarehouseFull()
    {
        var loadout = new PlayerLoadout();
        for (var i = 0; i < PlayerLoadout.WarehouseCapacity; i++)
            loadout.SetWarehouse(i, TestItems.WeaponInstance());

        var bag = new RaidBag();
        bag.TryAddToCarry(TestItems.WeaponInstance("放不下"));
        bag.TryAddToCarry(TestItems.WeaponInstance("也放不下"));

        var overflow = LoadoutService.ExtractToLoadout(bag, loadout);

        Assert.That(overflow, Is.EqualTo(2));
    }

    [Test]
    public void ExtractToLoadout_ClearsRaidBagAfterwards()
    {
        var loadout = new PlayerLoadout();
        var bag = new RaidBag();
        bag.TryAddToCarry(TestItems.WeaponInstance());
        bag.RecordLoot(TestItems.WeaponInstance());

        LoadoutService.ExtractToLoadout(bag, loadout);

        Assert.That(bag.CarryCount(), Is.Zero);
        Assert.That(bag.Loot, Is.Empty);
    }

    [Test]
    public void ExtractToLoadout_KeepsExistingWarehouseItems()
    {
        var loadout = new PlayerLoadout();
        var existing = TestItems.WeaponInstance("原有");
        loadout.SetWarehouse(0, existing);

        var bag = new RaidBag();
        bag.TryAddToCarry(TestItems.WeaponInstance("新"));

        LoadoutService.ExtractToLoadout(bag, loadout);

        Assert.That(loadout.GetWarehouse(0), Is.SameAs(existing));
        Assert.That(loadout.GetWarehouse(1), Is.Not.Null);
    }
}
