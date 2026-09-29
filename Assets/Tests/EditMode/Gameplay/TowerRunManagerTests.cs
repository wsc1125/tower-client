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
        WorldRegistry.ClearForTests();
        config = ScriptableObject.CreateInstance<TowerConfig>();
        config.Configure(
            new[] { new TowerConfig.EnemyWeight { prefab = null, weight = 1 } },
            null);

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
        WorldRegistry.ClearForTests();
        GameSession.ResetForTests();
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
        var first = TestItems.WeaponInstance("A");
        var second = TestItems.WeaponInstance("B");

        GameSession.Raid.RecordLoot(first);
        GameSession.Raid.RecordLoot(second);

        var entries = manager.BuildResultEntries();

        Assert.That(entries.Count, Is.EqualTo(2));

        Object.DestroyImmediate(first.Definition);
        Object.DestroyImmediate(second.Definition);
    }

    [Test]
    public void BuildResultEntries_ReturnsEmptyWhenNoLoot()
    {
        Assert.That(manager.BuildResultEntries().Count, Is.Zero);
    }

    [Test]
    public void TotalLootValue_SumsItemValues()
    {
        var item = TestItems.WeaponInstance("战利品", 120);

        GameSession.Raid.RecordLoot(item);
        GameSession.Raid.RecordLoot(item);

        Assert.That(manager.TotalLootValue(), Is.EqualTo(240));

        Object.DestroyImmediate(item.Definition);
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
    public void AbortRun_FinishesRun()
    {
        manager.State.BeginRun();

        manager.AbortRun();

        Assert.That(manager.State.IsFinished, Is.True);
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

    [Test]
    public void ResolveSpawnPosition_ReturnsDesiredWhenNoRoomBounds()
    {
        // 未解析出房间边界时不做钳制，原样返回。
        Assert.That(manager.HasRoomBounds, Is.False);
        Assert.That(manager.ResolveSpawnPosition(new Vector2(99f, -99f)), Is.EqualTo(new Vector2(99f, -99f)));
    }

    [Test]
    public void ResolveSpawnPosition_ClampsIntoRoomWithMargin()
    {
        // 房间 x:[-18.6, 21.1] y:[-17.1, 7.6]，margin 1.5
        SetRoomBounds(new Rect(-18.6f, -17.1f, 39.7f, 24.7f));

        var clamped = manager.ResolveSpawnPosition(new Vector2(100f, 100f));

        Assert.That(clamped.x, Is.EqualTo(21.1f - 1.5f).Within(0.001f));
        Assert.That(clamped.y, Is.EqualTo(7.6f - 1.5f).Within(0.001f));
    }

    [Test]
    public void ResolveSpawnPosition_KeepsPointAlreadyInside()
    {
        SetRoomBounds(new Rect(-18.6f, -17.1f, 39.7f, 24.7f));

        var inside = new Vector2(0f, 0f);

        Assert.That(manager.ResolveSpawnPosition(inside), Is.EqualTo(inside));
    }

    [Test]
    public void ResolveSpawnPosition_FallsBackToCenterWhenRoomSmallerThanMargin()
    {
        // 房间比两倍 margin 还小时，退化为房间中心，避免 Min > Max。
        SetRoomBounds(new Rect(0f, 0f, 2f, 2f));

        var clamped = manager.ResolveSpawnPosition(new Vector2(50f, 50f));

        Assert.That(clamped.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(clamped.y, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void ResolveSpawnPosition_ClampsPatrolEndpointBackInsideRoom()
    {
        // 出生点靠近右墙时，以它为中心的巡逻端点会落到墙外，必须被钳回来。
        SetRoomBounds(new Rect(-6.5f, -5.72f, 13.55f, 9.2f));

        var spawn = manager.ResolveSpawnPosition(new Vector2(5.56f, -1.12f));
        var patrolB = manager.ResolveSpawnPosition(spawn + new Vector2(1.5f, 0f));

        Assert.That(manager.RoomBounds.Contains(patrolB), Is.True);
        Assert.That(patrolB.x, Is.EqualTo(7.05f - 1.5f).Within(0.001f));
    }

    private void SetRoomBounds(Rect bounds)
    {
        var serialized = new UnityEditor.SerializedObject(manager);
        serialized.FindProperty("roomBounds").rectValue = bounds;
        serialized.FindProperty("hasRoomBounds").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
