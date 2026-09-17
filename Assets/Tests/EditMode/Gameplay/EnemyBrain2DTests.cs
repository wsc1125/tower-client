using NUnit.Framework;
using UnityEngine;

public class EnemyBrain2DTests
{
    [Test]
    public void ResolveState_PatrolsWhenPlayerIsOutsideDetectionRange()
    {
        var state = EnemyBrain2D.ResolveState(EnemyState.Patrol, 10f, 7f, 9f, 5f, 2.5f, false);

        Assert.That(state, Is.EqualTo(EnemyState.Patrol));
    }

    [Test]
    public void ResolveState_ChasesDetectedPlayerOutsideAttackRange()
    {
        var state = EnemyBrain2D.ResolveState(EnemyState.Patrol, 6f, 7f, 9f, 5f, 2.5f, false);

        Assert.That(state, Is.EqualTo(EnemyState.Chase));
    }

    [Test]
    public void ResolveState_AttacksInsideAttackRange()
    {
        var state = EnemyBrain2D.ResolveState(EnemyState.Chase, 4f, 7f, 9f, 5f, 2.5f, false);

        Assert.That(state, Is.EqualTo(EnemyState.Attack));
    }

    [Test]
    public void ResolveState_RetreatsWhenPlayerIsTooClose()
    {
        var state = EnemyBrain2D.ResolveState(EnemyState.Attack, 2f, 7f, 9f, 5f, 2.5f, false);

        Assert.That(state, Is.EqualTo(EnemyState.Retreat));
    }

    [Test]
    public void ResolveState_LeavesRetreatAfterReachingSafeDistance()
    {
        var state = EnemyBrain2D.ResolveState(EnemyState.Retreat, 3.2f, 7f, 9f, 5f, 2.5f, false, 3.2f);

        Assert.That(state, Is.EqualTo(EnemyState.Attack));
    }

    [Test]
    public void ResolveState_ReturnsToPatrolBeyondLoseRange()
    {
        var state = EnemyBrain2D.ResolveState(EnemyState.Chase, 9.1f, 7f, 9f, 5f, 2.5f, false);

        Assert.That(state, Is.EqualTo(EnemyState.Patrol));
    }

    [Test]
    public void ResolveState_DeadOverridesEveryOtherState()
    {
        var state = EnemyBrain2D.ResolveState(EnemyState.Attack, 4f, 7f, 9f, 5f, 2.5f, true);

        Assert.That(state, Is.EqualTo(EnemyState.Dead));
    }

    [Test]
    public void EnemyHealth_TakeDamageReducesHealthAndMarksDeath()
    {
        var health = new EnemyHealth(30f);

        health.TakeDamage(12f);
        Assert.That(health.Current, Is.EqualTo(18f));
        Assert.That(health.IsDead, Is.False);

        health.TakeDamage(20f);
        Assert.That(health.Current, Is.Zero);
        Assert.That(health.IsDead, Is.True);
    }

    [Test]
    public void AttackTimeline_FiresOnlyWhenEnteringFifthFrame()
    {
        Assert.That(EnemyAttackTimeline.ShouldFire(3, 4, false), Is.True);
        Assert.That(EnemyAttackTimeline.ShouldFire(4, 4, false), Is.False);
        Assert.That(EnemyAttackTimeline.ShouldFire(3, 4, true), Is.False);
    }

    [Test]
    public void PlayerHealth_TakeDamageClampsAtZero()
    {
        var health = new PlayerHealth(20f);

        health.TakeDamage(25f);

        Assert.That(health.Current, Is.Zero);
        Assert.That(health.IsDead, Is.True);
    }

    [Test]
    public void LaserRotation_AlignsVerticalSpriteWithTravelDirection()
    {
        var rotation = EnemyLaserProjectile2D.GetRotation(Vector2.right);
        var rotatedUp = rotation * Vector3.up;

        Assert.That(Vector2.Distance(rotatedUp, Vector2.right), Is.LessThan(0.001f));
    }

    [Test]
    public void TakeDamage_DisablesColliderWhenEnemyDies()
    {
        var enemy = new GameObject("Enemy");
        enemy.AddComponent<Rigidbody2D>();
        var collider = enemy.AddComponent<CapsuleCollider2D>();
        var brain = enemy.AddComponent<EnemyBrain2D>();

        brain.TakeDamage(30f);

        Assert.That(collider.enabled, Is.False);
        Object.DestroyImmediate(enemy);
    }

    [Test]
    public void TakeDamage_RaisesDiedExactlyOnce()
    {
        var enemy = new GameObject("Enemy");
        enemy.AddComponent<Rigidbody2D>();
        var brain = enemy.AddComponent<EnemyBrain2D>();
        var count = 0;
        brain.Died += () => count++;

        brain.TakeDamage(30f);
        brain.TakeDamage(30f);

        Assert.That(count, Is.EqualTo(1));
        Object.DestroyImmediate(enemy);
    }

    [Test]
    public void TakeDamage_DoesNotRaiseDiedWhenStillAlive()
    {
        var enemy = new GameObject("Enemy");
        enemy.AddComponent<Rigidbody2D>();
        var brain = enemy.AddComponent<EnemyBrain2D>();
        var count = 0;
        brain.Died += () => count++;

        brain.TakeDamage(10f);

        Assert.That(brain.IsDead, Is.False);
        Assert.That(count, Is.Zero);
        Object.DestroyImmediate(enemy);
    }

    [Test]
    public void Launch_IgnoresOwnerCollider()
    {
        var owner = new GameObject("Owner");
        var ownerCollider = owner.AddComponent<BoxCollider2D>();
        var projectile = new GameObject("Projectile");
        projectile.AddComponent<Rigidbody2D>();
        var projectileCollider = projectile.AddComponent<BoxCollider2D>();
        projectileCollider.isTrigger = true;
        var laser = projectile.AddComponent<EnemyLaserProjectile2D>();

        laser.Launch(Vector2.right, 7f, ownerCollider);

        Assert.That(Physics2D.GetIgnoreCollision(projectileCollider, ownerCollider), Is.True);
        Object.DestroyImmediate(projectile);
        Object.DestroyImmediate(owner);
    }

    [Test]
    public void GetMoveDirection_UsesCurrentState()
    {
        var towardPlayer = Vector2.right;
        var patrolDirection = Vector2.up;

        Assert.That(EnemyBrain2D.GetMoveDirection(EnemyState.Patrol, towardPlayer, patrolDirection), Is.EqualTo(Vector2.up));
        Assert.That(EnemyBrain2D.GetMoveDirection(EnemyState.Chase, towardPlayer, patrolDirection), Is.EqualTo(Vector2.right));
        Assert.That(EnemyBrain2D.GetMoveDirection(EnemyState.Retreat, towardPlayer, patrolDirection), Is.EqualTo(Vector2.left));
        Assert.That(EnemyBrain2D.GetMoveDirection(EnemyState.Attack, towardPlayer, patrolDirection), Is.EqualTo(Vector2.zero));
    }
}
