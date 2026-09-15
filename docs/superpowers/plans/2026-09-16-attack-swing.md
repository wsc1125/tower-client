# 攻击挥砍表现实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按下攻击键时让 `MainHand` 锚点带动武器在 0.30 秒内从 +60° 线性扫到 −60°，按住自动连发，空手不响应。

**Architecture:** 把「时间 → 角度」抽成纯 C# 的 `SwingTimeline`（可测），`AttackSwing2D` 只负责状态机与写 `MainHand.localRotation`。攻击准入条件（按住 + 有武器）来自 `HoldButton.IsHeld` 与 `WeaponMountSlot.HasWeapon`，不直接查 `Inventory`。左右翻转由 `Visual.localScale.x = ±1` 的父级镜像自动完成，不为左右写两套角度。

**Tech Stack:** Unity 2022.3.62f3c1、C#、NUnit EditMode 测试、Assembly Definition、UGUI EventSystems（沿用现有）

设计依据：[2026-09-16-attack-swing-design.md](../specs/2026-09-16-attack-swing-design.md)

## Global Constraints

- 挥砍参数固定为：时长 `0.30` 秒、起始角 `+60°`、结束角 `−60°`、**线性**插值
- 挥砍期间只锁攻击，不锁移动、不锁朝向
- 空手（`WeaponMountSlot.HasWeapon` 为 false）时按攻击键不响应
- 只接 HUD 的 `AttackButton`，不做键盘 / 鼠标触发
- 不改动 `PlayerMovement2D`、`CharacterVisual2D` 的现有逻辑
- 不改动 `BagPanel` / `Joystick` / `StatusPanel` 的行为与 `BattleHUD` 布局
- 不引入 `Animator`、`AnimatorController` 或任何动画资产
- 不新增攻击间隔、连击段数、伤害数值字段
- 不修改 `Room_Battle_Closed_01` 预制体
- 命名沿用工程现状：无 namespace，脚本放各模块 `Runtime/` 或 `Scripts/` 下
- **提交信息用中文**。PowerShell 直接传中文 `-m` 会乱码：先把信息写入 UTF-8 文件，再 `git commit -F <文件>`，并在命令前设 `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)`

## 验证手段（本工程无 CLI 测试入口）

- 编译检查：`unity_get_compilation_errors`
- 跑测试：`unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "<测试类名>" })` 拿到 `jobId`，再用 `unity_advanced_tool(tool: "unity_testing_get_job", params: { jobId: <id>, waitTimeout: 60 })` 轮询
- 预制体 / 场景改动：`unity_execute_code`（`PrefabUtility.LoadPrefabContents` → 改 → `SaveAsPrefabAsset`）
- 实机确认：`unity_play_mode` + `RenderTexture` 截图（编辑器失焦时 Game View 截图不落盘，改用 `camera.Render()` + `ReadPixels`）

测试程序集归属：`Assets/Tests/EditMode/Gameplay/` 属于 `Gameplay.EditModeTests`（引用 `Gameplay` + `BattleHUD`）；`Assets/Tests/EditMode/` 根目录属于 `BattleHUD.EditModeTests`（引用 `BattleHUD` + `Inventory`）。

---

### Task 1: SwingTimeline 纯逻辑

**Files:**
- Create: `Assets/Tests/EditMode/Gameplay/SwingTimelineTests.cs`
- Create: `Assets/Gameplay/Runtime/SwingTimeline.cs`

**Interfaces:**
- Consumes: 无
- Produces: `SwingTimeline(float duration, float startAngle, float endAngle)`、`float Duration`、`float StartAngle`、`float EndAngle`、`float Elapsed`、`float Progress`、`float Angle`、`bool IsRunning`、`void Restart()`、`void Tick(float deltaTime)` —— Task 4 消费

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/Gameplay/SwingTimelineTests.cs`：

```csharp
using NUnit.Framework;

public sealed class SwingTimelineTests
{
    private const float Duration = 0.3f;
    private const float StartAngle = 60f;
    private const float EndAngle = -60f;

    private static SwingTimeline NewTimeline()
    {
        return new SwingTimeline(Duration, StartAngle, EndAngle);
    }

    [Test]
    public void InitialState_IsIdleAtStartAngle()
    {
        var timeline = NewTimeline();

        Assert.That(timeline.IsRunning, Is.False);
        Assert.That(timeline.Progress, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(StartAngle).Within(1e-4f));
    }

    [Test]
    public void Restart_ResetsToStartAndRuns()
    {
        var timeline = NewTimeline();

        timeline.Restart();

        Assert.That(timeline.IsRunning, Is.True);
        Assert.That(timeline.Progress, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(StartAngle).Within(1e-4f));
    }

    [Test]
    public void Tick_HalfDuration_ReachesMidAngle()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        timeline.Tick(Duration * 0.5f);

