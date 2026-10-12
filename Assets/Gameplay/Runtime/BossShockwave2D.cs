using Unity.Netcode;
using UnityEngine;

/// <summary>
/// BOSS 阶段二的红色冲击波：以 BOSS 为圆心向外扩散，对半径内的玩家结算一次范围伤害。
/// 与 <see cref="ArtilleryShell2D"/> 的区别：冲击波不飞行、不锁定落点，
/// 而是原地扩散，因此不需要抛物线，只需要「扩散到最大半径 → 结算 → 销毁」。
/// 伤害在扩散到最大半径的那一刻结算，给玩家留出向外跑的反应时间。
///
/// 联机：由主机生成并 <c>Spawn</c>，半径与伤害通过 NetworkVariable 广播，
/// 客户端按同一套扩散公式复现，**不结算伤害**。
/// 单机模式下 prefab 上没有 NetworkObject，<see cref="IsSpawned"/> 恒为 false，
/// 走本地分支，行为与改造前完全一致。
/// </summary>
public sealed class BossShockwave2D : NetworkBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float expandDuration = 0.45f;
    [SerializeField] private float startScale = 0.2f;

    /// <summary>权威伤害半径。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedRadius =
        new NetworkVariable<float>(3.2f, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威伤害。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedDamage =
        new NetworkVariable<float>(26f, writePerm: NetworkVariableWritePermission.Server);

    private float radius;
    private float damage;
    private float elapsed;
    private float baseScale = 1f;
    private bool playing;
    private bool damageDealt;
    private Collider2D ownerCollider;

    /// <summary>本端是否为权威端（主机 / 单机）。</summary>
    private bool IsAuthority => !IsSpawned || IsServer;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // 主机：此时 NetworkVariable 已初始化，写入会正常标记 dirty 并同步给客户端。
            PublishParams();
        }
        else
        {
            // 客户端：先按当前值启动表现，再订阅变化 —— 主机写入的 Delta 可能晚一帧到达。
            Play(syncedRadius.Value, syncedDamage.Value, null);
            syncedRadius.OnValueChanged += HandleRadiusChanged;
            syncedDamage.OnValueChanged += HandleDamageChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            syncedRadius.OnValueChanged -= HandleRadiusChanged;
            syncedDamage.OnValueChanged -= HandleDamageChanged;
        }

        base.OnNetworkDespawn();
    }

    private void HandleRadiusChanged(float previous, float current)
    {
        Play(current, syncedDamage.Value, null);
    }

    private void HandleDamageChanged(float previous, float current)
    {
        Play(syncedRadius.Value, current, null);
    }

    /// <summary>
    /// 开始扩散。radius 为最终伤害半径，ownerCollider 为 BOSS 自身碰撞体（用于避免自伤判定）。
    /// 主机调用。
    /// </summary>
    public void Play(float blastRadius, float blastDamage, Collider2D owner = null)
    {
        radius = Mathf.Max(0.1f, blastRadius);
        damage = Mathf.Max(0f, blastDamage);
        ownerCollider = owner;
        elapsed = 0f;
        playing = true;
        damageDealt = false;

        // 冲击波贴图直径对齐伤害直径。baseScale 只算一次，避免每帧累乘导致缩放失控。
        var diameter = radius * 2f;
        var sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        var spriteWidth = sprite != null ? sprite.bounds.size.x : 1f;
        baseScale = spriteWidth > 0.0001f ? diameter / spriteWidth : 1f;

        transform.localScale = Vector3.one * (baseScale * startScale);

        // 联机主机：若已 Spawn（例如运行时改参数），立即同步。
        if (IsSpawned && IsServer)
            PublishParams();
    }

    /// <summary>把本地参数写入权威字段。仅主机调用。</summary>
    private void PublishParams()
    {
        syncedRadius.Value = radius;
        syncedDamage.Value = damage;
    }

    private void Update()
    {
        if (!playing)
            return;

        // 世界冻结（仅单机暂停）期间冲击波停住：联机下世界永不停，扩散照常推进。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return;

        elapsed += Time.deltaTime;
        var t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, expandDuration));

        // 从 startScale 扩散到 1（基于固定的 baseScale，不读当前缩放）
        var scale = Mathf.Lerp(startScale, 1f, t);
        transform.localScale = Vector3.one * (baseScale * scale);

        if (t < 1f)
            return;

        if (!damageDealt)
        {
            damageDealt = true;

            // 伤害只在权威端结算：客户端扩散只做表现，避免各端血量不一致。
            if (IsAuthority)
                DealDamage();
        }

        playing = false;
        DespawnSelf();
    }

    /// <summary>
    /// 销毁自身。联机下**只有主机能销毁已 Spawn 的 NetworkObject** ——
    /// 客户端本地 Destroy 会报「Destroy a spawned NetworkObject on a non-host client is not valid」，
    /// 且不会通知主机，导致两端状态不一致。
    /// 客户端什么都不做，等主机的 Despawn 消息到达后由 NGO 自动销毁。
    /// </summary>
    private void DespawnSelf()
    {
        if (IsSpawned && !IsServer)
            return;

        Destroy(gameObject);
    }

    /// <summary>范围伤害：以 BOSS 为圆心、radius 为半径，命中玩家结算一次。</summary>
    private void DealDamage()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, radius);
        foreach (var hit in hits)
        {
            if (ownerCollider != null && hit == ownerCollider)
                continue;

            var playerHealth = hit.GetComponentInParent<PlayerHealth2D>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
                break;
            }
        }
    }
}
