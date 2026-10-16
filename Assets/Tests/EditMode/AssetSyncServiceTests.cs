using NUnit.Framework;
using Tower.Network;
using UnityEngine;

/// <summary>
/// <see cref="AssetSyncService"/> 的生命周期与接线冒烟（纯同步，不驱动协程）：
/// 实例注册、初始状态、静态 MarkDirty 的空实例安全性、登出复位。
/// 登录拉取/上传的完整链路依赖协程与真实服务端，放在手动验收清单（计划 Task 8）。
/// </summary>
public sealed class AssetSyncServiceTests
{
    private GameObject host;

    [SetUp]
    public void SetUp()
    {
        PlayerSaveStore.Delete();
        PlayerSaveStore.Delete("account-alpha");
        PlayerPrefs.DeleteKey("AssetSyncPending:account:account-alpha");
        PlayerPrefs.DeleteKey("StarCoins:account:account-alpha");
        CollectionStore.Delete("account-alpha");
        CollectionStore.Delete();
        GameSession.ResetForTests();
        CollectionService.ResetForTests();
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null)
            Object.DestroyImmediate(host);

        PlayerSaveStore.Delete();
        PlayerSaveStore.Delete("account-alpha");
        PlayerPrefs.DeleteKey("AssetSyncPending:account:account-alpha");
        PlayerPrefs.DeleteKey("StarCoins:account:account-alpha");
        CollectionStore.Delete("account-alpha");
        CollectionStore.Delete();
        PlayerPrefs.Save();
        GameSession.ResetForTests();
        CollectionService.ResetForTests();
    }

    [Test]
    public void Instance_ClearedOnDestroy()
    {
        host = new GameObject("sync");
        host.AddComponent<AssetSyncService>();

        // AddComponent 后 Awake 在 EditMode 下不保证同步执行，因此不在此断言 Instance 已注册；
        // 只验证销毁后一定被清空（避免悬挂引用）。
        Object.DestroyImmediate(host);
        host = null;
        Assert.That(AssetSyncService.Instance, Is.Null);
    }

    [Test]
    public void MarkDirty_WithoutInstance_IsSafe()
    {
        Assert.DoesNotThrow(() => AssetSyncService.MarkDirty());
    }

    [Test]
    public void OnAccountBound_EntersSyncingImmediately()
    {
        host = new GameObject("sync");
        var service = host.AddComponent<AssetSyncService>();
        service.Initialize(new ApiClient(new NetworkConfig { baseUrl = "http://127.0.0.1:1" }));

        service.OnAccountBound("account-alpha");

        // 拉取是异步的，但状态必须立刻切到 Syncing —— 出发门禁据此保持拦截。
        Assert.That(service.State, Is.EqualTo(AssetSyncState.Syncing));
    }

    [Test]
    public void OnLoggedOut_ResetsToNotLoggedIn()
    {
        host = new GameObject("sync");
        var service = host.AddComponent<AssetSyncService>();

        service.OnLoggedOut();

        Assert.That(service.State, Is.EqualTo(AssetSyncState.NotLoggedIn));
    }
}
