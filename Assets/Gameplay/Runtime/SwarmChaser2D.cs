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
    [SerializeField] private Transform player;
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

        if (player == null)
        {
            var playerMovement = FindObjectOfType<PlayerMovement2D>();
            if (playerMovement != null)
                player = playerMovement.transform;
        }
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

        if (player == null)
            return;

        var toPlayer = (Vector2)(player.position - transform.position);
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
