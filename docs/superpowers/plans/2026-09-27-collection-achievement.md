# 收藏成就系统 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现收藏成就系统——玩家从地牢带出材料/消耗品后获得「收藏资格」，在展馆中手动点亮为彩图并获得星币奖励。

**Architecture:** 新增独立模块（`CollectionCatalog` / `CollectionProgress` / `CollectionStore` / `CollectionService`），放在 `Inventory` 程序集内，使用独立 PlayerPrefs 键 `CollectionSave` 与仓库存档风险隔离。图鉴条目运行时从 `ItemDatabase` 动态计算（`equipSlotType == None` 自动收录 + 排除名单）。撤离时由 `GameSession.ExtractRaid` 调用钩子结算 `RaidBag.Loot`。UI 复用 `UiTokens` 色板，主菜单 `Btn_Storage` 改造为「收藏」入口。

**Tech Stack:** Unity 2021+ / C# / uGUI + TextMeshPro / NUnit (EditMode) / PlayerPrefs + JsonUtility

## Global Constraints

- **程序集归属**：所有非 UI 类型放在 `Assets/Inventory/Runtime/`（`Inventory` 程序集，`references: []`，不可引用 `BattleHUD` 或 `Gameplay`）。UI 类型放在 `Assets/UI/MainMenu/Scripts/`（`Assembly-CSharp`）。
- **物品唯一 ID**：`ItemDefinition.Id`（= asset 文件名），通过 `ItemDatabase.Find(id)` 反查。
- **物品实例**：`RaidBag` / `PlayerLoadout` 存的是 `ItemInstance`，物品模板通过 `instance.Definition` 获取。
- **存档键**：`CollectionSave`（独立于 `PlayerSave` 与 `StarCoins`）。
- **测试约定**：EditMode only；测试类 `public sealed class <类名>Tests`；方法名 `<方法>_<预期行为>`；`[SetUp]` 中清理存档 + `GameSession.ResetForTests()`；断言用 NUnit `Assert.That`，失败消息用中文。
- **测试程序集**：根测试放 `Assets/Tests/EditMode/`（`BattleHUD.EditModeTests`，已引用 `Inventory`）。
- **视觉令牌**：颜色一律取自 `UiTokens`，不写字面值。
- **奖励金额**：Common 30 / Fine 60 / Rare 150 / Epic 400 / Legendary 1000；全收集额外 3000。
- **图标尺寸**：展馆 192px，4×4 排列，无格子底框，无进度条（文字呈现），超出 16 件用 `ScrollRect` 纵向滑动。
- **剪影实现**：同一张彩图 + 代码 tint。`Locked` = `#000000` alpha 0.55；`Obtained` = `#6B7075` alpha 0.75；`Lit` = 原色。

---

## 文件结构

### 新建

| 文件 | 职责 |
|---|---|
| `Assets/Inventory/Runtime/CollectionCatalog.cs` | ScriptableObject：排除名单 + 品质奖励表 + 全收集奖励额；提供 `Collectibles(database)` 动态计算图鉴条目 |
| `Assets/Inventory/Runtime/CollectionProgress.cs` | 纯 C#：持有 `obtainedIds` / `litIds` / `allCollectedRewarded`，提供查询与变更 |
| `Assets/Inventory/Runtime/CollectionSaveData.cs` | 可序列化存档载体，含 `version` |
| `Assets/Inventory/Runtime/CollectionStore.cs` | 静态：`CollectionSave` 键的读写 |
| `Assets/Inventory/Runtime/CollectionService.cs` | 静态门面：撤离结算、点亮、发奖、对账 |
| `Assets/UI/MainMenu/Scripts/CollectionSlotView.cs` | 单个收藏图标视图（三态表现 + 点击） |
| `Assets/UI/MainMenu/Scripts/CollectionPanelController.cs` | 展馆面板控制器 |
| `Assets/Tests/EditMode/CollectionProgressTests.cs` | 进度层测试 |
| `Assets/Tests/EditMode/CollectionStoreTests.cs` | 存档层测试 |
| `Assets/Tests/EditMode/CollectionServiceTests.cs` | 业务层测试 |
| `Assets/Tests/EditMode/CollectionCatalogTests.cs` | 图鉴计算测试 |

### 修改

| 文件 | 改动 |
|---|---|
| `Assets/Inventory/Runtime/GameSession.cs` | `ExtractRaid()` 中插入 `CollectionService.RecordRaidLoot(Raid.Loot)` 钩子 |
| `Assets/UI/MainMenu/Prefabs/MainMenu.prefab` | `Btn_Storage` 改造为「收藏」：改文字、改 `m_OnClick` 目标为 `CollectionPanelController.Toggle`（贴图待素材） |

---

## Task 1: CollectionCatalog（图鉴定义与动态收录）

**Files:**
- Create: `Assets/Inventory/Runtime/CollectionCatalog.cs`
- Test: `Assets/Tests/EditMode/CollectionCatalogTests.cs`

**Interfaces:**
- Consumes: `ItemDatabase.Items`（`IReadOnlyList<ItemDefinition>`）、`ItemDefinition.EquipSlotType`、`ItemDefinition.Rarity`、`EquipmentSlotType.None`、`ItemRarity`
- Produces:
  - `CollectionCatalog.Collectibles(ItemDatabase database) → IReadOnlyList<ItemDefinition>`
  - `CollectionCatalog.RewardFor(ItemRarity rarity) → int`
  - `CollectionCatalog.AllCollectedReward → int`

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/CollectionCatalogTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="CollectionCatalog"/> 的图鉴条目计算：自动收录材料、排除武器防具与排除名单。
/// </summary>
public sealed class CollectionCatalogTests
{
    private ItemDatabase database;
    private CollectionCatalog catalog;

