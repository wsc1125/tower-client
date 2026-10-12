using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

namespace Tower.Netcode
{
    /// <summary>
    /// 玩家对象的位置同步组件（阶段A：多人同场 + 位置同步）。
    ///
    /// 权威模型：**客户端权威（Client Authority）**。
    /// 每个玩家对象由自己的 owner 控制，owner 本地读输入物理移动，
    /// 再通过 ServerRpc 把位置上报主机，主机校验后写入权威字段广播给所有端。
    /// 之所以不用主机权威：PvE 爬塔对操作手感要求高，客户端权威无输入延迟；
    /// 代价是主机无法完全防作弊，因此这里加了「最大位移速度」校验兜底。
    ///
    /// 职责：
    ///   1. 区分「本地控制玩家」与「远端玩家」。
    ///      本地（owner）仍照常读输入用 Rigidbody2D.MovePosition 物理移动；
    ///      远端禁用本端移动输入，位置改由主机广播的权威字段驱动，并设 isKinematic 避免物理反馈干扰。
    ///   2. 权威字段 <see cref="syncedPosition"/> 只允许主机写入：
    ///      - 主机自己的玩家：本地移动后直接把位置写入字段。
    ///      - 远端客户端玩家：客户端通过 ServerRpc 上报，主机校验（owner + 限速）后写入字段，再广播给所有端。
    /// </summary>
    public sealed class PlayerNetworkSync : NetworkBehaviour
    {
        [Tooltip("本端控制的移动组件。远端会被自动禁用，避免重复读输入。")]
        [SerializeField] private PlayerMovement2D movement;
        [SerializeField] private Sprite deathSprite;

        [Tooltip("主机侧允许的最大位移速度（单位/秒）。超过则判定为异常上报并拒绝。\n" +
                 "取一个明显高于正常移动速度的值，避免误伤正常玩家。")]
        [SerializeField] private float maxReportedSpeed = 30f;

        [Tooltip("召唤复活的脱战半径。存活玩家此半径内无存活敌人才能召唤复活队友。")]
        [SerializeField] private float summonSafeRadius = 8f;

        private const float ReviveRange = 2f;
        private SummonReviveButton summonButton;
        private CanvasGroup summonButtonGroup;
        private Button summonUiButton;

        private readonly NetworkVariable<Vector2> syncedPosition =
            new NetworkVariable<Vector2>(writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// 权威血量。主机结算伤害后写入，客户端据此刷新 HUD 与受击表现。
        ///
        /// 为什么必须同步：伤害只在主机结算（客户端敌人 AI 被禁用），
        /// 若不同步，客户端玩家的血量永远是满的 —— 看不到掉血、受击闪烁与屏幕红边。
        /// </summary>
        private readonly NetworkVariable<float> syncedHealth =
            new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Server);

