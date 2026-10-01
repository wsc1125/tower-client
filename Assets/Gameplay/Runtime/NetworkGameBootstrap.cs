using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Tower.Network;

namespace Tower.Netcode
{
    /// <summary>
    /// 联机战斗的连接引导（阶段A：多人同场 + 位置同步）。
    ///
    /// 挂在 BattleScene，仅在「联机会话有效」（已登录且在房间内）时启用 {@link #networkManager}。
    /// 职责：
    ///   1. 房主（Host / 房间成员最早加入者）→ <see cref="NetworkManager.StartHost"/>，并把自己的
    ///      监听 IP:Port 通过现有信令广播给房间其他成员；
    ///   2. 普通成员 → 从信令收到房主地址后 <see cref="NetworkManager.StartClient"/>；
    ///   3. 玩家生成：为每个已连接客户端 Spawn 一个归属该客户端的玩家对象（owner），
    ///      各端经 <see cref="PlayerNetworkSync"/> 各自控制并同步位置。
    ///
    /// 单机流程不经过本组件（联机会话为空时直接跳过），完全不影响离线爬塔。
    /// </summary>
    public sealed class NetworkGameBootstrap : MonoBehaviour
    {
        [Header("C10 联机接线")]
        [Tooltip("房主监听端口（UDP）。客户端来信令取到房主 IP 后连这个端口。")]
        [SerializeField] private int hostListenPort = 7777;

        [Tooltip("场景内 NetworkManager 对象（需已挂 UnityTransport）。留空自动在场景中查找。")]
        [SerializeField] private NetworkManager networkManager;

        [Tooltip("玩家预制体（需已挂 NetworkObject + PlayerNetworkSync）。留空回退到场景中的 Character_Base。")]
        [SerializeField] private NetworkObject playerPrefab;

        [Tooltip("相机跟随。联机时指向本端控制的玩家。")]
        [SerializeField] private CameraFollow2D cameraFollow;

        [Tooltip("爬塔流程。联机时把出生点玩家映射必要时替换为网络玩家。")]
        [SerializeField] private TowerRunManager towerRun;

        [Tooltip("房主地址信令的消息前缀，与房间成员端一致即可。")]
        [SerializeField] private string connectSignalPrefix = "tower_connect";

        private bool started;
        private string receivedHostAddress;

        /// <summary>联机时被隐藏的场景静态玩家（单机挂载的角色）。隐藏后不再参与移动/战斗，避免与网络动态玩家冲突。</summary>
        private GameObject staticPlayer;

        private static NetworkGameBootstrap _instance;
        public static NetworkGameBootstrap Instance => _instance;

        /// <summary>本端是否为房主（host），或未联网（单机）时沿用 null。</summary>
        public bool IsHost => networkManager != null && networkManager.IsHost;
        public bool IsOnlineActive => networkManager != null && networkManager.IsListening;
        public static bool IsOnlineSession => NetworkSession.Instance != null && NetworkSession.Instance.CurrentRoom != null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            // 单机：不启动网络，直接返回。
            if (!IsOnlineSession)
            {
                Debug.Log("[NetBootstrap] 非联机会话（未登录或未进房），跳过网络启动。");
                return;
            }

            if (networkManager == null)
                networkManager = FindObjectOfType<NetworkManager>();

            if (networkManager == null)
            {
                Debug.LogWarning("[NetBootstrap] 联机模式下未找到 NetworkManager，跳过。");
                return;
            }

            if (cameraFollow == null)
                cameraFollow = FindObjectOfType<CameraFollow2D>();
            if (towerRun == null)
                towerRun = FindObjectOfType<TowerRunManager>();

            HideStaticPlayer();

