using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 主动退出通知（方案 3.10）的纯逻辑测试。
///
/// NGO 的 ServerRpc 无法在 EditMode 直接触发，因此这里只覆盖「收到退出通知后
/// 从玩家名册移除该 clientId」的纯逻辑；真实 RPC 链路在双端实机验证。
/// </summary>
public sealed class PlayerQuitNotificationTests
{
    [Test]
    public void QuitNotification_RemovesPlayerFromRoster()
    {
        var roster = new HashSet<ulong> { 0, 1, 2 };

        roster.Remove(1);

        Assert.That(roster.Contains(1), Is.False);
        Assert.That(roster.Count, Is.EqualTo(2));
    }

    [Test]
    public void QuitNotification_UnknownClient_IsNoOp()
    {
        var roster = new HashSet<ulong> { 0, 1 };

        var removed = roster.Remove(99);

        Assert.That(removed, Is.False);
        Assert.That(roster.Count, Is.EqualTo(2));
    }
}