    [SetUp]
    public void SetUp()
    {
        database = ScriptableObject.CreateInstance<ItemDatabase>();
        catalog = ScriptableObject.CreateInstance<CollectionCatalog>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(database);
        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void Collectibles_ExcludesWeaponsAndArmor()
    {
        var material = TestItems.Create(EquipmentSlotType.None, "材料");
        var weapon = TestItems.Weapon("武器");
        var armor = TestItems.Armor(EquipmentSlotType.Helmet, "头盔");
        SetDatabaseItems(material, weapon, armor);

        var result = catalog.Collectibles(database);

        Assert.That(result, Has.Count.EqualTo(1), "只有材料应进图鉴");
        Assert.That(result[0], Is.SameAs(material));
    }

    [Test]
    public void Collectibles_ExcludesExplicitlyExcludedItems()
    {
        var material = TestItems.Create(EquipmentSlotType.None, "材料");
        var questItem = TestItems.Create(EquipmentSlotType.None, "任务道具");
        SetDatabaseItems(material, questItem);
        SetExcluded(questItem);

        var result = catalog.Collectibles(database);

        Assert.That(result, Has.Count.EqualTo(1), "排除名单里的物品不应进图鉴");
        Assert.That(result[0], Is.SameAs(material));
    }

    [Test]
    public void Collectibles_WithNullDatabase_ReturnsEmpty()
    {
        Assert.That(catalog.Collectibles(null), Is.Empty);
    }

    [Test]
    public void RewardFor_ReturnsConfiguredAmountPerRarity()
    {
        Assert.That(catalog.RewardFor(ItemRarity.Common), Is.EqualTo(30));
        Assert.That(catalog.RewardFor(ItemRarity.Fine), Is.EqualTo(60));
        Assert.That(catalog.RewardFor(ItemRarity.Rare), Is.EqualTo(150));
        Assert.That(catalog.RewardFor(ItemRarity.Epic), Is.EqualTo(400));
        Assert.That(catalog.RewardFor(ItemRarity.Legendary), Is.EqualTo(1000));
    }

    [Test]
    public void AllCollectedReward_DefaultsTo3000()
    {
        Assert.That(catalog.AllCollectedReward, Is.EqualTo(3000));
    }

    private void SetDatabaseItems(params ItemDefinition[] items)
    {
        var serialized = new SerializedObject(database);
        var array = serialized.FindProperty("items");
        array.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private void SetExcluded(params ItemDefinition[] items)
    {
        var serialized = new SerializedObject(catalog);
        var array = serialized.FindProperty("excluded");
        array.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

在 Unity 中打开 Test Runner（Window > General > Test Runner > EditMode），运行 `CollectionCatalogTests`。
预期：编译失败，`CollectionCatalog` 类型不存在。

- [ ] **Step 3: 写最小实现**

创建 `Assets/Inventory/Runtime/CollectionCatalog.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 收藏图鉴的配置：排除名单 + 奖励表。
///
/// 图鉴条目**不在这里列举**，而是运行时从 <see cref="ItemDatabase"/> 动态计算
/// （见 <see cref="Collectibles"/>）：新增材料自动进图鉴，无需改配置。
/// 这里只负责「排除哪些」与「奖励多少」。
/// </summary>
[CreateAssetMenu(menuName = "Inventory/Collection Catalog", fileName = "CollectionCatalog")]
public sealed class CollectionCatalog : ScriptableObject
{
    [Tooltip("不参与收藏的物品（任务道具、钥匙等）。武器防具无需登记，会自动排除。")]
    [SerializeField] private ItemDefinition[] excluded;

    [Header("单件奖励（按品质）")]
    [SerializeField] private int commonReward = 30;
    [SerializeField] private int fineReward = 60;
    [SerializeField] private int rareReward = 150;
    [SerializeField] private int epicReward = 400;
    [SerializeField] private int legendaryReward = 1000;

    [Header("全收集额外奖励")]
    [SerializeField] private int allCollectedReward = 3000;

    /// <summary>全收集额外奖励金额。</summary>
    public int AllCollectedReward => allCollectedReward;

    /// <summary>
    /// 计算当前图鉴条目：登记表里所有非装备物品，减去排除名单。
    /// 顺序与 <see cref="ItemDatabase.Items"/> 一致，保证 UI 排列稳定。
    /// </summary>
    public IReadOnlyList<ItemDefinition> Collectibles(ItemDatabase database)
    {
        var result = new List<ItemDefinition>();
        if (database == null)
            return result;

        foreach (var item in database.Items)
        {
            if (item == null)
                continue;

            // 武器防具不参与收藏。
            if (item.EquipSlotType != EquipmentSlotType.None)
                continue;

            if (IsExcluded(item))
                continue;

            result.Add(item);
        }

        return result;
    }

    /// <summary>该品质对应的单件点亮奖励。</summary>
    public int RewardFor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Fine:
                return fineReward;
            case ItemRarity.Rare:
                return rareReward;
            case ItemRarity.Epic:
                return epicReward;
            case ItemRarity.Legendary:
                return legendaryReward;
            default:
                return commonReward;
        }
    }

    private bool IsExcluded(ItemDefinition item)
    {
        if (excluded == null)
            return false;

        foreach (var entry in excluded)
        {
            if (entry == item)
                return true;
        }

        return false;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

运行 `CollectionCatalogTests`。
预期：5 个测试全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Assets/Inventory/Runtime/CollectionCatalog.cs Assets/Inventory/Runtime/CollectionCatalog.cs.meta Assets/Tests/EditMode/CollectionCatalogTests.cs Assets/Tests/EditMode/CollectionCatalogTests.cs.meta
git commit -m "feat(collection): 新增收藏图鉴配置与动态收录计算"
```

---

## Task 2: CollectionProgress（进度状态机）

**Files:**
- Create: `Assets/Inventory/Runtime/CollectionProgress.cs`
- Test: `Assets/Tests/EditMode/CollectionProgressTests.cs`

**Interfaces:**
- Consumes: 无（纯 C#，不依赖 Unity）
- Produces:
  - `CollectionProgress.ObtainedIds → IReadOnlyCollection<string>`
  - `CollectionProgress.LitIds → IReadOnlyCollection<string>`
  - `CollectionProgress.AllCollectedRewarded → bool`（可读写）
  - `CollectionProgress.Obtain(string id) → bool`（新增返回 true）
  - `CollectionProgress.IsObtained(string id) → bool`
  - `CollectionProgress.TryLight(string id) → bool`（仅 Obtained 且未点亮时成功）
  - `CollectionProgress.IsLit(string id) → bool`
  - `CollectionProgress.LitCountWithin(IEnumerable<string> validIds) → int`
  - `CollectionProgress.ObtainedCountWithin(IEnumerable<string> validIds) → int`
  - `CollectionProgress.Reconcile()` — 校正 `litIds ⊆ obtainedIds`

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/CollectionProgressTests.cs`：

```csharp
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
    public void Reconcile_AddsLitIdsIntoObtainedIds()
    {
        var progress = new CollectionProgress();
        progress.Obtain("A");
        progress.TryLight("A");
        // 模拟脏数据：litIds 有 B 但 obtainedIds 没有。
        progress.ForceLitForTests("B");

        progress.Reconcile();

        Assert.That(progress.IsObtained("B"), Is.True, "litIds 应被补进 obtainedIds");
        Assert.That(progress.IsLit("B"), Is.True);
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
```

- [ ] **Step 2: 运行测试确认失败**

运行 `CollectionProgressTests`。
预期：编译失败，`CollectionProgress` 类型不存在。

- [ ] **Step 3: 写最小实现**

创建 `Assets/Inventory/Runtime/CollectionProgress.cs`：

```csharp
using System.Collections.Generic;

/// <summary>
/// 收藏进度：三态流转的状态机。
///
/// 未获得 → 已获得（<see cref="Obtain"/>，撤离带出时调用）
///        → 已点亮（<see cref="TryLight"/>，玩家在展馆手动点亮）
///
/// 只存 ID 字符串，不存物品引用 —— 物品增删不会导致存档解析失败。
/// 纯 C#，不依赖 Unity，可独立测试。
/// </summary>
public sealed class CollectionProgress
{
    private readonly HashSet<string> obtainedIds = new HashSet<string>();
    private readonly HashSet<string> litIds = new HashSet<string>();

    /// <summary>已获得资格的物品 ID（含已点亮的）。</summary>
    public IReadOnlyCollection<string> ObtainedIds => obtainedIds;

    /// <summary>已点亮的物品 ID。</summary>
    public IReadOnlyCollection<string> LitIds => litIds;

    /// <summary>全收集额外奖励是否已发放。</summary>
    public bool AllCollectedRewarded { get; set; }

    /// <summary>标记为已获得。首次获得返回 true，重复获得返回 false。</summary>
    public bool Obtain(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        return obtainedIds.Add(id);
    }

    public bool IsObtained(string id)
    {
        return !string.IsNullOrEmpty(id) && obtainedIds.Contains(id);
    }

    /// <summary>点亮。仅「已获得且未点亮」时成功，不可撤销。</summary>
    public bool TryLight(string id)
    {
        if (!IsObtained(id))
            return false;

        return litIds.Add(id);
    }

    public bool IsLit(string id)
    {
        return !string.IsNullOrEmpty(id) && litIds.Contains(id);
    }

    /// <summary>在给定 ID 集合内统计已点亮数量。用于忽略孤儿 ID。</summary>
    public int LitCountWithin(IEnumerable<string> validIds)
    {
        return CountWithin(litIds, validIds);
    }

    /// <summary>在给定 ID 集合内统计已获得数量。用于忽略孤儿 ID。</summary>
    public int ObtainedCountWithin(IEnumerable<string> validIds)
    {
        return CountWithin(obtainedIds, validIds);
    }

    /// <summary>
    /// 校正数据一致性：把 litIds 里但 obtainedIds 里没有的 ID 补进 obtainedIds。
    /// 加载存档后调用一次。
    /// </summary>
    public void Reconcile()
    {
        foreach (var id in litIds)
            obtainedIds.Add(id);
    }

    /// <summary>测试用：绕过 Obtain 直接写入 litIds，用于构造脏数据。</summary>
    internal void ForceLitForTests(string id)
    {
        litIds.Add(id);
    }

    private static int CountWithin(HashSet<string> source, IEnumerable<string> validIds)
    {
        if (validIds == null)
            return 0;

        var count = 0;
        foreach (var id in validIds)
        {
            if (source.Contains(id))
                count++;
        }

        return count;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

运行 `CollectionProgressTests`。
预期：8 个测试全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Assets/Inventory/Runtime/CollectionProgress.cs Assets/Inventory/Runtime/CollectionProgress.cs.meta Assets/Tests/EditMode/CollectionProgressTests.cs Assets/Tests/EditMode/CollectionProgressTests.cs.meta
git commit -m "feat(collection): 新增收藏进度状态机"
```

---

## Task 3: CollectionSaveData + CollectionStore（存档层）

**Files:**
- Create: `Assets/Inventory/Runtime/CollectionSaveData.cs`
- Create: `Assets/Inventory/Runtime/CollectionStore.cs`
- Test: `Assets/Tests/EditMode/CollectionStoreTests.cs`

**Interfaces:**
- Consumes: `CollectionProgress`（Task 2）
- Produces:
  - `CollectionSaveData.CurrentVersion → int`（= 1）
  - `CollectionSaveData.version / obtainedIds / litIds / allCollectedRewarded`
  - `CollectionStore.PrefsKey → string`（= `"CollectionSave"`）
  - `CollectionStore.HasSave → bool`
  - `CollectionStore.Load() → CollectionProgress`
  - `CollectionStore.Save(CollectionProgress progress)`
  - `CollectionStore.Flush()`
  - `CollectionStore.Delete()`

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/CollectionStoreTests.cs`：

```csharp
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
```

- [ ] **Step 2: 运行测试确认失败**

运行 `CollectionStoreTests`。
预期：编译失败，`CollectionStore` 类型不存在。

- [ ] **Step 3: 写最小实现**

创建 `Assets/Inventory/Runtime/CollectionSaveData.cs`：

```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// 收藏存档载体。只存 ID 字符串，不存物品引用。
/// </summary>
[Serializable]
public sealed class CollectionSaveData
{
    /// <summary>当前存档格式版本。</summary>
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public List<string> obtainedIds = new List<string>();
    public List<string> litIds = new List<string>();
    public bool allCollectedRewarded;

    /// <summary>补齐可能为 null 的列表（JsonUtility 对缺失字段会给 null）。</summary>
    public void Validate()
    {
        obtainedIds ??= new List<string>();
        litIds ??= new List<string>();
    }
}
```

创建 `Assets/Inventory/Runtime/CollectionStore.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 收藏存档读写：<see cref="CollectionProgress"/> ↔ <see cref="CollectionSaveData"/> ↔ PlayerPrefs JSON。
///
/// 刻意使用**独立存档键**（<see cref="PrefsKey"/>），与仓库的 <c>PlayerSave</c> 分开：
/// 收藏是长期进度，不该因仓库存档损坏而一起清零。
/// </summary>
public static class CollectionStore
{
    public const string PrefsKey = "CollectionSave";

    public static bool HasSave => PlayerPrefs.HasKey(PrefsKey);

    /// <summary>读取存档。缺失或解析失败时返回空进度，不抛异常。</summary>
    public static CollectionProgress Load()
    {
        var progress = new CollectionProgress();

        if (!PlayerPrefs.HasKey(PrefsKey))
            return progress;

        var json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return progress;

        CollectionSaveData data;
        try
        {
            data = JsonUtility.FromJson<CollectionSaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CollectionStore] 收藏存档解析失败，已回退为空进度：{e.Message}");
            return progress;
        }

        if (data == null)
        {
            Debug.LogWarning("[CollectionStore] 收藏存档为空，已回退为空进度。");
            return progress;
        }

        data.Validate();

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

    /// <summary>写入存档。只调用 PlayerPrefs.SetString，真正落盘由 <see cref="Flush"/> 完成。</summary>
    public static void Save(CollectionProgress progress)
    {
        if (progress == null)
            return;

        var data = new CollectionSaveData
        {
            version = CollectionSaveData.CurrentVersion,
            obtainedIds = new List<string>(progress.ObtainedIds),
            litIds = new List<string>(progress.LitIds),
            allCollectedRewarded = progress.AllCollectedRewarded,
        };

        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
    }

    /// <summary>立即落盘。</summary>
    public static void Flush() => PlayerPrefs.Save();

    /// <summary>删除存档，主要用于测试与「重新开始」。</summary>
    public static void Delete()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

运行 `CollectionStoreTests`。
预期：5 个测试全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Assets/Inventory/Runtime/CollectionSaveData.cs Assets/Inventory/Runtime/CollectionSaveData.cs.meta Assets/Inventory/Runtime/CollectionStore.cs Assets/Inventory/Runtime/CollectionStore.cs.meta Assets/Tests/EditMode/CollectionStoreTests.cs Assets/Tests/EditMode/CollectionStoreTests.cs.meta
git commit -m "feat(collection): 新增收藏独立存档读写"
```

---

## Task 4: CollectionService（业务层：结算、点亮、发奖）

**Files:**
- Create: `Assets/Inventory/Runtime/CollectionService.cs`
- Test: `Assets/Tests/EditMode/CollectionServiceTests.cs`

**Interfaces:**
- Consumes: `CollectionCatalog`（Task 1）、`CollectionProgress`（Task 2）、`CollectionStore`（Task 3）、`ItemDatabase`、`ItemInstance.Definition`、`ItemDefinition.Id`、`ItemDefinition.Rarity`
- Produces:
  - `CollectionService.Configure(ItemDatabase database, CollectionCatalog catalog)`
  - `CollectionService.Progress → CollectionProgress`
  - `CollectionService.Collectibles → IReadOnlyList<ItemDefinition>`
  - `CollectionService.RecordRaidLoot(IReadOnlyList<ItemInstance> loot) → int`（返回新获得数量）
  - `CollectionService.TryLight(string id, out int reward) → bool`
  - `CollectionService.StateOf(string id) → CollectionState`
  - `CollectionService.LitCount → int` / `ObtainedCount → int` / `TotalCount → int`
  - `CollectionService.ResetForTests()`
  - `enum CollectionState { Locked, Obtained, Lit }`

**注意**：奖励发放通过 `Action<int>` 回调 `RewardGranted` 暴露，**不直接依赖 `PlayerCurrency`**（`PlayerCurrency` 在 `Assembly-CSharp`，`Inventory` 程序集不可引用）。UI 层订阅该事件后调用 `PlayerCurrency.Add`。

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/CollectionServiceTests.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="CollectionService"/> 的结算、点亮与发奖。
/// </summary>
public sealed class CollectionServiceTests
{
    private ItemDatabase database;
    private CollectionCatalog catalog;
    private ItemDefinition alloy;
    private ItemDefinition ration;
    private ItemDefinition weapon;
    private readonly List<int> grantedRewards = new List<int>();

    [SetUp]
    public void SetUp()
    {
        CollectionStore.Delete();
        CollectionService.ResetForTests();
        grantedRewards.Clear();

        database = ScriptableObject.CreateInstance<ItemDatabase>();
        catalog = ScriptableObject.CreateInstance<CollectionCatalog>();

        alloy = TestItems.Create(EquipmentSlotType.None, "废合金");
        ration = TestItems.Create(EquipmentSlotType.None, "口粮");
        weapon = TestItems.Weapon("步枪");
        SetDatabaseItems(alloy, ration, weapon);

        CollectionService.Configure(database, catalog);
        CollectionService.RewardGranted += grantedRewards.Add;
    }

    [TearDown]
    public void TearDown()
    {
        CollectionService.RewardGranted -= grantedRewards.Add;
        CollectionStore.Delete();
        CollectionService.ResetForTests();
        Object.DestroyImmediate(database);
        Object.DestroyImmediate(catalog);
    }

    [Test]
    public void RecordRaidLoot_UnlocksLootItems()
    {
        var loot = new List<ItemInstance> { TestItems.Instance(alloy) };

        var added = CollectionService.RecordRaidLoot(loot);

        Assert.That(added, Is.EqualTo(1));
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained));
    }

    [Test]
    public void RecordRaidLoot_IgnoresNonCollectibleItems()
    {
        var loot = new List<ItemInstance> { TestItems.Instance(weapon) };

        var added = CollectionService.RecordRaidLoot(loot);

        Assert.That(added, Is.EqualTo(0), "武器不应进图鉴");
        Assert.That(CollectionService.StateOf(weapon.Id), Is.EqualTo(CollectionState.Locked));
    }

    [Test]
    public void RecordRaidLoot_IsIdempotent()
    {
        var loot = new List<ItemInstance> { TestItems.Instance(alloy) };
        CollectionService.RecordRaidLoot(loot);

        var added = CollectionService.RecordRaidLoot(loot);

        Assert.That(added, Is.EqualTo(0), "重复带出不应重复计数");
    }

    [Test]
    public void RecordRaidLoot_WithNull_DoesNotThrow()
    {
        Assert.That(CollectionService.RecordRaidLoot(null), Is.EqualTo(0));
    }

    [Test]
    public void TryLight_GrantsRarityReward()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });

        var ok = CollectionService.TryLight(alloy.Id, out var reward);

        Assert.That(ok, Is.True);
        Assert.That(reward, Is.EqualTo(30), "普通品质奖励 30");
        Assert.That(grantedRewards, Is.EqualTo(new[] { 30 }));
    }

    [Test]
    public void TryLight_RequiresObtained()
    {
        var ok = CollectionService.TryLight(alloy.Id, out var reward);

        Assert.That(ok, Is.False);
        Assert.That(reward, Is.EqualTo(0));
        Assert.That(grantedRewards, Is.Empty);
    }

    [Test]
    public void TryLight_OnLastItem_GrantsAllCollectedBonus()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance>
        {
            TestItems.Instance(alloy),
            TestItems.Instance(ration),
        });

        CollectionService.TryLight(alloy.Id, out _);
        CollectionService.TryLight(ration.Id, out _);

        // 两件单件奖励 + 全收集 3000。
        Assert.That(grantedRewards, Is.EqualTo(new[] { 30, 30, 3000 }));
    }