        Assert.That(timeline.Progress, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.IsRunning, Is.True);
    }

    [Test]
    public void Tick_BeyondDuration_FinishesAtEndAngle()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        timeline.Tick(Duration * 2f);

        Assert.That(timeline.IsRunning, Is.False);
        Assert.That(timeline.Progress, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));
        Assert.That(timeline.Elapsed, Is.EqualTo(Duration).Within(1e-4f));
    }

    [Test]
    public void Tick_AfterFinished_DoesNotChangeAngle()
    {
        var timeline = NewTimeline();
        timeline.Restart();
        timeline.Tick(Duration * 2f);

        timeline.Tick(Duration);

        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));
        Assert.That(timeline.Elapsed, Is.EqualTo(Duration).Within(1e-4f));
    }

    [Test]
    public void Tick_NonPositiveDeltaTime_DoesNotAdvance()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        timeline.Tick(0f);
        timeline.Tick(-1f);

        Assert.That(timeline.Elapsed, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.IsRunning, Is.True);
    }

    [Test]
    public void Tick_WithoutRestart_DoesNotAdvance()
    {
        var timeline = NewTimeline();

        timeline.Tick(Duration);

        Assert.That(timeline.Elapsed, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.IsRunning, Is.False);
    }

    [Test]
    public void ZeroDuration_IsCompleteImmediately()
    {
        var timeline = new SwingTimeline(0f, StartAngle, EndAngle);

        Assert.That(timeline.Progress, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));

        timeline.Restart();
        timeline.Tick(0.016f);

        Assert.That(timeline.IsRunning, Is.False);
        Assert.That(timeline.Angle, Is.EqualTo(EndAngle).Within(1e-4f));
    }

    [Test]
    public void Restart_Midway_ResetsProgress()
    {
        var timeline = NewTimeline();
        timeline.Restart();
        timeline.Tick(Duration * 0.75f);

        timeline.Restart();

        Assert.That(timeline.IsRunning, Is.True);
        Assert.That(timeline.Progress, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(timeline.Angle, Is.EqualTo(StartAngle).Within(1e-4f));
    }

    [Test]
    public void Angle_DecreasesMonotonically_WhenStartAboveEnd()
    {
        var timeline = NewTimeline();
        timeline.Restart();

        var previous = timeline.Angle;
        for (var i = 0; i < 6; i++)
        {
            timeline.Tick(Duration / 6f);

            Assert.That(timeline.Angle, Is.LessThan(previous));
            previous = timeline.Angle;
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "SwingTimelineTests" })`
Expected: 编译失败或全部 FAIL —— `SwingTimeline` 类型不存在

- [ ] **Step 3: 写最小实现**

创建 `Assets/Gameplay/Runtime/SwingTimeline.cs`：

```csharp
using UnityEngine;

public sealed class SwingTimeline
{
    public SwingTimeline(float duration, float startAngle, float endAngle)
    {
        Duration = Mathf.Max(duration, 0f);
        StartAngle = startAngle;
        EndAngle = endAngle;
    }

    public float Duration { get; }
    public float StartAngle { get; }
    public float EndAngle { get; }
    public float Elapsed { get; private set; }
    public bool IsRunning { get; private set; }

    public float Progress => Duration <= 0f ? 1f : Mathf.Clamp01(Elapsed / Duration);

    public float Angle => Mathf.Lerp(StartAngle, EndAngle, Progress);

    public void Restart()
    {
        Elapsed = 0f;
        IsRunning = true;
    }

    public void Tick(float deltaTime)
    {
        if (!IsRunning || deltaTime <= 0f)
        {
            return;
        }

        Elapsed += deltaTime;

        if (Elapsed >= Duration)
        {
            Elapsed = Duration;
            IsRunning = false;
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "SwingTimelineTests" })`
Expected: PASS，10 个测试全绿

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/SwingTimeline.cs Assets/Gameplay/Runtime/SwingTimeline.cs.meta \
        Assets/Tests/EditMode/Gameplay/SwingTimelineTests.cs Assets/Tests/EditMode/Gameplay/SwingTimelineTests.cs.meta
git commit -F Temp/commit-msg.txt
```

提交信息（写入 `Temp/commit-msg.txt`，UTF-8）：

```
feat: 新增挥砍时间轴 SwingTimeline

把「时间 → 角度」抽成纯 C# 映射，供 AttackSwing2D 消费。线性插值，
Tick 到时长后夹住并置 IsRunning，支持中途 Restart 重入。
```

---

### Task 2: WeaponMountSlot 暴露 HasWeapon

**Files:**
- Create: `Assets/Tests/EditMode/Gameplay/WeaponMountSlotTests.cs`
- Modify: `Assets/Gameplay/Runtime/WeaponMountSlot.cs`

**Interfaces:**
- Consumes: 无
- Produces: `bool WeaponMountSlot.HasWeapon` —— Task 4 消费

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/Gameplay/WeaponMountSlotTests.cs`：

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class WeaponMountSlotTests
{
    private GameObject slotRoot;
    private GameObject weaponRoot;
    private WeaponMountSlot slot;
    private SpriteRenderer weaponRenderer;
    private Texture2D texture;
    private Sprite sprite;

    [SetUp]
    public void SetUp()
    {
        texture = new Texture2D(4, 4);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

        slotRoot = new GameObject("Slot");
        slot = slotRoot.AddComponent<WeaponMountSlot>();

        weaponRoot = new GameObject("Weapon");
        weaponRenderer = weaponRoot.AddComponent<SpriteRenderer>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(slotRoot);
        Object.DestroyImmediate(weaponRoot);
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);
    }

    [Test]
    public void HasWeapon_IsFalse_WhenRendererNotWired()
    {
        Assert.That(slot.HasWeapon, Is.False);
    }

    [Test]
    public void HasWeapon_IsFalse_WhenSpriteMissing()
    {
        WireRenderer();

        Assert.That(slot.HasWeapon, Is.False);
    }

    [Test]
    public void HasWeapon_IsFalse_WhenRendererDisabled()
    {
        WireRenderer();
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = false;

        Assert.That(slot.HasWeapon, Is.False);
    }

    [Test]
    public void HasWeapon_IsTrue_WhenSpriteVisible()
    {
        WireRenderer();
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = true;

        Assert.That(slot.HasWeapon, Is.True);
    }

    private void WireRenderer()
    {
        var serialized = new SerializedObject(slot);
        serialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "WeaponMountSlotTests" })`
Expected: 编译失败 —— `WeaponMountSlot` 没有 `HasWeapon` 成员

- [ ] **Step 3: 写最小实现**

在 `Assets/Gameplay/Runtime/WeaponMountSlot.cs` 的 `weaponRenderer` 字段下方加入属性：

```csharp
    public bool HasWeapon => weaponRenderer != null
                          && weaponRenderer.enabled
                          && weaponRenderer.sprite != null;
```

放在类内 `private Inventory subscribed;` 之前即可。其余代码不动。

- [ ] **Step 4: 跑测试确认通过**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "WeaponMountSlotTests" })`
Expected: PASS，4 个测试全绿

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/WeaponMountSlot.cs \
        Assets/Tests/EditMode/Gameplay/WeaponMountSlotTests.cs Assets/Tests/EditMode/Gameplay/WeaponMountSlotTests.cs.meta
git commit -F Temp/commit-msg.txt
```

