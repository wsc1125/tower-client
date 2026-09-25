using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class TowerRunManagerTests
{
    private GameObject root;
    private TowerRunManager manager;
    private TowerConfig config;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<TowerConfig>();
        config.Configure(
            new[] { new TowerConfig.EnemyWeight { prefab = null, weight = 1 } },
            new[] { new TowerConfig.LootTier { fromFloor = 1, table = null } });

        root = new GameObject("TowerRunManager");
        manager = root.AddComponent<TowerRunManager>();

        var serialized = new UnityEditor.SerializedObject(manager);
        serialized.FindProperty("config").objectReferenceValue = config;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(config);
    }

    [Test]
    public void State_StartsAtFloorOne()
    {
        Assert.That(manager.State.CurrentFloor, Is.EqualTo(1));
        Assert.That(manager.State.IsFinished, Is.False);
    }

    [Test]
    public void BuildResultEntries_MapsCollectedLoot()
    {
        var first = ScriptableObject.CreateInstance<ItemDefinition>();
        var second = ScriptableObject.CreateInstance<ItemDefinition>();

        manager.State.BeginRun();
        manager.State.RecordLoot(first);
        manager.State.RecordLoot(second);

        var entries = manager.BuildResultEntries();

        Assert.That(entries.Count, Is.EqualTo(2));

        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
    }

    [Test]
    public void BuildResultEntries_ReturnsEmptyWhenNoLoot()
    {
        manager.State.BeginRun();

        Assert.That(manager.BuildResultEntries().Count, Is.Zero);
    }

    [Test]
    public void TotalLootValue_SumsItemValues()
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var serialized = new UnityEditor.SerializedObject(item);
        serialized.FindProperty("value").intValue = 120;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        manager.State.BeginRun();
        manager.State.RecordLoot(item);
        manager.State.RecordLoot(item);

        Assert.That(manager.TotalLootValue(), Is.EqualTo(240));

        Object.DestroyImmediate(item);
    }

    [Test]
    public void HandlePortalEntered_IgnoresWhenRunFinished()
    {
        manager.State.BeginRun();
        manager.State.Finish();

        Assert.DoesNotThrow(() => manager.HandlePortalEntered(PortalKind.Next));
        Assert.That(manager.State.CurrentFloor, Is.EqualTo(1));
    }

    [Test]
    public void AbortRun_FinishesAndClearsLoot()
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        manager.State.BeginRun();
        manager.State.RecordLoot(item);

        manager.AbortRun();

        Assert.That(manager.State.IsFinished, Is.True);
        Assert.That(manager.State.CollectedLoot.Count, Is.Zero);

        Object.DestroyImmediate(item);
    }

    [Test]
    public void IsFloorCleared_TrueWhenNoDamageablesAlive()
    {
        // EditMode 下 AddComponent 会立即跑 Awake，但不会跑 Start，
        // 因此不会真的生成房间与敌人，场上没有 IDamageable，判定为已清空。
        Assert.That(manager.IsFloorCleared(), Is.True);
    }

    [Test]
    public void IsFloorCleared_FalseWhenSwarmIsAlive()
    {
        var swarmRoot = new GameObject("Swarm");
        swarmRoot.AddComponent<Rigidbody2D>();
        swarmRoot.AddComponent<SwarmChaser2D>();

        Assert.That(manager.IsFloorCleared(), Is.False);

        Object.DestroyImmediate(swarmRoot);
    }
}
