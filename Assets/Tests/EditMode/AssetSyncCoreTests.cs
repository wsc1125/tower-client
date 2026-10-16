using NUnit.Framework;
using Tower.Network;
using UnityEngine;

/// <summary>
/// 云同步核心逻辑：去抖 / 上限 / 退避 / 断点续传标记 / 快照打包落地。
/// 时间全部手动注入，不依赖帧时钟。
/// </summary>
public sealed class AssetSyncCoreTests
{
    private ItemDatabase database;
    private AssetSyncCore core;

    [SetUp]
    public void SetUp()
    {
        PlayerSaveStore.Delete();
        PlayerSaveStore.Delete("account-alpha");
        PlayerPrefs.DeleteKey(new AssetSyncCore().PendingKey("account-alpha"));
        PlayerPrefs.DeleteKey("StarCoins:account:account-alpha");
        CollectionStore.Delete("account-alpha");
        CollectionStore.Delete();
        GameSession.ResetForTests();
        CollectionService.ResetForTests();

        database = ScriptableObject.CreateInstance<ItemDatabase>();
        core = new AssetSyncCore();
        core.BindAccount("account-alpha");
    }

    [TearDown]
    public void TearDown()
    {
        PlayerSaveStore.Delete();
        PlayerSaveStore.Delete("account-alpha");
        PlayerPrefs.DeleteKey(new AssetSyncCore().PendingKey("account-alpha"));
        PlayerPrefs.DeleteKey("StarCoins:account:account-alpha");
        CollectionStore.Delete("account-alpha");
        CollectionStore.Delete();
        PlayerPrefs.Save();
        GameSession.ResetForTests();
        CollectionService.ResetForTests();
        Object.DestroyImmediate(database);
    }

    // ---- 脏标记与去抖 ----

    [Test]
    public void MarkDirty_NotDueInsideDebounceWindow()
    {
        core.MarkDirty(0f);

        Assert.That(core.IsDirty, Is.True);
        Assert.That(core.IsUploadDue(4.9f), Is.False);
        Assert.That(core.IsUploadDue(5f), Is.True);
    }

    [Test]
    public void ContinuousChanges_ForceUploadAtCap()
    {
        // 从 0 到 29 秒每秒都在变：去抖窗口永远打不开，但 30 秒上限必须触发。
        for (var t = 0f; t <= 29f; t += 1f)
            core.MarkDirty(t);

        Assert.That(core.IsUploadDue(29.9f), Is.False);
        Assert.That(core.IsUploadDue(30f), Is.True);
    }

    [Test]
    public void MarkDirty_WhenSuppressed_IsIgnored()
    {
        core.SetSuppress(true);
        core.MarkDirty(0f);

        Assert.That(core.IsDirty, Is.False);
    }

    [Test]
    public void MarkDirty_WithoutAccount_IsIgnored()
    {
        var unbound = new AssetSyncCore();
        unbound.MarkDirty(0f);

        Assert.That(unbound.IsDirty, Is.False);
    }

    [Test]
    public void OnUploadSucceeded_ClearsDirtyAndPending()
    {
        core.MarkDirty(0f);
        core.OnUploadSucceeded(1f);

        Assert.That(core.IsDirty, Is.False);
        Assert.That(core.IsUploadDue(999f), Is.False);
        Assert.That(PlayerPrefs.GetInt(core.PendingKey("account-alpha"), 0), Is.EqualTo(0));
    }

    // ---- 退避 ----

    [Test]
    public void UploadFailed_BackoffGrowsExponentially_CappedAt60()
    {
        core.MarkDirty(0f);
        core.OnUploadFailed(0f);
        Assert.That(core.IsUploadDue(4.9f), Is.False);
        Assert.That(core.IsUploadDue(5f), Is.True);

        core.OnUploadFailed(5f);
        Assert.That(core.IsUploadDue(14.9f), Is.False);
        Assert.That(core.IsUploadDue(15f), Is.True);

        // 连续失败到封顶：第 5 次起间隔恒为 60 秒。
        core.OnUploadFailed(15f);
        core.OnUploadFailed(15f);
        core.OnUploadFailed(15f);
        core.OnUploadFailed(15f);
        core.OnUploadFailed(15f);
        Assert.That(core.IsUploadDue(15f + 59.9f), Is.False);
        Assert.That(core.IsUploadDue(15f + 60f), Is.True);
    }

    // ---- 断点续传标记 ----

    [Test]
    public void PendingFlag_PersistsAcrossCoreInstances()
    {
        core.MarkDirty(0f);

        var fresh = new AssetSyncCore();
        Assert.That(fresh.HasPendingUpload("account-alpha"), Is.True);
        Assert.That(fresh.HasPendingUpload("account-beta"), Is.False);
        Assert.That(fresh.HasPendingUpload(GameSession.LocalPlayerId), Is.False);
    }

    // ---- 快照打包与落地 ----

    [Test]
    public void CaptureEnvelope_ReadsContextCollectionAndCurrency()
    {
        var rifle = TestItems.WeaponInstance("步枪");
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);
        PlayerPrefs.SetInt(LocalCacheKeys.For("StarCoins", "account-alpha"), 4321);
        PlayerPrefs.Save();

        var envelope = core.CaptureEnvelope("account-alpha");

        Assert.That(envelope.save.equipment, Has.Count.EqualTo(1));
        Assert.That(envelope.save.equipment[0].itemId, Is.EqualTo(rifle.Definition.Id));
        Assert.That(envelope.collection, Is.Not.Null);
        Assert.That(envelope.currency, Is.EqualTo(4321));
    }

    [Test]
    public void ApplyEnvelope_WritesCachesAndReloadsContext()
    {
        var shotgun = TestItems.WeaponInstance("霰弹枪");
        var rifle = TestItems.WeaponInstance("步枪");
        Register(shotgun.Definition, rifle.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);

        var envelope = new AssetSnapshotEnvelopeDto { currency = 999 };
        envelope.save.warehouse.Add(new PlayerSaveData.SlotEntry("0", shotgun.Definition.Id, shotgun.InstanceId, -1));
        envelope.collection.obtainedIds.Add(shotgun.Definition.Id);

        var loadoutChangedFired = false;
        System.Action handler = () => loadoutChangedFired = true;
        GameSession.LoadoutChanged += handler;
        try
        {
            Assert.That(core.ApplyEnvelope(envelope, "account-alpha"), Is.True);
        }
        finally
        {
            GameSession.LoadoutChanged -= handler;
        }

        // 上下文已与服务端快照一致。
        var loadout = GameSession.LocalPlayer.Loadout;
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
        Assert.That(loadout.GetWarehouse(0).Definition.Id, Is.EqualTo(shotgun.Definition.Id));
        Assert.That(loadoutChangedFired, Is.True, "重载后必须广播变化让 UI 刷新");
        // 缓存键已落地。
        Assert.That(PlayerPrefs.HasKey("StarCoins:account:account-alpha"), Is.True);
        Assert.That(CollectionStore.HasSaveFor("account-alpha"), Is.True);
    }

    [Test]
    public void ApplyEnvelope_Null_ReturnsFalse()
    {
        Assert.That(core.ApplyEnvelope(null, "account-alpha"), Is.False);
    }

    /// <summary>把物品定义注册进测试用 ItemDatabase（存档还原依赖它解析物品 ID）。</summary>
    private void Register(params ItemDefinition[] items)
    {
        var serialized = new UnityEditor.SerializedObject(database);
        var array = serialized.FindProperty("items");
        array.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
