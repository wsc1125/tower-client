using NUnit.Framework;
using UnityEngine;

public class EnemyPatrol2DTests
{
    [Test]
    public void GetNextPosition_MovesTowardTarget()
    {
        var next = EnemyPatrol2D.GetNextPosition(Vector2.zero, Vector2.right, 2f, 0.5f);

        Assert.That(next.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(next.y, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void ShouldSwapTarget_ReturnsTrueNearDestination()
    {
        var shouldSwap = EnemyPatrol2D.ShouldSwapTarget(new Vector2(0.98f, 0f), Vector2.right, 0.05f);

        Assert.That(shouldSwap, Is.True);
    }

    [Test]
    public void ShouldSwapTarget_ReturnsFalseWhenFarFromDestination()
    {
        var shouldSwap = EnemyPatrol2D.ShouldSwapTarget(Vector2.zero, Vector2.right, 0.05f);

        Assert.That(shouldSwap, Is.False);
    }
}
