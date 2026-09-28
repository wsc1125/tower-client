using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家死亡结算：暂停游戏 → 弹出「你已死亡」提示 → 玩家确认后丢弃本局携带并回主菜单。
/// 局外仓库与局外装备不受影响 —— 这是「死亡只丢局内携带」规则的落点。
///
/// 丢弃放在玩家确认之后执行，避免死亡瞬间到确认之间经由变更存档把掉落物留下来。
/// 暂停用 <see cref="Time.timeScale"/> = 0，因此本组件所有等待都必须用 unscaledTime，
/// 否则协程会永远停住。
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
    }

    private void HandleDeath()
    {
        if (deathHandled)
            return;

        deathHandled = true;

        // 先标记本局结束，避免残留到下一局。
        if (towerRun != null)
            towerRun.AbortRun();

        // 暂停游戏：敌人、子弹、动画全部停住，玩家只能看到死亡提示。
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
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private IEnumerator ExitAfterDelay()
    {
        // 已暂停，必须用 unscaledTime 计时。
        var remaining = Mathf.Max(0f, delayBeforeExit);
        while (remaining > 0f)
        {
            remaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        AbandonRaid();
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void AbandonRaid()
    {
        // 只清局内携带与战利品，局外仓库与装备原样保留。
        GameSession.AbandonRaid();
    }
}