            // 玩家对象由 NGO 的 NetworkConfig.PlayerPrefab 自动生成；这里的 playerPrefab 字段
            // 仅作 Inspector 对照参考，若二者不一致会提示（避免误以为手动 SpawnPlayerFor 兜底）。
            if (playerPrefab != null && networkManager.NetworkConfig.PlayerPrefab != playerPrefab.gameObject)
                Debug.LogWarning("[NetBootstrap] playerPrefab 字段与 NetworkManager.PlayerPrefab 不一致。" +
                                 "实际生效的是 NetworkManager.PlayerPrefab。");

            networkManager.OnClientConnectedCallback += OnClientConnected;
            GameSession.SetLocalPlayerId(NetworkSession.Instance.Account != null
                ? NetworkSession.Instance.Account.account_id.ToString()
                : GameSession.LocalPlayerIdValue);

            // 房间状态可能晚于场景加载就绪（信令 room_state 异步下发）。
            // 先尝试一次；若房间还没到，订阅 RoomChanged 等它到齐再启动，避免误判房主。
            if (NetworkSession.Instance.CurrentRoom != null)
                TryStartNetwork();
            else
            {
                Debug.Log("[NetBootstrap] 房间状态尚未就绪，等待 RoomChanged…");
                NetworkSession.Instance.RoomChanged += OnRoomChanged;
            }

            StartCoroutine(SelfCheck());
        }

        private void OnRoomChanged(RoomDto room)
        {
            if (room == null || started)
                return;

            NetworkSession.Instance.RoomChanged -= OnRoomChanged;
            TryStartNetwork();
        }

        /// <summary>房间状态就绪后，按房主/成员身份启动网络。</summary>
        private void TryStartNetwork()
        {
            if (started)
                return;

            var room = NetworkSession.Instance.CurrentRoom;
            var me = NetworkSession.Instance.Account != null ? NetworkSession.Instance.Account.account_id : 0L;
            Debug.Log($"[NetBootstrap] 会话就绪：本端账号={me}，房间={room?.room_code}，房主账号={room?.host_account}，" +
                      $"成员数={room?.members?.Length ?? 0}，判定为房主={IsRoomHostOrSole()}");

            if (IsRoomHostOrSole())
                StartAsHost();
            else
                WaitForHostAddress();
        }

        /// <summary>房主判定：房间状态里的 host_account == 本端账号。房间不可得时先当房主（单成员房间）。</summary>
        private bool IsRoomHostOrSole()
        {
            var room = NetworkSession.Instance != null ? NetworkSession.Instance.CurrentRoom : null;
            if (room == null)
                return true;

            var me = NetworkSession.Instance.Account != null ? NetworkSession.Instance.Account.account_id : 0L;
            return room.host_account == me;
        }

        private void StartAsHost()
        {
            if (started)
                return;
            started = true;

            ConfigureHostTransport();
            Debug.Log($"[NetBootstrap] 作为房主启动，监听端口 {hostListenPort}");
            networkManager.StartHost();
            BroadcastHostAddress();

            // 后加入的成员在 host 首次广播时还不在房间里，收不到地址。
            // 订阅成员加入事件，每有新成员进房就补发一次房主地址。
            var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
            if (signal != null)
            {
                signal.MemberJoined += OnMemberJoinedBroadcast;
                // 成员进场景后会主动请求地址，房主需应答。
                signal.SignalReceived += OnRemoteSignal;
            }
        }

        /// <summary>有成员加入房间时，补发房主地址（解决后加入者收不到首次广播的时序问题）。</summary>
        private void OnMemberJoinedBroadcast(MemberJoinedPayload payload)
        {
            if (payload == null || payload.account_id == SelfAccountId())
                return;

            // 直接发给刚加入的成员：此刻 CurrentRoom.members 可能尚未包含它，
            // 走 BroadcastHostAddress 的成员遍历会漏发。
            var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
            signal?.SendSignal(payload.account_id, connectSignalPrefix + ":" + LocalAddress());
        }

        private void OnDestroy()
        {
            var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
            if (signal != null)
            {
                signal.MemberJoined -= OnMemberJoinedBroadcast;
                signal.SignalReceived -= OnRemoteSignal;
            }
        }

