# 封闭战斗房间 Prefab 设计

**日期：** 2026-09-12

**目标：** 为当前 Unity 2D 俯视太空探索项目设计并创建一个第一版可复用的封闭式战斗房间 Prefab，优先解决房间结构、层级命名和素材挂载问题，为后续碰撞、传送、敌人刷新和装饰扩展提供稳定骨架。

## 背景

当前项目已经具备 Battle HUD 相关 UI 预制体与场景基础，但还没有房间、舱室或 Tilemap 相关模块。用户决定第一版不做门框和门位系统，改为先制作一个完全封闭的战斗房间，未来再通过传送点、切场景或触发器来实现房间切换。

用户已准备好房间相关美术素材，素材位于桌面路径 `C:\Users\16648\Desktop\素材`，其中已确认可用于当前房间结构的图片包括：

- `大底图.png`
- `直墙体.png`
- `竖墙体.png`
- `左上墙角.png`
- `右上墙角.png`
- `左下墙角.png`
- `右下墙角.png`

## 范围

### 本次要做

- 创建一个单体房间 Prefab
- 使用现有素材搭建封闭式战斗房间
- 明确房间的节点层级、命名规范和素材映射关系
- 为后续障碍物、装饰、特效保留扩展容器
- 让该 Prefab 可以被直接拖入场景进行视觉验证

### 本次不做

- 不做门、门框、出口开口
- 不做传送点、房间切换逻辑
- 不做敌人出生点、玩家出生点、摄像机边界点
- 不做碰撞体、导航、战斗逻辑脚本
- 不做程序化拼房或随机房间生成

## 方案选择

本次采用 **方案 A：单体房间 Prefab**。

选择原因：

- 当前项目还处于房间系统的起步阶段，单体 Prefab 最快落地
- 用户已经明确第一版只需要确定房间构成并开始创建，不需要同时引入模块化门系统或 Tilemap 体系
- 通过把层级结构先设计规范，后续仍然可以平滑扩展到障碍物模块、传送点或更复杂的房型

不采用其他方案的原因：

- `骨架 + 子模块` 方案会让第一版拆分过细，增加编辑成本
- `Tilemap 优先` 方案虽然利于后续批量扩展，但会提前引入不必要的复杂度

## Prefab 命名

- 第一版房间 Prefab 名称：`Room_Battle_Closed_01`

命名约定：

- 房间根节点和子节点统一使用英文命名
- 房间变体可按 `Room_Battle_Closed_XX` 延续
- 方向命名统一使用 `Top / Bottom / Left / Right`
- 角落命名统一使用 `TL / TR / BL / BR`

## Prefab 层级结构

```text
Room_Battle_Closed_01
├─ Floor
│  └─ Floor_Base
├─ Walls
│  ├─ Wall_Top
│  ├─ Wall_Bottom
│  ├─ Wall_Left
│  └─ Wall_Right
├─ Corners
│  ├─ Corner_TL
│  ├─ Corner_TR
│  ├─ Corner_BL
│  └─ Corner_BR
├─ InnerBlocks
├─ Decoration
├─ VFX
└─ Debug
```

## 节点职责

### 1. Floor

`Floor` 负责房间地面视觉承载，第一版只放一个 `Floor_Base` 节点，挂载主地面底图。它只负责地面表现，不承担墙体、遮挡、门位或装饰职责。

### 2. Walls

`Walls` 负责四面封闭墙体结构：

- `Wall_Top`
- `Wall_Bottom`
- `Wall_Left`
- `Wall_Right`

这样拆分的目的是让后续替换素材、微调位置、扩展局部破损版本都更容易，而不是把四边混成一张整图。

### 3. Corners

`Corners` 负责四个墙角的独立拼接：

- `Corner_TL`
- `Corner_TR`
- `Corner_BL`
- `Corner_BR`

墙角单独成层可以避免直墙素材和角块硬拼时出现边缘接缝，也更利于太空舱室风格的包角表现。

### 4. InnerBlocks

`InnerBlocks` 是内部结构占位层。第一版保持为空节点，后续可以在其中挂载集装箱、控制台、能量屏障、障碍物等战斗阻挡内容。该层预留是为了让房间骨架在第一版创建完成后无需重新整理层级。

