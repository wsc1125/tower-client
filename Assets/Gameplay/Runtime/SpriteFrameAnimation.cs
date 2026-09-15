using UnityEngine;

public sealed class SpriteFrameAnimation
{
    private readonly Sprite[] frames;
    private readonly float framesPerSecond;

    private int frameIndex;
    private float elapsed;

    public SpriteFrameAnimation(Sprite[] frames, float framesPerSecond)
    {
        this.frames = frames;
        this.framesPerSecond = framesPerSecond;
    }

    public int FrameIndex => frameIndex;

    public Sprite CurrentFrame
    {
        get
        {
            if (frames == null || frames.Length == 0)
                return null;

            return frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
        }
    }

    public void Tick(float deltaTime)
    {
        if (frames == null || frames.Length <= 1 || framesPerSecond <= 0f || deltaTime <= 0f)
            return;

        var frameDuration = 1f / framesPerSecond;
        elapsed += deltaTime;

        while (elapsed >= frameDuration)
        {
            elapsed -= frameDuration;
            frameIndex = (frameIndex + 1) % frames.Length;
        }
    }

    public void Reset()
    {
        frameIndex = 0;
        elapsed = 0f;
    }
}
