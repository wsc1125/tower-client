using System;
using UnityEngine;

public enum EnemyState
{
    Patrol,
    Chase,
    Attack,
    Retreat,
    Dead,
}

public sealed class EnemyHealth
{
    public EnemyHealth(float maximum)
    {
        Maximum = Mathf.Max(1f, maximum);
        Current = Maximum;
    }

    public float Maximum { get; }
    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        Current = Mathf.Max(0f, Current - amount);
    }
}

[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyBrain2D : MonoBehaviour, IDamageable
{
    [Header("目标")]
    [SerializeField] private Transform player;
    [SerializeField] private Vector2 patrolPointA = new(-2f, 0f);
    [SerializeField] private Vector2 patrolPointB = new(2f, 0f);

    [Header("移动")]
    [SerializeField] private float patrolSpeed = 1.2f;
    [SerializeField] private float chaseSpeed = 2.2f;
    [SerializeField] private float retreatSpeed = 1.8f;
    [SerializeField] private float patrolArriveDistance = 0.1f;

    [Header("距离")]
    [SerializeField] private float detectionRange = 7f;
    [SerializeField] private float loseRange = 9f;
    [SerializeField] private float attackRange = 5f;
    [SerializeField] private float retreatStartRange = 2.5f;
    [SerializeField] private float retreatStopRange = 3.2f;

    [Header("生命")]
    [SerializeField] private float maximumHealth = 30f;

    /// <summary>未经层数缩放的基础血量。ScaleMaximumHealth 每次都基于它重算，避免重复调用指数膨胀。</summary>
    private float baseMaximumHealth;

    [Header("攻击")]
    [SerializeField] private EnemyVisual2D visual;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private EnemyLaserProjectile2D laserPrefab;
    [SerializeField] private SpawnerSummoner2D summoner;
    [SerializeField] private ArtilleryMortar2D mortar;
    [SerializeField] private float laserSpeed = 7f;
    [SerializeField] private float attackCooldown = 1.5f;

    /// <summary>激光伤害系数，由 EnemyStatScaler 按层数写入。1 表示不缩放。</summary>
    private float laserDamageMultiplier = 1f;

    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Collider2D bodyCollider;

    private EnemyHealth health;
    private EnemyState state = EnemyState.Patrol;
    private bool movingToB = true;
    private float cooldownRemaining;
    private Vector2 movementVelocity;

    public EnemyState State => state;
    public bool IsDead => health != null && health.IsDead;
    public float CurrentHealth => health != null ? health.Current : maximumHealth;

    /// 血量归零的那一刻触发一次；已死亡后再次受伤不会重复触发。
    public event Action Died;

    public static EnemyState ResolveState(
        EnemyState current,
        float playerDistance,
        float detectionRange,
        float loseRange,
        float attackRange,
        float retreatStartRange,
        bool isDead,
        float retreatStopRange = 3.2f)
    {
        if (isDead)
            return EnemyState.Dead;

        if (current == EnemyState.Retreat && playerDistance < retreatStopRange)
            return EnemyState.Retreat;

        if (playerDistance < retreatStartRange)
            return EnemyState.Retreat;

        if (current != EnemyState.Patrol && playerDistance > loseRange)
            return EnemyState.Patrol;

        if (current == EnemyState.Patrol && playerDistance > detectionRange)
            return EnemyState.Patrol;

        if (playerDistance <= attackRange)
            return EnemyState.Attack;

        return EnemyState.Chase;
    }

    public static Vector2 GetMoveDirection(EnemyState current, Vector2 towardPlayer, Vector2 patrolDirection)
    {
        switch (current)
        {
            case EnemyState.Patrol:
                return patrolDirection.normalized;
            case EnemyState.Chase:
                return towardPlayer.normalized;
            case EnemyState.Retreat:
                return -towardPlayer.normalized;
            default:
                return Vector2.zero;
        }
    }

    /// <summary>
    /// 攻击已经开始就必须执行到底：只要动作还没播完，即使玩家走出攻击范围也要保持攻击状态。
    /// 死亡可随时打断；没有配置攻击帧的敌人不会进入锁定，避免永久停在攻击状态。
    /// </summary>
    public static bool ShouldHoldAttack(bool isAttacking, bool isDead, bool hasAttackFrames, bool attackFinished)
    {
        return isAttacking && !isDead && hasAttackFrames && !attackFinished;
    }

    /// <summary>
    /// 按层数缩放血量上限。必须在 <see cref="Awake"/> 之前调用才有效
    /// （Awake 会创建 health 实例），因此调用方需先 SetActive(false) 再缩放再 SetActive(true)。
    /// 每次调用都基于原始基础血量重算，重复调用不会指数膨胀。
    /// </summary>
    public void ScaleMaximumHealth(float multiplier)
    {
        if (multiplier <= 0f)
            return;

        if (baseMaximumHealth <= 0f)
            baseMaximumHealth = maximumHealth;

        maximumHealth = baseMaximumHealth * multiplier;
        health = new EnemyHealth(maximumHealth);
    }

    /// <summary>设置激光伤害系数。由 <see cref="EnemyStatScaler"/> 按层数调用。</summary>
    public void ScaleLaserDamage(float multiplier)
    {
        if (multiplier <= 0f)
            return;

        laserDamageMultiplier = multiplier;
    }

    public void TakeDamage(float amount)
    {
        EnsureInitialized();
        if (health.IsDead)
            return;

        health.TakeDamage(amount);
        if (!health.IsDead)
            return;

        state = EnemyState.Dead;
        movementVelocity = Vector2.zero;
        if (bodyCollider != null)
            bodyCollider.enabled = false;

        Died?.Invoke();
    }

    private void Awake()
    {
        EnsureInitialized();

        if (player == null)
        {
            var playerMovement = FindObjectOfType<PlayerMovement2D>();
            if (playerMovement != null)
                player = playerMovement.transform;
        }
    }

    private void EnsureInitialized()
    {
        if (body == null)
            body = GetComponent<Rigidbody2D>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();
        if (health == null)
        {
            if (baseMaximumHealth <= 0f)
                baseMaximumHealth = maximumHealth;

            health = new EnemyHealth(baseMaximumHealth);
        }
    }

    private void FixedUpdate()
    {
        cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Time.fixedDeltaTime);

        var hasPlayer = player != null;
        var toPlayer = hasPlayer ? (Vector2)(player.position - transform.position) : Vector2.zero;
        var playerDistance = hasPlayer ? toPlayer.magnitude : float.PositiveInfinity;

        var resolvedState = ResolveState(
            state,
            playerDistance,
            detectionRange,
            loseRange,
            attackRange,
            retreatStartRange,
            IsDead,
            retreatStopRange);

        // 攻击一旦开始就执行到底：动作没播完之前，玩家跑开、超出侦测范围都不收招，
        // 否则会出现「挥到一半突然中断、伤害也没打出来」。
        if (ShouldHoldAttack(
                state == EnemyState.Attack,
                IsDead,
                visual != null && visual.HasAttackFrames,
                visual != null && visual.AttackFinished))
        {
            resolvedState = EnemyState.Attack;
        }
        else if (resolvedState == EnemyState.Attack && cooldownRemaining > 0f)
        {
            resolvedState = EnemyState.Chase;
        }

        state = resolvedState;

        var patrolTarget = movingToB ? patrolPointB : patrolPointA;
        var patrolDirection = patrolTarget - body.position;
        if (state == EnemyState.Patrol && patrolDirection.magnitude <= patrolArriveDistance)
        {
            movingToB = !movingToB;
            patrolTarget = movingToB ? patrolPointB : patrolPointA;
            patrolDirection = patrolTarget - body.position;
        }

        var direction = GetMoveDirection(state, toPlayer, patrolDirection);
        var speed = GetSpeed(state);
        movementVelocity = direction * speed;
        body.MovePosition(body.position + movementVelocity * Time.fixedDeltaTime);

        if (visual == null)
            return;

        var shouldFire = visual.Step(state, movementVelocity, Time.fixedDeltaTime);
        if (shouldFire)
        {
            if (summoner != null)
                summoner.Summon();
            else if (mortar != null)
                mortar.Fire(player != null ? (Vector2)player.position : (Vector2)transform.position);
            else
                FireLaser(toPlayer);
        }

        if (state == EnemyState.Attack && visual.AttackFinished)
            cooldownRemaining = attackCooldown;
    }

    private void FireLaser(Vector2 toPlayer)
    {
        if (laserPrefab == null || muzzlePoint == null)
            return;

        var direction = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
        var laser = Instantiate(laserPrefab, muzzlePoint.position, Quaternion.identity);
        laser.Launch(direction, laserSpeed, bodyCollider);
        laser.ScaleDamage(laserDamageMultiplier);
    }

    private float GetSpeed(EnemyState current)
    {
        switch (current)
        {
            case EnemyState.Patrol:
                return patrolSpeed;
            case EnemyState.Chase:
                return chaseSpeed;
            case EnemyState.Retreat:
                return retreatSpeed;
            default:
                return 0f;
        }
    }
}