提交信息：

```
feat: WeaponMountSlot 暴露 HasWeapon

以「武器渲染器当前是否可见」作为手上有无武器的判据，供挥砍准入使用。
这样武器的显示与挥砍的准入来自同一个事实来源，不会出现面板显示已装备
但手上没剑却仍在挥砍的情况。
```

---

### Task 3: HoldButton 按住检测

**Files:**
- Create: `Assets/Tests/EditMode/HoldButtonTests.cs`
- Create: `Assets/UI/BattleHUD/Scripts/HoldButton.cs`

**Interfaces:**
- Consumes: 无
- Produces: `bool HoldButton.IsHeld`、`void HoldButton.OnPointerDown(PointerEventData)`、`void HoldButton.OnPointerUp(PointerEventData)`、`void HoldButton.OnPointerExit(PointerEventData)` —— Task 4 消费 `IsHeld`；Task 5 把它挂到按钮上

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/HoldButtonTests.cs`：

```csharp
using NUnit.Framework;
using UnityEngine;

public sealed class HoldButtonTests
{
    private GameObject root;
    private HoldButton button;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("HoldButton");
        button = root.AddComponent<HoldButton>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
    }

    [Test]
    public void Initially_IsNotHeld()
    {
        Assert.That(button.IsHeld, Is.False);
    }

    [Test]
    public void OnPointerDown_MarksHeld()
    {
        button.OnPointerDown(null);

        Assert.That(button.IsHeld, Is.True);
    }

    [Test]
    public void OnPointerUp_ClearsHeld()
    {
        button.OnPointerDown(null);

        button.OnPointerUp(null);

        Assert.That(button.IsHeld, Is.False);
    }

    [Test]
    public void OnPointerExit_ClearsHeld()
    {
        button.OnPointerDown(null);

        button.OnPointerExit(null);

        Assert.That(button.IsHeld, Is.False);
    }

    [Test]
    public void OnDisable_ClearsHeld()
    {
        button.OnPointerDown(null);

        root.SetActive(false);

        Assert.That(button.IsHeld, Is.False);
    }
}
```

注意：`OnPointerDown(null)` 传 `null` 是安全的，实现不读取 `eventData`。

- [ ] **Step 2: 跑测试确认失败**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "HoldButtonTests" })`
Expected: 编译失败 —— `HoldButton` 类型不存在

- [ ] **Step 3: 写最小实现**

