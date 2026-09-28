# 收藏成就系统 设计文档

日期：2026-09-27
状态：待实现

## 1. 目标

在现有 Unity 2D 俯视地牢项目中引入「收藏成就」功能：玩家在地牢中带出特定物品后，可在收藏展馆中将其点亮为彩图，并获得星币奖励。

## 2. 需求确认

| 项 | 结论 |
|---|---|
| 收藏范围 | 材料/消耗品（`equipSlotType == None`），**不含武器防具** |
| 获得条件 | 成功撤离时，本局 `RaidBag.loot` 中的物品计入「已获得」；死亡/放弃不计 |
| 获得性质 | 永久，一旦获得不再丢失 |
| 三态模型 | 未获得（黑色剪影）→ 已获得待点亮（灰色剪影）→ 已点亮（彩图） |
| 点亮方式 | 玩家在展馆中**手动点击**点亮，点亮瞬间发放星币 |
| 剪影实现 | 同一张彩图 + 深色 tint，零美术成本 |
| 奖励 | 按品质统一配置的单件奖励 + 全收集额外奖励，全部为星币 |
| 动态适应 | 新增物品自动进图鉴（口径 3：自动收录 + 排除名单） |

## 3. 三态模型

| 状态 | 视觉 | 进入条件 | 可执行动作 |
|---|---|---|---|
| `Locked` 未获得 | 黑色剪影（tint `#000000`，alpha 0.55） | 初始状态 | 无 |
| `Obtained` 已获得待点亮 | 灰色剪影（tint `#6B7075`，alpha 0.75） | 从局内带出该物品 | 点击点亮 |
| `Lit` 已点亮 | 彩图 + 品质底板 | 玩家手动点亮 | 无（不可撤销） |

状态流转：

```
Locked --[撤离带出]--> Obtained --[手动点亮 + 发奖]--> Lit
```

规则：
- 重复带出同一物品：若已是 `Obtained` 或 `Lit`，无变化，不叠加。
- 点亮不可撤销。
- 全收集额外奖励在**最后一件被点亮时自动发放**，无需二次领取。
- 「全收集」的判定基准是**当前图鉴条目总数**（动态计算，见第 4 节），而非写死的 16。若日后新增材料使总数变为 20，则需点亮 20 件才触发全收集奖励。

## 4. 收藏范围（口径 3：自动收录 + 排除名单）

图鉴条目在运行时动态计算：

```
收藏条目 = ItemDatabase.Items
           .Where(i => i.equipSlotType == EquipmentSlotType.None)   // 排除武器防具
           .Where(i => !catalog.excluded.Contains(i))               // 排除任务道具等特例
```

- 新增普通材料：自动进图鉴，零配置。
- 新增武器防具：自动排除。
- 新增任务道具/钥匙：拖进 `CollectionCatalog.excluded` 即可。

`CollectionCatalog` **不存物品白名单**，只存排除名单与奖励配置。

## 5. 奖励配置

### 5.1 单件奖励（按品质统一）

| 品质 | 件数 | 单件奖励（星币） |
|---|---|---|
| Common 普通 | 3 | 30 |
| Fine 精良 | 4 | 60 |
| Rare 稀有 | 4 | 150 |
| Epic 史诗 | 3 | 400 |
| Legendary 传说 | 2 | 1000 |

单件合计：3×30 + 4×60 + 4×150 + 3×400 + 2×1000 = **4130 星币**

### 5.2 全收集额外奖励

**3000 星币**，在最后一件被点亮时自动发放。

总计可获得：**7130 星币**。

### 5.3 配置载体

品质→金额映射表放在 `CollectionCatalog` 中，不修改 `ItemDefinition`。

## 6. 架构

### 6.1 模块划分

新增独立模块，与现有存档解耦。

