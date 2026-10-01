using UnityEngine;
using Unity.Netcode;

namespace Tower.Netcode
{
    /// <summary>
    /// 玩家对象的主机权威位置同步组件（阶段A：多人同场 + 位置同步）。
    ///
    /// 职责：
    ///   1. 区分「本地控制玩家」与「远端玩家」。
    ///      本地（owner）仍照常读输入用 Rigidbody2D.MovePosition 物理移动；
    ///      远端禁用本端移动输入，位置改由主机广播的权威字段驱动，并设 isKinematic 避免物理反馈干扰。
    ///   2. 权威字段 <see cref="syncedPosition"/> 只允许主机写入：
    ///      - 主机自己的玩家：本地移动后直接把位置写入字段。
    ///      - 远端客户端玩家：客户端通过 ServerRpc 上报，主机校验（仅 owner）后写入字段，再广播给所有端。
    /// </summary>
    public sealed class PlayerNetworkSync : NetworkBehaviour
    {
        [Tooltip("本端控制的移动组件。远端会被自动禁用，避免重复读输入。")]
        [SerializeField] private PlayerMovement2D movement;

        private readonly NetworkVariable<Vector2> syncedPosition =
            new NetworkVariable<Vector2>(writePerm: NetworkVariableWritePermission.Server);

        private Rigidbody2D body;
        private bool applyRemotePosition;

        /// <summary>是否本端实际控制的玩家。</summary>
        public bool IsLocalControl => IsSpawned && IsOwner;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            body = GetComponent<Rigidbody2D>();
            if (movement == null)
                movement = GetComponent<PlayerMovement2D>();

            if (IsOwner)
            {
                // 本地玩家：照常读取输入、物理移动。
                if (movement != null)
                    movement.enabled = true;
                if (body != null)
                    body.isKinematic = false;

                // 让相机跟着自己控制的角色走（联机 Bootstrap 激活时才有 Instance，单机为 null 无副作用）。
                if (Tower.Netcode.NetworkGameBootstrap.Instance != null)
                    Tower.Netcode.NetworkGameBootstrap.Instance.AttachLocalCamera(transform);
            }
            else
            {
                // 远端玩家：不读输入；位置由主机广播驱动；物理体改为运动学，只跟随权威位置。
                if (movement != null)
                    movement.enabled = false;
                if (body != null)
                    body.isKinematic = true;

                transform.position = syncedPosition.Value;
                applyRemotePosition = true;
            }
        }

        /// <summary>
        /// 远端客户端把本机玩家位置上报主机。仅允许该对象的 owner 调用，
        /// 主机校验通过后写入权威字段，由 NGO 广播给所有客户端。
        /// </summary>
        [ServerRpc]
        private void SyncPositionServerRpc(Vector2 pos, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            syncedPosition.Value = pos;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
            {
                // 本地玩家：移动已由 PlayerMovement2D 在物理阶段完成，这里把位置写入权威字段。
                // 主机玩家：本地直接写字段（ServerRpc 在主机上即时执行）；
                // 客户端玩家：经 ServerRpc 上报主机后再广播。
                SyncPositionServerRpc(transform.position);
            }
            else if (applyRemotePosition && body != null)
            {
                // 远端玩家：让物理体跟随主机权威位置，保持碰撞体正确参与判定。
                body.MovePosition(syncedPosition.Value);
            }
        }
    }
}