创建 `Assets/UI/BattleHUD/Scripts/HoldButton.cs`：

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsHeld { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsHeld = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        IsHeld = false;
    }

    private void OnDisable()
    {
        IsHeld = false;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "HoldButtonTests" })`
Expected: PASS，5 个测试全绿

- [ ] **Step 5: 提交**

```bash
git add Assets/UI/BattleHUD/Scripts/HoldButton.cs Assets/UI/BattleHUD/Scripts/HoldButton.cs.meta \
        Assets/Tests/EditMode/HoldButtonTests.cs Assets/Tests/EditMode/HoldButtonTests.cs.meta
git commit -F Temp/commit-msg.txt
```

提交信息：

```
feat: 新增 HoldButton 按住检测

Button.onClick 只在抬起时触发一次，无法表达持续按住，按住连发做不了。
HoldButton 实现指针按下/抬起/划出三个接口并暴露 IsHeld，按住的判定
由它单独负责，不改动按钮原有交互。
```

---

### Task 4: AttackSwing2D 状态机

**Files:**
- Create: `Assets/Tests/EditMode/Gameplay/AttackSwing2DTests.cs`
- Create: `Assets/Gameplay/Runtime/AttackSwing2D.cs`

**Interfaces:**
- Consumes: `SwingTimeline`（Task 1）、`WeaponMountSlot.HasWeapon`（Task 2）、`HoldButton.IsHeld`（Task 3）
- Produces: `bool AttackSwing2D.IsSwinging`、`void AttackSwing2D.Step(float deltaTime)`；序列化字段 `weaponPivot`、`mountSlot`、`attackButton`、`duration`、`startAngle`、`endAngle` —— Task 6 接线

行为要点：`Step` 是 `Update` 的唯一内容，暴露出来是为了能在 EditMode 里手动推进。`restRotation` 用懒捕获，不依赖 `Awake`（EditMode 下 Unity 不调用 `Awake`）。

- [ ] **Step 1: 写失败的测试**

创建 `Assets/Tests/EditMode/Gameplay/AttackSwing2DTests.cs`：

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class AttackSwing2DTests
{
    private const float Duration = 0.3f;

    private GameObject root;
    private GameObject buttonRoot;
    private AttackSwing2D attack;
    private Transform pivot;
    private WeaponMountSlot mountSlot;
    private HoldButton attackButton;
    private SpriteRenderer weaponRenderer;
    private Texture2D texture;
    private Sprite sprite;

    [SetUp]
    public void SetUp()
    {
        texture = new Texture2D(4, 4);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

        root = new GameObject("Character");

        var pivotGo = new GameObject("MainHand");
        pivotGo.transform.SetParent(root.transform);
        pivot = pivotGo.transform;
        mountSlot = pivotGo.AddComponent<WeaponMountSlot>();

        var weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(pivot);
        weaponRenderer = weaponGo.AddComponent<SpriteRenderer>();

        buttonRoot = new GameObject("AttackButton");
        attackButton = buttonRoot.AddComponent<HoldButton>();

        attack = root.AddComponent<AttackSwing2D>();

        var attackSerialized = new SerializedObject(attack);
        attackSerialized.FindProperty("weaponPivot").objectReferenceValue = pivot;
        attackSerialized.FindProperty("mountSlot").objectReferenceValue = mountSlot;
        attackSerialized.FindProperty("attackButton").objectReferenceValue = attackButton;
        attackSerialized.FindProperty("duration").floatValue = Duration;
        attackSerialized.FindProperty("startAngle").floatValue = 60f;
        attackSerialized.FindProperty("endAngle").floatValue = -60f;
        attackSerialized.ApplyModifiedPropertiesWithoutUndo();

        var slotSerialized = new SerializedObject(mountSlot);
        slotSerialized.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
        slotSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(buttonRoot);
        Object.DestroyImmediate(sprite);
        Object.DestroyImmediate(texture);
    }

    [Test]
    public void Step_NotHeld_StaysAtRestRotation()
    {
        GiveWeapon();

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.False);
        Assert.That(Quaternion.Angle(pivot.localRotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [Test]
    public void Step_HeldWithoutWeapon_DoesNotSwing()
    {
        attackButton.OnPointerDown(null);

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.False);
    }

    [Test]
    public void Step_HeldWithWeapon_StartsAtStartAngle()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.True);
        Assert.That(pivot.localEulerAngles.z, Is.EqualTo(60f).Within(0.01f));
    }

    [Test]
    public void Step_HalfDuration_ReachesMidAngle()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);

        attack.Step(Duration * 0.5f);

        Assert.That(attack.IsSwinging, Is.True);
        Assert.That(Mathf.DeltaAngle(pivot.localEulerAngles.z, 0f), Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void Step_BeyondDuration_EndsAndRestoresRestRotation()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);

        attack.Step(Duration * 2f);

        Assert.That(attack.IsSwinging, Is.False);
        Assert.That(Quaternion.Angle(pivot.localRotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [Test]
    public void Step_StillHeldAfterFinish_RestartsSwing()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);
        attack.Step(Duration * 2f);

        attack.Step(0.016f);

        Assert.That(attack.IsSwinging, Is.True);
        Assert.That(pivot.localEulerAngles.z, Is.EqualTo(60f).Within(0.01f));
    }

    [Test]
    public void Step_ReleasedMidSwing_FinishesCurrentSwing()
    {
        GiveWeapon();
        attackButton.OnPointerDown(null);
        attack.Step(0.016f);
        attackButton.OnPointerUp(null);

        attack.Step(Duration * 2f);

        Assert.That(attack.IsSwinging, Is.False);
        Assert.That(Quaternion.Angle(pivot.localRotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [Test]
    public void Step_WithoutWiredReferences_DoesNotThrow()
    {
        var bare = new GameObject("Bare");
        var bareAttack = bare.AddComponent<AttackSwing2D>();

        Assert.DoesNotThrow(() => bareAttack.Step(0.016f));
        Assert.That(bareAttack.IsSwinging, Is.False);

        Object.DestroyImmediate(bare);
    }

    [Test]
    public void Step_PreservesRestRotationOffset()
    {
        GiveWeapon();
        pivot.localRotation = Quaternion.Euler(0f, 0f, 15f);
        attackButton.OnPointerDown(null);

        attack.Step(0.016f);

        Assert.That(Mathf.DeltaAngle(pivot.localEulerAngles.z, 75f), Is.EqualTo(0f).Within(0.01f));
    }

    private void GiveWeapon()
    {
        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = true;
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "AttackSwing2DTests" })`
Expected: 编译失败 —— `AttackSwing2D` 类型不存在

