using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主菜单设置面板：点主菜单右上角设置按钮打开，目前只有「退出登录」一项。
///
/// 与局内 <see cref="SettingsPanelController"/> 同构（复用同一套扁平视觉），
/// 但只负责展示与把点击转成事件，不直接依赖 <c>Tower.Network</c>：
/// 点「退出登录」抛 <see cref="LogoutRequested"/>，由 <see cref="MainMenuAccountBinder"/>
/// 调用 NetworkSession 执行登出。
/// </summary>
public sealed class MainMenuSettingsPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button logoutButton;

    /// <summary>玩家点了「退出登录」。由 <see cref="MainMenuAccountBinder"/> 订阅并执行实际流程。</summary>
    public event Action LogoutRequested;

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

        if (logoutButton != null)
            logoutButton.onClick.RemoveListener(RaiseLogoutRequested);
    }

    /// <summary>
    /// 幂等绑定按钮。拆成公开方法是因为 Awake 跑在 AddComponent 那一刻，
    /// 此时序列化引用可能还没赋值，需要能补绑一次。
    /// </summary>
    public void BindButtons()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        if (logoutButton != null)
        {
            logoutButton.onClick.RemoveListener(RaiseLogoutRequested);
            logoutButton.onClick.AddListener(RaiseLogoutRequested);
        }
    }

    /// <summary>打开设置面板。</summary>
    public void Open()
    {
        if (panelRoot == null)
            return;

        BindButtons();
        panelRoot.SetActive(true);
    }

    /// <summary>关闭设置面板。</summary>
    public void Close()
    {
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

    private void RaiseLogoutRequested()
    {
        LogoutRequested?.Invoke();
    }
}
