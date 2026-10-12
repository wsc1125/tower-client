using UnityEngine;

/// <summary>
/// 母巢的召唤组件：挂在母巢本体上，每隔一段时间在产道口生成一批虫群。
/// 生成时机由 <see cref="EnemyVisual2D.Step"/> 返回的开火信号驱动（Attack 动画第 5 帧），
/// 因此「喷射瞬间」与「实际产虫」严格对齐，不需要额外的计时器。
/// </summary>
[RequireComponent(typeof(EnemyBrain2D))]
public sealed class SpawnerSummoner2D : MonoBehaviour
{
    [Header("产物")]
    [SerializeField] private GameObject swarmPrefab;
    [SerializeField] private int spawnCountPerAttack = 3;
    [SerializeField] private float spawnRadius = 0.6f;

    [Header("上限")]
    [SerializeField] private int maxAliveSwarms = 12;

    private EnemyBrain2D brain;

    public int SpawnCountPerAttack => spawnCountPerAttack;

    private void Awake()
    {
        brain = GetComponent<EnemyBrain2D>();
    }

    /// <summary>
    /// 由 <see cref="EnemyBrain2D"/> 在开火帧调用。母巢死亡后不再产虫。
    /// </summary>
    public void Summon()
    {
        if (swarmPrefab == null || brain == null || brain.IsDead)
            return;

        var alive = CountAliveSwarms();
        var budget = Mathf.Max(0, maxAliveSwarms - alive);
        var count = Mathf.Min(spawnCountPerAttack, budget);

        for (var i = 0; i < count; i++)
        {
            var angle = 360f * i / Mathf.Max(1, count);
            var offset = Quaternion.Euler(0f, 0f, angle) * (Vector3)(Vector2.down * spawnRadius);
            var swarm = Instantiate(swarmPrefab, transform.position + offset, Quaternion.identity);

            // 联机主机：把虫群注册进 NGO，由 NGO 自动同步到所有客户端。
            // 客户端不生成虫群（母巢 AI 已被禁用，不会走到这里）。
            SpawnIfServer(swarm);
        }
    }

    /// <summary>
    /// 联机主机：把运行时生成的虫群注册进 NGO，由 NGO 自动同步到所有客户端。
    /// 单机时 NetworkManager 未启动，直接跳过，行为与改造前一致。
    /// </summary>
    private static void SpawnIfServer(GameObject target)
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer)
            return;

        var netObj = target.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null && !netObj.IsSpawned)
            netObj.Spawn(true);
    }

    private int CountAliveSwarms()
    {
        var count = 0;
        foreach (var candidate in WorldRegistry.Damageables)
        {
            if (candidate == null || candidate.IsDead)
                continue;

            if (candidate is EnemyBrain2D candidateBrain && candidateBrain == brain)
                continue;

            if (candidate is EnemyBrain2D enemy && enemy.GetComponent<SpawnerSummoner2D>() != null)
                continue;

            count++;
        }

        return count;
    }
}
