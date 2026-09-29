using NUnit.Framework;
using UnityEngine;

public sealed class WorldRegistryTests
{
    [SetUp]
    public void SetUp()
    {
        WorldRegistry.ClearForTests();
    }

    [TearDown]
    public void TearDown()
    {
        WorldRegistry.ClearForTests();
    }

    [Test]
    public void RegisterDamageable_AddsItToDamageables()
    {
        var root = new GameObject("Enemy");
        root.AddComponent<Rigidbody2D>();
        var enemy = root.AddComponent<EnemyBrain2D>();

        WorldRegistry.Register(enemy);

        Assert.That(WorldRegistry.Damageables, Does.Contain(enemy));
        Object.DestroyImmediate(root);
    }

    [Test]
    public void UnregisterDamageable_RemovesItFromDamageables()
    {
        var root = new GameObject("Enemy");
        root.AddComponent<Rigidbody2D>();
        var enemy = root.AddComponent<EnemyBrain2D>();
        WorldRegistry.Register(enemy);

        WorldRegistry.Unregister(enemy);

        var contains = false;
        foreach (var target in WorldRegistry.Damageables)
            contains |= target == enemy;

        Assert.That(contains, Is.False);
        Object.DestroyImmediate(root);
    }

    [Test]
    public void RegisterDroppedItem_AddsItToDroppedItems()
    {
        var root = new GameObject("DroppedItem");
        var dropped = root.AddComponent<DroppedItem2D>();

        WorldRegistry.Register(dropped);

        Assert.That(WorldRegistry.DroppedItems, Does.Contain(dropped));
        Object.DestroyImmediate(root);
    }
}
