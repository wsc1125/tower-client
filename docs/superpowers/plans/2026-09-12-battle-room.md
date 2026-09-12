# Battle Room Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Unity 项目中导入现有墓室素材，创建可复用的 `Room_Battle_Closed_01` 封闭战斗房间 Prefab，并放入场景进行视觉验证。

**Architecture:** 采用单体房间 Prefab 方案。先把桌面素材复制到项目内稳定目录，再在 Unity 中创建封闭战斗房间根节点和标准子层级，随后把地面、墙体、墙角素材挂到对应 `SpriteRenderer` 上，最后在 `SampleScene` 中实例化并检查拼接与层级。

**Tech Stack:** Unity 2022.3、SpriteRenderer、Unity MCP、PowerShell、PNG 素材

## Global Constraints

- 素材来源固定为 `C:\Users\16648\Desktop\素材`
- 房间 Prefab 名称固定为 `Room_Battle_Closed_01`
- 房间节点命名统一使用英文：`Floor`、`Walls`、`Corners`、`InnerBlocks`、`Decoration`、`VFX`、`Debug`
- 第一版必须是完全封闭的战斗房间，不包含门、门框、出口开口
- 第一版不包含传送点、房间切换逻辑、敌人出生点、玩家出生点、摄像机边界点
- 第一版不包含碰撞体、导航、战斗逻辑脚本
- 必须优先使用 Unity MCP 完成编辑器内对象创建、材质挂载和场景验证
- 产物必须包含项目内可复用的 Prefab 和 `Assets/Scenes/SampleScene.unity` 中的验证实例

---

## File Structure

- Create: `Assets/Environment/BattleRoom/`
- Create: `Assets/Environment/BattleRoom/Sprites/`
- Create: `Assets/Environment/BattleRoom/Prefabs/`
- Create: `Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab`
- Modify: `Assets/Scenes/SampleScene.unity`

目录职责：

- `Assets/Environment/BattleRoom/Sprites/`：保存房间地面、墙体、墙角素材
- `Assets/Environment/BattleRoom/Prefabs/`：保存房间 Prefab
- `Assets/Scenes/SampleScene.unity`：用于挂入房间实例并做视觉验证

### Task 1: 导入房间素材并建立资源目录

**Files:**
- Create: `Assets/Environment/BattleRoom/`
- Create: `Assets/Environment/BattleRoom/Sprites/`
- Create: `Assets/Environment/BattleRoom/Prefabs/`
- Create: `Assets/Environment/BattleRoom/Sprites/大底图.png`
- Create: `Assets/Environment/BattleRoom/Sprites/直墙体.png`
- Create: `Assets/Environment/BattleRoom/Sprites/竖墙体.png`
- Create: `Assets/Environment/BattleRoom/Sprites/左上墙角.png`
- Create: `Assets/Environment/BattleRoom/Sprites/右上墙角.png`
- Create: `Assets/Environment/BattleRoom/Sprites/左下墙角.png`
- Create: `Assets/Environment/BattleRoom/Sprites/右下墙角.png`

**Interfaces:**
- Consumes: `C:\Users\16648\Desktop\素材\大底图.png`、`直墙体.png`、`竖墙体.png`、`左上墙角.png`、`右上墙角.png`、`左下墙角.png`、`右下墙角.png`
- Produces: Unity 项目内可被 `SpriteRenderer` 引用的房间素材路径 `Assets/Environment/BattleRoom/Sprites/*.png`

- [ ] **Step 1: 创建房间资源目录**

Run:
```powershell
New-Item -ItemType Directory -Force "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites"
New-Item -ItemType Directory -Force "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Prefabs"
```

Expected: 终端返回新目录路径，Unity 项目中出现 `Assets/Environment/BattleRoom/` 结构。

- [ ] **Step 2: 复制房间素材到项目目录**

Run:
```powershell
Copy-Item "c:\Users\16648\Desktop\素材\大底图.png" "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites\大底图.png"
Copy-Item "c:\Users\16648\Desktop\素材\直墙体.png" "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites\直墙体.png"
Copy-Item "c:\Users\16648\Desktop\素材\竖墙体.png" "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites\竖墙体.png"
Copy-Item "c:\Users\16648\Desktop\素材\左上墙角.png" "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites\左上墙角.png"
Copy-Item "c:\Users\16648\Desktop\素材\右上墙角.png" "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites\右上墙角.png"
Copy-Item "c:\Users\16648\Desktop\素材\左下墙角.png" "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites\左下墙角.png"
Copy-Item "c:\Users\16648\Desktop\素材\右下墙角.png" "c:\Users\16648\Desktop\My project (3)\Assets\Environment\BattleRoom\Sprites\右下墙角.png"
```

