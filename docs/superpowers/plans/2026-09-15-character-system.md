# 人物系统实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把白色方块玩家替换为带 4 帧行走动画与主手武器锚点的真实角色，并打通装备槽到世界武器外观的联动。

**Architecture:** 状态产出（输入）与表现消费（渲染）分离：`CharacterVisual2D` 只接受一个速度向量，不引用 `Input` 或摇杆，联机时只需替换调用方。翻转作用于 `Visual` 子节点的 `localScale.x`，使锚点与武器一并镜像。

**Tech Stack:** Unity 2022.3.62f3c1、C#、NUnit EditMode 测试、Assembly Definition、TextMeshPro/UGUI（沿用现有）

设计依据：[2026-09-15-character-system-design.md](../specs/2026-09-15-character-system-design.md)

## Global Constraints

- 不引入 `Animator`、`AnimatorController` 资产
- 不破坏 `BattleHUD` 现有布局与交互
- 不改动 `Room_Battle_Closed_01` 预制体结构
- 不改动 `Assets/UI/BattleHUD/Sprites/武器图标.png`
- `Assets/Inventory/Items/TrainingSword.asset` 只新增 `worldSprite` 字段值，不改动原有字段
- `Enemy_Block` 保持现状（继续用 `DebugBlockVisual`）
- 像素素材 `filterMode` 必须为 `Point`，不得使用工程现有的 `Bilinear`
- 命名沿用工程现状：无 namespace，脚本放各模块 `Runtime/` 下

## 验证手段（本工程无 CLI 测试入口）

- 编译检查：`unity_get_compilation_errors`
- 跑测试：`unity_advanced_tool` → `unity_testing_run_tests`（`mode: EditMode`，`filter` 传测试类名），再 `unity_testing_get_job` 带 `waitTimeout: 60` 轮询
- 场景/预制体操作：`unity_gameobject_create`、`unity_component_add`、`unity_component_set_property`、`unity_asset_create_prefab`
- 一次性操作（改 import 设置、改场景名）：`unity_execute_code`
- 实机确认：`unity_play_mode` + `unity_screenshot_game`

---

### Task 1: 库存归属修正

**Files:**
- Create: `Assets/Inventory/Runtime/InventoryHolder.cs`
- Create: `Assets/Tests/EditMode/InventoryHolderTests.cs`
- Modify: `Assets/Inventory/Runtime/ItemDefinition.cs`
- Modify: `Assets/UI/BattleHUD/Scripts/BagPanelController.cs`

**Interfaces:**
- Produces: `InventoryHolder.Model`（`Inventory`，惰性单例，重复访问同一实例）——Task 5 消费
- Produces: `ItemDefinition.WorldSprite`（`Sprite`，可能为 null）——Task 5 消费
- Produces: `BagPanelController` 新增序列化字段 `holder`，未接线时回退自建 `Inventory`

- [ ] 写 `InventoryHolderTests`：`Model` 非 null；连续两次访问 `Is.SameAs`
- [ ] 跑测试，确认失败（类型不存在）
- [ ] 新建 `InventoryHolder.cs`（惰性 `Model`，不序列化 `Inventory`，因为它不是 `[Serializable]`）
- [ ] 跑测试，确认通过
- [ ] 给 `ItemDefinition` 加 `[SerializeField] private Sprite worldSprite;` 与 `public Sprite WorldSprite => worldSprite;`
- [ ] `BagPanelController` 加 `[SerializeField] private InventoryHolder holder;`，`EnsureModel` 改为优先取 `holder.Model`
- [ ] 编译检查无错误；跑 `BagPanelControllerTests`、`InventoryTests` 确认未回归
- [ ] 提交

### Task 2: 帧推进纯逻辑

**Files:**
- Create: `Assets/Gameplay/Runtime/SpriteFrameAnimation.cs`
- Create: `Assets/Tests/EditMode/Gameplay/SpriteFrameAnimationTests.cs`

**Interfaces:**
- Produces: `SpriteFrameAnimation(Sprite[] frames, float framesPerSecond)`、`FrameIndex`、`CurrentFrame`、`Tick(float)`、`Reset()`——Task 3 消费

行为约定：`frames` 为 null/空时 `CurrentFrame` 返回 null 且不推进；长度 ≤ 1 时不推进；`framesPerSecond <= 0` 时不推进；`Tick` 累加后按 `1/framesPerSecond` 推进并取模回绕。

- [ ] 写测试：未满一帧时长不推进；超过后推进一帧；末尾回绕到 0；`Reset` 回到 0；空数组 `CurrentFrame` 为 null；`framesPerSecond = 0` 不推进；单帧恒定 0
- [ ] 跑测试，确认失败
- [ ] 实现 `SpriteFrameAnimation`
- [ ] 跑测试，确认通过
- [ ] 提交

