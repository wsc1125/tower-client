using System;
using UnityEngine;

/// <summary>
/// BOSS「机械巢母」的独立行为组件。
/// 不复用 <see cref="EnemyBrain2D"/>，原因有三：
/// ① BOSS 原地不动，不需要巡逻 / 追击 / 撤退的完整 FSM；
/// ② 需要两段攻击（Attack_A / Attack_B），而 <see cref="EnemyVisual2D"/> 只持有一组 attackFrames；
/// ③ 死亡后需要销毁，而现有敌人一律保留尸体。
/// 实现 <see cref="IDamageable"/>，使玩家子弹、自动索敌、掉落拾取判定都能识别它。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class BossBrain2D : MonoBehaviour, IDamageable
{
    [Header("目标")]
    [SerializeField] private Transform player;

    [Header("距离")]
    [SerializeField] private float detectionRange = 12f;
    [SerializeField] private float loseRange = 14f;
    [SerializeField] private float attackRange = 10f;

    [Header("生命")]
    [SerializeField] private float maximumHealth = 1200f;

    /// <summary>血量降到该比例时进入阶段二，攻击动画切换到 Attack_B。</summary>
    [Range(0f, 1f)]
    [SerializeField] private float phaseTwoHealthRatio = 0.5f;

    [Header("阶段一 · 扇形弹幕")]
    [SerializeField] private EnemyLaserProjectile2D bulletPrefab;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private float bulletSpeed = 6f;
    [SerializeField] private float phaseOneCooldown = 2.2f;

    /// <summary>一次齐射的弹丸数量。BOSS 靠弹幕密度施压，而不是单发点射。</summary>
    [SerializeField] private int burstCount = 9;

    /// <summary>扇形总张角（度）。以玩家方向为中心，向两侧均分。</summary>
    [SerializeField] private float burstSpreadAngle = 70f;

    /// <summary>每颗弹丸沿自身方向错开的出生距离，避免同点生成时互相触发销毁。</summary>
    [SerializeField] private float burstSpawnOffset = 0.45f;

    [Header("阶段二 · 冲击波")]
    [SerializeField] private BossShockwave2D shockwavePrefab;
    [SerializeField] private float shockwaveRadius = 3.2f;
    [SerializeField] private float shockwaveDamage = 26f;
    [SerializeField] private float phaseTwoCooldown = 1.6f;

    [Header("表现")]
    [SerializeField] private BossVisual2D visual;
    [SerializeField] private float deathDestroyDelay = 1.5f;

    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Collider2D bodyCollider;

    private EnemyHealth health;
    private float cooldownRemaining;
    private float deathElapsed;
    private bool deathHandled;

    public bool IsDead => health != null && health.IsDead;
    public float CurrentHealth => health != null ? health.Current : maximumHealth;

    /// <summary>当前是否处于阶段二（血量低于阈值）。</summary>
    public bool IsPhaseTwo => health != null && health.Current <= health.Maximum * phaseTwoHealthRatio;

    /// <summary>血量归零的那一刻触发一次；已死亡后再次受伤不会重复触发。</summary>
    public event Action Died;

    /// <summary>
    /// 阶段判定：血量比例低于阈值即进入阶段二。
    /// 抽成静态纯函数便于单测，且保证阶段只前进不后退（血量不会回升）。
    /// </summary>
    public static bool ResolvePhaseTwo(float currentHealth, float maximumHealth, float phaseTwoHealthRatio)
    {
        if (maximumHealth <= 0f)
            return false;

        return currentHealth <= maximumHealth * Mathf.Clamp01(phaseTwoHealthRatio);
    }

    /// <summary>
    /// 是否应该开火：玩家在攻击范围内、冷却结束、且未死亡。
    /// 抽成静态纯函数便于单测。
    /// </summary>
    public static bool ShouldAttack(bool isDead, float playerDistance, float attackRange, float cooldownRemaining)
    {
        return !isDead && playerDistance <= attackRange && cooldownRemaining <= 0f;
    }

    public void TakeDamage(float amount)
    {
        EnsureInitialized();
        if (health.IsDead)
            return;

        health.TakeDamage(amount);
        if (!health.IsDead)
            return;

        if (bodyCollider != null)
            bodyCollider.enabled = false;

        deathElapsed = 0f;
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
            health = new EnemyHealth(maximumHealth);
    }

    private void FixedUpdate()
    {
        if (IsDead)
        {
            HandleDeath();
            return;
        }

        cooldownRemaining = Mathf.Max(0f, cooldownRemaining - Time.fixedDeltaTime);

        var hasPlayer = player != null;
        var toPlayer = hasPlayer ? (Vector2)(player.position - transform.position) : Vector2.zero;
        var playerDistance = hasPlayer ? toPlayer.magnitude : float.PositiveInfinity;

        // BOSS 原地不动：不调用 MovePosition，避免大型碰撞体被玩家挤压位移。
        // 朝向由视觉组件按玩家方位翻转。
        var facing = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;

        var phaseTwo = IsPhaseTwo;
        var shouldAttack = ShouldAttack(IsDead, playerDistance, attackRange, cooldownRemaining);

        if (visual != null)
        {
            var shouldFire = visual.Step(phaseTwo, shouldAttack, facing, Time.fixedDeltaTime);
            if (shouldFire)
                Fire(phaseTwo, toPlayer);

            if (shouldAttack && visual.AttackFinished)
                cooldownRemaining = phaseTwo ? phaseTwoCooldown : phaseOneCooldown;
        }
    }

    /// <summary>
    /// 开火。阶段一以玩家方向为中心打出一片扇形弹幕，阶段二释放红色冲击波（以自身为中心的范围伤害）。
    /// 两个分支的素材与玩法完全不同，因此不做统一抽象。
    /// </summary>
    private void Fire(bool phaseTwo, Vector2 toPlayer)
    {
        if (phaseTwo)
        {
            if (shockwavePrefab == null)
                return;

            var shockwave = Instantiate(shockwavePrefab, transform.position, Quaternion.identity);
            shockwave.Play(shockwaveRadius, shockwaveDamage, bodyCollider);
            return;
        }

        if (bulletPrefab == null || muzzlePoint == null)
            return;

        var center = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
        var directions = BuildSpreadDirections(center, burstCount, burstSpreadAngle);

        // 先全部生成，再统一互相忽略碰撞。若边生成边忽略，后生成的弹丸还没被
        // FindObjectsOfType 收录，会漏掉配对，导致同批弹丸在第一帧互相销毁。
        var spawned = new EnemyLaserProjectile2D[directions.Length];
        for (var i = 0; i < directions.Length; i++)
        {
            // 沿各自方向错开出生点，进一步降低触发体重叠程度。
            var spawnPosition = (Vector2)muzzlePoint.position + directions[i] * burstSpawnOffset;
            spawned[i] = Instantiate(bulletPrefab, spawnPosition, Quaternion.identity);
            spawned[i].Launch(directions[i], bulletSpeed, bodyCollider);
        }

        for (var i = 0; i < spawned.Length; i++)
        {
            // 弹幕出生点常压在近身敌人身上，必须忽略所有敌人碰撞体，
            // 否则弹丸会在生成瞬间被敌人刚体挤开或卡住（实测 9 颗只剩 4 颗）。
            spawned[i].IgnoreAllEnemies();

            for (var j = i + 1; j < spawned.Length; j++)
            {
                foreach (var a in spawned[i].GetComponentsInChildren<Collider2D>())
                    spawned[j].IgnoreCollisionWith(a);
            }
        }
    }

    /// <summary>
    /// 以 center 方向为中轴，向两侧均分出一组扇形方向。
    /// 抽成静态纯函数便于单测：count 为 1 时直接返回中轴，避免除零。
    /// </summary>
    public static Vector2[] BuildSpreadDirections(Vector2 center, int count, float spreadAngleDegrees)
    {
        count = Mathf.Max(1, count);
        var result = new Vector2[count];
        if (count == 1)
        {
            result[0] = center.sqrMagnitude > 0.0001f ? center.normalized : Vector2.right;
            return result;
        }

        var baseAngle = Mathf.Atan2(center.y, center.x) * Mathf.Rad2Deg;
        var step = spreadAngleDegrees / (count - 1);
        var start = baseAngle - spreadAngleDegrees * 0.5f;

        for (var i = 0; i < count; i++)
        {
            var radians = (start + step * i) * Mathf.Deg2Rad;
            result[i] = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        return result;
    }

    /// <summary>
    /// 死亡处理：播完死亡动画后延迟销毁。
    /// 延迟是为了让最后一帧的残骸在画面上停留一下，避免「刚炸完就消失」。
    /// </summary>
    private void HandleDeath()
    {
        if (visual != null)
            visual.StepDeath(Time.fixedDeltaTime);

        if (deathHandled)
            return;

        var animationDone = visual == null || visual.DeathFinished;
        if (!animationDone)
            return;

        deathElapsed += Time.fixedDeltaTime;
        if (deathElapsed < deathDestroyDelay)
            return;

        deathHandled = true;
        Destroy(gameObject);
    }
}