| 类型 | 职责 | 依赖 |
|---|---|---|
| `CollectionCatalog` (ScriptableObject) | 排除名单 + 品质奖励表 + 全收集奖励额 | `ItemDefinition` |
| `CollectionProgress` (纯 C#) | 持有 `obtainedIds` / `litIds` / `allCollectedRewarded`，提供查询与变更方法 | 无 |
| `CollectionSaveData` (可序列化) | 存档载体，含 `version` | 无 |
| `CollectionStore` (静态) | 独立 PlayerPrefs 键 `CollectionSave` 的读写 | `CollectionSaveData` |
| `CollectionService` (静态门面) | 撤离结算、点亮、发奖、对账 | 上述全部 + `GameSession` + `PlayerCurrency` |
| `CollectionPanelController` (MonoBehaviour) | 展馆 UI 控制器 | `CollectionService` |
| `CollectionSlotView` (MonoBehaviour) | 单个收藏格视图 | `CollectionService` |

### 6.2 为什么用独立存档键

收藏是玩家投入时间最长的长期进度。现有 [PlayerSaveStore.Load](../../../Assets/Inventory/Runtime/PlayerSaveStore.cs) 在解析失败时会整体回退到 `CreateEmpty()`，若收藏数据与仓库共用一条存档链，一次存档损坏会同时清零仓库与收藏。

独立键 `CollectionSave` 使两者风险隔离：收藏存档损坏只影响收藏，且可单独恢复。

### 6.3 程序集归属

放在 `Inventory` 程序集（`Assets/Inventory/Runtime/`），理由：
- `ItemDatabase`、`ItemDefinition`、`EquipmentSlotType` 均在此程序集，无需新增引用。
- `UiTokens` 也在此程序集，UI 可直接复用色板。
- 避免新增程序集带来的 asmdef 依赖调整。

`CollectionPanelController` / `CollectionSlotView` 放在 `Assets/UI/MainMenu/Scripts/`，与现有 UI 脚本一致。

## 7. 数据流

### 7.1 撤离结算（获得资格）

现有链路：

```
GameSession.ExtractRaid()
  → LoadoutService.ExtractToLoadout(...)
```

在 `GameSession.ExtractRaid()` 中，于 `ExtractToLoadout` 调用**之前**插入钩子：

```
CollectionService.RecordRaidLoot(GameSession.Raid.Loot)
```

**关键点**：只遍历 `RaidBag.loot`（本局拾取清单），**不遍历 `carry` 数组**。因为 `carry` 包含玩家带入地牢的初始装备，按「必须从局内带出」的定义，带入的装备不应算解锁。

`RaidBag.loot` 恰好就是「本局在地牢里捡到的全部东西」，语义完全吻合。

### 7.2 点亮（发放奖励）

```
玩家点击 CollectionSlotView
  → CollectionPanelController.HandleSlotClicked(id)
  → CollectionService.TryLight(id, out int reward)
      ├─ 校验：id 必须处于 Obtained 状态
      ├─ litIds.Add(id)
      ├─ reward = catalog.RewardFor(item.rarity)
      ├─ PlayerCurrency.Add(reward)
      ├─ 若 litIds.Count == 图鉴总数 且 !allCollectedRewarded
      │     ├─ PlayerCurrency.Add(catalog.allCollectedReward)
      │     └─ allCollectedRewarded = true
      └─ CollectionStore.Save(progress)
```

### 7.3 加载与对账

`CollectionService` 首次访问时加载存档，并执行一次**对账**：

- 计算当前图鉴 ID 集合 `currentIds`。
- 存档中不在 `currentIds` 里的 ID（孤儿 ID，因物品被删除产生）**保留在存档中**，但**计数与进度条忽略**。
- 理由：万一以后物品被加回来，玩家的解锁记录不会丢。

进度条分母 = `currentIds.Count`，分子 = `litIds ∩ currentIds` 的数量。

## 8. 存档结构

独立 PlayerPrefs 键：`CollectionSave`

```json
{
  "version": 1,
  "obtainedIds": ["ScrapAlloy", "RationPack"],
  "litIds": ["ScrapAlloy"],
  "allCollectedRewarded": false
}
```

- `version` 用于未来迁移。
- `litIds` 恒为 `obtainedIds` 的子集，加载时做一次校正（若出现 `litIds` 有而 `obtainedIds` 无的脏数据，补进 `obtainedIds`）。
- 解析失败时回退为空进度，并 `Debug.LogWarning`，**不抛异常**。
- 存档只记录 ID 字符串，不记录物品引用，因此物品增删不会导致存档解析失败。

## 9. UI 设计

### 9.1 入口

**将主菜单现有的「仓库」按钮（`Btn_Storage`）替换为「收藏」按钮。**

现状核实（`Assets/UI/MainMenu/Prefabs/MainMenu.prefab`）：

| 按钮 | 位置 | 点击行为 |
|---|---|---|
| `Btn_Storage`（仓库） | 第 527 行 | 调用 `LoadoutPanelController.Toggle`（第 634-636 行） |
| `Btn_Loadout`（装备配置） | 第 1616 行 | 调用 `LoadoutPanelController.Toggle`（第 1723-1725 行） |
| `Btn_Shop`（商店） | 第 2245 行 | 调用 `ShopPanelController.Toggle`（第 2352-2354 行） |

**关键结论**：`Btn_Storage` 与 `Btn_Loadout` 指向**同一个** `LoadoutPanelController.Toggle`，功能完全重复。因此移除 `Btn_Storage` 不影响任何功能——仓库仍可通过「装备配置」按钮访问（[LoadoutPanelController](../../../Assets/UI/MainMenu/Scripts/LoadoutPanelController.cs) 本身就同时管理仓库分页网格与装备槽）。

替换方案：
- 复用 `Btn_Storage` 的 GameObject 与 RectTransform（保持位置、尺寸不变），仅替换：
  - 按钮贴图（`m_Sprite`）→ 待用户提供
  - 按钮文字 → 「收藏」
  - `m_OnClick` 目标 → `CollectionPanelController.Toggle`
- 仓库数据、存档、撤离入库逻辑**全部保留不变**，仅移除主菜单入口。

**待用户提供**：收藏按钮的贴图素材。在素材到位前，可先复用 `Btn_Storage` 现有贴图占位，仅改文字与点击目标，保证功能可测。

### 9.2 面板布局

```
┌─────────────────────────────────────────────┐
│  收藏展馆                          [关闭]   │
├─────────────────────────────────────────────┤
│  已点亮 5 / 16 · 待点亮 2 件                │
├─────────────────────────────────────────────┤
│                                             │
│    彩图      灰剪      黑剪      黑剪       │
│   废合金    口粮      ???       ???         │
│                                             │
│    黑剪      黑剪      黑剪      黑剪       │
│    ???       ???       ???       ???        │
│                                             │
│    黑剪      黑剪      黑剪      黑剪       │
│    ???       ???       ???       ???        │
│                                             │
│    黑剪      黑剪      黑剪      黑剪       │
│    ???       ???       ???       ???        │
│                                             │
├─────────────────────────────────────────────┤
│  全收集奖励：3000 星币  [未达成]            │
└─────────────────────────────────────────────┘
```

### 9.3 图标与排列

- **图标尺寸 192px**（背包内图标约 64px，展馆为其 3 倍），突出「展示」属性。
- **4×4 排列**，16 件恰好一屏放下，占约 768×768，在设计分辨率 1603×938 下留白充足。
- **无格子底框**：图标直接浮在面板背景上，不使用 `SlotBackground` 与 9-slice 边框。
- **无进度条**：进度以文字形式呈现（`已点亮 5 / 16 · 待点亮 2 件`）。
- **超出 16 件时用滑动而非翻页**：内容区套 `ScrollRect`，纵向滚动。初始 16 件不触发滚动，后续新增物品自动可滑。

### 9.4 图标状态表现

| 状态 | 图标 tint | 品质底板 | 名称 | 可点击 |
|---|---|---|---|---|
| `Locked` | `#000000`，alpha 0.55 | 隐藏 | `???` | 否 |
| `Obtained` | `#6B7075`，alpha 0.75 | 隐藏 | 真实名称 | 是（点亮） |
| `Lit` | 原色 | 显示 | 真实名称 | 否 |

- `Obtained` 状态用代码做呼吸效果（`Image.color` 的 alpha 在 0.6~0.9 之间正弦往复），提示「可点亮」，不依赖额外素材。
- 点击 `Obtained` 图标时，先弹确认框显示「点亮后获得 XXX 星币」，确认后执行。

### 9.5 视觉令牌

复用 [UiTokens](../../../Assets/Inventory/Runtime/UiTokens.cs)：`PanelBackground`、`AccentGold`、`TextPrimary/Secondary/Muted`、`RarityColor(rarity)`、`RarityPlateSpritePath`（仅 `Lit` 状态使用品质底板）。

## 10. 动态适应

| 场景 | 行为 |
|---|---|
| 新增材料物品 | 自动进图鉴，进度分母自动 +1，无需改配置 |
| 新增武器防具 | 自动排除（`equipSlotType != None`） |
| 新增任务道具 | 拖进 `CollectionCatalog.excluded` |
| 删除物品 | 存档记录保留，计数忽略，进度不错乱 |
| 物品加回来 | 原解锁记录自动生效 |
| 图鉴超过 16 件 | 内容区自动可纵向滑动，无需改布局 |

## 10.1 美术素材需求

**核心功能零新增美术**，全部复用现有资源：

| 需求 | 现有资源 |
|---|---|
| 物品彩图 | 16 张 `Item_*.png`（`Assets/Gameplay/Art/Items/`） |
| 黑色/灰色剪影 | 同一张彩图 + 代码 tint（图标为中心轴点且带 alpha，染色即得剪影轮廓） |
| 品质底板（仅 `Lit` 状态） | `QualityPlate_Rounded.png` |
| 面板背景 | `UiTokens.PanelBackground` |
| 「可点亮」提示 | 代码呼吸效果，无需素材 |

**唯一待确认项**：主菜单「收藏」入口按钮的贴图素材（由用户提供）。素材到位前，复用 `Btn_Storage` 现有贴图占位，仅改文字与点击目标，不影响功能验证。

## 11. 测试计划

按项目约定（EditMode only，`<类名>Tests` + `<方法>_<行为>`，`SetUp` 中清理存档）新增：

### `CollectionProgressTests`
- `Unlock_IsIdempotent` — 重复获得同一 ID 不产生重复记录
- `Light_RequiresObtained` — 未获得的 ID 无法点亮
- `Light_IsIrreversible` — 已点亮的 ID 再次点亮返回 false
- `LitIds_IsSubsetOfObtainedIds` — 加载脏数据时自动校正

### `CollectionStoreTests`
- `SaveAndLoad_RoundTripsProgress` — 存档往返一致
- `Load_WithCorruptedJson_ReturnsEmptyProgress` — 损坏数据回退空进度且不抛异常
- `Load_WithMissingKey_ReturnsEmptyProgress` — 无存档时返回空进度

### `CollectionServiceTests`
- `RecordRaidLoot_UnlocksOnlyLootNotCarry` — **只结算 loot，不结算带入的初始装备**
- `RecordRaidLoot_IgnoresNonCollectibleItems` — 武器防具不进图鉴
- `TryLight_GrantsRarityReward` — 点亮发放对应品质的星币
- `TryLight_OnLastItem_GrantsAllCollectedBonus` — 最后一件点亮时额外发放全收集奖励
- `TryLight_DoesNotGrantAllCollectedBonusTwice` — 全收集奖励不重复发放
- `Progress_IgnoresOrphanIds` — 孤儿 ID 不影响进度计数

### `CollectionCatalogTests`
- `Collectibles_ExcludesWeaponsAndArmor` — 自动排除 `equipSlotType != None`
- `Collectibles_ExcludesExplicitlyExcludedItems` — 排除名单生效

## 12. 实现顺序

1. `CollectionCatalog` + `CollectionProgress` + `CollectionSaveData`（纯数据层，可独立测试）
2. `CollectionStore`（存档层）
3. `CollectionService`（业务层，含撤离钩子）
4. `GameSession.ExtractRaid` 接入钩子
5. `CollectionSlotView` + `CollectionPanelController`（UI 层）
6. 主菜单入口接线：`Btn_Storage` 改造为「收藏」，改文字 + 改 `m_OnClick` 目标（贴图待素材到位后替换）
7. EditMode 测试补齐

## 13. 不在本次范围内

- 武器防具的收藏（用户明确排除）
- 永久属性加成奖励（项目无此系统）
- 收藏品的 3D 展示 / 旋转预览
- 收藏成就的分享 / 导出功能
