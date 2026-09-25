# 高塔爬塔玩法设计

## 目标

把当前「主菜单 + 单房间战斗沙盒」改造成一条完整的爬塔循环：

主菜单出发 → 第 1 层 → 清空敌人 → 传送点出现 → 进入下一层 → …… → 撤退层（5 的倍数）二选一（继续爬 / 撤退结算）→ 回主菜单。

层数越高，敌人越强、掉落物价值越高。玩家在撤退层主动撤退可保住本局全部战利品；死亡则清空全部库存（沿用现有惩罚）。

## 范围

### 本轮包含

* `TowerConfig`（ScriptableObject）：难度曲线、掉落分档、撤退层间隔、敌人池权重

* `FloorPlan` / `FloorGenerator`（纯 C#）：由层数推导出该层内容，可单测

* `TowerRunManager`（MonoBehaviour）：串联「生成一层 → 统计清空 → 生成传送点 → 换层 → 撤退/死亡收尾」

* `FloorPortal2D`：传送点组件，`Next` / `Retreat` 两种

* `EnemyStatScaler`：按层数缩放敌人血量与伤害

* `TowerRunState`：跨层保留的局内状态（当前层数、本局战利品清单）

* `TowerResultPanelController` + `TowerResultRowView`（BattleHUD）：撤退结算面板

* `StatusPanelController` 层数显示接线（`SetLevel` 已有接口，当前无调用方）

* `LootInteractor` 改为面向 `IDamageable`（当前只认 `EnemyBrain2D`）

* `PlayerDeathFlow2D` 增加「本局战利品清单」清空

* 场景与预制体接线

### 本轮不包含

* 程序化房间生成（复用现有 `Room_Battle_Closed_01`，每层实例化一份）

* 每层独立场景（已确认走单场景内换房间）

* 层间商店 / 补给点 / 事件房

* 存档持久化（`TowerRunState` 只活在本次战斗场景内，退出即丢）

* 层数上限（塔无限高，靠难度曲线自然劝退）

* 联机（Netcode / RPC）

* 新敌人类型、新美术素材

* 稀有度参与数值计算（`ItemRarity` 仍只用于展示）

* 一键出售战利品（已确认结算面板只展示）

## 已确认参数（占位值，可后续在 asset 里调整）

| 项          | 值                               | 说明                                    |
| ---------- | ------------------------------- | ------------------------------------- |
| 撤退层间隔      | 5                               | 第 5、10、15… 层为撤退层                      |
| 敌人血量系数     | `1 + 0.15 × (层数 - 1)`           | 第 1 层 ×1.00，第 10 层 ×2.35，第 20 层 ×3.85 |
| 敌人伤害系数     | `1 + 0.10 × (层数 - 1)`           | 第 1 层 ×1.00，第 10 层 ×1.90，第 20 层 ×2.90 |
| 敌人数量       | `1 + floor((层数 - 1) / 2)`，上限 6  | 第 1 层 1 只，第 3 层 2 只，第 11 层 6 只        |
| 掉落分档       | 1-4 / 5-9 / 10-14 / 15-19 / 20+ | 五档，对应 `TowerConfig` 里五张 `LootTable`   |
| 敌人池        | 太空海盗 / 机械水母 / 炮台 / 母巢           | 虫群由母巢召唤，不进池；Boss 本轮不进池                |
| 敌人池权重      | 海盗 4 / 水母 3 / 炮台 2 / 母巢 1       | 母巢最稀有（它会持续产虫，压力最大）                    |
| 传送点触发半径    | 0.8                             | 世界单位，`CircleCollider2D` IsTrigger     |
| 传送点出现延迟    | 0.6                             | 秒，清空后稍等再出现，避免战斗刚结束就闪出来                |
| 敌人出生点距房间中心 | 3.5 \~ 5.5                      | 环形随机，避开玩家出生点（房间中心）                    |
| 玩家出生点      | 房间中心                            | 每层换房后把玩家放回中心                          |

## 架构

程序集依赖现状为 `Gameplay → BattleHUD → Inventory`，**`BattleHUD`** **不可反向引用** **`Gameplay`**。因此结算面板不能认识 `ItemDefinition`，采用「Gameplay 侧驱动 BattleHUD 面板」的既有分层（与 `LootInteractor → LootPanelController` 同构）。

**本设计不新增、不修改任何** **`asmdef`。**

