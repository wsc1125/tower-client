using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
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
        // FloorPortal2D 是 NetworkBehaviour，需要 NetworkObject 才能挂载。
        // 测试中不启动 NGO，IsSpawned 恒为 false，走本地分支。
        portalObject.AddComponent<NetworkObject>();
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
    public void RemovePlayer_ClearsTrackedColliderAndAllowsFreshEntry()
    {
        var collider = playerObject.GetComponent<Collider2D>();
        var insideField = typeof(FloorPortal2D).GetField("playersInside", BindingFlags.Instance | BindingFlags.NonPublic);
        var trackedField = typeof(FloorPortal2D).GetField("trackedColliders", BindingFlags.Instance | BindingFlags.NonPublic);
        var remove = typeof(FloorPortal2D).GetMethod("RemovePlayer", BindingFlags.Instance | BindingFlags.Public);
        const ulong clientId = 42;
        var inside = (HashSet<ulong>)insideField.GetValue(portal);
        var tracked = (Dictionary<Collider2D, ulong>)trackedField.GetValue(portal);
        var teammate = new GameObject("Teammate", typeof(BoxCollider2D));
        try
        {
            var teammateCollider = teammate.GetComponent<Collider2D>();
            tracked[collider] = clientId;
            tracked[teammateCollider] = 43;
            inside.Add(clientId);
            inside.Add(43);

            Assert.That(remove, Is.Not.Null);
            remove.Invoke(portal, new object[] { clientId });
            Assert.That(portal.IsPlayerInside(clientId), Is.False);
            Assert.That(tracked.ContainsKey(collider), Is.False);
            Assert.That(portal.IsPlayerInside(43), Is.True);
            Assert.That(tracked[teammateCollider], Is.EqualTo(43UL));
        }
        finally
        {
            Object.DestroyImmediate(teammate);
        }

        // A subsequent trigger enter must be able to register the same player anew.
        tracked[collider] = clientId;
        inside.Add(clientId);
        Assert.That(portal.IsPlayerInside(clientId), Is.True);
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
