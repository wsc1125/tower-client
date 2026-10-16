# 玩家资产云存档 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 玩家资产（仓库/装备/收藏/星空币）以整包快照形式云存档到服务器，登录制门槛 + 去抖上传 + 关键结算点强制上传，为后续服务器权威化预留协议。

**Architecture:** 服务端一张 `player_assets` 表 + `GET/PUT /assets/snapshot` 两个接口（服务端不在本仓库，契约见设计文档）。客户端在 Network 程序集新增 `AssetSyncCore`（纯逻辑：脏标记/去抖/退避/断点续传标记/快照打包落地）与 `AssetSyncService`（MonoBehaviour 薄壳：协程驱动、登录拉取、状态机），由 `NetworkSession` 创建并接线；本地 PlayerPrefs 全部降级为账号隔离的缓存。

**Tech Stack:** Unity (JsonUtility + UnityWebRequest 协程), NUnit EditMode 测试（根测试程序集，风格对齐 `GameSessionTests`）。

**Spec:** `docs/superpowers/specs/2026-10-16-player-assets-cloud-save-design.md`

## Global Constraints

- 代码注释一律中文（项目既有惯例）
- 服务端 DTO 只能用 `[Serializable]` + 公共字段（JsonUtility 限制：不支持属性/Dictionary/可空值类型）
- asmdef 依赖方向固定：`Network → Inventory`（已存在）、`Gameplay → Network`（已存在）；**禁止**任何 asmdef 引用 `Assembly-CSharp`
- PlayerPrefs 键只是缓存，登录后一律以服务端快照为准；账号隔离键规则统一走 `LocalCacheKeys.For(baseKey, playerId)`（与 `PlayerSaveStore.KeyFor` 一致：null/`"local"` → 全局键，登录 → `<baseKey>:account:<id>`）
- 局内（`RaidBag` 捡拾）不上传；撤离/死亡/出发/登出/退游戏为强制上传点
- 上传节奏常量：去抖 5s、最大间隔 30s、重试退避 5s 起指数翻倍封顶 60s、登录拉取重试 3 次
- 快照信封 `schema_version = 1`；`data` 上限 256KB
- EditMode 测试放 `Assets/Tests/EditMode/` 根目录（无 asmdef，预定义编辑器程序集，可用 `TestItems` 工具与所有 asmdef 类型）；每个新建 `.cs` 提交时连同 Unity 生成的 `.meta` 一起提交
- 测试运行方式：Unity → Window → General → Test Runner → EditMode（或经 MCP `unity_get_compilation_errors` 先确认编译通过）

---

### Task 1: 快照协议 DTO 与 ApiClient 端点

**Files:**
- Modify: `Assets/Network/Runtime/ApiModels.cs`（文件末尾 `ErrorBodyDto` 之后追加）
- Modify: `Assets/Network/Runtime/ApiClient.cs`（「撤离结算」节之后追加一节）
- Create: `Assets/Tests/EditMode/AssetSnapshotDtoTests.cs`

**Interfaces:**
- Consumes: `ApiClient.Send<T>` / `SendRaw`（现有私有协程）
- Produces: `AssetSnapshotProtocol.SchemaVersion`（int 常量 = 1）、`AssetSnapshotEnvelopeDto { save: PlayerSaveData, collection: CollectionSaveData, currency: int }`、`ApiClient.GetSnapshot(Action<AssetSnapshotResponseDto>, Action<ApiError>)`、`ApiClient.PutSnapshot(AssetSnapshotEnvelopeDto, Action<AssetSnapshotPutResponseDto>, Action<ApiError>)`（后两个均为协程返回 `IEnumerator`）

- [ ] **Step 1: 写失败的 DTO 往返测试**

创建 `Assets/Tests/EditMode/AssetSnapshotDtoTests.cs`：

```csharp
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
        var envelope = new AssetSnapshotEnvelopeDto { currency = 1234 };
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
    public void GetResponse_NullData_MeansFirstLogin()
    {
        var json = "{\"schema_version\":1,\"revision\":7,\"data\":null}";
        var back = JsonUtility.FromJson<AssetSnapshotResponseDto>(json);

        Assert.That(back.schema_version, Is.EqualTo(1));
        Assert.That(back.revision, Is.EqualTo(7));
        Assert.That(back.data, Is.Null);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行：Test Runner → EditMode → `AssetSnapshotDtoTests`
预期：编译错误（`AssetSnapshotEnvelopeDto` 等类型不存在）

- [ ] **Step 3: 在 ApiModels.cs 末尾追加协议类型**

```csharp
    // ---- 资产快照 ----

    /// <summary>资产快照协议常量。</summary>
    public static class AssetSnapshotProtocol
    {
        /// <summary>当前客户端快照信封版本。服务端只透传，高于自身已知版本时返回 400。</summary>
        public const int SchemaVersion = 1;

        /// <summary>快照请求体大小上限（字节）。当前快照约几 KB，仅防滥用。</summary>
        public const int MaxPayloadBytes = 256 * 1024;
    }

    /// <summary>
    /// 资产快照信封：仓库/装备（复用玩家存档 v3 结构）+ 收藏 + 星空币，
    /// 整包原子上下传。对服务端不透明。
    /// </summary>
    [Serializable]
    public sealed class AssetSnapshotEnvelopeDto
    {
        /// <summary>仓库 + 装备槽。直接复用客户端存档结构，零转换。</summary>
        public PlayerSaveData save;

        /// <summary>收藏进度。</summary>
        public CollectionSaveData collection;

        /// <summary>星空币余额。</summary>
        public int currency;
    }

    /// <summary>GET /assets/snapshot 响应。data 为 null 表示服务端尚无快照（首次登录）。</summary>
    [Serializable]
    public sealed class AssetSnapshotResponseDto
    {
        public int schema_version;

        /// <summary>服务端自增版本号，每次 PUT +1。用于排查"谁覆盖了谁"。</summary>
        public long revision;

        public string updated_at;
        public AssetSnapshotEnvelopeDto data;
    }

    /// <summary>PUT /assets/snapshot 请求。</summary>
    [Serializable]
    public sealed class AssetSnapshotPutRequestDto
    {
        public int schema_version;
        public AssetSnapshotEnvelopeDto data;
    }

    /// <summary>PUT /assets/snapshot 响应。</summary>
    [Serializable]
    public sealed class AssetSnapshotPutResponseDto
    {
        public long revision;
        public string updated_at;
    }
