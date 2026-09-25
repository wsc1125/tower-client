using NUnit.Framework;
using UnityEngine;

public sealed class TowerRunStateTests
{
    private TowerRunState state;

    [SetUp]
    public void SetUp()
    {
        state = new TowerRunState();
    }

    [Test]
    public void BeginRun_StartsAtFloorOneWithEmptyLoot()
    {
        state.AdvanceFloor();
        state.RecordLoot(ScriptableObject.CreateInstance<ItemDefinition>());

        state.BeginRun();

        Assert.That(state.CurrentFloor, Is.EqualTo(1));
        Assert.That(state.CollectedLoot.Count, Is.Zero);
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
    public void RecordLoot_AppendsInOrderAndIgnoresNull()
    {
        var first = ScriptableObject.CreateInstance<ItemDefinition>();
        var second = ScriptableObject.CreateInstance<ItemDefinition>();

        state.BeginRun();
        state.RecordLoot(first);
        state.RecordLoot(null);
        state.RecordLoot(second);

        Assert.That(state.CollectedLoot.Count, Is.EqualTo(2));
        Assert.That(state.CollectedLoot[0], Is.SameAs(first));
        Assert.That(state.CollectedLoot[1], Is.SameAs(second));

        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
    }

    [Test]
    public void ClearLoot_EmptiesCollectedLoot()
    {
        state.BeginRun();
        state.RecordLoot(ScriptableObject.CreateInstance<ItemDefinition>());

        state.ClearLoot();

        Assert.That(state.CollectedLoot.Count, Is.Zero);
    }

    [Test]
    public void Finish_MarksRunAsFinished()
    {
        state.BeginRun();

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