### 组件与职责

| 类型                           | 位置                             | 职责                                                  |
| ---------------------------- | ------------------------------ | --------------------------------------------------- |
| `TowerConfig`                | `Assets/Gameplay/Runtime/`     | ScriptableObject：难度系数、掉落分档、敌人池、撤退间隔                 |
| `FloorPlan`（struct）          | `Assets/Gameplay/Runtime/`     | 一层的纯数据：层数、是否撤退层、敌人条目数组、掉落表                          |
| `FloorGenerator`             | `Assets/Gameplay/Runtime/`     | 纯静态：`Generate(floor, config, random)` → `FloorPlan` |
| `TowerRunState`              | `Assets/Gameplay/Runtime/`     | 局内状态：当前层数、本局战利品清单、是否已结束                             |
| `TowerRunManager`            | `Assets/Gameplay/Runtime/`     | 流程编排：生成层、统计清空、生成传送点、换层、撤退/死亡收尾                      |
| `FloorPortal2D`              | `Assets/Gameplay/Runtime/`     | 传送点：`Next` / `Retreat`，玩家踩上触发                       |
| `EnemyStatScaler`            | `Assets/Gameplay/Runtime/`     | 按层数缩放敌人血量与伤害                                        |
| `TowerResultEntry`（struct）   | `Assets/UI/BattleHUD/Scripts/` | 结算面板纯展示数据：名称 / 价值 / 图标                              |
| `TowerResultRowView`         | `Assets/UI/BattleHUD/Scripts/` | 结算面板单行                                              |
| `TowerResultPanelController` | `Assets/UI/BattleHUD/Scripts/` | 结算面板：显示战利品清单与总价值，确认后回主菜单                            |
| `LootInteractor`（改）          | `Assets/Gameplay/Runtime/`     | 附近敌人判定改为面向 `IDamageable`                            |
| `PlayerDeathFlow2D`（改）       | `Assets/Gameplay/Runtime/`     | 死亡时额外清空本局战利品清单                                      |
| `StatusPanelController`（改）   | `Assets/UI/BattleHUD/Scripts/` | 无需改代码，由 `TowerRunManager` 调用现有 `SetLevel`           |

## 组件设计

### `TowerConfig`（ScriptableObject）

```csharp
[CreateAssetMenu(menuName = "Gameplay/Tower Config", fileName = "TowerConfig")]
public sealed class TowerConfig : ScriptableObject
{
    [Serializable]
    public struct EnemyWeight
    {
        public GameObject prefab;
        public int weight;
    }

    [Serializable]
    public struct LootTier
    {
        public int fromFloor;      // 含
        public LootTable table;
    }

    [Header("难度曲线")]
    [SerializeField] private float healthGrowthPerFloor = 0.15f;
    [SerializeField] private float damageGrowthPerFloor = 0.10f;
    [SerializeField] private int enemiesPerTwoFloors = 1;
    [SerializeField] private int maxEnemiesPerFloor = 6;

    [Header("撤退层")]
    [SerializeField] private int retreatEveryFloors = 5;

    [Header("敌人池")]
    [SerializeField] private EnemyWeight[] enemyPool;

    [Header("掉落分档（按 fromFloor 升序）")]
    [SerializeField] private LootTier[] lootTiers;

    public float HealthGrowthPerFloor => healthGrowthPerFloor;
    public float DamageGrowthPerFloor => damageGrowthPerFloor;
    public int MaxEnemiesPerFloor => maxEnemiesPerFloor;
    public int RetreatEveryFloors => retreatEveryFloors;
    public IReadOnlyList<EnemyWeight> EnemyPool => enemyPool;
    public IReadOnlyList<LootTier> LootTiers => lootTiers;
}
```

* 所有数值都在 Inspector 里调，改曲线不用重编译

* `enemyPool` 里 `prefab` 为空或 `weight <= 0` 的条目被跳过

### `FloorPlan` / `FloorGenerator`（纯逻辑）