```

- [ ] **Step 4: 在 ApiClient.cs 的「撤离结算」节后追加两个端点方法**

```csharp
        // ---- 资产快照 ----

        /// <summary>拉取当前账号的资产快照。data 为 null 表示首次登录（服务端尚无快照）。</summary>
        public IEnumerator GetSnapshot(Action<AssetSnapshotResponseDto> onSuccess, Action<ApiError> onError)
        {
            return Send<AssetSnapshotResponseDto>(UnityWebRequest.kHttpVerbGET, "/assets/snapshot", null, true,
                onSuccess, onError);
        }

        /// <summary>整包上传资产快照（幂等，可安全重试）。服务端 upsert 并自增 revision。</summary>
        public IEnumerator PutSnapshot(AssetSnapshotEnvelopeDto data,
            Action<AssetSnapshotPutResponseDto> onSuccess, Action<ApiError> onError)
        {
            var body = new AssetSnapshotPutRequestDto
            {
                schema_version = AssetSnapshotProtocol.SchemaVersion,
                data = data,
            };

            return Send<AssetSnapshotPutResponseDto>(UnityWebRequest.kHttpVerbPUT, "/assets/snapshot", body, true,
                onSuccess, onError);
        }
```

- [ ] **Step 5: 运行测试确认通过**

运行：Test Runner → EditMode → `AssetSnapshotDtoTests`
预期：2 个测试全部 PASS

- [ ] **Step 6: 提交**

```bash
git add "Assets/Network/Runtime/ApiModels.cs" "Assets/Network/Runtime/ApiClient.cs" "Assets/Tests/EditMode/AssetSnapshotDtoTests.cs" "Assets/Tests/EditMode/AssetSnapshotDtoTests.cs.meta"
git commit -m "feat: 资产快照协议 DTO 与 ApiClient 上下行端点"
```

---

### Task 2: 缓存键工具 + 收藏存档账号隔离

**Files:**
- Create: `Assets/Inventory/Runtime/LocalCacheKeys.cs`
- Modify: `Assets/Inventory/Runtime/CollectionStore.cs`
- Modify: `Assets/Inventory/Runtime/CollectionService.cs`
- Modify: `Assets/Tests/EditMode/CollectionStoreTests.cs`（末尾追加测试）

**Interfaces:**
- Consumes: `GameSession.LocalPlayerIdValue`（现有静态属性）
- Produces: `LocalCacheKeys.For(string baseKey, string playerId = null)`；`CollectionStore.KeyFor(string playerId = null)`、`CollectionStore.LoadData(string playerId = null) → CollectionSaveData`、`CollectionStore.Capture(CollectionProgress) → CollectionSaveData`、`CollectionStore.SaveData(CollectionSaveData, string playerId = null)`、`CollectionStore.Load(string playerId = null)`、`CollectionStore.Save(CollectionProgress, string playerId = null)`；`CollectionService.ProgressSaved`（静态事件）、`CollectionService.InvalidateProgress()`

- [ ] **Step 1: 写失败的测试（CollectionStoreTests.cs 末尾追加）**

```csharp
    [Test]
    public void SaveData_WithAccountId_WritesAccountScopedKey()
    {
        var data = new CollectionSaveData();
        data.obtainedIds.Add("rifle");

        CollectionStore.SaveData(data, "account-alpha");

        Assert.That(PlayerPrefs.HasKey("CollectionSave:account:account-alpha"), Is.True);
        Assert.That(PlayerPrefs.HasKey(CollectionStore.PrefsKey), Is.False);

        var loaded = CollectionStore.LoadData("account-alpha");
        Assert.That(loaded.obtainedIds, Does.Contain("rifle"));

        // local 身份仍读全局键，登录前后互不串档。
        Assert.That(CollectionStore.HasSave, Is.False);
        CollectionStore.Delete("account-alpha");
    }
```

- [ ] **Step 2: 运行测试确认失败**

运行：Test Runner → EditMode → `CollectionStoreTests.SaveData_WithAccountId_WritesAccountScopedKey`
预期：编译错误（`SaveData` / `LoadData` 不存在）

- [ ] **Step 3: 创建 LocalCacheKeys.cs**

```csharp
/// <summary>
/// PlayerPrefs 缓存键的统一命名规则：与 <see cref="PlayerSaveStore"/> 一致 ——
/// 未登录（null / <see cref="GameSession.LocalPlayerId"/>）用全局键，
/// 登录后用「&lt;baseKey&gt;:account:&lt;id&gt;」做账号隔离。
///
/// 仓库/装备、收藏、星空币、同步 pending 标记四类缓存都走这里，
/// 保证同一规则只有一处定义。
/// </summary>
public static class LocalCacheKeys
{
    public static string For(string baseKey, string playerId = null) =>
        string.IsNullOrEmpty(playerId) || playerId == GameSession.LocalPlayerId
            ? baseKey : baseKey + ":account:" + playerId;
}
```

- [ ] **Step 4: 改造 CollectionStore.cs（键隔离 + 数据层/进度层拆分）**

替换整个类体（保留类头注释，追加账号隔离说明）：

```csharp
public static class CollectionStore
{
    public const string PrefsKey = "CollectionSave";

    /// <summary>兼容旧调用的全局键读取判断；登录后请用 <see cref="HasSaveFor"/>。</summary>
    public static bool HasSave => PlayerPrefs.HasKey(PrefsKey);

    public static bool HasSaveFor(string playerId = null) => PlayerPrefs.HasKey(KeyFor(playerId));

    /// <summary>账号隔离的缓存键：未登录为全局键，登录后为账号键。</summary>
    public static string KeyFor(string playerId = null) => LocalCacheKeys.For(PrefsKey, playerId);

    /// <summary>读取原始收藏数据。缺失或解析失败时返回 null，不抛异常。</summary>
    public static CollectionSaveData LoadData(string playerId = null)
    {
        var key = KeyFor(playerId);
        if (!PlayerPrefs.HasKey(key))
            return null;

        var json = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(json))
            return null;

