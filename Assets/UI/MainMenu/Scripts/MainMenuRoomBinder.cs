using System.Collections;
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

        if (roomPanel != null)
        {
            roomPanel.CreateRequested += OnCreateRequested;
            roomPanel.JoinRequested += OnJoinRequested;
            roomPanel.LeaveRequested += OnLeaveRequested;
        }

        if (modeTab != null)
            modeTab.ModeChanged += OnModeChanged;
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

        // 场景重载后若已在房间内（NetworkSession 跨场景常驻），直接展示房间码。
        if (session.CurrentRoom != null)
            roomPanel?.ShowRoomCode(session.CurrentRoom.room_code);
    }

    private void OnDestroy()
    {
        if (roomPanel != null)
        {
            roomPanel.CreateRequested -= OnCreateRequested;
            roomPanel.JoinRequested -= OnJoinRequested;
            roomPanel.LeaveRequested -= OnLeaveRequested;
        }

        if (modeTab != null)
            modeTab.ModeChanged -= OnModeChanged;

        if (session != null)
        {
            session.RoomChanged -= OnRoomChanged;
            session.RoomLeft -= OnRoomLeft;
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
    }

    private void OnRoomLeft()
    {
        roomPanel?.HideResult();
        roomPanel?.ClearError();
    }
}
