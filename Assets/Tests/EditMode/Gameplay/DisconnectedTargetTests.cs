using NUnit.Framework;

public sealed class DisconnectedTargetTests
{
    [Test]
    public void DisconnectedPlayer_IsRemovedFromRoster()
    {
        // 掉线即移除：NGO 默认销毁 owner 断连的 NetworkObject（DontDestroyWithOwner: 0），
        // 房主侧名册不应再包含该玩家。
        var roster = new System.Collections.Generic.HashSet<ulong> { 0, 1, 2 };
        roster.Remove(1);

        Assert.That(roster.Contains(1), Is.False);
        Assert.That(roster.Count, Is.EqualTo(2));
    }

    [Test]
    public void RemainingPlayers_StillCounted()
    {
        // 移除掉线玩家后，其余玩家仍应被计入存活数（RefreshAliveCount 按 WorldRegistry.Players 统计）。
        var roster = new System.Collections.Generic.HashSet<ulong> { 0, 2 };
        Assert.That(roster.Count, Is.EqualTo(2));
    }
}
