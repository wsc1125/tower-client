using System;
using System.Collections;
using UnityEngine;

namespace Tower.Network
{
    /// <summary>
    /// 联机会话管理器：把 HTTP 客户端与信令客户端串起来，并绑定本机玩家身份。
    ///
    /// 挂在场景里的 MonoBehaviour，因为：
    ///   1. 协程需要 MonoBehaviour 驱动；
    ///   2. 需要在场景切换时保持（DontDestroyOnLoad）。
    ///
    /// 单机流程完全不碰这个类 —— 它只在玩家选择「联机」模式后启用。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSession : MonoBehaviour
    {
        private static NetworkSession instance;

        /// <summary>全局单例。未挂载时返回 null。</summary>
        public static NetworkSession Instance => instance;

        [Tooltip("服务端地址。本地开发用 localhost；局域网测试填主机内网 IP。")]
        [SerializeField] private string serverHost = "localhost";

        [Tooltip("服务端端口。")]
        [SerializeField] private int serverPort = 8080;

        private NetworkConfig config;
        private ApiClient api;
        private SignalClient signal;

        /// <summary>HTTP 客户端。</summary>
        public ApiClient Api => api;

        /// <summary>信令客户端。</summary>
        public SignalClient Signal => signal;

        /// <summary>配置。</summary>
        public NetworkConfig Config => config;

        /// <summary>当前登录账号。未登录时为 null。</summary>
        public AccountDto Account => api?.CurrentAccount;

        /// <summary>当前所在房间。未加入时为 null。</summary>
        public RoomDto CurrentRoom { get; private set; }

        /// <summary>当前本局游戏会话。未创建时为 null。</summary>
        public GameSessionDto CurrentSession { get; private set; }

        /// <summary>登录成功。</summary>
        public event Action<AccountDto> LoggedIn;

        /// <summary>登出。</summary>
        public event Action LoggedOut;

        /// <summary>房间状态变化（加入 / 成员进出 / 准备状态）。</summary>
        public event Action<RoomDto> RoomChanged;

        /// <summary>已离开房间。</summary>
        public event Action RoomLeft;

        /// <summary>本局游戏会话变化（创建 / 就绪 / 关闭）。</summary>
        public event Action<GameSessionDto> SessionChanged;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            config = NetworkConfig.ForHost(serverHost, serverPort);
            api = new ApiClient(config);
            signal = new SignalClient(config);

            HookSignalEvents();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            UnhookSignalEvents();
        }

        // ---- 账号 ----

        /// <summary>注册并自动登录。</summary>
        /// <param name="avatar">头像图片字节。为 null 或空数组时不带头像。</param>
        public IEnumerator Register(string username, string password, string displayName, byte[] avatar,
            Action<AccountDto> onSuccess, Action<ApiError> onError)
        {
            return api.Register(username, password, displayName, avatar,
                response => HandleLoggedIn(response.account, onSuccess),
                onError);
        }

        /// <summary>登录。</summary>
        public IEnumerator Login(string username, string password,
            Action<AccountDto> onSuccess, Action<ApiError> onError)
        {
            return api.Login(username, password,
                response => HandleLoggedIn(response.account, onSuccess),
                onError);
        }

        /// <summary>登出：断开信令、清空登录态、把本机玩家身份还原为单机。</summary>
        public IEnumerator Logout()
        {
            if (signal != null && signal.IsConnected)
                yield return signal.Disconnect();

            CurrentRoom = null;
            api.Logout();

            // 还原为单机身份 —— 否则后续单机流程会继续读写联机账号的数据。
            GameSession.SetLocalPlayerId(null);

            LoggedOut?.Invoke();
        }

        /// <summary>登录成功后的统一处理：绑定本机玩家身份。</summary>
        private void HandleLoggedIn(AccountDto account, Action<AccountDto> onSuccess)
        {
            if (account == null)
                return;

            // 关键：把「本机玩家」绑定到账号。
            // 此后全项目的 GameSession.LocalPlayer 都指向该账号的上下文，
            // 装备 / 携带 / 磨损都落到这个账号上。
            GameSession.SetLocalPlayerId(account.account_id.ToString());

            LoggedIn?.Invoke(account);
            onSuccess?.Invoke(account);
        }

        // ---- 房间 ----

        /// <summary>创建房间并自动连入信令。</summary>
        public IEnumerator CreateRoom(string name, int maxPlayers, bool isPublic,
            Action<RoomDto> onSuccess, Action<ApiError> onError)
        {
            RoomDto created = null;
            ApiError failure = null;

            yield return api.CreateRoom(name, maxPlayers, isPublic,
                room => created = room,
                error => failure = error);

            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            yield return ConnectSignal(created.room_code, onSuccess, onError);
        }

        /// <summary>加入房间并自动连入信令。</summary>
        public IEnumerator JoinRoom(string roomCode, Action<RoomDto> onSuccess, Action<ApiError> onError)
        {
            RoomDto joined = null;
            ApiError failure = null;

            yield return api.JoinRoom(roomCode,
                room => joined = room,
                error => failure = error);

            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            yield return ConnectSignal(joined.room_code, onSuccess, onError);
        }

