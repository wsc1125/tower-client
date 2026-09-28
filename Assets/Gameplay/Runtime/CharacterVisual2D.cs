using UnityEngine;

public enum FacingDirection
{
    Left = -1,
    Right = 1,
}

[RequireComponent(typeof(SpriteRenderer))]
public sealed class CharacterVisual2D : MonoBehaviour
{
    private const float MotionEpsilon = 0.01f;

    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite[] moveFrames;
    [SerializeField] private float framesPerSecond = 10f;

    [Header("受击闪烁")]
    [Tooltip("受击瞬间角色整体染上的颜色。默认高饱和红——玩家贴图本色是纯白，染白等于没效果。")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Tooltip("受击闪烁持续时间（秒）。这段时间内角色免疫伤害。")]
    [SerializeField] private float hitFlashDuration = 0.12f;

    private SpriteFrameAnimation animation;
    private FacingDirection facing = FacingDirection.Right;
    private Vector2? facingOverride;
    private Color baseColor = Color.white;
    private bool baseColorCaptured;
    private float hitFlashRemaining;

    public FacingDirection Facing => facing;

    /// <summary>是否正在受击闪烁。闪烁期间角色免疫伤害，供 PlayerHealth2D 查询。</summary>
    public bool IsHitFlashing => hitFlashRemaining > 0f;

    public static bool TryResolveFacing(Vector2 velocity, out FacingDirection resolved)
    {
        resolved = FacingDirection.Right;
        if (Mathf.Abs(velocity.x) <= MotionEpsilon)
            return false;

        resolved = velocity.x > 0f ? FacingDirection.Right : FacingDirection.Left;
        return true;
    }

    public static bool IsMoving(Vector2 velocity)
    {
        return velocity.sqrMagnitude > MotionEpsilon * MotionEpsilon;
    }

    public void SetFacingOverride(Vector2? aimDirection)
    {
        facingOverride = aimDirection;
    }

    public void SetMotion(Vector2 velocity)
    {
        if (animation == null)
            animation = new SpriteFrameAnimation(moveFrames, framesPerSecond);

        var facingSource = facingOverride.HasValue ? facingOverride.Value : velocity;
        if (TryResolveFacing(facingSource, out var resolved))
            ApplyFacing(resolved);

        if (IsMoving(velocity))
            animation.Tick(Time.deltaTime);
        else
            animation.Reset();

        ApplyFrame(animation.CurrentFrame);
        StepHitFlash(Time.deltaTime);
    }

    /// <summary>
    /// 触发一次受击闪烁。重复调用会重置计时（连续受击时闪烁不中断），
    /// 因此不需要额外的「已在闪烁中」判断。
    /// </summary>
    public void FlashHit()
    {
        // 首次受击时若 Awake 没拿到 renderer（运行时 AddComponent），
        // 此刻补记本色，否则闪烁结束会还原成硬编码白色而不是美术调好的颜色。
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null && !baseColorCaptured)
        {
            baseColor = spriteRenderer.color;
            baseColorCaptured = true;
        }

        hitFlashRemaining = Mathf.Max(0f, hitFlashDuration);
        ApplyColor(hitFlashRemaining > 0f ? hitFlashColor : baseColor);
    }

    /// <summary>
    /// 推进闪烁计时。由 <see cref="SetMotion"/> 每帧驱动，
    /// 这样表现层只需在受击时调一次 <see cref="FlashHit"/>，不必自己维护计时器。
    /// </summary>
    private void StepHitFlash(float deltaTime)
    {
        if (hitFlashRemaining <= 0f)
            return;

        hitFlashRemaining = Mathf.Max(0f, hitFlashRemaining - Mathf.Max(0f, deltaTime));
        if (hitFlashRemaining <= 0f)
            ApplyColor(baseColor);
    }

    /// <summary>
    /// 手动推进闪烁计时。EditMode 测试里没有帧循环，用它模拟时间流逝。
    /// </summary>
    public void AdvanceHitFlash(float deltaTime)
    {
        StepHitFlash(deltaTime);
    }

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (animation == null)
            animation = new SpriteFrameAnimation(moveFrames, framesPerSecond);

        // 记录美术在预制体上调好的本色，闪白结束后要还原回它，而不是硬编码白色。
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
            baseColorCaptured = true;
        }

        ApplyFacing(facing);
        ApplyFrame(animation.CurrentFrame);
    }

    private void ApplyColor(Color color)
    {
        // 惰性兜底：RequireComponent 只在编辑器添加组件时自动补 SpriteRenderer，
        // 运行时 AddComponent 或预制体引用丢失时字段会是 null，这里补一次避免闪红静默失效。
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }

    private void ApplyFacing(FacingDirection value)
    {
        facing = value;

        // 只翻转角色贴图本身，不翻转 Visual 节点的 localScale。
        // 旧实现用 localScale.x 取负做镜像，会把挂在 Visual 下的 MainHand（含武器与枪口）
        // 一起镜像，而挂在根节点下的 OffHand 不受影响，导致两只手的武器翻转行为不一致，
        // 还会与武器自身的瞄准旋转叠加成「X 镜像 + 180° 旋转」的错位。
        if (spriteRenderer != null)
            spriteRenderer.flipX = value == FacingDirection.Left;
    }

    private void ApplyFrame(Sprite frame)
    {
        if (spriteRenderer != null && frame != null)
            spriteRenderer.sprite = frame;
    }
}
