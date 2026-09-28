using NUnit.Framework;
using UnityEngine;

public sealed class TowerConfigTests
{
    private TowerConfig config;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<TowerConfig>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(config);
    }

    [Test]
    public void NewInstance_UsesExpectedDefaults()
    {
        Assert.That(config.HealthGrowthPerFloor, Is.EqualTo(0.15f).Within(0.0001f));
        Assert.That(config.DamageGrowthPerFloor, Is.EqualTo(0.10f).Within(0.0001f));
        Assert.That(config.StartingEnemiesPerFloor, Is.EqualTo(3));
        Assert.That(config.MaxEnemiesPerFloor, Is.EqualTo(12));
        Assert.That(config.RetreatEveryFloors, Is.EqualTo(5));
    }

    [Test]
    public void NewInstance_HasEmptyPoolAndNoLootPool()
    {
        Assert.That(config.EnemyPool.Count, Is.Zero);
        Assert.That(config.LootPool, Is.Null);
    }

    [Test]
    public void Configure_StoresPoolAndLootPool()
    {
        var prefab = new GameObject("Enemy");
        var loot = ScriptableObject.CreateInstance<LootPool>();

        config.Configure(
            new[] { new TowerConfig.EnemyWeight { prefab = prefab, weight = 3 } },
            loot);

        Assert.That(config.EnemyPool.Count, Is.EqualTo(1));
        Assert.That(config.EnemyPool[0].weight, Is.EqualTo(3));
        Assert.That(config.LootPool, Is.SameAs(loot));

        Object.DestroyImmediate(prefab);
        Object.DestroyImmediate(loot);
    }
}
