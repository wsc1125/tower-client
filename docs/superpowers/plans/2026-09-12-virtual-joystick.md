# Virtual Joystick Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `BattleHUD` 的 `Joystick` 模块补齐标准虚拟摇杆逻辑，使其支持拖拽、前轮盘转向、回中和事件接口，同时不直接绑定角色移动。

**Architecture:** 保留现有 `Joystick -> Base + Handle` 预制体结构，在根节点新增 `VirtualJoystickController` 脚本并实现 `IPointerDownHandler`、`IDragHandler`、`IPointerUpHandler`。脚本负责计算局部输入向量、控制 `Handle` 的位移和旋转，并通过公开只读属性和 UnityEvent 暴露输入值。

**Tech Stack:** Unity 2022、UGUI、Unity EventSystem、UnityEngine.Events、Unity EditMode Tests、Unity MCP

## Global Constraints

- 素材与 HUD 结构继续沿用 `Assets/UI/BattleHUD/` 现有目录
- 保留 `Joystick` 现有 `Base` 与 `Handle` 命名，不改成其他层级结构
- 当前阶段不直接绑定角色移动逻辑
- 当前阶段不做动态底座、死区配置、平滑插值
- 优先使用 `Unity MCP` 完成 prefab、场景与验证操作
- 当前项目不是 Git 仓库，不执行提交步骤

---

### Task 1: 为摇杆脚本建立失败测试与公共接口

**Files:**
- Create: `Assets/UI/BattleHUD/Scripts/VirtualJoystickController.cs`
- Create: `Assets/Tests/EditMode/VirtualJoystickControllerTests.cs`
- Modify: `Assets/UI/BattleHUD/BattleHUD.asmdef`
- Modify: `Assets/Tests/EditMode/BattleHUD.EditModeTests.asmdef`

**Interfaces:**
- Consumes: `RectTransform baseRect`, `RectTransform handleRect`, `UnityEngine.EventSystems.PointerEventData`
- Produces:
  - `public Vector2 Input { get; }`
  - `public float Magnitude { get; }`
  - `public bool IsDragging { get; }`
  - `public void OnPointerDown(PointerEventData eventData)`
  - `public void OnDrag(PointerEventData eventData)`
  - `public void OnPointerUp(PointerEventData eventData)`
  - `public JoystickValueChangedEvent OnValueChanged`
  - `public UnityEvent OnPointerDownEvent`
  - `public UnityEvent OnPointerUpEvent`

- [ ] **Step 1: 写摇杆失败测试**

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

public class VirtualJoystickControllerTests
{
    [Test]
    public void DraggingUpdatesInputMagnitudeAndHandlePosition()
    {
        var root = new GameObject("Joystick", typeof(RectTransform), typeof(VirtualJoystickController));
        var baseGo = new GameObject("Base", typeof(RectTransform));
        var handleGo = new GameObject("Handle", typeof(RectTransform));
        baseGo.transform.SetParent(root.transform, false);
        handleGo.transform.SetParent(root.transform, false);

        var controller = root.GetComponent<VirtualJoystickController>();
        controller.EditorBind(
            root.GetComponent<RectTransform>(),
            baseGo.GetComponent<RectTransform>(),
            handleGo.GetComponent<RectTransform>(),
            100f
        );

        var eventData = new PointerEventData(EventSystem.current ?? new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).GetComponent<EventSystem>())
        {
            position = new Vector2(100f, 0f)
        };

        controller.OnPointerDown(eventData);
        controller.OnDrag(eventData);

        Assert.That(controller.IsDragging, Is.True);
        Assert.That(controller.Input.x, Is.EqualTo(1f).Within(0.01f));
        Assert.That(controller.Magnitude, Is.EqualTo(1f).Within(0.01f));
        Assert.That(handleGo.GetComponent<RectTransform>().anchoredPosition.x, Is.EqualTo(100f).Within(0.01f));
    }
}
```

- [ ] **Step 2: 运行测试确认当前失败**

Run: 在 Unity Test Runner 中运行 `VirtualJoystickControllerTests`  
Expected: 因 `VirtualJoystickController` 尚不存在或缺少 `EditorBind` / 输入更新逻辑而失败

- [ ] **Step 3: 写最小摇杆脚本骨架**

```csharp
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[System.Serializable]
public class JoystickValueChangedEvent : UnityEvent<Vector2, float> { }