- [ ] **Step 3: 写最小实现**

创建 `Assets/Gameplay/Runtime/AttackSwing2D.cs`：

```csharp
using UnityEngine;

public sealed class AttackSwing2D : MonoBehaviour
{
    [SerializeField] private Transform weaponPivot;
    [SerializeField] private WeaponMountSlot mountSlot;
    [SerializeField] private HoldButton attackButton;
    [SerializeField] private float duration = 0.3f;
    [SerializeField] private float startAngle = 60f;
    [SerializeField] private float endAngle = -60f;

    private SwingTimeline timeline;
    private Quaternion restRotation = Quaternion.identity;
    private bool restCaptured;
    private bool isSwinging;

    public bool IsSwinging => isSwinging;

    private void Awake()
    {
        EnsureReady();
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    public void Step(float deltaTime)
    {
        if (!EnsureReady())
        {
            return;
        }

        if (!isSwinging)
        {
            if (attackButton.IsHeld && mountSlot.HasWeapon)
            {
                BeginSwing();
            }

            return;
        }

        timeline.Tick(deltaTime);
        ApplyAngle(timeline.Angle);

        if (!timeline.IsRunning)
        {
            EndSwing();
        }
    }

    private bool EnsureReady()
    {
        if (weaponPivot == null || mountSlot == null || attackButton == null)
        {
            return false;
        }

        if (!restCaptured)
        {
            restRotation = weaponPivot.localRotation;
            restCaptured = true;
        }

        if (timeline == null)
        {
            timeline = new SwingTimeline(duration, startAngle, endAngle);
        }

        return true;
    }

    private void BeginSwing()
    {
        timeline.Restart();
        isSwinging = true;
        ApplyAngle(timeline.Angle);
    }

    private void EndSwing()
    {
        isSwinging = false;
        weaponPivot.localRotation = restRotation;
    }

    private void ApplyAngle(float angle)
    {
        weaponPivot.localRotation = restRotation * Quaternion.Euler(0f, 0f, angle);
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode", filter: "AttackSwing2DTests" })`
Expected: PASS，9 个测试全绿

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/AttackSwing2D.cs Assets/Gameplay/Runtime/AttackSwing2D.cs.meta \
        Assets/Tests/EditMode/Gameplay/AttackSwing2DTests.cs Assets/Tests/EditMode/Gameplay/AttackSwing2DTests.cs.meta
git commit -F Temp/commit-msg.txt
```

提交信息：

```
feat: 新增挥砍状态机 AttackSwing2D

