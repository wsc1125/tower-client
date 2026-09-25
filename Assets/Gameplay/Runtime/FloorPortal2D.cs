using System;
using UnityEngine;

/// <summary>传送点的种类。</summary>
public enum PortalKind
{
    /// <summary>进入下一层。</summary>
    Next,

    /// <summary>撤退结算。</summary>
    Retreat,
}

/// <summary>
/// 层间传送点：玩家踩上去触发一次，把「踩到了哪种传送点」抛给订阅者。
/// 只抛事件，不认识 <see cref="TowerRunManager"/>，由 Manager 在生成时订阅。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class FloorPortal2D : MonoBehaviour
{
    [SerializeField] private PortalKind kind;
    [SerializeField] private SpriteRenderer visual;

    private Collider2D trigger;
    private bool consumed;

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
}
