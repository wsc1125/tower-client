using NUnit.Framework;

public sealed class ReconnectRestoreTests
{
    [SetUp]
    public void SetUp()
    {
        // 每个测试从干净的冻结状态开始（静态字段跨测试残留）。
        LocalPauseScope.Unfreeze();
        LocalPauseScope.SetSpectating(false);
    }

    [TearDown]
    public void TearDown()
    {
        LocalPauseScope.Unfreeze();
        LocalPauseScope.SetSpectating(false);
    }

    [Test]
    public void Reconnect_ShouldUnfreezeLocalInput()
    {
        // 宽限期冻结过本地输入，重连成功后必须解冻，否则角色永久不能动。
        LocalPauseScope.Freeze();
        Assert.That(LocalPauseScope.IsFrozen, Is.True);

        LocalPauseScope.Unfreeze();
        Assert.That(LocalPauseScope.IsFrozen, Is.False);
    }

    [Test]
    public void Unfreeze_DoesNotClearSpectateFreeze()
    {
        // 解冻普通冻结不应影响观战冻结（死亡观战玩家重连后仍应保持观战）。
        LocalPauseScope.SetSpectating(true);
        LocalPauseScope.Freeze();
        LocalPauseScope.Unfreeze();

        Assert.That(LocalPauseScope.IsFrozen, Is.True, "观战冻结应保留");
    }
}