        /// <summary>离开房间：先断信令，再调 HTTP 离开。</summary>
        public IEnumerator LeaveRoom(Action onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onSuccess?.Invoke();
                yield break;
            }

            var code = CurrentRoom.room_code;

            // 先断信令：否则服务端会因连接仍在而认为成员未离开。
            if (signal.IsConnected)
                yield return signal.Disconnect();

            ApiError failure = null;
            yield return api.LeaveRoom(code, _ => { }, error => failure = error);

            CurrentSession = null;
            CurrentRoom = null;
            RoomLeft?.Invoke();

            if (failure != null)
                onError?.Invoke(failure);
            else
                onSuccess?.Invoke();
        }

        /// <summary>关闭房间（仅房主）。</summary>
        public IEnumerator CloseRoom(Action onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onError?.Invoke(new ApiError("ROOM_NOT_FOUND", "当前不在房间内"));
                yield break;
            }

            var code = CurrentRoom.room_code;

            if (signal.IsConnected)
                yield return signal.Disconnect();

            ApiError failure = null;
            yield return api.CloseRoom(code, () => { }, error => failure = error);

            CurrentSession = null;
            CurrentRoom = null;
            RoomLeft?.Invoke();

            if (failure != null)
                onError?.Invoke(failure);
            else
                onSuccess?.Invoke();
        }

        /// <summary>刷新房间列表。</summary>
        public IEnumerator ListRooms(Action<RoomListEntryDto[]> onSuccess, Action<ApiError> onError)
        {
            return api.ListRooms(onSuccess, onError);
        }

        // ---- 游戏会话 ----

        /// <summary>房主创建本局游戏会话。</summary>
        public IEnumerator CreateSession(Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onError?.Invoke(new ApiError("ROOM_NOT_FOUND", "当前不在房间内"));
                yield break;
            }

            GameSessionDto created = null;
            ApiError failure = null;

            yield return api.CreateSession(CurrentRoom.room_code,
                session => created = session,
                error => failure = error);

            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            CurrentSession = created;
            SessionChanged?.Invoke(CurrentSession);
            onSuccess?.Invoke(created);
        }

        /// <summary>查询房间当前会话（成员补齐错过的推送）。</summary>
        public IEnumerator FetchSession(Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onError?.Invoke(new ApiError("ROOM_NOT_FOUND", "当前不在房间内"));
                yield break;
            }

            GameSessionDto fetched = null;
            ApiError failure = null;

            yield return api.GetSession(CurrentRoom.room_code,
                session => fetched = session,
                error => failure = error);

            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            CurrentSession = fetched;
            SessionChanged?.Invoke(CurrentSession);
            onSuccess?.Invoke(fetched);
        }

        /// <summary>房主写入连接地址，标记会话可连接。</summary>
        public IEnumerator MarkSessionReady(string hostAddress, int hostPort,
            Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onError?.Invoke(new ApiError("ROOM_NOT_FOUND", "当前不在房间内"));
                yield break;
            }

            GameSessionDto ready = null;
            ApiError failure = null;

            yield return api.MarkSessionReady(CurrentRoom.room_code, hostAddress, hostPort,
                session => ready = session,
                error => failure = error);

            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            CurrentSession = ready;
            SessionChanged?.Invoke(CurrentSession);
            onSuccess?.Invoke(ready);
        }

        /// <summary>房主结束本局会话。</summary>
        public IEnumerator CloseSession(Action onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onSuccess?.Invoke();
                yield break;
            }

            ApiError failure = null;
            yield return api.CloseSession(CurrentRoom.room_code, () => { }, error => failure = error);

            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            CurrentSession = null;
            SessionChanged?.Invoke(null);
            onSuccess?.Invoke();
        }

        /// <summary>获取中继凭证。</summary>
        public IEnumerator GetRelayCredentials(Action<RelayCredentialsDto> onSuccess, Action<ApiError> onError)
        {
            return api.GetRelayCredentials(onSuccess, onError);
        }

        /// <summary>连接信令并等待房间状态下发。</summary>
        private IEnumerator ConnectSignal(string roomCode, Action<RoomDto> onSuccess, Action<ApiError> onError)
        {
            RoomDto pending = null;
            ApiError failure = null;

            // 临时订阅：拿到首个 room_state 即视为加入成功。
            Action<RoomStatePayload> onState = state =>
            {
                if (state == null || state.room_code != roomCode)
                    return;

                pending = BuildRoomFromState(state);
            };

            Action<SignalErrorPayload> onErrorEvent = err =>
            {
                if (err != null)
                    failure = new ApiError(err.code, err.message);
            };

            signal.RoomStateReceived += onState;
            signal.ErrorReceived += onErrorEvent;

            yield return signal.Connect(roomCode, api.AccessToken);

            // 等待 room_state 或错误，最多 5 秒。
            var deadline = Time.realtimeSinceStartup + 5f;
            while (pending == null && failure == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            signal.RoomStateReceived -= onState;
            signal.ErrorReceived -= onErrorEvent;

            if (failure != null)
            {
                onError?.Invoke(failure);
                yield break;
            }

            if (pending == null)
            {
                onError?.Invoke(new ApiError("TIMEOUT", "等待房间状态超时"));
                yield break;
            }

            CurrentRoom = pending;
            RoomChanged?.Invoke(pending);
            onSuccess?.Invoke(pending);
        }

        /// <summary>把信令侧的 room_state 转成 HTTP 侧的 RoomDto，统一对外类型。</summary>
        private static RoomDto BuildRoomFromState(RoomStatePayload state)
        {
            var members = new RoomMemberDto[state.members?.Length ?? 0];
            for (var i = 0; i < members.Length; i++)
            {
                var m = state.members[i];
                members[i] = new RoomMemberDto
                {
                    account_id = m.account_id,
                    display_name = m.display_name,
                    avatar_url = m.avatar_url,
                    is_host = m.is_host,
                };
            }

            return new RoomDto
            {
                room_code = state.room_code,
                host_account = state.host_account_id,
                members = members,
                max_players = members.Length,
                status = "waiting",
            };
        }

        // ---- 信令事件转发 ----

        private void HookSignalEvents()
        {
            signal.RoomStateReceived += OnRoomState;
            signal.MemberJoined += OnMemberJoined;
            signal.MemberLeft += OnMemberLeft;
            signal.HostChanged += OnHostChanged;
            signal.RoomClosed += OnRoomClosed;
            signal.GameSessionReceived += OnGameSessionReceived;
        }

        private void UnhookSignalEvents()
        {
            if (signal == null)
                return;

            signal.RoomStateReceived -= OnRoomState;
            signal.MemberJoined -= OnMemberJoined;
            signal.MemberLeft -= OnMemberLeft;
            signal.HostChanged -= OnHostChanged;
            signal.RoomClosed -= OnRoomClosed;
            signal.GameSessionReceived -= OnGameSessionReceived;
        }

        private void OnRoomState(RoomStatePayload state)
        {
            if (state == null)
                return;

            CurrentRoom = BuildRoomFromState(state);
            RoomChanged?.Invoke(CurrentRoom);
        }

        private void OnMemberJoined(MemberJoinedPayload payload)
        {
            if (payload == null)
                return;

            Debug.Log($"[NetworkSession] 玩家加入: {payload.display_name} (#{payload.account_id})");

            // 把新成员并入 CurrentRoom.members：房主据此遍历广播房主地址，
            // 否则后加入的成员不在列表里，收不到地址、连不上房主。
            if (CurrentRoom != null && CurrentRoom.members != null)
            {
                foreach (var m in CurrentRoom.members)
                {
                    if (m != null && m.account_id == payload.account_id)
                        return;
                }

                var merged = new RoomMemberDto[CurrentRoom.members.Length + 1];
                System.Array.Copy(CurrentRoom.members, merged, CurrentRoom.members.Length);
                merged[merged.Length - 1] = new RoomMemberDto
                {
                    account_id = payload.account_id,
                    display_name = payload.display_name,
                    avatar_url = payload.avatar_url,
                    is_host = false,
                };
                CurrentRoom.members = merged;
                RoomChanged?.Invoke(CurrentRoom);
            }
        }

        private void OnMemberLeft(MemberLeftPayload payload)
        {
            if (payload == null)
                return;

            Debug.Log($"[NetworkSession] 玩家离开: #{payload.account_id}");
        }

        private void OnHostChanged(HostChangedPayload payload)
        {
            if (payload == null)
                return;

            Debug.Log($"[NetworkSession] 房主迁移为: {payload.new_host_display_name} (#{payload.new_host_account_id})");

            // 游戏层面的权威接管（NetworkObject 所有权转移）由 NGO 侧完成，
            // 这里只更新房间数据，供 UI 显示新房主。
            if (CurrentRoom != null)
            {
                CurrentRoom.host_account = payload.new_host_account_id;
                RoomChanged?.Invoke(CurrentRoom);
            }
        }

        private void OnRoomClosed(RoomClosedPayload payload)
        {
            Debug.Log($"[NetworkSession] 房间已关闭: {payload?.reason}");

            CurrentRoom = null;
            RoomLeft?.Invoke();
        }

        private void OnGameSessionReceived(GameSessionPayload payload)
        {
            if (payload == null)
                return;

            // 信令侧与 HTTP 侧字段一致，转成统一对外类型，避免调用方区分两个来源。
            CurrentSession = new GameSessionDto
            {
                session_id = payload.session_id,
                room_code = payload.room_code,
                transport_mode = payload.transport_mode,
                host_account_id = payload.host_account_id,
                host_address = payload.host_address,
                host_port = payload.host_port,
                status = payload.status,
            };

            Debug.Log($"[NetworkSession] 游戏会话: {CurrentSession.session_id} ({CurrentSession.status}) " +
                      $"{CurrentSession.host_address}:{CurrentSession.host_port}");

            SessionChanged?.Invoke(CurrentSession);
        }
    }
}
