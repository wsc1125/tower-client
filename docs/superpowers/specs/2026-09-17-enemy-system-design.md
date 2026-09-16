# 敌人系统设计（完整战斗闭环）

## 目标

引入真正的敌人系统，打通「敌人可被击杀、敌人会追击并攻击、玩家会受伤与死亡」的核心战斗闭环。敌人属性用 `ScriptableObject` 数据驱动，后续可低成本扩展多类型敌人。

## 范围

### 本轮包含

- 敌人血量 / 受伤 / 死亡（归零即死亡，不显示血条）
- 敌人 AI 状态机：`Patrol`（巡逻）→ `Chase`（追击）→ `Attack`（近战攻击）
- 敌人属性数据化：`EnemyDefinition`（ScriptableObject）
- 玩家血量 + 接入现有 HUD 血条（`StatusPanelController`）
- 玩家死亡后的重置流程（重置玩家与所有敌人）
- 玩家子弹 / 近战对敌人结算伤害

### 本轮不包含

- 敌人血条显示（用户明确「不显示，归零直接死亡」）
- 远程 / 多类型敌人（架构预留，数据层已支持）
- 掉落、经验、金币、得分
- 敌人移动 / 受击 / 攻击动画帧（美术帧暂缺）
- GameOver UI（死亡提示先用 `Debug.Log`）
- 对象池 / 敌人批量生成器

## 已确认参数（占位值，可后续调整）

| 项 | 值 | 说明 |
| --- | --- | --- |
| 敌人血量 `maxHealth` | 100 | |
| 敌人移速 `moveSpeed` | 2.0 | 巡逻与追击共用 |
| 索敌半径 `detectRadius` | 6.0 | 玩家进入后开始追击 |
| 攻击距离 `attackRange` | 1.2 | 进入后停下攻击 |
| 敌人单次伤害 `attackDamage` | 10 | |
| 攻击冷却 `attackCooldown` | 1.0 | 秒 |
| 玩家血量 `maxHealth` | 100 | 与 `StatusPanelController` 默认一致 |
| 子弹伤害 | 10 | 由 `RifleShooter2D` 传入 |
| 近战伤害 | 20 | 由 `AttackSwing2D` 配置 |

## 架构

沿用既有「状态产出 / 表现消费」分层：纯逻辑类可独立测试，`MonoBehaviour` 只做接线与每帧驱动。敌人移动继续使用 `Rigidbody2D.MovePosition`（与 `PlayerMovement2D` / 旧 `EnemyPatrol2D` 一致）。

程序集依赖保持现状 `Gameplay → BattleHUD + Inventory`（不可逆）。`HealthComponent` 放在 `Gameplay`，通过可选引用接 `StatusPanelController`（`BattleHUD`），与 `PlayerMovement2D → VirtualJoystickController` 的先例同构，无需改动任何 `asmdef`。

### 组件与职责

| 类型 | 位置 | 职责 |
| --- | --- | --- |
| `EnemyDefinition` | `Assets/Gameplay/Runtime/` | ScriptableObject，敌人属性数据 |
| `Health` | `Assets/Gameplay/Runtime/` | 纯 C# 血量模型（受伤 / 回血 / 死亡事件） |
| `HealthComponent` | `Assets/Gameplay/Runtime/` | MonoBehaviour，包 `Health`，暴露伤害入口与 HUD 刷新 |
| `EnemyController` | `Assets/Gameplay/Runtime/` | 敌人 AI 状态机，读取 `EnemyDefinition`，移动与攻击 |
| `CombatDirector` | `Assets/Gameplay/Runtime/` | 玩家死亡重置编排（玩家 + 敌人） |
| `Projectile2D`（改） | `Assets/Gameplay/Runtime/` | 增加伤害字段与命中结算 |
| `AttackSwing2D`（改） | `Assets/Gameplay/Runtime/` | 增加近战扇形命中结算 |
| `RifleShooter2D`（改） | `Assets/Gameplay/Runtime/` | 自动索敌目标从 `EnemyPatrol2D` 改为 `EnemyController` |

## 组件设计

### `EnemyDefinition`（ScriptableObject）

```csharp
[CreateAssetMenu(menuName = "Gameplay/Enemy Definition", fileName = "EnemyDefinition")]
public sealed class EnemyDefinition : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float detectRadius = 6f;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackCooldown = 1f;
    [SerializeField] private Sprite worldSprite;
    [SerializeField] private Vector2 colliderSize = new Vector2(1.1f, 1.1f);
}
```

只读属性暴露各字段。`colliderSize` 用于敌人实例的 `BoxCollider2D.size`（保持与色块原型 1.1×1.1 一致的体积）。

### `Health`（纯 C#）

```csharp
public sealed class Health
{
    public Health(float maxHealth);
    public float MaxHealth { get; }
    public float CurrentHealth { get; }
    public bool IsDead { get; }
    public event Action Changed;
    public event Action Died;
    public bool TakeDamage(float amount);   // 返回是否因此死亡
    public void Heal(float amount);
    public void Reset();                    // CurrentHealth = MaxHealth
}
```