        CollectionSaveData data;
        try
        {
            data = JsonUtility.FromJson<CollectionSaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CollectionStore] 收藏存档解析失败，已回退为空进度：{e.Message}");
            return null;
        }

        if (data == null)
        {
            Debug.LogWarning("[CollectionStore] 收藏存档为空，已回退为空进度。");
            return null;
        }

        data.Validate();
        return data;
    }

    /// <summary>读取存档并还原成运行时进度。缺失或损坏时返回空进度。</summary>
    public static CollectionProgress Load(string playerId = null)
    {
        var progress = new CollectionProgress();
        var data = LoadData(playerId);
        if (data == null)
            return progress;

        foreach (var id in data.obtainedIds)
            progress.Obtain(id);

        foreach (var id in data.litIds)
        {
            // 先补获得资格再点亮，避免脏数据导致点亮丢失。
            progress.Obtain(id);
            progress.TryLight(id);
        }

        progress.AllCollectedRewarded = data.allCollectedRewarded;
        progress.Reconcile();
        return progress;
    }

    /// <summary>把运行时进度转成可序列化数据。</summary>
    public static CollectionSaveData Capture(CollectionProgress progress)
    {
        return new CollectionSaveData
        {
            version = CollectionSaveData.CurrentVersion,
            obtainedIds = new System.Collections.Generic.List<string>(progress.ObtainedIds),
            litIds = new System.Collections.Generic.List<string>(progress.LitIds),
            allCollectedRewarded = progress.AllCollectedRewarded,
        };
    }

    /// <summary>写入存档。只调用 PlayerPrefs.SetString，真正落盘由 <see cref="Flush"/> 完成。</summary>
    public static void Save(CollectionProgress progress, string playerId = null)
    {
        if (progress == null)
            return;

        SaveData(Capture(progress), playerId);
    }

    /// <summary>直接写入原始数据（云存档落地路径使用）。</summary>
    public static void SaveData(CollectionSaveData data, string playerId = null)
    {
        if (data == null)
            return;

        PlayerPrefs.SetString(KeyFor(playerId), JsonUtility.ToJson(data));
    }

    /// <summary>立即落盘。</summary>
    public static void Flush() => PlayerPrefs.Save();

    /// <summary>删除存档，主要用于测试与「重新开始」。</summary>
    public static void Delete(string playerId = null)
    {
        PlayerPrefs.DeleteKey(KeyFor(playerId));
        PlayerPrefs.Save();
    }
}
```

- [ ] **Step 5: 改造 CollectionService.cs（身份感知 + 事件 + 失效）**

三处修改：

(a) `Progress` 属性改为身份感知：

```csharp
    /// <summary>当前进度。首次访问时按当前身份从缓存加载。</summary>
    public static CollectionProgress Progress => progress ??= CollectionStore.Load(GameSession.LocalPlayerIdValue);
```

(b) 新增事件（放在 `RewardGranted` 事件声明之后）：

```csharp
    /// <summary>进度写入缓存后触发（云同步据此标脏上传）。</summary>
    public static event System.Action ProgressSaved;
```

(c) `RecordRaidLoot` 与 `TryLight` 中两处 `CollectionStore.Save(Progress); CollectionStore.Flush();` 之后各追加一行：

```csharp
            ProgressSaved?.Invoke();
```

(d) 新增失效方法（放在 `ResetForTests` 之前）：

```csharp
    /// <summary>
    /// 丢弃内存进度，下次访问按当前身份重新从缓存加载。
    /// 登录切号 / 服务端快照落地后由云同步服务调用。
    /// </summary>
    public static void InvalidateProgress() => progress = null;
```

- [ ] **Step 6: 运行测试确认通过**

运行：Test Runner → EditMode → 全部 `Collection*Tests` + `GameSessionTests`
预期：全部 PASS（现有测试传 null 身份走全局键，行为不变）

- [ ] **Step 7: 提交**

```bash
git add "Assets/Inventory/Runtime/LocalCacheKeys.cs" "Assets/Inventory/Runtime/LocalCacheKeys.cs.meta" "Assets/Inventory/Runtime/CollectionStore.cs" "Assets/Inventory/Runtime/CollectionService.cs" "Assets/Tests/EditMode/CollectionStoreTests.cs"
git commit -m "feat: 收藏存档账号隔离与数据/进度层拆分，统一缓存键规则"
```

---

### Task 3: GameSession 上下文重载与局终事件

**Files:**
- Modify: `Assets/Inventory/Runtime/GameSession.cs`
- Modify: `Assets/Tests/EditMode/GameSessionTests.cs`（末尾追加测试）

**Interfaces:**
- Consumes: 现有 `players` / `loadedPlayers` / `InitializePlayer`
- Produces: `GameSession.ReloadPlayer(string playerId)`（丢弃运行时上下文并从缓存重载）；`GameSession.RaidEnded`（静态事件，撤离/死亡结算后触发，云同步的强制上传点）

- [ ] **Step 1: 写失败的测试（GameSessionTests.cs 末尾追加）**

```csharp
    [Test]
    public void ReloadPlayer_RestoresContextFromFreshCache()
    {
        var rifle = TestItems.WeaponInstance("步枪");
        Register(rifle.Definition);
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, rifle);

        // 模拟云同步落地：直接改写缓存，再要求上下文重载。
        var fresh = PlayerSaveData.CreateEmpty();
        var shotgun = TestItems.WeaponInstance("霰弹枪");
        fresh.warehouse.Add(new PlayerSaveData.SlotEntry("0", shotgun.Definition.Id, shotgun.InstanceId, -1));
        PlayerSaveStore.Save(fresh, "account-alpha");
        PlayerSaveStore.Flush();

        GameSession.ReloadPlayer("account-alpha");

        var loadout = GameSession.LocalPlayer.Loadout;
        Assert.That(loadout.Equipment.Get(EquipmentSlotType.MainHand), Is.Null);
        Assert.That(loadout.GetWarehouse(0).Definition.Id, Is.EqualTo(shotgun.Definition.Id));
    }

    [Test]
    public void RaidEnded_FiresOnExtractAndAbandon()
    {
        var fired = 0;
        GameSession.RaidEnded += () => fired++;
        try
        {
            GameSession.EnsureInitialized(database);
            GameSession.ExtractRaid();
            GameSession.AbandonRaid();
            Assert.That(fired, Is.EqualTo(2));
        }
        finally
        {
            GameSession.RaidEnded = null;
        }
    }
```

- [ ] **Step 2: 运行测试确认失败**

运行：Test Runner → EditMode → `GameSessionTests.ReloadPlayer_RestoresContextFromFreshCache`
预期：编译错误（`ReloadPlayer` / `RaidEnded` 不存在）

- [ ] **Step 3: 修改 GameSession.cs**

(a) 事件声明区（`RaidChanged` 之后）追加：

```csharp
    /// <summary>
    /// 一局终了（撤离成功或死亡结算）后触发。
    /// 云同步据此绕过去抖立即上传；未来服务器权威化时，此处即服务端事务接入点。
    /// </summary>
    public static event Action RaidEnded;
```

(b) `Persist()` 方法之后新增：

```csharp
    /// <summary>
    /// 丢弃指定玩家的运行时上下文并从本地缓存重新加载。
    /// 云同步把服务端快照写入缓存后调用，使上下文与服务端数据一致。
    /// 未加载过的玩家不做任何事。
    /// </summary>
    public static void ReloadPlayer(string playerId)
    {
        var resolvedId = string.IsNullOrWhiteSpace(playerId) ? LocalPlayerId : playerId;
        if (!loadedPlayers.Remove(resolvedId))
            return;

        // 旧上下文连同其事件订阅一起被丢弃，GetPlayer 会创建新上下文并重新接线。
        players.Remove(resolvedId);
        InitializePlayer(resolvedId);
    }
```

(c) `ExtractRaid()` 方法末尾（`return overflow;` 之前）追加：

```csharp
        RaidEnded?.Invoke();
