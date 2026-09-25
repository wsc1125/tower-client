using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家死亡结算：延迟片刻后清空背包存档（背包、携带、装备全部丢失），退出战斗场景回到主菜单。
/// 清空放在延迟之后执行，避免死亡期间拾取的掉落物经由变更存档留下来。
/// </summary>
public sealed class PlayerDeathFlow2D : MonoBehaviour
{
    [SerializeField] private PlayerHealth2D playerHealth;
    [SerializeField] private InventoryHolder inventoryHolder;

    [Tooltip("爬塔流程管理器。死亡时标记本局结束并清空战利品清单。留空则跳过。")]
    [SerializeField] private TowerRunManager towerRun;

    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private float delayBeforeExit = 1.5f;

    private bool deathHandled;

    private void Awake()
    {
        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth2D>();
        if (inventoryHolder == null)
            inventoryHolder = FindObjectOfType<InventoryHolder>();
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
    }

    private void HandleDeath()
    {
        if (deathHandled)
            return;

        deathHandled = true;
        StartCoroutine(ExitToMainMenu());
    }

    private IEnumerator ExitToMainMenu()
    {
        yield return new WaitForSeconds(delayBeforeExit);

        LoseInventory();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void LoseInventory()
    {
        // 先标记本局结束并清空战利品清单，避免残留到下一局。
        if (towerRun != null)
            towerRun.AbortRun();

        if (inventoryHolder == null)
            return;

        // ClearAll 不触发 Changed，手动通知让宿主把空背包写入存档并立即落盘。
        var model = inventoryHolder.Model;
        model.ClearAll();
        model.NotifyChanged();
        PlayerInventoryStore.Flush();
    }
}
