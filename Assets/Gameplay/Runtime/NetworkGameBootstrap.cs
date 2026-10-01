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
                return;

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

            networkManager.OnClientConnectedCallback += OnClientConnected;
            GameSession.SetLocalPlayerId(NetworkSession.Instance.Account != null
                ? NetworkSession.Instance.Account.account_id.ToString()
                : GameSession.LocalPlayerIdValue);

            if (IsRoomHostOrSole())
                StartAsHost();
            else
                WaitForHostAddress();

            StartCoroutine(SelfCheck());
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
        }

        private void WaitForHostAddress()
        {
            Debug.Log("[NetBootstrap] 作为成员，等待房主地址…");
            var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
            if (signal != null)
                signal.SignalReceived += OnRemoteSignal;
        }

        private void OnRemoteSignal(SignalForwardPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.data))
                return;

            if (!payload.data.StartsWith(connectSignalPrefix + ":"))
                return;

            receivedHostAddress = payload.data.Substring(connectSignalPrefix.Length + 1);
            Debug.Log($"[NetBootstrap] 收到房主地址 {receivedHostAddress}");

            var signal = NetworkSession.Instance != null ? NetworkSession.Instance.Signal : null;
            if (signal != null)
                signal.SignalReceived -= OnRemoteSignal;

            // 部署联机：主编辑器 host → 成员连入即马上 StartClient，避免等待竞技场生成。
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

        private string LocalAddress()
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }
            return "127.0.0.1";
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

        /// <summary>客户端连接成功 / 房主已有本地玩家的玩家生成入口。</summary>
        private void OnClientConnected(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer)
                return;

            // 阶段A：为每个连接生成一个归属它的玩家对象。
            SpawnPlayerFor(clientId);
        }

        /// <summary>为指定客户端生成一个玩家对象并归属给它。</summary>
        private void SpawnPlayerFor(ulong clientId)
        {
            if (playerPrefab == null)
            {
                // 回退：假定场景已有静态玩家（单机支撑）。联机时顶层交给外部。
                Debug.LogWarning("[NetBootstrap] 未配置玩家预制体，跳过动态生成。");
                return;
            }

            var instance = Instantiate(playerPrefab);
            instance.transform.position = ResolveSpawnPosition();
            var netObj = instance.GetComponent<NetworkObject>();
            if (netObj != null)
                netObj.SpawnAsPlayerObject(clientId);
            else
                Destroy(instance);
        }

        private Vector2 ResolveSpawnPosition()
        {
            if (towerRun != null && towerRun.HasRoomBounds)
                return towerRun.FloorCenter;
            return Vector2.zero;
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
