using NUnit.Framework;
using UnityEngine;

public sealed class LootScanTests
{
    private GameObject enemyRoot;
    private GameObject nearRoot;
    private GameObject farRoot;
    private DroppedItem2D near;
    private DroppedItem2D far;

    [SetUp]
    public void SetUp()
    {
        enemyRoot = new GameObject("Enemy");
        enemyRoot.AddComponent<Rigidbody2D>();
        enemyRoot.AddComponent<EnemyBrain2D>();

        nearRoot = new GameObject("Near");
        nearRoot.transform.position = new Vector3(1f, 0f, 0f);
        near = nearRoot.AddComponent<DroppedItem2D>();

        farRoot = new GameObject("Far");
        farRoot.transform.position = new Vector3(3f, 0f, 0f);
        far = farRoot.AddComponent<DroppedItem2D>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(enemyRoot);
        Object.DestroyImmediate(nearRoot);
        Object.DestroyImmediate(farRoot);
    }

    [Test]
    public void HasAliveEnemyWithin_TrueWhenAliveEnemyInsideRadius()
    {
        enemyRoot.transform.position = new Vector3(2f, 0f, 0f);
        var brain = enemyRoot.GetComponent<EnemyBrain2D>();

        Assert.That(LootScan.HasAliveEnemyWithin(new[] { brain }, Vector2.zero, 8f), Is.True);
    }

    [Test]
    public void HasAliveEnemyWithin_FalseWhenEnemyOutsideRadius()
    {
        enemyRoot.transform.position = new Vector3(9f, 0f, 0f);
        var brain = enemyRoot.GetComponent<EnemyBrain2D>();

        Assert.That(LootScan.HasAliveEnemyWithin(new[] { brain }, Vector2.zero, 8f), Is.False);
    }

    [Test]
    public void HasAliveEnemyWithin_IgnoresDeadEnemies()
    {
        enemyRoot.transform.position = new Vector3(2f, 0f, 0f);
        var brain = enemyRoot.GetComponent<EnemyBrain2D>();
        brain.TakeDamage(30f);

        Assert.That(brain.IsDead, Is.True);
        Assert.That(LootScan.HasAliveEnemyWithin(new[] { brain }, Vector2.zero, 8f), Is.False);
    }

    [Test]
    public void HasAliveEnemyWithin_HandlesNullArray()
    {
        Assert.That(LootScan.HasAliveEnemyWithin(null, Vector2.zero, 8f), Is.False);
    }

    [Test]
    public void SelectNearest_SortsByDistanceAscending()
    {
        var result = LootScan.SelectNearest(new[] { far, near }, Vector2.zero, 4);

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0], Is.SameAs(near));
        Assert.That(result[1], Is.SameAs(far));
    }

    [Test]
    public void SelectNearest_TruncatesToMax()
    {
        var result = LootScan.SelectNearest(new[] { far, near }, Vector2.zero, 1);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.SameAs(near));
    }

    [Test]
    public void SelectNearest_SkipsNullEntries()
    {
        var result = LootScan.SelectNearest(new DroppedItem2D[] { null, near, null }, Vector2.zero, 4);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.SameAs(near));
    }

    [Test]
    public void SelectNearest_ReturnsEmptyForNonPositiveMax()
    {
        Assert.That(LootScan.SelectNearest(new[] { near }, Vector2.zero, 0), Is.Empty);
    }
}
