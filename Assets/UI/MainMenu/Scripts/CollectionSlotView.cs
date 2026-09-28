using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 收藏展馆里的单个展品：图标直接浮在底板上，无格子底框。
///
/// 三态：
///   未获得（Locked）  —— 纯黑剪影，不可点击
///   已获得（Obtained）—— 灰色剪影 + 呼吸提示，点击点亮
///   已点亮（Lit）     —— 彩色原图，不可再点
///
/// 剪影靠同一张彩图 + 深色 tint 实现，不需要额外美术资源。
/// </summary>
public sealed class CollectionSlotView : MonoBehaviour, IPointerClickHandler
{
    [Header("引用")]
    [SerializeField] private Image icon;

    [Header("剪影配色")]
    [Tooltip("未获得：纯黑剪影。")]
    [SerializeField] private Color lockedTint = new Color(0f, 0f, 0f, 0.88f);

    [Tooltip("已获得待点亮：灰色剪影。")]
    [SerializeField] private Color obtainedTint = new Color(0.45f, 0.47f, 0.50f, 0.80f);

    [Header("呼吸提示")]
    [Tooltip("已获得待点亮时，alpha 在 [min, max] 之间正弦往复。")]
    [SerializeField] private float breathMinAlpha = 0.55f;
    [SerializeField] private float breathMaxAlpha = 0.95f;
    [SerializeField] private float breathSpeed = 2.2f;

    private ItemDefinition item;
    private CollectionState state = CollectionState.Locked;
    private Action<string> clicked;

    /// <summary>本展品的物品 ID；未绑定时为空字符串。</summary>
    public string ItemId => item != null ? item.Id : string.Empty;

    /// <summary>当前三态。</summary>
    public CollectionState State => state;

    /// <summary>绑定物品与状态。onClicked 仅在 Obtained 状态被点击时回调，参数为物品 ID。</summary>
    public void Bind(ItemDefinition definition, CollectionState newState, Action<string> onClicked)
    {
        item = definition;
        clicked = onClicked;

        if (icon != null)
        {
            icon.sprite = definition != null ? definition.Icon : null;
            icon.enabled = definition != null && definition.Icon != null;
        }

        SetState(newState);
    }

    /// <summary>刷新三态表现。</summary>
    public void SetState(CollectionState newState)
    {
        state = newState;

        if (icon == null)
            return;

        switch (state)
        {
            case CollectionState.Lit:
                icon.color = Color.white;
                break;
            case CollectionState.Obtained:
                icon.color = obtainedTint;
                break;
            default:
                icon.color = lockedTint;
                break;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (state != CollectionState.Obtained || item == null)
            return;

        clicked?.Invoke(item.Id);
    }

    private void Update()
    {
        if (state != CollectionState.Obtained || icon == null)
            return;

        // 呼吸效果：提示「可点亮」。
        var t = (Mathf.Sin(Time.unscaledTime * breathSpeed) + 1f) * 0.5f;
        var alpha = Mathf.Lerp(breathMinAlpha, breathMaxAlpha, t);
        var color = obtainedTint;
        color.a = alpha;
        icon.color = color;
    }
}
