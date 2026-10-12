using UnityEngine;

/// <summary>
/// BOSS 的动画驱动组件。与 <see cref="EnemyVisual2D"/> 的区别在于：
/// ① 持有两组攻击帧（phaseOneAttackFrames / phaseTwoAttackFrames），按阶段切换；
/// ② 不依赖 <see cref="EnemyState"/>，因为 BOSS 只有「待机 / 攻击 / 死亡」三种表现，
///    没有巡逻、追击、撤退，用布尔量表达更直接。
/// 开火帧沿用 <see cref="EnemyAttackTimeline.ShouldFire"/> 的第 5 帧约定，
/// 因此两组攻击帧数组都必须 ≥ 5 帧。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public sealed class BossVisual2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite[] idleFrames;
    [SerializeField] private Sprite[] phaseOneAttackFrames;
    [SerializeField] private Sprite[] phaseTwoAttackFrames;
    [SerializeField] private Sprite[] deathFrames;
    [SerializeField] private float idleFramesPerSecond = 4f;
    [SerializeField] private float attackFramesPerSecond = 8f;
    [SerializeField] private float deathFramesPerSecond = 6f;

    private bool previousAttacking;
    private bool previousDead;
    private bool previousPhaseTwo;
    private float elapsed;
    private int previousAttackFrame;
    private bool firedThisAttack;

    /// <summary>当前这次攻击动画是否已播完（非循环，播完停在最后一帧）。</summary>
    public bool AttackFinished { get; private set; }

    /// <summary>死亡动画是否已播完（非循环，播完停在最后一帧）。</summary>
    public bool DeathFinished { get; private set; }

    /// <summary>是否配置了当前阶段的攻击帧。没有攻击帧时 <see cref="AttackFinished"/> 永远不会置位。</summary>
    public bool HasAttackFrames => SelectAttackFrames(previousPhaseTwo) != null &&
                                   SelectAttackFrames(previousPhaseTwo).Length > 0;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// 推进一帧动画。返回 true 表示本帧应当开火（攻击动画第 5 帧，每次攻击只触发一次）。
    /// </summary>
    /// <param name="phaseTwo">是否处于阶段二，决定用哪一组攻击帧。</param>
    /// <param name="attacking">是否正在攻击。</param>
    /// <param name="facing">朝向玩家的方向，仅用于水平翻转。</param>
    public bool Step(bool phaseTwo, bool attacking, Vector2 facing, float deltaTime)
    {
        // 世界冻结（仅单机暂停）期间动画停住：联机下世界永不停，BOSS 动画照常推进。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return false;

        var dead = false;
        var clipChanged = attacking != previousAttacking || dead != previousDead || phaseTwo != previousPhaseTwo;

        if (clipChanged)
        {
            elapsed = 0f;
            previousAttackFrame = 0;
            firedThisAttack = false;
            AttackFinished = false;
            DeathFinished = false;
            previousAttacking = attacking;
            previousDead = dead;
            previousPhaseTwo = phaseTwo;
        }
        else
        {
            elapsed += Mathf.Max(0f, deltaTime);
        }

        if (Mathf.Abs(facing.x) > 0.01f)
            spriteRenderer.flipX = facing.x < 0f;

        var frames = SelectAttackFrames(phaseTwo);
        var framesPerSecond = attackFramesPerSecond;
        var loops = false;

        if (!attacking)
        {
            frames = idleFrames;
            framesPerSecond = idleFramesPerSecond;
            loops = true;
        }

        if (frames == null || frames.Length == 0)
            return false;

        var rawFrame = Mathf.FloorToInt(elapsed * framesPerSecond);
        var frame = loops ? rawFrame % frames.Length : Mathf.Min(rawFrame, frames.Length - 1);
        spriteRenderer.sprite = frames[frame];

        var shouldFire = attacking &&
                         EnemyAttackTimeline.ShouldFire(previousAttackFrame, frame, firedThisAttack);
        if (shouldFire)
            firedThisAttack = true;

        previousAttackFrame = frame;
        AttackFinished = attacking && rawFrame >= frames.Length;
        return shouldFire;
    }

    /// <summary>推进死亡动画。播完停在最后一帧，由调用方决定何时销毁。</summary>
    public void StepDeath(float deltaTime)
    {
        if (!previousDead)
        {
            elapsed = 0f;
            DeathFinished = false;
            previousDead = true;
            previousAttacking = false;
        }
        else
        {
            elapsed += Mathf.Max(0f, deltaTime);
        }

        if (deathFrames == null || deathFrames.Length == 0)
        {
            DeathFinished = true;
            return;
        }

        var rawFrame = Mathf.FloorToInt(elapsed * deathFramesPerSecond);
        var frame = Mathf.Min(rawFrame, deathFrames.Length - 1);
        spriteRenderer.sprite = deathFrames[frame];
        DeathFinished = rawFrame >= deathFrames.Length;
    }

    private Sprite[] SelectAttackFrames(bool phaseTwo)
    {
        return phaseTwo ? phaseTwoAttackFrames : phaseOneAttackFrames;
    }
}