    [Test]
    public void TryLight_DoesNotGrantAllCollectedBonusTwice()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance>
        {
            TestItems.Instance(alloy),
            TestItems.Instance(ration),
        });
        CollectionService.TryLight(alloy.Id, out _);
        CollectionService.TryLight(ration.Id, out _);
        grantedRewards.Clear();

        // 再点亮一次已点亮的物品，不应再发任何奖励。
        CollectionService.TryLight(alloy.Id, out _);

        Assert.That(grantedRewards, Is.Empty);
    }

    [Test]
    public void Progress_IgnoresOrphanIds()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });
        // 手工塞一个不在图鉴里的 ID。
        CollectionService.Progress.Obtain("OrphanItem");

        Assert.That(CollectionService.TotalCount, Is.EqualTo(2), "图鉴总数应为 2（合金 + 口粮）");
        Assert.That(CollectionService.ObtainedCount, Is.EqualTo(1), "孤儿 ID 不应计入");
    }

    [Test]
    public void StateOf_ReflectsThreeStates()
    {
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Locked));

        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained));

        CollectionService.TryLight(alloy.Id, out _);
        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Lit));
    }

    [Test]
    public void RecordRaidLoot_PersistsAcrossReload()
    {
        CollectionService.RecordRaidLoot(new List<ItemInstance> { TestItems.Instance(alloy) });
        CollectionStore.Flush();

        CollectionService.ResetForTests();
        CollectionService.Configure(database, catalog);

        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained));
    }

    private void SetDatabaseItems(params ItemDefinition[] items)
    {
        var serialized = new SerializedObject(database);
        var array = serialized.FindProperty("items");
        array.arraySize = items.Length;
        for (var i = 0; i < items.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `CollectionServiceTests`。
预期：编译失败，`CollectionService` 类型不存在。

- [ ] **Step 3: 写最小实现**

创建 `Assets/Inventory/Runtime/CollectionService.cs`：

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>收藏图鉴中一件物品的三态。</summary>
public enum CollectionState
{
    /// <summary>未获得：黑色剪影。</summary>
    Locked = 0,

    /// <summary>已获得待点亮：灰色剪影。</summary>
    Obtained = 1,

    /// <summary>已点亮：彩图。</summary>
    Lit = 2,
}

/// <summary>
/// 收藏成就的业务门面：撤离结算、点亮、发奖、对账。
///
/// 奖励通过 <see cref="RewardGranted"/> 事件对外广播，**不直接依赖 PlayerCurrency**
/// —— 后者在 Assembly-CSharp，本程序集（Inventory）不可引用。
/// UI 层订阅该事件后调用 PlayerCurrency.Add。
/// </summary>
public static class CollectionService
{
    private static ItemDatabase database;
    private static CollectionCatalog catalog;
    private static CollectionProgress progress;
    private static List<ItemDefinition> collectibles;

    /// <summary>发放奖励时触发，参数为星币数量。UI 层据此调用 PlayerCurrency.Add。</summary>
    public static event Action<int> RewardGranted;

    /// <summary>当前进度。首次访问时从存档加载。</summary>
    public static CollectionProgress Progress => progress ??= CollectionStore.Load();

    /// <summary>当前图鉴条目（动态计算，已缓存）。</summary>
    public static IReadOnlyList<ItemDefinition> Collectibles
    {
        get
        {
            if (collectibles == null)
                collectibles = new List<ItemDefinition>(catalog != null ? catalog.Collectibles(database) : Array.Empty<ItemDefinition>());

            return collectibles;
        }
    }

    /// <summary>图鉴条目总数。</summary>
    public static int TotalCount => Collectibles.Count;

    /// <summary>已点亮数量（忽略孤儿 ID）。</summary>
    public static int LitCount => Progress.LitCountWithin(ValidIds());

    /// <summary>已获得数量（忽略孤儿 ID）。</summary>
    public static int ObtainedCount => Progress.ObtainedCountWithin(ValidIds());

    /// <summary>注入依赖。场景初始化时调用，重复调用安全。</summary>
    public static void Configure(ItemDatabase itemDatabase, CollectionCatalog collectionCatalog)
    {
        database = itemDatabase;
        catalog = collectionCatalog;
        collectibles = null;
    }

    /// <summary>
    /// 撤离结算：把本局拾取的战利品计入收藏。
    /// 返回本次新获得资格的数量。
    ///
    /// 只应传入 <c>RaidBag.Loot</c>（本局拾取清单），**不要传 carry 数组**
    /// —— 后者含玩家带入地牢的初始装备，不算「从局内带出」。
    /// </summary>
    public static int RecordRaidLoot(IReadOnlyList<ItemInstance> loot)
    {
        if (loot == null || loot.Count == 0)
            return 0;

        var added = 0;
        foreach (var instance in loot)
        {
            var definition = instance?.Definition;
            if (definition == null)
                continue;

            if (!IsCollectible(definition))
                continue;

            if (Progress.Obtain(definition.Id))
                added++;
        }

        if (added > 0)
        {
            CollectionStore.Save(Progress);
            CollectionStore.Flush();
        }

        return added;
    }

    /// <summary>
    /// 点亮一件已获得的收藏品并发放奖励。
    /// 成功时 <paramref name="reward"/> 为本次发放的星币总额（含可能的全收集奖励）。
    /// </summary>
    public static bool TryLight(string id, out int reward)
    {
        reward = 0;

        if (!Progress.TryLight(id))
            return false;

        var definition = FindCollectible(id);
        if (definition != null && catalog != null)
            reward += catalog.RewardFor(definition.Rarity);

        // 全收集判定：以当前图鉴总数为基准（动态），而非写死的 16。
        if (!Progress.AllCollectedRewarded && TotalCount > 0 && LitCount >= TotalCount)
        {
            Progress.AllCollectedRewarded = true;
            if (catalog != null)
                reward += catalog.AllCollectedReward;
        }

        CollectionStore.Save(Progress);
        CollectionStore.Flush();

        if (reward > 0)
            RewardGranted?.Invoke(reward);

        return true;
    }

    /// <summary>查询某件物品当前的三态。</summary>
    public static CollectionState StateOf(string id)
    {
        if (Progress.IsLit(id))
            return CollectionState.Lit;

        return Progress.IsObtained(id) ? CollectionState.Obtained : CollectionState.Locked;
    }

    /// <summary>测试用：丢弃全部运行时状态（存档不受影响）。</summary>
    public static void ResetForTests()
    {
        database = null;
        catalog = null;
        progress = null;
        collectibles = null;
        RewardGranted = null;
    }

    private static bool IsCollectible(ItemDefinition definition)
    {
        foreach (var entry in Collectibles)
        {
            if (entry == definition)
                return true;
        }

        return false;
    }

    private static ItemDefinition FindCollectible(string id)
    {
        foreach (var entry in Collectibles)
        {
            if (entry != null && entry.Id == id)
                return entry;
        }

        return null;
    }

    private static IEnumerable<string> ValidIds()
    {
        foreach (var entry in Collectibles)
        {
            if (entry != null)
                yield return entry.Id;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

运行 `CollectionServiceTests`。
预期：11 个测试全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Assets/Inventory/Runtime/CollectionService.cs Assets/Inventory/Runtime/CollectionService.cs.meta Assets/Tests/EditMode/CollectionServiceTests.cs Assets/Tests/EditMode/CollectionServiceTests.cs.meta
git commit -m "feat(collection): 新增收藏业务层（结算/点亮/发奖）"
```

---

## Task 5: GameSession 撤离钩子接入

**Files:**
- Modify: `Assets/Inventory/Runtime/GameSession.cs:102-108`
- Test: `Assets/Tests/EditMode/CollectionServiceTests.cs`（追加一个集成测试）

**Interfaces:**
- Consumes: `CollectionService.RecordRaidLoot`（Task 4）、`RaidBag.Loot`
- Produces: `GameSession.ExtractRaid()` 行为变更——撤离时自动结算收藏

- [ ] **Step 1: 写失败的测试**

在 `Assets/Tests/EditMode/CollectionServiceTests.cs` 末尾（`SetDatabaseItems` 之前）追加：

```csharp
    [Test]
    public void ExtractRaid_UnlocksLootButNotCarriedEquipment()
    {
        // 出征：把武器放进携带（模拟玩家带进地牢的装备）。
        GameSession.ResetForTests();
        GameSession.EnsureInitialized(database);
        GameSession.Raid.TryAddToCarry(TestItems.Instance(weapon));

        // 局内捡到一件材料。
        GameSession.Raid.TryAddToCarry(TestItems.Instance(alloy));
        GameSession.Raid.RecordLoot(TestItems.Instance(alloy));

        GameSession.ExtractRaid();

        Assert.That(CollectionService.StateOf(alloy.Id), Is.EqualTo(CollectionState.Obtained),
            "局内拾取的材料应获得收藏资格");
        Assert.That(CollectionService.StateOf(weapon.Id), Is.EqualTo(CollectionState.Locked),
            "带入地牢的装备不应获得收藏资格");
    }
```

- [ ] **Step 2: 运行测试确认失败**

运行 `CollectionServiceTests.ExtractRaid_UnlocksLootButNotCarriedEquipment`。
预期：FAIL —— `StateOf(alloy.Id)` 返回 `Locked`，因为 `ExtractRaid` 还没接钩子。

- [ ] **Step 3: 写最小实现**

修改 `Assets/Inventory/Runtime/GameSession.cs` 的 `ExtractRaid`：

```csharp
    /// <summary>
    /// 撤离成功：局内携带并入局外仓库，然后落盘。
    /// 返回仓库放不下的物品数量（0 表示全部带出）。
    /// </summary>
    public static int ExtractRaid()
    {
        // 收藏结算：只算本局拾取的战利品，不含带入地牢的初始装备。
        CollectionService.RecordRaidLoot(Raid.Loot);

        var overflow = LoadoutService.ExtractToLoadout(Raid, Loadout);
        PlayerSaveStore.Save(Loadout);
        PlayerSaveStore.Flush();
        return overflow;
    }
```

- [ ] **Step 4: 运行测试确认通过**

运行 `CollectionServiceTests`。
预期：12 个测试全部 PASS。

- [ ] **Step 5: 提交**

```bash
git add Assets/Inventory/Runtime/GameSession.cs Assets/Tests/EditMode/CollectionServiceTests.cs
git commit -m "feat(collection): 撤离时结算本局战利品收藏"
```

---

## Task 6: CollectionSlotView（单个收藏图标视图）

**Files:**
- Create: `Assets/UI/MainMenu/Scripts/CollectionSlotView.cs`

**Interfaces:**
- Consumes: `CollectionState`（Task 4）、`ItemDefinition`、`UiTokens`
- Produces:
  - `CollectionSlotView.Bind(ItemDefinition item, CollectionState state, Action<string> onClicked)`
  - `CollectionSlotView.SetState(CollectionState state)`
  - `CollectionSlotView.ItemId → string`

**说明**：本任务只写脚本，prefab 接线在 Task 8。脚本需在无 prefab 时也能编译通过（字段可为 null，做空值防御）。

- [ ] **Step 1: 写实现**

创建 `Assets/UI/MainMenu/Scripts/CollectionSlotView.cs`：

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 收藏展馆里的单个图标：三态表现 + 点击点亮。
///
/// 无格子底框 —— 图标直接浮在面板背景上，尺寸比背包内图标大（192px）。
/// 剪影靠同一张彩图 + 深色 tint 实现，不需要额外美术资源。
/// </summary>
public sealed class CollectionSlotView : MonoBehaviour, IPointerClickHandler
{
    [Header("引用")]
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image qualityPlate;

    [Header("剪影配色")]
    [Tooltip("未获得：黑色剪影。")]
    [SerializeField] private Color lockedTint = new Color(0f, 0f, 0f, 0.55f);

    [Tooltip("已获得待点亮：灰色剪影。")]
    [SerializeField] private Color obtainedTint = new Color(0.42f, 0.44f, 0.46f, 0.75f);

    [Header("呼吸提示")]
    [Tooltip("已获得待点亮时，alpha 在 [min, max] 之间正弦往复。")]
    [SerializeField] private float breathMinAlpha = 0.6f;
    [SerializeField] private float breathMaxAlpha = 0.9f;
    [SerializeField] private float breathSpeed = 2f;

    private ItemDefinition item;
    private CollectionState state = CollectionState.Locked;
    private Action<string> clicked;

    /// <summary>本格物品的 ID；未绑定时为空字符串。</summary>
    public string ItemId => item != null ? item.Id : string.Empty;

    /// <summary>绑定物品与状态。onClicked 仅在 Obtained 状态被点击时回调，参数为物品 ID。</summary>
    public void Bind(ItemDefinition definition, CollectionState newState, Action<string> onClicked)
    {
        item = definition;
        clicked = onClicked;

        if (icon != null)
            icon.sprite = definition != null ? definition.Icon : null;

        if (nameText != null)
            nameText.text = definition != null ? definition.DisplayName : string.Empty;

        SetState(newState);
    }

    /// <summary>刷新三态表现。</summary>
    public void SetState(CollectionState newState)
    {
        state = newState;

        if (icon != null)
        {
            switch (state)
            {
                case CollectionState.Lit:
                    icon.color = Color.white;
                    break;
                case CollectionState.Obtained:
                    icon.color = obtainedTint;
                    break;
                default:
                    icon.color = lockedTint;
                    break;
            }
        }

        // 品质底板只在点亮后显示。
        if (qualityPlate != null)
        {
            qualityPlate.enabled = state == CollectionState.Lit && item != null;
            if (qualityPlate.enabled)
                qualityPlate.color = UiTokens.RarityColor(item.Rarity);
        }

        // 未获得时名称隐藏为 ???。
        if (nameText != null)
        {
            if (state == CollectionState.Locked)
                nameText.text = "???";
            else if (item != null)
                nameText.text = item.DisplayName;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (state != CollectionState.Obtained || item == null)
            return;

        clicked?.Invoke(item.Id);
    }

    private void Update()
    {
        if (state != CollectionState.Obtained || icon == null)
            return;

        // 呼吸效果：提示「可点亮」。
        var t = (Mathf.Sin(Time.unscaledTime * breathSpeed) + 1f) * 0.5f;
        var alpha = Mathf.Lerp(breathMinAlpha, breathMaxAlpha, t);
        var color = obtainedTint;
        color.a = alpha;
        icon.color = color;
    }
}
```

- [ ] **Step 2: 确认编译通过**

在 Unity 中等待编译完成，检查 Console 无报错。
预期：无编译错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/UI/MainMenu/Scripts/CollectionSlotView.cs Assets/UI/MainMenu/Scripts/CollectionSlotView.cs.meta
git commit -m "feat(collection): 新增收藏图标视图（三态表现）"
```

---

## Task 7: CollectionPanelController（展馆面板）

**Files:**
- Create: `Assets/UI/MainMenu/Scripts/CollectionPanelController.cs`

**Interfaces:**
- Consumes: `CollectionService`（Task 4）、`CollectionSlotView`（Task 6）、`PlayerCurrency`、`ItemDatabase`、`CollectionCatalog`、`UiTokens`
- Produces:
  - `CollectionPanelController.Toggle()` / `Open()` / `Close()`
  - `CollectionPanelController.IsOpen → bool`

**说明**：仿 `LoadoutPanelController` 的开关与动画结构。奖励发放通过订阅 `CollectionService.RewardGranted` 转交 `PlayerCurrency.Add`。

- [ ] **Step 1: 写实现**

创建 `Assets/UI/MainMenu/Scripts/CollectionPanelController.cs`：

```csharp
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 收藏展馆面板：4×4 图标网格 + 文字进度 + 全收集奖励状态。
///
/// 无格子底框、无进度条（进度用文字呈现）。超出 16 件时靠 ScrollRect 纵向滑动。
/// 奖励发放：订阅 <see cref="CollectionService.RewardGranted"/> 后转交 <see cref="PlayerCurrency.Add"/>。
/// </summary>
public sealed class CollectionPanelController : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalGroup;
    [SerializeField] private RectTransform panelBody;
    [SerializeField] private Button maskButton;
    [SerializeField] private Button closeButton;

    [Header("图标")]
    [Tooltip("收藏图标视图，数量等于一屏格数（16）。")]
    [SerializeField] private CollectionSlotView[] slots;

    [Header("文本")]
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text allCollectedText;

    [Header("数据")]
    [SerializeField] private ItemDatabase database;
    [SerializeField] private CollectionCatalog catalog;
    [SerializeField] private PlayerCurrency currency;

    [Header("动画")]
    [SerializeField] private float openDuration = 0.15f;
    [SerializeField] private float closeDuration = 0.1f;

    private Coroutine animationRoutine;

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    private void Awake()
    {
        CollectionService.Configure(database, catalog);
        BindButtons();

        if (modalRoot != null)
            modalRoot.SetActive(false);
    }

    private void OnEnable()
    {
        CollectionService.RewardGranted += HandleRewardGranted;
    }

    private void OnDisable()
    {
        CollectionService.RewardGranted -= HandleRewardGranted;
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (modalRoot == null || IsOpen)
            return;

        modalRoot.SetActive(true);
        PlayAnimation(open: true);
        Refresh();
    }

    public void Close()
    {
        if (modalRoot == null || !IsOpen)
            return;

        PlayAnimation(open: false);
    }

    /// <summary>按当前进度刷新全部图标与文本。</summary>
    public void Refresh()
    {
        var items = CollectionService.Collectibles;

        if (slots != null)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    continue;

                if (i < items.Count)
                {
                    var item = items[i];
                    slots[i].gameObject.SetActive(true);
                    slots[i].Bind(item, CollectionService.StateOf(item.Id), HandleSlotClicked);
                }
                else
                {
                    // 图鉴不足一屏时隐藏多余格子。
                    slots[i].gameObject.SetActive(false);
                }
            }
        }

        if (progressText != null)
        {
            var lit = CollectionService.LitCount;
            var total = CollectionService.TotalCount;
            var pending = CollectionService.ObtainedCount - lit;
            progressText.text = $"已点亮 {lit} / {total} · 待点亮 {pending} 件";
        }

        if (allCollectedText != null)
        {
            var done = CollectionService.Progress.AllCollectedRewarded;
            var amount = catalog != null ? catalog.AllCollectedReward : 0;
            allCollectedText.text = done
                ? $"全收集奖励：{amount} 星币  [已达成]"
                : $"全收集奖励：{amount} 星币  [未达成]";
        }
    }

    private void HandleSlotClicked(string id)
    {
        if (!CollectionService.TryLight(id, out _))
            return;

        Refresh();
    }

    private void HandleRewardGranted(int amount)
    {
        if (currency != null)
            currency.Add(amount);
    }

    private void BindButtons()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (maskButton != null)
            maskButton.onClick.AddListener(Close);
    }

    private void PlayAnimation(bool open)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(Animate(open));
    }

    private IEnumerator Animate(bool open)
    {
        var duration = open ? openDuration : closeDuration;
        var from = open ? 0f : 1f;
        var to = open ? 1f : 0f;

        if (modalGroup != null)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                modalGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            modalGroup.alpha = to;
        }

        if (!open && modalRoot != null)
            modalRoot.SetActive(false);

        animationRoutine = null;
    }
}
```

- [ ] **Step 2: 确认编译通过**

在 Unity 中等待编译完成，检查 Console 无报错。
预期：无编译错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/UI/MainMenu/Scripts/CollectionPanelController.cs Assets/UI/MainMenu/Scripts/CollectionPanelController.cs.meta
git commit -m "feat(collection): 新增收藏展馆面板控制器"
```

