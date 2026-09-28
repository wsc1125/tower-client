using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyLaserProjectile2D : MonoBehaviour
{
    [SerializeField] private float damage = 12f;
    [SerializeField] private float lifetime = 3f;

    /// <summary>未经层数缩放的基础伤害。</summary>
    private float baseDamage;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;
    private float speed = 7f;
    private float age;

    public static Quaternion GetRotation(Vector2 travelDirection)
    {
        var direction = travelDirection.sqrMagnitude > 0.0001f ? travelDirection.normalized : Vector2.right;
        return Quaternion.FromToRotation(Vector3.up, direction);
    }

    /// <summary>当前伤害（已含层数缩放）。</summary>
    public float Damage => damage;

    /// <summary>按层数缩放伤害。每次调用都基于原始基础值重算。</summary>
    public void ScaleDamage(float multiplier)
    {
        if (multiplier <= 0f)
            return;

        if (baseDamage <= 0f)
            baseDamage = damage;

        damage = baseDamage * multiplier;
    }

    public void Launch(Vector2 launchDirection, float launchSpeed, Collider2D ownerCollider = null)
    {
        direction = launchDirection.sqrMagnitude > 0.0001f ? launchDirection.normalized : Vector2.right;
        speed = launchSpeed;
        transform.rotation = GetRotation(direction);

        if (ownerCollider != null)
            IgnoreCollisionWith(ownerCollider);
    }

    /// <summary>
    /// 忽略与另一个碰撞体的碰撞。同批弹幕的出生点彼此很近，触发体会重叠，
    /// 若不忽略，OnTriggerEnter2D 会在生成的第一帧把彼此全部销毁（实测 9 颗齐射只剩 1 颗）。
    /// 由发射方在整批弹丸都生成完毕后统一调用，避免依赖 FindObjectsOfType 的注册时机。
    /// </summary>
    public void IgnoreCollisionWith(Collider2D other)
    {
        if (other == null)
            return;

        foreach (var projectileCollider in GetComponentsInChildren<Collider2D>())
            Physics2D.IgnoreCollision(projectileCollider, other, true);
    }

    /// <summary>
    /// 忽略场景中所有敌人碰撞体。BOSS 的弹幕出生点常常压在近身敌人身上，
    /// 若不忽略，弹丸会在生成瞬间被敌人的刚体挤开甚至卡住。
    /// 弹丸只对玩家结算伤害，因此与敌人之间无需任何碰撞关系。
    /// </summary>
    public void IgnoreAllEnemies()
    {
        foreach (var enemy in FindObjectsOfType<EnemyBrain2D>())
            IgnoreCollisionWith(enemy.GetComponent<Collider2D>());

        foreach (var swarm in FindObjectsOfType<SwarmChaser2D>())
            IgnoreCollisionWith(swarm.GetComponent<Collider2D>());

        foreach (var boss in FindObjectsOfType<BossBrain2D>())
            IgnoreCollisionWith(boss.GetComponent<Collider2D>());
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        age += Time.fixedDeltaTime;
        body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));

        if (age >= lifetime)
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 掩体挡子弹：撞上实体掩体即销毁，不结算伤害。
        // 这条判定必须放在玩家判定之前——掩体与玩家可能同时被触发体覆盖，
        // 若先判玩家，贴着掩体站位的玩家会「隔着掩体被打中」。
        if (other.GetComponentInParent<Obstacle2D>() != null)
        {
            Destroy(gameObject);
            return;
        }

        // 只对玩家结算伤害；撞到其他弹丸 / 敌人 / 场景装饰时直接穿过，不销毁。
        var playerHealth = other.GetComponentInParent<PlayerHealth2D>();
        if (playerHealth == null)
            return;

        playerHealth.TakeDamage(damage);
        Destroy(gameObject);
    }
}
