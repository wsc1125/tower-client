# 掉落与拾取系统设计

## 目标

敌人死亡后按掉落表掷点产生物品，物品以实体形式落在地上；玩家在**附近没有存活敌人**时靠近，HUD 弹出「附近物品」列表，玩家从列表中选择拾取，物品进入现有背包（与 `BagPanelController` 共用同一个 `Inventory` 实例）。

同时为 `ItemDefinition` 补充「价值」（`value`）数据维度，作为后续经济系统的基础。

## 范围

### 本轮包含

- `ItemDefinition` 增加 `value`（价值）与 `worldSize`（地面体量）字段
- `LootTable`（ScriptableObject）：条目 = 物品 + 掉落概率，纯逻辑掷点
- `EnemyBrain2D` 增加 `Died` 事件
- `LootDropper`（敌人组件）：死亡时掷表并生成掉落物
- `DroppedItem2D`：地面掉落物实体
- `LootInteractor`（玩家侧）：近距检测 + 附近敌人判定 + 驱动面板 + 执行拾取
- `LootPanelController` + `LootRowView`（BattleHUD）：附近物品列表 UI
- 场景与预制体接线（敌人挂掉落、玩家挂交互、HUD 加面板）

### 本轮不包含

- 经济系统（金币累加、消费、商店、出售）
- 物品堆叠数量、物品分类、稀有度分级
- 掉落物的生命周期 / 自动消失、物理弹跳散落
- 拾取动画、掉落物吸附飞行
- 角色手持武器的显示适配（`WeaponMountSlot` 现有行为保持不变，不做尺寸 / 朝向归一化）
- 统一尺寸（不做"所有掉落物一样大"的一刀切）：体量逐物品在 `ItemDefinition.worldSize` 上配置，比例永远保留
- 存档与持久化
- 任何联机代码（Netcode / RPC）。仅为将来联机保留两个收口：**掷点的单一函数**与**拾取的单一入口**
- 「玩家死亡重置」对掉落物的处理（该重置逻辑当前尚未实现；若后续实现，掉落物保持在地面不清理）

## 已确认参数（占位值，可后续调整）

| 项 | 值 | 说明 |
| --- | --- | --- |
| 掉落概率（本轮） | TrainingSword `1.0`；TrainingBow `0`；Rifle `0` | 仅用于验证链路。三条独立掷点，后续在 asset 里调数值即可，不用改结构 |
| 物品价值 `value` | TrainingSword 60 / TrainingBow 45 / Rifle 120 | 占位值 |
| 拾取半径 `pickupRadius` | 1.6 | 世界单位，以玩家为中心 |
| 战斗判定半径 `combatRadius` | 8 | 此半径内存在存活敌人则不显示列表 |
| 列表最大行数 `maxEntries` | 4 | 超出只显示最近的 4 件 |
| 扫描间隔 `scanInterval` | 0.2 | 秒 |
| 掉落散落半径 `scatterRadius` | 0.35 | 多件掉落时避免完全重叠 |
| 物品体量 `worldSize` | 逐物品在 `ItemDefinition` 上填 | 贴图**最大边**的目标世界长度。剑约 0.42、竖立箱盒约 0.48、方形盾约 0.45 |
| 世界贴图 PPU | 不再影响地面显示 | 只要导入为 Sprite 即可，PPU 随意（`icon` 同理） |
| 检测碰撞体半径 | `worldSize ÷ 2` | 与视觉体量一致，在 `Initialize` 中设置 |

## 架构

程序集依赖现状为 `Gameplay → BattleHUD → Inventory`，**`BattleHUD` 不可反向引用 `Gameplay`**（否则循环编译错误）。因此列表 UI 不能认识 `DroppedItem2D`，采用「Gameplay 侧驱动 BattleHUD 面板」的既有分层（与 `PlayerMovement2D → VirtualJoystickController` 同构）。

**本设计不新增、不修改任何 `asmdef`。**

### 组件与职责

