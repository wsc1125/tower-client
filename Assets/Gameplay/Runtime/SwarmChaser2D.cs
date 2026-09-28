using UnityEngine;

/// <summary>
/// 虫群的行为：无 FSM、无巡逻，生成后直线扑向玩家，靠接触伤害。
/// 与 <see cref="EnemyBrain2D"/> 互斥使用——虫群不挂 EnemyBrain2D，
/// 因此不会进入 Attack 状态，也就不会因缺少攻击帧而卡死。
/// 实现 <see cref="IDamageable"/>，使玩家子弹、自动索敌、掉落拾取判定都能识别它。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class SwarmChaser2D : MonoBehaviour, IDamageable
{
    [Tooltip("手工指定的追击目标。留空则由目标解析器自动解析（单机下即场景中的玩家）。")]
    [SerializeField] private Transform player;

    /// <summary>
    /// 目标解析器。未注入时由 <see cref="EnsureTargetProvider"/> 创建默认实现，
    /// 单机下行为与旧的 <c>FindObjectOfType&lt;PlayerMovement2D&gt;()</c> 等价。
    /// </summary>
    private ITargetProvider targetProvider;

    [SerializeField] private float moveSpeed = 2.6f;
    [SerializeField] private float contactDamage = 8f;
    [SerializeField] private float contactCooldown = 0.8f;
    [SerializeField] private float lifetime = 20f;

    /// <summary>未经层数缩放的基础接触伤害。</summary>
    private float baseContactDamage;

    [Header("生命")]
    [SerializeField] private float maximumHealth = 12f;
    [SerializeField] private EnemyVisual2D visual;
    [SerializeField] private Collider2D bodyCollider;

    private Rigidbody2D body;
    private float cooldownRemaining;
    private float age;
    private float currentHealth;
    private bool isDead;

    public bool IsDead => isDead;

    /// <summary>当前接触伤害（已含层数缩放）。</summary>
    public float ContactDamage => contactDamage;

    /// <summary>按层数缩放接触伤害。每次调用都基于原始基础值重算。</summary>
    public void ScaleDamage(float multiplier)
    {
        if (multiplier <= 0f)
            return;

        if (baseContactDamage <= 0f)
            baseContactDamage = contactDamage;

        contactDamage = baseContactDamage * multiplier;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();
        if (visual == null)
            visual = GetComponentInChildren<EnemyVisual2D>();

        if (baseContactDamage <= 0f)
            baseContactDamage = contactDamage;

        currentHealth = Mathf.Max(1f, maximumHealth);

        EnsureTargetProvider();
    }

    /// <summary>
    /// 确保目标解析器已就绪。
    /// <see cref="player"/> 在 Inspector 上手工指定时优先用它；
    /// 否则用默认解析器（单机下等价于旧的 <c>FindObjectOfType&lt;PlayerMovement2D&gt;()</c>）。
    /// </summary>
    private void EnsureTargetProvider()
    {
        if (targetProvider != null)
            return;

        var provider = new NearestPlayerTargetProvider();
        provider.SetSelf(transform);
        if (player != null)
            provider.SetExplicitTarget(player);

        targetProvider = provider;
    }

    /// <summary>
    /// 注入自定义的目标解析器。联机时由生成方按玩家列表构造后注入。
    /// 传 null 则恢复为默认解析器。
    /// </summary>
    public void SetTargetProvider(ITargetProvider provider)
    {
        targetProvider = provider;
    }

    /// <summary>
    /// 取当前目标位置。返回 false 表示当前没有可用目标，虫群会原地不动。
    /// </summary>
    private bool TryGetTargetPosition(out Vector2 position)
    {
        EnsureTargetProvider();

        if (targetProvider != null && targetProvider.TryGetTargetPosition(out position))
            return true;

        // 兜底：解析器不可用时退回 Inspector 上手工指定的目标。
        if (player != null)
        {
            position = player.position;
            return true;
        }

        position = Vector2.zero;
        return false;
    }

    public void TakeDamage(float amount)
    {
        if (isDead || amount <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        if (currentHealth > 0f)
            return;

        isDead = true;
        if (bodyCollider != null)
            bodyCollider.enabled = false;
    }

    private void FixedUpdate()
    {
        // 本地冻结（联机弹窗）期间虫群停住：世界照常推进，但本机看到的虫群暂停。
        // 单机下 LocalPauseScope.IsFrozen 恒为 false，行为与改造前一致。
        if (LocalPauseScope.IsFrozen)
            return;

        if (isDead)
        {
            // 死亡后只播死亡动画，播完销毁（虫群是廉价产物，不留尸体）
            if (visual != null)
            {
                visual.Step(EnemyState.Dead, Vector2.zero, Time.fixedDeltaTime);
                if (visual.DeathFinished)
                    Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }

            return;
        }

        cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Time.fixedDeltaTime);
        age += Time.fixedDeltaTime;

        if (age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        if (!TryGetTargetPosition(out var targetPosition))
            return;

        var toPlayer = targetPosition - (Vector2)transform.position;
        if (toPlayer.sqrMagnitude < 0.0001f)
            return;

        var direction = toPlayer.normalized;
        body.MovePosition(body.position + direction * (moveSpeed * Time.fixedDeltaTime));

        if (visual != null)
            visual.Step(EnemyState.Chase, direction * moveSpeed, Time.fixedDeltaTime);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryDamage(collision.collider);
    }

    private void TryDamage(Collider2D other)
    {
        if (isDead || cooldownRemaining > 0f)
            return;

        var playerHealth = other.GetComponentInParent<PlayerHealth2D>();
        if (playerHealth == null)
            return;

        playerHealth.TakeDamage(contactDamage);
        cooldownRemaining = contactCooldown;
    }
}
