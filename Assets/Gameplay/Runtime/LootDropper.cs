using UnityEngine;

/// <summary>
/// 挂在敌人身上，订阅 <see cref="EnemyBrain2D.Died"/>，死亡时按「全局掉落池 + 当前层数」掷点并生成地面掉落物。
/// 掉落池与层数由 <see cref="TowerRunManager"/> 在生成敌人时注入，敌人 prefab 上不再硬编码掉落表。
/// 掷点与生成各自只有一处，后续接入联机时由主机权威调用即可。
/// </summary>
public sealed class LootDropper : MonoBehaviour
{
    [SerializeField] private EnemyBrain2D enemy;
    [SerializeField] private DroppedItem2D dropPrefab;
    [SerializeField] private float scatterRadius = 0.35f;

    private LootPool lootPool;
    private int floor = 1;
    private bool subscribed;

    /// <summary>
    /// 掉落掷点与散落偏移使用的随机源。由 <see cref="TowerRunManager"/> 注入本局随机源，
    /// 使掉落结果可随本局种子复现（联机时各端一致）。
    /// 未注入时回退到自建实例，保证单机 / 测试场景仍能工作。
    /// </summary>
    private System.Random random;

    /// <summary>当前使用的掉落池；未注入时为 null，此时不掉落。</summary>
    public LootPool LootPool => lootPool;

    /// <summary>当前层数，决定各品质的掉落概率。</summary>
    public int Floor => floor;

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

    /// <summary>注入本层使用的掉落池与层数。由 <see cref="TowerRunManager"/> 在敌人生成时调用。</summary>
    public void Configure(LootPool pool, int currentFloor)
    {
        Configure(pool, currentFloor, null);
    }

    /// <summary>
    /// 注入本层使用的掉落池、层数与随机源。
    /// <paramref name="source"/> 为 null 时保留已有随机源（首次调用则自建一个）。
    /// </summary>
    public void Configure(LootPool pool, int currentFloor, System.Random source)
    {
        lootPool = pool;
        floor = currentFloor < 1 ? 1 : currentFloor;

        if (source != null)
            random = source;
    }

    /// <summary>敌人死亡时的处理。拆成公开方法以便直接测试；联机化时改为主机权威调用。</summary>
    public void HandleDied()
    {
        if (lootPool == null || dropPrefab == null)
            return;

        random ??= new System.Random();

        var items = lootPool.Roll(floor, random);
        foreach (var item in items)
        {
            var offset = NextInsideUnitCircle() * scatterRadius;
            var position = (Vector2)transform.position + offset;
            var dropped = Instantiate(dropPrefab, position, Quaternion.identity);

            // 掉落池掷出的是模板，落地时现场创建一个满耐久的实例。
            dropped.Initialize(ItemInstance.Create(item));
        }
    }

    /// <summary>
    /// 用本局随机源取单位圆内的随机点。
    /// 不用 <c>UnityEngine.Random.insideUnitCircle</c>：那是全局静态状态，无法按局固定种子。
    /// 用「随机角度 + 半径平方根」保证圆内均匀分布（直接取半径会向圆心聚集）。
    /// </summary>
    private Vector2 NextInsideUnitCircle()
    {
        var angle = random.NextDouble() * System.Math.PI * 2.0;
        var radius = System.Math.Sqrt(random.NextDouble());
        return new Vector2(
            (float)(System.Math.Cos(angle) * radius),
            (float)(System.Math.Sin(angle) * radius));
    }
}