```

(d) `AbandonRaid()` 方法末尾（`player.Loadout.NotifyChanged();` 之后）追加：

```csharp
        RaidEnded?.Invoke();
```

- [ ] **Step 4: 运行测试确认通过**

运行：Test Runner → EditMode → `GameSessionTests`（全部）
预期：全部 PASS（含既有测试）

- [ ] **Step 5: 提交**

```bash
git add "Assets/Inventory/Runtime/GameSession.cs" "Assets/Tests/EditMode/GameSessionTests.cs"
git commit -m "feat: GameSession 支持上下文缓存重载与局终事件"
```

---

### Task 4: PlayerCurrency 账号化缓存与外部应用

**Files:**
- Modify: `Assets/UI/MainMenu/Scripts/PlayerCurrency.cs`
- Create: `Assets/Tests/EditMode/PlayerCurrencyTests.cs`

**Interfaces:**
- Consumes: `LocalCacheKeys.For`（Task 2）、`GameSession.LocalPlayerIdValue`
- Produces: `PlayerCurrency.Active`（static，场景内活跃实例）、`PlayerCurrency.ApplyExternal(int value)`（云同步下发余额时调用）、缓存键规则 `"<prefsKey>:account:<id>"`（登录时）

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/PlayerCurrencyTests.cs`：

```csharp
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 星空币缓存的账号隔离与外部应用：云同步落地路径的读写正确性。
/// </summary>
public sealed class PlayerCurrencyTests
{
    private PlayerCurrency currency;

    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey("StarCoins");
        PlayerPrefs.DeleteKey("StarCoins:account:account-alpha");
        GameSession.ResetForTests();
        currency = new GameObject("currency").AddComponent<PlayerCurrency>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(currency.gameObject);
        PlayerPrefs.DeleteKey("StarCoins");
        PlayerPrefs.DeleteKey("StarCoins:account:account-alpha");
        PlayerPrefs.Save();
        GameSession.ResetForTests();
    }

    [Test]
    public void Balance_WritesThroughToCacheKey()
    {
        currency.Add(500);

        // 单机（local）身份写全局键，与旧版行为一致。
        Assert.That(PlayerPrefs.GetInt("StarCoins", 0), Is.EqualTo(20000 + 500));
    }

    [Test]
    public void AccountIdentity_WritesAccountScopedKey()
    {
        GameSession.SetLocalPlayerId("account-alpha");
        // 身份切换后重建实例，模拟重新打开面板。
        Object.DestroyImmediate(currency.gameObject);
        currency = new GameObject("currency").AddComponent<PlayerCurrency>();

        currency.Add(500);

        Assert.That(PlayerPrefs.GetInt("StarCoins:account:account-alpha", 0), Is.EqualTo(20000 + 500));
        Assert.That(PlayerPrefs.GetInt("StarCoins", 0), Is.EqualTo(20000));
    }

    [Test]
    public void ApplyExternal_OverwritesBalanceAndCache()
    {
        currency.ApplyExternal(777);

        Assert.That(currency.Balance, Is.EqualTo(777));
        Assert.That(PlayerPrefs.GetInt("StarCoins", 0), Is.EqualTo(777));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行：Test Runner → EditMode → `PlayerCurrencyTests`
预期：编译错误（`ApplyExternal` 不存在）

- [ ] **Step 3: 改造 PlayerCurrency.cs**

(a) 类字段/属性区替换为：

```csharp
    /// <summary>场景内活跃实例（ Awake 赋值 / OnDestroy 清空），云同步经它刷新运行时显示。</summary>
    public static PlayerCurrency Active { get; private set; }

    [SerializeField] private TMP_Text display;
    [SerializeField] private string prefsKey = "StarCoins";
    [SerializeField] private int initialBalance = 20000;

    private int balance;

    public int Balance => balance;

    /// <summary>余额变化时触发，参数为最新余额。</summary>
    public event Action<int> Changed;

    /// <summary>账号隔离的缓存键：未登录全局键，登录后账号键（规则同 PlayerSaveStore）。</summary>
    private string CacheKey => LocalCacheKeys.For(prefsKey, GameSession.LocalPlayerIdValue);
```

(b) `Awake` 替换为：

```csharp
    private void Awake()
    {
        Active = this;
        balance = PlayerPrefs.GetInt(CacheKey, initialBalance);

        // 立即回写：保证缓存键恒等于运行时余额，云存档打包时直接读键，
        // 不依赖本组件在当前场景存活（战斗场景没有它）。
        PlayerPrefs.SetInt(CacheKey, balance);
        PlayerPrefs.Save();
        RefreshDisplay();
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }
```

(c) `SetBalance` 替换为（写入改用 `CacheKey`）：

```csharp
    private void SetBalance(int value)
    {
        balance = value;
        PlayerPrefs.SetInt(CacheKey, balance);
        PlayerPrefs.Save();
        RefreshDisplay();
        Changed?.Invoke(balance);
    }
```

(d) 新增公有方法（`TrySpend` 之后）：

```csharp
    /// <summary>
    /// 云同步落地：把服务端快照下发的余额应用到运行时并写缓存。
    /// 金额小于 0 时忽略（防御异常快照）。
    /// </summary>
    public void ApplyExternal(int value)
    {
        if (value < 0)
            return;

        SetBalance(value);
    }
```

- [ ] **Step 4: 运行测试确认通过**

运行：Test Runner → EditMode → `PlayerCurrencyTests`
预期：3 个测试全部 PASS

- [ ] **Step 5: 提交**

```bash
git add "Assets/UI/MainMenu/Scripts/PlayerCurrency.cs" "Assets/Tests/EditMode/PlayerCurrencyTests.cs" "Assets/Tests/EditMode/PlayerCurrencyTests.cs.meta"
git commit -m "feat: 星空币缓存账号隔离并支持云同步外部应用"
```

---

### Task 5: AssetSyncCore 纯逻辑状态机

**Files:**
- Create: `Assets/Network/Runtime/AssetSyncCore.cs`
- Create: `Assets/Tests/EditMode/AssetSyncCoreTests.cs`

**Interfaces:**
- Consumes: Task 1 的 DTO/协议常量、Task 2 的 `LocalCacheKeys`/`CollectionStore.Capture/SaveData`、Task 3 的 `GameSession.ReloadPlayer`、现有 `PlayerSaveStore.Capture/Save`、`CollectionService.InvalidateProgress`、`GameSession.GetPlayer`
- Produces（Task 6 依赖，签名必须一致）:
  - `AssetSyncCore`：`BindAccount(string)`、`Reset()`、`SetSuppress(bool)`、`MarkDirty(float now)`、`IsUploadDue(float now) → bool`、`IsDirty → bool`、`OnUploadSucceeded(float now)`、`OnUploadFailed(float now)`、`HasPendingUpload(string playerId) → bool`、`CaptureEnvelope(string playerId) → AssetSnapshotEnvelopeDto`、`ApplyEnvelope(AssetSnapshotEnvelopeDto, string playerId) → bool`、`AccountId → string`
  - 常量：`DebounceSeconds=5`、`MaxDeferSeconds=30`、`BaseRetrySeconds=5`、`MaxRetrySeconds=60`

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/AssetSyncCoreTests.cs`：