```csharp
public readonly struct FloorPlan
{
    public readonly int Floor;
    public readonly bool IsRetreatFloor;
    public readonly GameObject[] Enemies;
    public readonly LootTable LootTable;
}

public static class FloorGenerator
{
    public static FloorPlan Generate(int floor, TowerConfig config, System.Random random);

    // 以下三个抽成公开静态纯函数，便于 EditMode 直接验证
    public static int ResolveEnemyCount(int floor, int perTwoFloors, int max);
    public static float ResolveHealthMultiplier(int floor, float growthPerFloor);
    public static float ResolveDamageMultiplier(int floor, float growthPerFloor);
    public static bool IsRetreatFloor(int floor, int everyFloors);
    public static LootTable ResolveLootTable(int floor, IReadOnlyList<TowerConfig.LootTier> tiers);
    public static GameObject PickEnemy(IReadOnlyList<TowerConfig.EnemyWeight> pool, System.Random random);
}
```

* `floor` 从 1 开始；`floor <= 0` 时钳制为 1

* `ResolveEnemyCount`：`1 + (floor - 1) / 2`，再 `Min(max)`

* `ResolveHealthMultiplier`：`1 + growth × (floor - 1)`

* `IsRetreatFloor`：`floor % everyFloors == 0`；`everyFloors <= 0` 时恒为 false

* `ResolveLootTable`：取 `fromFloor <= floor` 中 `fromFloor` 最大的那一档；没有匹配档时返回 null

* `PickEnemy`：按权重加权随机；池为空或总权重为 0 时返回 null

* 接收 `System.Random` 而非 `UnityEngine.Random`：EditMode 可用固定种子验证可复现性，也为将来把权威随机移到服务器留唯一改动点（与 `LootTable.Roll` 同构）

### `TowerRunState`

```csharp
public sealed class TowerRunState
{
    public int CurrentFloor { get; private set; }
    public bool IsFinished { get; private set; }
    public IReadOnlyList<ItemDefinition> CollectedLoot { get; }

    public void BeginRun();                       // 层数归 1，清空战利品
    public void AdvanceFloor();                   // 层数 +1
    public void RecordLoot(ItemDefinition item);  // 记一件本局战利品
    public void ClearLoot();                      // 死亡时清空
    public void Finish();                         // 标记本局结束，防重入
}
```

* 纯 C# 类，不继承 MonoBehaviour，可单测

* `CollectedLoot` 只记录**本局拾取**的物品，用于撤退结算面板展示；实际物品在 `Inventory.carry` 里

* `RecordLoot` 忽略 null

### `TowerRunManager`（场景单例）

```csharp
public sealed class TowerRunManager : MonoBehaviour
{
    [Header("配置")]
    [SerializeField] private TowerConfig config;
    [SerializeField] private GameObject roomPrefab;        // Room_Battle_Closed_01
    [SerializeField] private Transform roomParent;
    [SerializeField] private FloorPortal2D nextPortalPrefab;
    [SerializeField] private FloorPortal2D retreatPortalPrefab;

    [Header("接线")]
    [SerializeField] private Transform player;
    [SerializeField] private CameraFollow2D cameraFollow;
    [SerializeField] private StatusPanelController statusPanel;
    [SerializeField] private TowerResultPanelController resultPanel;
    [SerializeField] private InventoryHolder inventoryHolder;
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("节奏")]
    [SerializeField] private float portalSpawnDelay = 0.6f;
    [SerializeField] private float enemySpawnRadiusMin = 3.5f;
    [SerializeField] private float enemySpawnRadiusMax = 5.5f;
}
```

流程：

1. `Start()` → `state.BeginRun()` → `BuildFloor(1)`
2. `BuildFloor(floor)`：

   * 销毁上一层的房间实例与残留敌人 / 掉落物 / 传送点

   * 实例化 `roomPrefab`，把玩家 `Rigidbody2D.position` 与 `transform.position` 都设到房间中心

   * `cameraFollow.CaptureRoomBounds()` 重新取房间包围盒

   * `FloorGenerator.Generate(floor, config, random)` → 按 `plan.Enemies` 在环形随机点实例化敌人

   * 给每个敌人挂 `EnemyStatScaler` 并 `Apply(plan.Floor, config)`

   * `statusPanel.SetLevel(floor)`
3. 每帧（或每 0.2s）统计：`FindObjectsOfType<MonoBehaviour>()` 中所有 `IDamageable` 是否全部 `IsDead` 或已销毁

   * 注意：母巢召唤的虫群也是 `IDamageable`，必须一并计入，否则「母巢死了但虫群还在」会提前判定清层
