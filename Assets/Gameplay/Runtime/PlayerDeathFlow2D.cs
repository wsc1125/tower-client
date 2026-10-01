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

        // 订阅前玩家可能已经死亡（例如本组件晚于死亡被启用）。
        if (playerHealth.IsDead)
            HandleDeath();
    }

    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.Died -= HandleDeath;

        if (deathPanel != null)
            deathPanel.Confirmed -= HandleConfirmed;

        // 组件被禁用时兜底解除冻结，避免把「本机玩家不能动」的状态带出本场景。
        LocalPauseScope.Unfreeze();
    }

    private void HandleDeath()
    {
        if (deathHandled)
            return;

        deathHandled = true;

        // 先标记本局结束，避免残留到下一局。
        if (towerRun != null)
            towerRun.AbortRun();

        // 暂停：单机冻结整个世界，联机只冻结本地玩家。
        // 联机判定以运行时会话为准（prefab 被单机/联机共用，不能只靠 Inspector 开关）。
        if (multiplayerMode || Tower.Netcode.NetworkGameBootstrap.IsOnlineSession)
            LocalPauseScope.Freeze();
        else
            Time.timeScale = 0f;

        if (deathPanel != null)
        {
            deathPanel.Confirmed += HandleConfirmed;
            deathPanel.Show();
            return;
        }

        // 没有提示面板时退回旧行为：延迟后自动返回主菜单。
        StartCoroutine(ExitAfterDelay());
    }

    /// <summary>玩家点了「返回主菜单」。</summary>
    private void HandleConfirmed()
    {
        if (deathPanel != null)
            deathPanel.Confirmed -= HandleConfirmed;

        AbandonRaid();
        ResumeWorld();
        SceneManager.LoadScene(mainMenuSceneName);
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
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>解除暂停。单机恢复 timeScale，联机解除本地冻结。</summary>
    private void ResumeWorld()
    {
        LocalPauseScope.Unfreeze();
        Time.timeScale = 1f;
    }

    private void AbandonRaid()
    {
        // 只清局内携带与战利品，局外仓库与装备原样保留。
        GameSession.AbandonRaid();
    }
}
