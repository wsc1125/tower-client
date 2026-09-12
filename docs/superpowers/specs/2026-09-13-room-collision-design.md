# 房间人物与碰撞场景设计

## 目标

在现有 `Room_Battle_Closed_01` 预制体基础上，完成一个可直接运行的测试场景，支持：

- 使用 `WASD` 与现有虚拟摇杆共同驱动玩家方块移动
- 玩家与房间墙体产生 2D 实体碰撞，不能穿出封闭房间
- 玩家与敌人方块产生 2D 实体碰撞，验证战斗空间占位
- 为后续 `InnerBlocks` 障碍物、敌人 AI、传送触发器保留扩展位

## 架构

本次采用 Unity 2D 的 `Rigidbody2D + Collider2D` 方案，不引入自定义 Cast 或 Transform 穿模修正逻辑。

- 房间负责边界与未来障碍碰撞
- 玩家负责输入聚合与位移
- 敌人负责最小占位与巡逻验证
- 场景继续复用现有 `BattleHUD` 中的 `VirtualJoystickController`

## 场景结构

`Assets/Scenes/SampleScene.unity` 内保留现有房间与 HUD，并新增：

- `Gameplay`
  - `Player_Block`
  - `Enemy_Block`

其中：

- `Player_Block`
  - `SpriteRenderer`
  - `Rigidbody2D` (`Dynamic`, `Gravity Scale = 0`, `Freeze Rotation Z`)
  - `BoxCollider2D`
  - `PlayerMovement2D`
  - `DebugBlockVisual`
- `Enemy_Block`
  - `SpriteRenderer`
  - `Rigidbody2D` (`Kinematic`, `Gravity Scale = 0`, `Freeze Rotation Z`)
  - `BoxCollider2D`
  - `EnemyPatrol2D`
  - `DebugBlockVisual`

房间预制体中的 `Walls` 下四面墙对象将补充 `BoxCollider2D`；`InnerBlocks` 维持空节点，用于未来添加障碍碰撞体。

## 输入设计

玩家输入由 `PlayerMovement2D` 聚合：

- 键盘输入：`Horizontal` / `Vertical`
- 摇杆输入：读取场景中的 `VirtualJoystickController.Input`

聚合规则：

1. 先读取键盘二维向量
2. 再读取摇杆二维向量
3. 两者相加后做 `ClampMagnitude(1)`
4. 在 `FixedUpdate` 中通过 `Rigidbody2D.MovePosition` 推进位移

这样可以同时满足编辑器键盘调试与移动端 HUD 联调。

## 碰撞矩阵

- `Player_Block` vs `Walls`: 实体阻挡
- `Player_Block` vs `InnerBlocks`: 实体阻挡
- `Player_Block` vs `Enemy_Block`: 实体阻挡
- `Enemy_Block` vs `Walls`: 实体阻挡
- `Enemy_Block` vs `InnerBlocks`: 实体阻挡

本阶段不加入触发器伤害、击退、传送、无敌帧等逻辑。

## 敌人行为

敌人采用最小验证版本：

- 使用 `EnemyPatrol2D` 在两个点之间往返移动
- 运动方式同样走 `Rigidbody2D.MovePosition`
- 只负责证明“移动中的敌人与玩家、墙体可稳定共存”

如果后续需要静止敌人，可将速度设为 `0` 或关闭脚本。

## 可视化方案

由于当前仅需验证逻辑，不额外导入角色美术资源。

- `DebugBlockVisual` 以运行时生成的纯色方块 Sprite 作为调试外观
- 玩家使用偏青色
- 敌人使用偏红色

这样可以避免为了原型阶段额外维护图片素材，同时保持场景内可见性。

## 测试策略

编辑器测试覆盖两类核心行为：

1. `PlayerMovement2D` 输入聚合逻辑
   - 仅键盘输入
   - 仅摇杆输入
   - 键盘与摇杆叠加后被正确归一化
2. `EnemyPatrol2D` 巡逻端点切换逻辑
   - 抵达目标点后会反向

场景验证覆盖：

- 进入 Play Mode 后玩家可移动
- 玩家无法穿墙
- 玩家与敌人接触时被阻挡
- 敌人巡逻时不会飞出房间

## 约束

- 不改动现有 HUD 视觉布局
- 不破坏现有房间预制体层级命名
- 优先新增脚本与组件，不重构现有 UI 脚本
- 仅实现最小可运行战斗房间移动碰撞闭环