4. 清空后延迟 `portalSpawnDelay` 生成传送点：

   * 非撤退层：只生成 `nextPortalPrefab`

   * 撤退层：同时生成 `nextPortalPrefab` 与 `retreatPortalPrefab`，分列房间中心左右两侧
5. 玩家踩到 `Next` 传送点 → `state.AdvanceFloor()` → `BuildFloor(floor + 1)`
6. 玩家踩到 `Retreat` 传送点 → `Retreat()`：

   * `state.Finish()`

   * `resultPanel.Show(entries, totalValue)`，`entries` 由 `state.CollectedLoot` 构造

   * 玩家点确认 → `SceneManager.LoadScene(mainMenuSceneName)`
7. 玩家死亡 → `PlayerDeathFlow2D` 照旧清空库存并回主菜单；`TowerRunManager` 订阅 `PlayerHealth2D.Died` 只做 `state.Finish()`，不重复切场景

**防重入**：`state.IsFinished` 为 true 后，传送点触发与死亡回调都直接返回。

**换层不销毁玩家**：只销毁房间与敌人，玩家对象跨层保留，因此 `PlayerHealth2D` 的血量与护甲天然继承到下一层（这是难度曲线生效的前提）。

### `FloorPortal2D`

```csharp
public enum PortalKind { Next, Retreat }

[RequireComponent(typeof(Collider2D))]
public sealed class FloorPortal2D : MonoBehaviour
{
    [SerializeField] private PortalKind kind;
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private string nextLabel = "下一层";
    [SerializeField] private string retreatLabel = "撤退";

    public PortalKind Kind => kind;
    public event Action<PortalKind> Entered;
}
```

* `Collider2D` 必须是 `IsTrigger = true`

* `OnTriggerEnter2D` 里 `GetComponentInParent<PlayerMovement2D>()` 判定是不是玩家；是则 `Entered?.Invoke(kind)`

* 触发后立刻关闭自身碰撞体，避免同一帧重复触发

* 只抛事件，不认识 `TowerRunManager`（由 Manager 在生成时订阅）

### `EnemyStatScaler`

```csharp
public sealed class EnemyStatScaler : MonoBehaviour
{
    public void Apply(int floor, TowerConfig config);
}
```

* 血量：`EnemyBrain2D` 的 `maximumHealth` 是私有序列化字段，无法直接改。因此给 `EnemyBrain2D` 增加公开方法 `ScaleMaximumHealth(float multiplier)`，内部基于**原始基础血量**重算（不是在上次结果上再乘，否则重复调用会指数膨胀）

* 伤害：`ArtilleryMortar2D.blastDamage`、`SwarmChaser2D.contactDamage` 各自增加 `ScaleDamage(float multiplier)`；`EnemyLaserProjectile2D` 是运行时 `Instantiate` 出来的，缩放预制体上的实例没有意义，因此改为给 `EnemyBrain2D` 增加 `ScaleLaserDamage(float multiplier)`，由它在 `FireLaser` 时把系数传给每颗弹丸

* `EnemyStatScaler` 只负责「找到这些组件并调用它们的缩放方法」，不直接反射改私有字段

* **调用时机**：`Instantiate` 后 `Awake` 会立即执行，而 `Awake` 里会创建 `health` 实例。因此 `TowerRunManager` 的生成顺序是 `Instantiate` → `SetActive(false)` → `Apply` → `SetActive(true)`，`Awake` 在 `SetActive(true)` 时才跑，缩放先生效

**为什么不用反射**：项目现有代码全部走显式公开接口（`Configure` / `EditorBind` / `Launch` 等），反射会绕过编译期检查，且 IL2CPP 下反射改私有字段不可靠。

### `LootInteractor`（改）

当前 `Scan()` 里 `FindObjectsOfType<EnemyBrain2D>()` 只覆盖 `EnemyBrain2D`，虫群与 Boss 不算「附近有敌人」，会导致战斗中点开拾取列表。

改为：`FindObjectsOfType<MonoBehaviour>()` 过滤出 `IDamageable`，或更省的做法——分别取 `EnemyBrain2D` / `SwarmChaser2D` / `BossBrain2D` 三类并合并（与 `RifleShooter2D.FindNearestEnemy` 现有写法一致）。

**采用后者**：与 `RifleShooter2D` 保持同构，避免 `FindObjectsOfType<MonoBehaviour>()` 的全量扫描开销。

### `TowerResultEntry` / `TowerResultRowView` / `TowerResultPanelController`（BattleHUD）

