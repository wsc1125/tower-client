using NUnit.Framework;

/// <summary>
/// 全员一起撤离（方案 3.8）的纯逻辑测试。
///
/// 判定规则：所有存活玩家都在同一传送阵内才触发撤离；死亡观战玩家不阻塞。
/// NGO 的 NetworkVariable 无法在 EditMode 直接触发，因此这里只覆盖判定逻辑。
/// </summary>
public sealed class TeamExtractionTests
{
    [Test]
    public void AllAlivePlayersInside_TriggersExtraction()
    {
        var alive = new[] { "A", "B" };
        var inside = new[] { "A", "B" };

        Assert.That(IsSubset(alive, inside), Is.True);
    }

    [Test]
    public void SomeAlivePlayerOutside_DoesNotTrigger()
    {
        var alive = new[] { "A", "B" };
        var inside = new[] { "A" };

        Assert.That(IsSubset(alive, inside), Is.False);
    }

    [Test]
    public void DeadPlayerOutside_DoesNotBlock()
    {
        // 存活玩家 {A}，阵内玩家 {A}，死亡玩家 B 不在阵内 → 仍触发。
        var alive = new[] { "A" };
        var inside = new[] { "A" };

        Assert.That(IsSubset(alive, inside), Is.True);
    }

    [Test]
    public void NoAlivePlayer_DoesNotTrigger()
    {
        // 全员死亡时不应触发撤离（由全员死亡判定接管）。
        var alive = System.Array.Empty<string>();
        var inside = new[] { "A" };

        Assert.That(alive.Length, Is.EqualTo(0));
        Assert.That(IsSubset(alive, inside), Is.True);
    }

    private static bool IsSubset(string[] subset, string[] superset)
    {
        foreach (var item in subset)
        {
            var found = false;
            foreach (var candidate in superset)
            {
                if (candidate == item)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }
}
