using System;
using Unity.Netcode;
using UnityEngine;

namespace Tower.Netcode
{
    public enum BattleFinishReason : byte
    {
        None,
        Extracted,
        TeamWiped,
        HostClosed,
    }

    /// <summary>
    /// 战斗场景的联机会话状态（场景内 NetworkObject，随场景同步生成）。
    ///
    /// 承载两类必须由主机权威决定、且所有端一致的数据：
    ///   1. 本局随机种子 —— 各端据此生成完全相同的房间与敌人布局；
    ///   2. 本局是否已结束 —— 房主死亡 / 撤退时置位，所有端据此统一切回主菜单。
    ///
    /// 单机模式下本组件不参与任何逻辑（NGO 未启动，IsSpawned 恒为 false），
    /// 由 <see cref="TowerRunManager"/> 走本地分支，行为与改造前一致。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleSessionState : NetworkBehaviour
    {
        private static BattleSessionState instance;

        /// <summary>全局单例。单机或未生成时为 null。</summary>
        public static BattleSessionState Instance => instance;

        /// <summary>本局随机种子。主机写入，客户端只读。</summary>
        private readonly NetworkVariable<int> seed =
            new NetworkVariable<int>(0, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>本局结束原因。None 表示仍在进行。</summary>
        private readonly NetworkVariable<BattleFinishReason> finishReason =
            new NetworkVariable<BattleFinishReason>(BattleFinishReason.None,
                writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// 当前层数。主机换层时写入，客户端据此同步换层。
        ///
        /// 不同步层数会导致联机时各端层数不一致：主机走到传送点换层后，
        /// 客户端仍停留在旧层，看到的房间与敌人完全不同。
        /// <summary>
        /// 当前层数。主机换层时写入，客户端据此同步换层。
        ///
        /// 不同步层数会导致联机时各端层数不一致：主机走到传送点换层后，
        /// 客户端仍停留在旧层，看到的房间与敌人完全不同。
        /// </summary>
        private readonly NetworkVariable<int> currentFloor =
            new NetworkVariable<int>(1, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// 当前存活玩家数。主机维护，用于「全员死亡」判定。
        ///
        /// 初始值为 -1，表示「尚未初始化」—— 与「0 个存活玩家」区分开。
        /// 否则进场景瞬间玩家对象尚未 Spawn 完成时会被误判为全员死亡。
        /// </summary>
        private readonly NetworkVariable<int> alivePlayerCount =
            new NetworkVariable<int>(-1, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>本局随机种子。未就绪时返回 0。</summary>
        public int Seed => seed.Value;

        /// <summary>本局是否已结束。</summary>
        public bool RunFinished => finishReason.Value != BattleFinishReason.None;

        /// <summary>本局结束原因。</summary>
        public BattleFinishReason FinishReason => finishReason.Value;

        /// <summary>当前层数。</summary>
        public int CurrentFloor => currentFloor.Value;

        /// <summary>种子就绪（主机已写入）。</summary>
        public event Action<int> SeedReady;

        /// <summary>本局结束。</summary>
        public event Action RunFinishedChanged;

        /// <summary>层数变化（主机换层后广播）。参数为新层数。</summary>
        public event Action<int> FloorChanged;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            instance = this;

            seed.OnValueChanged += HandleSeedChanged;
            finishReason.OnValueChanged += HandleFinishReasonChanged;
            currentFloor.OnValueChanged += HandleFloorChanged;

            // 后加入的端：字段可能已有值，补发一次。
            if (seed.Value != 0)
                SeedReady?.Invoke(seed.Value);

            if (RunFinished)
                RunFinishedChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            seed.OnValueChanged -= HandleSeedChanged;
            finishReason.OnValueChanged -= HandleFinishReasonChanged;
            currentFloor.OnValueChanged -= HandleFloorChanged;

            if (instance == this)
                instance = null;

            base.OnNetworkDespawn();
        }

        /// <summary>主机写入本局种子。仅主机可调用。</summary>
        public void SetSeed(int value)
        {
            if (!IsServer)
                return;

            seed.Value = value;
        }

        /// <summary>主机标记本局结束。仅首次调用生效。</summary>
        public void MarkRunFinished(BattleFinishReason reason = BattleFinishReason.HostClosed)
        {
            if (!IsServer || RunFinished || reason == BattleFinishReason.None)
                return;

            finishReason.Value = reason;
        }

        /// <summary>主机写入当前层数。仅主机可调用，客户端据此同步换层。</summary>
        public void SetCurrentFloor(int floor)
        {
            if (!IsServer)
                return;

            currentFloor.Value = floor;
        }

        /// <summary>当前存活玩家数。尚未初始化时返回 -1。</summary>
        public int AlivePlayerCount => alivePlayerCount.Value;

        /// <summary>
        /// 主机更新存活玩家数。
        ///
        /// 只有「曾经有过存活玩家（count 曾 > 0），之后归零」才标记本局结束。
        /// 进场景瞬间玩家对象尚未 Spawn 完成时 count 为 0，此时**不能**判定全员死亡 ——
        /// 否则会立刻广播本局结束，表现为「刚进战斗就退回主菜单」。
        /// </summary>
        public void SetAlivePlayerCount(int count)
        {
            if (!IsServer)
                return;

            var clamped = Mathf.Max(0, count);

            // 尚未初始化（-1）且当前为 0：玩家还没就绪，不判定死亡，也不写入。
            if (alivePlayerCount.Value < 0 && clamped == 0)
                return;

            var previous = alivePlayerCount.Value;
            var reason = ResolveFinishReason(previous, clamped);
            alivePlayerCount.Value = clamped;

            if (reason != BattleFinishReason.None)
                MarkRunFinished(reason);
        }

        private void HandleSeedChanged(int previous, int current)
        {
            if (current != 0)
                SeedReady?.Invoke(current);
        }

        public static BattleFinishReason ResolveFinishReason(int previousAliveCount, int currentAliveCount)
        {
            return previousAliveCount > 0 && currentAliveCount == 0
                ? BattleFinishReason.TeamWiped
                : BattleFinishReason.None;
        }

        private void HandleFinishReasonChanged(BattleFinishReason previous, BattleFinishReason current)
        {
            if (current != BattleFinishReason.None)
                RunFinishedChanged?.Invoke();
        }

        private void HandleFloorChanged(int previous, int current)
        {
            FloorChanged?.Invoke(current);
        }
    }
}
