using Tower.Network;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// 战斗场景内的联机绑定。
    ///
    /// 只做场景内的事：隐藏单机静态玩家、通知协调器场景就绪。
    /// 连接建立与场景切换都在常驻层完成，本组件不参与。
    /// 单机模式（非联机会话）直接返回，不隐藏静态玩家。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleNetworkBinder : MonoBehaviour
    {
        private static BattleNetworkBinder instance;

        /// <summary>全局单例。单机时为 null 或未激活。</summary>
        public static BattleNetworkBinder Instance => instance;

        [Tooltip("相机跟随。联机时指向本端控制的玩家。")]
        [SerializeField] private CameraFollow2D cameraFollow;

        private GameObject staticPlayer;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void Start()
        {
            // 单机：不隐藏静态玩家，不通知协调器。
            if (!IsOnlineSession)
            {
                Debug.Log("[BattleNetworkBinder] 非联机会话，跳过联机绑定。");
                return;
            }

            if (cameraFollow == null)
                cameraFollow = FindObjectOfType<CameraFollow2D>();

            HideStaticPlayer();

            // 通知协调器：战斗场景已就绪。
            if (MultiplayerCoordinator.Instance != null)
                MultiplayerCoordinator.Instance.NotifyBattleSceneReady();
        }

        /// <summary>是否处于联机会话（已登录且在房间内）。</summary>
        public static bool IsOnlineSession =>
            NetworkSession.Instance != null && NetworkSession.Instance.CurrentRoom != null;

        /// <summary>
        /// 把相机跟随目标重定向到某个网络玩家。由本端 owner 玩家的
        /// <see cref="PlayerNetworkSync"/> 在 OnNetworkSpawn 时调用。
        /// </summary>
        public void AttachLocalCamera(Transform player)
        {
            if (cameraFollow == null)
                cameraFollow = FindObjectOfType<CameraFollow2D>();

            if (cameraFollow != null && player != null)
                cameraFollow.SetTarget(player);
        }

        /// <summary>
        /// 隐藏场景静态玩家。联机模式下所有角色都由 Host 动态生成，
        /// 静态玩家若继续存在会与网络玩家并存，出现「不归网络支配的幽灵角色」。
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
    }
}
