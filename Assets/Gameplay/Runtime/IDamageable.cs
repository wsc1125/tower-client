/// <summary>
/// 可受伤目标的统一契约。玩家武器、自动索敌、掉落拾取判定都面向这个接口，
/// 而不是具体敌人类型，这样新增敌人（如虫群）无需改动这些系统。
/// </summary>
public interface IDamageable
{
    /// <summary>目标的位置，供索敌与距离判定使用。</summary>
    UnityEngine.Transform transform { get; }

    /// <summary>是否已死亡。已死亡的目标不再受伤、不再被索敌。</summary>
    bool IsDead { get; }

    /// <summary>承受一次伤害。</summary>
    void TakeDamage(float amount);
}
