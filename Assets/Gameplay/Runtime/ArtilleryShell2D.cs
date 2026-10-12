using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 迫击炮弹体：从炮口高抛飞向落点，落地时结算范围伤害。
/// 弹体朝向跟随飞行方向：上升段朝上，下落段朝下。
/// 弹体只做表现与伤害结算，预警圈由 <see cref="ArtilleryWarning2D"/> 单独负责。
///
/// 联机：由主机生成并 <c>Spawn</c>，6 个飞行参数（起点 / 落点 / 弧高 / 时长 / 半径 / 伤害）
/// 通过 NetworkVariable 广播，客户端按同一套抛物线公式复现飞行，**不结算伤害**。
/// 单机模式下 prefab 上没有 NetworkObject，<see cref="IsSpawned"/> 恒为 false，
/// 走本地分支，行为与改造前完全一致。
/// </summary>
public sealed class ArtilleryShell2D : NetworkBehaviour
{
    [SerializeField] private float spriteScale = 0.35f;

    /// <summary>权威起点。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<Vector2> syncedStart =
        new NetworkVariable<Vector2>(Vector2.zero, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威落点。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<Vector2> syncedTarget =
        new NetworkVariable<Vector2>(Vector2.zero, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威弧高。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedArc =
        new NetworkVariable<float>(3.2f, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威飞行时长。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedFlightTime =
        new NetworkVariable<float>(0.8f, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威爆炸半径。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedBlastRadius =
        new NetworkVariable<float>(1.6f, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威爆炸伤害。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedBlastDamage =
        new NetworkVariable<float>(18f, writePerm: NetworkVariableWritePermission.Server);

    private Vector2 start;
    private Vector2 target;
    private float arcHeight;
    private float flightTime;
    private float blastRadius;
    private float blastDamage;
    private float elapsed;
    private bool launched;

    /// <summary>本端是否为权威端（主机 / 单机）。</summary>
    private bool IsAuthority => !IsSpawned || IsServer;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // 主机：此时 NetworkVariable 已初始化，写入会正常标记 dirty 并同步给客户端。
            // 必须在 OnNetworkSpawn 里写（而非 Spawn 之前）—— Spawn 前写入不会标记 dirty。
            PublishParams();
        }
        else
        {
            // 客户端：先按当前值启动表现，再订阅变化 —— 主机写入的 Delta 可能晚一帧到达。
            ApplyAuthoritativeParams(
                syncedStart.Value,
                syncedTarget.Value,
                syncedArc.Value,
                syncedFlightTime.Value,
                syncedBlastRadius.Value,
                syncedBlastDamage.Value);

            syncedStart.OnValueChanged += HandleStartChanged;
            syncedTarget.OnValueChanged += HandleTargetChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            syncedStart.OnValueChanged -= HandleStartChanged;
            syncedTarget.OnValueChanged -= HandleTargetChanged;
        }

        base.OnNetworkDespawn();
    }

    private void HandleStartChanged(Vector2 previous, Vector2 current)
    {
        ApplyAuthoritativeParams(
            current,
            syncedTarget.Value,
            syncedArc.Value,
            syncedFlightTime.Value,
            syncedBlastRadius.Value,
            syncedBlastDamage.Value);
    }

    private void HandleTargetChanged(Vector2 previous, Vector2 current)
    {
        ApplyAuthoritativeParams(
            syncedStart.Value,
            current,
            syncedArc.Value,
            syncedFlightTime.Value,
            syncedBlastRadius.Value,
            syncedBlastDamage.Value);
    }

    /// <summary>发射。start 为炮口位置，target 为落点。主机调用。</summary>
    public void Launch(
        Vector2 startPosition,
        Vector2 targetPosition,
        float arc,
        float duration,
        float radius,
        float damage)
    {
        start = startPosition;
        target = targetPosition;
        arcHeight = Mathf.Max(0f, arc);
        flightTime = Mathf.Max(0.05f, duration);
        blastRadius = Mathf.Max(0.1f, radius);
        blastDamage = Mathf.Max(0f, damage);
        elapsed = 0f;
        launched = true;

        transform.position = start;
        transform.localScale = Vector3.one * spriteScale;
        transform.rotation = Quaternion.identity;

        // 联机主机：若已 Spawn（例如运行时改参数），立即同步。
        if (IsSpawned && IsServer)
            PublishParams();
    }

    /// <summary>把本地参数写入权威字段。仅主机调用。</summary>
    private void PublishParams()
    {
        syncedStart.Value = start;
        syncedTarget.Value = target;
        syncedArc.Value = arcHeight;
        syncedFlightTime.Value = flightTime;
        syncedBlastRadius.Value = blastRadius;
        syncedBlastDamage.Value = blastDamage;
    }

    /// <summary>客户端按权威参数启动表现。</summary>
    private void ApplyAuthoritativeParams(
        Vector2 startPosition,
        Vector2 targetPosition,
        float arc,
        float duration,
        float radius,
        float damage)
    {
        start = startPosition;
        target = targetPosition;
        arcHeight = Mathf.Max(0f, arc);
        flightTime = Mathf.Max(0.05f, duration);
        blastRadius = Mathf.Max(0.1f, radius);
        blastDamage = Mathf.Max(0f, damage);
        elapsed = 0f;
        launched = true;

        transform.position = start;
        transform.localScale = Vector3.one * spriteScale;
        transform.rotation = Quaternion.identity;
    }

    private void Update()
    {
        if (!launched)
            return;

        // 世界冻结（仅单机暂停）期间弹体停住：联机下世界永不停，弹体照常飞行。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return;

        elapsed += Time.deltaTime;
        var t = Mathf.Clamp01(elapsed / flightTime);

        // 水平线性插值 + 垂直抛物线，形成高抛弧线
        var horizontal = Vector2.Lerp(start, target, t);
        var height = 4f * arcHeight * t * (1f - t);
        transform.position = new Vector3(horizontal.x, horizontal.y + height, 0f);

        // 朝向跟随飞行方向：上升段朝上，顶点后转为朝下。
        // 弹体贴图按【纵向、尖端朝上】绘制，所以直接用速度方向对齐 up 轴。
        var velocity = GetVelocity(t);
        if (velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.FromToRotation(Vector3.up, velocity.normalized);

        if (t >= 1f)
            Explode();
    }

    /// <summary>抛物线在归一化时间 t 处的速度方向（水平分量恒定，垂直分量随 t 线性变化）。</summary>
    private Vector2 GetVelocity(float t)
    {
        var horizontal = (target - start) / flightTime;
        var vertical = 4f * arcHeight * (1f - 2f * t) / flightTime;
        return new Vector2(horizontal.x, vertical);
    }

    private void Explode()
    {
        launched = false;

        // 范围伤害只在权威端结算：客户端爆炸只做表现，避免各端血量不一致。
        if (IsAuthority)
        {
            var hits = Physics2D.OverlapCircleAll(target, blastRadius);
            foreach (var hit in hits)
            {
                var playerHealth = hit.GetComponentInParent<PlayerHealth2D>();
                if (playerHealth != null)
                {
                    playerHealth.TakeDamage(blastDamage);
                    break;
                }
            }
        }

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