边界行为：

- `MaxHealth` 构造时夹到 `>= 1`
- `CurrentHealth` 恒在 `[0, MaxHealth]`
- `TakeDamage(amount <= 0)` 无效；扣血到 0 时触发 `Died`（只触发一次），返回 `true`
- `Died` 触发后再次 `TakeDamage` 不重复触发 `Died`
- `Heal(amount <= 0)` 无效；已死亡不回血
- `Reset()` 恢复到满血并触发 `Changed`

### `HealthComponent`（MonoBehaviour）

```csharp
public sealed class HealthComponent : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private StatusPanelController statusPanel; // 仅玩家可选
    public Health Model { get; }
    public event Action Died;
    public event Action<float, float> HealthChanged;
    public void TakeDamage(float amount);
    public void Heal(float amount);
    public void Reset();
}
```

- `Awake` 创建 `Health`，订阅 `Changed`/`Died` 并转发为 `HealthChanged`/`Died`
- `statusPanel != null` 时，每次变化调用 `statusPanel.SetHealth(current, max)`
- 玩家实例在场景中把 `statusPanel` 接到 `StatusPanelController`；敌人实例保持为空

### `EnemyController`（MonoBehaviour，取代旧 `EnemyPatrol2D` 行为）

```csharp
[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyController : MonoBehaviour
{
    [SerializeField] private EnemyDefinition definition;
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private HealthComponent health;
    [SerializeField] private SpriteRenderer renderer;
    [SerializeField] private Transform player;
    [SerializeField] private Vector2 patrolPointA;
    [SerializeField] private Vector2 patrolPointB;
    [SerializeField] private float arriveThreshold = 0.05f;
}
```

状态机与规则：

1. `Patrol`：在 `patrolPointA` 与 `patrolPointB` 间往返，复用 `GetNextPosition` / `ShouldSwapTarget`（从旧 `EnemyPatrol2D` 迁入，保持纯静态可测）。
2. 玩家进入 `detectRadius` → `Chase`：朝玩家移动。
3. 玩家进入 `attackRange` → `Attack`：停下，`attackCooldown` 冷却结束后对玩家 `HealthComponent.TakeDamage(attackDamage)`。
4. 玩家离开 `attackRange` → 回 `Chase`；离开 `detectRadius` → 回 `Patrol`。
5. `health.Model.Died` → 销毁 `gameObject`（死亡即消失，不做血条）。

细节：

- 敌人使用 kinematic `Rigidbody2D`（`BodyType.Kinematic`）+ `MovePosition`，与玩家移动一致。
- `Awake` 从 `definition` 初始化血量（`maxHealth`）、移动速度、碰撞体尺寸、贴图；`definition` 为空时组件不动作、不抛异常（沿用既有空引用静默策略）。
- `player` 为空时在运行时 `FindObjectOfType<PlayerMovement2D>()` 兜底一次。
- 索敌/攻击距离用 `Vector2.Distance` 基于玩家与敌人位置判断。
- 敌人不引入受击硬直、攻击前摇动画；攻击表现为「冷却到就结算伤害」。

旧 `EnemyPatrol2D.cs` 的 MonoBehaviour 行为被 `EnemyController` 取代，其两个静态辅助方法迁入 `EnemyController`，原类与旧单测对应更新（见「测试策略」）。

### `CombatDirector`（玩家死亡重置）

```csharp
public sealed class CombatDirector : MonoBehaviour
{
    [SerializeField] private HealthComponent playerHealth;
    [SerializeField] private Transform playerSpawn;
    [SerializeField] private PlayerMovement2D playerMovement;
    [SerializeField] private float resetDelay = 1f;
}
```

- 订阅 `playerHealth.Died`
- 死亡后等待 `resetDelay`，然后：
  - `playerHealth.Reset()`，把玩家位置复位到 `playerSpawn`（通过 `playerMovement` 所在 `Rigidbody2D` 或直接 `transform.position`）
  - `FindObjectsOfType<EnemyController>(true)`：对每个敌人 `health.Reset()`、位置复位到 `patrolPointA`、状态回 `Patrol`
- 死亡提示先 `Debug.Log`，不新增 UI

### `Projectile2D`（改）

新增：

```csharp
[SerializeField] private float damage = 10f;
public void Launch(Vector2 dir, float launchSpeed, float projectileDamage);
```

- 保留原 `Launch(dir, speed)` 作为重载或默认伤害入口
- `OnCollisionEnter2D`：先取 `collision.collider.GetComponentInParent<HealthComponent>()`，命中则 `TakeDamage(damage)`；随后照旧生成火花并销毁（无论是否命中敌人）

### `AttackSwing2D`（改）

新增：

```csharp
[SerializeField] private float meleeDamage = 20f;
[SerializeField] private float meleeRange = 1.4f;
[SerializeField] private float meleeArcAngle = 120f; // 与挥砍扇形一致
```

命中结算：

