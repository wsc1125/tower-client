using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tower.Netcode;
using Unity.Netcode;
using UnityEngine;

public sealed class WebSocketRelayTransportTests
{
    private GameObject owner;
    private WebSocketRelayTransport transport;
    private readonly List<(NetworkEvent Type, ulong Id)> events = new List<(NetworkEvent, ulong)>();

    [SetUp]
    public void SetUp()
    {
        owner = new GameObject("RelayTransportTest");
        transport = owner.AddComponent<WebSocketRelayTransport>();
        SetField("isServer", true);
        transport.OnTransportEvent += (type, id, payload, time) => events.Add((type, id));
        events.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(owner);
    }

    [Test]
    public void Host_JoinedThenDataThenLeft_UsesAccountId_GraceExpiryPreservesOrder()
    {
        Control("peer_joined", 1234567890123);
        var frame = new byte[] { 1, 0, 0, 1, 31, 113, 251, 4, 203, 42 };
        Message("HandleData", frame);
        Control("peer_left", 1234567890123);
        PendingLefts[1234567890123] = 0f; // 宽限耗尽，投递 Disconnect。
        Pump();
        Assert.That(events, Is.EqualTo(new[]
        {
            (NetworkEvent.Connect, 1234567890123UL),
            (NetworkEvent.Data, 1234567890123UL),
            (NetworkEvent.Disconnect, 1234567890123UL),
        }));
    }

