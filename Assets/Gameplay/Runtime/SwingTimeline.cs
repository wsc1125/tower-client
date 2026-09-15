using UnityEngine;

public sealed class SwingTimeline
{
    public SwingTimeline(float duration, float startAngle, float endAngle)
    {
        Duration = Mathf.Max(duration, 0f);
        StartAngle = startAngle;
        EndAngle = endAngle;
    }

    public float Duration { get; }
    public float StartAngle { get; }
    public float EndAngle { get; }
    public float Elapsed { get; private set; }
    public bool IsRunning { get; private set; }

    public float Progress => Duration <= 0f ? 1f : Mathf.Clamp01(Elapsed / Duration);

    public float Angle => Mathf.Lerp(StartAngle, EndAngle, Progress);

    public void Restart()
    {
        Elapsed = 0f;
        IsRunning = true;
    }

    public void Tick(float deltaTime)
    {
        if (!IsRunning || deltaTime <= 0f)
        {
            return;
        }

        Elapsed += deltaTime;

        if (Elapsed >= Duration)
        {
            Elapsed = Duration;
            IsRunning = false;
        }
    }
}
