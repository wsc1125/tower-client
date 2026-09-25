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

    [Header("接线")]
    [SerializeField] private Transform player;
    [SerializeField] private CameraFollow2D cameraFollow;
    [SerializeField] private StatusPanelController statusPanel;
    [SerializeField] private TowerResultPanelController resultPanel;
    [SerializeField] private InventoryHolder inventoryHolder;
    [SerializeField] private PlayerHealth2D playerHealth;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("节奏")]
    [SerializeField] private float portalSpawnDelay = 0.6f;
    [SerializeField] private float clearCheckInterval = 0.2f;
    [SerializeField] private float enemySpawnRadiusMin = 3.5f;
    [SerializeField] private float enemySpawnRadiusMax = 5.5f;
    [SerializeField] private float portalOffsetX = 2.5f;

    private readonly TowerRunState state = new TowerRunState();
    private readonly List<GameObject> spawnedEnemies = new List<GameObject>();
    private readonly List<FloorPortal2D> spawnedPortals = new List<FloorPortal2D>();
    private GameObject currentRoom;
    private System.Random random;
    private float clearTimer;
    private bool portalsSpawned;
    private bool subscribedDeath;

    public TowerRunState State => state;

    private void Awake()
    {
        random = new System.Random();
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died += HandlePlayerDied;
            subscribedDeath = true;
        }
    }

    private void OnDisable()
    {
        if (subscribedDeath && playerHealth != null)
        {
            playerHealth.Died -= HandlePlayerDied;
            subscribedDeath = false;
        }
    }

    private void Start()
    {
        state.BeginRun();
        BuildFloor(state.CurrentFloor);
    }

    private void Update()
    {
        if (state.IsFinished || portalsSpawned)
            return;

        clearTimer -= Time.deltaTime;
        if (clearTimer > 0f)
            return;

        clearTimer = Mathf.Max(0.05f, clearCheckInterval);
        if (IsFloorCleared())
            StartCoroutine(SpawnPortalsAfterDelay());
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

        var center = currentRoom != null ? (Vector2)currentRoom.transform.position : Vector2.zero;
        MovePlayerTo(center);

        if (cameraFollow != null)
            cameraFollow.CaptureRoomBounds();

        var plan = FloorGenerator.Generate(floor, config, random);
        SpawnEnemies(plan, center);

        if (statusPanel != null)
            statusPanel.SetLevel(floor);
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

        // 母巢召唤的虫群不在 spawnedEnemies 里，单独扫一遍。
        foreach (var swarm in FindObjectsOfType<SwarmChaser2D>())
        {
            if (swarm != null && !swarm.IsDead)
                return false;
        }

        return true;
    }

    /// <summary>玩家踩到传送点。</summary>
    public void HandlePortalEntered(PortalKind kind)
    {
        if (state.IsFinished)
            return;

        if (kind == PortalKind.Retreat)
        {
            Retreat();
            return;
        }

        state.AdvanceFloor();
        BuildFloor(state.CurrentFloor);
    }

    /// <summary>撤退：结束本局并弹出结算面板。</summary>
    public void Retreat()
    {
        if (state.IsFinished)
            return;

        state.Finish();
        ClearFloor();

        if (resultPanel != null)
            resultPanel.Show(BuildResultEntries(), TotalLootValue(), state.CurrentFloor);
    }

    /// <summary>本局中止（玩家死亡）。只标记结束并清空战利品清单，切场景由 PlayerDeathFlow2D 负责。</summary>
    public void AbortRun()
    {
        state.Finish();
        state.ClearLoot();
    }

    /// <summary>把本局战利品清单转成结算面板的展示数据。</summary>
    public IReadOnlyList<TowerResultEntry> BuildResultEntries()
    {
        var entries = new List<TowerResultEntry>();
        foreach (var item in state.CollectedLoot)
        {
            if (item == null)
                continue;

            entries.Add(new TowerResultEntry
            {
                DisplayName = item.DisplayName,
                Value = item.Value,
                Icon = item.Icon,
            });
        }

        return entries;
    }

    /// <summary>本局战利品的总价值。</summary>
    public int TotalLootValue()
    {
        var total = 0;
        foreach (var item in state.CollectedLoot)
        {
            if (item != null)
                total += item.Value;
        }

        return total;
    }

    /// <summary>结算面板确认后回主菜单。</summary>
    public void ConfirmRetreat()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void HandlePlayerDied()
    {
        AbortRun();
    }

    private IEnumerator SpawnPortalsAfterDelay()
    {
        portalsSpawned = true;
        yield return new WaitForSeconds(Mathf.Max(0f, portalSpawnDelay));

        if (state.IsFinished)
            yield break;

        var center = currentRoom != null ? (Vector2)currentRoom.transform.position : Vector2.zero;
        var isRetreatFloor = FloorGenerator.IsRetreatFloor(state.CurrentFloor, config != null ? config.RetreatEveryFloors : 0);

        if (isRetreatFloor && retreatPortalPrefab != null)
        {
            SpawnPortal(retreatPortalPrefab, center + new Vector2(-portalOffsetX, 0f));
            SpawnPortal(nextPortalPrefab, center + new Vector2(portalOffsetX, 0f));
            yield break;
        }

        SpawnPortal(nextPortalPrefab, center);
    }

    private void SpawnPortal(FloorPortal2D prefab, Vector2 position)
    {
        if (prefab == null)
            return;

        var portal = Instantiate(prefab, position, Quaternion.identity);
        portal.Entered += HandlePortalEntered;
        spawnedPortals.Add(portal);
    }

    private void SpawnEnemies(FloorPlan plan, Vector2 center)
    {
        for (var i = 0; i < plan.Enemies.Length; i++)
        {
            var prefab = plan.Enemies[i];
            if (prefab == null)
                continue;

            var angle = 360f * i / Mathf.Max(1, plan.Enemies.Length);
            var radius = Random.Range(enemySpawnRadiusMin, enemySpawnRadiusMax);
            var offset = Quaternion.Euler(0f, 0f, angle) * (Vector3)(Vector2.right * radius);
            var position = center + (Vector2)offset;

            var enemy = Instantiate(prefab, position, Quaternion.identity);

            // 先 SetActive(false) 再缩放再 SetActive(true)：Awake 在 SetActive(true) 时才跑，
            // 因此缩放先生效，EnemyBrain2D 会用缩放后的血量创建 health。
            enemy.SetActive(false);
            var scaler = enemy.GetComponent<EnemyStatScaler>();
            if (scaler == null)
                scaler = enemy.AddComponent<EnemyStatScaler>();
            scaler.Apply(plan.Floor, config);
            enemy.SetActive(true);

            spawnedEnemies.Add(enemy);
        }
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

    private void ClearFloor()
    {
        foreach (var portal in spawnedPortals)
        {
            if (portal != null)
            {
                portal.Entered -= HandlePortalEntered;
                Destroy(portal.gameObject);
            }
        }

        spawnedPortals.Clear();

        foreach (var enemy in spawnedEnemies)
        {
            if (enemy != null)
                Destroy(enemy);
        }

        spawnedEnemies.Clear();

        // 母巢召唤的虫群不在 spawnedEnemies 里，换层时一并清掉。
        foreach (var swarm in FindObjectsOfType<SwarmChaser2D>())
        {
            if (swarm != null)
                Destroy(swarm.gameObject);
        }

        if (currentRoom != null)
        {
            Destroy(currentRoom);
            currentRoom = null;
        }

        portalsSpawned = false;
        clearTimer = 0f;
    }
}