        /// <summary>权威血量上限。与当前血量一起同步，避免客户端用错上限导致血条比例失真。</summary>
        private readonly NetworkVariable<float> syncedMaxHealth =
            new NetworkVariable<float>(100f, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// 权威存活状态。主机写入，客户端据此切换观战 / 操作。
        ///
        /// 为什么必须同步：死亡判定只在主机结算（客户端敌人 AI 被禁用），
        /// 若不同步，客户端玩家死亡后仍能操作，且队友看不到「已倒地」状态。
        /// </summary>
        private readonly NetworkVariable<bool> syncedAlive =
            new NetworkVariable<bool>(true, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>
        /// 主手武器 Id（<see cref="ItemDefinition.Id"/>，即资源名）。空表示未装备。
        /// 写入权限为 Owner —— 装备数据本来就在各端本地，owner 是唯一真相来源。
        /// </summary>
        private readonly NetworkVariable<Unity.Collections.FixedString32Bytes> syncedMainHandId =
            new NetworkVariable<Unity.Collections.FixedString32Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        /// <summary>副手武器 Id。空表示未装备。</summary>
        private readonly NetworkVariable<Unity.Collections.FixedString32Bytes> syncedOffHandId =
            new NetworkVariable<Unity.Collections.FixedString32Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        /// <summary>当前使用的武器槽（映射 <see cref="EquipmentSlotType"/>）。</summary>
        private readonly NetworkVariable<byte> syncedActiveSlot =
            new NetworkVariable<byte>(
                (byte)EquipmentSlotType.None,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        /// <summary>当前瞄准角（度）。远端据此复现队友枪口朝向。</summary>
        private readonly NetworkVariable<float> syncedAimAngle =
            new NetworkVariable<float>(
                0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        private Rigidbody2D body;
        private bool applyRemotePosition;
        private PlayerHealth2D health;
        private PlayerDeathFlow2D deathFlow;
        private Transform visual;
        private float lastReportedHealth = -1f;
        private System.Action<float> pendingHeal;
        private uint nextHealRequestId;
        private uint pendingHealRequestId;
        private uint lastSettledHealRequestId;

        private WeaponMountSlot mainHandMount;
        private WeaponMountSlot offHandMount;
        private PlayerWeaponSelector weaponSelector;
        private RifleShooter2D shooter;
        private bool weaponStateSubscribed;
        private float lastPublishedAimAngle = float.NaN;

        /// <summary>主机侧记录的上一次接受位置与时间，用于限速校验。</summary>
        private Vector2 lastAcceptedPosition;
        private float lastAcceptedTime;
        private uint teleportVersion;

        /// <summary>是否本端实际控制的玩家。</summary>
        public bool IsLocalControl => IsSpawned && IsOwner;
        public bool IsConsumableOwner => IsLocalConsumableOwner(IsSpawned, IsOwner, BattleNetworkBinder.IsOnlineSession);
        public static bool IsLocalConsumableOwner(bool spawned, bool owner, bool online) =>
            spawned ? owner : !online;

        public bool RequestConsumableHeal(float amount, System.Action<float> completed)
        {
            if (!IsSpawned || !IsOwner || pendingHeal != null || (float.IsNaN(amount) || float.IsInfinity(amount)) || amount <= 0f)
                return false;

            pendingHeal = completed;
            pendingHealRequestId = ++nextHealRequestId;
            RequestHealServerRpc(amount, pendingHealRequestId);
            return true;
        }

        public static bool ShouldProcessHealRequest(ulong sender, ulong owner, uint requestId, uint lastSettled)
        {
            return sender == owner && requestId > lastSettled;
        }

        public static bool CanApplyHealRequest(ulong sender, ulong owner, PlayerHealth2D target, float amount)
        {
            return sender == owner && target != null && !target.IsDead && !target.IsInCombatLock &&
                   target.MissingHealth() > 0f && !float.IsNaN(amount) && !float.IsInfinity(amount) &&
                   amount > 0f && amount <= target.MaximumHealthValue;
        }

        [ServerRpc]
        private void RequestHealServerRpc(float amount, uint requestId, ServerRpcParams rpcParams = default)
        {
            var healed = 0f;
            if (ShouldProcessHealRequest(rpcParams.Receive.SenderClientId, OwnerClientId,
                requestId, lastSettledHealRequestId))
            {
                lastSettledHealRequestId = requestId;
                if (IsServer && IsSpawned && syncedAlive.Value &&
                    CanApplyHealRequest(rpcParams.Receive.SenderClientId, OwnerClientId, health, amount))
                {
                    healed = health.Heal(amount);
                    syncedHealth.Value = health.CurrentHealthValue;
                    syncedMaxHealth.Value = health.MaximumHealthValue;
                    lastReportedHealth = health.CurrentHealthValue;
                }
            }

            if (IsServer && rpcParams.Receive.SenderClientId == OwnerClientId)
                ConfirmHealClientRpc(requestId, healed, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
                });
        }

        [ClientRpc]
        private void ConfirmHealClientRpc(uint requestId, float healed, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner || pendingHeal == null || requestId != pendingHealRequestId)
                return;

            var callback = pendingHeal;
            pendingHeal = null;
            callback(healed);
        }

        /// <summary>
        /// 本端玩家是否存活（联机权威值）。
        /// 单机（未 Spawn）恒为 true，不影响单机行为。
        /// </summary>
        public bool IsAlive => !IsSpawned || syncedAlive.Value;

        /// <summary>
        /// 主机侧：某玩家已主动退出本局。参数为退出的 clientId。
        ///
        /// 供 <see cref="TowerRunManager"/> 等订阅，用于把该玩家从撤离判定等名册中移除。
        /// </summary>
        public event System.Action<ulong> PlayerQuit;

        /// <summary>
        /// 主机侧：某玩家已死亡。参数为死亡的 clientId。
        ///
        /// 供 <see cref="BattleSessionState"/> 等订阅，用于更新存活计数并判定全员死亡。
        /// </summary>
        public event System.Action<ulong> PlayerDied;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            body = GetComponent<Rigidbody2D>();
            if (movement == null)
                movement = GetComponent<PlayerMovement2D>();

            health = GetComponent<PlayerHealth2D>();
            deathFlow = GetComponentInChildren<PlayerDeathFlow2D>(true);
            visual = transform.Find("Visual");

            // 主机侧初始化限速基准，避免首个上报因 elapsed 过大而误判。
            if (IsServer)
            {
                lastAcceptedPosition = transform.position;
                lastAcceptedTime = Time.realtimeSinceStartup;

                // 主机：把本端血量作为初始权威值广播，避免客户端开局血条为空。
                if (health != null)
                {
                    syncedHealth.Value = health.CurrentHealthValue;
                    syncedMaxHealth.Value = health.MaximumHealthValue;
                    lastReportedHealth = health.CurrentHealthValue;
                }
            }

            syncedHealth.OnValueChanged += HandleHealthChanged;
            syncedMaxHealth.OnValueChanged += HandleMaxHealthChanged;
            syncedAlive.OnValueChanged += HandleAliveChanged;
            syncedMainHandId.OnValueChanged += HandleWeaponStateChanged;
            syncedOffHandId.OnValueChanged += HandleWeaponStateChanged;
            syncedActiveSlot.OnValueChanged += HandleWeaponStateChanged;
            syncedAimAngle.OnValueChanged += HandleAimAngleChanged;

            // OnValueChanged 不会为初始同步值触发，所有端先落地外观与碰撞状态。
            SetVisualAlive(visual, syncedAlive.Value, deathSprite);
            SetColliderAlive(syncedAlive.Value);
            if (IsOwner)
                ApplyAliveState(syncedAlive.Value);

            if (IsOwner)
            {
                // 本地玩家：照常读取输入、物理移动。
                if (movement != null)
                    movement.enabled = true;
                if (body != null)
                    body.isKinematic = false;

                // 动态生成的玩家来自 prefab，其 PlayerMovement2D.joystick 未接线（摇杆在场景 HUDCanvas 内，
                // 不在 prefab 里）。这里在运行时把场景摇杆绑给本地玩家，否则虚拟摇杆不响应。
                if (movement != null && movement.Joystick == null)
                {
                    var joystick = FindObjectOfType<VirtualJoystickController>();
                    if (joystick != null)
                        movement.SetJoystick(joystick);
                }

                // 武器接线：prefab 无法序列化场景内引用（攻击按钮 / 切换按钮在场景 HUD 里），
                // 不补接会导致「房主玩家武器不显示、攻击键无反应、切武器无效」。
                //
                // 时序关键：联机玩家对象由 NGO 在**主菜单场景**生成（StartHost 那一刻），
                // 而攻击按钮在**战斗场景**的 HUDCanvas 里。OnNetworkSpawn 只执行一次，
                // 此刻 HUD 尚不存在，直接补接必然拿到 null 且永不重试 ——
                // 表现为「联机下无法攻击、无法瞄准」。
                // 因此改为：HUD 已就绪则立即补接，否则等 HUD 广播就绪后再补接。
                if (BattleHudReady.IsReady)
                {
                    BindHealthHud();
                    BindWeaponRuntime();
                    BindLootRuntime();
                }
                else
                    BattleHudReady.Ready += HandleHudReady;

                // 让相机跟着自己控制的角色走（联机 Binder 激活时才有 Instance，单机为 null 无副作用）。
                if (Tower.Netcode.BattleNetworkBinder.Instance != null)
                    Tower.Netcode.BattleNetworkBinder.Instance.AttachLocalCamera(transform);
            }
            else
            {
                // 远端玩家：不读输入；位置由主机广播驱动；物理体改为运动学，只跟随权威位置。
                if (movement != null)
                {
                    movement.KeepTargetRegistrationWhenDisabled(true);
                    movement.enabled = false;
                }
                if (body != null)
                    body.isKinematic = true;
                var remoteLootInteractor = GetComponent<LootInteractor>();
                if (remoteLootInteractor != null)
                    remoteLootInteractor.enabled = false;

                transform.position = syncedPosition.Value;
                applyRemotePosition = true;

                // 远端副本的受击闪烁必须照常计时：主机端它是权威无敌帧的计时来源，
                // 若被本机冻结（本机玩家观战 / 弹窗）卡住，远端玩家会永久免疫，
                // 血量不再结算 → 客户端血条与死亡状态不再同步。
                SetRemoteCopyVisual(visual, true);

                // 远端玩家的武器组件必须禁用：它们读的是 GameSession.LocalPlayer（本机装备），
                // 若继续运行，队友手上会显示「你自己的武器」，且会用自己的武器数据自动瞄准。
                DisableRemoteWeaponComponents();

                // 远端武器视图：显示内容改由 owner 广播的武器状态驱动。
                ApplyRemoteWeaponState();
                ApplyRemoteAimAngle(syncedAimAngle.Value);

                // 远端玩家只同步自身血量，不写本机玩家的 HUD。
                if (health != null)
                    health.ApplyAuthoritativeHealth(syncedHealth.Value, syncedMaxHealth.Value, false);
            }
        }

        /// <summary>
        /// 禁用远端玩家身上的武器组件。
        ///
        /// 这些组件都硬编码读 <c>GameSession.LocalPlayer</c>（本机玩家装备），
        /// 在远端玩家对象上运行会显示错误的武器并自动瞄准，因此必须关掉。
        /// 队友的武器表现后续由网络同步驱动（当前阶段先保证不显示错误内容）。
        /// </summary>
        private void DisableRemoteWeaponComponents()
        {
            var shooter = GetComponent<RifleShooter2D>();
            if (shooter != null)
                shooter.enabled = false;

            var selector = GetComponent<PlayerWeaponSelector>();
            if (selector != null)
                selector.enabled = false;

            // 挂点切换为远端驱动：不再读本机装备，改由 ApplyRemoteWeaponState 落地同步值。
            // 不能像以前那样直接关渲染，否则队友永远看不到武器。
            foreach (var mount in GetComponentsInChildren<WeaponMountSlot>(true))
                mount.SetRemoteDriven(true);
        }

        /// <summary>
        /// 远端武器视图：按 owner 广播的武器 Id 与当前槽位，驱动两个挂点的显示。
        /// 只读同步值，不读 <c>GameSession.LocalPlayer</c>。
        /// </summary>
        private void ApplyRemoteWeaponState()
        {
            if (IsOwner)
                return;

            var activeSlot = (EquipmentSlotType)syncedActiveSlot.Value;

            foreach (var mount in GetComponentsInChildren<WeaponMountSlot>(true))
            {
                var isMain = mount.MountedSlot == EquipmentSlotType.MainHand;
                var id = isMain ? syncedMainHandId.Value.ToString() : syncedOffHandId.Value.ToString();
                var definition = ResolveDefinition(id);
                var selected = activeSlot == EquipmentSlotType.None || mount.MountedSlot == activeSlot;

                mount.ApplyRemoteState(definition, selected);
            }
        }

        /// <summary>按 Id 解析武器定义。数据库缺失或未登记时返回 null（挂点会隐藏）。</summary>
        private ItemDefinition ResolveDefinition(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            // ItemDatabase 是 ScriptableObject 资产，必须从 GameSession 取，
            // FindObjectOfType 只能搜场景对象，永远拿不到它。
            var database = GameSession.Database;
            return database != null ? database.Find(id) : null;
        }

        /// <summary>远端瞄准角变化：旋转对应挂点的瞄准轴，复现队友枪口朝向。</summary>
        private void ApplyRemoteAimAngle(float angle)
        {
            if (IsOwner)
                return;

            var activeSlot = (EquipmentSlotType)syncedActiveSlot.Value;
            var mount = ResolveMount(activeSlot);
            if (mount == null)
                return;

            var pivot = mount.AimPivot != null
                ? mount.AimPivot
                : (mount.Muzzle != null ? mount.Muzzle.parent : null);
            if (pivot != null)
                pivot.rotation = Quaternion.Euler(0f, 0f, angle);

            var renderer = mount.WeaponRenderer;
            if (renderer != null)
                renderer.flipY = Mathf.Abs(angle) > 90f;
        }

        /// <summary>
        /// 按槽位取挂点。远端分支不能依赖 owner 才赋值的缓存字段，
        /// 因此这里按需从子物体解析并缓存。
        /// </summary>
        private WeaponMountSlot ResolveMount(EquipmentSlotType slot)
        {
            if (mainHandMount == null || offHandMount == null)
            {
                foreach (var mount in GetComponentsInChildren<WeaponMountSlot>(true))
                {
                    if (mount.MountedSlot == EquipmentSlotType.MainHand)
                        mainHandMount = mount;
                    else if (mount.MountedSlot == EquipmentSlotType.OffHand)
                        offHandMount = mount;
                }
            }

            return slot == EquipmentSlotType.OffHand ? offHandMount : mainHandMount;
        }

        private void HandleWeaponStateChanged(Unity.Collections.FixedString32Bytes previous, Unity.Collections.FixedString32Bytes current)
        {
            ApplyRemoteWeaponState();
        }

        private void HandleWeaponStateChanged(byte previous, byte current)
        {
            ApplyRemoteWeaponState();
            ApplyRemoteAimAngle(syncedAimAngle.Value);
        }

        private void HandleAimAngleChanged(float previous, float current)
        {
            ApplyRemoteAimAngle(current);
        }

        public override void OnNetworkDespawn()
        {
            syncedHealth.OnValueChanged -= HandleHealthChanged;
            syncedMaxHealth.OnValueChanged -= HandleMaxHealthChanged;
            syncedAlive.OnValueChanged -= HandleAliveChanged;
            syncedMainHandId.OnValueChanged -= HandleWeaponStateChanged;
            syncedOffHandId.OnValueChanged -= HandleWeaponStateChanged;
            syncedActiveSlot.OnValueChanged -= HandleWeaponStateChanged;
            syncedAimAngle.OnValueChanged -= HandleAimAngleChanged;

            UnsubscribeWeaponState();
            var interruptedHeal = pendingHeal;
            pendingHeal = null;
            interruptedHeal?.Invoke(0f);
            if (IsOwner)
            {
                var lootInteractor = GetComponent<LootInteractor>();
                if (lootInteractor != null)
                    lootInteractor.BindRuntime(null);
            }

            // 退订 HUD 就绪事件，避免玩家销毁后仍被回调（悬挂引用）。
            BattleHudReady.Ready -= HandleHudReady;

            // 退订缓存的按钮引用；HUD 可能已先被场景卸载销毁。
            if (summonButton != null)
            {
                summonButton.Clicked -= OnSummonReviveClicked;
                SetSummonButtonVisible(summonButtonGroup, summonUiButton, false);
            }
            summonButton = null;
            summonButtonGroup = null;
            summonUiButton = null;

            base.OnNetworkDespawn();
        }

        /// <summary>
        /// HUD 就绪回调：此时战斗场景的 HUDCanvas 已实例化完毕，
        /// 可以安全地补接血量面板、受击红边与武器按钮等场景内引用。
        /// 补接后立即退订，避免重复执行。
        /// </summary>
        private void HandleHudReady()
        {
            BattleHudReady.Ready -= HandleHudReady;
            BindHealthHud();
            BindWeaponRuntime();
            BindLootRuntime();
        }

        private void BindLootRuntime()
        {
            if (!IsOwner)
                return;

            var lootInteractor = GetComponent<LootInteractor>();
            if (lootInteractor != null)
                lootInteractor.BindRuntime(FindObjectOfType<LootPanelController>());
        }

        private void BindHealthHud()
        {
            if (health != null)
                health.BindHud(
                    FindObjectOfType<StatusPanelController>(),
                    FindObjectOfType<DamageVignetteController>());
        }

        /// <summary>
        /// 为本机玩家补接武器相关引用。
        ///
        /// 为什么需要：联机玩家对象由 NGO 从 prefab 生成，而 prefab 无法序列化**场景内**引用
        /// （攻击按钮 / 切换按钮都在场景 HUDCanvas 里）。单机玩家是场景实例，这些引用在场景里
        /// 已 override，所以单机正常；联机玩家若不补接，会出现：
        ///   - <c>RifleShooter2D.mainHandSlot</c> 为空 → 不瞄准、不开火；
        ///   - <c>RifleShooter2D.attackButton</c> 为空 → 攻击键无反应；
        ///   - <c>PlayerWeaponSelector.switchButton</c> 为空 → 切武器无效；
        ///   - <c>WeaponMountSlot.selector</c> 为空 → 武器显示判定失效。
        ///
        /// 挂点（主手 / 副手）在玩家对象自身层级内，prefab 里已接好，这里按槽位重新解析一次，
        /// 保证与 selector 的引用一致。
        /// </summary>
        private void BindWeaponRuntime()
        {
            var selector = GetComponent<PlayerWeaponSelector>();
            var shooter = GetComponent<RifleShooter2D>();

            // 挂点：按 mountedSlot 区分主手 / 副手。
            WeaponMountSlot mainHand = null;
            WeaponMountSlot offHand = null;
            foreach (var mount in GetComponentsInChildren<WeaponMountSlot>(true))
            {
                if (mount.MountedSlot == EquipmentSlotType.MainHand)
                    mainHand = mount;
                else if (mount.MountedSlot == EquipmentSlotType.OffHand)
                    offHand = mount;

                // 挂点也要接 selector，否则 IsSelected 判定失效、两把武器同时显示。
                if (selector != null)
                    mount.BindRuntime(selector);
            }

            mainHandMount = mainHand;
            offHandMount = offHand;
            weaponSelector = selector;
            this.shooter = shooter;

            // 绑定后强制刷新一次：消除「依赖 LoadoutChanged 触发」的时序依赖，
            // 否则非房主玩家可能拿不到武器贴图，表现为不瞄准、不开火。
            if (mainHand != null)
                mainHand.RefreshFromLocal();
            if (offHand != null)
                offHand.RefreshFromLocal();

            SubscribeWeaponState();

            // 切换按钮：场景 HUD 里的唯一一个。
            if (selector != null)
            {
                var switchButton = FindObjectOfType<AttackSwitchButton>();
                if (switchButton != null)
                    selector.BindRuntime(switchButton);
            }

            // 开火按钮：场景 HUD 里的唯一一个。
            if (shooter != null)
            {
                var fireButton = FindObjectOfType<HoldButton>();
                shooter.BindRuntime(mainHand, offHand, selector, fireButton, movement);
            }

            // 召唤复活按钮：场景 HUD 里的唯一一个。仅本机 owner 玩家绑定，
            // 否则远端玩家对象也会去订阅同一个按钮，导致一次点击触发多次召唤。
            if (IsOwner)
            {
                var button = FindObjectOfType<SummonReviveButton>();
                if (summonButton != null && summonButton != button)
                    summonButton.Clicked -= OnSummonReviveClicked;

                summonButton = button;
                if (summonButton != null)
                {
                    summonButtonGroup = summonButton.GetComponent<CanvasGroup>();
                    if (summonButtonGroup == null)
                        summonButtonGroup = summonButton.gameObject.AddComponent<CanvasGroup>();
                    summonUiButton = summonButton.GetComponent<Button>();
                    summonButton.Clicked -= OnSummonReviveClicked;
                    summonButton.Clicked += OnSummonReviveClicked;
                    RefreshSummonButton();
                }
            }
        }

        private void OnSummonReviveClicked()
        {
            if (IsSpawned && IsOwner && IsAlive && FindNearestDeadTeammate(transform.position) != null)
                RequestSummonReviveServerRpc();
        }

        private void RefreshSummonButton()
        {
            if (summonButton == null)
                return;

            SetSummonButtonVisible(summonButtonGroup, summonUiButton,
                IsSpawned && IsOwner && IsAlive && FindNearestDeadTeammate(transform.position) != null);
        }

        public static void SetSummonButtonVisible(CanvasGroup group, Button button, bool visible)
        {
            if (group != null)
            {
                group.alpha = visible ? 1f : 0f;
                group.blocksRaycasts = visible;
                group.interactable = visible;
            }
            if (button != null)
                button.interactable = visible;
        }

        public static bool IsWithinReviveRange(Vector2 origin, Vector2 corpse)
        {
            return (origin - corpse).sqrMagnitude <= ReviveRange * ReviveRange;
        }

        private PlayerNetworkSync FindNearestDeadTeammate(Vector2 origin)
        {
            PlayerNetworkSync nearest = null;
            var nearestDistance = ReviveRange * ReviveRange;
            foreach (var player in WorldRegistry.Players)
            {
                if (player == null)
                    continue;

                var candidate = player.GetComponent<PlayerNetworkSync>();
                if (candidate == null || candidate == this || !candidate.IsSpawned || candidate.IsAlive)
                    continue;

                var corpsePosition = candidate.IsOwner ? (Vector2)candidate.transform.position : candidate.syncedPosition.Value;
                if (!IsWithinReviveRange(origin, corpsePosition))
                    continue;

                var distance = (origin - corpsePosition).sqrMagnitude;
                if (distance <= nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        /// <summary>
        /// owner 侧订阅装备与切枪事件，变化时把武器状态广播出去。
        /// 只在 owner 上订阅，避免远端对象也去写这些字段。
        /// </summary>
        private void SubscribeWeaponState()
        {
            if (!IsOwner || weaponStateSubscribed)
                return;

            weaponStateSubscribed = true;
            GameSession.LoadoutChanged += PublishWeaponState;

            if (weaponSelector != null)
                weaponSelector.Changed += PublishWeaponState;

            PublishWeaponState();
        }

        private void UnsubscribeWeaponState()
        {
            if (!weaponStateSubscribed)
                return;

            weaponStateSubscribed = false;
            GameSession.LoadoutChanged -= PublishWeaponState;

            if (weaponSelector != null)
                weaponSelector.Changed -= PublishWeaponState;
        }

        /// <summary>owner 把本机装备与当前槽位写入同步字段。</summary>
        private void PublishWeaponState()
        {
            if (!IsOwner || !IsSpawned)
                return;

            syncedMainHandId.Value = ToFixedString(ResolveWeaponId(EquipmentSlotType.MainHand));
            syncedOffHandId.Value = ToFixedString(ResolveWeaponId(EquipmentSlotType.OffHand));
            syncedActiveSlot.Value = (byte)(weaponSelector != null
                ? weaponSelector.ActiveSlot
                : EquipmentSlotType.None);
        }

        /// <summary>取本机某槽位武器的 Id；未装备时返回空串。</summary>
        private static string ResolveWeaponId(EquipmentSlotType slot)
        {
            var instance = GameSession.LocalPlayer.Loadout.Equipment.Get(slot);
            return instance != null && instance.Definition != null ? instance.Definition.Id : string.Empty;
        }

        private static Unity.Collections.FixedString32Bytes ToFixedString(string value)
        {
            return string.IsNullOrEmpty(value)
                ? default
                : new Unity.Collections.FixedString32Bytes(value);
        }

        /// <summary>owner 每帧把当前瞄准角广播出去，供远端复现枪口朝向。</summary>
        private void PublishAimAngle()
        {
            if (!IsOwner || !IsSpawned || shooter == null)
                return;

            var angle = shooter.CurrentAimAngle;
            if (Mathf.Approximately(angle, lastPublishedAimAngle))
                return;

            lastPublishedAimAngle = angle;
            syncedAimAngle.Value = angle;
        }

        /// <summary>
        /// 权威血量变化：客户端刷新本地血量与受击表现。
        /// 主机自己不需要（本地已是权威值）。
        /// </summary>
        private void HandleHealthChanged(float previous, float current)
        {
            if (IsServer || health == null)
                return;

            health.ApplyAuthoritativeHealth(current, syncedMaxHealth.Value, true);
        }

        private void HandleMaxHealthChanged(float previous, float current)
        {
            if (IsServer || health == null)
                return;

            health.ApplyAuthoritativeHealth(syncedHealth.Value, current, false);
        }

        /// <summary>
        /// 远端客户端把本机玩家位置上报主机。仅允许该对象的 owner 调用，
        /// 主机校验通过后写入权威字段，由 NGO 广播给所有客户端。
        ///
        /// 校验两项：
        ///   1. 来源必须是该对象的 owner（防止替别人上报）；
        ///   2. 位移速度不得超过 <see cref="maxReportedSpeed"/>（防止瞬移作弊）。
        /// </summary>
        [ServerRpc]
        private void SyncPositionServerRpc(Vector2 pos, uint version, ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || version != teleportVersion)
                return;

            // 限速校验：主机自己的玩家跳过（本地物理移动，无需防作弊）。
            if (rpcParams.Receive.SenderClientId != NetworkManager.LocalClientId)
            {
                var now = Time.realtimeSinceStartup;
                var elapsed = now - lastAcceptedTime;

                if (elapsed > 0.0001f)
                {
                    var distance = Vector2.Distance(pos, lastAcceptedPosition);
                    if (distance / elapsed > maxReportedSpeed)
                    {
                        // 超速：拒绝本次上报，保留上一权威位置，避免瞬移。
                        return;
                    }
                }

                lastAcceptedPosition = pos;
                lastAcceptedTime = now;
            }

            syncedPosition.Value = pos;
        }

        public static bool ShouldTransitionToDead(float authoritativeHealth, bool currentlyAlive)
        {
            return currentlyAlive && authoritativeHealth <= 0f;
        }

        /// <summary>
        /// 客户端主动退出本局时调用，通知主机移除本端角色。
        ///
        /// 为什么必须通知：客户端退出后其 NetworkObject 不会自动销毁（NGO 只在断连时回收），
        /// 主机若不知道，会继续把该玩家算进撤离判定，且场景里残留一个不动的幽灵角色。
        /// </summary>
        [ServerRpc]
        public void RequestQuitServerRpc(ServerRpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;

            // 先广播退出事件，让订阅方（撤离判定等）把该玩家移出名册。
            PlayerQuit?.Invoke(sender);

            // 主机侧销毁该玩家的网络对象，所有端同步移除。
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        /// <summary>主机重新校验请求，原地复活距离请求者最近的一名阵亡队友。</summary>
        [ServerRpc]
        public void RequestSummonReviveServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId || !syncedAlive.Value)
                return;

            var sessionState = BattleSessionState.Instance;
            if (sessionState != null && sessionState.RunFinished)
                return;

            var origin = IsOwner ? (Vector2)transform.position : syncedPosition.Value;
            var nearest = FindNearestDeadTeammate(origin);
            if (nearest == null)
                return;

            var enemies = LootInteractor.CollectDamageables();
            if (LootScan.HasAliveEnemyWithin(enemies, origin, summonSafeRadius))
                return;

            nearest.Revive(0.4f);
        }

        /// <summary>主机：把本玩家对象移到指定位置（召唤用）。</summary>
        public void TeleportTo(Vector2 position)
        {
            if (!IsServer)
                return;

            if (body != null)
                body.position = position;

            transform.position = new Vector3(position.x, position.y, transform.position.z);
            lastAcceptedPosition = position;
            lastAcceptedTime = Time.realtimeSinceStartup;
            teleportVersion++;
            syncedPosition.Value = position;

            if (!IsOwner)
            {
                TeleportOwnerClientRpc(position, teleportVersion, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
                });
            }
        }

        [ClientRpc]
        private void TeleportOwnerClientRpc(Vector2 position, uint version, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner || version <= teleportVersion)
                return;

            teleportVersion = version;
            if (body != null)
                body.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        /// <summary>
        /// 主机执行复活：恢复血量并广播存活状态。
        /// 复活后血量 = 上限 × <paramref name="healthRatio"/>（默认 40%）。
        /// </summary>
        public void Revive(float healthRatio)
        {
            if (!IsServer || health == null || syncedAlive.Value)
                return;

            health.Revive(healthRatio);

            syncedMaxHealth.Value = health.MaximumHealthValue;
            syncedHealth.Value = health.CurrentHealthValue;
            lastReportedHealth = health.CurrentHealthValue;
            syncedAlive.Value = true;
        }

        /// <summary>
        /// 存活状态变化：本机玩家据此启用 / 禁用本地操作。
        ///
        /// 主机端也必须生效：主机玩家死亡时同样要冻结本机操作（转观战），
        /// 复活时解冻。此前对 <c>IsServer</c> 直接 return，导致主机玩家死亡后
        /// 本机被冻结却没有任何解冻路径 —— 表现为「房主死亡后角色永久不能动」。
        ///
        /// 只有 <c>IsOwner</c>（本端实际控制的玩家）才动 <see cref="LocalPauseScope"/>：
        /// 主机端对客户端玩家（非 owner）不生效，避免远端玩家死亡冻结主机操作。
        /// </summary>
        private void HandleAliveChanged(bool previous, bool current)
        {
            SetVisualAlive(visual, current, deathSprite);
            SetColliderAlive(current);
            if (IsOwner)
                ApplyAliveState(current);
        }

        private void SetColliderAlive(bool alive)
        {
            if (!alive)
            {
                foreach (var portal in FloorPortal2D.Active)
                {
                    if (portal != null)
                        portal.RemovePlayer(OwnerClientId);
                }
            }

            var playerCollider = GetComponent<Collider2D>();
            if (playerCollider != null)
                playerCollider.enabled = alive;
        }

        public static void SetVisualAlive(Transform visualRoot, bool alive, Sprite deathFrame = null)
        {
            if (visualRoot == null)
                return;

            var characterVisual = visualRoot.GetComponent<CharacterVisual2D>();
            if (characterVisual != null)
                characterVisual.SetDeathState(!alive, deathFrame);

            var mainHand = visualRoot.Find("MainHand");
            if (mainHand != null)
                mainHand.gameObject.SetActive(alive);
            var offHand = visualRoot.Find("OffHand");
            if (offHand != null)
                offHand.gameObject.SetActive(alive);
        }

        /// <summary>
        /// 标记远端玩家的网络副本（非 owner 实例）。远端副本的受击闪烁计时
        /// 不参与本机冻结，保证主机侧权威无敌帧不会因本机弹窗 / 观战而永久卡住。
        /// </summary>
        public static void SetRemoteCopyVisual(Transform visualRoot, bool remote)
        {
            if (visualRoot == null)
                return;

            var characterVisual = visualRoot.GetComponent<CharacterVisual2D>();
            if (characterVisual != null)
                characterVisual.IsRemoteCopy = remote;
        }

        private void ApplyAliveState(bool alive)
        {
            if (alive)
            {
                if (!IsServer && health != null)
                    health.ApplyAuthoritativeRevive(syncedHealth.Value, syncedMaxHealth.Value);

                if (deathFlow != null)
                    deathFlow.ExitSpectate();
                else
                    LocalPauseScope.SetSpectating(false);
            }
            else
            {
                if (deathFlow != null)
                    deathFlow.EnterSpectate();
                else
                    LocalPauseScope.SetSpectating(true);
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
            {
                RefreshSummonButton();
                // 本地玩家：移动已由 PlayerMovement2D 在物理阶段完成，这里把位置写入权威字段。
                // 主机玩家：本地直接写字段（ServerRpc 在主机上即时执行）；
                // 客户端玩家：经 ServerRpc 上报主机后再广播。
                SyncPositionServerRpc(transform.position, teleportVersion);

                // owner 广播当前瞄准角，供远端复现枪口朝向。
                PublishAimAngle();
            }
            else if (applyRemotePosition && body != null)
            {
                // 远端玩家：让物理体跟随主机权威位置，保持碰撞体正确参与判定。
                body.MovePosition(syncedPosition.Value);
            }

            // 主机：血量变化时广播。放在 FixedUpdate 而非事件里，
            // 是因为伤害结算分散在多个组件（激光 / 近战 / 虫群 / 炮弹），
            // 统一在这里轮询比对，避免每处都插一段同步代码。
            if (IsServer && health != null)
            {
                var current = health.CurrentHealthValue;
                if (!Mathf.Approximately(current, lastReportedHealth))
                {
                    lastReportedHealth = current;
                    syncedHealth.Value = current;
                    syncedMaxHealth.Value = health.MaximumHealthValue;
                }

                if (ShouldTransitionToDead(current, syncedAlive.Value))
                {
                    syncedAlive.Value = false;
                    PlayerDied?.Invoke(OwnerClientId);
                }
            }
        }
    }
}