Expected: 7 张 PNG 文件全部出现在项目目录中。

- [ ] **Step 3: 等待 Unity 导入并确认素材存在**

Run in Unity MCP:
```text
使用 unity_asset_list 或 unity_search_assets，确认以下路径都可见：
Assets/Environment/BattleRoom/Sprites/大底图.png
Assets/Environment/BattleRoom/Sprites/直墙体.png
Assets/Environment/BattleRoom/Sprites/竖墙体.png
Assets/Environment/BattleRoom/Sprites/左上墙角.png
Assets/Environment/BattleRoom/Sprites/右上墙角.png
Assets/Environment/BattleRoom/Sprites/左下墙角.png
Assets/Environment/BattleRoom/Sprites/右下墙角.png
```

Expected: Unity 内可以看到全部素材资源，后续能被 `SpriteRenderer` 选择。

- [ ] **Step 4: 提交本任务**

Run:
```bash
git add "Assets/Environment/BattleRoom" 
git commit -m "$(cat <<'EOF'
feat: import battle room source sprites
EOF
)"
```

Expected: 产生一个仅包含房间素材目录的提交。

### Task 2: 创建房间 Prefab 骨架

**Files:**
- Create: `Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab`

**Interfaces:**
- Consumes: Task 1 导入完成的房间素材目录结构
- Produces: 标准层级的 `Room_Battle_Closed_01.prefab`，包含 `Floor`、`Walls`、`Corners`、`InnerBlocks`、`Decoration`、`VFX`、`Debug`

- [ ] **Step 1: 在场景中创建房间根节点**

Run in Unity MCP:
```text
创建根对象 `Room_Battle_Closed_01`，Transform 位置设为 (0,0,0)，缩放设为 (1,1,1)。
```

Expected: Hierarchy 中出现 `Room_Battle_Closed_01`。

- [ ] **Step 2: 创建一级容器节点**

Run in Unity MCP:
```text
在 `Room_Battle_Closed_01` 下创建 7 个空子节点：
Floor
Walls
Corners
InnerBlocks
Decoration
VFX
Debug
```

Expected: 一级层级与设计文档一致。

- [ ] **Step 3: 创建必须的二级节点**

Run in Unity MCP:
```text
在 `Floor` 下创建 `Floor_Base`
在 `Walls` 下创建 `Wall_Top`、`Wall_Bottom`、`Wall_Left`、`Wall_Right`
在 `Corners` 下创建 `Corner_TL`、`Corner_TR`、`Corner_BL`、`Corner_BR`
```

Expected: 根节点下总共形成 1 个地面节点、4 个墙体节点、4 个墙角节点。

- [ ] **Step 4: 给可视节点挂 SpriteRenderer**

Run in Unity MCP:
```text
为以下 9 个节点添加 SpriteRenderer：
Floor_Base
Wall_Top
Wall_Bottom
Wall_Left
Wall_Right
Corner_TL
Corner_TR
Corner_BL
Corner_BR
```

Expected: Hierarchy 中 9 个节点均具备 SpriteRenderer，其他容器仍为空节点。

- [ ] **Step 5: 保存为 Prefab**

Run in Unity MCP:
```text
将 `Room_Battle_Closed_01` 保存为 `Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab`
```

Expected: 项目中出现可复用 Prefab 文件。

- [ ] **Step 6: 提交本任务**

Run:
```bash
git add "Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab" "Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab.meta"
git commit -m "$(cat <<'EOF'
feat: add closed battle room prefab skeleton
EOF
)"
```

Expected: 产生包含房间骨架 prefab 的提交。

### Task 3: 挂载素材并整理渲染顺序

**Files:**
- Modify: `Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab`

**Interfaces:**
- Consumes: Task 1 的素材资源、Task 2 的房间骨架 prefab
- Produces: 已挂载地面、墙体、墙角素材并设置渲染顺序的完整房间 prefab

- [ ] **Step 1: 按映射关系挂载 Sprite**

Run in Unity MCP:
```text
设置 SpriteRenderer.sprite：
Floor_Base -> Assets/Environment/BattleRoom/Sprites/大底图.png
Wall_Top -> Assets/Environment/BattleRoom/Sprites/直墙体.png
Wall_Bottom -> Assets/Environment/BattleRoom/Sprites/直墙体.png
Wall_Left -> Assets/Environment/BattleRoom/Sprites/竖墙体.png
Wall_Right -> Assets/Environment/BattleRoom/Sprites/竖墙体.png
Corner_TL -> Assets/Environment/BattleRoom/Sprites/左上墙角.png
Corner_TR -> Assets/Environment/BattleRoom/Sprites/右上墙角.png
Corner_BL -> Assets/Environment/BattleRoom/Sprites/左下墙角.png
Corner_BR -> Assets/Environment/BattleRoom/Sprites/右下墙角.png
```

