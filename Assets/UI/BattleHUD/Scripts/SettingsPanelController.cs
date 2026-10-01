using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 局内设置面板：点 HUD 右上角设置按钮打开，目前只有「退出游戏」一项。
/// 点「退出游戏」先弹二次确认（提示会丢失所有携带物），确认后抛 <see cref="QuitRequested"/>，
/// 由 Gameplay 侧的 <see cref="TowerRunManager"/> 执行「丢弃局内携带 + 回主菜单」。
///
/// 与 <see cref="PortalConfirmPanelController"/> 同构：只负责展示与把点击转成事件，
/// 不反向依赖 Gameplay（BattleHUD 程序集不引用 Gameplay）。
///
/// 面板显示时游戏已被冻结（<see cref="LocalPauseScope"/>），按钮仍能响应输入 ——
/// 这依赖 EventSystem 的 UpdateModules 使用 unscaledTime，Unity 默认即是如此。
/// </summary>
public sealed class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button quitButton;

    [Header("退出确认弹窗")]
    [SerializeField] private GameObject confirmRoot;
    [SerializeField] private TMP_Text confirmTitleText;
    [SerializeField] private TMP_Text confirmHintText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    [SerializeField] private string confirmTitle = "退出游戏";
    [SerializeField] private string confirmHint = "退出后将返回主界面，并丢失所有携带物（装备槽内的物品也会一并丢失）。";

    /// <summary>玩家确认退出本局。由 Gameplay 侧订阅并执行实际流程。</summary>
    public event Action QuitRequested;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        BindButtons();
        Close();
    }

    private void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);

        if (quitButton != null)
            quitButton.onClick.RemoveListener(OpenConfirm);

        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(RaiseQuitRequested);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(CloseConfirm);
    }

    /// <summary>
    /// 幂等绑定按钮。拆成公开方法是因为 Awake 跑在 AddComponent 那一刻，
    /// 此时序列化引用可能还没赋值（测试与运行时接线都会遇到），需要能补绑一次。
    /// </summary>
    public void BindButtons()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(OpenConfirm);
            quitButton.onClick.AddListener(OpenConfirm);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(RaiseQuitRequested);
            confirmButton.onClick.AddListener(RaiseQuitRequested);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(CloseConfirm);
            cancelButton.onClick.AddListener(CloseConfirm);
        }
    }

    /// <summary>打开设置面板（同时收起可能残留的确认弹窗）。</summary>
    public void Open()
    {
        if (panelRoot == null)
            return;

        BindButtons();
        CloseConfirm();
        panelRoot.SetActive(true);
    }

    /// <summary>关闭设置面板（连同确认弹窗一起收起）。</summary>
    public void Close()
    {
        CloseConfirm();

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>设置按钮入口：已打开则关闭，否则打开。</summary>
    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    /// <summary>弹出「是否退出游戏」二次确认。</summary>
    public void OpenConfirm()
    {
        if (confirmRoot == null)
            return;

        BindButtons();

        if (confirmTitleText != null)
            confirmTitleText.text = confirmTitle;

        if (confirmHintText != null)
            confirmHintText.text = confirmHint;

        confirmRoot.SetActive(true);
    }

    /// <summary>收起二次确认，回到设置面板。</summary>
    public void CloseConfirm()
    {
        if (confirmRoot != null)
            confirmRoot.SetActive(false);
    }

    private void RaiseQuitRequested()
    {
        QuitRequested?.Invoke();
    }
}