按住攻击键且有可见武器时启动一次 0.30 秒的挥砍，把 SwingTimeline 的角度
写进 MainHand.localRotation，结束后复位。连发不写计时器，回 Idle 后仍按住
就自然进入下一次。restRotation 懒捕获，便于在 EditMode 下驱动测试。
```

---

### Task 5: 把 HoldButton 挂到 AttackButton 预制体

**Files:**
- Modify: `Assets/UI/BattleHUD/Prefabs/Modules/AttackButton.prefab`

**Interfaces:**
- Consumes: `HoldButton`（Task 3）
- Produces: 场景中 `HUDCanvas/BattleHUD/AttackButton` 上存在 `HoldButton` —— Task 6 消费

`AttackButton.prefab` 是嵌套预制体，嵌在 `BattleHUD.prefab` 内、被 `BattleScene` 实例引用。改它即可自动传播到外层，不需要在场景里单独添加。

- [ ] **Step 1: 在预制体上加组件**

Run: `unity_execute_code`

```csharp
var path = "Assets/UI/BattleHUD/Prefabs/Modules/AttackButton.prefab";
var contents = UnityEditor.PrefabUtility.LoadPrefabContents(path);
if (contents.GetComponent<HoldButton>() == null)
{
    contents.AddComponent<HoldButton>();
}
UnityEditor.PrefabUtility.SaveAsPrefabAsset(contents, path);
UnityEditor.PrefabUtility.UnloadPrefabContents(contents);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
return "done";
```

- [ ] **Step 2: 校验**

Run: `unity_execute_code`

```csharp
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/BattleHUD/Prefabs/Modules/AttackButton.prefab");
var sceneButton = GameObject.Find("HUDCanvas/BattleHUD/AttackButton");
return string.Format("prefab HoldButton={0} | scene HoldButton={1} | scene IsHeld={2}",
    prefab != null && prefab.GetComponent<HoldButton>() != null,
    sceneButton != null && sceneButton.GetComponent<HoldButton>() != null,
    sceneButton != null && sceneButton.GetComponent<HoldButton>() != null ? sceneButton.GetComponent<HoldButton>().IsHeld.ToString() : "n/a");
```

Expected: `prefab HoldButton=True | scene HoldButton=True | scene IsHeld=False`

- [ ] **Step 3: 确认 Button 原有交互未被破坏**

Run: `unity_execute_code`

```csharp
var go = GameObject.Find("HUDCanvas/BattleHUD/AttackButton");
var button = go.GetComponent<UnityEngine.UI.Button>();
return string.Format("Button present={0} onClickCount={1} interactable={2}",
    button != null, button.onClick.GetPersistentEventCount(), button.interactable);
```

Expected: `Button present=True onClickCount=0 interactable=True`（与改动前一致；`onClick` 本来就是空的，攻击不由它触发）

- [ ] **Step 4: 提交**

```bash
git add Assets/UI/BattleHUD/Prefabs/Modules/AttackButton.prefab \
        Assets/UI/BattleHUD/Prefabs/Modules/AttackButton.prefab.meta \
        Assets/UI/BattleHUD/Prefabs/BattleHUD.prefab
git commit -F Temp/commit-msg.txt
```

提交信息：

```
feat: 攻击按钮接入 HoldButton

AttackButton 是嵌套预制体，在它上面加 HoldButton 后外层 BattleHUD 与场景
实例自动继承，不需要在场景里单独挂。按钮原有的 Button 组件与空 onClick
保持不变。
```

注：`BattleHUD.prefab` 是否出现在改动列表取决于 Unity 是否重写了外层嵌套引用，若 `git status` 显示它未变更，从 `git add` 中移除即可。

---

### Task 6: 角色预制体与场景接线

**Files:**
- Modify: `Assets/Gameplay/Prefabs/Character_Base.prefab`
- Modify: `Assets/Scenes/BattleScene.unity`

**Interfaces:**
- Consumes: `AttackSwing2D`（Task 4）、`HoldButton`（Task 5）
- Produces: 可运行的完整链路 —— Task 7 验证

接线分两层：`weaponPivot` 与 `mountSlot` 是预制体内部引用，写进预制体；`attackButton` 指向 HUD 内的组件，预制体不能引用场景对象，必须作为场景 override（与既有 `PlayerMovement2D.joystick` 的处理方式一致）。

- [ ] **Step 1: 给 Character_Base 预制体加 AttackSwing2D 并接内部引用**

Run: `unity_execute_code`

```csharp
var path = "Assets/Gameplay/Prefabs/Character_Base.prefab";
var contents = UnityEditor.PrefabUtility.LoadPrefabContents(path);

var mainHand = contents.transform.Find("Visual/MainHand");
if (mainHand == null)
{
    UnityEditor.PrefabUtility.UnloadPrefabContents(contents);
    return "MainHand not found";
}

var attack = contents.GetComponent<AttackSwing2D>();
if (attack == null)
{
    attack = contents.AddComponent<AttackSwing2D>();
}

var serialized = new UnityEditor.SerializedObject(attack);
serialized.FindProperty("weaponPivot").objectReferenceValue = mainHand;
serialized.FindProperty("mountSlot").objectReferenceValue = mainHand.GetComponent<WeaponMountSlot>();
serialized.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.PrefabUtility.SaveAsPrefabAsset(contents, path);
UnityEditor.PrefabUtility.UnloadPrefabContents(contents);
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
return "ok";
```

- [ ] **Step 2: 校验预制体**

Run: `unity_execute_code`

```csharp
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Prefabs/Character_Base.prefab");
var attack = prefab.GetComponent<AttackSwing2D>();
var serialized = new UnityEditor.SerializedObject(attack);
var pivot = serialized.FindProperty("weaponPivot").objectReferenceValue;
var slot = serialized.FindProperty("mountSlot").objectReferenceValue;
return string.Format("attack present=True pivot={0} mountSlot={1} duration={2} start={3} end={4}",
    pivot != null ? pivot.name : "NULL",
    slot != null ? slot.name : "NULL",
    serialized.FindProperty("duration").floatValue,
    serialized.FindProperty("startAngle").floatValue,
    serialized.FindProperty("endAngle").floatValue);
