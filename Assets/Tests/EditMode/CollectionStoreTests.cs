using NUnit.Framework;
using UnityEngine;

/// <summary>
/// <see cref="CollectionStore"/> 的存档往返与容错。
/// </summary>
public sealed class CollectionStoreTests
{
    [SetUp]
    public void SetUp()
    {
        CollectionStore.Delete();
    }

    [TearDown]
    public void TearDown()
    {
        CollectionStore.Delete();
    }

    [Test]
    public void Load_WithMissingKey_ReturnsEmptyProgress()
    {
        var progress = CollectionStore.Load();

        Assert.That(progress.ObtainedIds, Is.Empty);
        Assert.That(progress.LitIds, Is.Empty);
        Assert.That(progress.AllCollectedRewarded, Is.False);
    }

    [Test]
    public void SaveAndLoad_RoundTripsProgress()
    {
        var progress = new CollectionProgress();
        progress.Obtain("ScrapAlloy");
        progress.Obtain("RationPack");
        progress.TryLight("ScrapAlloy");
        progress.AllCollectedRewarded = true;

        CollectionStore.Save(progress);
        var loaded = CollectionStore.Load();

        Assert.That(loaded.IsObtained("ScrapAlloy"), Is.True);
        Assert.That(loaded.IsObtained("RationPack"), Is.True);
        Assert.That(loaded.IsLit("ScrapAlloy"), Is.True);
        Assert.That(loaded.IsLit("RationPack"), Is.False);
        Assert.That(loaded.AllCollectedRewarded, Is.True);
    }

    [Test]
    public void Load_WithCorruptedJson_ReturnsEmptyProgress()
    {
        PlayerPrefs.SetString(CollectionStore.PrefsKey, "{ this is not json");
        PlayerPrefs.Save();

        var progress = CollectionStore.Load();

        Assert.That(progress.ObtainedIds, Is.Empty, "损坏数据应回退空进度");
    }

    [Test]
    public void Load_WithExplicitNullLists_ReturnsEmptyProgress()
    {
        // JsonUtility 对 JSON 中显式 null 的列表字段会置 null，Validate() 负责补齐。
        PlayerPrefs.SetString(
            CollectionStore.PrefsKey,
            "{\"version\":1,\"obtainedIds\":null,\"litIds\":null,\"allCollectedRewarded\":false}");
        PlayerPrefs.Save();

        var progress = CollectionStore.Load();

        Assert.That(progress.ObtainedIds, Is.Empty, "显式 null 的列表应被补齐为空");
        Assert.That(progress.LitIds, Is.Empty);
    }

    [Test]
    public void Load_ReconcilesLitIdsIntoObtainedIds()
    {
        // 手工写入一份 litIds 有、obtainedIds 没有的脏存档。
        PlayerPrefs.SetString(
            CollectionStore.PrefsKey,
            "{\"version\":1,\"obtainedIds\":[],\"litIds\":[\"A\"],\"allCollectedRewarded\":false}");
        PlayerPrefs.Save();

        var progress = CollectionStore.Load();

        Assert.That(progress.IsObtained("A"), Is.True, "加载时应自动校正");
    }

    [Test]
    public void Delete_RemovesSave()
    {
        var progress = new CollectionProgress();
        progress.Obtain("A");
        CollectionStore.Save(progress);

        CollectionStore.Delete();

        Assert.That(CollectionStore.HasSave, Is.False);
    }
}
