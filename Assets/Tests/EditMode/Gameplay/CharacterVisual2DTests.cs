using NUnit.Framework;
using UnityEngine;

public sealed class CharacterVisual2DTests
{
    [Test]
    public void FacingDirection_ValuesMatchScaleSigns()
    {
        Assert.That((int)FacingDirection.Left, Is.EqualTo(-1));
        Assert.That((int)FacingDirection.Right, Is.EqualTo(1));
    }

    [Test]
    public void TryResolveFacing_PositiveHorizontalVelocity_ResolvesRight()
    {
        var resolved = CharacterVisual2D.TryResolveFacing(new Vector2(3f, 0f), out var facing);

        Assert.That(resolved, Is.True);
        Assert.That(facing, Is.EqualTo(FacingDirection.Right));
    }

    [Test]
    public void TryResolveFacing_NegativeHorizontalVelocity_ResolvesLeft()
    {
        var resolved = CharacterVisual2D.TryResolveFacing(new Vector2(-3f, 0f), out var facing);

        Assert.That(resolved, Is.True);
        Assert.That(facing, Is.EqualTo(FacingDirection.Left));
    }

    [Test]
    public void TryResolveFacing_DiagonalVelocity_UsesHorizontalComponent()
    {
        var resolved = CharacterVisual2D.TryResolveFacing(new Vector2(-3f, 4f), out var facing);

        Assert.That(resolved, Is.True);
        Assert.That(facing, Is.EqualTo(FacingDirection.Left));
    }

    [Test]
    public void TryResolveFacing_VerticalOnlyVelocity_ReturnsFalse()
    {
        var resolved = CharacterVisual2D.TryResolveFacing(new Vector2(0f, 5f), out _);

        Assert.That(resolved, Is.False);
    }

    [Test]
    public void TryResolveFacing_HorizontalWithinDeadZone_ReturnsFalse()
    {
        var resolved = CharacterVisual2D.TryResolveFacing(new Vector2(0.005f, 5f), out _);

        Assert.That(resolved, Is.False);
    }

    [Test]
    public void TryResolveFacing_HorizontalJustPastDeadZone_Resolves()
    {
        var resolved = CharacterVisual2D.TryResolveFacing(new Vector2(0.02f, 0f), out var facing);

        Assert.That(resolved, Is.True);
        Assert.That(facing, Is.EqualTo(FacingDirection.Right));
    }

    [Test]
    public void IsMoving_ZeroVelocity_ReturnsFalse()
    {
        Assert.That(CharacterVisual2D.IsMoving(Vector2.zero), Is.False);
    }

    [Test]
    public void IsMoving_VelocityWithinDeadZone_ReturnsFalse()
    {
        Assert.That(CharacterVisual2D.IsMoving(new Vector2(0.001f, 0.001f)), Is.False);
    }

    [Test]
    public void IsMoving_HorizontalVelocity_ReturnsTrue()
    {
        Assert.That(CharacterVisual2D.IsMoving(new Vector2(1f, 0f)), Is.True);
    }

    [Test]
    public void IsMoving_VerticalOnlyVelocity_ReturnsTrue()
    {
        Assert.That(CharacterVisual2D.IsMoving(new Vector2(0f, 1f)), Is.True);
    }
}
