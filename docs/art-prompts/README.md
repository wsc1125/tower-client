# 敌人美术提示词包

来源：`美术素材清单.md`（已修订版）。本目录把每个单位的提示词拆成**可直接整段复制**的独立文件。

## 文件索引

| 文件 | 单位 | 目标目录 | 帧数 |
|---|---|---|---|
| `00_prefix_A.txt` | 全局前缀 A（角色与敌人共用） | — | — |
| `01_Rusher.txt` | 冲锋兵 Rusher | `Assets/Gameplay/Art/Enemies/Rusher/` | 14 |
| `02_Spawner.txt` | 母巢 Spawner | `Assets/Gameplay/Art/Enemies/Spawner/` | 13 |
| `03_Artillery.txt` | 炮台 Artillery | `Assets/Gameplay/Art/Enemies/Artillery/` | 15 |
| `04_Splitter.txt` | 分裂体 Splitter | `Assets/Gameplay/Art/Enemies/Splitter/` | 14 |
| `05_Swarm.txt` | 虫群 Swarm | `Assets/Gameplay/Art/Enemies/Swarm/` | 4 |
| `06_SplitterSmall.txt` | 分裂小体 | `Assets/Gameplay/Art/Enemies/SplitterSmall/` | 3 |
| `07_Boss.txt` | BOSS 机械巢母 | `Assets/Gameplay/Art/Enemies/Boss/` | 32 |
| `08_SpacePirate_rework.txt` | 太空海盗（返工） | `Assets/Gameplay/Art/Enemies/SpacePirate/` | 21 |
| `09_Armor_Sets.txt` | 护甲套装 ×4（普通/精良/稀有/史诗） | `Assets/Gameplay/Art/Armor/<套装名>/` | 12 |
| `10_Consumables.txt` | 消耗品 ×2（血包 / 甲修） | `Assets/Gameplay/Art/Items/` | 2 |

合计新增 **95 帧**（敌人）+ **12 件**（护甲）+ **2 件**（消耗品）。

## 统一生成流程

1. **① 原型图**：每个文件都有一段「① 原型图」，先生成 1 张确认造型与配色。不满意只重跑这一张，成本最低。
2. **② 雪碧图**：造型定稿后，**把原型图作为参考图**，再整段复制「② 雪碧图」生成 1 张。图生图比文生图更能保持角色一致。
3. **③ 切分与导入**：按文件末尾的切分表切成单帧 PNG，再设导入参数。

> ⚠️ **一个形象 = 一张雪碧图。** 所有帧必须一次画在同一张图上，禁止按动画拆成多张，否则各张之间比例/配色会漂移。

## 统一导入参数

- Filter Mode：**Point**（禁止 Bilinear）
- Mip Maps：**关闭**
- Alpha Is Transparency：**开启**
- 不缩放，生成即 1:1 导入
- 轴点：**悬浮/无腿用中心 (0.5,0.5)**；**直立/有腿用脚底**（参考 SpacePirate `(0.5,0.2)`）
- PPU：按「**本体像素高 ÷ 目标世界高**」计算，不要照搬固定值
- 敌人本体生成高度约 **300 像素**
- 背景：纯色洋红 **#FF00FF**

## 画风三条硬指标（生成后自检）

| 指标 | 目标 |
|---|---|
| 轮廓带明度 / 本体明度 | ≤ 0.25 |
| 半透明边缘像素占比 | ≤ 12% |
| 有效像素密度 | ≥ 220 px/单位 |

生成时始终上传 **MechJelly** 作为参考图，并明确要求「保持参考图视角与朝向不变」。

## 敌人配色系统（详见 `美术素材清单.md §2.1.2`）

- **族群定色相**：装甲机械=低饱和钢蓝灰 / 胶质生物=品红 / 巢群=酸绿 / 核心=BOSS 玄紫
- **明度与饱和定威胁档**：T1 普通 / T2 精英 / T3 BOSS
- **预警色独立留用**：普通与精英=高饱和黄 `#FFE600`；BOSS=高饱和红 `#FF2D2D`；均禁止用于身体
- **R1**：敌人白/浅灰占比 ≤ **25%**（玩家基准 32.8%），且不得以白/浅灰为最大类别

## 工具（`docs/art-prompts/tools/`）

| 脚本 | 用途 |
|---|---|
| `CheckPalette.ps1` | **配色合规检查**：白/浅灰占比、最大类别、主色相、高饱和黄/红像素 |
| `AnalyzePalette.ps1` | 粗分：主色与平均色 |
| `ShowAscii.ps1` | 终端 ASCII 预览，快速判断素材明暗分布与是否白主体 |

用法：

```
powershell -File docs/art-prompts/tools/CheckPalette.ps1 <png> [<png> ...]
```

## 注意

以下行为目前**没有代码支持**，相关帧可先跳过（详见 `美术素材清单.md` §9）：
- Rusher 的 `Dash_01~03`（无 Dash 状态）
- Spawner 召唤、Splitter 死亡分裂、接触伤害
- Boss `Attack_B`（需 Boss 独立组件切换）