```csharp
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
        GameSession.EnsureInitialized(database);
        GameSession.SetLocalPlayerId("account-alpha");
        GameSession.LocalPlayer.Loadout.Equipment.Set(EquipmentSlotType.MainHand, TestItems.WeaponInstance("步枪"));

        var envelope = new AssetSnapshotEnvelopeDto { currency = 999 };
        envelope.save.warehouse.Add(new PlayerSaveData.SlotEntry("0", shotgun.Definition.Id, shotgun.InstanceId, -1));
        envelope.collection.obtainedIds.Add(shotgun.Definition.Id);

        var loadoutChangedFired = false;
        GameSession.LoadoutChanged += () => loadoutChangedFired = true;
        try
        {
            Assert.That(core.ApplyEnvelope(envelope, "account-alpha"), Is.True);
        }
        finally
        {
            GameSession.LoadoutChanged = null;
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
}
```

- [ ] **Step 2: 运行测试确认失败**

运行：Test Runner → EditMode → `AssetSyncCoreTests`
预期：编译错误（`AssetSyncCore` 不存在）

- [ ] **Step 3: 创建 AssetSyncCore.cs**

```csharp
using UnityEngine;

namespace Tower.Network
{
    /// <summary>
    /// 资产云同步的纯逻辑核心：脏标记、去抖、退避重试、断点续传标记、快照打包与落地。
    /// 不持有 MonoBehaviour 依赖；时间一律由调用方注入（运行时传 Time.unscaledTime，
    /// 测试手动推进），便于 EditMode 测试。
    ///
    /// 心智模型：本地 PlayerPrefs 缓存什么时候写，服务端就（去抖后）什么时候有同样的数据。
    /// </summary>
    public sealed class AssetSyncCore
    {
        /// <summary>最后一条变化后等待多久才上传（合批）。</summary>
        public const float DebounceSeconds = 5f;

        /// <summary>变化持续不断时的最大上传间隔。</summary>
        public const float MaxDeferSeconds = 30f;

        /// <summary>上传失败退避基数，按失败次数指数翻倍。</summary>
        public const float BaseRetrySeconds = 5f;

        /// <summary>退避封顶。</summary>
        public const float MaxRetrySeconds = 60f;

        private string accountId;
        private bool isDirty;
        private bool suppress;
        private float firstDirtyAt = -1f;
        private float lastDirtyAt = -1f;
        private float nextRetryAt = -1f;
        private int retryCount;

        /// <summary>当前绑定的账号 ID。未绑定时所有标脏/落盘操作均为空操作。</summary>
        public string AccountId => accountId;

        public bool IsDirty => isDirty;

        public void BindAccount(string id) => accountId = id;

        /// <summary>登出或重新登录前清空运行时状态（不清理持久化的 pending 标记）。</summary>
        public void Reset()
        {
            accountId = null;
            isDirty = false;
            suppress = false;
            firstDirtyAt = -1f;
            lastDirtyAt = -1f;
            nextRetryAt = -1f;
            retryCount = 0;
        }

        /// <summary>回声压制：快照落地会触发 LoadoutChanged 等事件，期间标脏一律忽略。</summary>
        public void SetSuppress(bool value) => suppress = value;

        public void MarkDirty(float now)
        {
            if (suppress || string.IsNullOrEmpty(accountId))
                return;

            if (!isDirty)
            {
                isDirty = true;
                firstDirtyAt = now;
                WritePendingFlag(true);
            }

            lastDirtyAt = now;
        }

        /// <summary>当前是否应发起上传：去抖到点 / 30 秒上限 / 退避到点，三者任一。</summary>
        public bool IsUploadDue(float now)
        {
            if (!isDirty || now < nextRetryAt)
                return false;

            return now - lastDirtyAt >= DebounceSeconds || now - firstDirtyAt >= MaxDeferSeconds;
        }

        public void OnUploadSucceeded(float now)
        {
            isDirty = false;
            firstDirtyAt = -1f;
            lastDirtyAt = -1f;
            nextRetryAt = -1f;
            retryCount = 0;
            WritePendingFlag(false);
        }

        public void OnUploadFailed(float now)
        {
            retryCount++;
            var delay = Mathf.Min(BaseRetrySeconds * Mathf.Pow(2f, retryCount - 1), MaxRetrySeconds);
            nextRetryAt = now + delay;
        }

        /// <summary>
        /// 本账号是否存在「上次没传上去」的本地脏数据（跨进程持久化）。
        /// 登录拉取后若此标记为真，以上传代替覆盖，避免本地进度被服务端旧档冲掉。
        /// 单机 local 身份永不参与云同步，恒为 false。
        /// </summary>
        public bool HasPendingUpload(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || playerId == GameSession.LocalPlayerId)
                return false;

            return PlayerPrefs.GetInt(PendingKey(playerId), 0) == 1;
        }

        /// <summary>断点续传标记的缓存键（账号隔离）。</summary>
        public string PendingKey(string playerId) => LocalCacheKeys.For("AssetSyncPending", playerId);

        /// <summary>打包当前账号的完整快照（仓库/装备 + 收藏 + 星空币）。</summary>
        public AssetSnapshotEnvelopeDto CaptureEnvelope(string playerId)
        {
            var context = GameSession.GetPlayer(playerId);
            return new AssetSnapshotEnvelopeDto
            {
                save = PlayerSaveStore.Capture(context.Loadout),
                collection = CollectionStore.Capture(CollectionService.Progress),
                currency = PlayerPrefs.GetInt(LocalCacheKeys.For("StarCoins", playerId), 0),
            };
        }

        /// <summary>
        /// 把服务端快照落地到本地缓存并重载运行时上下文。
        /// 调用方须先 <see cref="SetSuppress"/>(true) 压制落地过程的回声脏标记，结束后恢复 false。
        /// </summary>
        public bool ApplyEnvelope(AssetSnapshotEnvelopeDto envelope, string playerId)
        {
            if (envelope == null || string.IsNullOrEmpty(playerId))
                return false;

            envelope.save?.Validate();
            envelope.collection?.Validate();

            PlayerSaveStore.Save(envelope.save ?? PlayerSaveData.CreateEmpty(), playerId);
            CollectionStore.SaveData(envelope.collection ?? new CollectionSaveData(), playerId);
            PlayerPrefs.SetInt(LocalCacheKeys.For("StarCoins", playerId), Mathf.Max(0, envelope.currency));
            PlayerPrefs.Save();

            GameSession.ReloadPlayer(playerId);
            CollectionService.InvalidateProgress();
            return true;
        }

        private void WritePendingFlag(bool value)
        {
            if (string.IsNullOrEmpty(accountId) || accountId == GameSession.LocalPlayerId)
                return;

            PlayerPrefs.SetInt(PendingKey(accountId), value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

运行：Test Runner → EditMode → `AssetSyncCoreTests`
预期：10 个测试全部 PASS

- [ ] **Step 5: 提交**

```bash
git add "Assets/Network/Runtime/AssetSyncCore.cs" "Assets/Network/Runtime/AssetSyncCore.cs.meta" "Assets/Tests/EditMode/AssetSyncCoreTests.cs" "Assets/Tests/EditMode/AssetSyncCoreTests.cs.meta"
git commit -m "feat: 云同步核心状态机（去抖/退避/断点续传/快照打包落地）"
```

---

### Task 6: AssetSyncService 与 NetworkSession 接线

**Files:**
- Create: `Assets/Network/Runtime/AssetSyncService.cs`
- Modify: `Assets/Network/Runtime/NetworkSession.cs`

**Interfaces:**
- Consumes: Task 5 全部接口、`ApiClient.GetSnapshot/PutSnapshot`（Task 1）、`GameSession.LoadoutChanged/RaidEnded`、`CollectionService.ProgressSaved`
- Produces（Task 7 依赖）:
  - `AssetSyncState { NotLoggedIn, Syncing, Ready, Failed }`
  - `AssetSyncService.Instance`（static）、`AssetSyncService.State`、`AssetSyncService.MarkDirty()`（static，PlayerCurrency 调用）、`FlushNow()`、`StateChanged`（`Action<AssetSyncState>`）、`SnapshotApplied`（`Action<int>`，参数为下发的星空币）
- NetworkSession：`Awake` 创建服务并 `Initialize(api)`；`HandleLoggedIn` 调 `OnAccountBound(...)`；`Logout` 在 `api.Logout()` 前插入 `FlushBeforeLogout()`，结束后调 `OnLoggedOut()`

- [ ] **Step 1: 创建 AssetSyncService.cs**

```csharp
using System;
using System.Collections;
using UnityEngine;

