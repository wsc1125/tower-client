using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「切换攻击」按钮：只把点击翻译成一个事件，不关心切的是谁 —— 切换规则属于战斗逻辑，
/// 放在 Gameplay 侧的 <c>PlayerWeaponSelector</c>（BattleHUD 不能反向引用 Gameplay）。
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class AttackSwitchButton : MonoBehaviour
{
    /// <summary>按钮被点击时触发一次。</summary>
    public event Action Clicked;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(HandleClick);
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
