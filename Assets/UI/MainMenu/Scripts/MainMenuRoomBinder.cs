using System.Collections;
using System.Collections.Generic;
using Tower.Netcode;
using Tower.Network;
using UnityEngine;

/// <summary>
/// 房间面板接线：把 <see cref="RoomPanelController"/> 的事件接到 <see cref="NetworkSession"/>。
///
/// 与 <see cref="MainMenuAccountBinder"/> 同样的分层理由：
/// RoomPanelController 在 Assembly-CSharp（UI 层），NetworkSession 在 Network 程序集。
/// 让 UI 面板直接引用 Network 会把两个程序集绑死，这里做一层薄适配。
///
/// 同时负责：联机模式下点「出发」时，若尚未建房/进房则先弹出房间面板。
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuRoomBinder : MonoBehaviour
{
    [Header("接线")]
    [Tooltip("房间面板。留空则自动在子层级里查找。")]
    [SerializeField] private RoomPanelController roomPanel;

    [Tooltip("单机 / 联机模式选择器。用于判断当前是否联机模式。")]
    [SerializeField] private ModeTabSelector modeTab;

    [Tooltip("出发流程。房主点出发 / 成员收到开始信号后，由它切场景。")]
    [SerializeField] private DepartFlow departFlow;

    [Header("行为")]
    [Tooltip("切到联机模式时自动弹出房间面板。")]
    [SerializeField] private bool openOnOnlineMode = true;

    private NetworkSession session;

    private void Awake()
    {
        if (roomPanel == null)
            roomPanel = GetComponentInChildren<RoomPanelController>(true);

        if (modeTab == null)
            modeTab = GetComponentInChildren<ModeTabSelector>(true);

        if (departFlow == null)
            departFlow = GetComponent<DepartFlow>();

        if (roomPanel != null)
        {
            roomPanel.CreateRequested += OnCreateRequested;
            roomPanel.JoinRequested += OnJoinRequested;
            roomPanel.LeaveRequested += OnLeaveRequested;
            roomPanel.DepartRequested += OnDepartRequested;
            roomPanel.Closed += OnPanelClosed;
        }

        if (modeTab != null)
            modeTab.ModeChanged += OnModeChanged;

        // 联机失败时留在房间界面显示原因，不进入战斗。
        if (MultiplayerCoordinator.Instance != null)
            MultiplayerCoordinator.Instance.Failed += OnMultiplayerFailed;
    }

    private void Start()
    {
        session = NetworkSession.Instance;

        if (session == null)
        {
            Debug.LogWarning("[MainMenuRoomBinder] 场景中未找到 NetworkSession，联机房间功能不可用。");
            return;
        }

        session.RoomChanged += OnRoomChanged;
        session.RoomLeft += OnRoomLeft;

        // 成员端：房主发起开始游戏后，自动进入战斗场景。
        if (session.Signal != null)
            session.Signal.GameStarting += OnGameStarting;

        // 场景重载后若已在房间内（NetworkSession 跨场景常驻），直接展示房间码。
        if (session.CurrentRoom != null)
        {
            roomPanel?.ShowRoomCode(session.CurrentRoom.room_code);
            RefreshDepartState(session.CurrentRoom);
            RefreshMembers(session.CurrentRoom);
        }
    }

    private void OnDestroy()
    {
        if (roomPanel != null)
        {
            roomPanel.CreateRequested -= OnCreateRequested;
            roomPanel.JoinRequested -= OnJoinRequested;
            roomPanel.LeaveRequested -= OnLeaveRequested;
            roomPanel.DepartRequested -= OnDepartRequested;
            roomPanel.Closed -= OnPanelClosed;
        }

        if (modeTab != null)
            modeTab.ModeChanged -= OnModeChanged;

        if (MultiplayerCoordinator.Instance != null)
            MultiplayerCoordinator.Instance.Failed -= OnMultiplayerFailed;

        if (session != null)
        {
            session.RoomChanged -= OnRoomChanged;
            session.RoomLeft -= OnRoomLeft;

            if (session.Signal != null)
                session.Signal.GameStarting -= OnGameStarting;
        }
    }

    /// <summary>当前是否处于联机模式。</summary>
    public bool IsOnlineMode => modeTab != null && modeTab.IsOnline;

    /// <summary>当前是否已在房间内。</summary>
    public bool IsInRoom => session != null && session.CurrentRoom != null;

    /// <summary>
    /// 联机模式下点「出发」时调用。
    /// 返回 true 表示已就绪（在房间内），可以继续；false 表示已弹出房间面板，需等待。
    /// </summary>
    public bool EnsureReadyForDepart()
    {
        if (IsInRoom)
            return true;

        roomPanel?.Open();
        roomPanel?.ShowError("请先创建或加入一个房间");

        return false;
    }

    private void OnModeChanged(int mode)
    {
        if (mode == ModeTabSelector.OnlineMode)
        {
            if (openOnOnlineMode)
                roomPanel?.Open();

            return;
        }

        // 切回单机：若还在房间里，静默退出，避免留下幽灵成员。
        if (IsInRoom)
            StartCoroutine(session.LeaveRoom(null, null));

        roomPanel?.Close();
    }

    /// <summary>房主点了「出发」：启动联机协调器，由它建立连接并统一切场。</summary>
    private void OnDepartRequested()
    {
        if (session == null || session.CurrentRoom == null)
            return;

        // 仅房主可发起。
        if (!IsLocalHost())
        {
            roomPanel?.ShowError("只有房主可以开始游戏");
            return;
        }

        if (MemberCount() <= 1)
        {
            roomPanel?.ShowError("至少需要 2 名玩家才能开始");
            return;
        }

        var coordinator = MultiplayerCoordinator.Instance;
        if (coordinator == null)
        {
            roomPanel?.ShowError("联机组件未就绪");
            return;
        }

        // 广播开始信号，成员端收到 GameStarting 后启动 Client。
        session.Signal?.SendStartGame();

        // 房主启动 Host 流程：创建会话 → 监听 → 等成员 → 切场景。
        StartCoroutine(coordinator.StartHostFlow());
    }

    /// <summary>成员端收到房主的开始信号：启动 Client 流程，等待房主切场景。</summary>
    private void OnGameStarting(GameStartingPayload payload)
    {
        // 房主自己已通过 StartHostFlow 进入，这里只处理成员端。
        if (IsLocalHost())
            return;

        var coordinator = MultiplayerCoordinator.Instance;
        if (coordinator == null)
        {
            roomPanel?.ShowError("联机组件未就绪");
            return;
        }

        StartCoroutine(coordinator.StartClientFlow());
    }

    /// <summary>联机失败：留在房间界面并显示原因，不进入战斗。</summary>
    private void OnMultiplayerFailed(string reason)
    {
        roomPanel?.ShowError(reason);
    }

    /// <summary>本端是否为当前房间的房主。</summary>
    private bool IsLocalHost()
    {
        if (session == null || session.CurrentRoom == null || session.Account == null)
            return false;

        return session.CurrentRoom.host_account == session.Account.account_id;
    }

    private int MemberCount()
    {
        var members = session != null && session.CurrentRoom != null ? session.CurrentRoom.members : null;
        return members?.Length ?? 0;
    }

    /// <summary>按房间状态刷新出发按钮（仅房主 + 人数 > 1 可点）。</summary>
    private void RefreshDepartState(RoomDto room)
    {
        if (roomPanel == null)
            return;

        var isHost = room != null && session != null && session.Account != null
                     && room.host_account == session.Account.account_id;
        var count = room?.members?.Length ?? 0;
        roomPanel.SetDepartState(isHost, count);
    }

    /// <summary>
    /// 房间面板被关闭：退出联机界面，模式选择器自动切回单机。
    /// 切回单机会经 <see cref="OnModeChanged"/> 静默离开房间，避免留下幽灵成员。
    /// </summary>
    private void OnPanelClosed()
    {
        if (modeTab != null && modeTab.IsOnline)
            modeTab.SetMode(ModeTabSelector.SingleMode);
    }

    private void OnCreateRequested(string roomName, int maxPlayers)
    {
        if (session == null)
        {
            roomPanel?.ShowError("未连接服务器");
            return;
        }

        StartCoroutine(CreateRoutine(roomName, maxPlayers));
    }

    private void OnJoinRequested(string roomCode)
    {
        if (session == null)
        {
            roomPanel?.ShowError("未连接服务器");
            return;
        }

        StartCoroutine(JoinRoutine(roomCode));
    }

    private void OnLeaveRequested()
    {
        if (session == null)
            return;

        StartCoroutine(LeaveRoutine());
    }

    private IEnumerator CreateRoutine(string roomName, int maxPlayers)
    {
        yield return session.CreateRoom(roomName, maxPlayers, true, null, OnRequestFailed);
    }

    private IEnumerator JoinRoutine(string roomCode)
    {
        yield return session.JoinRoom(roomCode, null, OnRequestFailed);
    }

    private IEnumerator LeaveRoutine()
    {
        yield return session.LeaveRoom(null, OnRequestFailed);

        // 退出联机界面即退出联机：离开房间后模式切回单机。
        // 用 SetMode 而非直接改 UI，让 OnModeChanged 统一处理（含关闭面板）。
        if (modeTab != null && modeTab.IsOnline)
            modeTab.SetMode(ModeTabSelector.SingleMode);
    }

    private void OnRequestFailed(ApiError error)
    {
        roomPanel?.ShowError(error != null ? error.Message : "请求失败");
    }

    private void OnRoomChanged(RoomDto room)
    {
        if (room == null)
            return;

        roomPanel?.ShowRoomCode(room.room_code);
        RefreshDepartState(room);
        RefreshMembers(room);
    }

    /// <summary>
    /// 刷新房间成员列表（头像 + 昵称）。
    ///
    /// 头像需要异步下载，因此先立即用「无头像」的占位数据渲染一遍
    /// （让玩家马上看到有人进来了），再逐个下载头像并回填。
    /// </summary>
    private void RefreshMembers(RoomDto room)
    {
        if (roomPanel == null)
            return;

        var members = room?.members;
        if (members == null || members.Length == 0)
        {
            roomPanel.SetMembers(null);
            return;
        }

        var localId = session?.Account?.account_id ?? 0;

        var views = new List<RoomPanelController.MemberView>(members.Length);
        foreach (var m in members)
        {
            if (m == null)
                continue;

            views.Add(new RoomPanelController.MemberView
            {
                DisplayName = m.display_name,
                IsHost = m.is_host,
                IsLocal = m.account_id == localId,
                Avatar = null,
            });
        }

        roomPanel.SetMembers(views);

        // 头像异步补齐：下载完成后重建列表（人数上限 4，重建成本可忽略）。
        StartCoroutine(LoadAvatarsRoutine(members, localId));
    }

    private IEnumerator LoadAvatarsRoutine(RoomMemberDto[] members, long localId)
    {
        var views = new List<RoomPanelController.MemberView>(members.Length);

        foreach (var m in members)
        {
            if (m == null)
                continue;

            Texture2D avatar = null;
            if (!string.IsNullOrEmpty(m.avatar_url) && session?.Api != null)
                yield return session.Api.DownloadAvatar(m.avatar_url, tex => avatar = tex);

            views.Add(new RoomPanelController.MemberView
            {
                DisplayName = m.display_name,
                IsHost = m.is_host,
                IsLocal = m.account_id == localId,
                Avatar = avatar,
            });
        }

        roomPanel?.SetMembers(views);
    }

    private void OnRoomLeft()
    {
        roomPanel?.HideResult();
        roomPanel?.ClearError();
    }
}
