# 人物系统设计（角色表现层 + 主手武器锚点）

## 目标

用真实角色美术替换现有白色方块外观，建立一套可复用、联机友好的角色表现层：

- 角色改用真实像素美术，播放 6 帧行走循环
- 左右方向通过**镜像翻转**实现
- 角色身上提供 `MainHand` 锚点，用于挂载武器外观
- 武器外观由装备槽自动驱动：背包面板装上武器 → 手上出现武器；卸下 → 移除
- 结构可被后续敌人与其他角色复用

## 范围

### 本轮包含

- `Idle`（静止，显示首帧）与 `Move`（6 帧循环）两个由速度驱动的表现状态
- 左右朝向镜像翻转
- `MainHand` 单个武器锚点
- 装备槽 → 世界内武器外观的自动挂载/卸下
- `Inventory` 归属修正（解除「UI 拥有唯一数据源」的耦合）
- 场景 `SampleScene` 重命名为 `BattleScene`
- 角色预制体 `Character_Base`
- 可测试的纯逻辑：朝向解析、帧推进

### 本轮不包含

- 攻击、受击、死亡表现（攻击行为后续由武器决定）
- 副手、背部、头顶、躯干等其它锚点
- 头盔、护甲、靴子等非武器装备槽
- 四方向朝向（上/下）
- Unity `Animator` / `AnimatorController` 状态机
- 敌人接入（保留同一套结构，敌人素材到位后再接）

## 前置依赖：美术素材

素材已就位，来源与落位如下：