---

## Task 8: 场景接线（Catalog 资产、面板 prefab、主菜单入口）

**Files:**
- Create: `Assets/Inventory/Items/CollectionCatalog.asset`（MCP: `unity_asset_import` 或 `unity_execute_code`）
- Create: `Assets/UI/MainMenu/Prefabs/CollectionPanel.prefab`（MCP: `unity_asset_create_prefab`）
- Modify: `Assets/UI/MainMenu/Prefabs/MainMenu.prefab`（`Btn_Storage` 改造）
- Modify: `Assets/Scenes/MainMenu.unity`（挂载面板实例）

**Interfaces:**
- Consumes: Task 1-7 的全部类型
- Produces: 可运行的完整功能

**重要**：按项目硬性规范，**一切能在 Unity MCP 上完成的修改必须走 MCP 工具**，不要直接改磁盘文件。

- [ ] **Step 1: 创建 CollectionCatalog 资产**

用 MCP `unity_execute_code` 创建资产：

```csharp
var catalog = ScriptableObject.CreateInstance<CollectionCatalog>();
UnityEditor.AssetDatabase.CreateAsset(catalog, "Assets/Inventory/Items/CollectionCatalog.asset");
UnityEditor.AssetDatabase.SaveAssets();
```

预期：`Assets/Inventory/Items/CollectionCatalog.asset` 生成，Inspector 中可见排除名单（空）与奖励表（30/60/150/400/1000，全收集 3000）。

