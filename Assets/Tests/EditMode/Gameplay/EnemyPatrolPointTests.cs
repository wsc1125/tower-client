using NUnit.Framework;
using UnityEngine;

public sealed class EnemyPatrolPointTests
{
    private GameObject root;
    private EnemyBrain2D brain;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Enemy");
        root.AddComponent<Rigidbody2D>();
        brain = root.AddComponent<EnemyBrain2D>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void SetPatrolPoints_StoresAbsoluteWorldPoints()
    {
        brain.SetPatrolPoints(new Vector2(10f, 20f), new Vector2(12f, 20f));

        Assert.That(brain.PatrolPointA, Is.EqualTo(new Vector2(10f, 20f)));
        Assert.That(brain.PatrolPointB, Is.EqualTo(new Vector2(12f, 20f)));
    }

    [Test]
    public void SetPatrolAround_CentersPointsOnGivenOrigin()
    {
        // 巡逻点必须相对出生点，而不是 prefab 里写死的世界坐标。
        brain.SetPatrolAround(new Vector2(5f, -3f), 1.5f);

        Assert.That(brain.PatrolPointA, Is.EqualTo(new Vector2(3.5f, -3f)));
        Assert.That(brain.PatrolPointB, Is.EqualTo(new Vector2(6.5f, -3f)));
    }

    [Test]
    public void SetPatrolAround_ClampsRadiusToMinimum()
    {
        brain.SetPatrolAround(Vector2.zero, 0f);

        // 半径为 0 会让敌人原地不动，但两个点不能重合（否则到达判定会抖动）。
        Assert.That(brain.PatrolPointA, Is.Not.EqualTo(brain.PatrolPointB));
    }

    [Test]
    public void SetPatrolAround_IgnoresNegativeRadius()
    {
        brain.SetPatrolAround(new Vector2(2f, 2f), -5f);

        Assert.That(brain.PatrolPointA, Is.Not.EqualTo(brain.PatrolPointB));
    }
}
