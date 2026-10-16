using NUnit.Framework;
using Tower.Netcode;

public sealed class ReconnectGraceTests
{
    [Test]
    public void MemberInGame_EntersGrace()
    {
        // 成员端在战斗中掉线 → 进入宽限期。
        Assert.That(MultiplayerCoordinator.ShouldEnterGrace(MultiplayerState.InGame, false), Is.True);
    }

    [Test]
    public void HostInGame_DoesNotEnterGrace()
    {
        // 房主掉线不走宽限期（房主是拓扑中心，仍走解散）。
        Assert.That(MultiplayerCoordinator.ShouldEnterGrace(MultiplayerState.InGame, true), Is.False);
    }

    [Test]
    public void MemberIdle_DoesNotEnterGrace()
    {
        // 未进战斗（主菜单）掉线无需宽限期。
        Assert.That(MultiplayerCoordinator.ShouldEnterGrace(MultiplayerState.Idle, false), Is.False);
    }

    [Test]
    public void GraceSeconds_IsFifteen()
    {
        Assert.That(MultiplayerCoordinator.ReconnectGraceSeconds, Is.EqualTo(15f).Within(0.001f));
    }
}
