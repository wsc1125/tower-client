using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家死亡结算：暂停游戏 → 弹出「你已死亡」提示 → 玩家确认后丢弃本局携带并回主菜单。
/// 局外仓库与局外装备不受影响 —— 这是「死亡只丢局内携带」规则的落点。
///
/// 丢弃放在玩家确认之后执行，避免死亡瞬间到确认之间经由变更存档把掉落物留下来。
///
/// 暂停分两种语义（见 <see cref="LocalPauseScope"/>）：
/// ① 单机：<see cref="Time.timeScale"/> = 0，整个世界停住，行为与改造前完全一致；
/// ② 联机：只冻结本地玩家，世界照常推进（否则房主一死全队都动不了）。
/// 单机下 timeScale 为 0，因此本组件所有等待都必须用 unscaledTime，否则协程会永远停住。
/// </summary>
public sealed class PlayerDeathFlow2D : MonoBehaviour
{
    [SerializeField] private PlayerHealth2D playerHealth;

    [Tooltip("死亡提示面板。留空则跳过提示，直接按延迟返回主菜单。")]
    [SerializeField] private DeathPanelController deathPanel;

    [Tooltip("爬塔流程管理器。死亡时标记本局结束。留空则跳过。")]
    [SerializeField] private TowerRunManager towerRun;

    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Tooltip("无提示面板时的兜底延迟（秒）。有面板时以玩家点击为准。")]
    [SerializeField] private float delayBeforeExit = 1.5f;

    [Tooltip("联机模式。勾选后死亡只冻结本地玩家，不冻结世界（避免房主一死全队停住）。\n" +
             "单机保持不勾选，行为与改造前完全一致。")]
    [SerializeField] private bool multiplayerMode;

    private bool deathHandled;
    private bool spectating;

    private void Awake()
    {
        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth2D>();
    }

    private void OnEnable()
    {
        if (playerHealth == null)
            return;

        playerHealth.Died += HandleDeath;
        playerHealth.Revived += HandleRevived;

        // 订阅前玩家可能已经死亡（例如本组件晚于死亡被启用）。
        if (playerHealth.IsDead)
            HandleDeath();
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= HandleDeath;
            playerHealth.Revived -= HandleRevived;
        }

        if (deathPanel != null)
            deathPanel.Confirmed -= HandleConfirmed;

        // 只释放本流程持有的观战冻结，避免远端副本销毁时影响本地玩家。
        ExitSpectate();
    }

    /// <summary>
    /// 复活回调：重置「已处理死亡」标记，让复活后再次死亡能重新进入观战流程。
    /// 否则 <see cref="deathHandled"/> 停在 true，再次死亡不会通知主机更新存活计数。
    /// </summary>
    private void HandleRevived()
    {
        deathHandled = false;
        ExitSpectate();
    }

    private void HandleDeath()
    {
        if (deathHandled)
            return;

        // 联机死亡由 owner 本机响应 syncedAlive，禁止主机上的远端副本处理本地 Died 事件。
        if (IsOnlineSession())
            return;

        deathHandled = true;

        // 单机：原行为（标记本局结束 + 弹结算）。
        if (towerRun != null)
            towerRun.AbortRun();

        // 单机暂停整个世界：timeScale 归零 + 世界冻结标记（供敌人 AI / 相机等读取）。
        // 联机不走这里 —— 联机死亡只冻结本机玩家，世界照常推进。
        Time.timeScale = 0f;
        LocalPauseScope.FreezeWorld();

        if (deathPanel != null)
        {
            deathPanel.Confirmed += HandleConfirmed;
            deathPanel.Show();
            return;
        }

        // 没有提示面板时退回旧行为：延迟后自动返回主菜单。
        StartCoroutine(ExitAfterDelay());
    }

    /// <summary>owner 本机进入联机观战，只冻结本地操作。</summary>
    public void EnterSpectate()
    {
        if (!IsOnlineSession() || spectating)
            return;

        spectating = true;
        LocalPauseScope.SetSpectating(true);
    }

    /// <summary>owner 本机退出观战并恢复操作。</summary>
    public void ExitSpectate()
    {
        if (!spectating)
            return;

        spectating = false;
        LocalPauseScope.SetSpectating(false);
    }

    private bool IsOnlineSession()
    {
        return multiplayerMode || Tower.Netcode.BattleNetworkBinder.IsOnlineSession || IsNetworkActive();
    }

    /// <summary>NGO 是否正在运行（Host 或 Client）。单机返回 false。</summary>
    private static bool IsNetworkActive()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        return nm != null && nm.IsListening;
    }

    /// <summary>玩家点了「返回主菜单」。</summary>
    private void HandleConfirmed()
    {
        if (deathPanel != null)
            deathPanel.Confirmed -= HandleConfirmed;

        AbandonRaid();
        ResumeWorld();

        // 联机：房主确认死亡后广播本局结束，所有端（含成员）统一切回主菜单。
        // 否则房主切走后成员会卡在战斗场景、玩家对象被销毁后「角色消失」。
        if (towerRun != null)
            towerRun.BroadcastRunFinished();

        StartCoroutine(ExitToMainMenu());
    }

    private IEnumerator ExitAfterDelay()
    {
        // 单机下已暂停，必须用 unscaledTime 计时。
        var remaining = Mathf.Max(0f, delayBeforeExit);
        while (remaining > 0f)
        {
            remaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        AbandonRaid();
        ResumeWorld();

        // 与确认路径一致：联机下广播本局结束，所有端统一切回主菜单。
        if (towerRun != null)
            towerRun.BroadcastRunFinished();

        yield return ExitToMainMenu();
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
        var session = Tower.Network.NetworkSession.Instance;
        if (session != null && session.CurrentRoom != null)
            yield return session.LeaveOrCloseRoom(null);

        // 死亡退出同样要经协调器关闭 NGO：本路径原来不关 NGO，
        // NetworkManager（DontDestroyOnLoad）会一直监听 7777 端口、
        // 协调器状态卡在 InGame —— 下次建房出发必然失败。
        Tower.Netcode.MultiplayerCoordinator.Instance?.Shutdown();

        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>解除暂停。单机恢复 timeScale 与世界冻结，联机解除本地冻结。</summary>
    private void ResumeWorld()
    {
        LocalPauseScope.Unfreeze();
        LocalPauseScope.UnfreezeWorld();
        Time.timeScale = 1f;
    }

    private void AbandonRaid()
    {
        // 只清局内携带与战利品，局外仓库原样保留。
        GameSession.AbandonRaid();

        // 死亡结算为强制上传点：装备清空必须立刻落服务端，防止「死后回档」。
        Tower.Network.AssetSyncService.Instance?.FlushNow();
    }
}
