# Room Collision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `SampleScene` 中补齐玩家方块、敌人方块、墙体碰撞与双输入移动，形成可运行的 2D 房间碰撞闭环。

**Architecture:** 保持现有房间和 HUD 结构不变，新增 `Gameplay` 运行时脚本处理输入、移动与敌人巡逻。场景碰撞基于 `Rigidbody2D + Collider2D`，逻辑验证通过 EditMode 测试和 Unity 实机截图完成。

**Tech Stack:** Unity 2022.3、C#、2D Physics、Unity Test Framework、现有 `VirtualJoystickController`

## Global Constraints

- 不改动现有 HUD 视觉布局
- 不破坏现有房间预制体层级命名
- 优先新增脚本与组件，不重构现有 UI 脚本
- 仅实现最小可运行战斗房间移动碰撞闭环

---

### Task 1: 建立 Gameplay 代码与测试骨架

**Files:**
- Create: `Assets/Gameplay/Gameplay.asmdef`
- Create: `Assets/Gameplay/Runtime/DebugBlockVisual.cs`
- Create: `Assets/Gameplay/Runtime/EnemyPatrol2D.cs`
- Create: `Assets/Gameplay/Runtime/PlayerMovement2D.cs`
- Create: `Assets/Tests/EditMode/Gameplay.EditModeTests.asmdef`
- Create: `Assets/Tests/EditMode/EnemyPatrol2DTests.cs`
- Create: `Assets/Tests/EditMode/PlayerMovement2DTests.cs`

**Interfaces:**
- Consumes: `VirtualJoystickController.Input`
- Produces: `PlayerMovement2D.ResolveMovementInput(Vector2 keyboardInput, Vector2 joystickInput): Vector2`
- Produces: `EnemyPatrol2D.GetNextPosition(Vector2 currentPosition, Vector2 targetPosition, float speed, float deltaTime): Vector2`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void ResolveMovementInput_ClampsCombinedKeyboardAndJoystick()
{
    var resolved = PlayerMovement2D.ResolveMovementInput(Vector2.right, Vector2.up);
    Assert.That(resolved.magnitude, Is.EqualTo(1f).Within(0.001f));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "BattleHUD.EditModeTests.csproj" --filter ResolveMovementInput_ClampsCombinedKeyboardAndJoystick`
Expected: FAIL with missing `PlayerMovement2D`

- [ ] **Step 3: Write minimal implementation**

```csharp
public static Vector2 ResolveMovementInput(Vector2 keyboardInput, Vector2 joystickInput)
{
    return Vector2.ClampMagnitude(keyboardInput + joystickInput, 1f);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "BattleHUD.EditModeTests.csproj" --filter ResolveMovementInput_ClampsCombinedKeyboardAndJoystick`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/Gameplay Assets/Tests/EditMode
git commit -m "feat: add gameplay movement test scaffold"
```

### Task 2: 完成玩家移动与敌人巡逻运行时脚本

**Files:**
- Modify: `Assets/Gameplay/Runtime/EnemyPatrol2D.cs`
- Modify: `Assets/Gameplay/Runtime/PlayerMovement2D.cs`
- Modify: `Assets/Gameplay/Runtime/DebugBlockVisual.cs`
- Modify: `Assets/Tests/EditMode/EnemyPatrol2DTests.cs`
- Modify: `Assets/Tests/EditMode/PlayerMovement2DTests.cs`

**Interfaces:**
- Consumes: `VirtualJoystickController`
- Produces: `PlayerMovement2D.SetJoystick(VirtualJoystickController joystick): void`
- Produces: `EnemyPatrol2D.Initialize(Vector2 pointA, Vector2 pointB): void`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void GetNextPosition_MovesTowardTarget()
{
    var next = EnemyPatrol2D.GetNextPosition(Vector2.zero, Vector2.right, 2f, 0.5f);
    Assert.That(next.x, Is.EqualTo(1f).Within(0.001f));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "BattleHUD.EditModeTests.csproj" --filter "GetNextPosition_MovesTowardTarget|ResolveMovementInput"`
Expected: FAIL with missing methods or wrong behavior

- [ ] **Step 3: Write minimal implementation**

```csharp
public static Vector2 GetNextPosition(Vector2 currentPosition, Vector2 targetPosition, float speed, float deltaTime)
{
    return Vector2.MoveTowards(currentPosition, targetPosition, speed * deltaTime);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "BattleHUD.EditModeTests.csproj" --filter "GetNextPosition_MovesTowardTarget|ResolveMovementInput"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Assets/Gameplay/Runtime Assets/Tests/EditMode
git commit -m "feat: add player movement and enemy patrol runtime scripts"
```

### Task 3: 给场景挂接对象与 2D 碰撞

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`
- Modify: `Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab`

**Interfaces:**
- Consumes: `PlayerMovement2D`, `EnemyPatrol2D`, `DebugBlockVisual`
- Produces: 可运行的 `Gameplay/Player_Block` 与 `Gameplay/Enemy_Block`

- [ ] **Step 1: Add wall colliders and gameplay objects**

```text
Walls/Wall_Top, Wall_Bottom, Wall_Left, Wall_Right: add BoxCollider2D
Create Gameplay/Player_Block and Gameplay/Enemy_Block with Rigidbody2D + BoxCollider2D
```

- [ ] **Step 2: Bind joystick reference and patrol points**

```text
PlayerMovement2D.joystick -> HUDCanvas/BattleHUD/Joystick
EnemyPatrol2D.pointA / pointB -> scene positions inside room
```

- [ ] **Step 3: Save scene and prefab**

Run: save `SampleScene` and `Room_Battle_Closed_01.prefab`
Expected: Unity compilation clean

- [ ] **Step 4: Verify scene compiles**

Run: query Unity compile errors
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/SampleScene.unity Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab
git commit -m "feat: wire room collision scene"
```

### Task 4: 验证运行闭环

**Files:**
- Modify: `Assets/Screenshots/`

**Interfaces:**
- Consumes: 已完成场景与脚本
- Produces: 测试结果与截图证据

- [ ] **Step 1: Run EditMode tests**

Run: `dotnet test "BattleHUD.EditModeTests.csproj"`
Expected: PASS

- [ ] **Step 2: Enter Play Mode and capture game view**

Run: Unity Play Mode + game screenshot
Expected: 可看到玩家方块、敌人方块、房间边界

- [ ] **Step 3: Manually verify collision**

```text
Move player into walls -> blocked
Move player into enemy -> blocked
Observe enemy patrol -> stays inside room
```

- [ ] **Step 4: Exit Play Mode and save artifacts**

Run: save screenshot path under `Assets/Screenshots`
Expected: screenshot created successfully

- [ ] **Step 5: Commit**

```bash
git add Assets/Screenshots
git commit -m "test: verify room collision prototype"
```
