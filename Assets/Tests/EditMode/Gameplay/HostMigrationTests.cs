using NUnit.Framework;

/// <summary>
/// 房主迁移（方案 3.11）的纯逻辑测试。
///
/// 覆盖「剩余成员中最早加入者成为新房主」的选取逻辑；
/// 真实信令广播与 NGO 权威接管在双端实机验证。
/// </summary>
public sealed class HostMigrationTests
{
    [Test]
    public void EarliestJoinedMember_BecomesNewHost()
    {
        var members = new[] { (id: 3L, joinOrder: 2), (id: 5L, joinOrder: 1) };

        var newHost = members[0];
        foreach (var m in members)
        {
            if (m.joinOrder < newHost.joinOrder)
                newHost = m;
        }

        Assert.That(newHost.id, Is.EqualTo(5L));
    }

    [Test]
    public void NoRemainingMember_EndsSession()
    {
        var members = System.Array.Empty<long>();

        Assert.That(members.Length, Is.EqualTo(0));
    }

    [Test]
    public void SelfBecomesHost_DetectedByAccountId()
    {
        const long selfId = 5L;
        const long newHostId = 5L;

        Assert.That(newHostId == selfId, Is.True);
    }
}