### 5. Decoration

`Decoration` 是纯装饰层，第一版保持为空节点，未来用于放置冷光灯、管线、地板细节、舷窗、舱室陈设等内容。

### 6. VFX

`VFX` 是后续特效层，第一版保持为空节点，未来用于雾气、火光、粒子灰尘等表现。

### 7. Debug

`Debug` 用于开发期辅助内容，第一版可为空节点，后续用于边界辅助线、占位标记、中心点或可视化调试对象。

## 组件要求

第一版必须挂 `SpriteRenderer` 的节点：

- `Floor/Floor_Base`
- `Walls/Wall_Top`
- `Walls/Wall_Bottom`
- `Walls/Wall_Left`
- `Walls/Wall_Right`
- `Corners/Corner_TL`
- `Corners/Corner_TR`
- `Corners/Corner_BL`
- `Corners/Corner_BR`

第一版只保留为空节点容器的节点：

- `InnerBlocks`
- `Decoration`
- `VFX`
- `Debug`

## 素材映射

已确认的素材与节点映射如下：

- `Floor_Base` ← `大底图.png`
- `Wall_Top` ← `直墙体.png`
- `Wall_Bottom` ← `直墙体.png`
- `Wall_Left` ← `竖墙体.png`
- `Wall_Right` ← `竖墙体.png`
- `Corner_TL` ← `左上墙角.png`
- `Corner_TR` ← `右上墙角.png`
- `Corner_BL` ← `左下墙角.png`
- `Corner_BR` ← `右下墙角.png`

## 渲染顺序

第一版统一使用 `Sorting Layer = Default`，通过 `Order in Layer` 做层次控制：

- `Floor`：`0`
- `InnerBlocks`：`5`
- `Walls`：`10`
- `Corners`：`10`
- `Decoration`：`15`
- `VFX`：`20`
- `Debug`：`100`

这样可以保证：

- 地面在最底层
- 未来内部障碍物处于地面之上、墙体之下或同层可调
- 墙体和墙角形成稳定外框
- 调试层不会被其他内容遮挡

## 空间组织原则

为了降低后续返工，第一版创建时遵循以下原则：

- 以 `Floor_Base` 作为房间视觉基准
- `Walls` 围绕地面边缘摆放，保持房间完全封闭
- `Corners` 优先用于修补四角拼接处
- 所有子节点局部坐标相对于 `Room_Battle_Closed_01` 根节点整理
- 不通过临时命名或杂乱层级来完成拼装

## 验收标准

当以下条件全部满足时，第一版设计视为达成：

- 项目中存在可复用的 `Room_Battle_Closed_01` Prefab
- Prefab 层级符合本设计文档
- 地面、四边墙体、四角墙角素材均已正确挂载
- 房间视觉上为封闭式战斗房，没有门洞或门框残留结构
- `InnerBlocks`、`Decoration`、`VFX`、`Debug` 作为空容器保留
- Prefab 可被拖入场景并进行视觉检查

## 风险与后续扩展

### 当前风险

- `大底图` 与墙体、墙角素材在实际场景中的透视和边缘贴合关系，需要通过实际摆放后微调
- 墙角和直墙素材可能存在轻微尺寸差异，创建时需要以视觉一致性为准做局部调整

### 后续扩展方向

- 为 `InnerBlocks` 增加内部障碍布局
- 为房间增加传送点或战斗结束出口点
- 补充碰撞层与阻挡区
- 拓展多个封闭战斗房变体
- 将单体房间过渡为更高层级的房间系统

## 实施摘要

实际创建阶段按以下顺序执行：

1. 将房间素材导入项目中的房间资源目录
2. 创建 `Room_Battle_Closed_01`
3. 创建并整理房间子层级
4. 挂载地面、墙体、墙角素材
5. 调整相对位置与渲染顺序
6. 保存为 Prefab 并拖入场景检查视觉效果

本设计明确了第一版房间 prefab 的边界：它是一个封闭、可复用、可扩展的战斗房间骨架，而不是完整的房间系统。