| 类型 | 位置 | 职责 |
| --- | --- | --- |
| `ItemDefinition`（改） | `Assets/Inventory/Runtime/` | 增加 `value` 与 `worldSize` 字段 |
| `LootTable` | `Assets/Inventory/Runtime/` | ScriptableObject：条目 = 物品 + 概率；纯逻辑掷点 |
| `LootEntry`（struct） | `Assets/UI/BattleHUD/Scripts/` | 面板展示数据：名称 / 价值 / 图标 |
| `LootRowView` | `Assets/UI/BattleHUD/Scripts/` | 单行视图，点击抛索引 |
| `LootPanelController` | `Assets/UI/BattleHUD/Scripts/` | 列表 UI 的显示 / 隐藏 / 提示 |
| `LootDropper` | `Assets/Gameplay/Runtime/` | 订阅敌人死亡 → 掷点 → 生成掉落物 |
| `DroppedItem2D` | `Assets/Gameplay/Runtime/` | 地面掉落物实体，持有 `ItemDefinition` |
| `LootScan` | `Assets/Gameplay/Runtime/` | 纯逻辑：附近敌人判定、掉落物排序截断 |
| `LootInteractor` | `Assets/Gameplay/Runtime/` | 近距检测 + 附近敌人判定 + 驱动面板 + 执行拾取 |
| `EnemyBrain2D`（改） | `Assets/Gameplay/Runtime/` | 增加 `event Action Died` |

## 组件设计

### `ItemDefinition`（改）

```csharp
[SerializeField] private int value;
[SerializeField] private float worldSize = 0.4f;   // 贴图「最大边」的目标世界长度

public int Value => value;
public float WorldSize => worldSize;
```

- `value` 默认 0，本轮只用于列表展示，不做任何累加或消费
- `worldSize` 是**逐物品**的体量配置：剑填 0.42、竖立箱盒填 0.48、方形盾填 0.45。它只决定「多大」，**不改变贴图的比例** —— 剑永远细长、箱盒永远竖立

### `LootTable`（ScriptableObject）

```csharp
[CreateAssetMenu(menuName = "Inventory/Loot Table", fileName = "LootTable")]
public sealed class LootTable : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public ItemDefinition item;
        [Range(0f, 1f)] public float dropChance;
    }

    [SerializeField] private Entry[] entries;

    public IReadOnlyList<ItemDefinition> Roll(System.Random random);
    public static bool ShouldDrop(float dropChance, double roll); // 纯函数，可测
}
```

- **每条独立掷点**，互不影响，所以一次死亡可能掉多件
- 掷点接收 `System.Random` 而不是直接调 `UnityEngine.Random`：EditMode 可用固定种子验证可复现性；将来把「权威随机」移到服务器时只改这一处
- 边界：`dropChance <= 0` 恒不掉、`dropChance >= 1` 恒掉；`item` 为空的条目跳过
- `random` 为 null 时返回空列表（沿用既有空引用静默策略）

### `EnemyBrain2D`（改）

