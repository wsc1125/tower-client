using NUnit.Framework;

/// <summary>
/// <see cref="TowerRunState"/> 的层数与结束标记。
/// 战利品清单已移到 <see cref="RaidBag"/>，因此这里不再覆盖拾取。
/// </summary>
public sealed class TowerRunStateTests
{
    private TowerRunState state;

    [SetUp]
    public void SetUp()
    {
        state = new TowerRunState();
    }

    [Test]
    public void BeginRun_StartsAtFloorOneAndNotFinished()
    {
        state.AdvanceFloor();
        state.Finish();

        state.BeginRun();

        Assert.That(state.CurrentFloor, Is.EqualTo(1));
        Assert.That(state.IsFinished, Is.False);
    }

    [Test]
    public void AdvanceFloor_IncrementsFloor()
    {
        state.BeginRun();

        state.AdvanceFloor();
        Assert.That(state.CurrentFloor, Is.EqualTo(2));

        state.AdvanceFloor();
        Assert.That(state.CurrentFloor, Is.EqualTo(3));
    }

    [Test]
    public void Finish_MarksRunAsFinished()
    {
        state.BeginRun();

        state.Finish();

        Assert.That(state.IsFinished, Is.True);
    }

    [Test]
    public void Finish_IsIdempotent()
    {
        state.Finish();
        state.Finish();

        Assert.That(state.IsFinished, Is.True);
    }

    [Test]
    public void BeginRun_ResetsFinishedFlag()
    {
        state.BeginRun();
        state.Finish();

        state.BeginRun();

        Assert.That(state.IsFinished, Is.False);
    }
}
