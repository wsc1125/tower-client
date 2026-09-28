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
            Instantiate(swarmPrefab, transform.position + offset, Quaternion.identity);
        }
    }

    private int CountAliveSwarms()
    {
        var count = 0;
        var all = FindObjectsOfType<EnemyBrain2D>();
        foreach (var candidate in all)
        {
            if (candidate == null || candidate == brain || candidate.IsDead)
                continue;

            if (candidate.GetComponent<SpawnerSummoner2D>() != null)
                continue;

            count++;
        }

        return count;
    }
}
