# 攻击挥砍表现设计（剑挥动）

## 目标

按下攻击键时，让 `MainHand` 锚点带动武器在固定时间内插值旋转，扫过身前一个扇形，形成一次挥砍动作。本轮只做「剑挥动」这一段表现，不含刀光、伤害判定与受击反馈。

## 范围

### 本轮包含

- 独立的攻击状态机：`Idle` / `Swinging`，挥砍期间当前这刀不能被新输入打断或重启（连发是回到 `Idle` 后重新启动，不是打断）
- `MainHand` 锚点绕 Z 轴从起始角线性插值到结束角
- 按住攻击键自动连发
- 空手（武器槽没有可见武器）时按攻击键不响应
- 可测试的纯逻辑：时间轴推进

### 本轮不包含

- 刀光贴图（美术素材尚未提供，见「后续扩展」）
- 伤害判定、命中检测、攻击范围
- 受击、硬直、死亡表现
- 键盘 / 鼠标攻击键（仅接 HUD 的 `AttackButton`）
- 攻击期间锁定朝向（用户明确选择「允许翻转」）
- 攻击与武器数据的绑定（攻速、角度暂为组件上的固定值）

## 已确认参数

| 项 | 值 | 说明 |
| --- | --- | --- |
| 扇形范围 | `+60°` → `-60°`，共 `120°` | 相对人物正前方，人物面向右时为 0°；从上往下劈 |
| 单次时长 | `0.30` 秒 | 固定值 |
| 插值曲线 | **线性** | 不做缓动 |
| 移动 | 不锁 | 挥砍期间照常移动 |
| 朝向 | 不锁 | 挥砍中途反向键会镜像翻转，接受剑的瞬间跳变 |
| 触发源 | 仅 `AttackButton` | 不做键盘 |
| 连发 | 按住自动连发 | |

## 架构

### 分层

沿用「状态产出 / 表现消费」的既有分层。攻击表现是纯本地的，不参与权威状态，因此：

- 时间轴推进用 `Time.deltaTime`，不做确定性帧同步
- 表现层不引用输入系统，只接收「是否按住」这一个布尔事实

攻击状态机挂在角色根节点上，与 `PlayerMovement2D` 并列，互不依赖。挥砍只改 `MainHand.localRotation`，不碰物理、不碰 `Visual.localScale`，因此和移动表现可以同时进行。

### 程序集依赖

现有方向为 `Gameplay → BattleHUD → Inventory`（不可逆）。`HoldButton` 放 `BattleHUD`、`AttackSwing2D` 放 `Gameplay`，方向一致，无需改动任何 `asmdef`。

这与既有先例同构：`PlayerMovement2D`（`Gameplay`）已经引用了 `VirtualJoystickController`（`BattleHUD`）。

### 组件与职责

| 类型 | 位置 | 职责 |
| --- | --- | --- |
| `SwingTimeline` | `Assets/Gameplay/Runtime/` | 纯 C#。时间 → 角度的映射，无 `MonoBehaviour` |
| `AttackSwing2D` | `Assets/Gameplay/Runtime/` | 状态机 Idle↔Swinging，每帧把角度写进 `MainHand.localRotation` |
| `HoldButton` | `Assets/UI/BattleHUD/Scripts/` | 挂在 `AttackButton`，检测按下/抬起/划出，暴露 `IsHeld` |
| `WeaponMountSlot` | 已有 | 新增只读属性 `HasWeapon` |

## 组件设计

### `SwingTimeline`（纯 C# 类）

```csharp
public sealed class SwingTimeline
{
    public SwingTimeline(float duration, float startAngle, float endAngle);

    public float Duration { get; }
    public float StartAngle { get; }
    public float EndAngle { get; }
    public float Elapsed { get; }
    public float Progress { get; }    // 0..1，已夹住
    public float Angle { get; }       // Mathf.Lerp(StartAngle, EndAngle, Progress)
    public bool IsRunning { get; }

    public void Restart();            // Elapsed = 0，IsRunning = true
    public void Tick(float deltaTime); // 推进；达到或超过 Duration 时 IsRunning = false
}
```