| 素材 | 来源 | 落位路径 |
| --- | --- | --- |
| 角色行走序列帧（6 张） | `C:\Users\16648\Desktop\素材\人物1\` | `Assets/Gameplay/Art/Characters/Player/Player_Walk_Right_0~5.png` |
| 武器世界外观 | `C:\Users\16648\Desktop\素材\武器1.png` | `Assets/Gameplay/Art/Weapons/Weapon_Sword.png` |

### 角色序列帧：实测结果

| 文件 | 画布 |
| --- | --- |
| Player_Walk_Right_0.png | 280×344 |
| Player_Walk_Right_1.png | 281×319 |
| Player_Walk_Right_2.png | 278×346 |
| Player_Walk_Right_3.png | 275×337 |
| Player_Walk_Right_4.png | 280×341 |
| Player_Walk_Right_5.png | 279×340 |

**画风**：粗颗粒像素风（黑描边，一格约 8 屏幕像素）；角色为**太空探险战士**——白 / 浅灰宇航装甲为主体、青蓝能量发光、橙色作点缀，外沿一圈黑色粗描边，面朝右；均为 RGBA、背景透明。

**尺寸不统一，但统一以脚底中心对齐**：逐帧 pivot 取脚底中心 `(0.5, 0.032)`，六帧脚底落在同一锚点上，无需重新拼图或统一画布。

### 序列帧质量判定

以「内容外接矩形 + 底部中心对齐」为基准做轮廓（alpha 遮罩）异或对比：差异集中在腿部区域，即符合行走循环的特征。已用「顶部对齐 / 底部对齐」两版对照图目视确认：底部对齐时脚底稳定贴地、头部有自然起伏，为正确选择。

### 角色序列帧：Import 参数

| 参数 | 值 | 说明 |
| --- | --- | --- |
| `filterMode` | **Point (0)** | 像素风素材必须点采样，用工程现有的 Bilinear 会把像素糊掉 |
| `spriteMeshType` | **FullRect (0)** | 角色用整矩形网格 |
| `spritePixelsToUnits` | `320` | 角色本体站立高约 1.0 世界单位 |
| `spritePivot` | 统一 `(0.5, 0.032)` | 脚底中心，六帧一致 |

排列顺序以文件名升序为准（`Player_Walk_Right_0` → `_5`），对应 `moveFrames[0..5]`。

### 武器世界外观：实测结果与 Import 参数

`C:\Users\16648\Desktop\素材\武器1.png`：

- 1774×887、RGBA，四角 alpha = 0，**背景透明**（用户已手工处理）
- 内容外接矩形 1648×438，水平约占画布 3%~96%
- 画风与角色一致：像素风、黑描边、金护手、暗红握柄、灰色剑身
- **水平横放、剑尖朝右**

列高轮廓（占内容宽度百分比 → 该列像素高度，内容高 438）：

| 位置 | 高度 | 部位 |
| --- | --- | --- |
| 0%~11% | 134 → 261 → 199 | 柄头（金色菱形） |
| 12%~23% | 141 | 握柄（暗红缠绕） |
| 24%~38% | 248 → 438 | 护手（金色十字） |
| 39%~88% | 217 | 剑身 |
| 89%~100% | 146 → 82 → 20 | 剑尖 |

由轮廓推得握把中心在内容坐标约 `(288, 219)`，换算到整图即 `(341, 473)`。

| 参数 | 值 | 说明 |
| --- | --- | --- |
| `filterMode` | Point (0) | 与角色一致 |
| `spritePixelsToUnits` | `1735` | 使剑身长约 0.95 世界单位（1648 / 1735），与 1 单位高的角色相称 |
| `spritePivot` | `(0.192, 0.467)` | 握把中心 `(341, 473)` 归一化后的结果 |

pivot 设在握把是关键约定：锚点位置即握持点，**无需为每把武器引入偏移/旋转字段**。因为剑已经是水平朝向，锚点也不需要旋转补偿。

## 架构

### 分层原则

角色系统拆成「状态产出」与「表现消费」两层，为联机留出接缝：

- **状态产出**：本地输入（键盘 / 摇杆）或未来的网络同步，产生一个速度向量
- **表现消费**：`CharacterVisual2D` 只认速度向量，不引用 `Input`、不引用 `VirtualJoystickController`

联机时唯一需要替换的是「谁来调用 `SetMotion`」，表现层代码不变。动画使用 `Time.deltaTime` 推进既可 —— 动画是纯本地表现，位置才是需要权威同步的数据，不需要确定性帧同步。

### Inventory 归属修正

当前 `BagPanelController.EnsureModel()` 内部 `Model = new Inventory()`，唯一数据源被关在 UI 面板里，角色表现层无法拿到同一实例。修正方式：

- 在 `Inventory` 程序集（最底层，无依赖）新增 `InventoryHolder` MonoBehaviour，持有唯一 `Inventory` 实例
- `BagPanelController` 增加 `InventoryHolder` 序列化引用，优先用 holder 的实例；未接线时回退到自建实例
- `WeaponMountSlot` 订阅同一个 holder 的 `Inventory.Changed`

放在 `Inventory` 程序集而非 `Gameplay` 是为了避免程序集环：当前依赖方向为 `Gameplay → BattleHUD → Inventory`，若把持有者放进 `Gameplay`，`BattleHUD` 将无法反向引用。

保留回退路径意味着现有 `BagPanelController` 相关测试（不接线 holder）继续通过。

### 程序集变更

- `Assets/Gameplay/Gameplay.asmdef`：`references` 增加 `Inventory`（`WeaponMountSlot` 需要 `ItemDefinition` / `EquipmentSlotType`）
- `Assets/Tests/EditMode/Gameplay/Gameplay.EditModeTests.asmdef`：不变（本轮测试只覆盖 `Gameplay` 内的纯逻辑）

命名沿用工程现状：无 namespace、脚本放 `Assets/Gameplay/Runtime/`。

## 场景与预制体结构

### 场景重命名

`Assets/Scenes/SampleScene.unity` → `Assets/Scenes/BattleScene.unity`，同时重命名对应的 `.meta`（保持 GUID 不变，引用它的对象无需改动）。需要同步更新的路径引用：

| 文件 | 修改内容 |
| --- | --- |
| `ProjectSettings/EditorBuildSettings.asset` | `path: Assets/Scenes/SampleScene.unity` → `BattleScene.unity` |
| `ProjectSettings/ProjectSettings.asset` | `templateDefaultScene` 路径 |
| `Assets/Tests/EditMode/Gameplay/RoomCollisionBoundsTests.cs` | `ScenePath` 常量，以及测试方法名 `SampleScene_RoomUses...` |

场景内根对象名 `SampleScene` 无特殊含义，不强制改名。

### `Character_Base` 预制体

```
Character_Base                          ← 物理与逻辑根，localScale 恒为 (1,1,1)
  ├─ Rigidbody2D                        (Dynamic, Gravity Scale 0, Freeze Rotation Z)
  ├─ BoxCollider2D                      (尺寸按角色脚部占位重调)
  ├─ PlayerMovement2D                   (引用 Visual 上的 CharacterVisual2D)
  └─ Visual                             ← 表现层，翻转只作用于这一层
       ├─ SpriteRenderer                (sortingOrder 20)
       ├─ CharacterVisual2D
       └─ MainHand                      ← 空物体锚点 + AnchorGizmo + WeaponMountSlot
            └─ Weapon                   ← SpriteRenderer (sortingOrder 21)，由 WeaponMountSlot 控制
