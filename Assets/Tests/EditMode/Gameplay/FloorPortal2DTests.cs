using NUnit.Framework;
using UnityEngine;

public sealed class FloorPortal2DTests
{
    private GameObject portalObject;
    private GameObject playerObject;
    private FloorPortal2D portal;

    [SetUp]
    public void SetUp()
    {
        portalObject = new GameObject("Portal");
        portalObject.AddComponent<CircleCollider2D>().isTrigger = true;
        portal = portalObject.AddComponent<FloorPortal2D>();

        playerObject = new GameObject("Player");
        playerObject.AddComponent<Rigidbody2D>();
        playerObject.AddComponent<BoxCollider2D>();
        playerObject.AddComponent<PlayerMovement2D>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(portalObject);
        Object.DestroyImmediate(playerObject);
    }

    [Test]
    public void Configure_SetsKind()
    {
        portal.Configure(PortalKind.Retreat);

        Assert.That(portal.Kind, Is.EqualTo(PortalKind.Retreat));
    }

    [Test]
    public void TryEnter_RaisesEnteredWithKind()
    {
        portal.Configure(PortalKind.Next);
        PortalKind? received = null;
        portal.Entered += kind => received = kind;

        var entered = portal.TryEnter(playerObject.GetComponent<Collider2D>());

        Assert.That(entered, Is.True);
        Assert.That(received, Is.EqualTo(PortalKind.Next));
    }

    [Test]
    public void TryEnter_IgnoresNonPlayerCollider()
    {
        portal.Configure(PortalKind.Next);
        var raised = false;
        portal.Entered += _ => raised = true;

        var other = new GameObject("Enemy");
        other.AddComponent<BoxCollider2D>();

        var entered = portal.TryEnter(other.GetComponent<Collider2D>());

        Assert.That(entered, Is.False);
        Assert.That(raised, Is.False);

        Object.DestroyImmediate(other);
    }

    [Test]
    public void TryEnter_OnlyFiresOnce()
    {
        portal.Configure(PortalKind.Next);
        var count = 0;
        portal.Entered += _ => count++;

        var collider = playerObject.GetComponent<Collider2D>();
        portal.TryEnter(collider);
        portal.TryEnter(collider);

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void TryEnter_IgnoresNullCollider()
    {
        portal.Configure(PortalKind.Next);
        var raised = false;
        portal.Entered += _ => raised = true;

        Assert.That(portal.TryEnter(null), Is.False);
        Assert.That(raised, Is.False);
    }
}