边界行为：

- 初始状态 `IsRunning == false`，`Progress == 0`
- `deltaTime <= 0` 时不推进
- `Elapsed` 累加后超过 `Duration` 时夹到 `Duration`，`Progress` 夹到 `1`，`IsRunning` 置 `false`；此后继续 `Tick` 不改变 `Angle`
- `Duration <= 0` 时 `Progress` 恒为 `1`，不发生除零；`Restart()` 后首次 `Tick` 即结束
- `StartAngle > EndAngle` 时 `Angle` 递减（本设计正是如此，从 +60 到 −60）
- 中途 `Restart()` 可重入，回到起点重新计时

不引入缓动函数：本轮明确用线性插值，`Progress` 直接作为 `Lerp` 的 `t`。

### `AttackSwing2D`（MonoBehaviour）

挂在 `Character_Base` 根节点。

```csharp
[SerializeField] private Transform weaponPivot;        // Visual/MainHand
[SerializeField] private WeaponMountSlot mountSlot;    // MainHand 上的组件
[SerializeField] private HoldButton attackButton;      // HUD 上的攻击键
[SerializeField] private float duration = 0.3f;
[SerializeField] private float startAngle = 60f;
[SerializeField] private float endAngle = -60f;

public bool IsSwinging { get; }
```

行为：

1. `Awake` 记录 `restRotation = weaponPivot.localRotation` 作为静止姿态
2. `Update`：
   - 未挥砍时：若 `attackButton.IsHeld` 且 `mountSlot.HasWeapon` 为真，则启动一次挥砍
   - 挥砍中：`timeline.Tick(Time.deltaTime)`，把 `restRotation * Quaternion.Euler(0, 0, timeline.Angle)` 写进 `weaponPivot.localRotation`
   - 时间轴跑完：回到 `Idle`，`weaponPivot.localRotation` 复位到 `restRotation`
3. 连发不写计时器：回到 `Idle` 的下一帧若仍按住，自然进入下一次挥砍。松开时当前这刀完整播完，不中断

`restRotation` 用作基准而不是硬编码 `Quaternion.identity`，是为了将来给锚点设默认握持角度（比如斜握 15°）时不必改复位逻辑。

`weaponPivot` / `mountSlot` / `attackButton` 任一为空时组件不动作、不抛异常，与 `WeaponMountSlot` 未接线时的处理一致。

### `HoldButton`（MonoBehaviour）

```csharp
public sealed class HoldButton : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsHeld { get; private set; }
}
```

- `OnPointerDown` → `IsHeld = true`
- `OnPointerUp` / `OnPointerExit` → `IsHeld = false`
- `OnDisable` → `IsHeld = false`，避免对象被禁用后卡在「按住」状态

`OnPointerExit` 是必要的：手指按住后滑出按钮应当视为松开，这与 `Button` 自身的取消点击行为一致。

不改用 `Button.onClick`：`onClick` 只在抬起（且未划出）时触发一次，无法表达「持续按住」。

已知限制：`Button.interactable = false` 只影响 `Button` 自身，`HoldButton` 仍会收到指针事件。当前 `AttackButton` 始终可交互，不处理该情况。

### `WeaponMountSlot` 改动

新增只读属性：

```csharp
public bool HasWeapon => weaponRenderer != null
                      && weaponRenderer.enabled
                      && weaponRenderer.sprite != null;
```

用「武器渲染器当前是否可见」作为「手上有武器」的判据，而不是去 `Inventory` 查装备槽。理由：武器的显示与挥砍的准入应当来自同一个事实来源，避免出现「面板显示已装备但手上没剑却仍在挥砍」这类不一致。

## 渲染与翻转

挥砍只旋转 `MainHand`，而 `MainHand` 是 `Visual` 的子物体。`Visual.localScale.x = ±1` 的镜像会连带翻转扇形朝向，因此：

- 向右：`+60° → -60°` 在屏幕上表现为顺时针、自上而下
- 向左：同一组局部角度因父级负缩放而表现为镜像的逆时针、同样是自上而下