public class VirtualJoystickController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public Vector2 Input => input;
    public float Magnitude => input.magnitude;
    public bool IsDragging => isDragging;

    [field: SerializeField] public JoystickValueChangedEvent OnValueChanged { get; private set; } = new();
    [field: SerializeField] public UnityEvent OnPointerDownEvent { get; private set; } = new();
    [field: SerializeField] public UnityEvent OnPointerUpEvent { get; private set; } = new();

    private RectTransform rootRect;
    private RectTransform baseRect;
    private RectTransform handleRect;
    private float maxRadius = 100f;
    private Vector2 input;
    private bool isDragging;

    public void EditorBind(RectTransform root, RectTransform baseTarget, RectTransform handleTarget, float radius)
    {
        rootRect = root;
        baseRect = baseTarget;
        handleRect = handleTarget;
        maxRadius = radius;
    }

    public void OnPointerDown(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnPointerUp(PointerEventData eventData) { }
}
```

- [ ] **Step 4: 重新运行测试确认进入下一类失败**

Run: 在 Unity Test Runner 中重新运行 `VirtualJoystickControllerTests`  
Expected: 编译通过，但断言失败，提示 `Input` / `Handle` 尚未更新

### Task 2: 实现标准虚拟摇杆输入行为

**Files:**
- Modify: `Assets/UI/BattleHUD/Scripts/VirtualJoystickController.cs`
- Modify: `Assets/Tests/EditMode/VirtualJoystickControllerTests.cs`

**Interfaces:**
- Consumes:
  - `RectTransformUtility.ScreenPointToLocalPointInRectangle(...)`
  - `handleRect.anchoredPosition`
  - `OnValueChanged.Invoke(Vector2 input, float magnitude)`
- Produces:
  - `Input` 在拖拽时输出归一化方向
  - `Magnitude` 输出 `0 ~ 1`
  - `Handle` 在拖拽时位移、旋转，抬起时回中

- [ ] **Step 1: 扩充测试覆盖半径限制与回中**

```csharp
[Test]
public void PointerUpResetsInputAndHandle()
{
    var root = new GameObject("Joystick", typeof(RectTransform), typeof(VirtualJoystickController));
    var baseGo = new GameObject("Base", typeof(RectTransform));
    var handleGo = new GameObject("Handle", typeof(RectTransform));
    baseGo.transform.SetParent(root.transform, false);
    handleGo.transform.SetParent(root.transform, false);

    var controller = root.GetComponent<VirtualJoystickController>();
    controller.EditorBind(
        root.GetComponent<RectTransform>(),
        baseGo.GetComponent<RectTransform>(),
        handleGo.GetComponent<RectTransform>(),
        80f
    );

    var eventSystem = EventSystem.current ?? new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).GetComponent<EventSystem>();
    var eventData = new PointerEventData(eventSystem) { position = new Vector2(120f, 0f) };

    controller.OnPointerDown(eventData);
    controller.OnDrag(eventData);
    controller.OnPointerUp(eventData);

    Assert.That(controller.IsDragging, Is.False);
    Assert.That(controller.Input, Is.EqualTo(Vector2.zero));
    Assert.That(controller.Magnitude, Is.EqualTo(0f).Within(0.01f));
    Assert.That(handleGo.GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(Vector2.zero));
}
```

- [ ] **Step 2: 实现拖拽、旋转、回中逻辑**

```csharp
private void Awake()
{
    if (rootRect == null) rootRect = GetComponent<RectTransform>();
    if (baseRect == null) baseRect = transform.Find("Base") as RectTransform;
    if (handleRect == null) handleRect = transform.Find("Handle") as RectTransform;
    if (maxRadius <= 0f && baseRect != null) maxRadius = baseRect.sizeDelta.x * 0.5f;
    ResetHandle();
}

public void OnPointerDown(PointerEventData eventData)
{
    isDragging = true;
    OnPointerDownEvent.Invoke();
    UpdateFromPointer(eventData);
}

public void OnDrag(PointerEventData eventData)
{
    if (!isDragging) return;
    UpdateFromPointer(eventData);
}

public void OnPointerUp(PointerEventData eventData)
{
    isDragging = false;
    ResetHandle();
    OnPointerUpEvent.Invoke();
}

private void UpdateFromPointer(PointerEventData eventData)
{
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, eventData.position, eventData.pressEventCamera, out var localPoint))
        return;

    var clamped = Vector2.ClampMagnitude(localPoint, maxRadius);
    input = clamped / Mathf.Max(1f, maxRadius);
    handleRect.anchoredPosition = clamped;
    handleRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg - 90f);
    OnValueChanged.Invoke(input, input.magnitude);
}

