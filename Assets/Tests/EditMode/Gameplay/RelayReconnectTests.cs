using NUnit.Framework;
using Tower.Netcode;

public sealed class RelayReconnectTests
{
    [Test]
    public void Backoff_GrowsExponentially_AndIsCapped()
    {
        // 退避序列：1s / 2s / 4s / 8s / 8s（上限 8 秒）。
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(1), Is.EqualTo(1f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(2), Is.EqualTo(2f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(3), Is.EqualTo(4f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(4), Is.EqualTo(8f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(5), Is.EqualTo(8f).Within(0.001f));
    }

    [Test]
    public void ShouldGiveUp_OnlyAfterMaxAttempts()
    {
        // 未达上限：继续重连。
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(1), Is.False);
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(4), Is.False);

        // 达到上限：放弃，投递失败事件。
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(5), Is.True);
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(6), Is.True);
    }
}