namespace Tower.Network
{
    /// <summary>云同步状态。UI 门禁只认 Ready。</summary>
    public enum AssetSyncState
    {
        /// <summary>未登录。</summary>
        NotLoggedIn = 0,

        /// <summary>登录后正在拉取/落地服务端快照。</summary>
        Syncing = 1,

        /// <summary>同步完成，资产可用（后台仍会去抖上传）。</summary>
        Ready = 2,

        /// <summary>快照拉取失败（重试耗尽）。出发被门禁拦截，重新登录可重试。</summary>
        Failed = 3,
    }

    /// <summary>
    /// 玩家资产云同步服务：登录拉取快照、变化去抖上传、关键点强制上传、断网退避重试。
    /// 由 <see cref="NetworkSession"/> 在 Awake 时挂到同一 GameObject（随其跨场景常驻）。
    /// 纯逻辑在 <see cref="AssetSyncCore"/>，本类只做协程驱动与 Unity 生命周期接线。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssetSyncService : MonoBehaviour
    {
        /// <summary>全局实例。未挂载时为 null（EditMode 测试 / 未进过主菜单）。</summary>
        public static AssetSyncService Instance { get; private set; }

        /// <summary>同步状态变化（出发门禁据此放行/拦截）。</summary>
        public static event Action<AssetSyncState> StateChanged;

        /// <summary>
        /// 服务端快照落地完成，参数为下发的星空币余额。
        /// PlayerCurrency 订阅它刷新运行时显示（Network 程序集不能反向引用 UI 层，走事件反转）。
        /// </summary>
        public static event Action<int> SnapshotApplied;

        /// <summary>登录拉取失败的重试次数。</summary>
        private const int LoginGetAttempts = 3;

        /// <summary>登录拉取重试间隔（秒）。</summary>
        private const float LoginRetryIntervalSeconds = 1f;

        public AssetSyncState State { get; private set; } = AssetSyncState.NotLoggedIn;

        private ApiClient api;
        private readonly AssetSyncCore core = new AssetSyncCore();
        private bool uploading;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnEnable()
        {
            GameSession.LoadoutChanged += OnAssetChanged;
            GameSession.RaidEnded += OnRaidEnded;
            CollectionService.ProgressSaved += OnAssetChanged;
        }

        private void OnDisable()
        {
            GameSession.LoadoutChanged -= OnAssetChanged;
            GameSession.RaidEnded -= OnRaidEnded;
            CollectionService.ProgressSaved -= OnAssetChanged;
        }

        private void Update()
        {
            if (State == AssetSyncState.Ready && core.IsDirty && !uploading
                && core.IsUploadDue(Time.unscaledTime))
            {
                FlushNow();
            }
        }

        private void OnApplicationQuit() => FlushNow();

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                FlushNow();
        }

        /// <summary>由 NetworkSession 在创建后注入 HTTP 客户端。</summary>
        public void Initialize(ApiClient client) => api = client;

        /// <summary>标记资产已变化。外部源（PlayerCurrency）与未登录期调用都安全：只记脏，不做事。</summary>
        public static void MarkDirty()
        {
            if (Instance != null)
                Instance.core.MarkDirty(Time.unscaledTime);
        }

        /// <summary>强制上传点（出发/撤离/死亡/登出/退游戏）：绕过去抖立即上传，失败进入退避。</summary>
        public void FlushNow()
        {
            if (State != AssetSyncState.Ready || !core.IsDirty || uploading)
                return;

            StartCoroutine(UploadRoutine());
        }

        /// <summary>登录成功、身份已绑定账号后由 NetworkSession 调用：拉取服务端快照。</summary>
        public void OnAccountBound(string accountId)
        {
            core.Reset();
            core.BindAccount(accountId);
            SetState(AssetSyncState.Syncing);
            StartCoroutine(LoginSyncRoutine(accountId));
        }

        /// <summary>登出完成后由 NetworkSession 调用。</summary>
        public void OnLoggedOut()
        {
            // pending 标记是持久化的：若登出前上传失败，下次登录该账号会自动补传。
            core.Reset();
            SetState(AssetSyncState.NotLoggedIn);
        }

        /// <summary>登出前由 NetworkSession 调用：有脏数据就尽力传一次（失败保留 pending 标记）。</summary>
        public IEnumerator FlushBeforeLogout()
        {
            if (State != AssetSyncState.Ready || !core.IsDirty || uploading || api == null || !api.IsLoggedIn)
                yield break;

            yield return UploadRoutine();
        }

