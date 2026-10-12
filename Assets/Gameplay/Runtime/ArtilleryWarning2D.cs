using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 地面预警圈：标记落点，从生成到爆炸期间持续收缩，给玩家躲避窗口。
/// 只做表现，伤害由 <see cref="ArtilleryShell2D"/> 在落地时结算。
///
/// 联机：由主机生成并 <c>Spawn</c>，时长与半径通过 NetworkVariable 广播，
/// 客户端按同一套收缩公式复现。本组件不结算任何伤害，因此客户端表现与主机完全一致。
/// 单机模式下 prefab 上没有 NetworkObject，<see cref="IsSpawned"/> 恒为 false，
/// 走本地分支，行为与改造前完全一致。
/// </summary>
public sealed class ArtilleryWarning2D : NetworkBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float startScale = 1.6f;
    [SerializeField] private float endScale = 0.85f;
    [SerializeField] private float spriteScale = 0.55f;

    /// <summary>权威播放时长。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedDuration =
        new NetworkVariable<float>(1.4f, writePerm: NetworkVariableWritePermission.Server);

    /// <summary>权威爆炸半径。主机写入，客户端只读。</summary>
    private readonly NetworkVariable<float> syncedRadius =
        new NetworkVariable<float>(1.6f, writePerm: NetworkVariableWritePermission.Server);

    private float duration;
    private float blastRadius = 1.6f;
    private float elapsed;
    private float baseScale = 1f;
    private bool playing;

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
            Play(syncedDuration.Value, syncedRadius.Value);
            syncedDuration.OnValueChanged += HandleDurationChanged;
            syncedRadius.OnValueChanged += HandleRadiusChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            syncedDuration.OnValueChanged -= HandleDurationChanged;
            syncedRadius.OnValueChanged -= HandleRadiusChanged;
        }

        base.OnNetworkDespawn();
    }

    private void HandleDurationChanged(float previous, float current)
    {
        Play(current, syncedRadius.Value);
    }

    private void HandleRadiusChanged(float previous, float current)
    {
        Play(syncedDuration.Value, current);
    }

    /// <summary>开始播放。radius 为爆炸半径，用于把预警圈缩放到对应世界尺寸。主机调用。</summary>
    public void Play(float playDuration, float radius)
    {
        duration = Mathf.Max(0.01f, playDuration);
        blastRadius = Mathf.Max(0.1f, radius);
        elapsed = 0f;
        playing = true;

        // 预警圈直径对齐爆炸直径。baseScale 只算一次，避免每帧累乘导致缩放失控。
        var diameter = Mathf.Max(0.1f, radius * 2f);
        var sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        var spriteWidth = sprite != null ? sprite.bounds.size.x : 1f;
        baseScale = spriteWidth > 0.0001f ? diameter / spriteWidth : 1f;
        baseScale *= spriteScale;

        transform.localScale = Vector3.one * (baseScale * startScale);

        // 联机主机：若已 Spawn（例如运行时改参数），立即同步。
        if (IsSpawned && IsServer)
            PublishParams();
    }

    /// <summary>把本地参数写入权威字段。仅主机调用。</summary>
    private void PublishParams()
    {
        syncedDuration.Value = duration;
        syncedRadius.Value = Mathf.Max(0.1f, blastRadius);
    }

    private void Update()
    {
        if (!playing)
            return;

        // 世界冻结（仅单机暂停）期间预警圈停住：联机下世界永不停，倒计时照常推进。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return;

        elapsed += Time.deltaTime;
        var t = Mathf.Clamp01(elapsed / duration);

        // 从 startScale 收缩到 endScale（基于固定的 baseScale，不读当前缩放）
        var scale = Mathf.Lerp(startScale, endScale, t);
        transform.localScale = Vector3.one * (baseScale * scale);

        if (t >= 1f)
        {
            playing = false;
            DespawnSelf();
        }
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
