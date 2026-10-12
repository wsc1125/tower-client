using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 死亡提示面板：全屏遮罩 + 「你已死亡」文案 + 返回主菜单按钮。
/// 只负责展示与把「点按钮」转成事件，切场景由 <c>PlayerDeathFlow2D</c> 处理，
/// 避免 BattleHUD 反向依赖 Gameplay（与 <see cref="TowerResultPanelController"/> 同构）。
///
/// 面板显示时游戏已被暂停（Time.timeScale = 0），因此按钮必须能响应输入 ——
/// 这依赖 EventSystem 的 <c>UpdateModules</c> 使用 unscaledTime，Unity 默认即是如此。
/// </summary>
public sealed class DeathPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private Button confirmButton;

    [SerializeField] private string title = "你已死亡";
    [SerializeField] private string hint = "本局携带的战利品已全部丢失";

    /// <summary>玩家点了「返回主菜单」。</summary>
    public event Action Confirmed;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        BindConfirmButton();
        ApplyTexts();
        Hide();
    }

    private void OnDestroy()
    {
        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(RaiseConfirmed);
    }

    /// <summary>
    /// 幂等绑定确认按钮。拆成公开方法是因为 Awake 跑在 AddComponent 那一刻，
    /// 此时序列化引用可能还没赋值（测试与运行时接线都会遇到），需要能补绑一次。
    /// </summary>
    public void BindConfirmButton()
    {
        if (confirmButton == null)
            return;

        confirmButton.onClick.RemoveListener(RaiseConfirmed);
        confirmButton.onClick.AddListener(RaiseConfirmed);
    }

    /// <summary>显示死亡提示。</summary>
    public void Show()
    {
        if (panelRoot == null)
            return;

        ApplyTexts();
        panelRoot.SetActive(true);
    }

    /// <summary>显示全灭提示并隐藏确认按钮，退出由上层按 unscaled time 执行。</summary>
    public void ShowAutoExit(string autoTitle, string autoHint)
    {
        if (panelRoot == null)
            return;

        if (titleText != null)
            titleText.text = autoTitle;
        if (hintText != null)
            hintText.text = autoHint;
        if (confirmButton != null)
            confirmButton.gameObject.SetActive(false);

        panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (confirmButton != null)
            confirmButton.gameObject.SetActive(true);

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void ApplyTexts()
    {
        if (titleText != null)
            titleText.text = title;

        if (hintText != null)
            hintText.text = hint;
    }

    private void RaiseConfirmed()
    {
        Confirmed?.Invoke();
    }
}
