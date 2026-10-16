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

        /// <summary>
        /// 会话就绪（Awake 完成、信令事件已挂载）。
        ///
        /// 用**静态**事件而非实例事件：订阅方（如 MultiplayerCoordinator）可能在
        /// 本组件 Awake 之前就执行，此时 Instance 仍为 null，无法访问实例事件。
        /// 静态事件让订阅方在「会话尚未就绪」时也能可靠挂载，消除 Awake 顺序依赖。
        /// </summary>
        public static event Action ReadyStatic;

        /// <summary>会话是否已就绪。</summary>
        public bool IsReady { get; private set; }

        [Tooltip("服务端地址。本地开发用 localhost；局域网测试填主机内网 IP。")]
        [SerializeField] private string serverHost = "localhost";

        [Tooltip("服务端端口。")]
        [SerializeField] private int serverPort = 8080;

        private NetworkConfig config;
        private ApiClient api;
        private SignalClient signal;
        private AssetSyncService assetSync;

        /// <summary>资产云同步服务（与本组件同 GameObject，跨场景常驻）。</summary>
        public AssetSyncService Assets => assetSync;

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

        /// <summary>登录成功。</summary>
        public event Action<AccountDto> LoggedIn;

        /// <summary>登出。</summary>
        public event Action LoggedOut;

        /// <summary>房间状态变化（加入 / 成员进出 / 准备状态）。</summary>
        public event Action<RoomDto> RoomChanged;

        /// <summary>已离开房间。</summary>
        public event Action RoomLeft;

        /// <summary>
        /// 最近一次 game_starting 携带的传输模式（"lan" / "relay"）。
        ///
        /// 由服务端按「全员是否同公网出口」判定。null 表示尚未收到
        /// （旧服务端无此字段），调用方按局域网直连处理。
        /// UI 层若需感知开始游戏，直接订阅 <see cref="SignalClient.GameStarting"/>。
        /// </summary>
        public string LastTransportMode { get; private set; }

        /// <summary>
        /// 房主迁移。参数为新房主账号 ID。
        ///
        /// 与 <see cref="RoomChanged"/> 分开：房主迁移需要 UI 做额外提示
        /// （「你已成为房主」/「房主已变更」），而普通成员进出不需要。
        /// </summary>
        public event Action<long> HostChanged;

        /// <summary>
        /// 房主离开了房间（且本端不是房主）。
        ///
        /// 与 <see cref="HostChanged"/> 区分：服务端在房主离开后会把房主移交给
        /// 最早加入的成员并广播 host_changed，但 NGO 的 Host 是网络拓扑中心，
        /// 无法迁移 —— 因此战斗中房主离开即会话解散，由 Gameplay 侧据此退出。
        /// </summary>
        public event Action HostLeft;

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

            assetSync = gameObject.AddComponent<AssetSyncService>();
            assetSync.Initialize(api);

            HookSignalEvents();

            // 信令保活：房间挂着不动时，客户端必须定期发心跳，
            // 否则会被服务端读超时（60 秒）断开，全员被踢出房间。
            StartCoroutine(SignalHeartbeat());

            // 就绪：通知在 Awake 之前就等待的订阅方（如 MultiplayerCoordinator）。
            IsReady = true;
            ReadyStatic?.Invoke();
        }

        /// <summary>
        /// 信令消息泵：每帧驱动 SignalClient 把收到的消息派发成事件。
        ///
        /// 必须挂在本组件（DontDestroyOnLoad 常驻）上 —— 旧实现把泵放在
        /// 发起连接的协程尾部，出发进战斗后发起方随场景销毁、泵停转，
        /// 战斗中收不到 room_closed / member_left，退出后残留幽灵房间。
        /// </summary>
        private void Update()
        {
            signal?.Pump();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            IsReady = false;
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

        /// <summary>登出：断开信令、尽力上传脏资产、清空登录态、把本机玩家身份还原为单机。</summary>
        public IEnumerator Logout()
        {
            if (signal != null && signal.IsConnected)
                yield return signal.Disconnect();

            // 云存档：登出前尽力把脏资产传上去；失败时 pending 标记已持久化，下次登录自动补传。
            if (assetSync != null)
                yield return assetSync.FlushBeforeLogout();

            CurrentRoom = null;
            LastTransportMode = null;
            api.Logout();

            // 还原为单机身份 —— 否则后续单机流程会继续读写联机账号的数据。
            GameSession.SetLocalPlayerId(null);

            if (assetSync != null)
                assetSync.OnLoggedOut();

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

            // 云存档：绑定账号后立即拉取服务端快照（Syncing 状态下出发门禁保持拦截）。
            if (assetSync != null)
                assetSync.OnAccountBound(account.account_id.ToString());

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
                LastTransportMode = null;
                onSuccess?.Invoke();
                yield break;
            }

            var code = CurrentRoom.room_code;

            // 先断信令：服务端在信令断开时即移除成员（并广播 member_left、
            // 必要时迁移房主/关闭房间），这是该服务端的权威退出路径。
            var signalDisconnected = false;
            if (signal.IsConnected)
            {
                yield return signal.Disconnect();
                signalDisconnected = true;
            }

            ApiError failure = null;
            yield return api.LeaveRoom(code, _ => { }, error => failure = error);

            // 信令断开时服务端已移除本端成员，此时 HTTP 离开必然返回
            // 403（你不在该房间内）/ 404（房间不存在）—— 这是预期结果而非错误，
            // 上报出去会让玩家以为退出失败（退出/解散报错的根源）。
            // HTTP 调用保留作为兜底：信令断线处理失败（如落库异常）时仍能移除成员。
            if (failure != null && !signalDisconnected)
                onError?.Invoke(failure);

            CurrentRoom = null;
            LastTransportMode = null;
            RoomLeft?.Invoke();

            if (failure == null || signalDisconnected)
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

            // 顺序与容错同 LeaveRoom：先断信令（服务端据此关房/迁移房主），
            // 再调 HTTP 关闭兜底。房内还有成员时服务端会先迁移房主，
            // 此时 HTTP 关闭返回 403（只有房主可以执行此操作）—— 属预期结果。
            var signalDisconnected = false;
            if (signal.IsConnected)
            {
                yield return signal.Disconnect();
                signalDisconnected = true;
            }

            ApiError failure = null;
            yield return api.CloseRoom(code, () => { }, error => failure = error);

            if (failure != null && !signalDisconnected)
                onError?.Invoke(failure);

            CurrentRoom = null;
            LastTransportMode = null;
            RoomLeft?.Invoke();

            if (failure == null || signalDisconnected)
                onSuccess?.Invoke();
        }

        /// <summary>刷新房间列表。</summary>
        public IEnumerator ListRooms(Action<RoomListEntryDto[]> onSuccess, Action<ApiError> onError)
        {
            return api.ListRooms(onSuccess, onError);
        }

        /// <summary>
        /// 退出当前房间：房主关闭房间，成员离开房间。
        ///
        /// 供「死亡 / 撤退回主菜单」等场景调用 —— 这些路径只切场景不退出房间，
        /// 会让服务端仍认为玩家在房间内，导致下次建房/进房报「你已在其他房间中」，
        /// 以及 UI 显示「仍在房间中」的幽灵房间问题。
        /// </summary>
        public IEnumerator LeaveOrCloseRoom(Action onDone)
        {
            if (CurrentRoom == null)
            {
                onDone?.Invoke();
                yield break;
            }

            var isHost = Account != null && CurrentRoom.host_account == Account.account_id;

            if (isHost)
                yield return CloseRoom(null, null);
            else
                yield return LeaveRoom(null, null);

            onDone?.Invoke();
        }

        /// <summary>获取中继凭证。</summary>
        public IEnumerator GetRelayCredentials(Action<RelayCredentialsDto> onSuccess, Action<ApiError> onError)
        {
            return api.GetRelayCredentials(onSuccess, onError);
        }

        /// <summary>
        /// 提交本局撤离战利品清单。未登录时静默跳过（单机不调用）。
        /// </summary>
        public IEnumerator SubmitExtraction(ExtractionRequestDto body,
            Action onSuccess, Action<ApiError> onError)
        {
            if (api == null || !api.IsLoggedIn)
            {
                onSuccess?.Invoke();
                yield break;
            }

            yield return api.SubmitExtraction(body, onSuccess, onError);
        }

        /// <summary>
        /// 标记本端准备状态。服务端会广播完整房间状态，各端据此刷新「已准备」显示。
        /// 房主不需要准备（房主点出发即开始），因此只由成员调用。
        /// </summary>
        public void SetReady(bool ready)
        {
            signal?.SendReady(ready);
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

            // Connect 建立连接后立即返回（消息泵由 Update 驱动），
            // 因此这里的后续逻辑在「连接期间」就会执行，而不是像旧实现那样
            // 挂起到断线后才执行 —— 后者会在断线时把旧房间复活成 CurrentRoom。
            yield return signal.Connect(roomCode, api.AccessToken);

            // 握手失败（超时 / 被拒）：上报真实原因，而不是等 room_state 超时。
            if (!string.IsNullOrEmpty(signal.ConnectError))
            {
                signal.RoomStateReceived -= onState;
                signal.ErrorReceived -= onErrorEvent;

                onError?.Invoke(new ApiError("CONNECT_FAILED", signal.ConnectError));
                yield break;
            }

            // 等待 room_state 或错误，最多 5 秒。
            var deadline = Time.realtimeSinceStartup + 5f;
            while (pending == null && failure == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            signal.RoomStateReceived -= onState;
            signal.ErrorReceived -= onErrorEvent;

            if (failure != null)
            {
                yield return signal.Disconnect();
                onError?.Invoke(failure);
                yield break;
            }

            if (pending == null)
            {
                yield return signal.Disconnect();
                onError?.Invoke(new ApiError("TIMEOUT", "等待房间状态超时"));
                yield break;
            }

            CurrentRoom = pending;
            LastTransportMode = null;
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
                    ready = m.ready,
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

        /// <summary>
        /// 信令保活心跳：每 pingIntervalSeconds 秒发一次应用层 ping。
        ///
        /// 服务端读超时 60 秒（pongWait）。虽然服务端也会主动发协议层 ping，
        /// 但客户端主动保活能同时刷新 NAT / 防火墙的空闲连接映射，
        /// 避免家庭网络下「房间放着不动，几分钟后全员掉线」。
        /// SendPing 内部已做 IsConnected 检查，未连接时调用是空操作。
        /// </summary>
        private IEnumerator SignalHeartbeat()
        {
            var interval = new WaitForSeconds(config.pingIntervalSeconds);

            while (true)
            {
                yield return interval;
                signal?.SendPing();
            }
        }

        private void HookSignalEvents()
        {
            signal.RoomStateReceived += OnRoomState;
            signal.MemberJoined += OnMemberJoined;
            signal.MemberLeft += OnMemberLeft;
            signal.HostChanged += OnHostChanged;
            signal.RoomClosed += OnRoomClosed;
            signal.Disconnected += OnSignalDisconnected;
            signal.SignalReceived += OnSignalForwarded;
            signal.GameStarting += OnGameStarting;
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
            signal.Disconnected -= OnSignalDisconnected;
            signal.SignalReceived -= OnSignalForwarded;
            signal.GameStarting -= OnGameStarting;
        }

        /// <summary>收到开始游戏广播：缓存服务端判定的传输模式。</summary>
        private void OnGameStarting(GameStartingPayload payload)
        {
            LastTransportMode = payload?.transport_mode;
        }

        /// <summary>
        /// 房主发起开始游戏：先清空旧的传输模式，
        /// 等服务端下发的最新 game_starting 广播回来（含 transport_mode）再启动 NGO。
        /// </summary>
        public void RequestStartGame()
        {
            LastTransportMode = null;
            signal?.SendStartGame();
        }

        /// <summary>
        /// 构造中继 WebSocket 完整地址（含房间码与令牌）。
        /// 未在房间内或未登录时返回 null。
        /// </summary>
        public string BuildRelayUrl()
        {
            if (config == null || CurrentRoom == null || api == null)
                return null;

            return $"{config.RelayUrl}?room_code={Uri.EscapeDataString(CurrentRoom.room_code)}" +
                   $"&token={Uri.EscapeDataString(api.AccessToken)}";
        }

        /// <summary>
        /// 收到转发的信令数据（来自房间内其他玩家）。
        ///
        /// 只做透传，不解析内容：Network 程序集不引用 Gameplay，
        /// 由 Gameplay 侧的 MultiplayerCoordinator 订阅本事件解析房主地址。
        /// </summary>
        public event Action<long, string> SignalForwarded;

        /// <summary>
        /// 收到转发的信令数据：透传给 Gameplay 侧的订阅者。
        ///
        /// 用事件转发而非让协调器直接订阅 SignalClient：
        /// 协调器在 Gameplay 程序集，SignalClient 在 Network 程序集，
        /// 由 NetworkSession 做这层桥接，避免两个程序集互相引用。
        /// </summary>
        private void OnSignalForwarded(SignalForwardPayload payload)
        {
            if (payload == null)
                return;

            SignalForwarded?.Invoke(payload.from_account_id, payload.data);
        }

        /// <summary>
        /// 信令连接断开：清空房间状态。
        ///
        /// 这是「幽灵房间」的兜底防线：连接已断，服务端那边成员已被移除，
        /// 本地若继续保留 CurrentRoom，UI 会显示「仍在房间中」，
        /// 且下次建房/进房会被服务端以「你已在其他房间中」拒绝。
        /// </summary>
        private void OnSignalDisconnected(string reason)
        {
            LastTransportMode = null;
            if (CurrentRoom == null)
                return;

            Debug.Log($"[NetworkSession] 信令断开（{reason}），清空房间状态");

            CurrentRoom = null;
            RoomLeft?.Invoke();
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

            // 离开的是房主，且本端不是房主：NGO 的 Host 无法迁移，会话解散。
            // 必须在移除成员前判断，否则 host_account 已被服务端改写。
            var wasHost = CurrentRoom != null && CurrentRoom.host_account == payload.account_id;
            var selfIsHost = Account != null && CurrentRoom != null && CurrentRoom.host_account == Account.account_id;

            // 从 CurrentRoom.members 移除该成员并通知 UI。
            // 只打日志不更新列表的话，别人离开后本地仍显示旧成员（幽灵成员）。
            if (CurrentRoom == null || CurrentRoom.members == null)
                return;

            var kept = new System.Collections.Generic.List<RoomMemberDto>(CurrentRoom.members.Length);
            foreach (var m in CurrentRoom.members)
            {
                if (m != null && m.account_id != payload.account_id)
                    kept.Add(m);
            }

            if (kept.Count == CurrentRoom.members.Length)
                return;

            CurrentRoom.members = kept.ToArray();
            RoomChanged?.Invoke(CurrentRoom);

            if (wasHost && !selfIsHost)
                HostLeft?.Invoke();
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

            HostChanged?.Invoke(payload.new_host_account_id);
        }

        private void OnRoomClosed(RoomClosedPayload payload)
        {
            Debug.Log($"[NetworkSession] 房间已关闭: {payload?.reason}");

            CurrentRoom = null;
            LastTransportMode = null;
            RoomLeft?.Invoke();
        }
    }
}
