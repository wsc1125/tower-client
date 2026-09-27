using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 收藏展馆里的单个图标：三态表现 + 点击点亮。
///
/// 无格子底框 —— 图标直接浮在面板背景上，尺寸比背包内图标大（192px）。
/// 剪影靠同一张彩图 + 深色 tint 实现，不需要额外美术资源。
/// </summary>
public sealed class CollectionSlotView : MonoBehaviour, IPointerClickHandler
{
    [Header("引用")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image qualityPlate;

    [Header("剪影配色")]
    [Tooltip("未获得：黑色剪影。")]
    [SerializeField] private Color lockedTint = new Color(0f, 0f, 0f, 0.55f);

    [Tooltip("已获得待点亮：灰色剪影。")]
    [SerializeField] private Color obtainedTint = new Color(0.42f, 0.44f, 0.46f, 0.75f);

    [Header("呼吸提示")]
    [Tooltip("已获得待点亮时，alpha 在 [min, max] 之间正弦往复。")]
    [SerializeField] private float breathMinAlpha = 0.6f;
    [SerializeField] private float breathMaxAlpha = 0.9f;
    [SerializeField] private float breathSpeed = 2f;

    private ItemDefinition item;
    private CollectionState state = CollectionState.Locked;
    private Action<string> clicked;

    /// <summary>本格物品的 ID；未绑定时为空字符串。</summary>
    public string ItemId => item != null ? item.Id : string.Empty;

    /// <summary>绑定物品与状态。onClicked 仅在 Obtained 状态被点击时回调，参数为物品 ID。</summary>
    public void Bind(ItemDefinition definition, CollectionState newState, Action<string> onClicked)
    {
        item = definition;
        clicked = onClicked;

        if (icon != null)
            icon.sprite = definition != null ? definition.Icon : null;

        if (nameText != null)
            nameText.text = definition != null ? definition.DisplayName : string.Empty;

        SetState(newState);
    }

    /// <summary>刷新三态表现。</summary>
    public void SetState(CollectionState newState)
    {
        state = newState;

        if (icon != null)
        {
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

        // 品质底板只在点亮后显示。
        if (qualityPlate != null)
        {
            qualityPlate.enabled = state == CollectionState.Lit && item != null;
            if (qualityPlate.enabled)
                qualityPlate.color = UiTokens.RarityColor(item.Rarity);
        }

        // 未获得时名称隐藏为 ???。
        if (nameText != null)
        {
            if (state == CollectionState.Locked)
                nameText.text = "???";
            else if (item != null)
                nameText.text = item.DisplayName;
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
