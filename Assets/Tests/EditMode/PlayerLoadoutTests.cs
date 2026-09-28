using NUnit.Framework;

/// <summary>
/// <see cref="PlayerLoadout"/> 的仓库与装备槽行为。
/// 这是局外资产，死亡不会动它，因此这里也覆盖「清空只发生在显式调用」。
/// </summary>
public sealed class PlayerLoadoutTests
{
    [Test]
    public void TryAddToWarehouse_PlacesItemAndRaisesChangedOnce()
    {
        var loadout = new PlayerLoadout();
        var item = TestItems.WeaponInstance();
        var changedCount = 0;
        loadout.Changed += () => changedCount++;

        Assert.That(loadout.TryAddToWarehouse(item), Is.True);
        Assert.That(loadout.GetWarehouse(0), Is.SameAs(item));
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void TryAddToWarehouse_RejectsWhenFull()
    {
        var loadout = new PlayerLoadout();
        Assert.That(PlayerLoadout.WarehouseCapacity, Is.EqualTo(72));

        for (var i = 0; i < PlayerLoadout.WarehouseCapacity; i++)
            Assert.That(loadout.TryAddToWarehouse(TestItems.WeaponInstance()), Is.True);

        Assert.That(loadout.GetWarehouse(PlayerLoadout.WarehouseCapacity - 1), Is.Not.Null);
        Assert.That(loadout.TryAddToWarehouse(TestItems.WeaponInstance()), Is.False);
    }

    [Test]
    public void TryAddToWarehouse_RejectsNull()
    {
        var loadout = new PlayerLoadout();
        Assert.That(loadout.TryAddToWarehouse(null), Is.False);
    }

    [Test]
    public void WarehouseCount_CountsOnlyOccupiedCells()
    {
        var loadout = new PlayerLoadout();
        loadout.TryAddToWarehouse(TestItems.WeaponInstance());
        loadout.TryAddToWarehouse(TestItems.WeaponInstance());

        Assert.That(loadout.WarehouseCount(), Is.EqualTo(2));
    }

    [Test]
    public void TryRemoveFromWarehouse_ClearsCell()
    {
        var loadout = new PlayerLoadout();
        loadout.TryAddToWarehouse(TestItems.WeaponInstance());

        Assert.That(loadout.TryRemoveFromWarehouse(0), Is.True);
        Assert.That(loadout.GetWarehouse(0), Is.Null);
        Assert.That(loadout.TryRemoveFromWarehouse(0), Is.False);
    }

    [Test]
    public void TrySwapInWarehouse_SwapsCells()
    {
        var loadout = new PlayerLoadout();
        var first = TestItems.WeaponInstance("A");
        var second = TestItems.WeaponInstance("B");
        loadout.SetWarehouse(0, first);
        loadout.SetWarehouse(1, second);

        Assert.That(loadout.TrySwapInWarehouse(0, 1), Is.True);
        Assert.That(loadout.GetWarehouse(0), Is.SameAs(second));
        Assert.That(loadout.GetWarehouse(1), Is.SameAs(first));
    }

    [Test]
    public void TrySwapWarehouseAndEquipment_EquipsAndUnequips()
    {
        var loadout = new PlayerLoadout();
        var weapon = TestItems.WeaponInstance();
        loadout.SetWarehouse(0, weapon);

        Assert.That(loadout.TrySwapWarehouseAndEquipment(0, EquipmentSlotType.MainHand), Is.True);
        Assert.That(loadout.GetWarehouse(0), Is.Null);
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(weapon));

        // 再换回来：装备回到原仓库格。
        Assert.That(loadout.TrySwapWarehouseAndEquipment(0, EquipmentSlotType.MainHand), Is.True);
        Assert.That(loadout.GetWarehouse(0), Is.SameAs(weapon));
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
    }

