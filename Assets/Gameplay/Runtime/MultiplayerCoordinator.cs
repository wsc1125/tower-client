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
    /// 联机生命周期协调器（常驻）。
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
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        // ---- 房主流程 ----

        /// <summary>
        /// 房主出发流程：创建会话 → 启动 Host → 写入地址 → 等待成员 → 切场景。
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

            // 1. 创建本局会话。
            GameSessionDto created = null;
            ApiError createError = null;
            yield return session.CreateSession(s => created = s, e => createError = e);

            if (createError != null || created == null)
            {
                Fail($"创建游戏会话失败：{createError?.Message ?? "未知错误"}");
                yield break;
            }

            // 2. 启动 Host。
            var strategy = ConnectionStrategies.For(created.transport_mode);
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

            // 3. 写入连接地址，成员据此连接。
            var address = LocalAddress();
            ApiError readyError = null;
            yield return session.MarkSessionReady(address, hostPort, null, e => readyError = e);

            if (readyError != null)
            {
                Fail($"写入连接地址失败：{readyError.Message}");
                yield break;
            }

            Debug.Log($"[MultiplayerCoordinator] 房主已就绪，地址 {address}:{hostPort}");

            // 4. 等待所有房间成员连入。
            SetState(MultiplayerState.WaitingForPlayers);
            yield return WaitForAllMembersConnected();

            if (state == MultiplayerState.Failed)
                yield break;

            // 5. 统一切换战斗场景。
            LoadBattleScene();
        }

        /// <summary>等待所有房间成员连入 NGO。超时则失败。</summary>
        private IEnumerator WaitForAllMembersConnected()
        {
            var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (ConnectedMemberCount >= ExpectedMemberCount && ExpectedMemberCount > 0)
                {
                    Debug.Log($"[MultiplayerCoordinator] 全部 {ExpectedMemberCount} 名成员已连接。");
                    yield break;
                }

                yield return null;
            }

            Fail($"等待成员连接超时（{ConnectedMemberCount}/{ExpectedMemberCount}）");
        }

        // ---- 成员流程 ----

        /// <summary>
        /// 成员出发流程：等待会话就绪 → 启动 Client → 等待 Host 切场景。
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

            // 1. 取会话信息：先看已有推送，没有则主动查询。
            var info = session.CurrentSession;
            if (info == null || string.IsNullOrEmpty(info.host_address))
            {
                GameSessionDto fetched = null;
                ApiError fetchError = null;
                yield return session.FetchSession(s => fetched = s, e => fetchError = e);

                if (fetchError != null || fetched == null)
                {
                    Fail($"获取游戏会话失败：{fetchError?.Message ?? "会话不存在"}");
                    yield break;
                }

                info = fetched;
            }

            if (string.IsNullOrEmpty(info.host_address))
            {
                Fail("房主尚未就绪，请稍后重试");
                yield break;
            }

            // 2. 启动 Client。
            var strategy = ConnectionStrategies.For(info.transport_mode);
            if (!strategy.ConfigureClient(networkManager, info.host_address, info.host_port))
            {
                Fail("配置连接目标失败");
                yield break;
            }

            if (!networkManager.StartClient())
            {
                Fail("启动客户端失败");
                yield break;
            }

            SetState(MultiplayerState.Connecting);
            Debug.Log($"[MultiplayerCoordinator] 正在连接房主 {info.host_address}:{info.host_port}");

            // 3. 等待连接成功或超时。场景切换由 Host 发起，这里只等。
            var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
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

        /// <summary>登记某客户端对应的账号（连接审批通过后调用）。</summary>
        public void RegisterClientAccount(ulong clientId, long accountId)
        {
            clientAccounts[clientId] = accountId;
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
            SetState(MultiplayerState.Failed);
            Failed?.Invoke(reason);
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
