using NUnit.Framework;
using Tower.Network;
using UnityEngine;

/// <summary>
/// 资产快照信封的 JSON 往返：字段丢失或改名都会在这里暴露，
/// 服务端契约（设计文档 §5）以本测试为准绳。
/// </summary>
public sealed class AssetSnapshotDtoTests
{
    [Test]
    public void PutRequest_RoundTrip_PreservesAllSections()
    {
        var envelope = new AssetSnapshotEnvelopeDto
        {
            save = new PlayerSaveData(),
            collection = new CollectionSaveData(),
            currency = 1234,
        };
        envelope.save.warehouse.Add(new PlayerSaveData.SlotEntry("0", "rifle", "inst-1", 42));
        envelope.save.equipment.Add(new PlayerSaveData.SlotEntry("MainHand", "rifle", "inst-2", -1));
        envelope.collection.obtainedIds.Add("rifle");
        envelope.collection.litIds.Add("rifle");

        var request = new AssetSnapshotPutRequestDto
        {
            schema_version = AssetSnapshotProtocol.SchemaVersion,
            data = envelope,
        };

        var json = JsonUtility.ToJson(request);
        var back = JsonUtility.FromJson<AssetSnapshotPutRequestDto>(json);

        Assert.That(back.schema_version, Is.EqualTo(1));
        Assert.That(back.data.currency, Is.EqualTo(1234));
        Assert.That(back.data.save.warehouse, Has.Count.EqualTo(1));
        Assert.That(back.data.save.warehouse[0].itemId, Is.EqualTo("rifle"));
        Assert.That(back.data.save.warehouse[0].instanceId, Is.EqualTo("inst-1"));
        Assert.That(back.data.save.warehouse[0].durability, Is.EqualTo(42));
        Assert.That(back.data.save.equipment[0].key, Is.EqualTo("MainHand"));
        Assert.That(back.data.collection.obtainedIds, Does.Contain("rifle"));
        Assert.That(back.data.collection.litIds, Does.Contain("rifle"));
    }

    [Test]
    public void GetResponse_RevisionZero_MeansFirstLogin()
    {
        // JsonUtility 会把 "data":null 物化成完整的空对象图（连内层 save 都非 null），
        // 因此「服务端尚无快照」不能靠 JSON 的 null 表达，改用 revision == 0 这个无歧义信号
        // （有快照时服务端 revision 从 1 起）。
        var json = "{\"schema_version\":1,\"revision\":0}";
        var back = JsonUtility.FromJson<AssetSnapshotResponseDto>(json);

        Assert.That(back.schema_version, Is.EqualTo(1));
        Assert.That(back.revision, Is.EqualTo(0));
    }
}
