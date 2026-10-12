using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「召唤复活」按钮：只把点击翻译成一个事件，不关心召唤逻辑 ——
/// 召唤规则属于战斗逻辑，放在 Gameplay 侧的 <c>PlayerNetworkSync</c>
/// （BattleHUD 不能反向引用 Gameplay）。
///
/// 与 <see cref="AttackSwitchButton"/> 同构：HUD 只负责「喊一声被点了」，
/// 由 Gameplay 侧订阅并决定是否发起召唤。
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class SummonReviveButton : MonoBehaviour
{
    /// <summary>按钮被点击时触发一次。</summary>
    public event Action Clicked;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(HandleClick);

        // 两个无贴图 UI 矩形组成临时矢量加号，后续可替换整个图标子节点。
        CreateStroke("Horizontal", new Vector2(52f, 12f));
        CreateStroke("Vertical", new Vector2(12f, 52f));
    }

    private void CreateStroke(string name, Vector2 size)
    {
        var stroke = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        stroke.transform.SetParent(transform, false);
        var rect = (RectTransform)stroke.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        var image = stroke.GetComponent<Image>();
        image.color = new Color(0.96f, 1f, 0.95f, 1f);
        image.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        Clicked?.Invoke();
    }
}