```csharp
public struct TowerResultEntry
{
    public string DisplayName;
    public int Value;
    public Sprite Icon;
}

public sealed class TowerResultPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TowerResultRowView[] rows;
    [SerializeField] private ScrollRect scrollRect;      // 战利品可能超过行数
    [SerializeField] private TMP_Text totalValueText;
    [SerializeField] private TMP_Text floorText;
    [SerializeField] private Button confirmButton;

    public event Action Confirmed;

    public void Show(IReadOnlyList<TowerResultEntry> entries, int totalValue, int floor);
    public void Hide();
}
```

* 与 `LootPanelController` 同构：只展示 + 抛 `Confirmed` 事件，不认识 `Gameplay`

* 战利品可能超过固定行数，因此用 `ScrollRect` 承载行列表（`LootPanelController` 是固定 4 行无滚动，这里不能照搬）

* `totalValueText` 显示 `总价值 {n}`，`floorText` 显示 `抵达第 {n} 层`

* `confirmButton` 点击 → `Confirmed?.Invoke()`

* 面板打开时隐藏 HUD 其余元素（复用 `BagPanelController.SetHudChromeVisible` 的做法）

### `PlayerDeathFlow2D`（改）

新增 `[SerializeField] private TowerRunManager towerRun;`，在 `LoseInventory()` 里额外调用 `towerRun?.AbortRun()`（内部 `state.Finish()` + `state.ClearLoot()`）。

死亡仍然清空全部库存（已确认），本局战利品清单也一并清空，避免残留到下一局。

## 数据流

```
出发（主菜单）:
  DepartFlow → LoadScene("BattleScene")

开局:
  TowerRunManager.Start → state.BeginRun() → BuildFloor(1)
    → 实例化房间 → 玩家回中心 → 相机重取包围盒
    → FloorGenerator.Generate(1, config, random) → FloorPlan
    → 按 plan.Enemies 环形随机点生成敌人 + EnemyStatScaler.Apply(1, config)
    → statusPanel.SetLevel(1)

清层:
  每 0.2s 统计所有 IDamageable（含虫群）是否全灭
    → 全灭 → 延迟 0.6s → 生成传送点
        ├ 非撤退层：Next
        └ 撤退层：Next + Retreat

推进:
  玩家踩 Next → state.AdvanceFloor() → BuildFloor(floor + 1)
    （玩家对象不销毁，血量/护甲继承）

撤退:
  玩家踩 Retreat → state.Finish()
    → resultPanel.Show(state.CollectedLoot, 总价值, 当前层数)
    → 玩家点确认 → LoadScene("MainMenu")

死亡:
  PlayerHealth2D.Died → PlayerDeathFlow2D
    → towerRun.AbortRun()（state.Finish + ClearLoot）
    → 延迟 1.5s → 清空全部库存 → LoadScene("MainMenu")

拾取:
  LootInteractor.TryPickup → Inventory.TryAddToCarry
    → 成功 → towerRun.state.RecordLoot(item)
```

## 预制体 / 数据 / 场景改动

### 新增 `Assets/Gameplay/Data/TowerConfig.asset`

按「已确认参数」填写：血量增幅 0.15、伤害增幅 0.10、每 2 层 +1 敌人、上限 6、撤退间隔 5、敌人池 4 项、掉落分档 5 档。

### 新增掉落表（`Assets/Gameplay/Data/`）

现有只有 `Loot_MechJelly.asset` 一张。需要按分档补到 5 张：

| 分档    | 文件                 | 内容                                 |
| ----- | ------------------ | ---------------------------------- |
| 1-4   | `Loot_Tier1.asset` | 复用 `Loot_MechJelly` 的条目（野生狗奶 100%） |
| 5-9   | `Loot_Tier2.asset` | 待定，需先补物品资产                         |
| 10-14 | `Loot_Tier3.asset` | 待定                                 |
| 15-19 | `Loot_Tier4.asset` | 待定                                 |
| 20+   | `Loot_Tier5.asset` | 待定                                 |

**注意**：当前 `Assets/Inventory/Items/` 只有 6 件物品（Rifle / Pierce / Shotgun / Sniper / Mobility / WildDogMilk），不足以支撑 5 档掉落。本轮先建 5 张表，Tier2-5 暂时复用现有物品并调高 `dropChance`，等新物品资产到位后再填。

