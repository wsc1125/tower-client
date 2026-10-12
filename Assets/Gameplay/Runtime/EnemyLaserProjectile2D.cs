using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 敌人激光弹：直线飞行，命中玩家结算伤害。
///
/// 联机：由主机生成并 <c>Spawn</c>，参数（方向 / 速度 / 伤害）通过 NetworkVariable 广播，
/// 客户端只做表现（飞行 + 命中特效），**不结算伤害** —— 伤害是主机权威。
/// 单机模式下 prefab 上没有 NetworkObject，<see cref="IsSpawned"/> 恒为 false，
/// 走本地分支，行为与改造前完全一致。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyLaserProjectile2D : NetworkBehaviour
{
    [SerializeField] private float damage = 12f;
    [SerializeField] private float lifetime = 3f;

    /// <summary>未经层数缩放的基础伤害。</summary>
    private float baseDamage;

    /// <summary>权威方向。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<Vector2> syncedDirection =
        new NetworkVariable<Vector2>(Vector2.right, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威速度。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedSpeed =
        new NetworkVariable<float>(7f, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威伤害。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedDamage =
        new NetworkVariable<float>(12f, writePerm: NetworkVariableWritePermission.Server);

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;
    private float speed = 7f;
    private float age;
    private bool launched;

    /// <summary>本端是否为权威端（主机 / 单机）。</summary>
    private bool IsAuthority => !IsSpawned || IsServer;

    public static Quaternion GetRotation(Vector2 travelDirection)
    {
        var direction = travelDirection.sqrMagnitude > 0.0001f ? travelDirection.normalized : Vector2.right;
        return Quaternion.FromToRotation(Vector3.up, direction);
    }

    /// <summary>当前伤害（已含层数缩放）。</summary>
    public float Damage => damage;

    /// <summary>按层数缩放伤害。每次调用都基于原始基础值重算。</summary>
    public void ScaleDamage(float multiplier)
    {
        if (multiplier <= 0f)
            return;

        if (baseDamage <= 0f)
            baseDamage = damage;

        damage = baseDamage * multiplier;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // 主机：此时 NetworkVariable 已初始化，写入会正常标记 dirty 并同步给客户端。
            // 必须在 OnNetworkSpawn 里写（而非 Spawn 之前）—— Spawn 前写入不会标记 dirty，
            // 参数不会同步（NGO 会打 "doesn't know its NetworkBehaviour yet" 警告）。
            syncedDirection.Value = direction;
            syncedSpeed.Value = speed;
            syncedDamage.Value = damage;
        }
        else
        {
            // 客户端：先按当前值启动表现，再订阅变化 —— 主机写入的 Delta 可能晚一帧到达，
            // 届时通过 OnValueChanged 修正方向与速度。
            ApplyAuthoritativeParams(syncedDirection.Value, syncedSpeed.Value);
            syncedDirection.OnValueChanged += HandleDirectionChanged;
            syncedSpeed.OnValueChanged += HandleSpeedChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            syncedDirection.OnValueChanged -= HandleDirectionChanged;
            syncedSpeed.OnValueChanged -= HandleSpeedChanged;
        }

        base.OnNetworkDespawn();
    }

    private void HandleDirectionChanged(Vector2 previous, Vector2 current)
    {
        ApplyAuthoritativeParams(current, syncedSpeed.Value);
    }

    private void HandleSpeedChanged(float previous, float current)
    {
        ApplyAuthoritativeParams(syncedDirection.Value, current);
    }

    /// <summary>
    /// 发射。主机调用后参数会被广播；客户端不调用本方法（由 OnNetworkSpawn 读取权威值）。
    /// </summary>
    public void Launch(Vector2 launchDirection, float launchSpeed, Collider2D ownerCollider = null)
    {
        direction = launchDirection.sqrMagnitude > 0.0001f ? launchDirection.normalized : Vector2.right;
        speed = launchSpeed;
        transform.rotation = GetRotation(direction);
        launched = true;

        if (ownerCollider != null)
            IgnoreCollisionWith(ownerCollider);

        // 联机主机：若已 Spawn（例如运行时改参数），立即同步。
        if (IsSpawned && IsServer)
        {
            syncedDirection.Value = direction;
            syncedSpeed.Value = speed;
            syncedDamage.Value = damage;
        }
    }

    /// <summary>客户端按权威参数启动表现。</summary>
    private void ApplyAuthoritativeParams(Vector2 dir, float spd)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        speed = spd;
        transform.rotation = GetRotation(direction);
        launched = true;
    }

    /// <summary>
    /// 忽略与另一个碰撞体的碰撞。同批弹幕的出生点彼此很近，触发体会重叠，
    /// 若不忽略，OnTriggerEnter2D 会在生成的第一帧把彼此全部销毁（实测 9 颗齐射只剩 1 颗）。
    /// 由发射方在整批弹丸都生成完毕后统一调用，避免依赖 FindObjectsOfType 的注册时机。
    /// </summary>
    public void IgnoreCollisionWith(Collider2D other)
    {
        if (other == null)
            return;

        foreach (var projectileCollider in GetComponentsInChildren<Collider2D>())
            Physics2D.IgnoreCollision(projectileCollider, other, true);
    }

    /// <summary>
    /// 忽略场景中所有敌人碰撞体。BOSS 的弹幕出生点常常压在近身敌人身上，
    /// 若不忽略，弹丸会在生成瞬间被敌人的刚体挤开甚至卡住。
    /// 弹丸只对玩家结算伤害，因此与敌人之间无需任何碰撞关系。
    /// </summary>
    public void IgnoreAllEnemies()
    {
        foreach (var enemy in WorldRegistry.Damageables)
        {
            var collider = enemy.transform != null ? enemy.transform.GetComponent<Collider2D>() : null;
            IgnoreCollisionWith(collider);
        }
    }

    private void FixedUpdate()
    {
        if (!launched)
            return;

        // 世界冻结（仅单机暂停）期间弹幕停住：联机下世界永不停，弹幕照常飞行。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return;

        age += Time.fixedDeltaTime;
        body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));

        if (age >= lifetime)
            DespawnSelf();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 掩体挡子弹：撞上实体掩体即销毁，不结算伤害。
        // 这条判定必须放在玩家判定之前——掩体与玩家可能同时被触发体覆盖，
        // 若先判玩家，贴着掩体站位的玩家会「隔着掩体被打中」。
        if (other.GetComponentInParent<Obstacle2D>() != null)
        {
            DespawnSelf();
            return;
        }

        // 只对玩家结算伤害；撞到其他弹丸 / 敌人 / 场景装饰时直接穿过，不销毁。
        var playerHealth = other.GetComponentInParent<PlayerHealth2D>();
        if (playerHealth == null)
            return;

        // 伤害只在权威端结算：客户端命中不结算，避免各端血量不一致。
        if (IsAuthority)
            playerHealth.TakeDamage(syncedDamage.Value > 0f ? syncedDamage.Value : damage);

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
}
