using NUnit.Framework;

public sealed class SwingTimelineTests
{
    private const float Duration = 0.3f;
    private const float StartAngle = 60f;
    private const float EndAngle = -60f;

    private static SwingTimeline NewTimeline()
    {
        return new SwingTimeline(Duration, StartAngle, EndAngle);
    }

    [Test]
    public void InitialState_IsIdleAtStartAngle()
    {
        var timeline = NewTimeline();

        Assert.That(timeline.IsRunning, Is.False);
        Assert.That(timeline.Progress, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(StartAngle).Within(1e-4f));
    }

    [Test]
    public void Restart_ResetsToStartAndRuns()
    {
        var timeline = NewTimeline();

        timeline.Restart();

        Assert.That(timeline.IsRunning, Is.True);
        Assert.That(timeline.Progress, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(StartAngle).Within(1e-4f));
    }

    [Test]
    public void Tick_HalfDuration_ReachesMidAngle()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        timeline.Tick(Duration * 0.5f);

        Assert.That(timeline.Progress, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.IsRunning, Is.True);
    }

    [Test]
    public void Tick_BeyondDuration_FinishesAtEndAngle()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        timeline.Tick(Duration * 2f);

        Assert.That(timeline.IsRunning, Is.False);
        Assert.That(timeline.Progress, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));
        Assert.That(timeline.Elapsed, Is.EqualTo(Duration).Within(1e-4f));
    }

    [Test]
    public void Tick_AfterFinished_DoesNotChangeAngle()
    {
        var timeline = NewTimeline();
        timeline.Restart();
        timeline.Tick(Duration * 2f);

        timeline.Tick(Duration);

        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));
        Assert.That(timeline.Elapsed, Is.EqualTo(Duration).Within(1e-4f));
    }

    [Test]
    public void Tick_NonPositiveDeltaTime_DoesNotAdvance()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        timeline.Tick(0f);
        timeline.Tick(-1f);

        Assert.That(timeline.Elapsed, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.IsRunning, Is.True);
    }

    [Test]
    public void Tick_WithoutRestart_DoesNotAdvance()
    {
        var timeline = NewTimeline();

        timeline.Tick(Duration);

        Assert.That(timeline.Elapsed, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.IsRunning, Is.False);
    }

    [Test]
    public void ZeroDuration_IsCompleteImmediately()
    {
        var timeline = new SwingTimeline(0f, StartAngle, EndAngle);

        Assert.That(timeline.Progress, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));

        timeline.Restart();
        timeline.Tick(0.016f);

        Assert.That(timeline.IsRunning, Is.False);
        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));
    }

    [Test]
    public void Restart_Midway_ResetsProgress()
    {
        var timeline = NewTimeline();
        timeline.Restart();
        timeline.Tick(Duration * 0.75f);

        timeline.Restart();

        Assert.That(timeline.IsRunning, Is.True);
        Assert.That(timeline.Progress, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(StartAngle).Within(1e-4f));
    }

    [Test]
    public void Angle_DecreasesMonotonically_WhenStartAboveEnd()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        var previous = timeline.Angle;
        for (var i = 0; i < 6; i++)
        {
            timeline.Tick(Duration / 6f);

            Assert.That(timeline.Angle, Is.LessThan(previous));
            previous = timeline.Angle;
        }
    }
}