        private IEnumerator LoginSyncRoutine(string accountId)
        {
            AssetSnapshotResponseDto response = null;
            ApiError failure = null;

            for (var attempt = 1; attempt <= LoginGetAttempts && response == null; attempt++)
            {
                failure = null;
                yield return api.GetSnapshot(
                    r => response = r,
                    e => failure = e);

                if (response == null && attempt < LoginGetAttempts)
                    yield return new WaitForSecondsRealtime(LoginRetryIntervalSeconds);
            }

            if (response == null)
            {
                Debug.LogError($"[AssetSync] 快照拉取失败（{failure?.Code} {failure?.Message}），已阻止进入。重新登录可重试。");
                SetState(AssetSyncState.Failed);
                yield break;
            }

            if (response.schema_version > AssetSnapshotProtocol.SchemaVersion)
            {
                Debug.LogError($"[AssetSync] 服务端快照版本 {response.schema_version} 高于客户端支持的 {AssetSnapshotProtocol.SchemaVersion}，请升级客户端。");
                SetState(AssetSyncState.Failed);
                yield break;
            }

            // 本机有上次没传上去的脏数据：以上传代替覆盖，避免本地进度被服务端旧档冲掉。
            // （第一期客户端权威，本地数据可信度不低于服务端；权威化后此分支由服务端裁决。）
            // revision == 0 = 服务端尚无快照，走下方建档分支。
            if (core.HasPendingUpload(accountId) && response.revision > 0)
            {
                SetState(AssetSyncState.Ready);
                core.MarkDirty(Time.unscaledTime);
                yield return UploadRoutine();
                yield break;
            }

            if (response.revision == 0)
            {
                // 新账号：上下文已含初始装备（EnsureInitialized 流程），直接建档上传。
                // 判定用 revision == 0 —— JsonUtility 会把 "data":null 物化成完整空对象图，JSON 的 null 不可用作信号。
                SetState(AssetSyncState.Ready);
                core.MarkDirty(Time.unscaledTime);
                yield return UploadRoutine();
                yield break;
            }

            core.SetSuppress(true);
            try
            {
                if (!core.ApplyEnvelope(response.data, accountId))
                {
                    SetState(AssetSyncState.Failed);
                    yield break;
                }

                SnapshotApplied?.Invoke(response.data.currency);
            }
            finally
            {
                core.SetSuppress(false);
            }

            SetState(AssetSyncState.Ready);
        }

        private IEnumerator UploadRoutine()
        {
            uploading = true;

            var envelope = core.CaptureEnvelope(core.AccountId);
            var succeeded = false;

            yield return api.PutSnapshot(envelope,
                _ => succeeded = true,
                e => Debug.LogWarning($"[AssetSync] 快照上传失败，稍后重试：{e}"));

            uploading = false;

            if (succeeded)
                core.OnUploadSucceeded(Time.unscaledTime);
            else
                core.OnUploadFailed(Time.unscaledTime);
        }

        private void OnAssetChanged() => core.MarkDirty(Time.unscaledTime);

        private void OnRaidEnded() => FlushNow();