- [ ] **Step 2: 创建 CollectionPanel prefab**

用 MCP 工具在 `Assets/UI/MainMenu/Prefabs/` 下创建 `CollectionPanel.prefab`，结构：

```
CollectionPanel (RectTransform, CanvasGroup, CollectionPanelController)
├── Mask (Image, Button)              ← 全屏半透明遮罩，点击关闭
└── Body (RectTransform, Image)       ← 面板主体，UiTokens.PanelBackground
    ├── Title (TMP_Text)              ← "收藏展馆"
    ├── CloseButton (Image, Button)   ← 右上角关闭
    ├── ProgressText (TMP_Text)       ← 进度文字
    ├── ScrollView (ScrollRect)
    │   └── Viewport (Mask)
    │       └── Content (GridLayoutGroup, 4 列)
    │           └── Slot_00 .. Slot_15 (CollectionSlotView × 16)
    │               ├── Icon (Image, 192×192)
    │               ├── QualityPlate (Image, 9-slice)
    │               └── Name (TMP_Text)
    └── AllCollectedText (TMP_Text)   ← 全收集奖励状态
```

接线要点：
- `CollectionPanelController.slots` ← 16 个 `CollectionSlotView`
- `CollectionPanelController.database` ← `Assets/Inventory/Items/ItemDatabase.asset`
- `CollectionPanelController.catalog` ← 上一步创建的 `CollectionCatalog.asset`
- `CollectionPanelController.currency` ← 场景中 `PlayerCurrency` 组件
- `CollectionSlotView.icon` / `nameText` / `qualityPlate` ← 各自子对象
- `GridLayoutGroup`：`cellSize = (192, 192)`，`spacing` 适中，`constraint = FixedColumnCount`，`constraintCount = 4`
- `QualityPlate` 的 `Image.sprite` ← `Assets/UI/Common/Sprites/QualityPlate_Rounded.png`，`type = Sliced`