```

要点：

- **`DebugBlockVisual` 必须从角色上移除**。它会强制改写同节点的 `SpriteRenderer.sprite` 与颜色，与序列帧动画冲突。
- `SpriteRenderer` 从根节点移到 `Visual` 子节点。
- 翻转作用于 `Visual.localScale.x`，物理根节点 `scale` 恒为 `(1,1,1)`，`Rigidbody2D` / `BoxCollider2D` 完全不受负缩放影响。
- 后续接敌人时，复制同一结构，只替换帧图数组、`moveSpeed` 与「谁喂 `SetMotion`」。

## 组件设计

### `SpriteFrameAnimation`（纯 C# 类）

不继承 `MonoBehaviour`，持有播放状态，可独立测试。

```csharp
public sealed class SpriteFrameAnimation
{
    public SpriteFrameAnimation(Sprite[] frames, float framesPerSecond);

    public int FrameIndex { get; }
    public Sprite CurrentFrame { get; }

    public void Tick(float deltaTime);  // 累加 dt，按 fps 推进并循环
    public void Reset();                // 回到第 0 帧并清零计时
}
```

边界行为：`frames` 为空或 `null` 时 `CurrentFrame` 返回 `null` 且 `Tick` 不推进；`framesPerSecond <= 0` 时同样不推进；单帧数组恒定返回该帧。

### 朝向解析

`FacingDirection` 枚举声明在 `CharacterVisual2D.cs` 内（值直接取 ±1，便于换算为 `localScale.x`），朝向解析做成静态纯函数，沿用 `PlayerMovement2D.ResolveMovementInput` 的工程习惯：

```csharp
public enum FacingDirection { Left = -1, Right = 1 }

public static bool TryResolveFacing(Vector2 velocity, out FacingDirection facing);
```

规则：`|velocity.x|` 超过死区（`0.01f`）时按符号解析朝向并返回 `true`；否则返回 `false`，调用方**保持当前朝向不变**。死区的意义是纯纵向移动不会导致角色反复翻面。

`FacingDirection` 的枚举值直接取 ±1，便于换算为 `localScale.x`。

### `CharacterVisual2D`（MonoBehaviour，挂在 `Visual`）

表现层唯一入口，带 `[RequireComponent(typeof(SpriteRenderer))]`，没有 `Update`，完全由外部驱动：

```csharp
public void SetMotion(Vector2 velocity);  // 由状态源每帧喂入
```

内部行为：

1. `TryResolveFacing` 成功且朝向变化时，设置 `transform.localScale.x = (float)facing`
2. 速度为 0（`sqrMagnitude` 小于死区平方）时，`Reset()` 到静止并显示首帧
3. 速度非 0 时，`animation.Tick(Time.deltaTime)` 并写入 `spriteRenderer.sprite`

静止态直接复用 `moveFrames[0]` 作为站立姿势，本轮不引入独立的 `idleSprite` 字段。

序列化字段：`SpriteRenderer spriteRenderer`、`Sprite[] moveFrames`、`float framesPerSecond`（默认 10）。

### `WeaponMountSlot`（MonoBehaviour，挂在 `MainHand`）

把装备槽状态映射为锚点下的武器外观：

```csharp
[SerializeField] private SpriteRenderer weaponRenderer;    // Weapon 子节点
[SerializeField] private InventoryHolder holder;
[SerializeField] private EquipmentSlotType mountedSlot = EquipmentSlotType.MeleeWeapon;
```

行为：

- `OnEnable` 订阅 `holder.Model.Changed`，`OnDisable` / `OnDestroy` 取消订阅
- 刷新逻辑：取 `GetEquippedItem(mountedSlot)`，用其 `WorldSprite` 赋值给 `weaponRenderer.sprite`，并 `weaponRenderer.enabled = sprite != null`
- `holder` 未接线时保持隐藏，不抛异常

用序列化字段而非硬编码 `MeleeWeapon`，是为了将来加副手锚点时直接改字段值即可复用，成本为零。

### `AnchorGizmo`（MonoBehaviour，挂在锚点）

`OnDrawGizmos` 在场景视图画出锚点的十字与圆圈，用于对齐刀的握持位置。空物体在场景视图里只有一个点击图标，无法精确对齐，这个小工具是必要的。

### `PlayerMovement2D` 改动

- 新增序列化引用 `CharacterVisual2D visual`
- 在 `Update` 中解析完输入后，调用 `visual.SetMotion(resolvedInput * moveSpeed)`

`FixedUpdate` 中的 `MovePosition` 保持不变。传给表现层的速度用「输入 × 速度」而非 `Rigidbody2D.velocity`，因为 `MovePosition` 不产生 `velocity` 值。

### `ItemDefinition` 改动

新增一个字段与只读属性：

```csharp
[SerializeField] private Sprite worldSprite;
public Sprite WorldSprite => worldSprite;
```

本轮**不**新增逐武器的偏移/旋转字段（见「前置依赖」中 pivot 约定）。

### `InventoryHolder`（MonoBehaviour，`Assets/Inventory/Runtime/`）

```csharp
[SerializeField] private Inventory model = new Inventory();
public Inventory Model => model;
```

约 20 行。挂在场景中一个独立的中立对象上（如 `PlayerContext`），而不是挂在 `Character_Base` 上 —— 因为 `Character_Base` 同时是敌人的基预制体，敌人不该带库存。后续接联机时，把它移到每名玩家的实体上即可。

### `BagPanelController` 改动

- 新增 `[SerializeField] private InventoryHolder holder;`
- `EnsureModel()` 改为：`holder != null` 时用 `holder.Model`，否则退回 `new Inventory()`
- 其余逻辑与现有事件订阅不变

## 翻转方案与取舍

| 方案 | 结果 |
| --- | --- |
| `SpriteRenderer.flipX` | 只翻精灵，**锚点位置不动**，左向时刀仍停在身体右侧 —— 错误 |
| `Visual.localScale.x = ±1` | 精灵、`MainHand` 锚点、武器一起镜像，刀自动换到另一侧且朝向正确 —— **采用** |

采用第二种。因为需求本身就是「左向用镜面设计」，整体镜像与美术前提一致。代价是 `Visual` 下的所有子物体都参与镜像，这也正是我们想要的语义。

## 渲染顺序

现有房间层级：`Floor` 系列 `sortingOrder = 0`，墙体与墙角 `sortingOrder = 10`。

预设值：角色 `20`，武器 `21`。角色与武器拉开一级是为了避免同 `sortingOrder` 下的绘制顺序不确定（精灵在相同排序值下按与相机的距离排序，不稳定）。最终数值可以在实机里按画面直觉微调。

## 数据流

```
本地输入(键盘/摇杆) ─┐
                     ├─→ PlayerMovement2D ─→ Rigidbody2D.MovePosition   (物理，未来由网络权威驱动)
