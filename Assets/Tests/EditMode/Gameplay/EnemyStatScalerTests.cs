using NUnit.Framework;
using UnityEngine;

public sealed class EnemyStatScalerTests
{
    private GameObject root;
    private TowerConfig config;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<TowerConfig>();
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);

        Object.DestroyImmediate(config);
    }

    private EnemyStatScaler CreateEnemy()
    {
        root = new GameObject("Enemy");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<EnemyBrain2D>();
        return root.AddComponent<EnemyStatScaler>();
    }

    [Test]
    public void Apply_ScalesHealthByFloor()
    {
        var scaler = CreateEnemy();
        var brain = root.GetComponent<EnemyBrain2D>();
        var baseHealth = brain.CurrentHealth;

        scaler.Apply(10, config);

        var expected = baseHealth * FloorGenerator.ResolveHealthMultiplier(10, config.HealthGrowthPerFloor);
        Assert.That(brain.CurrentHealth, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void Apply_FirstFloorLeavesStatsUnchanged()
    {
        var scaler = CreateEnemy();
        var brain = root.GetComponent<EnemyBrain2D>();
        var baseHealth = brain.CurrentHealth;

        scaler.Apply(1, config);

        Assert.That(brain.CurrentHealth, Is.EqualTo(baseHealth).Within(0.0001f));
    }

    [Test]
    public void Apply_ScalesSwarmContactDamage()
    {
        root = new GameObject("Swarm");
        root.AddComponent<Rigidbody2D>();
        var swarm = root.AddComponent<SwarmChaser2D>();
        var scaler = root.AddComponent<EnemyStatScaler>();

        scaler.Apply(10, config);

        var expected = 8f * FloorGenerator.ResolveDamageMultiplier(10, config.DamageGrowthPerFloor);
        Assert.That(swarm.ContactDamage, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void Apply_ScalesMortarBlastDamage()
    {
        root = new GameObject("Artillery");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<EnemyBrain2D>();
        var mortar = root.AddComponent<ArtilleryMortar2D>();
        var scaler = root.AddComponent<EnemyStatScaler>();

        scaler.Apply(10, config);

        var expected = 18f * FloorGenerator.ResolveDamageMultiplier(10, config.DamageGrowthPerFloor);
        Assert.That(mortar.BlastDamage, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void Apply_IsIdempotentForSameFloor()
    {
        var scaler = CreateEnemy();
        var brain = root.GetComponent<EnemyBrain2D>();
        var baseHealth = brain.CurrentHealth;

        scaler.Apply(10, config);
        scaler.Apply(10, config);

        var expected = baseHealth * FloorGenerator.ResolveHealthMultiplier(10, config.HealthGrowthPerFloor);
        Assert.That(brain.CurrentHealth, Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void Apply_HandlesNullConfig()
    {
        var scaler = CreateEnemy();
        var brain = root.GetComponent<EnemyBrain2D>();
        var baseHealth = brain.CurrentHealth;

        Assert.DoesNotThrow(() => scaler.Apply(10, null));
        Assert.That(brain.CurrentHealth, Is.EqualTo(baseHealth).Within(0.0001f));
    }
}
