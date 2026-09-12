# Battle HUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Unity 中基于现有图片资源制作模块化战斗 HUD 预制体，并组装出可直接复用的 `BattleHUD` 总预制体。

**Architecture:** 使用 `UGUI Canvas` 体系组织 HUD。先导入图片资源并统一到 `Assets/UI/BattleHUD/Sprites`，再搭建独立模块预制体，最后组装总 HUD 并在场景中验证层级、锚点和视觉位置。

**Tech Stack:** Unity、UGUI、Unity MCP、PNG Sprite 资源

## Global Constraints

- 素材来源固定为 `C:\Users\16648\Desktop\素材`
- UI 体系固定为 `UGUI Canvas`
- 产物必须同时包含独立模块预制体和整套 `BattleHUD` 预制体
- 优先使用 `Unity MCP` 完成编辑器内操作
- 不改动与 HUD 无关的现有场景内容
- 当前项目不是 Git 仓库，不执行提交步骤

---

### Task 1: 导入素材并建立目录结构

**Files:**
- Create: `Assets/UI/BattleHUD/`
- Create: `Assets/UI/BattleHUD/Sprites/`
- Create: `Assets/UI/BattleHUD/Prefabs/`
- Create: `Assets/UI/BattleHUD/Prefabs/Modules/`

**Interfaces:**
- Consumes: 桌面素材目录中的 PNG 文件
- Produces: Unity 内可用的 UI Sprite 资源路径

- [ ] **Step 1: 创建 UI 目录**

在 Unity 项目内建立 HUD 资源目录，确保后续导入路径稳定。

- [ ] **Step 2: 导入图片资源**

导入以下文件到 `Assets/UI/BattleHUD/Sprites/`：
- `状态.png`
- `背包按钮.png`
- `移动底盘.png`
- `移动中心.png`
- `攻击.png`
- `切换攻击.png`

- [ ] **Step 3: 校验导入结果**

检查导入后的资源在 Unity 内可见，且后续可作为 UI 图片使用。

### Task 2: 创建独立模块预制体

**Files:**
- Create: `Assets/UI/BattleHUD/Prefabs/Modules/StatusPanel.prefab`
- Create: `Assets/UI/BattleHUD/Prefabs/Modules/BagButton.prefab`
- Create: `Assets/UI/BattleHUD/Prefabs/Modules/Joystick.prefab`
- Create: `Assets/UI/BattleHUD/Prefabs/Modules/AttackSwitchButton.prefab`
- Create: `Assets/UI/BattleHUD/Prefabs/Modules/AttackButton.prefab`

**Interfaces:**
- Consumes: `Assets/UI/BattleHUD/Sprites/*.png`
- Produces: 可独立拖入场景的 HUD 模块预制体

- [ ] **Step 1: 制作 `StatusPanel`**

创建使用 `状态.png` 的左上状态区模块。

- [ ] **Step 2: 制作 `BagButton`**

创建使用 `背包按钮.png` 的右上按钮模块。

- [ ] **Step 3: 制作 `Joystick`**

创建根节点 `Joystick`，并在其下创建 `Base` 和 `Handle`，分别绑定 `移动底盘.png` 与 `移动中心.png`。

- [ ] **Step 4: 制作 `AttackSwitchButton`**

创建使用 `切换攻击.png` 的切换按钮模块。

- [ ] **Step 5: 制作 `AttackButton`**

创建使用 `攻击.png` 的主攻击按钮模块。

### Task 3: 组装 BattleHUD 总预制体

**Files:**
- Create: `Assets/UI/BattleHUD/Prefabs/BattleHUD.prefab`

**Interfaces:**
- Consumes: 所有模块预制体
- Produces: 一套完整的战斗 HUD 预制体

- [ ] **Step 1: 创建总根节点**

创建 `BattleHUD` 根节点并放入合适的 Canvas 层级下。

- [ ] **Step 2: 组装模块**

将 `StatusPanel`、`BagButton`、`Joystick`、`AttackSwitchButton`、`AttackButton` 按参考图位置布置到总 HUD 中。

- [ ] **Step 3: 调整锚点与尺寸**

对每个模块设置合适的 `RectTransform` 锚点、位置、缩放和尺寸，使布局接近参考图。

### Task 4: 场景验证

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`

**Interfaces:**
- Consumes: `BattleHUD.prefab`
- Produces: 可在现有场景中直接查看的 HUD 实例

- [ ] **Step 1: 打开验证场景**

打开 `Assets/Scenes/SampleScene.unity`。

- [ ] **Step 2: 放置 HUD**

将 `BattleHUD` 放入场景中，保证运行时可见。

- [ ] **Step 3: 检查结果**

检查层级、资源引用、屏幕位置和基础可见性。