```

Expected: `attack present=True pivot=MainHand mountSlot=MainHand duration=0.3 start=60 end=-60`

- [ ] **Step 3: 场景实例接 attackButton 并保存**

Run: `unity_execute_code`

```csharp
var root = GameObject.Find("Gameplay/Character_Base");
if (root == null) return "character instance not found";

var attack = root.GetComponent<AttackSwing2D>();
var hold = GameObject.Find("HUDCanvas/BattleHUD/AttackButton").GetComponent<HoldButton>();
if (attack == null || hold == null) return "missing component";

var serialized = new UnityEditor.SerializedObject(attack);
serialized.FindProperty("attackButton").objectReferenceValue = hold;
serialized.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(root.scene);

var check = new UnityEditor.SerializedObject(attack);
return string.Format("saved={0} attackButton={1}",
    saved,
    check.FindProperty("attackButton").objectReferenceValue != null
        ? check.FindProperty("attackButton").objectReferenceValue.name
        : "NULL");
```

Expected: `saved=True attackButton=AttackButton`

- [ ] **Step 4: 编译检查与全量 EditMode 测试**

Run: `unity_get_compilation_errors`
Expected: 无错误

Run: `unity_advanced_tool(tool: "unity_testing_run_tests", params: { mode: "EditMode" })`
Expected: 全绿，含此前的 `BagPanelControllerTests`、`RoomCollisionBoundsTests` 等未回归

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Prefabs/Character_Base.prefab Assets/Scenes/BattleScene.unity
git commit -F Temp/commit-msg.txt
```

提交信息：

```
feat: 角色预制体接入挥砍并连线攻击按钮

Character_Base 加 AttackSwing2D，weaponPivot 与 mountSlot 作为预制体内部
引用保存；attackButton 指向 HUD 的 HoldButton，因预制体无法引用场景对象，
作为 BattleScene 的场景 override。
```

---

### Task 7: 实机验证

**Files:** 无（只做验证，不改代码）

- [ ] **Step 1: 进入 Play Mode 并装备武器**

Run: `unity_play_mode(action: "play")`，然后 `unity_execute_code`

```csharp
var bpc = UnityEngine.Object.FindObjectOfType<BagPanelController>(true);
var sword = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Inventory/Items/TrainingSword.asset");
bpc.Model.TryAdd(sword);
var ok = bpc.Model.TryEquipFromBag(0, EquipmentSlotType.MeleeWeapon);
return "equipped=" + ok;
```

Expected: `equipped=True`

- [ ] **Step 2: 断言未按住时不挥砍**

Run: `unity_execute_code`

```csharp
var attack = GameObject.Find("Gameplay/Character_Base").GetComponent<AttackSwing2D>();
attack.Step(0.016f);
return "IsSwinging=" + attack.IsSwinging;
```

Expected: `IsSwinging=False`

- [ ] **Step 3: 模拟按住并逐段推进，抓三个关键帧的角度**

Run: `unity_execute_code`

```csharp
var root = GameObject.Find("Gameplay/Character_Base");
var attack = root.GetComponent<AttackSwing2D>();
var pivot = root.transform.Find("Visual/MainHand");
var hold = GameObject.Find("HUDCanvas/BattleHUD/AttackButton").GetComponent<HoldButton>();

hold.OnPointerDown(null);

attack.Step(0.016f);
var startZ = pivot.localEulerAngles.z;

attack.Step(0.15f);
var midZ = pivot.localEulerAngles.z;

attack.Step(0.15f);
var endSwinging = attack.IsSwinging;
var endZ = pivot.localEulerAngles.z;

return string.Format("start={0} mid={1} afterFinish swinging={2} z={3}", startZ, midZ, endSwinging, endZ);
```

Expected: `start=60` 附近、`mid=0` 附近、`afterFinish swinging=False` 且 `z=0`

- [ ] **Step 4: 截图确认剑在起始角与中间角**

Run: `unity_execute_code`（在 `hold.OnPointerDown` 后推进到起始角，用 `RenderTexture` 拍角色特写）

