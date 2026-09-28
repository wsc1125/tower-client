using UnityEngine;

/// <summary>
/// 近战攻击组件：挂在敌人身上，由 <see cref="EnemyBrain2D"/> 在攻击动画的开火帧调用。
/// 对攻击范围内最近的玩家结算一次伤害，并带冷却，避免同一帧被多个碰撞体重复结算。
///
/// 与 <see cref="EnemyLaserProjectile2D"/> 的区别：近战不生成弹丸，
/// 伤害在挥击瞬间直接结算，因此不需要飞行物与命中判定。
/// 太空海盗（Enemy_SpacePirate）没有激光 prefab，靠本组件提供伤害来源。
/// </summary>
public sealed class MeleeAttacker2D : MonoBehaviour
{
    [Tooltip("近战攻击距离（世界单位）。以自身为圆心，命中该半径内的玩家。")]
    [SerializeField] private float attackRange = 1.6f;

    [Tooltip("单次挥击伤害。")]
    [SerializeField] private float damage = 12f;

    [Tooltip("命中判定相对自身的位置偏移。默认取碰撞体中心，避免贴图轴点偏移导致判定错位。")]
    [SerializeField] private Vector2 hitOffset = new Vector2(0f, 0.6f);

    /// <summary>未经层数缩放的基础伤害。</summary>
    private float baseDamage;

    private EnemyBrain2D brain;

    /// <summary>当前伤害（已含层数缩放）。</summary>
    public float Damage => damage;

    /// <summary>攻击距离。</summary>
    public float AttackRange => attackRange;

    /// <summary>按层数缩放伤害。每次调用都基于原始基础值重算。</summary>
    public void ScaleDamage(float multiplier)
    {
        if (multiplier <= 0f)
            return;

        if (baseDamage <= 0f)
            baseDamage = damage;

        damage = baseDamage * multiplier;
    }

    private void Awake()
    {
        brain = GetComponent<EnemyBrain2D>();

        if (baseDamage <= 0f)
            baseDamage = damage;
    }

    /// <summary>
    /// 由 <see cref="EnemyBrain2D"/> 在开火帧调用。对范围内最近的玩家结算一次伤害。
    /// 敌人已死亡时不再结算。
    /// </summary>
    public void Attack()
    {
        if (brain != null && brain.IsDead)
            return;

        var origin = (Vector2)transform.position + hitOffset;
        var hits = Physics2D.OverlapCircleAll(origin, Mathf.Max(0.1f, attackRange));

        foreach (var hit in hits)
        {
            var playerHealth = hit.GetComponentInParent<PlayerHealth2D>();
            if (playerHealth == null)
                continue;

            playerHealth.TakeDamage(damage);
            return;
        }
    }
}
