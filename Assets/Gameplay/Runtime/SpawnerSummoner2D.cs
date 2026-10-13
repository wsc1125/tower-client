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
    [Tooltip("本母巢同一时间允许存活的虫群数量上限。每只母巢独立计数，互不影响。")]
    [SerializeField] private int maxAliveSwarms = 12;

    private EnemyBrain2D brain;

    /// <summary>本母巢自己生成的虫群。超过上限时暂停产虫，个体死亡或到期销毁后自动腾出名额。</summary>
    private readonly System.Collections.Generic.List<SwarmChaser2D> ownedSwarms
        = new System.Collections.Generic.List<SwarmChaser2D>();

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

        PruneOwnedSwarms();
        var budget = Mathf.Max(0, maxAliveSwarms - ownedSwarms.Count);
        var count = Mathf.Min(spawnCountPerAttack, budget);

        for (var i = 0; i < count; i++)
        {
            var angle = 360f * i / Mathf.Max(1, count);
            var offset = Quaternion.Euler(0f, 0f, angle) * (Vector3)(Vector2.down * spawnRadius);
            var swarm = Instantiate(swarmPrefab, transform.position + offset, Quaternion.identity);

            // 联机主机：把虫群注册进 NGO，由 NGO 自动同步到所有客户端。
            // 客户端不生成虫群（母巢 AI 已被禁用，不会走到这里）。
            SpawnIfServer(swarm);

            var chaser = swarm.GetComponent<SwarmChaser2D>();
            if (chaser != null)
                ownedSwarms.Add(chaser);
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

    /// <summary>
    /// 清点本母巢名下仍存活的虫群：剔除已死亡或已销毁（含 20 秒寿命到期）的个体。
    /// 只统计本巢生成的虫群——其他母巢的产物与场上普通敌人不占本巢名额。
    /// </summary>
    private void PruneOwnedSwarms()
    {
        for (var i = ownedSwarms.Count - 1; i >= 0; i--)
        {
            var swarm = ownedSwarms[i];
            if (swarm == null || swarm.IsDead)
                ownedSwarms.RemoveAt(i);
        }
    }
}