    [Test]
    public void TrySwapWarehouseAndEquipment_SwapsWhenBothOccupied()
    {
        var loadout = new PlayerLoadout();
        var incoming = TestItems.WeaponInstance("新");
        var outgoing = TestItems.WeaponInstance("旧");
        loadout.SetWarehouse(0, incoming);
        loadout.Equipment.Set(EquipmentSlotType.MainHand, outgoing);

        Assert.That(loadout.TrySwapWarehouseAndEquipment(0, EquipmentSlotType.MainHand), Is.True);
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(incoming));
        Assert.That(loadout.GetWarehouse(0), Is.SameAs(outgoing));
    }

    [Test]
    public void TrySwapWarehouseAndEquipment_RejectsMismatchedSlot()
    {
        var loadout = new PlayerLoadout();
        loadout.SetWarehouse(0, TestItems.WeaponInstance());

        // 武器不能放进防具槽。
        Assert.That(loadout.TrySwapWarehouseAndEquipment(0, EquipmentSlotType.Helmet), Is.False);
        Assert.That(loadout.GetWarehouse(0), Is.Not.Null);
    }

    [Test]
    public void TrySwapWarehouseAndEquipment_RejectsWhenBothEmpty()
    {
        var loadout = new PlayerLoadout();
        Assert.That(loadout.TrySwapWarehouseAndEquipment(0, EquipmentSlotType.MainHand), Is.False);
    }

    [Test]
    public void TrySwapEquipment_SwapsTwoWeaponSlots()
    {
        var loadout = new PlayerLoadout();
        var main = TestItems.WeaponInstance("主");
        var off = TestItems.WeaponInstance("副");
        loadout.Equipment.Set(EquipmentSlotType.MainHand, main);
        loadout.Equipment.Set(EquipmentSlotType.OffHand, off);

        Assert.That(loadout.TrySwapEquipment(EquipmentSlotType.MainHand, EquipmentSlotType.OffHand), Is.True);
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.SameAs(off));
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.OffHand), Is.SameAs(main));
    }

    [Test]
    public void TrySwapEquipment_RejectsArmorIntoWeaponSlot()
    {
        var loadout = new PlayerLoadout();
        loadout.Equipment.Set(EquipmentSlotType.Helmet, TestItems.ArmorInstance(EquipmentSlotType.Helmet));

        Assert.That(loadout.TrySwapEquipment(EquipmentSlotType.Helmet, EquipmentSlotType.MainHand), Is.False);
    }

    [Test]
    public void TrySwapEquipment_RejectsSameSlot()
    {
        var loadout = new PlayerLoadout();
        loadout.Equipment.Set(EquipmentSlotType.MainHand, TestItems.WeaponInstance());

        Assert.That(loadout.TrySwapEquipment(EquipmentSlotType.MainHand, EquipmentSlotType.MainHand), Is.False);
    }

    [Test]
    public void InvalidIndexes_AreRejectedWithoutEvents()
    {
        var loadout = new PlayerLoadout();
        var changes = 0;
        loadout.Changed += () => changes++;

        Assert.That(loadout.TryRemoveFromWarehouse(-1), Is.False);
        Assert.That(loadout.TryRemoveFromWarehouse(PlayerLoadout.WarehouseCapacity), Is.False);
        Assert.That(loadout.TrySwapInWarehouse(-1, PlayerLoadout.WarehouseCapacity), Is.False);
        Assert.That(loadout.TrySwapWarehouseAndEquipment(PlayerLoadout.WarehouseCapacity, EquipmentSlotType.MainHand), Is.False);
        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void ClearAll_EmptiesWarehouseAndEquipment()
    {
        var loadout = new PlayerLoadout();
        loadout.TryAddToWarehouse(TestItems.WeaponInstance());
        loadout.Equipment.Set(EquipmentSlotType.MainHand, TestItems.WeaponInstance());

        loadout.ClearAll();

        Assert.That(loadout.WarehouseCount(), Is.Zero);
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
    }
}
