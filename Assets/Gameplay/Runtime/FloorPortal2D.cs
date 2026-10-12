using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 层间传送点：玩家踩上去触发一次，把「踩到了哪种传送点」抛给订阅者。
/// 只抛事件，不认识 <see cref="TowerRunManager"/>，由 Manager 在生成时订阅。
///
/// 视觉上刻意与掉落物的地面光圈区分开：掉落物是「贴地的一滩光」，
/// 传送点是「立起来的门」—— 门框 + 门内能量幕 + 地面基座，
/// 并且带呼吸脉动，静止时也能一眼看出是可交互的入口。
///
/// 联机：传送点由主机权威生成并通过 NGO 同步到客户端。客户端踩到时不本地换层，
/// 而是发 ServerRpc 通知主机，由主机决定换层（与敌人同步机制一致）。
/// 单机模式下本组件没有 NetworkObject，<see cref="IsSpawned"/> 恒为 false，
/// 走本地分支，行为与改造前完全一致。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class FloorPortal2D : NetworkBehaviour
{
    [SerializeField] private PortalKind kind;

    [Tooltip("门框。不参与脉动，保持轮廓稳定。")]
    [SerializeField] private SpriteRenderer frame;

    [Tooltip("门内能量幕。做呼吸脉动，是「活的」的那一层。")]
    [SerializeField] private SpriteRenderer veil;

    [Tooltip("地面基座。贴地，用来把门「钉」在地板上。")]
    [SerializeField] private SpriteRenderer basePlate;

    [Header("脉动")]
    [Tooltip("能量幕透明度脉动幅度。0 表示不脉动。")]
    [SerializeField] private float pulseAmplitude = 0.22f;

    [Tooltip("脉动频率（次/秒）。")]
    [SerializeField] private float pulseSpeed = 1.6f;

    [Tooltip("能量幕整体缩放脉动幅度。")]
    [SerializeField] private float pulseScaleAmplitude = 0.05f;

    private Collider2D trigger;
    private bool consumed;
    private Color veilBaseColor = Color.white;
    private Vector3 veilBaseScale = Vector3.one;
    private float pulsePhase;

    /// <summary>
    /// 当前站在阵内的玩家（按 clientId）。主机据此做「全员撤离」聚合判定。
    ///
    /// 与 <see cref="consumed"/> 的一次性触发语义分开：本集合持续维护，
    /// 玩家进出阵都会更新，供主机轮询「存活玩家是否都在阵内」。
    /// </summary>
    private readonly System.Collections.Generic.HashSet<ulong> playersInside =
        new System.Collections.Generic.HashSet<ulong>();
    private readonly System.Collections.Generic.Dictionary<Collider2D, ulong> trackedColliders =
        new System.Collections.Generic.Dictionary<Collider2D, ulong>();

    /// <summary>阵内玩家集合（只读）。主机做全员撤离判定时读它。</summary>
    public System.Collections.Generic.IReadOnlyCollection<ulong> PlayersInside => playersInside;

    /// <summary>指定 clientId 的玩家是否在阵内。</summary>
    public bool IsPlayerInside(ulong clientId) => playersInside.Contains(clientId);

    /// <summary>玩家死亡时主动移除阵内记录，碰撞体禁用不保证触发 Exit。</summary>
    public void RemovePlayer(ulong clientId)
    {
        playersInside.Remove(clientId);
        var collidersToRemove = new System.Collections.Generic.List<Collider2D>();
        foreach (var entry in trackedColliders)
        {
            if (entry.Value == clientId)
                collidersToRemove.Add(entry.Key);
        }
        foreach (var collider in collidersToRemove)
            trackedColliders.Remove(collider);
    }

    /// <summary>
    /// 当前场景内所有存活的传送点。
    ///
    /// 客户端不本地生成传送点（由 NGO 同步过来），因此 Manager 无法在生成时订阅它们。
    /// 用静态注册表让 Manager 能拿到全部传送点（含 NGO 同步来的），
    /// 从而订阅 Entered / ExecuteRequested 事件。
    /// </summary>
    private static readonly System.Collections.Generic.List<FloorPortal2D> active =
        new System.Collections.Generic.List<FloorPortal2D>();

    /// <summary>当前存活的传送点（只读快照）。</summary>
    public static System.Collections.Generic.IReadOnlyList<FloorPortal2D> Active => active;

    public PortalKind Kind => kind;

    /// <summary>玩家踩上来时触发一次，参数为传送点种类。</summary>
    public event Action<PortalKind> Entered;

    /// <summary>测试与运行时接线用。</summary>
    public void Configure(PortalKind value)
    {
        kind = value;
    }

    private void Awake()
    {
        trigger = GetComponent<Collider2D>();
        if (trigger != null)
            trigger.isTrigger = true;

        CacheVeilBaseline();

        // 相位错开，避免同层两个传送点同步闪烁。
        pulsePhase = UnityEngine.Random.value * Mathf.PI * 2f;
    }

    private void OnEnable()
    {
        // 对象池 / 反复启停时基线可能被上一次脉动污染，重新取一次。
        CacheVeilBaseline();

        if (!active.Contains(this))
            active.Add(this);
    }

    private void OnDisable()
    {
        active.Remove(this);
        trackedColliders.Clear();
        playersInside.Clear();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 客户端：传送点由 NGO 同步生成，这里补登记，让 Manager 能订阅到。
        if (!active.Contains(this))
            active.Add(this);
    }

    public override void OnNetworkDespawn()
    {
        active.Remove(this);

        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (veil == null || consumed)
            return;

        // 世界冻结（仅单机暂停）期间脉动停住：联机下世界永不停，传送点照常脉动。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return;

        var wave = Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f + pulsePhase);

        var color = veilBaseColor;
        color.a = Mathf.Clamp01(veilBaseColor.a + wave * pulseAmplitude);
        veil.color = color;

        veil.transform.localScale = veilBaseScale * (1f + wave * pulseScaleAmplitude);
    }

    /// <summary>
    /// 记录能量幕的初始颜色与缩放作为脉动基线。
    /// 必须只取一次再按系数赋值 —— 若每帧读当前值再乘系数会形成正反馈，缩放会指数膨胀。
    /// </summary>
    private void CacheVeilBaseline()
    {
        if (veil == null)
            return;

        veilBaseColor = veil.color;
        veilBaseScale = veil.transform.localScale;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TrackEnter(other);
        TryEnter(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        TrackExit(other);
    }

    /// <summary>
    /// 记录玩家进入阵内（按 clientId）。主机据此做「全员撤离」聚合判定。
    ///
    /// 与 <see cref="TryEnter"/> 的区别：单机 TryEnter 是一次性确认触发，
    /// 本方法持续维护「当前谁在阵内」，供联机主机轮询判定。
    /// </summary>
    private void TrackEnter(Collider2D other)
    {
        var sync = ResolvePlayerSync(other);
        if (sync != null && sync.IsSpawned && other != null)
        {
            trackedColliders[other] = sync.OwnerClientId;
            playersInside.Add(sync.OwnerClientId);
        }
    }

    private void TrackExit(Collider2D other)
    {
        if (other == null || !trackedColliders.TryGetValue(other, out var clientId))
            return;

        trackedColliders.Remove(other);
        if (!trackedColliders.ContainsValue(clientId))
            playersInside.Remove(clientId);
    }

    /// <summary>从碰撞体解析玩家同步组件。非玩家返回 null。</summary>
    private static Tower.Netcode.PlayerNetworkSync ResolvePlayerSync(Collider2D other)
    {
        if (other == null)
            return null;

        if (other.GetComponentInParent<PlayerMovement2D>() == null)
            return null;

        return other.GetComponentInParent<Tower.Netcode.PlayerNetworkSync>();
    }

    /// <summary>
    /// 判定并触发。拆成公开方法以便 EditMode 直接调用（EditMode 下 Physics2D 查不到新建的碰撞体）。
    /// 只认玩家；已触发过的传送点不再响应。
    ///
    /// 单机触发本地确认；联机不消费传送门，由主机根据阵内玩家持续判定。
    /// </summary>
    public bool TryEnter(Collider2D other)
    {
        if (consumed || other == null)
            return false;

        if (other.GetComponentInParent<PlayerMovement2D>() == null)
            return false;

        // 联机传送门由主机轮询全队站位；不消费碰撞体，也不弹单人确认窗。
        if (IsOnlineSession())
            return false;

        consumed = true;

        // 立刻关掉碰撞体，避免同一帧被多个碰撞体重复触发。
        if (trigger != null)
            trigger.enabled = false;

        Entered?.Invoke(kind);
        return true;
    }

    /// <summary>本端是否处于联机会话（NGO 已启动）。单机返回 false。</summary>
    private static bool IsOnlineSession()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        return nm != null && nm.IsListening;
    }

    /// <summary>
    /// 旧客户端确认请求。联机换层 / 撤退由主机聚合判定，收到旧 RPC 时忽略；
    /// 无网络会话时保留原有事件路径（包括 EditMode 测试）。
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestExecuteServerRpc()
    {
        if (IsOnlineSession())
            return;

        ExecuteRequested?.Invoke(kind);
    }

    /// <summary>主机收到客户端的执行请求（或主机本地确认）后触发，由 Manager 订阅执行换层。</summary>
    public event Action<PortalKind> ExecuteRequested;

    /// <summary>
    /// 复位「已触发」标记，让传送点重新可踩。
    /// 用于玩家在确认弹窗上点「取消」后 —— 触发时已把碰撞体关掉，
    /// 不复位的话玩家离开再回来也踩不动了。
    /// </summary>
    public void ResetConsumed()
    {
        consumed = false;

        if (trigger != null)
            trigger.enabled = true;
    }
}