网络同步(未来) ──────┘         │
                               └─→ CharacterVisual2D.SetMotion(Vector2)   (纯表现，本地)

InventoryHolder.Inventory ──Changed──┐
                                      ├─→ WeaponMountSlot.Refresh() → Weapon.sprite
背包面板拖拽装备 ─────────────────────┘
```

## 测试策略

EditMode 测试覆盖两个纯逻辑单元（沿用现有 `Gameplay.EditModeTests` 程序集）：

`SpriteFrameAnimationTests`

- 未累积到一帧时长时帧号不变
- 累积超过一帧时长后推进一帧
- 播放到末尾后回绕到第 0 帧
- `Reset()` 后回到第 0 帧
- 空数组 / `framesPerSecond = 0` 时不推进且 `CurrentFrame` 为 `null`

`CharacterVisual2DTests`

- `velocity.x` 为正时解析为 `Right`，为负时解析为 `Left`
- `|velocity.x|` 在死区内时返回 `false`（保持朝向）
- 纯纵向速度返回 `false`（不翻面）

需要更新的既有测试：

- `RoomCollisionBoundsTests`：场景路径常量与方法名随场景重命名同步修改

场景验证覆盖（Play Mode）：

- 角色像素精灵正常显示，不再出现白色方块，且像素清晰不糊（Point 采样生效）
- 向右移动时朝向为右，6 帧行走循环正常播放且脚底不上下跳动
- 向左移动时整体镜像，武器跟着换到另一侧
- 松手后回到静止姿势
- 背包面板装上武器 → 手上出现武器；卸下 → 武器消失
- 角色与墙体碰撞行为与改造前一致

## 约束

- 不破坏 `BattleHUD` 现有布局与交互
- 不改动 `Assets/UI/BattleHUD/Sprites/武器图标.png`
- `Assets/Inventory/Items/TrainingSword.asset` 只新增 `worldSprite` 字段值，不改动原有字段
- 不改动 `Room_Battle_Closed_01` 预制体结构
- `Enemy_Block` 本轮保持现状（继续使用 `DebugBlockVisual`），敌人素材到位后按 `Character_Base` 结构接入
- 不引入 `Animator`、`AnimatorController` 等资产
- 不为单把武器引入偏移/旋转配置，靠锚点位置与精灵 pivot 解决
