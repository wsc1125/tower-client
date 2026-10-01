using System.Collections;
using TMPro;
using Tower.Network;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主菜单账号接线：把 <see cref="LoginPanelController"/> 的提交事件接到 <see cref="NetworkSession"/>，
/// 并把登录账号的昵称写入主菜单左上角的 <c>PlayerName</c>。
///
/// 之所以单独一个组件而不是塞进 LoginPanelController：
/// LoginPanelController 在 Assembly-CSharp（UI 层），而 NetworkSession 在 Network 程序集。
/// 让 UI 面板直接引用 Network 会把两个程序集绑死；这里做一层薄适配，UI 只认事件。
///
/// 登录面板在场景启动时自动弹出（<see cref="openOnStart"/>），登录成功后关闭。
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuAccountBinder : MonoBehaviour
{
    [Header("接线")]
    [Tooltip("登录面板。留空则自动在子层级里查找。")]
    [SerializeField] private LoginPanelController loginPanel;

    [Tooltip("主菜单左上角的昵称文本。")]
    [SerializeField] private TMP_Text playerNameText;

    [Tooltip("登出按钮。可选；未接线时不显示登出入口。")]
    [SerializeField] private Button logoutButton;

    [Header("行为")]
    [Tooltip("场景启动时自动弹出登录面板。")]
    [SerializeField] private bool openOnStart = true;

    [Tooltip("登录成功后是否关闭面板。")]
    [SerializeField] private bool closeOnSuccess = true;

    [Tooltip("未登录时昵称栏显示的占位文案。")]
    [SerializeField] private string guestName = "未登录";

    private NetworkSession session;

    private void Awake()
    {
        if (loginPanel == null)
            loginPanel = GetComponentInChildren<LoginPanelController>(true);

        if (loginPanel != null)
            loginPanel.SubmitRequested += OnSubmitRequested;

        if (logoutButton != null)
            logoutButton.onClick.AddListener(OnLogoutClicked);
    }

    private void Start()
    {
        session = NetworkSession.Instance;

        if (session == null)
        {
            Debug.LogWarning("[MainMenuAccountBinder] 场景中未找到 NetworkSession，登录功能不可用。");
            return;
        }

        session.LoggedIn += OnLoggedIn;
        session.LoggedOut += OnLoggedOut;

        // 场景重载后若已登录（NetworkSession 跨场景常驻），直接沿用登录态。
        if (session.Account != null)
            OnLoggedIn(session.Account);
        else if (openOnStart)
            loginPanel?.Open();
    }

    private void OnDestroy()
    {
        if (loginPanel != null)
            loginPanel.SubmitRequested -= OnSubmitRequested;

        if (logoutButton != null)
            logoutButton.onClick.RemoveListener(OnLogoutClicked);

        if (session != null)
        {
            session.LoggedIn -= OnLoggedIn;
            session.LoggedOut -= OnLoggedOut;
        }
    }

    /// <summary>面板提交：按页签分派到登录或注册。</summary>
    private void OnSubmitRequested(bool isRegister, string username, string password, string displayName, byte[] avatar)
    {
        if (session == null)
        {
            loginPanel?.ShowError("未连接服务器");
            return;
        }

        StartCoroutine(isRegister
            ? RegisterRoutine(username, password, displayName, avatar)
            : LoginRoutine(username, password));
    }

    private IEnumerator LoginRoutine(string username, string password)
    {
        yield return session.Login(username, password, null, OnRequestFailed);
    }

    private IEnumerator RegisterRoutine(string username, string password, string displayName, byte[] avatar)
    {
        yield return session.Register(username, password, displayName, avatar, null, OnRequestFailed);
    }

    private void OnRequestFailed(ApiError error)
    {
        loginPanel?.ShowError(error != null ? error.Message : "请求失败");
    }

    private void OnLoggedIn(AccountDto account)
    {
        if (playerNameText != null)
            playerNameText.text = string.IsNullOrEmpty(account?.display_name)
                ? account?.username
                : account.display_name;

        if (logoutButton != null)
            logoutButton.gameObject.SetActive(true);

        if (closeOnSuccess)
            loginPanel?.Close();
    }

    private void OnLoggedOut()
    {
        if (playerNameText != null)
            playerNameText.text = guestName;

        if (logoutButton != null)
            logoutButton.gameObject.SetActive(false);

        loginPanel?.Open();
    }

    private void OnLogoutClicked()
    {
        if (session != null)
            StartCoroutine(session.Logout());
    }
}
