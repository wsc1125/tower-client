using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class FloorGeneratorTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (var item in created)
        {
            if (item != null)
                UnityEngine.Object.DestroyImmediate(item);
        }

        created.Clear();
    }

    private GameObject CreatePrefab(string name)
    {
        var go = new GameObject(name);
        created.Add(go);
        return go;
    }

    private LootTable CreateTable()
    {
        var table = ScriptableObject.CreateInstance<LootTable>();
        created.Add(table);
        return table;
    }

    // ---------- ResolveEnemyCount ----------

    [Test]
    public void ResolveEnemyCount_GrowsEveryTwoFloors()
    {
        Assert.That(FloorGenerator.ResolveEnemyCount(1, 2, 6), Is.EqualTo(1));
        Assert.That(FloorGenerator.ResolveEnemyCount(2, 2, 6), Is.EqualTo(1));
        Assert.That(FloorGenerator.ResolveEnemyCount(3, 2, 6), Is.EqualTo(2));
        Assert.That(FloorGenerator.ResolveEnemyCount(5, 2, 6), Is.EqualTo(3));
    }

    [Test]
    public void ResolveEnemyCount_ClampsToMaximum()
    {
        Assert.That(FloorGenerator.ResolveEnemyCount(11, 2, 6), Is.EqualTo(6));
        Assert.That(FloorGenerator.ResolveEnemyCount(20, 2, 6), Is.EqualTo(6));
        Assert.That(FloorGenerator.ResolveEnemyCount(100, 2, 6), Is.EqualTo(6));
    }

    [Test]
    public void ResolveEnemyCount_ClampsFloorToOne()
    {
        Assert.That(FloorGenerator.ResolveEnemyCount(0, 2, 6), Is.EqualTo(1));
        Assert.That(FloorGenerator.ResolveEnemyCount(-5, 2, 6), Is.EqualTo(1));
    }

    // ---------- 难度系数 ----------

    [Test]
    public void ResolveHealthMultiplier_IsOneOnFirstFloor()
    {
        Assert.That(FloorGenerator.ResolveHealthMultiplier(1, 0.15f), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void ResolveHealthMultiplier_GrowsLinearly()
    {
        Assert.That(FloorGenerator.ResolveHealthMultiplier(10, 0.15f), Is.EqualTo(2.35f).Within(0.0001f));
        Assert.That(FloorGenerator.ResolveHealthMultiplier(20, 0.15f), Is.EqualTo(3.85f).Within(0.0001f));
    }

    [Test]
    public void ResolveDamageMultiplier_GrowsLinearly()
    {
        Assert.That(FloorGenerator.ResolveDamageMultiplier(1, 0.10f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(FloorGenerator.ResolveDamageMultiplier(10, 0.10f), Is.EqualTo(1.90f).Within(0.0001f));
        Assert.That(FloorGenerator.ResolveDamageMultiplier(20, 0.10f), Is.EqualTo(2.90f).Within(0.0001f));
    }

    // ---------- 撤退层 ----------

    [Test]
    public void IsRetreatFloor_TrueOnMultiples()
    {
        Assert.That(FloorGenerator.IsRetreatFloor(5, 5), Is.True);
        Assert.That(FloorGenerator.IsRetreatFloor(10, 5), Is.True);
        Assert.That(FloorGenerator.IsRetreatFloor(15, 5), Is.True);
    }

    [Test]
    public void IsRetreatFloor_FalseOnNonMultiples()
    {
        Assert.That(FloorGenerator.IsRetreatFloor(1, 5), Is.False);
        Assert.That(FloorGenerator.IsRetreatFloor(4, 5), Is.False);
        Assert.That(FloorGenerator.IsRetreatFloor(6, 5), Is.False);
    }

    [Test]
    public void IsRetreatFloor_FalseWhenIntervalIsNotPositive()
    {
        Assert.That(FloorGenerator.IsRetreatFloor(5, 0), Is.False);
        Assert.That(FloorGenerator.IsRetreatFloor(5, -1), Is.False);
    }

    // ---------- 掉落分档 ----------

    [Test]
    public void ResolveLootTable_PicksHighestTierNotAboveFloor()
    {
        var tier1 = CreateTable();
        var tier2 = CreateTable();
        var tier5 = CreateTable();
        var tiers = new List<TowerConfig.LootTier>
        {
            new TowerConfig.LootTier { fromFloor = 1, table = tier1 },
            new TowerConfig.LootTier { fromFloor = 5, table = tier2 },
            new TowerConfig.LootTier { fromFloor = 20, table = tier5 },
        };

        Assert.That(FloorGenerator.ResolveLootTable(1, tiers), Is.SameAs(tier1));
        Assert.That(FloorGenerator.ResolveLootTable(4, tiers), Is.SameAs(tier1));
        Assert.That(FloorGenerator.ResolveLootTable(5, tiers), Is.SameAs(tier2));
        Assert.That(FloorGenerator.ResolveLootTable(19, tiers), Is.SameAs(tier2));
        Assert.That(FloorGenerator.ResolveLootTable(20, tiers), Is.SameAs(tier5));
        Assert.That(FloorGenerator.ResolveLootTable(100, tiers), Is.SameAs(tier5));
    }

    [Test]
    public void ResolveLootTable_ReturnsNullWhenNoTierMatches()
    {
        var tier = CreateTable();
        var tiers = new List<TowerConfig.LootTier>
        {
            new TowerConfig.LootTier { fromFloor = 5, table = tier },
        };

        Assert.That(FloorGenerator.ResolveLootTable(1, tiers), Is.Null);
        Assert.That(FloorGenerator.ResolveLootTable(1, null), Is.Null);
    }

    // ---------- 敌人抽取 ----------

    [Test]
    public void PickEnemy_IsReproducibleWithSameSeed()
    {
        var pool = new List<TowerConfig.EnemyWeight>
        {
            new TowerConfig.EnemyWeight { prefab = CreatePrefab("A"), weight = 1 },
            new TowerConfig.EnemyWeight { prefab = CreatePrefab("B"), weight = 1 },
            new TowerConfig.EnemyWeight { prefab = CreatePrefab("C"), weight = 1 },
        };

        var first = new List<string>();
        var second = new List<string>();
        var randomA = new System.Random(12345);
        var randomB = new System.Random(12345);

        for (var i = 0; i < 20; i++)
        {
            first.Add(FloorGenerator.PickEnemy(pool, randomA).name);
            second.Add(FloorGenerator.PickEnemy(pool, randomB).name);
        }

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void PickEnemy_NeverPicksZeroWeightEntries()
    {
        var never = CreatePrefab("Never");
        var always = CreatePrefab("Always");
        var pool = new List<TowerConfig.EnemyWeight>
        {
            new TowerConfig.EnemyWeight { prefab = never, weight = 0 },
            new TowerConfig.EnemyWeight { prefab = always, weight = 5 },
        };

        var random = new System.Random(7);
        for (var i = 0; i < 50; i++)
            Assert.That(FloorGenerator.PickEnemy(pool, random), Is.SameAs(always));
    }

    [Test]
    public void PickEnemy_ReturnsNullForEmptyOrInvalidPool()
    {
        Assert.That(FloorGenerator.PickEnemy(null, new System.Random(1)), Is.Null);
        Assert.That(FloorGenerator.PickEnemy(new List<TowerConfig.EnemyWeight>(), new System.Random(1)), Is.Null);

        var pool = new List<TowerConfig.EnemyWeight>
        {
            new TowerConfig.EnemyWeight { prefab = null, weight = 3 },
            new TowerConfig.EnemyWeight { prefab = CreatePrefab("Zero"), weight = 0 },
        };
        Assert.That(FloorGenerator.PickEnemy(pool, new System.Random(1)), Is.Null);
    }

    // ---------- Generate ----------

    [Test]
    public void Generate_ProducesEnemyCountMatchingResolve()
    {
        var config = ScriptableObject.CreateInstance<TowerConfig>();
        created.Add(config);
        config.Configure(
            new[] { new TowerConfig.EnemyWeight { prefab = CreatePrefab("A"), weight = 1 } },
            new[] { new TowerConfig.LootTier { fromFloor = 1, table = CreateTable() } });

        var plan = FloorGenerator.Generate(3, config, new System.Random(1));

        Assert.That(plan.Floor, Is.EqualTo(3));
        Assert.That(plan.Enemies.Length, Is.EqualTo(FloorGenerator.ResolveEnemyCount(3, config.FloorsPerExtraEnemy, config.MaxEnemiesPerFloor)));
        Assert.That(plan.IsRetreatFloor, Is.False);
        Assert.That(plan.LootTable, Is.SameAs(config.LootTiers[0].table));
    }

    [Test]
    public void Generate_MarksRetreatFloorAndClampsFloor()
    {
        var config = ScriptableObject.CreateInstance<TowerConfig>();
        created.Add(config);
        config.Configure(
            new[] { new TowerConfig.EnemyWeight { prefab = CreatePrefab("A"), weight = 1 } },
            new[] { new TowerConfig.LootTier { fromFloor = 1, table = CreateTable() } });

        Assert.That(FloorGenerator.Generate(5, config, new System.Random(1)).IsRetreatFloor, Is.True);
        Assert.That(FloorGenerator.Generate(0, config, new System.Random(1)).Floor, Is.EqualTo(1));
    }

    [Test]
    public void Generate_HandlesNullConfig()
    {
        var plan = FloorGenerator.Generate(1, null, new System.Random(1));

        Assert.That(plan.Floor, Is.EqualTo(1));
        Assert.That(plan.Enemies, Is.Empty);
        Assert.That(plan.LootTable, Is.Null);
    }
}
