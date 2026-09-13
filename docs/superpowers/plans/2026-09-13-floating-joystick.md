# Floating Joystick Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the movement joystick spawn at the user's touch position on the left side of the screen and return to a centered default position when released.

**Architecture:** Extend `VirtualJoystickController` with a small amount of state for default position, dynamic placement, and reset behavior. Keep the player movement consumer unchanged and preserve the current HUD prefab/scene wiring while updating joystick prefab defaults and tests.

**Tech Stack:** Unity 2022.3, Unity UI, Rigidbody2D gameplay input, Unity Test Framework

## Global Constraints

- Keep keyboard + joystick movement aggregation unchanged in `PlayerMovement2D`.
- The joystick must return to a visible default position after pointer up.
- The joystick should only react to touch/pointer down on the left half of the screen.
- Existing attack and bag buttons must remain in place.

---

### Task 1: Cover Dynamic Joystick Behavior With Tests

**Files:**
- Modify: `Assets/Tests/EditMode/VirtualJoystickControllerTests.cs`

**Interfaces:**
- Consumes: `VirtualJoystickController.EditorBind(RectTransform root, RectTransform baseTarget, RectTransform handleTarget, float radius)`
- Produces: test coverage for dynamic root placement and return-to-default behavior

- [ ] Add a failing test that expects pointer down to move the joystick root to the pointer location.
- [ ] Run the Unity compile/test validation path and confirm the new assertion fails for the expected reason.
- [ ] Add a failing test that expects pointer up to restore the root to its default anchored position.
- [ ] Re-run validation and confirm it still fails for the new behavior gap.

### Task 2: Implement Dynamic Placement In The Joystick Controller

**Files:**
- Modify: `Assets/UI/BattleHUD/Scripts/VirtualJoystickController.cs`

**Interfaces:**
- Consumes: pointer events from Unity UI and current RectTransform references
- Produces: joystick root repositioning on pointer down, left-half filtering, and reset-to-default-position on pointer up

- [ ] Add serialized state for the default anchored position and optional dynamic placement toggle.
- [ ] Capture the default anchored position during initialization.
- [ ] On pointer down, move the joystick root to the touch position before calculating drag input.
- [ ] On pointer up, reset the handle and restore the root to its default anchored position.
- [ ] Keep `Input`, `Magnitude`, and drag math compatible with the existing player movement consumer.

### Task 3: Align Prefab And Scene Defaults

**Files:**
- Modify: `Assets/UI/BattleHUD/Prefabs/Modules/Joystick.prefab`
- Modify: `Assets/UI/BattleHUD/Prefabs/BattleHUD.prefab`
- Modify: `Assets/Scenes/SampleScene.unity`

**Interfaces:**
- Consumes: updated `VirtualJoystickController` behavior
- Produces: a centered-inward default joystick placement that matches runtime reset behavior

- [ ] Move the joystick default position inward from the screen edge.
- [ ] Ensure the scene instance used by `PlayerMovement2D` matches the prefab defaults.
- [ ] Preserve button layout and existing joystick size tuning.

### Task 4: Verify End-To-End Behavior

**Files:**
- Verify: `Assets/Tests/EditMode/VirtualJoystickControllerTests.cs`
- Verify: `Assets/UI/BattleHUD/Scripts/VirtualJoystickController.cs`
- Verify: `Assets/Scenes/SampleScene.unity`

**Interfaces:**
- Consumes: Unity compile state and current scene object graph
- Produces: evidence that the joystick uses dynamic placement and resets correctly

- [ ] Run Unity compilation checks and confirm zero errors.
- [ ] Read back the joystick controller state and default scene position from the active scene.
- [ ] Confirm `PlayerMovement2D` still references `HUDCanvas/BattleHUD/Joystick`.
