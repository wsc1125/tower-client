using Unity.Netcode;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// Host 侧玩家生成与出生点分配。
    ///
    /// 玩家对象由 NGO 的 NetworkConfig.PlayerPrefab 自动生成（每个连接一个 owner 玩家），
    /// 本组件只负责把它们摆到本层房间的出生点。
    /// 之所以不手动 Instantiate + SpawnAsPlayerObject：PlayerPrefab 已配置，
    /// 手动再生成会导致 Host 出现双玩家。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerSpawner : MonoBehaviour
    {
        [Tooltip("爬塔流程，用于取本层房间出生点。留空自动查找。")]
        [SerializeField] private TowerRunManager towerRun;

        private NetworkManager networkManager;

        private void Awake()
        {
            networkManager = FindObjectOfType<NetworkManager>();
            if (towerRun == null)
                towerRun = FindObjectOfType<TowerRunManager>();
        }

        private void OnEnable()
        {
            if (networkManager != null)
                networkManager.OnClientConnectedCallback += HandleClientConnected;
        }

        private void OnDisable()
        {
            if (networkManager != null)
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer)
                return;

            StartCoroutine(PlacePlayerAfterSpawn(clientId));
        }

        /// <summary>
        /// 等玩家对象生成后摆到出生点。
        /// 自动生成的玩家初始位置是 prefab 原点，不会落在房间几何中心。
        /// </summary>
        private System.Collections.IEnumerator PlacePlayerAfterSpawn(ulong clientId)
        {
            GameObject born = null;
            for (var i = 0; i < 10; i++)
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
                Debug.LogWarning($"[NetworkPlayerSpawner] 客户端 {clientId} 的玩家对象未在 10 帧内生成。");
                yield break;
            }

            // 服务器端直接改位置；客户端由 PlayerNetworkSync 的 owner 上报覆盖，
            // 因此这里只需在 Host（服务器）上摆一次即可全局生效。
            var target = ResolveSpawnPosition();
            var body = born.GetComponent<Rigidbody2D>();
            if (body != null)
                body.position = target;

            born.transform.position = new Vector3(target.x, target.y, born.transform.position.z);

            Debug.Log($"[NetworkPlayerSpawner] 客户端 {clientId} 的玩家已摆到 {target}");
        }

        private Vector2 ResolveSpawnPosition()
        {
            if (towerRun != null && towerRun.HasRoomBounds)
                return towerRun.FloorCenter;

            return Vector2.zero;
        }
    }
}