- [ ] **Step 3: 在 MainMenu 场景挂载面板**

用 MCP 把 `CollectionPanel.prefab` 实例化到 `Assets/Scenes/MainMenu.unity`，与现有 `LoadoutPanel` / `ShopPanel` 同级。

- [ ] **Step 4: 改造 Btn_Storage 为「收藏」入口**

用 MCP 修改 `Assets/UI/MainMenu/Prefabs/MainMenu.prefab` 中的 `Btn_Storage`：
- 按钮文字改为「收藏」
- `m_OnClick` 的 `m_Target` 改为场景中 `CollectionPanelController` 实例
- `m_MethodName` 改为 `Toggle`
- 贴图暂不替换（待用户提供素材）

- [ ] **Step 5: 保存场景与 prefab**

用 MCP `unity_scene_save` 保存场景，并保存 prefab。

- [ ] **Step 6: 实机验证**

进入 Play 模式，验证：
1. 主菜单点击「收藏」→ 展馆打开，16 个黑色剪影 + `???` 名称
2. 进度文字显示 `已点亮 0 / 16 · 待点亮 0 件`
3. 全收集文字显示 `全收集奖励：3000 星币  [未达成]`
4. 关闭按钮与遮罩点击均可关闭

- [ ] **Step 7: 提交**

