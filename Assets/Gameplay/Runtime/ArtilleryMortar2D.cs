using UnityEngine;

/// <summary>
/// 迫击炮台：炮管固定朝上，不旋转、不瞄准。
/// 攻击时在玩家当前位置生成地面预警圈，延迟后落弹爆炸。
/// 玩家看的是地上的预警圈，不是炮口朝向，因此炮管无需转向。
/// </summary>
[RequireComponent(typeof(EnemyBrain2D))]
public sealed class ArtilleryMortar2D : MonoBehaviour
{
    [Header("落点")]
    [SerializeField] private ArtilleryWarning2D warningPrefab;
    [SerializeField] private float warningDuration = 1.4f;
    [SerializeField] private float blastRadius = 1.6f;
    [SerializeField] private float blastDamage = 18f;

    /// <summary>未经层数缩放的基础爆炸伤害。</summary>
    private float baseBlastDamage;

    [Header("弹体")]
    [SerializeField] private ArtilleryShell2D shellPrefab;
    [SerializeField] private float shellArcHeight = 3.2f;
    [SerializeField] private float shellFlightTime = 0.8f;

    private EnemyBrain2D brain;

    /// <summary>当前爆炸伤害（已含层数缩放）。</summary>
    public float BlastDamage => blastDamage;

    /// <summary>按层数缩放爆炸伤害。每次调用都基于原始基础值重算。</summary>
    public void ScaleDamage(float multiplier)
    {
        if (multiplier <= 0f)
            return;

        if (baseBlastDamage <= 0f)
            baseBlastDamage = blastDamage;

        blastDamage = baseBlastDamage * multiplier;
    }

    private void Awake()
    {
        brain = GetComponent<EnemyBrain2D>();
    }

    /// <summary>
    /// 由 <see cref="EnemyBrain2D"/> 在开火帧调用。
    /// 锁定玩家当前位置，生成预警圈与高抛弹体。
    /// </summary>
    public void Fire(Vector2 targetPosition)
    {
        if (brain == null || brain.IsDead)
            return;

        if (warningPrefab != null)
        {
            var warning = Instantiate(warningPrefab, targetPosition, Quaternion.identity);
            warning.Play(warningDuration, blastRadius);
        }

        if (shellPrefab != null)
        {
            var shell = Instantiate(shellPrefab, transform.position, Quaternion.identity);
            shell.Launch(
                transform.position,
                targetPosition,
                shellArcHeight,
                shellFlightTime,
                blastRadius,
                blastDamage);
        }
    }
}
