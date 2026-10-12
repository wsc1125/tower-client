using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class EnemyVisual2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite[] idleFrames;
    [SerializeField] private Sprite[] moveFrames;
    [SerializeField] private Sprite[] attackFrames;
    [SerializeField] private Sprite[] deathFrames;
    [SerializeField] private float idleFramesPerSecond = 4f;
    [SerializeField] private float moveFramesPerSecond = 8f;
    [SerializeField] private float attackFramesPerSecond = 10f;
    [SerializeField] private float deathFramesPerSecond = 7f;

    private EnemyState previousState;
    private float elapsed;
    private int previousAttackFrame;
    private bool firedThisAttack;

    public bool AttackFinished { get; private set; }

    /// <summary>死亡动画是否已播完（非循环，播完停在最后一帧）。</summary>
    public bool DeathFinished { get; private set; }

    /// <summary>
    /// 是否配置了攻击动画帧。没有攻击帧时 <see cref="AttackFinished"/> 永远不会置位，
    /// 调用方据此判断能否进入「攻击执行到底」的锁定，避免永久停在攻击状态。
    /// </summary>
    public bool HasAttackFrames => attackFrames != null && attackFrames.Length > 0;

    /// <summary>
    /// 取当前水平朝向（true = 朝左）。供联机同步读取主机侧朝向。
    /// </summary>
    public bool TryGetFacingLeft(out bool facingLeft)
    {
        if (spriteRenderer == null)
        {
            facingLeft = false;
            return false;
        }

        facingLeft = spriteRenderer.flipX;
        return true;
    }

    /// <summary>
    /// 直接设置水平朝向。供联机客户端按主机广播的朝向翻转贴图 ——
    /// 客户端 velocity 恒为 0，靠 <see cref="Step"/> 内的速度判定无法翻转。
    /// </summary>
    public void SetFacingLeft(bool facingLeft)
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.flipX = facingLeft;
    }

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public bool Step(EnemyState state, Vector2 velocity, float deltaTime)
    {
        // 世界冻结（仅单机暂停）期间动画停住：联机下世界永不停，敌人动画照常推进。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return false;

        if (state != previousState)
        {
            elapsed = 0f;
            previousAttackFrame = 0;
            firedThisAttack = false;
            AttackFinished = false;
            DeathFinished = false;
            previousState = state;
        }
        else
        {
            elapsed += Mathf.Max(0f, deltaTime);
        }

        if (Mathf.Abs(velocity.x) > 0.01f)
            spriteRenderer.flipX = velocity.x < 0f;

        var frames = SelectFrames(state, velocity, out var framesPerSecond, out var loops);
        if (frames == null || frames.Length == 0)
            return false;

        var rawFrame = Mathf.FloorToInt(elapsed * framesPerSecond);
        var frame = loops ? rawFrame % frames.Length : Mathf.Min(rawFrame, frames.Length - 1);
        spriteRenderer.sprite = frames[frame];

        var shouldFire = state == EnemyState.Attack &&
                         EnemyAttackTimeline.ShouldFire(previousAttackFrame, frame, firedThisAttack);
        if (shouldFire)
            firedThisAttack = true;

        previousAttackFrame = frame;
        AttackFinished = state == EnemyState.Attack && rawFrame >= frames.Length;
        DeathFinished = state == EnemyState.Dead && rawFrame >= frames.Length;
        return shouldFire;
    }

    private Sprite[] SelectFrames(EnemyState state, Vector2 velocity, out float framesPerSecond, out bool loops)
    {
        loops = true;
        switch (state)
        {
            case EnemyState.Dead:
                framesPerSecond = deathFramesPerSecond;
                loops = false;
                return deathFrames;
            case EnemyState.Attack:
                framesPerSecond = attackFramesPerSecond;
                loops = false;
                return attackFrames;
            case EnemyState.Chase:
            case EnemyState.Retreat:
                framesPerSecond = moveFramesPerSecond;
                return moveFrames;
            case EnemyState.Recover:
                // 喘息期间原地不动，播 idle 帧，视觉上明确「收招了」。
                break;
            case EnemyState.Patrol:
                if (velocity.sqrMagnitude > 0.0001f)
                {
                    framesPerSecond = moveFramesPerSecond;
                    return moveFrames;
                }
                break;
        }

        framesPerSecond = idleFramesPerSecond;
        return idleFrames;
    }
}