**不需要为左右方向写两套角度**。代价是挥砍中途翻转朝向时剑会瞬间跳到另一侧——这是用户在澄清中明确接受的。

角度写在局部空间且不参与世界空间计算，因此相机与房间缩放不影响挥砍表现。

## 预制体与场景改动

### `Modules/AttackButton.prefab`

新增 `HoldButton` 组件。该预制体是嵌套预制体，嵌在 `BattleHUD.prefab` 内并被场景实例引用，改动会自动传播，无需在场景里单独加。

### `Character_Base.prefab`

新增 `AttackSwing2D` 组件，序列化字段：

- `weaponPivot` → 自身层级内的 `Visual/MainHand`（**预制体内部引用，写进预制体**）
- `mountSlot` → 同层级 `Visual/MainHand` 上的 `WeaponMountSlot`（**预制体内部引用，写进预制体**）
- `duration` / `startAngle` / `endAngle` → 默认值即最终值

### `BattleScene`

`AttackSwing2D.attackButton` → `HUDCanvas/BattleHUD/AttackButton` 上的 `HoldButton`。

这是**场景级 override**，与既有 `PlayerMovement2D.joystick` 的处理方式一致——预制体不能引用场景对象，`Character_Base.prefab` 无法内嵌对 HUD 的引用。

## 数据流

```
按住 AttackButton
      │
      ▼
HoldButton.IsHeld ─────────────┐
                               │
WeaponMountSlot.HasWeapon ─────┤
                               ▼
                    AttackSwing2D.Update
                               │
              Idle ──── 启动 ──┴── Swinging ── Tick(dt)
                                              │
                                              ▼
                             MainHand.localRotation = restRotation * Euler(0,0,Angle)
                                              │
                                     时间轴跑完 → 回 Idle，复位
                                              │
                                     仍按住 → 下一帧再次启动（连发）
```

## 测试策略

### EditMode（`Gameplay.EditModeTests`）

`SwingTimelineTests`：

- 初始 `IsRunning` 为 `false`、`Progress` 为 `0`
- `Restart()` 后 `Progress == 0`、`Angle == StartAngle`、`IsRunning == true`
- `Tick(时长的一半)` 后 `Progress` 约为 `0.5`、`Angle` 约为起止角中点
- `Tick` 累计超过时长后 `Progress == 1`、`Angle == EndAngle`、`IsRunning == false`
- 完成后再 `Tick` 不改变 `Angle`
- `deltaTime <= 0` 不推进
- `Duration <= 0` 时单次 `Tick` 即完成
- 中途 `Restart()` 回到起点
- `StartAngle > EndAngle` 时 `Angle` 单调递减

### Play Mode 手测

- 未装备武器时按攻击键无反应
- 装备武器后按攻击键挥砍一次，剑从斜上扫到斜下
- 按住不放持续连发，松开后当前这刀完整播完
- 挥砍期间按住方向键仍能移动
- 挥砍中途反向，剑镜像翻到另一侧
- 武器卸下后立即不再响应攻击

## 约束

- 不改动 `PlayerMovement2D` 的移动与 `FixedUpdate` 逻辑
- 不改动 `CharacterVisual2D` 的朝向与帧动画逻辑
- 不改动 `BattleHUD` 现有布局与其余交互
- 不改动 `BagPanel` / `Joystick` / `StatusPanel` 的行为
- 不引入 `Animator`、`AnimatorController` 或任何动画资产
- 不引入攻击间隔、连击段数、伤害数值等本轮用不到的字段
- 不修改 `Room_Battle_Closed_01` 预制体

## 后续扩展：刀光

刀光的插入点是 `AttackSwing2D` 每帧写 `weaponPivot.localRotation` 的那一处——同一帧按当前 `Progress` 推进刀光即可。

需要新增的东西（本轮不写）：刀光贴图资源、一个按轨迹播放并淡出的临时对象、以及随挥砍结束自动销毁与对象池。届时 `SwingTimeline` 无需改动，它已经把角度与进度暴露出来了。
