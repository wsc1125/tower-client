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

    private SpriteFrameAnimation animation;
    private FacingDirection facing = FacingDirection.Right;

    public FacingDirection Facing => facing;

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

    public void SetMotion(Vector2 velocity)
    {
        if (animation == null)
            animation = new SpriteFrameAnimation(moveFrames, framesPerSecond);

        if (TryResolveFacing(velocity, out var resolved))
            ApplyFacing(resolved);

        if (IsMoving(velocity))
            animation.Tick(Time.deltaTime);
        else
            animation.Reset();

        ApplyFrame(animation.CurrentFrame);
    }

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (animation == null)
            animation = new SpriteFrameAnimation(moveFrames, framesPerSecond);

        ApplyFacing(facing);
        ApplyFrame(animation.CurrentFrame);
    }

    private void ApplyFacing(FacingDirection value)
    {
        facing = value;

        var scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (float)value;
        transform.localScale = scale;
    }

    private void ApplyFrame(Sprite frame)
    {
        if (spriteRenderer != null && frame != null)
            spriteRenderer.sprite = frame;
    }
}