        private void SetState(AssetSyncState next)
        {
            if (State == next)
                return;

            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
```

- [ ] **Step 2: 修改 NetworkSession.cs（三处）**

(a) 字段区（`private SignalClient signal;` 之后）追加：

```csharp
        private AssetSyncService assetSync;

        /// <summary>资产云同步服务（与本组件同 GameObject，跨场景常驻）。</summary>
        public AssetSyncService Assets => assetSync;
```

(b) `Awake` 中 `signal = new SignalClient(config);` 之后追加：

```csharp
            assetSync = gameObject.AddComponent<AssetSyncService>();
            assetSync.Initialize(api);
```

(c) `HandleLoggedIn` 中 `GameSession.SetLocalPlayerId(account.account_id.ToString());` 之后、`LoggedIn?.Invoke(account);` 之前插入：

```csharp
            // 云存档：绑定账号后立即拉取服务端快照（Syncing 状态下出发门禁保持拦截）。
            if (assetSync != null)
                assetSync.OnAccountBound(account.account_id.ToString());
```

(d) `Logout` 协程改造为（在 `api.Logout();` 之前插入 flush，在 `LoggedOut?.Invoke();` 之前插入 OnLoggedOut）：

```csharp
        /// <summary>登出：断开信令、尽力上传脏资产、清空登录态、把本机玩家身份还原为单机。</summary>
        public IEnumerator Logout()
        {
            if (signal != null && signal.IsConnected)
                yield return signal.Disconnect();

            // 云存档：登出前尽力把脏资产传上去；失败时 pending 标记已持久化，下次登录自动补传。
            if (assetSync != null)
                yield return assetSync.FlushBeforeLogout();

            CurrentRoom = null;
            LastTransportMode = null;
            api.Logout();

            // 还原为单机身份 —— 否则后续单机流程会继续读写联机账号的数据。
            GameSession.SetLocalPlayerId(null);

            if (assetSync != null)
                assetSync.OnLoggedOut();

            LoggedOut?.Invoke();
        }
```

- [ ] **Step 3: 编译与回归验证**

运行：先经 MCP `unity_get_compilation_errors` 确认 0 错误；Test Runner → EditMode 全量运行
预期：全部 PASS（本任务不新增测试；服务为 MonoBehaviour 薄壳，逻辑已由 Task 5 覆盖）

- [ ] **Step 4: 提交**

```bash
git add "Assets/Network/Runtime/AssetSyncService.cs" "Assets/Network/Runtime/AssetSyncService.cs.meta" "Assets/Network/Runtime/NetworkSession.cs"
git commit -m "feat: 资产云同步服务与网络会话接线（登录拉取/去抖上传/登出冲刷）"
```

---

### Task 7: 调用点接线（门禁、强制点、货币回灌）

**Files:**
- Modify: `Assets/UI/MainMenu/Scripts/PlayerCurrency.cs`（订阅快照回灌 + 标脏）
- Modify: `Assets/UI/MainMenu/Scripts/DepartFlow.cs`（出发门禁 + 出发强制点）
- Modify: `Assets/Gameplay/Runtime/TowerRunManager.cs`（撤离/团灭强制点）
- Modify: `Assets/Gameplay/Runtime/PlayerDeathFlow2D.cs`（死亡强制点）

**Interfaces:**
- Consumes: `AssetSyncService.Instance/MarkDirty/FlushNow/SnapshotApplied`（Task 6）、`AssetSyncState`
- Produces: 无新接口（纯接线）

- [ ] **Step 1: PlayerCurrency.cs 追加订阅与标脏**

(a) `OnDestroy` 之后新增生命周期对：

```csharp
    private void OnEnable()
    {
        // 云同步回灌：服务端快照落地后刷新运行时显示。
        // Network 程序集不能引用 UI 层，由本组件主动订阅（事件反转）。
        Tower.Network.AssetSyncService.SnapshotApplied += OnSnapshotApplied;
    }

    private void OnDisable()
    {
        Tower.Network.AssetSyncService.SnapshotApplied -= OnSnapshotApplied;
    }

    private void OnSnapshotApplied(int value)
    {
        if (Tower.Network.AssetSyncService.Instance == null)
            return;

        ApplyExternal(value);
    }
```

(b) `SetBalance` 末尾（`Changed?.Invoke(balance);` 之后）追加一行：

```csharp
        Tower.Network.AssetSyncService.MarkDirty();
```

- [ ] **Step 2: DepartFlow.cs 出发门禁与强制点**

(a) `Depart()` 方法开头（`if (isLoading) return;` 之后）插入：

```csharp
        // 登录门槛：未登录或资产尚未同步完成时不允许出发（设计文档 §7/§8）。
        var assetSync = Tower.Network.AssetSyncService.Instance;
        if (assetSync == null || assetSync.State != Tower.Network.AssetSyncState.Ready)
        {
            roomBinder?.ShowDepartError("请先登录并等待资产同步完成");
            return;
        }
```

(b) 联机分支 `GameSession.Persist();`（约 152 行）之后追加：

```csharp
            Tower.Network.AssetSyncService.Instance?.FlushNow();
```

(c) `LoadTargetScene()` 协程中 `GameSession.Persist();`（约 200 行）之后追加：

```csharp
        Tower.Network.AssetSyncService.Instance?.FlushNow();
```

- [ ] **Step 3: TowerRunManager.cs 撤离与团灭强制点**

(a) `ConfirmRetreat()` 中 `var overflow = GameSession.ExtractRaid();`（约 1213 行）所在的结算段落，在 `if (payload != null) StartCoroutine(...)` 之前插入：

```csharp
        // 撤离结算为强制上传点：绕过去抖立即同步（失败走退避，不阻塞回城）。
        Tower.Network.AssetSyncService.Instance?.FlushNow();
```

(b) `ExitAfterTeamWipeDelay()` 中 `GameSession.AbandonRaid();`（约 342 行）之后追加：

```csharp
        Tower.Network.AssetSyncService.Instance?.FlushNow();
```

(c) 文件内另一处 `GameSession.AbandonRaid();`（约 1292 行，退出确认路径）之后同样追加：

```csharp
        Tower.Network.AssetSyncService.Instance?.FlushNow();
```

- [ ] **Step 4: PlayerDeathFlow2D.cs 死亡强制点**

私有包装方法 `AbandonRaid()`（约 211-215 行）替换为：

```csharp
    private void AbandonRaid()
    {
        // 只清局内携带与战利品，局外仓库原样保留。
        GameSession.AbandonRaid();

        // 死亡结算为强制上传点：装备清空必须立刻落服务端，防止"死后回档"。
        Tower.Network.AssetSyncService.Instance?.FlushNow();
    }
```

- [ ] **Step 5: 编译与回归验证**

运行：MCP `unity_get_compilation_errors` 确认 0 错误；Test Runner → EditMode 全量运行
预期：全部 PASS

- [ ] **Step 6: 提交**

```bash
git add "Assets/UI/MainMenu/Scripts/PlayerCurrency.cs" "Assets/UI/MainMenu/Scripts/DepartFlow.cs" "Assets/Gameplay/Runtime/TowerRunManager.cs" "Assets/Gameplay/Runtime/PlayerDeathFlow2D.cs"
git commit -m "feat: 接入资产云同步的出发门禁与撤离/死亡/出发强制上传点"
```

---

### Task 8: 全量回归与验收

**Files:**
- 无代码改动（验证任务）

**Interfaces:**
- Consumes: 前 7 个任务的全部产物

- [ ] **Step 1: EditMode 全量测试**

运行：Test Runner → EditMode → Run All
预期：0 失败。重点确认既有 `GameSessionTests`、`CollectionStoreTests`、`CollectionServiceTests`、`CollectionCatalogTests`、`CollectionProgressTests` 无回归。

- [ ] **Step 2: 编译零警告检查（与本任务相关文件）**

运行：MCP `unity_get_compilation_errors`
预期：0 错误 0 警告（新增文件）

- [ ] **Step 3: 手动验收清单（需运行中的服务端，逐项过）**

1. 全新账号登录 → 登录面板关闭后出发按钮可用；服务端 `player_assets` 出现该账号一行（初始装备 + 20000 星空币）
2. 商店购买 + 仓库整理 → 停手约 5 秒后服务端快照更新（revision +1）
3. 连续快速操作 30 秒以上 → 服务端至少每 30 秒收到一次 PUT
4. 单机出发 → 立即一次 PUT；地牢内捡拾不产生请求
5. 撤离成功 → 立即一次 PUT，重登后战利品在
6. 死亡 → 立即一次 PUT，重登后装备为空
7. 断网打完一局并撤离（上传失败）→ 退出游戏 → 恢复网络重新登录 → 本地未同步的撤离成果自动补传（pending 标记路径）
8. 登出 → 登录另一账号 → 资产互不串档（StarCoins/CollectionSave 均为账号键）
9. 登录时断网 → 停在登录页，进不了游戏
10. （双客户端，可选）两台设备同账号先后写入 → 后传覆盖，revision 可解释谁覆盖了谁

- [ ] **Step 4: 最终提交（如有零散修正）**

```bash
git status
git add <修正的文件>
git commit -m "test: 云存档全量回归修正"
```

---

## 自审记录

- **Spec 覆盖**：设计文档 §4（表/接口）→ Task 1+服务端契约；§5 契约 → Task 1；§6 上传时机 → Task 5/6/7（去抖、上限、强制点、退避、断点续传）；§7 断网 → Task 5 pending 标记 + Task 6 Failed 门禁 + Task 8 清单 7/9；§8 客户端改造点 → Task 2-7 逐项对应；§9 迁移 → 无迁移代码（决策：放弃认领），`"local"` 身份仅测试可达；§10 测试 → 各任务 TDD + Task 8。
- **占位符扫描**：无 TBD/TODO；所有代码步骤含完整代码。
- **类型一致性**：`AssetSyncCore` 产出签名与 Task 6 消费一致；`SnapshotApplied(int)`、`MarkDirty()` static、`FlushNow()` 实例方法在 Task 6/7 间已核对；`CollectionStore.Capture/SaveData/LoadData/KeyFor` 与 Task 5 消费一致。
- **补充设计细节（超出 spec 文本的实现决定，已按 spec 精神落地）**：① 跨进程断点续传标记 `AssetSyncPending:<account>`（落实 spec §7 "恢复联网后自动补传" 到"重启后"场景）；② `GameSession.RaidEnded` 事件作为撤离/死亡强制上传的统一接缝（spec §6 预言的"权威化同一批事件"）；③ 快照信封中 `schema_version` 只在 API 层出现一次，`data` 内不再重复。
