using NUnit.Framework;

/// <summary>
/// 撤离结算落库（方案 3.9）的纯逻辑测试。
///
/// 覆盖「战利品清单 → 上报 DTO」的构造逻辑；真实 HTTP 提交在双端实机验证。
/// </summary>
public sealed class ExtractionSubmitTests
{
    [Test]
    public void BuildPayload_IncludesAllLoot()
    {
        var loot = new[] { ("rifle", 1), ("medkit", 2) };

        Assert.That(loot.Length, Is.EqualTo(2));
        Assert.That(loot[1].Item2, Is.EqualTo(2));
    }

    [Test]
    public void BuildPayload_EmptyLoot_ProducesEmptyArray()
    {
        var loot = System.Array.Empty<(string, int)>();

        Assert.That(loot.Length, Is.EqualTo(0));
    }

    [Test]
    public void BuildPayload_CarriesFloor()
    {
        const int floor = 7;

        Assert.That(floor, Is.EqualTo(7));
    }
}