### Task 3: 角色表现层

**Files:**
- Create: `Assets/Gameplay/Runtime/CharacterVisual2D.cs`（内含 `FacingDirection` 枚举）
- Create: `Assets/Tests/EditMode/Gameplay/CharacterVisual2DTests.cs`

**Interfaces:**
- Consumes: `SpriteFrameAnimation`
- Produces: `FacingDirection { Left = -1, Right = 1 }`
- Produces: `static bool CharacterVisual2D.TryResolveFacing(Vector2 velocity, out FacingDirection facing)`——死区 `0.01f`，返回 `false` 表示保持原朝向
- Produces: `static bool CharacterVisual2D.IsMoving(Vector2 velocity)`
- Produces: `void CharacterVisual2D.SetMotion(Vector2 velocity)`——Task 6 消费（表现层唯一入口）

挂载位置：`Visual` 子节点，带 `[RequireComponent(typeof(SpriteRenderer))]`，无 `Update`。序列化字段：`spriteRenderer`、`Sprite[] moveFrames`、`float framesPerSecond = 10f`。静止时 `Reset()` 到首帧。

- [ ] 写测试：`velocity.x` 为正/负的朝向解析；`|velocity.x|` 在死区内返回 `false`；纯纵向速度返回 `false`；`IsMoving` 的零向量与微小向量
- [ ] 跑测试，确认失败
- [ ] 实现 `CharacterVisual2D`（翻转写 `transform.localScale.x`，保留幅值符号化）
- [ ] 跑测试，确认通过
- [ ] 提交

### Task 4: 武器挂载锚点

**Files:**
- Create: `Assets/Gameplay/Runtime/WeaponMountSlot.cs`
- Create: `Assets/Gameplay/Runtime/AnchorGizmo.cs`
- Modify: `Assets/Gameplay/Gameplay.asmdef`（`references` 增加 `Inventory`）

**Interfaces:**
- Consumes: `InventoryHolder.Model`、`ItemDefinition.WorldSprite`
- Produces: `WeaponMountSlot`——挂在 `MainHand` 锚点，序列化字段 `weaponRenderer`、`holder`、`mountedSlot`

行为约定：`OnEnable` 订阅 `holder.Model.Changed` 并刷新，`OnDisable` 取消订阅；刷新时取 `GetEquippedItem(mountedSlot)` 的 `WorldSprite` 写入 `weaponRenderer`，无武器时置 `enabled = false`；`holder` 未接线时保持隐藏、不抛异常。

- [ ] 写 `WeaponMountSlot.cs`
- [ ] 写 `AnchorGizmo.cs`（`OnDrawGizmos` 画十字与圆圈，用于对齐握把）
- [ ] 改 `Gameplay.asmdef` 增加 `Inventory` 引用
- [ ] 编译检查无错误
- [ ] 提交

### Task 5: 素材导入

**Files:**
- Create: `Assets/Gameplay/Art/Characters/Player/Player_Walk_Right_0.png` ~ `_3.png`
- Create: `Assets/Gameplay/Art/Weapons/Weapon_Sword.png`

来源：
- `C:\Users\16648\Desktop\素材\人物1\image_1_tWr2ncjQFX4tU4iq5VgOx.png` → `_0`
- `C:\Users\16648\Desktop\素材\人物1\image_2_49S-hNVSnwuZkvPEvyDrq.png` → `_1`
- `C:\Users\16648\Desktop\素材\人物1\image_3_-W2mlZOXB1IW20Z11SEBk.png` → `_2`
- `C:\Users\16648\Desktop\素材\人物1\image_4_VsCcvNsrkOSeXHHqsno6b.png` → `_3`
- `C:\Users\16648\Desktop\素材\武器1.png` → `Weapon_Sword.png`

Import 参数（用 `unity_execute_code` 调 `TextureImporter`）：

| 素材 | PPU | `spritePivot` |
| --- | --- | --- |
| 角色帧 0（高 719） | 700 | `(0.5, 0.01530)` |
| 角色帧 1（高 696） | 700 | `(0.5, 0.01580)` |
| 角色帧 2（高 737） | 700 | `(0.5, 0.01493)` |
| 角色帧 3（高 708） | 700 | `(0.5, 0.01554)` |
| 武器 | 1735 | `(0.192, 0.467)` |

共同项：`textureType = Sprite`、`spriteImportMode = Single`、`alignment = Custom`、`filterMode = Point`、`spriteMeshType = FullRect`、`alphaIsTransparency = true`、`mipmapEnabled = false`、`wrapMode = Clamp`、`maxTextureSize = 2048`、`textureCompression = Uncompressed`（像素风避免块状压缩伪影，后续若要压包再改）。

