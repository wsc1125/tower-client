using System;
using UnityEngine;

/// <summary>
/// 层间传送点：玩家踩上去触发一次，把「踩到了哪种传送点」抛给订阅者。
/// 只抛事件，不认识 <see cref="TowerRunManager"/>，由 Manager 在生成时订阅。
///
/// 视觉上刻意与掉落物的地面光圈区分开：掉落物是「贴地的一滩光」，
/// 传送点是「立起来的门」—— 门框 + 门内能量幕 + 地面基座，
/// 并且带呼吸脉动，静止时也能一眼看出是可交互的入口。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class FloorPortal2D : MonoBehaviour
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
    }

    private void Update()
    {
        if (veil == null || consumed)
            return;

        // 本地冻结（联机弹窗）期间脉动停住：世界照常推进，但传送点看起来是静止的。
        // 单机下 LocalPauseScope.IsFrozen 恒为 false，行为与改造前一致。
        if (LocalPauseScope.IsFrozen)
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
        TryEnter(other);
    }

    /// <summary>
    /// 判定并触发。拆成公开方法以便 EditMode 直接调用（EditMode 下 Physics2D 查不到新建的碰撞体）。
    /// 只认玩家；已触发过的传送点不再响应。
    /// </summary>
    public bool TryEnter(Collider2D other)
    {
        if (consumed || other == null)
            return false;

        if (other.GetComponentInParent<PlayerMovement2D>() == null)
            return false;

        consumed = true;

        // 立刻关掉碰撞体，避免同一帧被多个碰撞体重复触发。
        if (trigger != null)
            trigger.enabled = false;

        Entered?.Invoke(kind);
        return true;
    }

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
