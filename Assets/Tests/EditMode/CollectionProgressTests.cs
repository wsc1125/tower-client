using NUnit.Framework;

/// <summary>
/// <see cref="CollectionProgress"/> 的三态流转：未获得 → 已获得 → 已点亮。
/// </summary>
public sealed class CollectionProgressTests
{
    [Test]
    public void Obtain_IsIdempotent()
    {
        var progress = new CollectionProgress();

        Assert.That(progress.Obtain("A"), Is.True, "首次获得应返回 true");
        Assert.That(progress.Obtain("A"), Is.False, "重复获得应返回 false");
        Assert.That(progress.ObtainedIds, Has.Count.EqualTo(1), "不应产生重复记录");
    }

    [Test]
    public void Obtain_WithNullOrEmpty_IsIgnored()
    {
        var progress = new CollectionProgress();

        Assert.That(progress.Obtain(null), Is.False);
        Assert.That(progress.Obtain(string.Empty), Is.False);
        Assert.That(progress.ObtainedIds, Is.Empty);
    }

    [Test]
    public void TryLight_RequiresObtained()
    {
        var progress = new CollectionProgress();

        Assert.That(progress.TryLight("A"), Is.False, "未获得的物品不能点亮");
        Assert.That(progress.IsLit("A"), Is.False);
    }

    [Test]
    public void TryLight_SucceedsAfterObtain()
    {
        var progress = new CollectionProgress();
        progress.Obtain("A");

        Assert.That(progress.TryLight("A"), Is.True);
        Assert.That(progress.IsLit("A"), Is.True);
    }

    [Test]
    public void TryLight_IsIrreversible()
    {
        var progress = new CollectionProgress();
        progress.Obtain("A");
        progress.TryLight("A");

        Assert.That(progress.TryLight("A"), Is.False, "已点亮的物品不能再次点亮");
    }

    [Test]
    public void Reconcile_IsIdempotentOnConsistentData()
    {
        var progress = new CollectionProgress();
        progress.Obtain("A");
        progress.TryLight("A");

        progress.Reconcile();

        Assert.That(progress.IsObtained("A"), Is.True);
        Assert.That(progress.IsLit("A"), Is.True);
        Assert.That(progress.ObtainedIds, Has.Count.EqualTo(1), "对已一致的数据调用不应产生副作用");
        Assert.That(progress.LitIds, Has.Count.EqualTo(1));
    }

    [Test]
    public void LitCountWithin_IgnoresIdsNotInValidSet()
    {
        var progress = new CollectionProgress();
        progress.Obtain("A");
        progress.Obtain("Orphan");
        progress.TryLight("A");
        progress.TryLight("Orphan");

        var count = progress.LitCountWithin(new[] { "A", "B" });

        Assert.That(count, Is.EqualTo(1), "孤儿 ID 不应计入进度");
    }

    [Test]
    public void ObtainedCountWithin_IgnoresIdsNotInValidSet()
    {
        var progress = new CollectionProgress();
        progress.Obtain("A");
        progress.Obtain("Orphan");

        var count = progress.ObtainedCountWithin(new[] { "A", "B" });

        Assert.That(count, Is.EqualTo(1));
    }
}
