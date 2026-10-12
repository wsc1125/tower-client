using Unity.Netcode;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// 敌人网络同步：把敌人的权威状态（位置 / 朝向 / 存活 / 行为状态）从主机广播给客户端。
    ///
    /// 设计取舍：敌人 AI 只在主机跑（<see cref="EnemyBrain2D"/> 等行为脚本在客户端被禁用），
    /// 客户端只做表现 —— 跟随主机广播的位置、朝向与状态。
    /// 这样：
    ///   1. 各端看到的敌人完全一致（同一份权威状态），不会出现「各打各的怪」；
    ///   2. 客户端不需要重跑 AI，省算力，也不会因浮点差异导致位置漂移。
    ///
    /// 关键：客户端必须**继续驱动视觉**（<see cref="EnemyVisual2D.Step"/>），
    /// 否则敌人会永远停在 idle 第一帧 —— 看不到攻击动画、移动动画与死亡动画。
    /// 因此这里把主机的行为状态广播过来，客户端用同一套视觉组件复现动画。
    ///
    /// 单机模式下敌人 prefab 上没有 NetworkObject，本组件不会被 NGO 驱动，
    /// 因此对单机零影响。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyNetworkSync : NetworkBehaviour
    {
        [Tooltip("位置插值速度。越大越跟手，越小越平滑。")]
        [SerializeField] private float positionLerpSpeed = 15f;

        private readonly NetworkVariable<Vector2> syncedPosition =
            new NetworkVariable<Vector2>(
                Vector2.zero,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> syncedAlive =
            new NetworkVariable<bool>(
                true,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        /// <summary>主机广播的行为状态（映射 <see cref="EnemyState"/>）。客户端据此驱动动画。</summary>
        private readonly NetworkVariable<byte> syncedState =
            new NetworkVariable<byte>(
                (byte)EnemyState.Patrol,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        /// <summary>主机广播的水平朝向（true = 朝左）。客户端据此翻转贴图。</summary>
        private readonly NetworkVariable<bool> syncedFacingLeft =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private Rigidbody2D body;
        private EnemyVisual2D visual;
        private BossVisual2D bossVisual;
        private EnemyBrain2D brain;
        private SwarmChaser2D swarm;
        private BossBrain2D boss;
        private Vector2 lastReportedPosition;
        private byte lastReportedState;
        private bool lastReportedFacingLeft;










        /// <summary>
        /// 权威存活状态。供本机玩家武器索敌跳过已同步死亡的敌人 ——
        /// 客户端本地 <c>health.IsDead</c> 恒为 false，必须读这个同步值。
        /// 未 Spawn（单机）时恒为 true，不影响单机。
        /// </summary>
        public bool IsAlive => !IsSpawned || syncedAlive.Value;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            visual = GetComponentInChildren<EnemyVisual2D>();
            bossVisual = GetComponentInChildren<BossVisual2D>();
            brain = GetComponent<EnemyBrain2D>();
            swarm = GetComponent<SwarmChaser2D>();
            boss = GetComponent<BossBrain2D>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // 主机：把生成位置作为初始权威值，避免客户端第一帧闪到原点。
                syncedPosition.Value = transform.position;
                lastReportedPosition = transform.position;
                lastReportedState = (byte)ResolveLocalState();
                lastReportedFacingLeft = ResolveFacingLeft();
                syncedState.Value = lastReportedState;
                syncedFacingLeft.Value = lastReportedFacingLeft;
            }
            else
            {
                // 客户端：禁用 AI 与物理，只跟随主机广播的位置与状态。
                DisableLocalBehaviours();
                transform.position = syncedPosition.Value;
                ApplyFacing(syncedFacingLeft.Value);
            }

            syncedPosition.OnValueChanged += HandlePositionChanged;
            syncedAlive.OnValueChanged += HandleAliveChanged;
            syncedState.OnValueChanged += HandleStateChanged;
            syncedFacingLeft.OnValueChanged += HandleFacingChanged;
        }

        public override void OnNetworkDespawn()
        {
            syncedPosition.OnValueChanged -= HandlePositionChanged;
            syncedAlive.OnValueChanged -= HandleAliveChanged;
            syncedState.OnValueChanged -= HandleStateChanged;
            syncedFacingLeft.OnValueChanged -= HandleFacingChanged;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
                return;

            var current = (Vector2)transform.position;
            if ((current - lastReportedPosition).sqrMagnitude >= 0.000001f)
            {
                lastReportedPosition = current;
                syncedPosition.Value = current;
            }

            // 状态与朝向变化频率低，逐帧比对后再写，避免无谓的网络流量。
            var state = (byte)ResolveLocalState();
            if (state != lastReportedState)
            {
                lastReportedState = state;
                syncedState.Value = state;
            }

            var facingLeft = ResolveFacingLeft();
            if (facingLeft != lastReportedFacingLeft)
            {
                lastReportedFacingLeft = facingLeft;
                syncedFacingLeft.Value = facingLeft;
            }

            // 存活状态：死亡后置 false，客户端据此隐藏尸体。
            var alive = !IsLocalDead();
            if (syncedAlive.Value != alive)
                syncedAlive.Value = alive;
        }

        private void Update()
        {
            if (!IsSpawned || IsServer)
                return;

            // 客户端：平滑跟随权威位置，避免网络抖动导致敌人瞬移。
            transform.position = Vector2.Lerp(
                transform.position,
                syncedPosition.Value,
                Time.deltaTime * positionLerpSpeed);

            // 客户端必须自己驱动视觉：AI 已被禁用，没人再调 Step。
            // 用主机广播的状态 + 位置差推导的速度，复现移动 / 攻击 / 死亡动画。
            StepClientVisual();
        }

        /// <summary>
        /// 客户端驱动视觉。速度由位置差推导（客户端没有权威 velocity），
        /// 状态直接取主机广播值，保证动画与主机一致。
        /// </summary>
        private void StepClientVisual()
        {
            var state = (EnemyState)syncedState.Value;

            // BOSS 用独立的视觉组件（两段攻击帧），单独驱动。
            if (bossVisual != null)
            {
                StepBossVisual(state);
                return;
            }

            if (visual == null)
                return;

            // 死亡：播死亡动画，播完隐藏（与主机 ClearCorpses 的语义一致）。
            // 隐藏判定必须放在这里逐帧检查 —— HandleAliveChanged 只在值变化时触发一次，
            // 那一刻死亡动画通常还没播完，靠它隐藏会导致尸体永久残留。
            if (state == EnemyState.Dead)
            {
                visual.Step(EnemyState.Dead, Vector2.zero, Time.deltaTime);
                if (visual.DeathFinished)
                    gameObject.SetActive(false);

                return;
            }

            // 用本帧位移推导速度，供移动动画与朝向使用。
            var velocity = (syncedPosition.Value - (Vector2)transform.position) * positionLerpSpeed;
            visual.Step(state, velocity, Time.deltaTime);
        }

        /// <summary>
        /// 客户端驱动 BOSS 视觉。BOSS 只有「待机 / 攻击 / 死亡」三种表现，
        /// 攻击状态由主机广播的 <see cref="EnemyState.Attack"/> 表达。
        /// </summary>
        private void StepBossVisual(EnemyState state)
        {
            if (state == EnemyState.Dead)
            {
                bossVisual.StepDeath(Time.deltaTime);
                if (bossVisual.DeathFinished)
                    gameObject.SetActive(false);

                return;
            }

            var attacking = state == EnemyState.Attack;
            var facing = syncedFacingLeft.Value ? Vector2.left : Vector2.right;
            bossVisual.Step(false, attacking, facing, Time.deltaTime);
        }

        /// <summary>
        /// 客户端请求主机对本体结算伤害。
        ///
        /// 为什么需要：玩家子弹在客户端本地生成并命中敌人，但敌人血量是主机权威。
        /// 客户端若直接调 <see cref="EnemyBrain2D.TakeDamage"/> 只改本地血量，
        /// 主机不知道 —— 表现为「客户端打怪不掉血」。
        /// 因此客户端命中后发本 RPC，由主机统一结算并广播。
        ///
        /// 单机 / 主机端不经过本方法（直接本地结算）。
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestDamageServerRpc(float amount)
        {
            if (amount <= 0f)
                return;

            if (brain != null)
                brain.TakeDamage(amount);
            else if (swarm != null)
                swarm.TakeDamage(amount);
            else if (boss != null)
                boss.TakeDamage(amount);
        }

        /// <summary>
        /// 对本体结算伤害。联机客户端会转成 ServerRpc 请求主机结算；
        /// 主机 / 单机直接本地结算。供 <see cref="Projectile2D"/> 等伤害来源调用。
        /// </summary>
        public void ApplyDamage(float amount)
        {
            if (amount <= 0f)
                return;

            // 单机（未 Spawn）或主机：本地直接结算。
            if (!IsSpawned || IsServer)
            {
                if (brain != null)
                    brain.TakeDamage(amount);
                else if (swarm != null)
                    swarm.TakeDamage(amount);
                else if (boss != null)
                    boss.TakeDamage(amount);

                return;
            }

            // 客户端：请求主机结算。
            RequestDamageServerRpc(amount);
        }

        /// <summary>取本端（主机）敌人的当前行为状态。</summary>
        private EnemyState ResolveLocalState()
        {
            if (brain != null)
                return brain.State;

            if (swarm != null)
                return swarm.IsDead ? EnemyState.Dead : EnemyState.Chase;

            if (boss != null)
            {
                if (boss.IsDead)
                    return EnemyState.Dead;

                return boss.IsAttacking ? EnemyState.Attack : EnemyState.Patrol;
            }

            return EnemyState.Patrol;
        }

        /// <summary>本端敌人是否已死亡。</summary>
        private bool IsLocalDead()
        {
            if (brain != null)
                return brain.IsDead;

            if (swarm != null)
                return swarm.IsDead;

            if (boss != null)
                return boss.IsDead;

            return false;
        }

        /// <summary>取本端敌人的水平朝向（true = 朝左）。</summary>
        private bool ResolveFacingLeft()
        {
            if (visual != null && visual.TryGetFacingLeft(out var left))
                return left;

            return false;
        }

        private void HandlePositionChanged(Vector2 previous, Vector2 current)
        {
            if (IsServer)
                return;

            transform.position = current;
        }

        private void HandleAliveChanged(bool previous, bool current)
        {
            // 存活状态本身由 StepClientVisual 每帧读取（死亡动画播完才隐藏），
            // 这里无需额外处理 —— 保留回调是为了后续可能的音效 / 特效扩展。
        }

        private void HandleStateChanged(byte previous, byte current)
        {
            // 状态由 StepClientVisual 每帧读取，这里无需额外处理。
        }

        private void HandleFacingChanged(bool previous, bool current)
        {
            if (IsServer)
                return;

            ApplyFacing(current);
        }

        private void ApplyFacing(bool facingLeft)
        {
            if (visual != null)
                visual.SetFacingLeft(facingLeft);
        }

        /// <summary>
        /// 客户端禁用 AI 与物理：敌人行为由主机权威驱动，
        /// 客户端若继续跑 AI 会与广播位置互相拉扯，出现抖动或「各打各的」。
        ///
        /// 注意：**不能禁用视觉组件** —— 客户端要靠它复现动画。
        /// </summary>
        private void DisableLocalBehaviours()
        {
            // 用「网络傀儡门」而非 enabled = false：
            // 禁用组件会触发 OnDisable 注销 WorldRegistry，导致客户端武器索敌列表为空。
            // 傀儡门只跳过 AI 逻辑，保留注册与可被索敌。
            if (brain != null)
                brain.NetworkPuppet = true;
            if (swarm != null)
                swarm.NetworkPuppet = true;
            if (boss != null)
                boss.NetworkPuppet = true;

            foreach (var behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || behaviour == this)
                    continue;

                // 只关无注册副作用的行为脚本，保留视觉 / 动画等表现组件。
                if (behaviour is SpawnerSummoner2D
                    || behaviour is MeleeAttacker2D
                    || behaviour is EnemyPatrol2D
                    || behaviour is ArtilleryMortar2D)
                {
                    behaviour.enabled = false;
                }
            }

            if (body != null)
                body.isKinematic = true;
        }
    }
}