```bash
git add Assets/Inventory/Items/CollectionCatalog.asset Assets/Inventory/Items/CollectionCatalog.asset.meta Assets/UI/MainMenu/Prefabs/CollectionPanel.prefab Assets/UI/MainMenu/Prefabs/CollectionPanel.prefab.meta Assets/UI/MainMenu/Prefabs/MainMenu.prefab Assets/Scenes/MainMenu.unity
git commit -m "feat(collection): 接线收藏展馆面板与主菜单入口"
```

---

## Task 9: 端到端验证与回归

**Files:**
- 无新增文件

- [ ] **Step 1: 运行全部 EditMode 测试**

在 Unity Test Runner 中运行全部 EditMode 测试。
预期：全绿，包括新增的 4 个测试类与既有测试（`GameSessionTests`、`PlayerSaveStoreTests` 等）。

- [ ] **Step 2: 端到端实机验证**

Play 模式完整走一遍：
1. 主菜单 → 出征 → 地牢中拾取若干材料
2. 撤离成功 → 返回主菜单 → 打开收藏展馆
3. 验证拾取的材料显示为**灰色剪影**（已获得待点亮），名称可见
4. 点击灰色剪影 → 变为**彩图** + 品质底板，星币余额增加对应金额
5. 验证带入地牢的装备**未**获得收藏资格（仍为黑色剪影）
6. 死亡一局 → 验证本局拾取的材料**未**获得资格