新增 `public event Action Died;`，在 [TakeDamage](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/EnemyBrain2D.cs#L123-L134) 判定死亡的那一刻触发一次。其余行为（`state = Dead`、关闭碰撞体、播放死亡帧、不销毁）保持不变。

**必须防重入**：`EnemyHealth.TakeDamage` 在 `IsDead` 时早退，只保证血量不会重复扣减，**并不能**阻止 `EnemyBrain2D.TakeDamage` 再次进入死亡分支 —— 它判断的是「当前是否已死」，第二次调用时敌人已经死了，条件同样成立。因此在 `EnemyBrain2D.TakeDamage` 开头加了 `if (health.IsDead) return;`，否则 `Died` 会在每次后续受伤时重复触发（实测：连打两次，监听者收到 2 次事件）。

### `LootDropper`（挂在敌人预制体）

```csharp
public sealed class LootDropper : MonoBehaviour
{
    [SerializeField] private EnemyBrain2D enemy;
    [SerializeField] private LootTable lootTable;
    [SerializeField] private DroppedItem2D dropPrefab;
    [SerializeField] private float scatterRadius = 0.35f;
}
```

- `Awake` 订阅 `enemy.Died`（`enemy` 为空时用 `GetComponent<EnemyBrain2D>()` 兜底）；`OnDestroy` 取消订阅
- 死亡时：`lootTable` 或 `dropPrefab` 为空 → 静默返回，不抛异常
- 逐件生成：位置 = 敌人位置 + 半径 `scatterRadius` 内的随机偏移

### `DroppedItem2D`（地面掉落物）

```csharp
public sealed class DroppedItem2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer visual;        // 子物体 Visual 上的渲染器
    [SerializeField] private CircleCollider2D pickupZone;  // IsTrigger = true
    public ItemDefinition Item { get; }
    public void Initialize(ItemDefinition item);
}
```

- 渲染贴图取 `item.WorldSprite`；为 `null` 时回退 `item.Icon`
- **按最大边等比缩放**：`visual.transform.localScale = Vector3.one * (item.WorldSize / Mathf.Max(bounds.x, bounds.y))`。只调整**体量**，绝不改变**比例** —— 剑仍是细长的，箱盒仍是竖立的
- `pickupZone.radius = item.WorldSize * 0.5f`，在 `Initialize` 中设置，使检测范围与视觉体量一致
- `pickupZone` 为 IsTrigger，不阻挡玩家 / 敌人移动
- 依赖 `Physics2D.queriesHitTriggers` 默认值 `true`（已确认 `ProjectSettings/Physics2DSettings.asset` 未覆盖该项）
- 不设生命周期，一直留在地面

**为什么可以安全回退 `Icon`**：尺寸由 `item.WorldSize` 决定，与贴图自身的像素尺寸、PPU 无关。所以即使回退到 1254×1254 的方形 UI 图标，也只会被缩放成 `worldSize` 那么大，不会出现巨型方块。

**PPU 不再影响地面显示**，导入贴图时不用再为世界尺寸做换算。

### `LootScan`（纯逻辑）

```csharp
public static class LootScan
{
    // 半径内是否存在存活敌人（尸体不算）
    public static bool HasAliveEnemyWithin(EnemyBrain2D[] enemies, Vector2 origin, float radius);

    // 按到 origin 的距离升序排序，截断到 max 个
    public static IReadOnlyList<DroppedItem2D> SelectNearest(DroppedItem2D[] candidates, Vector2 origin, int max);
}
```

抽成纯静态方法以便 EditMode 直接验证排序 / 截断 / 死亡敌人过滤，`LootInteractor` 只负责物理查询与驱动。

### `LootInteractor`（挂在玩家预制体根）

```csharp
public sealed class LootInteractor : MonoBehaviour
{
    [SerializeField] private float pickupRadius = 1.6f;
    [SerializeField] private float combatRadius = 8f;
    [SerializeField] private int maxEntries = 4;
    [SerializeField] private float scanInterval = 0.2f;
    [SerializeField] private InventoryHolder inventory;
    [SerializeField] private LootPanelController panel;
}
```

每 `scanInterval` 秒执行一轮：

1. **附近敌人判定**：`FindObjectsOfType<EnemyBrain2D>()` 中存在 `!IsDead` 且与玩家距离 < `combatRadius` 的敌人 → `panel.Hide()`，本轮结束
2. **非战斗**：`Physics2D.OverlapCircleAll(transform.position, pickupRadius)` → 过滤出 `GetComponentInParent<DroppedItem2D>()` → 去重 → `LootScan.SelectNearest(...)` 取最近 `maxEntries` 个
3. 结果与上一轮缓存一致 → 不刷 UI（避免重复重建行）

拾取流程：

- `Awake` 订阅 `panel.ItemSelected(index)`；`OnDestroy` 取消订阅
- 拾取时取回缓存的第 `index` 个 `DroppedItem2D` → `TryPickup(...)`
- `TryPickup` 内部：`inventory.Model.TryAdd(item)`
  - 成功 → `Destroy(掉落物)`，并立即重扫刷新列表
  - 失败（背包满）→ `panel.ShowHint("背包已满")`，物品留在地上
- `inventory` 为空或 `panel` 为空 → 不动作、不抛异常

**联机预留**：拾取只经由 `TryPickup(DroppedItem2D)` 这一个方法。将来接联机时，把它内部改成「发请求 + 服务器校验后在服务器端 `TryAdd` 并销毁掉落物」，客户端其余代码不动。

### `LootEntry` / `LootRowView` / `LootPanelController`（BattleHUD）

```csharp
public struct LootEntry
{
    public string DisplayName;
    public int Value;
    public Sprite Icon;
}

public sealed class LootPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private LootRowView[] rows;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private float hintDuration = 1.5f;

    public event Action<int> ItemSelected;

    public void Show(IReadOnlyList<LootEntry> entries);
    public void Hide();
    public void ShowHint(string message);
}
```

- `Show`：按 `entries` 数量激活前 N 行并填充（图标若为空则该行图标隐藏），行数不足时其余行隐藏
- `LootRowView`：图标 + 名称 + `价值 xxx` + `Button`，点击 → `ItemSelected(index)`
- `Hide`：`panelRoot.SetActive(false)`
- `ShowHint`：设置提示文本并在 `hintDuration`（默认 1.5 秒）后清空
- 面板不含滚动，最多 4 行固定

## 数据流

```
敌人死亡:
  EnemyBrain2D.TakeDamage → 血量归零 → Died 事件
    → LootDropper: LootTable.Roll(random) 逐条掷点
      → 每件命中物品生成一个 DroppedItem2D（地面，带 IsTrigger 碰撞体）

玩家侧扫描（每 0.2s）:
  combatRadius 内存在存活敌人 → LootPanel.Hide()
  否则 → OverlapCircleAll(pickupRadius) → 过滤 DroppedItem2D → SelectNearest(最多 4)
       → 列表变化 → LootPanel.Show(entries)

拾取:
  LootPanel 行点击 → ItemSelected(index) → LootInteractor.TryPickup(dropped)
    → Inventory.TryAdd(item)
        ├ 成功 → 销毁掉落物 + 立即重扫
        └ 失败 → ShowHint("背包已满")，掉落物保留
```

## 预制体 / 数据 / 场景改动

### 新增 `Assets/Gameplay/Prefabs/DroppedItem.prefab`

- 根 `DroppedItem`：`DroppedItem2D`、`CircleCollider2D`（IsTrigger，半径由 `Initialize` 设为 `worldSize ÷ 2`）
- 子 `Visual`：`SpriteRenderer`（`sortingOrder` 低于角色；**等比缩放施加在这个节点**上）

### 新增 `Assets/Gameplay/Data/Loot_MechJelly.asset`

`LootTable` 实例，三条：TrainingSword `1.0` / TrainingBow `0` / Rifle `0`，按「已确认参数」填写。

### 现有资产改动

- `Assets/Inventory/Items/TrainingSword.asset` → `value: 60`
- `Assets/Inventory/Items/TrainingBow.asset` → `value: 45`
- `Assets/Inventory/Items/Rifle.asset` → `value: 120`

### `Enemy_MechJelly.prefab`

挂 `LootDropper`，接线 `enemy`（自身 `EnemyBrain2D`）、`lootTable`、`dropPrefab`。

### `BattleHUD.prefab` / `BattleScene`

新增 `LootPanel` 子面板（4 个 `LootRowView` + 提示文本），位置先放在屏幕中下偏左、避开左下摇杆底盘与右下攻击按钮区域；建好后截图确认再微调。

### `Character_Base.prefab`

挂 `LootInteractor`，接线 `inventory` → 场景中的 `PlayerContext/InventoryHolder`，`panel` → `HUDCanvas/BattleHUD/LootPanel`。

## 美术素材需求

### 每个物品必需（2 张）

| 素材 | 字段 | 用途 | 规格 | 现有基准 |
| --- | --- | --- | --- | --- |
| 物品图标 | `ItemDefinition.icon` | 背包格子、掉落列表行、拖拽幽灵图 | 1:1 方形、透明背景、主体居中留 8~12% 边距 | `武器图标.png` / `弓箭图标.png` = 1254×1254；背包格内实际只显示 96×96 UI px，**512×512 已富余** |
| 世界贴图 | `ItemDefinition.worldSprite` | 地面掉落物实体 | 透明背景、视角统一、**按物品自然比例出图（不强制横向）**、紧凑裁切、不带阴影 | 已有素材任意尺寸均可，体量靠 `worldSize` 统一 |

**贴图尺寸不再需要精确控制**：

- 出图**任意像素尺寸都行**，PPU 用默认值也无所谓
- 体量由 `ItemDefinition.worldSize` 决定（贴图最大边被缩放到的世界长度）
- 比例完全由美术决定：细长的剑、方形的盾、竖立的箱盒各自保留自己的形状
- 新增物品只需「画出来 + 在 asset 上填一个 `worldSize` 数字」，不用算像素、不用调 PPU
- `icon` 与 `worldSprite` **允许指向同一张贴图**，最少 1 张图即可接入

注意：`WeaponMountSlot` 仍然会读取 `worldSprite` 作为角色手上武器（该行为本轮保持不变、不做任何改动），但本轮**不为其做尺寸或朝向适配**，即不考虑手持表现。

### 已接入的素材（光圈）

4 张：`白色.png` / `红色.png` / `绿色.png` / `金色.png`，源目录 `C:\Users\16648\Desktop\素材\物品\光圈\`，
落到 `Assets/Gameplay/Art/Loot/Glow_{White,Gold,Green,Red}.png`。

| 项 | 值 |
| --- | --- |
| 内容包围盒 | 818×320（金色 804×321） |
| 导入 PPU | 100（世界尺寸 8.18 × 3.20） |
| 质检结论 | 中心弱（alpha 45~62）→ 最亮环落在 55~60% 半径 → 连续衰减到透明；四档峰值一致（差 2~3） |

**本轮只接白色一档**（`Glow_White`），验证视觉与层级；颜色按 `value` 分档等物品数量上来后再做。

接入方式：`DroppedItem.prefab` 下与 `Visual` 平级的 `Glow` 节点（`sortingOrder = 6`，`Visual` 为 7 —— 必须高于敌人的 3，否则会被尸体压住）。
光圈**不随物品宽高比变形** —— 它按自身「宽度」对齐物品体量：`世界宽 = worldSize × glowWidthRatio（1.6）`，
保持素材等比缩放，并把节点底边贴到物品底边（`localPosition.y = -物品世界高度 ÷ 2`），读起来像「地上的一滩光」。

### 建议复用（全局各 1 张，不随物品增加）

| 素材 | 说明 |
| --- | --- |
| 掉落物底光 / 光圈 | 叠在掉落物下面做「可拾取」提示 |
| 拾取特效 | 参考现有 `RifleImpact.prefab` 的做法 |
| 列表行底板 | 1 张九宫格 |

### 本轮不需要

稀有度边框与分级图标（不做稀有度）、物品描述图与商店出售图（不做经济）、掉落物弹跳动画帧（不做物理散落）、每个物品单独的掉落物预制体（共用 `DroppedItem.prefab`，换 sprite 区分）。

### 已知素材问题

- `Rifle.png`（260×124）当 1:1 图标用会显得又小又空，与其它物品风格不一致 → 建议为 Rifle 单独补一张方形图标
- `TrainingBow.asset` 没有 `worldSprite` → 会回退使用 `icon`（方形 UI 图标），体量由 `worldSize` 保证正确，但视觉上会是「一个 UI 图标躺在地上」→ 建议补一张世界贴图
- 光圈素材（`白色/红色/绿色/金色.png`）已定稿，见「已确认素材」一节

## 测试策略

### EditMode

- `LootTableTests`（`Assets/Tests/EditMode/`，归 `BattleHUD.EditModeTests`，该程序集已引用 `Inventory`）：概率 0 恒不掉 / 1 必掉 / 固定种子结果可复现 / 多条目互相独立 / 空 item 条目被跳过 / `random` 为 null 返回空
- `EnemyBrain2DTests`（补，`Assets/Tests/EditMode/Gameplay/`）：`Died` 只触发一次、死亡后再次 `TakeDamage` 不重复触发
- `LootScanTests`（`Assets/Tests/EditMode/Gameplay/`）：`SelectNearest` 的距离排序与截断、`HasAliveEnemyWithin` 对存活 / 死亡敌人的过滤与边界距离
- `DroppedItem2DTests`（`Assets/Tests/EditMode/Gameplay/`）：缩放后贴图**最大边**等于 `worldSize` 且**宽高比不变**；`worldSprite` 为空时回退 `icon` 且体量同样受 `worldSize` 约束；`pickupZone.radius` 等于 `worldSize ÷ 2`；光圈世界宽等于 `worldSize × glowWidthRatio` 且等比缩放不变形、贴物品底边、无贴图/无物品时关闭
- `ItemDefinitionValidationTests`（`Assets/Tests/EditMode/`，归 `BattleHUD.EditModeTests`）：遍历 `Assets/Inventory/Items/` 下所有 `ItemDefinition`，断言 `displayName` 非空、`worldSize > 0`、`icon != null` —— 把「忘填」从「运行时看不见东西」提前到「跑测试就红」
- `LootPanelControllerTests`（`Assets/Tests/EditMode/`，归 `BattleHUD.EditModeTests`）：`Show` 激活正确行数与内容、`Hide` 关闭面板、行点击抛出的索引正确、`ShowHint` 设置并清空文本

### Play Mode 手测

- 击杀敌人 → 地面出现掉落物图标（当前必掉一件）
- 附近无敌人时靠近 → 面板出现，列出名称与价值
- 点击列表行 → 物品进入背包（打开背包可见）
- 背包满（20 格）→ 提示「背包已满」，掉落物仍在地面
- 敌人进入 `combatRadius` → 面板自动隐藏
- 拾取后走出拾取半径 → 面板自动隐藏

#### 实机验证结论（2026-09-17，Unity 2022.3.62f3c1，Editor Play Mode）

验证方式：通过 Unity MCP 进入 Play Mode，用 `EditorApplication.Step()` 逐步推帧（后台 Editor 不会自动跑帧循环），每步后读运行期状态 + 截图。

| 验证项 | 结果 |
| --- | --- |
| 击杀 → 掉落 | 通过。`Died` 触发后生成 1 件「训练长剑」；`Visual.localScale = 0.41`（= `worldSize 0.42` ÷ 贴图最大边 1.02），碰撞体 `radius = 0.21`（= `worldSize ÷ 2`） |
| 靠近 → 列表出现 | 通过。玩家距掉落物 0.5 世界单位时面板出现，行内容为「训练长剑 / 价值 60」+ 图标，距离 3.0 时不出现 |
| 点击行 → 进背包 | 通过。`bag[0]` 得到该物品，地面掉落物被销毁，面板随扫描周期隐藏 |
| 背包满提示 | 通过。20/20 时 `TryPickup` 返回 false，提示「背包已满」，掉落物保留在地面，提示约 1.5s 后自动清空 |
| 敌人靠近 → 隐藏 | 通过。存活敌人进入 `combatRadius`（8）后面板隐藏，移出后自动重新出现 |
| 敌人靠近（死亡敌人） | 通过。死亡敌人不计入 `HasAliveEnemyWithin`，不会挡住列表 |

**实机验证中发现并修复的缺陷**：`Scan()` 原本在「物品集合与上轮相同」时直接早退，隐含了「面板当前一定是显示状态」的假设 —— 一旦面板被外部隐藏，列表就再也不会出现。现改为：集合未变时也检查 `panel.IsVisible`，未显示则重新 `Show`。已在 Play Mode 复现（手动 `Hide()` 后 30 帧内自动恢复）并修复。

**已知问题（待确认，非功能缺陷）**：
- `LootPanel` 背景是固定 420×500 的纯色块，只有 1~2 行时下方留大片空白。
- 背景色 `(0.08, 0.10, 0.14, 0.85)` 与房间地砖颜色接近，实机上几乎看不出面板边界（用临时纯红实测确认背景确实在渲染）。

**测试盲区**：EditMode 下 `Physics2D.OverlapCircleAll` 查不到测试中新建立的碰撞体，因此「靠近显示列表」「敌人靠近隐藏」这两条只能在 Play Mode 验证，未覆盖进 EditMode 测试。

#### 光圈 + 野生狗奶 实机验证（2026-09-17 二次）

同样用 MCP + `EditorApplication.Step()` 推帧验证。

| 验证项 | 结果 |
| --- | --- |
| 掉光圈的尺寸/位置 | 通过。`Glow` 世界尺寸 0.72×0.41，物品（野生狗奶）世界尺寸 0.28×0.45 —— 光圈比物品宽、贴在其底边 |
| 光圈层级 | 通过。`Floor_Base` order 0 → `Glow` order 6 → `Visual` order 7，光圈压在地面上、物品压在光圈上（层级数值的修正见下方「修复」） |
| 光圈渲染 | 通过（截图确认）。深色地砖上能清楚看到白色椭圆光斑，物品底部坐在光斑上 |
| 击杀 → 掉两件 | 通过。击杀 `Enemy_MechJelly` 后同时生成「训练长剑」与「野生狗奶」，两件都带白色光圈 |
| 列表内容 | 通过。靠近后列表两行：`训练长剑 / 价值 60`、`野生狗奶 / 价值 30` |
| 点击拾取 | 通过。点第 2 行后 `野生狗奶` 进背包，地面只剩「训练长剑」 |
| 掉落物不被尸体遮挡 | 通过（截图确认）。物品叠在敌人尸体上时完整可见，光圈也不被尸体挡住 |

**修复**：光圈首版用了 `Glow = 1 / Visual = 2`，而敌人 `Enemy_MechJelly` 的 `Visual` 是 `sortingOrder = 3`，导致掉落物被尸体压住。现改为 `Glow = 6 / Visual = 7`。

当前 `Default` 层的排序约定：地面 0 → 敌人 3 → 敌方激光 5 → **光圈 6 → 掉落物 7** → 墙体 10 → 子弹/命中特效 15 → 玩家 20~22。后续接入新东西时在这个序列里找空位，不要在「敌人 3」以下给地面物件排层级。

#### UI 输入优先级修复（点击列表被摇杆抢走）

**问题**：点掉落列表的行时角色会跟着动，拾取不触发。

**原因**：摇杆的捕获区是 `BattleHUD/Joystick` 下一张 `alpha = 0` 的全屏 `Image`（`raycastTarget = true`，尺寸 591×1080，覆盖屏幕左侧），它原本是 `BattleHUD` 的**最后一个子节点** —— 在 UGUI 里意味着渲染与射线检测都在最上层，于是左半屏的所有按下都先被摇杆吃掉，`LootPanel` 的行收不到事件。

**修复**：把 `BattleHUD/Joystick` 移到 `BattleHUD` 的**第一个子节点**（同级顺序：Joystick → TopResourceBar → BagButton → AttackSwitchButton → AttackButton → LootPanel → BagPanel → StatusPanel）。UGUI 的输入优先级就是层级顺序，摇杆放最底后，面板 / 按钮自然优先。

**约定**：摇杆捕获区必须是 `BattleHUD` 层级里的最底层，新增 HUD 面板时直接加在它后面，不要去改摇杆的顺序；也不要给纯装饰的 `Image` 打开 `raycastTarget`，否则会在那块区域静默吃掉摇杆输入。

**验证**（Play Mode，`EventSystem.RaycastAll` 实测）：

| 验证项 | 结果 |
| --- | --- |
| 行位置的射线命中顺序 | 通过。`Row0` → `Background` → `Joystick`，行在最上 |
| 模拟按下 + 抬起 + 点击 | 通过。事件落在 `Row1`，摇杆 `IsDragging = false`、底盘无位移，物品进背包、地面掉落物 2 → 1 |
| 空白区域仍是摇杆 | 通过。屏幕左侧空白处 `(60,120)` 的最上层命中仍是 `Joystick`，轮盘可正常拖动 |

## 约束

- 不改动 `Inventory` 的 20 格容量、装备槽与拖拽逻辑
- 不改动 `BagPanelController` 及其预制体结构（仅与它共用同一个 `Inventory` 实例）
- 不引入对象池（本房间掉落数量有限）
- 不引入任何联机依赖 / RPC / Netcode
- 不引入物品堆叠、数量、稀有度
- 不新增或修改任何 `asmdef`
- 掉落掷点与拾取各自收口到单一函数，便于将来接入权威服务器
