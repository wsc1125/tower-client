using NUnit.Framework;
using UnityEngine;

public class PlayerMovement2DTests
{
    [SetUp]
    public void SetUp()
    {
        WorldRegistry.ClearForTests();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var player in Object.FindObjectsOfType<PlayerMovement2D>(true))
            Object.DestroyImmediate(player.gameObject);

        WorldRegistry.ClearForTests();
    }

    [Test]
    public void DisabledRemoteMovement_RemainsRegisteredAsTarget()
    {
        var playerObject = new GameObject("RemotePlayer");
        playerObject.AddComponent<Rigidbody2D>();
        var movement = playerObject.AddComponent<PlayerMovement2D>();

        movement.KeepTargetRegistrationWhenDisabled(true);
        movement.enabled = false;

        Assert.That(WorldRegistry.Players, Does.Contain(movement));
    }

    [Test]
    public void NearestTarget_SwitchesWhenAnotherPlayerMovesCloser()
    {
        var enemy = new GameObject("Enemy");
        enemy.transform.position = Vector3.zero;
        var first = CreatePlayer("First", new Vector2(2f, 0f));
        var second = CreatePlayer("Second", new Vector2(5f, 0f));
        var provider = new NearestPlayerTargetProvider();
        provider.SetSelf(enemy.transform);
        provider.SetLoseRange(10f);

        Assert.That(provider.TryGetTargetPosition(out var initial), Is.True);
        second.transform.position = new Vector2(1f, 0f);
        Assert.That(provider.TryGetTargetPosition(out var afterMove), Is.True);

        Assert.That(initial, Is.EqualTo((Vector2)first.transform.position));
        Assert.That(afterMove, Is.EqualTo((Vector2)second.transform.position));
        Object.DestroyImmediate(enemy);
    }

    [Test]
    public void NearestTarget_KeepsCurrentTargetWhenDistancesAreEqual()
    {
        var enemy = new GameObject("Enemy");
        enemy.transform.position = Vector3.zero;
        var first = CreatePlayer("First", new Vector2(2f, 0f));
        var second = CreatePlayer("Second", new Vector2(4f, 0f));
        var provider = new NearestPlayerTargetProvider();
        provider.SetSelf(enemy.transform);

        Assert.That(provider.TryGetTargetPosition(out _), Is.True);
        second.transform.position = new Vector2(1f, 0f);
        Assert.That(provider.TryGetTargetPosition(out var closer), Is.True);
        first.transform.position = new Vector2(-1f, 0f);
        Assert.That(provider.TryGetTargetPosition(out var tied), Is.True);

        Assert.That(closer, Is.EqualTo((Vector2)second.transform.position));
        Assert.That(tied, Is.EqualTo((Vector2)second.transform.position));
        Object.DestroyImmediate(enemy);
    }

    [Test]
    public void LockedTarget_ReselectsWhenTargetIsUnregistered()
    {
        var enemy = new GameObject("Enemy");
        enemy.transform.position = Vector3.zero;
        var first = CreatePlayer("First", new Vector2(2f, 0f));
        var second = CreatePlayer("Second", new Vector2(4f, 0f));
        var provider = new NearestPlayerTargetProvider();
        provider.SetSelf(enemy.transform);

        Assert.That(provider.TryGetTargetPosition(out _), Is.True);
        WorldRegistry.Unregister(first);
        Assert.That(provider.TryGetTargetPosition(out var reselected), Is.True);

        Assert.That(reselected, Is.EqualTo((Vector2)second.transform.position));
        Object.DestroyImmediate(enemy);
    }

    [Test]
    public void LockedTarget_ReselectsWhenItExceedsLoseRange()
    {
        var enemy = new GameObject("Enemy");
        enemy.transform.position = Vector3.zero;
        var first = CreatePlayer("First", new Vector2(2f, 0f));
        var second = CreatePlayer("Second", new Vector2(4f, 0f));
        var provider = new NearestPlayerTargetProvider();
        provider.SetSelf(enemy.transform);
        provider.SetLoseRange(6f);

        Assert.That(provider.TryGetTargetPosition(out _), Is.True);
        first.transform.position = new Vector2(8f, 0f);
        Assert.That(provider.TryGetTargetPosition(out var reselected), Is.True);

        Assert.That(reselected, Is.EqualTo((Vector2)second.transform.position));
        Object.DestroyImmediate(enemy);
    }

    private static PlayerMovement2D CreatePlayer(string name, Vector2 position)
    {
        var playerObject = new GameObject(name);
        playerObject.transform.position = position;
        playerObject.AddComponent<Rigidbody2D>();
        return playerObject.AddComponent<PlayerMovement2D>();
    }

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