        private void WaitForHostAddress()
        {
            Debug.Log("[NetBootstrap] 作为成员，等待房主地址…");
            var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
            if (signal == null)
                return;

            signal.SignalReceived += OnRemoteSignal;

            // 主动向房主请求地址：房主可能在本端进场景前就已广播过，被动等待会错过。
            var hostId = NetworkSession.Instance.CurrentRoom != null
                ? NetworkSession.Instance.CurrentRoom.host_account
                : 0L;
            if (hostId != 0L && hostId != SelfAccountId())
            {
                Debug.Log($"[NetBootstrap] 向房主 #{hostId} 请求地址");
                signal.SendSignal(hostId, connectSignalPrefix + "_req");
            }
        }

        private void OnRemoteSignal(SignalForwardPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.data))
                return;

            // 房主收到成员的地址请求 → 回复自己的地址。
            if (payload.data == connectSignalPrefix + "_req")
            {
                if (IsHost)
                {
                    var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
                    signal?.SendSignal(payload.from_account_id, connectSignalPrefix + ":" + LocalAddress());
                }
                return;
            }

            if (!payload.data.StartsWith(connectSignalPrefix + ":"))
                return;

            receivedHostAddress = payload.data.Substring(connectSignalPrefix.Length + 1);
            Debug.Log($"[NetBootstrap] 收到房主地址 {receivedHostAddress}");

            var sig = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
            if (sig != null)
                sig.SignalReceived -= OnRemoteSignal;

            StartAsClient(receivedHostAddress);
        }

        private void StartAsClient(string hostAddress)
        {
            if (started)
                return;
            started = true;

            var utp = networkManager.GetComponent<UnityTransport>();
            if (utp != null)
                utp.SetConnectionData(hostAddress, (ushort)hostListenPort, "0.0.0.0");

            Debug.Log($"[NetBootstrap] 连接房主 {hostAddress}:{hostListenPort}");
            networkManager.StartClient();
        }

        private void ConfigureHostTransport()
        {
            var utp = networkManager.GetComponent<UnityTransport>();
            if (utp != null)
                utp.SetConnectionData("0.0.0.0", (ushort)hostListenPort, "127.0.0.1");
        }

        /// <summary>把房主监听地址广播给房间所有成员（含未来加入者）。</summary>
        private void BroadcastHostAddress()
        {
            var hostAddress = LocalAddress();
            var payload = connectSignalPrefix + ":" + hostAddress;
            foreach (var member in RoomMemberIds())
            {
                if (member == SelfAccountId())
                    continue;
                var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
                signal?.SendSignal(member, payload);
            }
        }

        /// <summary>
        /// 取本机对外可用的 IPv4 地址，用于广播给房间成员。
        /// 优先真实物理网卡（WLAN / 以太网），跳过 WSL / Hyper-V / VMware / VirtualBox 等虚拟网卡，
        /// 否则本机双端测试时可能广播出虚拟网卡地址（如 172.31.x.x），客户端连不上。
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
                var isVirtual = name.IndexOf("WSL", System.StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("Hyper-V", System.StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("VMware", System.StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("VirtualBox", System.StringComparison.OrdinalIgnoreCase) >= 0
                                || desc.IndexOf("Virtual", System.StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;

                    var ip = addr.Address.ToString();
                    // 跳过链路本地地址（169.254.x.x），它不可路由。
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

        private long[] RoomMemberIds()
        {
            var room = NetworkSession.Instance != null ? NetworkSession.Instance.CurrentRoom : null;
            if (room == null || room.members == null)
                return new long[0];

            var ids = new long[room.members.Length];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = room.members[i].account_id;
            return ids;
        }

        private long SelfAccountId()
        {
            return NetworkSession.Instance != null && NetworkSession.Instance.Account != null
                ? NetworkSession.Instance.Account.account_id
                : 0L;
        }

        /// <summary>
        /// 客户端连接成功（含 host 自己在 StartHost 时的连接）。玩家对象由 NGO 的
        /// <c>PlayerPrefab</c> 机制自动生成，这里只把它移到出生点（房间中心）。
        /// 之所以不手动 Instantiate+SpawnAsPlayerObject：NetworkConfig.PlayerPrefab 已配置，
        /// NGO 会在 Server 端自动为每个连接 Spawn owner 玩家，手动再生成会导致 host 出现双玩家。
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer)
                return;

            StartCoroutine(PlacePlayerAfterSpawn(clientId));
        }

        /// <summary>
        /// 等到 clientId 对应玩家对象生成后，把它摆到客厅 / 房间出生点。
        /// 自动生成的玩家对象初始位置是 prefab 原点，不会落在房间几何中心，需要摆一次。
        /// </summary>
        private System.Collections.IEnumerator PlacePlayerAfterSpawn(ulong clientId)
        {
            GameObject born = null;
            for (var i = 0; i < 5; i++)
            {
                yield return new WaitForEndOfFrame();
                var netObj = networkManager.SpawnManager.GetPlayerNetworkObject(clientId);
                if (netObj != null)
                {
                    born = netObj.gameObject;
                    break;
                }
            }

            if (born == null)
            {
                Debug.LogWarning($"[NetBootstrap] 客户端 {clientId} 的玩家对象未在 5 帧内生成。");
                yield break;
            }

            // 服务器端直接改位置；客户端由 PlayerNetworkSync 的 owner 上报覆盖，
            // 因此这里只需在 host（服务器）上摆一次即可全局生效。
            var target = ResolveSpawnPosition();
            var body = born.GetComponent<Rigidbody2D>();
            if (body != null)
                body.position = target;
            born.transform.position = new Vector3(target.x, target.y, born.transform.position.z);
        }

        private Vector2 ResolveSpawnPosition()
        {
            if (towerRun != null && towerRun.HasRoomBounds)
                return towerRun.FloorCenter;
            return Vector2.zero;
        }

        /// <summary>
        /// 隐藏场景静态玩家。联机模式下所有角色都由 host 动态生成，静态玩家若继续存在会
        /// 与网络玩家并存，出现「不归网络支配的幽灵角色」。隐藏它即可让联机世界只剩网络玩家。
        /// 单机路径不会走到这里（Start 已按 IsOnlineSession 提前 return）。
        /// </summary>
        private void HideStaticPlayer()
        {
            if (staticPlayer != null)
                return;

            var gameplay = GameObject.Find("Gameplay");
            staticPlayer = gameplay != null
                ? gameplay.transform.Find("Character_Base")?.gameObject
                : null;

            if (staticPlayer != null)
                staticPlayer.SetActive(false);
        }

        /// <summary>
        /// 把相机跟随目标重定向到某个网络玩家。由本端 owner 玩家的 <see cref="PlayerNetworkSync"/>
        /// 在 <c>OnNetworkSpawn</c> 时调用，使每端相机跟着自己控制的角色走。
        /// 单机不受影响（<see cref="Instance"/> 只在联机 Bootstrap 激活时非空）。
        /// </summary>
        public void AttachLocalCamera(Transform player)
        {
            if (cameraFollow == null)
                cameraFollow = FindObjectOfType<CameraFollow2D>();
            if (cameraFollow != null && player != null)
                cameraFollow.SetTarget(player);
        }

        private System.Collections.IEnumerator SelfCheck()
        {
            const float timeout = 15f;
            var waited = 0f;
            while (waited < timeout)
            {
                if (networkManager != null && networkManager.IsListening)
                    yield break;
                if (receivedHostAddress != null)
                    break;
                waited += Time.deltaTime;
                yield return null;
            }

            if (networkManager == null || !networkManager.IsListening)
                Debug.LogWarning($"[NetBootstrap] {timeout:F0}s 内未建立网络连接，请检查房主是否在线。");
        }
    }
}
