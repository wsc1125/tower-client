using System;
using System.Collections;
using System.Collections.Generic;
using Tower.Network;
using Unity.Netcode;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>联机生命周期状态。</summary>
    public enum MultiplayerState
    {
        /// <summary>未联机（单机或未进房）。</summary>
        Idle,

        /// <summary>正在准备 Host / Client。</summary>
        Preparing,

        /// <summary>正在建立 NGO 连接。</summary>
        Connecting,

        /// <summary>Host 等待所有房间成员连入。</summary>
        WaitingForPlayers,

        /// <summary>Host 正在切换战斗场景。</summary>
        LoadingBattleScene,

        /// <summary>已进入战斗。</summary>
        InGame,

        /// <summary>正在断开。</summary>
        Disconnecting,

        /// <summary>失败，需玩家重试或退出房间。</summary>
        Failed,
    }

    /// <summary>
    /// 联机生命周期协调器（常驻，挂在 NetworkManager 上）。
    ///
    /// 核心原则：**先建立 NGO 连接，再由 Host 统一切换场景**。
    /// 旧实现是「两端各自切场景，再尝试交换地址」，会因场景加载速度差异
    /// 导致连接消息丢失、两端各玩各的。这里把顺序倒过来，从架构上消除竞态。
    ///
    /// 单机模式完全不经过本组件：State 保持 Idle，NetworkManager 不启动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerCoordinator : MonoBehaviour
    {
        [Tooltip("房主监听端口（UDP）。")]
        [SerializeField] private int hostPort = 7777;

        [Tooltip("等待所有成员连接的超时（秒）。")]
        [SerializeField] private float connectTimeoutSeconds = 30f;

        [Tooltip("战斗场景名，需已在 Build Settings 登记。")]
        [SerializeField] private string battleSceneName = "BattleScene";

        private static MultiplayerCoordinator instance;

        /// <summary>全局单例。未挂载时返回 null。</summary>
        public static MultiplayerCoordinator Instance => instance;

        private NetworkManager networkManager;
        private MultiplayerState state = MultiplayerState.Idle;

        /// <summary>已连接的客户端 ID 集合（Host 侧维护）。</summary>
        private readonly HashSet<ulong> connectedClients = new HashSet<ulong>();

        /// <summary>clientId → accountId 映射，供玩家生成与身份绑定使用。</summary>
        private readonly Dictionary<ulong, long> clientAccounts = new Dictionary<ulong, long>();

        public MultiplayerState State => state;

        /// <summary>状态变化。</summary>
        public event Action<MultiplayerState> StateChanged;

        /// <summary>失败，参数为可展示的原因。</summary>
        public event Action<string> Failed;

        /// <summary>本端是否为 Host。</summary>
        public bool IsHost => networkManager != null && networkManager.IsHost;

        /// <summary>已连接的成员数（含 Host 自己）。</summary>
        public int ConnectedMemberCount => connectedClients.Count;

        /// <summary>本局预期成员数（房间成员数）。</summary>
        public int ExpectedMemberCount
        {
            get
            {
                var room = NetworkSession.Instance != null ? NetworkSession.Instance.CurrentRoom : null;
                return room?.members?.Length ?? 0;
            }
        }

        /// <summary>取某客户端对应的账号 ID。未登记时返回 0。</summary>
        public long AccountIdOf(ulong clientId)
        {
            return clientAccounts.TryGetValue(clientId, out var accountId) ? accountId : 0L;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            networkManager = GetComponent<NetworkManager>();
            if (networkManager == null)
                networkManager = FindObjectOfType<NetworkManager>();

            if (networkManager == null)
            {
                Debug.LogError("[MultiplayerCoordinator] 未找到 NetworkManager，联机不可用。");
                return;
            }

            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            // NetworkManager 启用了 ConnectionApproval，必须注册审批回调：
            // NGO 在收到连接请求时直接调用该回调（无 null 检查），
            // 未注册会抛异常导致所有成员连接被拒，房主只能等到超时。
            networkManager.ConnectionApprovalCallback = HandleConnectionApproval;

            // 订阅信令透传：Network 程序集不引用 Gameplay，
            // 房主地址由 NetworkSession 以事件形式转发过来。
            //
            // 注意：本组件挂在 NetworkManager 上，与 NetworkSession 是两个独立的
            // 根对象，Unity 不保证两者 Awake 的先后顺序。若本组件先 Awake，
            // NetworkSession.Instance 仍为 null，直接订阅会被静默跳过 ——
            // 房主将永远收不到成员的地址请求。因此采用「立即尝试 + Ready 兜底」。
            TryHookSession();

            // 订阅 UI 层的「出发」请求：UI 层无法引用本程序集，
            // 通过静态事件桥接进来，由本组件真正启动 NGO。
            MultiplayerDepartBridge.DepartRequested += OnDepartRequested;
        }

        /// <summary>
        /// 尝试订阅会话事件。会话已就绪则立即订阅；否则挂到静态 Ready 事件上等待。
        ///
        /// 双保险消除 Awake 顺序依赖：
        ///   1. 会话已就绪 → 直接订阅；
        ///   2. 会话未就绪 → 等它 Awake 后回调再订阅。
        /// </summary>
        private void TryHookSession()
        {
            var session = NetworkSession.Instance;
            if (session != null)
            {
                HookSession(session);
                return;
            }

            NetworkSession.ReadyStatic += OnSessionReady;
        }

        /// <summary>会话就绪回调：退订静态事件并完成订阅。</summary>
        private void OnSessionReady()
        {
            NetworkSession.ReadyStatic -= OnSessionReady;

            var session = NetworkSession.Instance;
            if (session != null)
                HookSession(session);
        }

        /// <summary>挂载会话事件。</summary>
        private void HookSession(NetworkSession session)
        {
            session.SignalForwarded += OnSignalReceived;
            session.HostLeft += OnHostLeft;
            session.HostChanged += OnHostChanged;
            Debug.Log("[MultiplayerCoordinator] 已订阅会话信令事件。");
        }

        /// <summary>卸载会话事件。</summary>
        private void UnhookSession(NetworkSession session)
        {
            session.SignalForwarded -= OnSignalReceived;
            session.HostLeft -= OnHostLeft;
            session.HostChanged -= OnHostChanged;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                networkManager.ConnectionApprovalCallback = null;
            }

            var session = NetworkSession.Instance;
            if (session != null)
                UnhookSession(session);

            // 会话尚未就绪时可能还挂在静态事件上，一并退订，避免悬挂引用。
            NetworkSession.ReadyStatic -= OnSessionReady;

            MultiplayerDepartBridge.DepartRequested -= OnDepartRequested;
        }

        /// <summary>
        /// UI 层点「出发」：房主启动 Host 流程，成员启动 Client 流程。
        ///
        /// 这是联机真正开始的入口 —— 此前 StartHostFlow / StartClientFlow
        /// 从未被任何地方调用，导致 NGO 从未启动、各端各玩各的。
        /// </summary>
        private void OnDepartRequested(bool isHost)
        {
            if (state != MultiplayerState.Idle && state != MultiplayerState.Failed)
            {
                Debug.LogWarning($"[MultiplayerCoordinator] 当前状态 {state}，忽略出发请求。");
                return;
            }

            StartCoroutine(isHost ? StartHostFlow() : StartClientFlow());
        }

        // ---- 房主流程 ----

        /// <summary>
        /// 房主出发流程：启动 Host → 广播地址 → **广播开始信号唤醒成员** → 等成员连入 → 切场景。
        ///
        /// 顺序很关键，且与直觉相反：
        ///   1. 必须先发 start_game —— 成员只有收到它才会启动 Client 去连房主；
        ///   2. 再等成员连入 —— 若先等再发，房主永远等不到成员（成员没被唤醒），死锁。
        ///
        /// 地址获取不依赖房主遍历成员列表（列表可能不全）：
        /// 成员被唤醒后主动发 tower_addr_req 向房主索取地址，房主收到立即回发。
        /// </summary>
        public IEnumerator StartHostFlow()
        {
            if (state != MultiplayerState.Idle && state != MultiplayerState.Failed)
            {
                Debug.LogWarning($"[MultiplayerCoordinator] 当前状态 {state}，忽略重复的房主启动。");
                yield break;
            }

            var session = NetworkSession.Instance;
            if (session == null || session.CurrentRoom == null)
            {
                Fail("未进入房间，无法开始联机");
                yield break;
            }

            SetState(MultiplayerState.Preparing);

            // 1. 启动 Host。
            var strategy = ConnectionStrategies.For("lan");
            if (!strategy.ConfigureHost(networkManager, hostPort))
            {
                Fail("配置房主监听失败");
                yield break;
            }

            if (!networkManager.StartHost())
            {
                Fail("启动房主失败，端口可能被占用");
                yield break;
            }

            SetState(MultiplayerState.Connecting);

            // 2. 广播连接地址（尽力而为，成员列表可能不全，成员端会主动补请求）。
            var address = LocalAddress();
            BroadcastAddress(session, address, hostPort);

            Debug.Log($"[MultiplayerCoordinator] 房主已就绪，地址 {address}:{hostPort}");

            // 3. 广播开始信号，唤醒成员启动 Client。
            //    必须在等待之前发 —— 否则成员不会被唤醒，房主永远等不到人。
            session.Signal?.SendStartGame();

            // 4. 等待所有房间成员连入。
            SetState(MultiplayerState.WaitingForPlayers);
            yield return WaitForAllMembersConnected();

            if (state == MultiplayerState.Failed)
                yield break;

            // 5. 统一切换战斗场景。
            LoadBattleScene();
        }

        /// <summary>
        /// 等待所有房间成员连入 NGO。超时则失败。
        ///
        /// 每帧动态重读 <see cref="ExpectedMemberCount"/>：房主点出发时成员列表
        /// 可能尚未更新（member_joined 有延迟），若在循环外取一次快照，
        /// 会按错误的人数等待（例如实际 2 人却只等 1 人，或反之）。
        /// </summary>
        private IEnumerator WaitForAllMembersConnected()
        {
            var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                var expected = ExpectedMemberCount;

                if (expected > 0 && ConnectedMemberCount >= expected)
                {
                    Debug.Log($"[MultiplayerCoordinator] 全部 {expected} 名成员已连接。");
                    yield break;
                }

                yield return null;
            }

            Fail($"等待成员连接超时（{ConnectedMemberCount}/{ExpectedMemberCount}）");
        }

        // ---- 成员流程 ----

        /// <summary>
        /// 成员出发流程：请求房主地址 → 启动 Client → 等待房主切场景。
        ///
        /// 地址获取采用「主动请求 + 被动接收」双保险：
        ///   1. 先向房主发一次地址请求（房主收到立即回发）；
        ///   2. 同时等待房主主动广播的地址。
        /// 任一到达即可，避免「房主广播时成员列表不全」导致收不到地址。
        /// </summary>
        public IEnumerator StartClientFlow()
        {
            if (state != MultiplayerState.Idle && state != MultiplayerState.Failed)
            {
                Debug.LogWarning($"[MultiplayerCoordinator] 当前状态 {state}，忽略重复的成员启动。");
                yield break;
            }

            var session = NetworkSession.Instance;
            if (session == null || session.CurrentRoom == null)
            {
                Fail("未进入房间，无法加入联机");
                yield break;
            }

            SetState(MultiplayerState.Preparing);

            // 1. 主动向房主请求地址（房主收到立即回发）。
            RequestHostAddress(session);

            // 2. 等房主地址（主动请求的响应，或房主主动广播的地址）。
            var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
            var nextRetry = Time.realtimeSinceStartup + 2f;
            while (string.IsNullOrEmpty(hostAddress) && Time.realtimeSinceStartup < deadline)
            {
                // 每 2 秒重发一次请求，防止首次请求在房主就绪前丢失。
                if (Time.realtimeSinceStartup >= nextRetry)
                {
                    RequestHostAddress(session);
                    nextRetry = Time.realtimeSinceStartup + 2f;
                }

                yield return null;
            }

            if (string.IsNullOrEmpty(hostAddress))
            {
                Fail("等待房主地址超时");
                yield break;
            }

            // 3. 启动 Client。
            var strategy = ConnectionStrategies.For("lan");
            if (!strategy.ConfigureClient(networkManager, hostAddress, hostPort))
            {
                Fail("配置连接目标失败");
                yield break;
            }

            // 把本机账号 ID 写进连接 payload，供房主在连接审批时登记
            // clientId → accountId 映射（否则房主侧无法把玩家对象对应到账号）。
            var account = session.Account;
            if (account != null)
                networkManager.NetworkConfig.ConnectionData = BitConverter.GetBytes(account.account_id);

            if (!networkManager.StartClient())
            {
                Fail("启动客户端失败");
                yield break;
            }

            SetState(MultiplayerState.Connecting);
            Debug.Log($"[MultiplayerCoordinator] 正在连接房主 {hostAddress}:{hostPort}");

            // 4. 等待连接成功或超时。场景切换由 Host 发起，这里只等。
            deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (networkManager.IsConnectedClient)
                {
                    Debug.Log("[MultiplayerCoordinator] 已连接房主，等待房主切换场景…");
                    yield break;
                }

                yield return null;
            }

            Fail("连接房主超时");
        }

        /// <summary>向房主发送地址请求。房主账号从当前房间取。</summary>
        private static void RequestHostAddress(NetworkSession session)
        {
            var room = session.CurrentRoom;
            if (room == null || session.Signal == null)
                return;

            var hostId = room.host_account;
            if (hostId == 0)
                return;

            Debug.Log($"[MultiplayerCoordinator] 请求房主地址 → 账号 #{hostId}");
            session.Signal.SendSignal(hostId, AddressRequest);
        }

        // ---- 地址交换 ----

        /// <summary>房主地址信令前缀，与成员端约定一致。</summary>
        private const string AddressPrefix = "tower_addr:";

        /// <summary>成员向房主请求地址的信令内容。</summary>
        private const string AddressRequest = "tower_addr_req";

        private string hostAddress;

        /// <summary>
        /// 收到信令转发的数据。由 NetworkSession 转发进来。
        ///
        /// 处理两类：
        ///   1. 房主地址（tower_addr:ip:port）—— 成员据此连接；
        ///   2. 地址请求（tower_addr_req）—— 房主收到后立即回发地址。
        ///
        /// 地址请求机制解决「成员列表不全导致地址漏发」：
        /// 成员不依赖房主主动广播，而是主动索取，房主收到就回。
        /// </summary>
        public void OnSignalReceived(long fromAccountId, string data)
        {
            if (string.IsNullOrEmpty(data))
                return;

            // 房主收到成员的地址请求：立即回发本机地址。
            if (data == AddressRequest)
            {
                Debug.Log($"[MultiplayerCoordinator] 收到地址请求，来自账号 #{fromAccountId}，IsHost={IsHost}");
                if (IsHost)
                    SendAddressTo(fromAccountId);

                return;
            }

            if (!data.StartsWith(AddressPrefix))
                return;

            var parts = data.Substring(AddressPrefix.Length).Split(':');
            if (parts.Length < 2)
                return;

            hostAddress = parts[0];
            if (int.TryParse(parts[1], out var port))
                hostPort = port;

            Debug.Log($"[MultiplayerCoordinator] 收到房主地址 {hostAddress}:{hostPort}");
        }

        private static string BuildAddressPayload(string address, int port)
        {
            return $"{AddressPrefix}{address}:{port}";
        }

        /// <summary>向指定账号发送本机地址。房主调用。</summary>
        private void SendAddressTo(long targetAccountId)
        {
            var session = NetworkSession.Instance;
            if (session?.Signal == null || targetAccountId == 0)
                return;

            var payload = BuildAddressPayload(LocalAddress(), hostPort);
            Debug.Log($"[MultiplayerCoordinator] 回发地址 {payload} → 账号 #{targetAccountId}");
            session.Signal.SendSignal(targetAccountId, payload);
        }

        /// <summary>
        /// 把房主地址逐个发给房间内其他成员。
        ///
        /// 信令的 signal 消息要求 target_account_id 非 0（服务端会拒绝 0），
        /// 因此不能一次广播，只能遍历成员逐个发送。
        ///
        /// 注意：这只是「尽力而为」的主动推送。成员列表可能不全（刚加入的成员
        /// 尚未出现在本地列表），因此成员端还有「主动请求地址」兜底。
        /// </summary>
        private static void BroadcastAddress(NetworkSession session, string address, int port)
        {
            var room = session.CurrentRoom;
            var selfId = session.Account != null ? session.Account.account_id : 0;
            if (room?.members == null || session.Signal == null)
                return;

            var payload = BuildAddressPayload(address, port);
            foreach (var m in room.members)
            {
                if (m == null || m.account_id == selfId)
                    continue;

                session.Signal.SendSignal(m.account_id, payload);
            }
        }

        // ---- 场景切换 ----

        /// <summary>
        /// 由 Host 统一切换战斗场景。成员端不得调用。
        /// </summary>
        public void LoadBattleScene()
        {
            if (!IsHost)
            {
                Debug.LogWarning("[MultiplayerCoordinator] 只有房主可以切换战斗场景。");
                return;
            }

            SetState(MultiplayerState.LoadingBattleScene);

            if (!networkManager.NetworkConfig.EnableSceneManagement)
            {
                Fail("NetworkManager 未启用场景管理，无法同步切换场景");
                return;
            }

            Debug.Log($"[MultiplayerCoordinator] 房主切换场景：{battleSceneName}");
            networkManager.SceneManager.LoadScene(battleSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        /// <summary>战斗场景就绪后由 BattleNetworkBinder 调用。</summary>
        public void NotifyBattleSceneReady()
        {
            SetState(MultiplayerState.InGame);
        }

        // ---- 连接回调 ----

        /// <summary>
        /// 连接审批：批准所有成员连入，并为其生成玩家对象。
        ///
        /// NetworkManager 启用了 ConnectionApproval，NGO 收到连接请求时会直接
        /// 调用本回调（无 null 检查），因此必须注册，否则成员连接全部失败。
        /// 本阶段不做鉴权：成员身份由信令侧的房间校验保证。
        ///
        /// 同时从连接 payload 里取出成员账号 ID 并登记 —— 否则 clientAccounts
        /// 只有房主自己，AccountIdOf 永远返回 0，玩家与账号的对应关系在 NGO 层缺失。
        /// </summary>
        private void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = networkManager.NetworkConfig.PlayerPrefab != null;

            // 客户端在 StartClient 前把 accountId 写进连接 payload（见 StartClientFlow）。
            var accountId = ParseAccountId(request.Payload);
            if (accountId != 0)
                clientAccounts[request.ClientNetworkId] = accountId;

            Debug.Log($"[MultiplayerCoordinator] 批准客户端连接 {request.ClientNetworkId}（账号 #{accountId}）");
        }

        /// <summary>从连接 payload 解析账号 ID。payload 为 8 字节小端 long，缺失时返回 0。</summary>
        private static long ParseAccountId(byte[] payload)
        {
            if (payload == null || payload.Length < sizeof(long))
                return 0;

            return BitConverter.ToInt64(payload, 0);
        }

        private void HandleClientConnected(ulong clientId)
        {
            connectedClients.Add(clientId);

            // 记录 clientId → accountId：Host 自己用本机账号，成员由连接审批写入。
            if (networkManager.IsServer && clientId == networkManager.LocalClientId)
            {
                var account = NetworkSession.Instance != null ? NetworkSession.Instance.Account : null;
                if (account != null)
                    clientAccounts[clientId] = account.account_id;
            }

            Debug.Log($"[MultiplayerCoordinator] 客户端已连接 {clientId}（{ConnectedMemberCount}/{ExpectedMemberCount}）");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            connectedClients.Remove(clientId);
            clientAccounts.Remove(clientId);

            Debug.Log($"[MultiplayerCoordinator] 客户端已断开 {clientId}（{ConnectedMemberCount}/{ExpectedMemberCount}）");

            // 成员端与房主断开：进入失败状态，不静默留在战斗里。
            if (!networkManager.IsServer && state == MultiplayerState.InGame)
                Fail("与房主的连接已断开");
        }

        /// <summary>
        /// 房主掉线：NGO 1.5 不支持 Host 迁移，Host 是网络拓扑中心，
        /// 掉线即整个会话崩溃。因此明确策略为「房主掉线即解散」——
        /// 所有成员收到通知后退出战斗回主菜单，不做权威接管。
        ///
        /// 由 <see cref="NetworkSession"/> 在信令层检测到房主离开时调用。
        /// </summary>
        public void OnHostLeft()
        {
            if (state == MultiplayerState.Idle)
                return;

            Debug.LogWarning("[MultiplayerCoordinator] 房主已离开，联机会话解散。");

            // 成员端：断开与房主的 NGO 连接。
            if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
                networkManager.Shutdown();

            connectedClients.Clear();
            clientAccounts.Clear();
            hostAddress = null;

            SetState(MultiplayerState.Failed);
            Failed?.Invoke("房主已离开，房间已解散");
        }

        /// <summary>登记某客户端对应的账号（连接审批通过后调用）。</summary>
        public void RegisterClientAccount(ulong clientId, long accountId)
        {
            clientAccounts[clientId] = accountId;
        }

        /// <summary>
        /// 房主迁移：服务端把房主移交给剩余成员中最早加入的一位。
        ///
        /// 与 <see cref="OnHostLeft"/> 的区别：
        ///   - OnHostLeft 是「房主掉线」——NGO Host 是网络拓扑中心，无法迁移，会话解散；
        ///   - 本方法是「房主主动退出」——服务端已完成房主移交，本端只需刷新状态。
        ///
        /// 注意：NGO 1.5 的 Host 无法在运行时迁移，因此本方法只做「状态刷新」，
        /// 真正的权威接管（NetworkObject 所有权转移）需新房主重新 StartHost，
        /// 属于阶段 4 的 4.4 范畴。
        /// </summary>
        private void OnHostChanged(long newHostAccountId)
        {
            var selfId = NetworkSession.Instance != null && NetworkSession.Instance.Account != null
                ? NetworkSession.Instance.Account.account_id
                : 0L;

            if (newHostAccountId == selfId)
            {
                Debug.Log("[MultiplayerCoordinator] 本端已成为新房主（服务端已移交）。");
            }
            else
            {
                Debug.Log($"[MultiplayerCoordinator] 房主已变更为账号 #{newHostAccountId}。");
            }
        }

        // ---- 关闭 ----

        /// <summary>关闭联机，回到 Idle。单机不受影响。</summary>
        public void Shutdown()
        {
            if (networkManager != null && networkManager.IsListening)
            {
                SetState(MultiplayerState.Disconnecting);
                networkManager.Shutdown();
            }

            connectedClients.Clear();
            clientAccounts.Clear();
            hostAddress = null;
            SetState(MultiplayerState.Idle);
        }

        // ---- 内部 ----

        private void SetState(MultiplayerState next)
        {
            if (state == next)
                return;

            state = next;
            Debug.Log($"[MultiplayerCoordinator] 状态 → {next}");
            StateChanged?.Invoke(next);
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[MultiplayerCoordinator] 失败：{reason}");

            // 释放 NGO 会话与监听端口。
            // 不释放的话，重试时 StartHost 会因 7777 端口仍被占用而失败
            // （实测报「启动房主失败，端口可能被占用」），玩家只能重启客户端。
            if (networkManager != null && networkManager.IsListening)
                networkManager.Shutdown();

            connectedClients.Clear();
            clientAccounts.Clear();
            hostAddress = null;

            SetState(MultiplayerState.Failed);
            Failed?.Invoke(reason);

            // 通知 UI 层恢复出发按钮、隐藏加载页，让玩家可以重试。
            MultiplayerDepartBridge.NotifyDepartFailed(reason);
        }

        /// <summary>
        /// 取本机对外可用的 IPv4 地址。
        /// 跳过 WSL / Hyper-V / VMware / VirtualBox 等虚拟网卡与链路本地地址，
        /// 否则本机双开时可能广播出虚拟网卡地址，成员连不上。
        /// </summary>
        private string LocalAddress()
        {
            string fallback = null;
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;

                var name = nic.Name ?? string.Empty;
                var desc = nic.Description ?? string.Empty;
                var isVirtual = name.IndexOf("WSL", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0
                                || desc.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;

                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("169.254."))
                        continue;

                    if (!isVirtual)
                        return ip;
                    if (fallback == null)
                        fallback = ip;
                }
            }

            return fallback ?? "127.0.0.1";
        }
    }
}
