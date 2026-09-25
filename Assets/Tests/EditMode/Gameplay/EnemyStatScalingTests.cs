using NUnit.Framework;
using UnityEngine;

public sealed class EnemyStatScalingTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
    }

    [Test]
    public void EnemyBrain_ScaleMaximumHealth_MultipliesBaseHealth()
    {
        root = new GameObject("Enemy");
        root.AddComponent<Rigidbody2D>();
        var brain = root.AddComponent<EnemyBrain2D>();

        var baseHealth = brain.CurrentHealth;
        brain.ScaleMaximumHealth(2.5f);

        Assert.That(brain.CurrentHealth, Is.EqualTo(baseHealth * 2.5f).Within(0.0001f));
    }

    [Test]
    public void EnemyBrain_ScaleMaximumHealth_IgnoresNonPositiveMultiplier()
    {
        root = new GameObject("Enemy");
        root.AddComponent<Rigidbody2D>();
        var brain = root.AddComponent<EnemyBrain2D>();

        var baseHealth = brain.CurrentHealth;
        brain.ScaleMaximumHealth(0f);
        brain.ScaleMaximumHealth(-3f);

        Assert.That(brain.CurrentHealth, Is.EqualTo(baseHealth).Within(0.0001f));
    }

    [Test]
    public void EnemyBrain_ScaleMaximumHealth_IsIdempotentPerCall()
    {
        root = new GameObject("Enemy");
        root.AddComponent<Rigidbody2D>();
        var brain = root.AddComponent<EnemyBrain2D>();

        var baseHealth = brain.CurrentHealth;
        brain.ScaleMaximumHealth(2f);
        brain.ScaleMaximumHealth(2f);

        // 每次调用都基于「原始基础血量」重算，而不是在已缩放的值上再乘，
        // 否则重复调用会指数级膨胀。
        Assert.That(brain.CurrentHealth, Is.EqualTo(baseHealth * 2f).Within(0.0001f));
    }

    [Test]
    public void SwarmChaser_ScaleDamage_MultipliesContactDamage()
    {
        root = new GameObject("Swarm");
        root.AddComponent<Rigidbody2D>();
        var swarm = root.AddComponent<SwarmChaser2D>();

        swarm.ScaleDamage(3f);

        Assert.That(swarm.ContactDamage, Is.EqualTo(8f * 3f).Within(0.0001f));
    }

    [Test]
    public void SwarmChaser_ScaleDamage_IgnoresNonPositiveMultiplier()
    {
        root = new GameObject("Swarm");
        root.AddComponent<Rigidbody2D>();
        var swarm = root.AddComponent<SwarmChaser2D>();

        swarm.ScaleDamage(0f);

        Assert.That(swarm.ContactDamage, Is.EqualTo(8f).Within(0.0001f));
    }

    [Test]
    public void EnemyLaser_ScaleDamage_MultipliesDamage()
    {
        root = new GameObject("Laser");
        root.AddComponent<Rigidbody2D>();
        var laser = root.AddComponent<EnemyLaserProjectile2D>();

        laser.ScaleDamage(2f);

        Assert.That(laser.Damage, Is.EqualTo(12f * 2f).Within(0.0001f));
    }

    [Test]
    public void ArtilleryMortar_ScaleDamage_MultipliesBlastDamage()
    {
        root = new GameObject("Artillery");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<EnemyBrain2D>();
        var mortar = root.AddComponent<ArtilleryMortar2D>();

        mortar.ScaleDamage(2f);

        Assert.That(mortar.BlastDamage, Is.EqualTo(18f * 2f).Within(0.0001f));
    }
}