private void ResetHandle()
{
    input = Vector2.zero;
    if (handleRect != null)
    {
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.localEulerAngles = Vector3.zero;
    }
    OnValueChanged.Invoke(Vector2.zero, 0f);
}
```

- [ ] **Step 3: 运行 EditMode 测试验证通过**

Run: 在 Unity Test Runner 中运行 `VirtualJoystickControllerTests`  
Expected: 所有摇杆测试通过，`Input`、`Magnitude`、回中行为都符合断言

### Task 3: 将摇杆逻辑接入现有预制体与场景实例

**Files:**
- Modify: `Assets/UI/BattleHUD/Prefabs/Modules/Joystick.prefab`
- Modify: `Assets/UI/BattleHUD/Prefabs/BattleHUD.prefab`
- Modify: `Assets/Scenes/SampleScene.unity`

**Interfaces:**
- Consumes:
  - `VirtualJoystickController.EditorBind(...)` 仅测试使用
  - prefab 中 `Base` 与 `Handle` 节点
- Produces:
  - 带 `VirtualJoystickController` 的 `Joystick.prefab`
  - `BattleHUD` 与 `SampleScene` 中的摇杆实例同步升级

- [ ] **Step 1: 写 prefab 结构测试**

```csharp
[Test]
public void JoystickPrefab_HasControllerAndNamedChildren()
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/BattleHUD/Prefabs/Modules/Joystick.prefab");

    Assert.That(prefab, Is.Not.Null);
    Assert.That(prefab.GetComponent<VirtualJoystickController>(), Is.Not.Null);
    Assert.That(prefab.transform.Find("Base"), Is.Not.Null);
    Assert.That(prefab.transform.Find("Handle"), Is.Not.Null);
}
```

- [ ] **Step 2: 用 Unity MCP 更新 prefab 绑定**

```csharp
var prefabRoot = PrefabUtility.LoadPrefabContents("Assets/UI/BattleHUD/Prefabs/Modules/Joystick.prefab");
var controller = prefabRoot.GetComponent<VirtualJoystickController>() ?? prefabRoot.AddComponent<VirtualJoystickController>();
var serialized = new SerializedObject(controller);
serialized.FindProperty("baseRect").objectReferenceValue = prefabRoot.transform.Find("Base").GetComponent<RectTransform>();
serialized.FindProperty("handleRect").objectReferenceValue = prefabRoot.transform.Find("Handle").GetComponent<RectTransform>();
serialized.FindProperty("maxRadius").floatValue = 100f;
serialized.ApplyModifiedPropertiesWithoutUndo();
PrefabUtility.SaveAsPrefabAsset(prefabRoot, "Assets/UI/BattleHUD/Prefabs/Modules/Joystick.prefab");
PrefabUtility.UnloadPrefabContents(prefabRoot);
```

- [ ] **Step 3: 替换场景中的摇杆实例并验证**

Run: 使用 Unity MCP 重新实例化 `HUDCanvas/BattleHUD/Joystick`，然后检查层级  
Expected:
  - `HUDCanvas/BattleHUD/Joystick` 根节点存在 `VirtualJoystickController`
  - `Base` 与 `Handle` 子节点仍存在
  - 无新增编译错误

### Task 4: 记录对外接入方式与人工验证步骤

**Files:**
- Modify: `docs/superpowers/specs/2026-09-12-battle-hud-design.md`

**Interfaces:**
- Consumes: `VirtualJoystickController.Input`, `Magnitude`, `OnValueChanged`
- Produces: 后续角色系统可直接接入的说明

- [ ] **Step 1: 补充接入示例**

```csharp
public class PlayerMover : MonoBehaviour
{
    [SerializeField] private VirtualJoystickController joystick;

    private void Update()
    {
        Vector2 input = joystick.Input;
        Vector3 move = new Vector3(input.x, 0f, input.y);
        transform.position += move * Time.deltaTime * 3f;
    }
}
```

- [ ] **Step 2: 列出人工验证清单**

Run:
- 在 Game 视图按下摇杆区域
- 向右上拖拽，观察 `Handle` 位移和旋转
- 松手，观察 `Handle` 回中

Expected:
- `Handle` 不超出底盘半径
- `Handle` 会沿方向旋转
- 松手后位置归零、输入归零
