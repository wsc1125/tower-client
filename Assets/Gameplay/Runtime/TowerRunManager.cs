using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 爬塔流程编排：生成一层 → 统计清空 → 生成传送点 → 换层 → 撤退 / 死亡收尾。
/// 换层时只销毁房间与敌人，玩家对象跨层保留，因此血量与护甲天然继承到下一层
/// （这是难度曲线生效的前提）。
/// </summary>
public sealed class TowerRunManager : MonoBehaviour
{
    [Header("配置")]
    [SerializeField] private TowerConfig config;
    [SerializeField] private GameObject roomPrefab;
    [SerializeField] private Transform roomParent;
    [SerializeField] private FloorPortal2D nextPortalPrefab;
    [SerializeField] private FloorPortal2D retreatPortalPrefab;

    [Tooltip("掩体生成器。留空则回退到本对象上的 ObstaclePlacer，仍找不到就不生成掩体。")]
    [SerializeField] private ObstaclePlacer obstaclePlacer;

    [Header("接线")]
    [SerializeField] private Transform player;
    [SerializeField] private CameraFollow2D cameraFollow;
    [SerializeField] private StatusPanelController statusPanel;
    [SerializeField] private TowerResultPanelController resultPanel;
    [SerializeField] private PortalConfirmPanelController portalConfirmPanel;
    [SerializeField] private SettingsPanelController settingsPanel;
    [SerializeField] private PlayerHealth2D playerHealth;
    [SerializeField] private DeathPanelController deathPanel;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Tooltip("联机全员阵亡提示显示时长（秒，使用 unscaled time）。")]
    [SerializeField] private float teamWipeExitDelay = 2f;

    [Header("节奏")]
    [SerializeField] private float portalSpawnDelay = 0.6f;
    [SerializeField] private float clearCheckInterval = 0.2f;
    [SerializeField] private float enemySpawnRadiusMin = 3.5f;
    [SerializeField] private float enemySpawnRadiusMax = 5.5f;
    [SerializeField] private float portalOffsetX = 2.5f;

    [Tooltip("敌人出生点距房间边界的额外内缩量，避免碰撞体插进墙里。")]
    [SerializeField] private float enemySpawnMargin = 1.5f;

    [Tooltip("敌人巡逻路径的半宽。以出生点为中心水平往返。")]
    [SerializeField] private float enemyPatrolHalfWidth = 1.5f;

    [Tooltip("联机全员撤离倒计时（秒）。所有存活玩家进入撤退阵后开始，给反悔窗口。")]
    [SerializeField] private float extractionCountdownSeconds = 3f;

    [Header("随机")]
    [Tooltip("本局的随机种子。0 表示每局自动生成（单机默认）；非 0 则固定，便于复现问题。\n" +
             "联机时由主机生成并广播给所有客户端，保证各端生成同一层。")]
    [SerializeField] private int randomSeed;

    private readonly TowerRunState state = new TowerRunState();
    private readonly List<GameObject> spawnedEnemies = new List<GameObject>();
    private readonly List<FloorPortal2D> spawnedPortals = new List<FloorPortal2D>();

    /// <summary>已订阅事件的传送点（含客户端 NGO 同步来的），避免重复订阅。</summary>
    private readonly HashSet<FloorPortal2D> subscribedPortals = new HashSet<FloorPortal2D>();

    /// <summary>已订阅死亡 / 退出事件的玩家同步组件，避免重复订阅。</summary>
    private readonly HashSet<Tower.Netcode.PlayerNetworkSync> subscribedPlayers =
        new HashSet<Tower.Netcode.PlayerNetworkSync>();
    private GameObject currentRoom;
    private System.Random random;
    private float clearTimer;
    private bool portalsSpawned;
    private bool subscribedDeath;
    private bool subscribedPortalConfirm;
    private bool subscribedSettings;
    private bool subscribedRunFinished;
    private bool floorBuilt;
    private bool runFinishedHandled;
    private bool exitingBattle;
    private bool subscribedDisconnect;

    /// <summary>联机全员撤离倒计时剩余秒数。-1 表示未在倒计时。</summary>
    private float extractionCountdown = -1f;
    private int lastTeamAdvanceFrame = -1;

    /// <summary>本局实际使用的随机种子。0 表示尚未确定（Awake 时会自动生成）。</summary>
    public int RandomSeed => randomSeed;

    /// <summary>当前已订阅的确认面板实例。用于判断订阅是否仍然有效。</summary>
    private PortalConfirmPanelController subscribedPortalTarget;

    /// <summary>当前已订阅的设置面板实例。用于判断订阅是否仍然有效。</summary>
    private SettingsPanelController subscribedSettingsTarget;

    /// <summary>确认弹窗打开期间挂起的传送种类；null 表示没有待确认的传送。</summary>
    private PortalKind? pendingPortalKind;

    // 序列化是为了让 EditMode 测试能直接注入房间边界；运行时由 ResolveRoomBounds 写入。
    [SerializeField] private Rect roomBounds;
    [SerializeField] private bool hasRoomBounds;

    /// <summary>
    /// 本层房间的可活动区域中心。所有「以房间为基准」的摆放（玩家 / 敌人 / 掩体 / 传送点）
    /// 都必须读这个值，不允许各自去读 <c>currentRoom.transform.position</c> ——
    /// 房间根节点并不在几何中心（实测偏移约 (1.25, -4.75)），用根节点位置会让各处摆放互相错位。
    /// 由 <see cref="BuildFloor"/> 在解析出房间边界后写入一次，本层内不再变化。
    /// </summary>
    private Vector2 floorCenter;

    public TowerRunState State => state;

    /// <summary>当前房间的可活动区域（世界坐标）。未解析出时为 <see cref="Rect.zero"/>。</summary>
    public Rect RoomBounds => roomBounds;

    /// <summary>是否已解析出房间可活动区域。</summary>
    public bool HasRoomBounds => hasRoomBounds;

    /// <summary>本层房间的可活动区域中心。未解析出房间边界时为 <see cref="Vector2.zero"/>。</summary>
    public Vector2 FloorCenter => floorCenter;

    private void Awake()
    {
        EnsureRandom();
    }

    /// <summary>
    /// 确保随机源已就绪。种子为 0 时自动生成一个（单机默认行为，每局不同）；
    /// 非 0 时用配置的种子，便于复现问题。
    /// </summary>
    private void EnsureRandom()
    {
        if (random != null)
            return;

        if (randomSeed == 0)
            randomSeed = Environment.TickCount;

        random = new System.Random(randomSeed);
    }

    /// <summary>
    /// 注入本局的随机种子。必须在 <see cref="Start"/> 之前调用（Start 会生成第一层）。
    /// 联机时由主机生成种子并广播，各客户端调用本方法后即可生成完全一致的层。
    /// </summary>
    public void SetRandomSeed(int seed)
    {
        randomSeed = seed;
        random = new System.Random(seed);
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died += HandlePlayerDied;
            subscribedDeath = true;
        }

        SubscribePortalConfirm();
        SubscribeSettings();
        SubscribeDisconnectCallback();

        // 房主会话结束（房主退出 / 掉线）：成员端据此统一退出本局。
        // 静态桥事件，OnEnable 订阅 / OnDisable 退订，与场景生命周期对齐。
        Tower.Netcode.MultiplayerDepartBridge.HostSessionEnded += HandleHostSessionEnded;

        // BattleSessionState 是场景内 NetworkObject，OnNetworkSpawn 晚于本组件 OnEnable，
        // 此刻 Instance 可能为 null。用协程重试，直到订阅成功。
        StartCoroutine(SubscribeRunFinishedWhenReady());
    }

    private void OnDisable()
    {
        if (subscribedDeath && playerHealth != null)
        {
            playerHealth.Died -= HandlePlayerDied;
            subscribedDeath = false;
        }

        UnsubscribePortalConfirm();
        UnsubscribeSettings();
        UnsubscribeRunFinished();
        UnsubscribeDisconnectCallback();
        Tower.Netcode.MultiplayerDepartBridge.HostSessionEnded -= HandleHostSessionEnded;

        foreach (var sync in subscribedPlayers)
        {
            if (sync == null)
                continue;

            sync.PlayerDied -= HandlePlayerDiedNetwork;
            sync.PlayerQuit -= HandlePlayerQuitNetwork;
        }
        subscribedPlayers.Clear();
    }

    /// <summary>
    /// 等 BattleSessionState 就绪后订阅「本局结束」与「层数变化」广播。
    ///
    /// 不能直接在 OnEnable 里订阅：BattleSessionState 由 NGO 在场景同步时生成，
    /// 晚于本组件 OnEnable，此刻 Instance 为 null，订阅会被静默跳过 ——
    /// 房主广播本局结束时客户端收不到，成员会滞留在战斗场景。
    /// </summary>
    private IEnumerator SubscribeRunFinishedWhenReady()
    {
        var deadline = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var sessionState = Tower.Netcode.BattleSessionState.Instance;
            if (sessionState != null)
            {
                sessionState.RunFinishedChanged += HandleRunFinished;
                sessionState.FloorChanged += HandleFloorChanged;
                subscribedRunFinished = true;

                // 网络变量可能早于事件订阅到达，订阅后补读当前状态。
                if (sessionState.RunFinished)
                    HandleRunFinished();
                HandleFloorChanged(sessionState.CurrentFloor);
                yield break;
            }

            yield return null;
        }

        Debug.LogError("[TowerRunManager] 等待 BattleSessionState 超时，本局结束广播将无法接收。");
    }

    /// <summary>
    /// 订阅「本局结束」广播。主机标记结束后，所有客户端据此退出战斗回主菜单，
    /// 否则房主死亡时成员会滞留在战斗场景里。
    /// </summary>
    private void SubscribeRunFinished()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState == null)
            return;

        sessionState.RunFinishedChanged += HandleRunFinished;
        sessionState.FloorChanged += HandleFloorChanged;
        subscribedRunFinished = true;

        if (sessionState.RunFinished)
            HandleRunFinished();
        HandleFloorChanged(sessionState.CurrentFloor);
    }

    private void UnsubscribeRunFinished()
    {
        if (!subscribedRunFinished)
            return;

        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null)
        {
            sessionState.RunFinishedChanged -= HandleRunFinished;
            sessionState.FloorChanged -= HandleFloorChanged;
        }

        subscribedRunFinished = false;
    }

    /// <summary>
    /// 主机广播了新层数：客户端跟随换层。
    ///
    /// 客户端本地不执行换层，由主机权威驱动，保证各端层数一致。
    /// </summary>
    private void HandleFloorChanged(int floor)
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState == null || sessionState.IsServer || !floorBuilt || state.IsFinished)
            return;

        if (floor <= state.CurrentFloor)
            return;

        Debug.Log($"[TowerRunManager] 主机换层到第 {floor} 层，客户端跟随。");

        // 把本地层数推进到主机广播的层数，再重建该层。
        while (state.CurrentFloor < floor)
            state.AdvanceFloor();

        BuildFloor(state.CurrentFloor);
    }

    /// <summary>
    /// 本局结束（主机广播）。客户端收到后退出房间并回主菜单。
    /// 主机自身由死亡 / 撤退流程负责退出，这里不重复处理。
    /// </summary>
    private void HandleRunFinished()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState == null || !sessionState.RunFinished || runFinishedHandled)
            return;

        // 正常撤离保留原结算确认流程；客户端收到主机广播后也展示本地结算。
        if (sessionState.FinishReason == Tower.Netcode.BattleFinishReason.Extracted)
        {
            runFinishedHandled = true;
            if (!sessionState.IsServer)
            {
                state.Finish();
                ClearFloor();
                if (resultPanel != null)
                    resultPanel.Show(BuildResultEntries(), TotalLootValue(), state.CurrentFloor);
            }
            return;
        }

        if (sessionState.FinishReason != Tower.Netcode.BattleFinishReason.TeamWiped)
        {
            runFinishedHandled = true;
            StartCoroutine(ExitToMainMenu());
            return;
        }

        runFinishedHandled = true;
        LocalPauseScope.Freeze();

        if (deathPanel == null)
            deathPanel = FindObjectOfType<DeathPanelController>();
        if (deathPanel != null)
            deathPanel.ShowAutoExit("全员阵亡", "所有队员均已倒下，即将返回主菜单");

        StartCoroutine(ExitAfterTeamWipeDelay());
    }

    private IEnumerator ExitAfterTeamWipeDelay()
    {
        var remaining = Mathf.Max(0f, teamWipeExitDelay);
        while (remaining > 0f)
        {
            remaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        GameSession.AbandonRaid();
        yield return ExitToMainMenu();
    }

    /// <summary>
    /// 房主会话结束（房主退出战斗或崩溃掉线）：成员端统一退出本局回主菜单。
    ///
    /// 复用 TeamWiped 体验：冻结场景 → 显示提示 → 延迟退出。
    /// 房主端不会收到本事件（EndHostSession 只在成员端触发），
    /// 房主自身的退出流程仍走 ConfirmRetreat / HandleQuitRequested 原路径。
    /// </summary>
    private void HandleHostSessionEnded(string reason)
    {
        if (runFinishedHandled)
            return;

        runFinishedHandled = true;
        LocalPauseScope.Freeze();

        if (deathPanel == null)
            deathPanel = FindObjectOfType<DeathPanelController>();
        if (deathPanel != null)
            deathPanel.ShowAutoExit("房主已离开", reason + "，即将返回主菜单");

        StartCoroutine(ExitAfterTeamWipeDelay());
    }

    /// <summary>
    /// 广播「本局结束」。仅主机生效。
    /// 房主死亡 / 撤退时调用，让所有成员同步退出战斗场景。
    /// </summary>
    public void BroadcastRunFinished(Tower.Netcode.BattleFinishReason reason = Tower.Netcode.BattleFinishReason.HostClosed)
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null && sessionState.IsServer)
            sessionState.MarkRunFinished(reason);
    }

    /// <summary>
    /// 幂等订阅确认面板。拆成独立方法是因为 OnEnable 跑在序列化引用赋值之前
    /// （运行时接线与测试都会遇到），此时 portalConfirmPanel 还是 null，
    /// 必须在真正用到之前补订一次，否则点按钮不会有任何反应。
    ///
    /// 注意不能只靠 <c>subscribedPortalConfirm</c> 布尔量判重：OnEnable 可能对着
    /// 一个「当时非空、之后被换掉」的引用订阅过，OnDisable 又把它退订了，
    /// 于是布尔量停在 true 而真实面板上 0 个订阅者。这里改为记录订阅目标，
    /// 目标变了就重新订阅。
    /// </summary>
    private void SubscribePortalConfirm()
    {
        if (portalConfirmPanel == null)
            return;

        if (subscribedPortalConfirm && subscribedPortalTarget == portalConfirmPanel)
            return;

        UnsubscribePortalConfirm();

        portalConfirmPanel.Confirmed += HandlePortalConfirmed;
        portalConfirmPanel.Cancelled += HandlePortalCancelled;
        subscribedPortalConfirm = true;
        subscribedPortalTarget = portalConfirmPanel;
    }

    private void UnsubscribePortalConfirm()
    {
        if (!subscribedPortalConfirm || subscribedPortalTarget == null)
        {
            subscribedPortalConfirm = false;
            subscribedPortalTarget = null;
            return;
        }

        subscribedPortalTarget.Confirmed -= HandlePortalConfirmed;
        subscribedPortalTarget.Cancelled -= HandlePortalCancelled;
        subscribedPortalConfirm = false;
        subscribedPortalTarget = null;
    }

    /// <summary>
    /// 幂等订阅设置面板。与 <see cref="SubscribePortalConfirm"/> 同理：
    /// OnEnable 可能跑在序列化引用赋值之前，必须在真正用到之前补订一次。
    /// </summary>
    private void SubscribeSettings()
    {
        if (settingsPanel == null)
            return;

        if (subscribedSettings && subscribedSettingsTarget == settingsPanel)
            return;

        UnsubscribeSettings();

        settingsPanel.QuitRequested += HandleQuitRequested;
        subscribedSettings = true;
        subscribedSettingsTarget = settingsPanel;
    }

    private void UnsubscribeSettings()
    {
        if (!subscribedSettings || subscribedSettingsTarget == null)
        {
            subscribedSettings = false;
            subscribedSettingsTarget = null;
            return;
        }

        subscribedSettingsTarget.QuitRequested -= HandleQuitRequested;
        subscribedSettings = false;
        subscribedSettingsTarget = null;
    }

    private void Start()
    {
        state.BeginRun();

        // 延后一帧：联机时 BattleSessionState 的 OnNetworkSpawn 可能晚于本组件的 Start，
        // 直接读 Seed 会拿到默认值 0，导致各端生成不同的层。
        StartCoroutine(StartAfterSessionReady());
    }

    /// <summary>
    /// 等会话就绪后再生成第一层。
    ///
    /// 单机：直接生成。
    /// 联机主机：生成权威种子写入 BattleSessionState，再生成。
    /// 联机客户端：等主机广播的种子到达后再生成，保证各端同一层。
    /// </summary>
    private IEnumerator StartAfterSessionReady()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        if (sessionState == null && networkManager != null && networkManager.IsListening)
        {
            var sessionDeadline = Time.realtimeSinceStartup + 30f;
            while (sessionState == null && Time.realtimeSinceStartup < sessionDeadline)
            {
                yield return null;
                sessionState = Tower.Netcode.BattleSessionState.Instance;
            }

            if (sessionState == null)
            {
                Debug.LogError("[TowerRunManager] 等待 BattleSessionState 超时，本层未生成。");
                yield break;
            }
        }

        if (sessionState == null)
        {
            BuildFloorOnce();
            yield break;
        }

        if (sessionState.IsServer)
        {
            // 主机：种子为 0 时用本机随机源生成一个，写入网络变量广播出去。
            var seed = randomSeed != 0 ? randomSeed : Environment.TickCount;
            sessionState.SetSeed(seed);
            SetRandomSeed(seed);
            BuildFloorOnce();
            yield break;
        }

        // 客户端：等种子。
        if (sessionState.Seed != 0)
        {
            SetRandomSeed(sessionState.Seed);
            BuildFloorOnce();
            yield break;
        }

        sessionState.SeedReady += HandleSeedReady;

        var deadline = Time.realtimeSinceStartup + 30f;
        while (!floorBuilt && Time.realtimeSinceStartup < deadline)
            yield return null;

        sessionState.SeedReady -= HandleSeedReady;

        if (!floorBuilt)
            Debug.LogError("[TowerRunManager] 等待主机种子超时，本层未生成。");
    }

    private void HandleSeedReady(int seed)
    {
        if (floorBuilt)
            return;

        SetRandomSeed(seed);
        BuildFloorOnce();
    }

    private void BuildFloorOnce()
    {
        if (floorBuilt)
            return;

        floorBuilt = true;
        BuildFloor(state.CurrentFloor);

        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null && !sessionState.IsServer)
            HandleFloorChanged(sessionState.CurrentFloor);
    }

    private void Update()
    {
        // 每帧补订传送点事件：客户端由 NGO 同步来的传送点不在本地生成路径里，
        // 只能从静态注册表补订。幂等，已订阅的会跳过。
        SubscribePortals();

        // 主机：补订玩家死亡 / 退出事件，维护存活计数（全员死亡判定）。
        SubscribePlayerLifecycle();

        // 主机：联机传送门由全队站位决定。
        UpdateTeamExtraction();
        UpdateTeamNextFloor();

        if (!floorBuilt || state.IsFinished || portalsSpawned)
            return;

        // 联机客户端：清空检测与传送门生成由主机权威决定。
        // 客户端本地 spawnedEnemies 为空（敌人由 NGO 同步），IsFloorCleared 会恒为 true，
        // 导致开局瞬间错误生成传送门。客户端只等主机广播层数后跟随。
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null && !sessionState.IsServer)
            return;

        clearTimer -= Time.deltaTime;
        if (clearTimer > 0f)
            return;

        clearTimer = Mathf.Max(0.05f, clearCheckInterval);
        if (IsFloorCleared())
            StartCoroutine(SpawnPortalsAfterDelay());
    }

    /// <summary>
    /// 订阅所有存活传送点的事件（含客户端由 NGO 同步来的）。
    ///
    /// 主机本地生成的传送点在 <see cref="SpawnPortal"/> 里已订阅；
    /// 客户端不本地生成，只能从 <see cref="FloorPortal2D.Active"/> 注册表里补订。
    /// 幂等：已订阅的跳过。
    /// </summary>
    private void SubscribePortals()
    {
        var portals = FloorPortal2D.Active;
        for (var i = 0; i < portals.Count; i++)
        {
            var portal = portals[i];
            if (portal == null || subscribedPortals.Contains(portal))
                continue;

            portal.Entered += HandlePortalEntered;
            portal.ExecuteRequested += HandlePortalExecuteRequested;
            subscribedPortals.Add(portal);
        }
    }

    /// <summary>
    /// 主机：补订所有玩家的死亡 / 退出事件，维护存活计数。
    ///
    /// 玩家对象由 NGO 动态生成，无法在生成时订阅，只能每帧从注册表补订（幂等）。
    /// 客户端不参与（存活计数由主机权威维护）。
    /// </summary>
    private void SubscribePlayerLifecycle()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState == null || !sessionState.IsServer)
            return;

        foreach (var player in WorldRegistry.Players)
        {
            if (player == null)
                continue;

            var sync = player.GetComponent<Tower.Netcode.PlayerNetworkSync>();
            if (sync == null || subscribedPlayers.Contains(sync))
                continue;

            sync.PlayerDied += HandlePlayerDiedNetwork;
            sync.PlayerQuit += HandlePlayerQuitNetwork;
            subscribedPlayers.Add(sync);
        }

        // 每次补订后重算一次存活数，保证新加入 / 已移除的玩家都被计入。
        RefreshAliveCount();
    }

    /// <summary>主机：重算存活玩家数并写入会话状态（归零即全员死亡）。</summary>
    private void RefreshAliveCount()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState == null || !sessionState.IsServer)
            return;

        var alive = 0;
        var spawned = 0;
        foreach (var player in WorldRegistry.Players)
        {
            if (player == null)
                continue;

            var sync = player.GetComponent<Tower.Netcode.PlayerNetworkSync>();
            if (sync == null || !sync.IsSpawned)
                continue;

            spawned++;

            if (sync.IsAlive)
                alive++;
        }

        // 玩家对象尚未 Spawn 完成（进场景瞬间）：不写入，避免误判全员死亡。
        // 由 SetAlivePlayerCount 的「-1 未初始化」语义兜底，这里再挡一层更直观。
        if (spawned == 0)
            return;

        sessionState.SetAlivePlayerCount(alive);
    }

    private void HandlePlayerDiedNetwork(ulong clientId)
    {
        RefreshAliveCount();
    }

    private void HandlePlayerQuitNetwork(ulong clientId)
    {
        RefreshAliveCount();
    }

    private void SubscribeDisconnectCallback()
    {
        var manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null || subscribedDisconnect)
            return;

        manager.OnClientDisconnectCallback += HandleClientDisconnected;
        subscribedDisconnect = true;
    }

    private void UnsubscribeDisconnectCallback()
    {
        var manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager != null && subscribedDisconnect)
            manager.OnClientDisconnectCallback -= HandleClientDisconnected;

        subscribedDisconnect = false;
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        var manager = Unity.Netcode.NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
            return;

        StartCoroutine(RefreshAliveCountAfterDisconnect());
    }

    private IEnumerator RefreshAliveCountAfterDisconnect()
    {
        yield return null;
        RefreshAliveCount();
    }

    /// <summary>
    /// 主机：全员撤离聚合判定。
    ///
    /// 规则：所有存活玩家都在同一个撤退传送阵内才触发撤离；死亡观战玩家不阻塞。
    /// 满足条件后启动倒计时（给反悔窗口），倒计时结束仍在阵内则执行撤退。
    /// 单机不参与（单机走传送点的一次性触发路径）。
    /// </summary>
    private void UpdateTeamExtraction()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState == null || !sessionState.IsServer || state.IsFinished)
            return;

        var portal = FindPortal(PortalKind.Retreat);
        if (portal == null)
        {
            extractionCountdown = -1f;
            return;
        }

        if (IsTeamFullyInside(portal))
        {
            if (extractionCountdown < 0f)
            {
                extractionCountdown = extractionCountdownSeconds;
                Debug.Log($"[TowerRunManager] 全员进入撤退阵，开始 {extractionCountdownSeconds} 秒撤离倒计时。");
            }

            extractionCountdown -= Time.deltaTime;
            if (extractionCountdown <= 0f)
            {
                extractionCountdown = -1f;
                Debug.Log("[TowerRunManager] 撤离倒计时结束，执行撤退。");
                Retreat();
            }

            return;
        }

        // 有人离开阵内：取消倒计时，回到等待状态。
        if (extractionCountdown >= 0f)
        {
            extractionCountdown = -1f;
            Debug.Log("[TowerRunManager] 有玩家离开撤退阵，撤离倒计时取消。");
        }
    }

    private void UpdateTeamNextFloor()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState == null || !sessionState.IsServer || state.IsFinished
            || !floorBuilt || lastTeamAdvanceFrame == Time.frameCount)
            return;

        var portal = FindPortal(PortalKind.Next);
        if (portal == null || !IsTeamFullyInside(portal))
            return;

        lastTeamAdvanceFrame = Time.frameCount;
        ExecutePortal(PortalKind.Next);
    }

    /// <summary>取当前场景内指定种类的传送阵。没有则返回 null。</summary>
    private FloorPortal2D FindPortal(PortalKind kind)
    {
        for (var i = 0; i < spawnedPortals.Count; i++)
        {
            var portal = spawnedPortals[i];
            if (portal != null && portal.Kind == kind)
                return portal;
        }

        return null;
    }

    /// <summary>
    /// 判定所有存活玩家是否都在该传送阵内。
    /// 死亡观战玩家不参与判定（不阻塞撤离）。
    /// </summary>
    private static bool IsTeamFullyInside(FloorPortal2D portal)
    {
        var hasAlive = false;

        foreach (var player in WorldRegistry.Players)
        {
            if (player == null)
                continue;

            var sync = player.GetComponent<Tower.Netcode.PlayerNetworkSync>();
            if (sync == null || !sync.IsSpawned)
                continue;

            // 死亡玩家不阻塞撤离。
            if (!sync.IsAlive)
                continue;

            hasAlive = true;

            if (!portal.IsPlayerInside(sync.OwnerClientId))
                return false;
        }

        // 全员死亡时不触发撤离（由全员死亡判定接管）。
        return hasAlive;
    }

    /// <summary>生成指定层：清掉上一层残留 → 实例化房间 → 放玩家 → 生成敌人 → 刷新 HUD。</summary>
    public void BuildFloor(int floor)
    {
        ClearFloor();

        if (roomPrefab != null)
        {
            var parent = roomParent != null ? roomParent : transform;
            currentRoom = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity, parent);
        }

        ResolveRoomBounds();

        // 玩家与敌人都以「房间可活动区域的几何中心」为基准，而不是房间根节点的位置。
        // 房间贴图并不以根节点为中心（实测中心偏移约 (1.25, -4.75)），
        // 用根节点位置会让出生点整体偏向房间一角。
        // 这里算一次存进 floorCenter，本层所有摆放（含延迟生成的传送点）统一读它。
        floorCenter = hasRoomBounds ? roomBounds.center : Vector2.zero;
        WarnIfRootOffCenter();
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null && sessionState.IsServer)
        {
            foreach (var networkPlayer in WorldRegistry.Players)
            {
                if (networkPlayer == null)
                    continue;

                var sync = networkPlayer.GetComponent<Tower.Netcode.PlayerNetworkSync>();
                if (sync != null && sync.IsSpawned)
                    sync.TeleportTo(floorCenter);
            }
        }
        else if (sessionState == null)
        {
            MovePlayerTo(floorCenter);
        }

        if (cameraFollow != null)
        {
            // 房间是运行时实例化的，相机上的 roomBounds 引用必须每层重新指向新房间，
            // 否则相机取不到包围盒，视野会跑出房间贴图之外。
            if (currentRoom != null)
                cameraFollow.SetRoomBounds(currentRoom.transform);

            cameraFollow.CaptureRoomBounds();
        }

        var plan = FloorGenerator.Generate(floor, config, random);
        SpawnEnemies(plan, floorCenter);
        SpawnObstacles(floorCenter);

        if (statusPanel != null)
            statusPanel.SetLevel(floor);
    }

    /// <summary>
    /// 从房间的碰撞边界求可活动区域。优先取 <c>CollisionBounds</c> 下所有 <see cref="BoxCollider2D"/> 的并集，
    /// 因为那才是玩家与敌人真正被挡住的地方；没有碰撞体时回退到渲染器包围盒。
    ///
    /// 注意：边界墙是「一圈有厚度的碰撞体」，直接取并集会得到墙的**外沿**，
    /// 比真正能走的区域每边大出约一个墙厚 + 贴图留白（实测大 2 个单位）。
    /// 因此这里取的是「内沿」——把每面墙朝房间中心的那一侧作为边界。
    /// </summary>
    private void ResolveRoomBounds()
    {
        hasRoomBounds = false;
        roomBounds = Rect.zero;

        if (currentRoom == null)
            return;

        var boundsRoot = currentRoom.transform.Find("CollisionBounds");
        var source = boundsRoot != null ? boundsRoot : currentRoom.transform;

        var colliders = source.GetComponentsInChildren<BoxCollider2D>();
        if (colliders.Length > 0)
        {
            roomBounds = ResolveInnerBounds(colliders);
            hasRoomBounds = true;
            return;
        }

        var renderers = source.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        var rmin = (Vector2)renderers[0].bounds.min;
        var rmax = (Vector2)renderers[0].bounds.max;
        for (var i = 1; i < renderers.Length; i++)
        {
            rmin = Vector2.Min(rmin, renderers[i].bounds.min);
            rmax = Vector2.Max(rmax, renderers[i].bounds.max);
        }

        roomBounds = Rect.MinMaxRect(rmin.x, rmin.y, rmax.x, rmax.y);
        hasRoomBounds = true;
    }

    /// <summary>
    /// 房间根节点与几何中心不一致时给出提示。这是「摆放基准必须用 floorCenter」的防线：
    /// 一旦有人误用 <c>currentRoom.transform.position</c>，日志会立刻指出来。
    /// 只在 Editor 下打，避免真机每层刷一条日志。
    /// </summary>
    private void WarnIfRootOffCenter()
    {
#if UNITY_EDITOR
        if (!hasRoomBounds || currentRoom == null)
            return;

        var drift = (roomBounds.center - (Vector2)currentRoom.transform.position).magnitude;
        if (drift > 0.01f)
        {
            Debug.Log($"[TowerRunManager] 房间根节点与几何中心偏移 {drift:F2}，" +
                      "所有摆放逻辑必须用 floorCenter 而非 currentRoom.transform.position。", this);
        }
#endif
    }

    /// <summary>
    /// 求一圈边界墙围出的**内部**可活动区域。
    /// 做法：先取所有碰撞体的总包围盒（外沿），再对每一面墙，
    /// 用「朝房间中心的那一侧」把外沿往里收。
    ///
    /// 注意不能假设墙一定贴着外沿：实测房间的上下墙比左右墙长得多
    /// （Top/Bottom 跨 x -6.50~7.05，而 Left/Right 只在 x -4.61 / 5.21），
    /// 因此外沿由最长的墙决定，左右墙并不在外沿上。
    /// 这里改为「按薄厚判定墙的朝向，再取朝中心的那一面」，
    /// 不依赖墙与外沿对齐。
    /// </summary>
    private static Rect ResolveInnerBounds(BoxCollider2D[] colliders)
    {
        var outerMin = (Vector2)colliders[0].bounds.min;
        var outerMax = (Vector2)colliders[0].bounds.max;
        for (var i = 1; i < colliders.Length; i++)
        {
            outerMin = Vector2.Min(outerMin, colliders[i].bounds.min);
            outerMax = Vector2.Max(outerMax, colliders[i].bounds.max);
        }

        var center = (outerMin + outerMax) * 0.5f;

        var innerMin = outerMin;
        var innerMax = outerMax;

        // 外沿总跨度，用于判断「薄」的阈值。
        var spanX = outerMax.x - outerMin.x;
        var spanY = outerMax.y - outerMin.y;
        var thinX = spanX * 0.25f;
        var thinY = spanY * 0.25f;

        foreach (var collider in colliders)
        {
            var b = collider.bounds;
            var size = b.size;

            // 竖向薄墙（左右墙）：厚度在 X 轴，跨度在 Y 轴。
            // 取「朝房间中心的那一面」作为内边界，不要求它贴着外沿。
            if (size.x <= thinX && size.y > thinY)
            {
                if (b.center.x < center.x)
                    innerMin.x = Mathf.Max(innerMin.x, b.max.x);
                else
                    innerMax.x = Mathf.Min(innerMax.x, b.min.x);
            }

            // 横向薄墙（上下墙）：厚度在 Y 轴，跨度在 X 轴。
            if (size.y <= thinY && size.x > thinX)
            {
                if (b.center.y < center.y)
                    innerMin.y = Mathf.Max(innerMin.y, b.max.y);
                else
                    innerMax.y = Mathf.Min(innerMax.y, b.min.y);
            }
        }

        // 内沿反了（房间异常小或墙互相穿插）时退回外沿，避免出现负尺寸。
        if (innerMin.x >= innerMax.x || innerMin.y >= innerMax.y)
            return Rect.MinMaxRect(outerMin.x, outerMin.y, outerMax.x, outerMax.y);

        return Rect.MinMaxRect(innerMin.x, innerMin.y, innerMax.x, innerMax.y);
    }

    /// <summary>本层敌人是否已全部清空。虫群（母巢召唤）也是 IDamageable，必须一并计入。</summary>
    public bool IsFloorCleared()
    {
        foreach (var enemy in spawnedEnemies)
        {
            if (enemy == null)
                continue;

            var damageable = enemy.GetComponent<IDamageable>();
            if (damageable != null && !damageable.IsDead)
                return false;
        }

        // 母巢召唤的虫群不在 spawnedEnemies 里，单独检查注册表。
        foreach (var damageable in WorldRegistry.Damageables)
        {
            if (damageable is SwarmChaser2D swarm && !swarm.IsDead)
                return false;
        }

        return true;
    }

    /// <summary>单机玩家踩到传送点后弹确认窗；联机由主机轮询全队站位。</summary>
    public void HandlePortalEntered(PortalKind kind)
    {
        if (state.IsFinished || Tower.Netcode.BattleSessionState.Instance != null)
            return;

        // 没有接线确认面板时退回旧行为（直接执行），避免面板缺失导致传送点完全失效。
        if (portalConfirmPanel == null)
        {
            HandlePortalExecuteRequested(kind);
            return;
        }

        // OnEnable 可能跑在引用赋值之前，这里补订一次（幂等）。
        SubscribePortalConfirm();

        pendingPortalKind = kind;

        // 弹窗期间冻结本地玩家：单机下世界本来就没暂停（旧行为），
        // 这里只保证玩家不会在「确认 / 取消」之间继续走动、把传送点踩乱。
        LocalPauseScope.Freeze();
        portalConfirmPanel.Show(kind);
    }

    /// <summary>单机确认后执行传送；联机请求不参与主机的全队判定。</summary>
    private void HandlePortalExecuteRequested(PortalKind kind)
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;

        // 联机传送由主机按全队站位判定，不接受单人确认或旧客户端请求。
        if (sessionState != null)
            return;

        ExecutePortal(kind);
    }

    /// <summary>确认面板点了「确认」：执行挂起的传送。</summary>
    private void HandlePortalConfirmed()
    {
        if (portalConfirmPanel != null)
            portalConfirmPanel.Hide();

        LocalPauseScope.Unfreeze();

        if (pendingPortalKind == null)
            return;

        var kind = pendingPortalKind.Value;
        pendingPortalKind = null;
        HandlePortalExecuteRequested(kind);
    }

    /// <summary>确认面板点了「取消」：关掉弹窗，传送点重新可用。</summary>
    private void HandlePortalCancelled()
    {
        if (portalConfirmPanel != null)
            portalConfirmPanel.Hide();

        LocalPauseScope.Unfreeze();

        pendingPortalKind = null;

        // 传送点触发时已把自己标记为 consumed 并关掉碰撞体，取消后必须复位，
        // 否则玩家离开再回来也踩不动了。
        // 客户端 spawnedPortals 为空（传送点由 NGO 同步），改从静态注册表复位。
        var portals = FloorPortal2D.Active;
        for (var i = 0; i < portals.Count; i++)
        {
            if (portals[i] != null)
                portals[i].ResetConsumed();
        }
    }

    /// <summary>真正执行传送：撤退结算或进入下一层。</summary>
    private void ExecutePortal(PortalKind kind)
    {
        if (state.IsFinished)
            return;

        // 换层 / 撤退都要恢复本地玩家：确认弹窗期间被冻结，这里兜底解除，
        // 避免任何路径下把「本机玩家不能动」的状态带进下一层。
        LocalPauseScope.Unfreeze();

        if (kind == PortalKind.Retreat)
        {
            Retreat();
            return;
        }

        state.AdvanceFloor();

        // 联机主机：把新层数广播出去，客户端据此同步换层。
        // 客户端本地不执行换层（ExecutePortal 只在主机被调用，见 HandlePortalEntered 的联机分支）。
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null && sessionState.IsServer)
            sessionState.SetCurrentFloor(state.CurrentFloor);

        BuildFloor(state.CurrentFloor);
    }

    /// <summary>撤退：结束本局并弹出结算面板。</summary>
    public void Retreat()
    {
        if (state.IsFinished)
            return;

        state.Finish();
        ClearFloor();

        // 联机：主机撤退即本局结束，广播给所有成员一起退出。
        BroadcastRunFinished(Tower.Netcode.BattleFinishReason.Extracted);

        if (resultPanel != null)
            resultPanel.Show(BuildResultEntries(), TotalLootValue(), state.CurrentFloor);
    }

    /// <summary>本局中止（玩家死亡）。只标记结束，丢弃局内携带由 PlayerDeathFlow2D 负责。</summary>
    public void AbortRun()
    {
        state.Finish();

        // 联机：主机死亡即本局结束，广播给所有成员一起退出。
        BroadcastRunFinished();

        // 死亡流程会自行决定暂停语义（单机 timeScale / 联机本地冻结），
        // 这里只解除「传送确认弹窗」可能留下的本地冻结，避免两者叠加。
        LocalPauseScope.Unfreeze();
    }

    /// <summary>把本局战利品清单转成结算面板的展示数据。</summary>
    public IReadOnlyList<TowerResultEntry> BuildResultEntries()
    {
        var entries = new List<TowerResultEntry>();
        foreach (var instance in GameSession.LocalPlayer.Raid.Loot)
        {
            var definition = instance != null ? instance.Definition : null;
            if (definition == null)
                continue;

            entries.Add(new TowerResultEntry
            {
                DisplayName = definition.DisplayName,
                Value = definition.Value,
                Icon = definition.Icon,
            });
        }

        return entries;
    }

    /// <summary>本局战利品的总价值。</summary>
    public int TotalLootValue()
    {
        var total = 0;
        foreach (var instance in GameSession.LocalPlayer.Raid.Loot)
        {
            if (instance != null && instance.Definition != null)
                total += instance.Definition.Value;
        }

        return total;
    }

    /// <summary>结算面板确认后回主菜单。</summary>
    public void ConfirmRetreat()
    {
        // 正常撤离：把局内携带与装备并入局外仓库并落盘，这才算「带出来了」。
        // 死亡路径不走这里，由 PlayerDeathFlow2D 丢弃局内携带，因此「只有正常撤离才带走」由分流保证。
        var session = Tower.Network.NetworkSession.Instance;
        var payload = session != null && session.CurrentRoom != null
            ? BuildExtractionPayload() : null;
        var overflow = GameSession.ExtractRaid();
        if (overflow > 0)
            Debug.LogWarning($"[TowerRunManager] 仓库已满，有 {overflow} 件战利品未能带出。");

        // 本地先结算；上报只是尽力而为，服务端未提供路由时不会落库。
        if (payload != null)
            StartCoroutine(SubmitExtractionRoutine(session, payload));

        // 死亡流程会把 timeScale 置 0 暂停游戏；这里兜底恢复，避免任何路径下把暂停状态带进主菜单。
        LocalPauseScope.Unfreeze();
        Time.timeScale = 1f;
        StartCoroutine(ExitToMainMenu());
    }

    /// <summary>尽力提交撤离前快照；失败不阻塞本地结算。</summary>
    private IEnumerator SubmitExtractionRoutine(Tower.Network.NetworkSession session,
        Tower.Network.ExtractionRequestDto body)
    {
        yield return session.SubmitExtraction(body, null, error =>
            Debug.LogWarning($"[TowerRunManager] 撤离上报失败：{error}"));
    }

    /// <summary>把本局战利品清单转成上报 DTO。</summary>
    private Tower.Network.ExtractionRequestDto BuildExtractionPayload()
    {
        var loot = GameSession.LocalPlayer.Raid.Loot;
        var items = new System.Collections.Generic.List<Tower.Network.ExtractionItemDto>(loot.Count);

        foreach (var instance in loot)
        {
            if (instance == null || instance.Definition == null)
                continue;

            items.Add(new Tower.Network.ExtractionItemDto
            {
                definition_id = instance.Definition.Id,
                durability = instance.CurrentDurability,
            });
        }

        return new Tower.Network.ExtractionRequestDto
        {
            floor = state.CurrentFloor,
            items = items.ToArray(),
        };
    }

    private void HandlePlayerDied()
    {
        // 联机：死亡不结束本局，转观战（由 PlayerDeathFlow2D 处理）。
        // 若这里 AbortRun，房主一死就会广播本局结束，全队被踢回主菜单 ——
        // 表现为「观战没生效、房主死亡直接退出」。
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null)
            return;

        AbortRun();
    }

    /// <summary>
    /// 设置面板里确认「退出游戏」：丢弃本局携带与装备槽，返回主菜单。
    /// 与死亡路径同规则（<see cref="GameSession.AbandonRaid"/>），局外仓库不受影响。
    /// </summary>
    private void HandleQuitRequested()
    {
        var sessionState = Tower.Netcode.BattleSessionState.Instance;

        // 联机客户端：先通知主机移除本端角色，再走本地退出。
        // 不通知的话，主机侧会残留一个不动的幽灵角色，且仍把本端算进撤离判定。
        if (sessionState != null && !sessionState.IsServer)
        {
            var sync = GetComponentInChildren<Tower.Netcode.PlayerNetworkSync>();
            if (sync != null)
                sync.RequestQuitServerRpc();
        }

        // 先标记本局结束，避免退出过程中残留的 Update / 传送点逻辑继续跑。
        AbortRun();

        GameSession.AbandonRaid();

        // 设置面板打开时可能冻结过本地玩家；这里兜底恢复，避免把暂停状态带进主菜单。
        LocalPauseScope.Unfreeze();
        Time.timeScale = 1f;
        StartCoroutine(ExitToMainMenu());
    }

    /// <summary>
    /// 退出房间后再切回主菜单。
    ///
    /// 必须先退出房间：NetworkSession 是 DontDestroyOnLoad，只切场景不会断开信令，
    /// 服务端会一直认为玩家还在房间内 —— 下次建房/进房报「你已在其他房间中」，
    /// 且 UI 会显示「仍在房间中」的幽灵房间。
    /// </summary>
    private IEnumerator ExitToMainMenu()
    {
        if (exitingBattle)
            yield break;

        exitingBattle = true;

        var session = Tower.Network.NetworkSession.Instance;
        if (session != null && session.CurrentRoom != null)
            yield return session.LeaveOrCloseRoom(null);

        // 必须经协调器关闭：除了停 NGO 释放 7777 端口，还要把状态机
        // 复位到 Idle 并清空成员表。直接调 NetworkManager.Shutdown 会留下
        // InGame 状态 —— 下次点出发被静默忽略，表现为「再次联机无响应」。
        Tower.Netcode.MultiplayerCoordinator.Instance?.Shutdown();

        LocalPauseScope.Unfreeze();
        LocalPauseScope.SetSpectating(false);
        LocalPauseScope.UnfreezeWorld();
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private IEnumerator SpawnPortalsAfterDelay()
    {
        portalsSpawned = true;
        yield return new WaitForSeconds(Mathf.Max(0f, portalSpawnDelay));

        if (state.IsFinished)
            yield break;

        // 必须用 floorCenter 而不是 currentRoom.transform.position：
        // 房间根节点不在几何中心，用根节点会让传送点偏离 SpawnObstacles 预留的避让位，
        // 导致掩体正好压在传送点上、把玩家挡在外面。
        var center = floorCenter;
        var isRetreatFloor = FloorGenerator.IsRetreatFloor(state.CurrentFloor, config != null ? config.RetreatEveryFloors : 0);

        if (isRetreatFloor && retreatPortalPrefab != null)
        {
            SpawnPortal(retreatPortalPrefab, center + new Vector2(-portalOffsetX, 0f));
            SpawnPortal(nextPortalPrefab, center + new Vector2(portalOffsetX, 0f));
            yield break;
        }

        // 传送点不能生成在房间中心：玩家每层都被放在中心，若传送点也落在中心会立刻触发，
        // 导致「清空后瞬间跳层」。这里沿 X 轴偏移，玩家需要主动走过去才算进入。
        SpawnPortal(nextPortalPrefab, center + new Vector2(portalOffsetX, 0f));
    }

    private void SpawnPortal(FloorPortal2D prefab, Vector2 position)
    {
        if (prefab == null)
            return;

        var portal = Instantiate(prefab, position, Quaternion.identity);
        portal.Entered += HandlePortalEntered;
        portal.ExecuteRequested += HandlePortalExecuteRequested;
        subscribedPortals.Add(portal);
        spawnedPortals.Add(portal);

        // 联机主机：把传送点注册进 NGO，由 NGO 自动同步到所有客户端。
        // 客户端本地不生成传送点（Update 已被 IsServer 守卫拦下），只接收同步。
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        var netObj = portal.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null && sessionState != null && sessionState.IsServer)
            netObj.Spawn(true);
    }

    private void SpawnEnemies(FloorPlan plan, Vector2 center)
    {
        // 联机客户端：敌人由主机权威生成并通过 NGO 自动同步过来，
        // 客户端本地不生成（否则各端各有一份怪，且与主机广播的对象重复）。
        // 单机时 BattleSessionState 为 null，走本地生成分支。
        var sessionState = Tower.Netcode.BattleSessionState.Instance;
        if (sessionState != null && !sessionState.IsServer)
            return;

        for (var i = 0; i < plan.Enemies.Length; i++)
        {
            var prefab = plan.Enemies[i];
            if (prefab == null)
                continue;

            var angle = 360f * i / Mathf.Max(1, plan.Enemies.Length);
            var radius = NextFloat(enemySpawnRadiusMin, enemySpawnRadiusMax);
            var offset = Quaternion.Euler(0f, 0f, angle) * (Vector3)(Vector2.right * radius);
            var position = ResolveSpawnPosition(center + (Vector2)offset);

            var enemy = Instantiate(prefab, position, Quaternion.identity);

            // 先 SetActive(false) 再缩放再 SetActive(true)：Awake 在 SetActive(true) 时才跑，
            // 因此缩放先生效，EnemyBrain2D 会用缩放后的血量创建 health。
            enemy.SetActive(false);
            var scaler = enemy.GetComponent<EnemyStatScaler>();
            if (scaler == null)
                scaler = enemy.AddComponent<EnemyStatScaler>();
            scaler.Apply(plan.Floor, config);

            // 敌人 prefab 里的巡逻点是写死的世界坐标，只适合手工摆放的场景。
            // 运行时生成的敌人必须按出生点重新指定，否则会朝固定坐标走、穿过房间甚至撞墙。
            // 巡逻端点同样要钳进房间，否则敌人会走到墙里贴着墙抖。
            var brain = enemy.GetComponent<EnemyBrain2D>();
            if (brain != null)
            {
                brain.SetPatrolAround(position, enemyPatrolHalfWidth);
                brain.SetPatrolPoints(
                    ResolveSpawnPosition(brain.PatrolPointA),
                    ResolveSpawnPosition(brain.PatrolPointB));
            }

            // 掉落池与层数在运行时注入：敌人 prefab 上不再硬编码掉落表，
            // 掉落概率由全局掉落池按当前层数解析（层数越高，高品质概率越高）。
            // 随机源一并注入，使掉落掷点也走本局种子（联机时各端一致）。
            var dropper = enemy.GetComponent<LootDropper>();
            if (dropper != null)
                dropper.Configure(plan.LootPool, plan.Floor, random);

            enemy.SetActive(true);

            // 联机主机：把敌人注册进 NGO，由 NGO 自动同步到所有客户端。
            // 单机时 NetworkManager 未启动，netObj 为 null 或 IsServer 为 false，
            // 但也没有会话，敌人留在本地即可。
            var netObj = enemy.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && sessionState != null && sessionState.IsServer)
                netObj.Spawn(true);

            spawnedEnemies.Add(enemy);
        }
    }

    /// <summary>
    /// 生成本层掩体。必须在敌人就位之后调用，这样掩体才能避开敌人出生点。
    /// 传送点此时还没生成（要等清空后才出），因此按「传送点可能出现的位置」预留空间，
    /// 否则掩体会正好压在传送点上、把玩家挡在外面。
    /// </summary>
    private void SpawnObstacles(Vector2 center)
    {
        var placer = ResolveObstaclePlacer();
        if (placer == null)
            return;

        var portalPositions = new List<Vector2>();
        if (nextPortalPrefab != null)
            portalPositions.Add(center + new Vector2(portalOffsetX, 0f));

        // 撤退层会同时出「下一层」与「撤退」两个传送点，位置与 SpawnPortalsAfterDelay 保持一致。
        if (retreatPortalPrefab != null
            && FloorGenerator.IsRetreatFloor(state.CurrentFloor, config != null ? config.RetreatEveryFloors : 0))
        {
            portalPositions.Add(center + new Vector2(-portalOffsetX, 0f));
        }

        var enemyPositions = new List<Vector2>();
        foreach (var enemy in spawnedEnemies)
        {
            if (enemy != null)
                enemyPositions.Add(enemy.transform.position);
        }

        placer.Place(
            roomBounds,
            center,
            portalPositions,
            enemyPositions,
            state.CurrentFloor,
            random);
    }

    private ObstaclePlacer ResolveObstaclePlacer()
    {
        if (obstaclePlacer == null)
            obstaclePlacer = GetComponent<ObstaclePlacer>();

        return obstaclePlacer;
    }

    /// <summary>
    /// 把出生点钳制进房间可活动区域（再内缩 <see cref="enemySpawnMargin"/>），
    /// 避免敌人出生在墙体内或房间外。未解析出房间边界时原样返回。
    /// </summary>
    public Vector2 ResolveSpawnPosition(Vector2 desired)
    {
        if (!hasRoomBounds)
            return desired;

        var margin = Mathf.Max(0f, enemySpawnMargin);
        var xMin = roomBounds.xMin + margin;
        var xMax = roomBounds.xMax - margin;
        var yMin = roomBounds.yMin + margin;
        var yMax = roomBounds.yMax - margin;

        // 房间比两倍 margin 还小时，退化为取房间中心，避免 Min > Max 导致钳制结果乱跳。
        var x = xMin <= xMax ? Mathf.Clamp(desired.x, xMin, xMax) : roomBounds.center.x;
        var y = yMin <= yMax ? Mathf.Clamp(desired.y, yMin, yMax) : roomBounds.center.y;

        return new Vector2(x, y);
    }

    /// <summary>
    /// 用本局随机源取 [min, max) 区间内的浮点数。
    /// 不用 <c>UnityEngine.Random</c>：那是全局静态状态，无法按局固定种子，
    /// 联机时各端会算出不同的出生半径。
    /// </summary>
    private float NextFloat(float min, float max)
    {
        EnsureRandom();
        return min + (float)random.NextDouble() * (max - min);
    }

    private void MovePlayerTo(Vector2 position)
    {
        if (player == null)
            return;

        var body = player.GetComponent<Rigidbody2D>();
        if (body != null)
            body.position = position;

        player.position = new Vector3(position.x, position.y, player.position.z);
    }

    /// <summary>
    /// 清掉本层地上所有未拾取的掉落物。掉落物是 <see cref="LootDropper"/> 在敌人死亡时
    /// 直接 Instantiate 的，不归 Manager 管，因此只能按类型全场景扫。
    /// 已拾取的掉落物已被销毁，不会重复处理。
    /// </summary>
    private void ClearDroppedItems()
    {
        foreach (var dropped in WorldRegistry.DroppedItems)
        {
            if (dropped != null)
                DestroyNetworkAware(dropped.gameObject);
        }
    }

    /// <summary>
    /// 清掉本层残留的敌人尸体。敌人死亡后对象仍在（要播死亡动画），
    /// 只有 <see cref="EnemyVisual2D.DeathFinished"/> 为 true 时才算「尸体」。
    /// 未播完死亡动画的也一并清掉 —— 换层时玩家已经看不到这一层了，没必要等动画。
    /// </summary>
    private void ClearCorpses()
    {
        foreach (var damageable in WorldRegistry.Damageables)
        {
            if (damageable is EnemyBrain2D brain && brain.IsDead)
                DestroyNetworkAware(brain.gameObject);
        }
    }

    /// <summary>
    /// 销毁对象，但对 NGO 已 Spawn 的对象做区分处理。
    ///
    /// 客户端不能直接 <c>Destroy</c> 已 Spawn 的 NetworkObject —— NGO 会报
    /// 「Destroy a spawned NetworkObject on a non-host client is not valid」，
    /// 并导致网络状态错乱（实测会引发断连）。
    /// 客户端应只清本地引用，实际销毁由主机 Despawn 后经 NGO 同步完成。
    /// </summary>
    private static void DestroyNetworkAware(GameObject target)
    {
        if (target == null)
            return;

        var netObj = target.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            // 主机：走 Despawn，NGO 同步移除到所有端。
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm != null && nm.IsServer)
            {
                netObj.Despawn(true);
                return;
            }

            // 客户端：不 Destroy（非法），等主机 Despawn 后 NGO 自动移除。
            return;
        }

        Destroy(target);
    }

    private void ClearFloor()
    {
        foreach (var portal in spawnedPortals)
        {
            if (portal != null)
            {
                portal.Entered -= HandlePortalEntered;
                portal.ExecuteRequested -= HandlePortalExecuteRequested;
                subscribedPortals.Remove(portal);
                DestroyNetworkAware(portal.gameObject);
            }
        }

        spawnedPortals.Clear();
        subscribedPortals.Clear();

        foreach (var enemy in spawnedEnemies)
        {
            if (enemy != null)
                DestroyNetworkAware(enemy);
        }

        spawnedEnemies.Clear();

        // 母巢召唤的虫群不在 spawnedEnemies 里，换层时一并清掉。
        var damageables = new List<IDamageable>(WorldRegistry.Damageables);
        foreach (var damageable in damageables)
        {
            if (damageable is SwarmChaser2D swarm)
                DestroyNetworkAware(swarm.gameObject);
        }

        ClearDroppedItems();
        ClearCorpses();

        // 掩体挂在 placer 下，由它自己清；这里不直接 Destroy，避免与 placer 的列表失同步。
        if (obstaclePlacer != null)
            obstaclePlacer.Clear();

        if (currentRoom != null)
        {
            Destroy(currentRoom);
            currentRoom = null;
        }

        portalsSpawned = false;
        clearTimer = 0f;
    }
}