### 新增 `Assets/Gameplay/Prefabs/FloorPortal_Next.prefab` / `FloorPortal_Retreat.prefab`

* 根节点：`FloorPortal2D` + `CircleCollider2D`（IsTrigger，半径 0.8）

* 子 `Visual`：`SpriteRenderer`（`sortingOrder` 取 6，与掉落物光圈同层，压在地面上、低于墙体 10）

* 两个 prefab 只有 `kind` 与贴图不同

### 新增 `Assets/UI/BattleHUD/Prefabs/TowerResultPanel.prefab`

* 全屏遮罩 + 面板体 + 标题（`抵达第 N 层`）+ `ScrollRect` 行列表 + 底部 `总价值 N` + 确认按钮

* 视觉令牌全部引用 `UiTokens`（`PanelBackground` / `LineStrong` / `TextPrimary` 等），不写字面值

* 层级顺序：加在 `BattleHUD` 的 `Joystick` **之后**（摇杆必须是最底层，见既有约定）

### `BattleScene` 改动

* 新增 `TowerRunManager` 宿主对象，接线 `config` / `roomPrefab` / 两个传送点 prefab / `player` / `cameraFollow` / `statusPanel` / `resultPanel` / `inventoryHolder`

* 移除场景里手工摆放的 `Enemy_Boss` 与 `Enemy_SpacePirate`（改由 `TowerRunManager` 运行时生成）

* 移除场景里手工摆放的 `Room_Battle_Closed_01` 实例（改由 `TowerRunManager` 运行时实例化）

* `HUDCanvas/BattleHUD` 下新增 `TowerResultPanel` 实例

### `EnemyBrain2D` / `EnemyLaserProjectile2D` / `ArtilleryMortar2D` / `SwarmChaser2D` 改动

各自新增一个公开缩放方法（见「组件设计 · `EnemyStatScaler`」），不改动现有行为。

## 美术素材需求

| 素材      | 用途                               | 规格                                        |
| ------- | -------------------------------- | ----------------------------------------- |
| 下一层传送点  | `FloorPortal_Next` 的 `Visual`    | 俯视视角、地面标记感（如向上的箭头 / 光柱），透明背景，建议 256×256   |
| 撤退传送点   | `FloorPortal_Retreat` 的 `Visual` | 与「下一层」明显区分（如向下的箭头 / 不同颜色），同规格             |
| 结算面板底板  | `TowerResultPanel` 背景            | 1 张九宫格，或直接用 `UiTokens.PanelBackground` 纯色 |
| 结算面板行底板 | `TowerResultRowView` 背景          | 1 张九宫格，或纯色                                |

传送点素材本轮可先用现有光圈素材（`Glow_White` / `Glow_Gold`）占位，靠颜色区分两种传送点，等正式素材到位再替换。

## 测试策略

### EditMode

* `FloorGeneratorTests`（`Assets/Tests/EditMode/Gameplay/`，归 `Gameplay.EditModeTests`）：

  * `ResolveEnemyCount`：第 1 层 1 只、第 3 层 2 只、第 11 层 6 只、第 20 层仍为 6（上限生效）、`floor <= 0` 钳制为 1

  * `ResolveHealthMultiplier` / `ResolveDamageMultiplier`：第 1 层为 1.0、第 10 层为 2.35 / 1.90

  * `IsRetreatFloor`：5 / 10 / 15 为 true，1 / 4 / 6 为 false，`everyFloors <= 0` 恒 false

  * `ResolveLootTable`：第 1 层取 Tier1、第 5 层取 Tier2、第 20 层取 Tier5、第 100 层仍取 Tier5（最高档兜底）

  * `PickEnemy`：固定种子结果可复现、权重为 0 的条目永不被选中、空池返回 null

  * `Generate`：`IsRetreatFloor` 与 `ResolveLootTable` 结果一致、`Enemies` 长度等于 `ResolveEnemyCount`

* `TowerRunStateTests`（同目录）：`BeginRun` 后层数为 1 且战利品为空、`AdvanceFloor` 递增、`RecordLoot` 忽略 null、`ClearLoot` 清空、`Finish` 后 `IsFinished` 为 true

* `TowerResultPanelControllerTests`（`Assets/Tests/EditMode/`，归 `BattleHUD.EditModeTests`）：`Show` 填充行与总价值文本、`Hide` 关闭面板、确认按钮抛出 `Confirmed` 事件