- 每刀维护一个「本刀已命中敌人集合」，挥砍开始时清空
- 挥砍过程中每帧用 `Physics2D.OverlapCircleAll(weaponPivot.position, meleeRange)` 找敌人，按「与人物朝向的夹角在 `meleeArcAngle/2` 内」过滤，对每个 `HealthComponent` 各结算一次 `TakeDamage(meleeDamage)`
- 不引入额外武器触发器碰撞体（用 `OverlapCircle` + 角度过滤，简单可靠）

### `RifleShooter2D`（改）

- `FindNearestEnemy` 由 `FindObjectsOfType<EnemyPatrol2D>()` 改为 `FindObjectsOfType<EnemyController>()`
- `Fire` 传给 `Projectile2D.Launch` 时带上伤害（子弹伤害作为组件字段，默认 10）

## 数据流

```
敌人 AI:
  Patrol ──玩家进入 detectRadius──▶ Chase ──进入 attackRange──▶ Attack
  Attack ──玩家离开 attackRange──▶ Chase ──离开 detectRadius──▶ Patrol
  Attack: cooldown 到 → 玩家 HealthComponent.TakeDamage(attackDamage)

玩家受伤:
  EnemyController.TakeDamage → 玩家 HealthComponent → StatusPanelController.SetHealth

玩家死亡:
  玩家 Health.Died → CombatDirector 延迟 resetDelay → 重置玩家血量/位置 + 重置所有敌人

敌人受伤:
  子弹 Projectile2D / 近战 AttackSwing2D → 敌人 HealthComponent.TakeDamage
  → 敌人 Health.Died → EnemyController 销毁 gameObject
```

## 预制体与场景改动

### 新增 `Enemy_Base.prefab`（`Assets/Gameplay/Prefabs/`）

结构（对齐 `Character_Base` 的「根 + Visual」思路，敌人更简单）：

- 根 `Enemy_Base`：`Rigidbody2D`（kinematic）、`BoxCollider2D`、`HealthComponent`、`EnemyController`
- 子 `Visual`：`SpriteRenderer`（`EnemyDefinition.worldSprite`）

敌人贴图尚未提供前，`Visual` 暂用占位渲染；用户生成贴图后引用到 `EnemyDefinition.worldSprite` 即可（透明背景 PNG，俯视视角，主体居中）。

### 新增 `Assets/Gameplay/Data/Enemy_Slime.asset`

`EnemyDefinition` 实例，字段按「已确认参数」填写。

### `Character_Base.prefab`

- 新增 `HealthComponent`（`maxHealth = 100`）
- 可选新增 `CombatDirector` 引用接线（见场景）

### `BattleScene`

- 删除旧的 `Enemy_Block`（`EnemyPatrol2D` + `DebugBlockVisual`），替换为 `Enemy_Base` 实例，配置 `definition`、`patrolPointA/B`、`player` 引用
- 玩家 `HealthComponent.statusPanel` → `HUDCanvas/BattleHUD/StatusPanel` 的 `StatusPanelController`
- `CombatDirector` 接线 `playerHealth` / `playerSpawn` / `playerMovement`
- `RifleShooter2D` / `AttackSwing2D` 相关字段沿用既有场景级接线

## 测试策略

### EditMode（`Gameplay.EditModeTests`）

- `HealthTests`：构造 / `TakeDamage` 扣血与夹取 / 死亡触发一次 `Died` / 已死亡不再触发 / `Heal` 与负值 / `Reset`
- `EnemyControllerTests`：迁移并保留旧 `EnemyPatrol2DTests` 的 `GetNextPosition` / `ShouldSwapTarget`；新增状态迁移纯逻辑（如「玩家进入半径 → Chase」「进入攻击距离 → Attack」「离开 → 回退」）——通过把距离判断抽成可测纯函数
- `EnemyDefinitionTests`（可选）：默认值可读、字段非负
- 近战扇形过滤纯函数（角度 / 范围判断）

### Play Mode 手测

- 玩家进入敌人索敌半径后敌人追击，靠近后停下并周期造成伤害
- 敌人攻击使玩家血量下降，HUD 血条同步刷新
- 子弹命中敌人掉血，击杀后敌人消失
- 近战挥砍命中敌人掉血（每刀每敌人一次）
- 玩家血量归零后约 1 秒，玩家与敌人状态复位，可继续测试
- 玩家远离后敌人回到巡逻

## 约束

- 不改动 `PlayerMovement2D` 的移动与输入逻辑
- 不改动 `CharacterVisual2D` 的朝向 / 帧动画逻辑
- 不改动 `BattleHUD` 布局与其他交互（仅新增血条数据接线）
- 不引入 `Animator` / `AnimatorController` / 动画资产
- 不引入对象池、掉落、经验、得分等本轮用不到的机制
- 不引入额外武器触发器碰撞体（近战用 `OverlapCircle` + 角度过滤）
- 保留 `EnemyPatrol2D` 两个纯静态辅助方法及其单测语义（迁移到 `EnemyController`）
- 不改动任何 `asmdef`
