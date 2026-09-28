using NUnit.Framework;

/// <summary>
/// <see cref="RaidBag"/> 的携带与局内装备行为。
/// 这是「会丢的东西」：死亡清空，撤离并入仓库。
/// </summary>
public sealed class RaidBagTests
{
    [Test]
    public void TryAddToCarry_PlacesItemAndRaisesChangedOnce()
    {
        var bag = new RaidBag();
        var item = TestItems.WeaponInstance();
        var changedCount = 0;
        bag.Changed += () => changedCount++;

        Assert.That(bag.TryAddToCarry(item), Is.True);
        Assert.That(bag.GetCarry(0), Is.SameAs(item));
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void TryAddToCarry_RejectsWhenFull()
    {
        var bag = new RaidBag();
        Assert.That(RaidBag.CarryCapacity, Is.EqualTo(20));

        for (var i = 0; i < RaidBag.CarryCapacity; i++)
            Assert.That(bag.TryAddToCarry(TestItems.WeaponInstance()), Is.True);

        Assert.That(bag.GetCarry(RaidBag.CarryCapacity - 1), Is.Not.Null);
        Assert.That(bag.TryAddToCarry(TestItems.WeaponInstance()), Is.False);
    }

    [Test]
    public void RecordLoot_KeepsPickupOrder()
    {
        var bag = new RaidBag();
        var first = TestItems.WeaponInstance("A");
        var second = TestItems.WeaponInstance("B");

        bag.RecordLoot(first);
        bag.RecordLoot(null);
        bag.RecordLoot(second);

        Assert.That(bag.Loot.Count, Is.EqualTo(2));
        Assert.That(bag.Loot[0], Is.SameAs(first));
        Assert.That(bag.Loot[1], Is.SameAs(second));
    }

    [Test]
    public void ClearAll_EmptiesCarryAndLoot()
    {
        var bag = new RaidBag();
        bag.TryAddToCarry(TestItems.WeaponInstance());
        bag.RecordLoot(TestItems.WeaponInstance());

        bag.ClearAll();

        Assert.That(bag.CarryCount(), Is.Zero);
        Assert.That(bag.Loot, Is.Empty);
    }

    [Test]
    public void ClearLoot_KeepsCarry()
    {
        var bag = new RaidBag();
        bag.TryAddToCarry(TestItems.WeaponInstance());
        bag.RecordLoot(TestItems.WeaponInstance());

        bag.ClearLoot();

        Assert.That(bag.Loot, Is.Empty);
        Assert.That(bag.CarryCount(), Is.EqualTo(1));
    }
}