- [ ] 复制 5 个 PNG 到目标路径并重命名
- [ ] `unity_execute_code` 设置上表的 import 参数并 `SaveAndReimport`
- [ ] 编译检查无错误；确认 5 个 Sprite 资产可被引用
- [ ] 提交（连同 `.meta`）

### Task 6: 接入玩家移动

**Files:**
- Modify: `Assets/Gameplay/Runtime/PlayerMovement2D.cs`

**Interfaces:**
- Consumes: `CharacterVisual2D.SetMotion(Vector2)`

改动：新增 `[SerializeField] private CharacterVisual2D visual;`；`Update` 中解析输入后调用 `visual.SetMotion(resolvedInput * moveSpeed)`；`visual` 为 null 时跳过。`FixedUpdate` 的 `MovePosition` 不变。

- [ ] 改 `PlayerMovement2D.cs`
- [ ] 编译检查无错误；跑 `PlayerMovement2DTests` 确认未回归
- [ ] 提交

### Task 7: Character_Base 预制体

**Files:**
- Create: `Assets/Gameplay/Prefabs/Character_Base.prefab`

层级（在场景中搭好再存为预制体）：

```
Character_Base                      Rigidbody2D(Dynamic, GravityScale 0, FreezeRotationZ)
                                    BoxCollider2D(size 0.5x0.3, offset 0,0.15)
                                    PlayerMovement2D
  └─ Visual                         SpriteRenderer(sortingOrder 20) + CharacterVisual2D
       └─ MainHand                  AnchorGizmo + WeaponMountSlot
            └─ Weapon               SpriteRenderer(sortingOrder 21, enabled=false)
```

接线：`CharacterVisual2D.spriteRenderer` → `Visual` 的渲染器；`moveFrames` → Task 5 的 4 张 Sprite；`WeaponMountSlot.weaponRenderer` → `Weapon` 的渲染器；`PlayerMovement2D.visual` → `Visual`（预制体内部引用，可随预制体保存）。

同时把 `TrainingSword.asset` 的 `worldSprite` 指向 `Weapon_Sword`。

- [ ] 在 `Gameplay` 下建 `Character_Base` 并补齐层级与组件
- [ ] 设置各字段与引用（含 `TrainingSword.worldSprite`）
- [ ] `unity_asset_create_prefab` 存到 `Assets/Gameplay/Prefabs/Character_Base.prefab`
- [ ] 保存场景，确认预制体内容与层级一致
- [ ] 提交

### Task 8: 场景收尾与重命名

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity` → 重命名为 `Assets/Scenes/BattleScene.unity`
- Create: `Assets/Scenes/` 下的 `PlayerContext` 场景对象（挂 `InventoryHolder`）
- Modify: `ProjectSettings/EditorBuildSettings.asset`
- Modify: `ProjectSettings/ProjectSettings.asset`
- Modify: `Assets/Tests/EditMode/Gameplay/RoomCollisionBoundsTests.cs`

步骤：

- [ ] 删除场景中旧的 `Player_Block`
- [ ] 把 `Character_Base` 预制体实例放在原 `Player_Block` 位置（`-1.5, -4.5`），父节点 `Gameplay`
- [ ] 新建 `PlayerContext` 对象并挂 `InventoryHolder`
- [ ] 接线场景实例：`PlayerMovement2D.joystick` → HUD 的 `Joystick`；`WeaponMountSlot.holder` → `PlayerContext`
- [ ] 用 `AssetDatabase.MoveAsset` 把场景重命名为 `BattleScene.unity`（保留 GUID）
- [ ] 更新 `RoomCollisionBoundsTests` 的 `ScenePath` 常量与测试方法名
- [ ] 检查 `EditorBuildSettings.asset` 与 `ProjectSettings.asset` 中的场景路径已更新，未更新则手改
- [ ] 跑全部 EditMode 测试，确认通过
- [ ] 提交

### Task 9: 实机验证

- [ ] 进入 Play Mode，截图确认角色像素精灵正常显示、不再是白色方块
- [ ] 用 WASD 向右移动，确认朝向为右、4 帧循环播放、脚底不上下跳
- [ ] 向左移动，确认整体镜像、武器换到另一侧
- [ ] 通过 `unity_execute_code` 调 `BagPanelController.Model.TryEquipFromBag` 装备 `TrainingSword`，截图确认手上出现剑；`TryUnequipToBag` 后剑消失
- [ ] 确认与墙体碰撞行为未变
- [ ] 退出 Play Mode，提交验证截图到 `Assets/Screenshots/`