* `LootInteractorTests`（补，`Assets/Tests/EditMode/Gameplay/`）：虫群在 `combatRadius` 内时面板隐藏（当前只测了 `EnemyBrain2D`）

### Play Mode 手测

* 出发 → 进入第 1 层，HUD 显示 `Lv.1`，房间内有 1 只敌人

* 击杀敌人 → 0.6s 后出现「下一层」传送点

* 踩传送点 → 换到新房间，HUD 显示 `Lv.2`，敌人变多、血量变高

* 玩家血量跨层继承（第 1 层掉血后，第 2 层不是满血）

* 爬到第 5 层 → 清空后同时出现「下一层」与「撤退」两个传送点

* 踩「撤退」→ 弹出结算面板，列出本局拾取的全部战利品与总价值

* 点确认 → 回主菜单，战利品仍在背包里

* 中途死亡 → 清空全部库存 → 回主菜单

* 母巢召唤的虫群未清完时，不出现传送点

### Play Mode 实测结论（2026-09-26）

全部 11 项手测通过，实测数据：

| 验证项 | 实测结果 |
| --- | --- |
| 开局第 1 层 | `floor=1`，1 只敌人，玩家在房间中心 `(0,0,0)` |
| 相机包围盒 | `hasBounds=True bounds=(x:-5.76, y:-5.61, w:12.07, h:9.06)` |
| 清空 → 传送点 | 击杀后 0.6s 生成 `Next @ (2.50, 0)`，`floor` 保持 1（不自动跳层） |
| 踩传送点换层 | `floor=2`，玩家回到中心，新敌人生成 |
| 血量跨层继承 | 40 HP 从第 2 层带到第 3 层，仍为 40 |
| 难度随层增长 | 第 3 层 2 只敌人；血量 ×1.30（海盗 120→156、水母 30→39） |
| 撤退层双传送点 | 第 5 层 `Next @ (2.50, 0)` + `Retreat @ (-2.50, 0)` |
| 撤退结算面板 | `finished=True`，面板可见，`floorText='抵达第 5 层'`，总价值正确 |
| 确认回主菜单 | `activeScene=MainMenu` |
| 死亡清空库存 | `loot=3 → 0`，`finished=True` |
| 虫群未清完 | 存活虫群时 `IsFloorCleared()=False`，不生成传送点 |

### 已知测试盲区

* `Physics2D.OverlapCircleAll` 在 EditMode 下查不到测试中新建立的碰撞体，因此「踩传送点触发」只能在 Play Mode 验证（与 `LootInteractor` 的既有盲区一致）

* 换层时的房间实例化 / 销毁只能在 Play Mode 验证

* `EditorApplication.Step()` 单步下 `WaitForSeconds` 需要数千次 Step 才推进 0.6s，验证传送点延迟生成时需注意

### 全量 EditMode 基线

`287 passed / 3 failed`。3 个失败均为本次改动前就存在的历史问题，与爬塔功能无关：

* `RifleShooter2DTests.Fire_WithMultiShotWeapon_SpawnsSpreadProjectiles`（弹丸数污染）

* `RifleShooter2DTests.Fire_WithSingleShotWeapon_SpawnsOneProjectile`（同上）

* `RoomCollisionBoundsTests.BattleScene_RoomUsesDedicatedCollisionBoundsInsteadOfVisualWallColliders`（房间墙体边界不匹配）

## 约束

* 不新增、不修改任何 `asmdef`

* 不改动 `Inventory` 的容量、装备槽与拖拽逻辑

* 不改动 `BagPanelController` 及其预制体结构

* 不改动 `PlayerMovement2D`、`CharacterVisual2D`、`WeaponMountSlot` 的行为

* 不改动 `LootTable` / `LootDropper` / `DroppedItem2D` 的现有行为

* 不引入对象池（每层敌人数量有限，且换层时整批销毁）

* 不引入反射改私有字段（IL2CPP 下不可靠）

* 不引入任何联机依赖 / RPC / Netcode

* 所有玩家可见文本使用中文：`下一层`、`撤退`、`抵达第 {n} 层`、`总价值 {n}`

* 空引用一律静默返回，不抛异常（沿用项目既有策略）

* 摇杆捕获区必须保持 `BattleHUD` 层级里的最底层，新增面板加在它之后

