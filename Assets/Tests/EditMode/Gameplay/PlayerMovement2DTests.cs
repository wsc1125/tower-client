using NUnit.Framework;
using UnityEngine;

public class PlayerMovement2DTests
{
    [Test]
    public void ResolveMovementInput_UsesKeyboardWhenJoystickIsZero()
    {
        var resolved = PlayerMovement2D.ResolveMovementInput(Vector2.right, Vector2.zero);

        Assert.That(resolved, Is.EqualTo(Vector2.right));
    }

    [Test]
    public void ResolveMovementInput_UsesJoystickWhenKeyboardIsZero()
    {
        var resolved = PlayerMovement2D.ResolveMovementInput(Vector2.zero, Vector2.up * 0.5f);

        Assert.That(resolved, Is.EqualTo(Vector2.up * 0.5f));
    }

    [Test]
    public void ResolveMovementInput_ClampsCombinedKeyboardAndJoystick()
    {
        var resolved = PlayerMovement2D.ResolveMovementInput(Vector2.right, Vector2.up);

        Assert.That(resolved.magnitude, Is.EqualTo(1f).Within(0.001f));
        Assert.That(resolved.x, Is.GreaterThan(0f));
        Assert.That(resolved.y, Is.GreaterThan(0f));
    }
}
