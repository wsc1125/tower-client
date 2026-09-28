using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 传送确认面板：玩家踩到传送点后先弹窗确认，再执行换层 / 撤退。
/// 只负责展示与把「确认 / 取消」转成事件，具体换层由 <see cref="TowerRunManager"/> 处理，
/// 避免 BattleHUD 反向依赖 Gameplay（与 <see cref="DeathPanelController"/> 同构）。
///
/// 面板显示时游戏已被暂停（Time.timeScale = 0），因此按钮必须能响应输入 ——
/// 这依赖 EventSystem 的 <c>UpdateModules</c> 使用 unscaledTime，Unity 默认即是如此。
/// </summary>
public sealed class PortalConfirmPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    [SerializeField] private string nextTitle = "进入下一层";
    [SerializeField] private string nextHint = "确认后将前往更深的一层，本层未拾取的战利品会留在原地。";
    [SerializeField] private string retreatTitle = "撤离本局";
    [SerializeField] private string retreatHint = "确认后将结束本局并结算，携带的战利品会带回仓库。";

    /// <summary>玩家点了确认。</summary>
    public event Action Confirmed;

    /// <summary>玩家点了取消。</summary>
    public event Action Cancelled;

    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        BindButtons();
        Hide();
    }

    private void OnDestroy()
    {
        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(RaiseConfirmed);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(RaiseCancelled);
    }

    /// <summary>
    /// 幂等绑定按钮。拆成公开方法是因为 Awake 跑在 AddComponent 那一刻，
    /// 此时序列化引用可能还没赋值（测试与运行时接线都会遇到），需要能补绑一次。
    /// </summary>
    public void BindButtons()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(RaiseConfirmed);
            confirmButton.onClick.AddListener(RaiseConfirmed);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(RaiseCancelled);
            cancelButton.onClick.AddListener(RaiseCancelled);
        }
    }

    /// <summary>按传送点种类显示确认弹窗。</summary>
    public void Show(PortalKind kind)
    {
        if (panelRoot == null)
            return;

        // Awake 可能跑在序列化引用赋值之前，这里补绑一次（幂等）。
        BindButtons();

        var isRetreat = kind == PortalKind.Retreat;

        if (titleText != null)
            titleText.text = isRetreat ? retreatTitle : nextTitle;

        if (hintText != null)
            hintText.text = isRetreat ? retreatHint : nextHint;

        panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void RaiseConfirmed()
    {
        Confirmed?.Invoke();
    }

    private void RaiseCancelled()
    {
        Cancelled?.Invoke();
    }
}