- [ ] **Step 3: 验证存档隔离**

1. 点亮若干物品后退出 Play 模式
2. 用 `PlayerPrefs.DeleteKey("PlayerSave")` 模拟仓库存档损坏
3. 重进 Play 模式 → 验证收藏进度**仍然保留**

- [ ] **Step 4: 验证动态适应**

1. 在 `ItemDatabase.asset` 中新增一件材料物品（`equipSlotType = None`）
2. 重进 Play 模式 → 验证图鉴自动多一格（黑色剪影），进度分母变为 17

- [ ] **Step 5: 提交（如有修复）**

```bash
git add -A
git commit -m "fix(collection): 端到端验证修复"
```

---

## 自检记录

**Spec 覆盖检查：**

| Spec 章节 | 对应 Task |
|---|---|
| 三态模型 | Task 2（状态机）、Task 6（视觉） |
| 收藏范围（口径 3） | Task 1 |
| 奖励配置（品质表 + 全收集） | Task 1（配置）、Task 4（发放） |
| 独立存档键 | Task 3 |
| 撤离结算只算 loot | Task 5 |
| 加载对账（孤儿 ID） | Task 2（`Reconcile`）、Task 4（`CountWithin`） |
| UI 布局（192px / 4×4 / 无格子 / 无进度条 / 滑动） | Task 7、Task 8 |
| 主菜单入口替换 | Task 8 |
| 动态适应 | Task 1、Task 9 Step 4 |
| 测试计划 | Task 1-5 各自的测试 |

**类型一致性检查：**
- `CollectionState` 在 Task 4 定义，Task 6/7 使用 —— 一致。
- `CollectionService.RewardGranted` 在 Task 4 定义（`Action<int>`），Task 7 订阅 —— 一致。
- `CollectionProgress.Obtain/TryLight/IsObtained/IsLit/LitCountWithin/ObtainedCountWithin/Reconcile` 在 Task 2 定义，Task 3/4 使用 —— 一致。
- `CollectionStore.PrefsKey/Load/Save/Flush/Delete/HasSave` 在 Task 3 定义，Task 4/5 使用 —— 一致。
- `CollectionCatalog.Collectibles/RewardFor/AllCollectedReward` 在 Task 1 定义，Task 4/7 使用 —— 一致。
- `CollectionSlotView.Bind/SetState/ItemId` 在 Task 6 定义，Task 7 使用 —— 一致。

**已知偏差说明：**
- 设计文档中 `CollectionService` 直接调用 `PlayerCurrency.Add`，但 `PlayerCurrency` 位于 `Assembly-CSharp`，而 `Inventory` 程序集 `references: []` 不可引用它。因此改为 `RewardGranted` 事件 + UI 层转交。这是实现约束导致的必要调整，功能语义不变。
- 设计文档提到「点击 Obtained 图标时先弹确认框」，本计划简化为直接点亮（`HandleSlotClicked` 直接调用 `TryLight`）。若需确认框，可在 Task 7 的 `HandleSlotClicked` 中插入弹窗，属后续增强。
