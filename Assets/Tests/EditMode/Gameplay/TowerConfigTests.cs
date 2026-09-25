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
        Assert.That(config.MaxEnemiesPerFloor, Is.EqualTo(6));
        Assert.That(config.RetreatEveryFloors, Is.EqualTo(5));
    }

    [Test]
    public void NewInstance_HasEmptyPoolAndTiers()
    {
        Assert.That(config.EnemyPool.Count, Is.Zero);
        Assert.That(config.LootTiers.Count, Is.Zero);
    }

    [Test]
    public void Configure_StoresPoolAndTiers()
    {
        var prefab = new GameObject("Enemy");
        var table = ScriptableObject.CreateInstance<LootTable>();

        config.Configure(
            new[] { new TowerConfig.EnemyWeight { prefab = prefab, weight = 3 } },
            new[] { new TowerConfig.LootTier { fromFloor = 1, table = table } });

        Assert.That(config.EnemyPool.Count, Is.EqualTo(1));
        Assert.That(config.EnemyPool[0].weight, Is.EqualTo(3));
        Assert.That(config.LootTiers.Count, Is.EqualTo(1));
        Assert.That(config.LootTiers[0].table, Is.SameAs(table));

        Object.DestroyImmediate(prefab);
        Object.DestroyImmediate(table);
    }
}