```csharp
var root = GameObject.Find("Gameplay/Character_Base");
var attack = root.GetComponent<AttackSwing2D>();
var pivot = root.transform.Find("Visual/MainHand");
var hold = GameObject.Find("HUDCanvas/BattleHUD/AttackButton").GetComponent<HoldButton>();
var cam = UnityEngine.Object.FindObjectOfType<Camera>();

hold.OnPointerDown(null);
attack.Step(0.016f);

var go = new GameObject("__tmpShotCam");
var shot = go.AddComponent<Camera>();
shot.orthographic = true;
shot.orthographicSize = 0.9f;
shot.transform.position = new Vector3(root.transform.position.x + 0.2f, root.transform.position.y + 0.45f, -10f);
shot.clearFlags = CameraClearFlags.SolidColor;
shot.backgroundColor = cam.backgroundColor;
shot.cullingMask = cam.cullingMask;

var rt = new RenderTexture(700, 700, 24);
shot.targetTexture = rt;
shot.Render();
shot.targetTexture = null;
RenderTexture.active = rt;
var tex = new Texture2D(700, 700, TextureFormat.RGBA32, false);
tex.ReadPixels(new Rect(0, 0, 700, 700), 0, 0);
tex.Apply();
RenderTexture.active = null;
System.IO.File.WriteAllBytes("Assets/Screenshots/swing_start.png", tex.EncodeToPNG());

UnityEngine.Object.DestroyImmediate(go);
UnityEngine.Object.DestroyImmediate(tex);
rt.Release();
return "saved swing_start.png";
```

Expected: 图片中剑斜举在头部侧上方（起始角 +60°）

再用同样方式推进到中间角另存 `swing_mid.png`（`attack.Step(0.15f)` 后再渲染），剑应指向正前方水平。

- [ ] **Step 5: 确认空手不响应**

Run: `unity_execute_code`

```csharp
var bpc = UnityEngine.Object.FindObjectOfType<BagPanelController>(true);
bpc.Model.TryUnequipToBag(EquipmentSlotType.MeleeWeapon, 0);
var root = GameObject.Find("Gameplay/Character_Base");
var attack = root.GetComponent<AttackSwing2D>();
var hold = GameObject.Find("HUDCanvas/BattleHUD/AttackButton").GetComponent<HoldButton>();

hold.OnPointerDown(null);
attack.Step(0.016f);
var swinging = attack.IsSwinging;
hold.OnPointerUp(null);
return "IsSwinging=" + swinging;
```

Expected: `IsSwinging=False`

- [ ] **Step 6: 退出 Play Mode 并清理临时截图**

Run: `unity_play_mode(action: "stop")`，然后删除 `Assets/Screenshots/swing_start.png`、`swing_mid.png` 及各自的 `.meta`。

Run: `unity_execute_code` 确认场景未被污染

```csharp
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var cam = UnityEngine.Object.FindObjectOfType<Camera>();
return string.Format("dirty={0} camOrtho={1} camPos={2}", scene.isDirty, cam.orthographicSize, cam.transform.position);
```

Expected: `dirty=False`、`camOrtho=4.108` 附近、`camPos=(0.24, -1.14, -10)`

- [ ] **Step 7: 交回用户手测**

以下三项 MCP 无法真实模拟，需用户在编辑器或真机上确认：

- 真实按住攻击按钮：剑连续扫动，松开后当前这刀播完
- 按住不放：每 0.30 秒一轮，持续连发
- 挥砍期间按方向键：人物照常移动；中途反向时剑镜像翻到另一侧

---

## Self-Review

**Spec 覆盖**

| Spec 章节 | 对应 Task |
| --- | --- |
| 扇形 +60°→−60°、0.30 秒、线性 | Task 1（`SwingTimeline`）、Task 4（序列化默认值）、Task 6 Step 2（校验） |
| 状态机 Idle/Swinging、不被打断 | Task 4 |
| 按住自动连发 | Task 4（`Step_StillHeldAfterFinish_RestartsSwing`） |
| 空手不响应 | Task 2（`HasWeapon`）、Task 4（`Step_HeldWithoutWeapon_DoesNotSwing`）、Task 7 Step 5 |
| 不锁移动、不锁朝向 | 无需改动 `PlayerMovement2D` / `CharacterVisual2D`；Task 7 Step 7 手测 |
| 仅 `AttackButton` 触发 | Task 3、Task 5 |
| 仅占位：刀光 | 本轮不实现，spec「后续扩展」已说明 |
| `HoldButton` 的 `OnDisable` 复位 | Task 3 |
| 组件未接线时不抛异常 | Task 4（`Step_WithoutWiredReferences_DoesNotThrow`） |

**占位符扫描**：无 TBD / TODO；每个改动步骤都给出了完整代码或可直接执行的 `unity_execute_code` 片段。

**类型一致性**：`SwingTimeline.Restart()` / `Tick(float)` / `Progress` / `Angle` / `IsRunning` 在 Task 1 定义、Task 4 消费，命名一致；`WeaponMountSlot.HasWeapon`、`HoldButton.IsHeld`、`AttackSwing2D.Step(float)` 同理。字段名 `weaponPivot` / `mountSlot` / `attackButton` / `duration` / `startAngle` / `endAngle` 在 Task 4 定义，Task 6 的 `SerializedObject.FindProperty` 用的是同一组名字。