    [Test]
    public void Host_DuplicateControlsAndUnknownLeft_DoNotRepeatEvents_AndRejoinRestoresSilently()
    {
        Control("peer_left", 10);
        Control("peer_joined", 10);
        Control("peer_joined", 10);
        Control("peer_left", 10);
        Control("peer_left", 10);
        Control("peer_joined", 10);
        Pump();

        // 新语义：peer_left 进入宽限（无事件），后续 peer_joined 静默恢复（无第二个 Connect）。
        // 只有首个 peer_joined 投递 Connect；未知 ID 的 peer_left 不进宽限。
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 10UL) }));
        Assert.That(PendingLefts.ContainsKey(10), Is.False, "重连场景宽限必须被 peer_joined 撤销");
        Assert.That(ConnectedPeers.Contains(10UL), Is.True);
    }

    [Test]
    public void Host_InvalidIdsAndWelcome_DoNotConnect()
    {
        Control("peer_joined", 0);
        Control("peer_joined", -1);
        Control("peer_left", -1);
        Control("welcome", 10);
        Pump();
        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Client_DuplicateWelcome_EmitOnce_AndCloseEntersReconnectInsteadOfDisconnect()
    {
        SetField("isServer", false);
        Control("peer_joined", 10);
        Control("peer_left", 10);
        Control("welcome", 10);
        Control("welcome", 10);
        Close();
        Close();
        Control("welcome", 10);
        Pump();

        // 旧语义：Close 立即投递 Disconnect。
        // 新语义：Close 进入传输层重连状态机，重连耗尽才向 NGO 投递事件；
        // welcome 的重复投递被 welcomed 门卫挡住（重连后服务端会重发 welcome）。
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 0UL) }));
        Assert.That(transport.IsReconnecting, Is.True);
    }

    [Test]
    public void Shutdown_DropsQueuedEventsAndRejectsOldSessionCallbacks()
    {
        var oldSession = Session;
        Control("peer_joined", 10);
        transport.Shutdown();
        Message("HandleControl", "{\"type\":\"peer_joined\",\"client_id\":20}", oldSession);
        Close(oldSession);
        Pump();
        Assert.That(events, Is.Empty);
        Assert.That(transport.IsReconnecting, Is.False, "Shutdown 必须重置重连状态");
    }

    [Test]
    public void Host_ClosureEntersReconnect_ExhaustionEmitsOneTransportFailure()
    {
        Close();
        Close();
        Control("peer_joined", 10);

        // 重连耗尽：强制宽限截止为 0，驱动 5 次失败尝试 + 1 次收尾判定。
        for (var i = 0; i < MaxReconnectSteps; i++)
        {
            SetField("reconnectDeadline", 0f);
            Pump();
        }

        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.TransportFailure, 0UL) }));
        Assert.That(transport.IsReconnecting, Is.False);
    }

    [Test]
    public void Host_PeerLeft_EntersGrace_NoImmediateDisconnect()
    {
        Control("peer_joined", 10);
        Control("peer_left", 10);
        Pump();

        // 宽限内不投递 Disconnect：成员可能正在传输层重连。
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 10UL) }));
        Assert.That(PendingLefts.ContainsKey(10), Is.True);
        Assert.That(ConnectedPeers.Contains(10UL), Is.False);
    }

    [Test]
    public void Host_ResumeDuringGrace_RestoresSilently_AndAcceptsData()
    {
        Control("peer_joined", 10);
        Control("peer_left", 10);
        Control("resume", 10);
        Pump();
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 10UL) }),
            "resume 撤销宽限时不投递任何事件，NGO 无感知");

        var frame = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 10, 7 };
        Message("HandleData", frame);
        Pump();
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 10UL), (NetworkEvent.Data, 10UL) }),
            "恢复后数据帧门卫应放行");
    }

    [Test]
    public void Host_PeerJoinedDuringGrace_RestoresSilently()
    {
        // resume 丢失时的兜底：宽限中收到 peer_joined 同样静默恢复。
        Control("peer_joined", 10);
        Control("peer_left", 10);
        Control("peer_joined", 10);
        Pump();
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 10UL) }));
        Assert.That(PendingLefts.ContainsKey(10), Is.False);
        Assert.That(ConnectedPeers.Contains(10UL), Is.True);
    }

    [Test]
    public void Host_GraceExpiry_EmitsDisconnectOnce()
    {
        Control("peer_joined", 10);
        Control("peer_left", 10);
        PendingLefts[10] = 0f; // 宽限耗尽。
        Pump();
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 10UL), (NetworkEvent.Disconnect, 10UL) }));

        Pump();
        Assert.That(events.Count, Is.EqualTo(2), "宽限投递必须幂等");
    }

    [Test]
    public void CleanupDuringDispatch_DropsRemainingQueuedEvents()
    {
        transport.OnTransportEvent += (type, id, payload, time) => transport.Shutdown();
        Control("peer_joined", 10);
        Control("peer_left", 10);
        Pump();
        Assert.That(events, Is.EqualTo(new[] { (NetworkEvent.Connect, 10UL) }));
    }

    private int Session => (int)(typeof(WebSocketRelayTransport)
        .GetField("sessionId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(transport) ?? 0);

    /// <summary>重连耗尽模拟步数：5 次失败尝试 + 1 次收尾判定。</summary>
    private const int MaxReconnectSteps = WebSocketRelayTransport.MaxReconnectAttempts + 1;

    private System.Collections.Generic.Dictionary<ulong, float> PendingLefts =>
        (System.Collections.Generic.Dictionary<ulong, float>)typeof(WebSocketRelayTransport)
            .GetField("pendingLefts", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(transport);

    private HashSet<ulong> ConnectedPeers => (HashSet<ulong>)typeof(WebSocketRelayTransport)
        .GetField("connectedPeers", BindingFlags.Instance | BindingFlags.NonPublic)
        .GetValue(transport);

    private void SetField(string name, object value) => typeof(WebSocketRelayTransport)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(transport, value);

    private void Control(string type, long id) => Message("HandleControl",
        $"{{\"type\":\"{type}\",\"client_id\":{id}}}");

    private void Message(string method, object value, int? session = null)
    {
        var target = typeof(WebSocketRelayTransport).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        target.Invoke(transport, target.GetParameters().Length == 1
            ? new[] { value } : new[] { value, (object)(session ?? Session) });
    }

    private void Close(int? session = null)
    {
        var target = typeof(WebSocketRelayTransport).GetMethod("OnSocketClosed", BindingFlags.Instance | BindingFlags.NonPublic);
        target.Invoke(transport, target.GetParameters().Length == 0 ? null : new object[] { session ?? Session });
    }

    private void Pump() => typeof(WebSocketRelayTransport)
        .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(transport, null);
}
