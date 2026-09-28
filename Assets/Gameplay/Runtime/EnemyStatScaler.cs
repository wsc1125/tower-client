using UnityEngine;

/// <summary>
/// 把层数难度套到一个敌人身上：血量按血量系数缩放，各类伤害按伤害系数缩放。
/// 只负责「找到组件并调用它们的缩放方法」，不直接改私有字段（IL2CPP 下反射不可靠）。
/// 必须在敌人的 Awake 之前调用才生效，因此调用方需先 SetActive(false) 再缩放再 SetActive(true)。
/// </summary>
public sealed class EnemyStatScaler : MonoBehaviour
{
    /// <summary>按层数缩放本对象上的敌人数值。config 为 null 时不做任何事。</summary>
    public void Apply(int floor, TowerConfig config)
    {
        if (config == null)
            return;

        var healthMultiplier = FloorGenerator.ResolveHealthMultiplier(floor, config.HealthGrowthPerFloor);
        var damageMultiplier = FloorGenerator.ResolveDamageMultiplier(floor, config.DamageGrowthPerFloor);

        var brain = GetComponent<EnemyBrain2D>();
        if (brain != null)
        {
            brain.ScaleMaximumHealth(healthMultiplier);

            // 激光弹是运行时 Instantiate 出来的，缩放预制体上的实例没有意义。
            // 改为把系数写进 EnemyBrain2D，由它在 FireLaser 时传给每颗弹丸。
            brain.ScaleLaserDamage(damageMultiplier);
        }

        var swarm = GetComponent<SwarmChaser2D>();
        if (swarm != null)
            swarm.ScaleDamage(damageMultiplier);

        var mortar = GetComponent<ArtilleryMortar2D>();
        if (mortar != null)
            mortar.ScaleDamage(damageMultiplier);

        var melee = GetComponent<MeleeAttacker2D>();
        if (melee != null)
            melee.ScaleDamage(damageMultiplier);
    }
}