Expected: 9 个可视节点全部显示正确素材。

- [ ] **Step 2: 设置排序层级**

Run in Unity MCP:
```text
将所有 SpriteRenderer 的 Sorting Layer 设为 Default。
将 Order in Layer 设为：
Floor_Base = 0
Wall_Top = 10
Wall_Bottom = 10
Wall_Left = 10
Wall_Right = 10
Corner_TL = 10
Corner_TR = 10
Corner_BL = 10
Corner_BR = 10
```

Expected: 地面处于底层，墙体和墙角稳定覆盖在房间边缘。

- [ ] **Step 3: 调整局部位置形成封闭房间**

Run in Unity MCP:
```text
以 Floor_Base 为基准，将 Wall_Top、Wall_Bottom、Wall_Left、Wall_Right 分别移动到地面边缘。
再将四个 Corner_* 调整到四角，确保视觉上闭合，没有明显门洞和角部缺口。
所有调整均以 `Room_Battle_Closed_01` 根节点为坐标基准完成。
```

Expected: 在 Scene 视图中看到一个完整封闭的战斗房间轮廓。

- [ ] **Step 4: 保存 Prefab 覆盖修改**

Run in Unity MCP:
```text
Apply `Room_Battle_Closed_01` 的位置与 SpriteRenderer 修改到 Prefab 资源。
```

Expected: 重新拖入 prefab 时仍保持相同的素材与位置。

- [ ] **Step 5: 提交本任务**

Run:
```bash
git add "Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab"
git commit -m "$(cat <<'EOF'
feat: assemble closed battle room visuals
EOF
)"
```

Expected: 产生包含素材挂载与摆放结果的提交。

### Task 4: 场景验证与截图留档

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity`
- Create: `Assets/Screenshots/battle_room_scene.png`
- Create: `Assets/Screenshots/battle_room_game.png`

**Interfaces:**
- Consumes: Task 3 完成的 `Room_Battle_Closed_01.prefab`
- Produces: `SampleScene` 中的房间实例，以及用于回看验证的场景截图

- [ ] **Step 1: 打开验证场景**

Run in Unity MCP:
```text
打开 `Assets/Scenes/SampleScene.unity`
```

Expected: 当前激活场景为 `SampleScene`。

- [ ] **Step 2: 将房间 Prefab 实例化到场景**

Run in Unity MCP:
```text
把 `Assets/Environment/BattleRoom/Prefabs/Room_Battle_Closed_01.prefab` 实例化到场景原点附近。
如果场景中已有旧测试对象，避免覆盖 HUD 与相机，仅新增房间实例。
```

Expected: `SampleScene` 中出现一个可见的房间实例。

- [ ] **Step 3: 调整相机或房间位置到可观察范围**

Run in Unity MCP:
```text
保持 Main Camera 现有职责不变，优先移动房间实例到相机能看到的位置；
只有在房间无法观察时才小幅调整相机，且不要破坏现有 HUD 验证布局。
```

Expected: Scene 和 Game 视图都能直接看到房间主体。

- [ ] **Step 4: 采集验证截图**

Run in Unity MCP:
```text
保存 Scene 视图截图到 `Assets/Screenshots/battle_room_scene.png`
保存 Game 视图截图到 `Assets/Screenshots/battle_room_game.png`
```

Expected: 项目内新增两张房间截图，便于后续对比和回归。

- [ ] **Step 5: 手动核对验收标准**

检查以下条件：
```text
1. Prefab 名称为 Room_Battle_Closed_01
2. 层级包含 Floor/Walls/Corners/InnerBlocks/Decoration/VFX/Debug
3. 地面、四边墙体、四角墙角素材全部可见
4. 房间完全封闭，没有门洞
5. 空容器层仍然保留
6. SampleScene 中可见该房间实例
```

Expected: 6 条全部满足。

- [ ] **Step 6: 提交本任务**

Run:
```bash
git add "Assets/Scenes/SampleScene.unity" "Assets/Screenshots/battle_room_scene.png" "Assets/Screenshots/battle_room_scene.png.meta" "Assets/Screenshots/battle_room_game.png" "Assets/Screenshots/battle_room_game.png.meta"
git commit -m "$(cat <<'EOF'
feat: validate closed battle room in sample scene
EOF
)"
```

Expected: 产生包含场景验证与截图留档的提交。
