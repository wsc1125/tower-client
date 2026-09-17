using UnityEngine;

/// <summary>
/// 挂在敌人身上，订阅 <see cref="EnemyBrain2D.Died"/>，死亡时按掉落表掷点并生成地面掉落物。
/// 掷点与生成各自只有一处，后续接入联机时由主机权威调用即可。
/// </summary>
public sealed class LootDropper : MonoBehaviour
{
    [SerializeField] private EnemyBrain2D enemy;
    [SerializeField] private LootTable lootTable;
    [SerializeField] private DroppedItem2D dropPrefab;
    [SerializeField] private float scatterRadius = 0.35f;

    private bool subscribed;

    private void Awake()
    {
        enemy ??= GetComponent<EnemyBrain2D>();
        Subscribe();
    }

    private void OnDestroy()
    {
        if (!subscribed || enemy == null)
            return;

        enemy.Died -= HandleDied;
        subscribed = false;
    }

    /// <summary>幂等订阅死亡事件。测试可直接调用，避免依赖 EditMode 中不会触发的 Awake。</summary>
    public void Subscribe()
    {
        if (subscribed)
            return;

        if (enemy == null)
            enemy = GetComponent<EnemyBrain2D>();

        if (enemy == null)
            return;

        enemy.Died += HandleDied;
        subscribed = true;
    }

    /// <summary>敌人死亡时的处理。拆成公开方法以便直接测试；联机化时改为主机权威调用。</summary>
    public void HandleDied()
    {
        if (lootTable == null || dropPrefab == null)
            return;

        var items = lootTable.Roll(new System.Random());
        foreach (var item in items)
        {
            var offset = Random.insideUnitCircle * scatterRadius;
            var position = (Vector2)transform.position + offset;
            var dropped = Instantiate(dropPrefab, position, Quaternion.identity);
            dropped.Initialize(item);
        }
    }
}
