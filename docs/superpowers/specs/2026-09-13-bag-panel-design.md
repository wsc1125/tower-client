# 背包面板设计（Battle Bag Panel）

**日期：** 2026-09-13
**范围：** 子项目① —— 战斗内背包面板（数据层 + UI + 拖拽穿脱）
**后续：** 子项目② —— 世界掉落物与拾取（本设计为其预留数据接口，不实现）

---

## 1. 目标

战斗中点击 HUD 右上角「背包按钮」弹出一个小面板，面板打开时**游戏不暂停**。面板内分为两部分：左侧角色装备区、右侧背包格子区。玩家可通过拖拽把物品穿到装备槽，或从装备槽卸回背包。

## 2. 范围

**本期做：**
- 物品定义（ScriptableObject）
- 背包数据容器（20 格、不可叠加）与装备槽数据
- 背包面板 UI 与开关逻辑
- 拖拽穿戴 / 卸下 / 背包内换位
- EditMode 单元测试

**本期不做（明确排除）：**
- 世界掉落物生成与拾取交互（子项目②）
- 装备对战斗数值（血量/攻击/防御）的影响
- 背包内容存档持久化
- 物品的使用、丢弃、分解
- 物品叠加/堆叠

## 3. 装备槽定义

共 5 个槽位：

| 槽位 | 枚举值 | 本期状态 | 行为 |
|---|---|---|---|
| 近战武器 | `MeleeWeapon` | 可用 | 只接受 `equipSlotType == MeleeWeapon` 的物品 |
| 远程武器 | `RangedWeapon` | 可用 | 只接受 `equipSlotType == RangedWeapon` 的物品 |
| 头盔 | `Helmet` | 预留 | 显示底图与类型剪影，拒绝任何拖入，且恒为空 |
| 身甲 | `BodyArmor` | 预留 | 同上 |
| 鞋子 | `Boots` | 预留 | 同上 |

**槽位校验规则：** 物品拖到不匹配的槽位 → 拒绝，图标动画/直接归位，数据不变。预留槽位拖入一律拒绝。

## 4. 数据层设计

数据层为**纯 C#**，不引用任何 UI 类型，可脱离 Unity 运行时做单元测试。

### 4.1 装配归属

新建 `Assets/Inventory/Inventory.asmdef`，仅引用 UnityEngine（无 UI 依赖）：
- `BattleHUD` 装配引用 `Inventory`（面板 UI 需要读数据）
- 将来的 `Gameplay` 装配（掉落拾取）也引用 `Inventory`

这样纯逻辑/数据不反向依赖 UI 装配。

### 4.2 `EquipmentSlotType`（枚举）

```csharp
public enum EquipmentSlotType
{
    None = 0,
    MeleeWeapon = 1,
    RangedWeapon = 2,
    Helmet = 3,
    BodyArmor = 4,
    Boots = 5,
}
```

### 4.3 `ItemDefinition : ScriptableObject`

| 字段 | 类型 | 说明 |
|---|---|---|
| `displayName` | string | 物品名，占位期用于在格子里显示前两字 |
| `icon` | Sprite | 物品图标，可为空 |
| `equipSlotType` | `EquipmentSlotType` | 可装备到的槽位；`None` 表示不可装备 |

不可叠加，因此**没有数量字段**。

### 4.4 `Inventory`（普通 C# 类，非 MonoBehaviour）

| 成员 | 说明 |
|---|---|
| `const int BagCapacity = 20` | 固定 20 格 |
| `ItemDefinition[] bag` | 长度 20，`null` 表示空格 |
| `Dictionary<EquipmentSlotType, ItemDefinition>` 已装备表 | 键为 5 个槽位，值为 `null` 表示空 |
| `event Action Changed` | 任一数据变更后触发，UI 据此刷新 |

**接口：**

| 方法 | 行为 | 失败条件 |
|---|---|---|
| `bool TryAdd(ItemDefinition item)` | 放入第一个空格 | 背包已满 → 返回 false |
| `bool TryEquipFromBag(int bagIndex)` | 把背包第 N 格物品穿到它对应的槽位 | 索引越界 / 该格为空 / `equipSlotType` 为 `None` / `IsSlotUsable` 为 false / 该槽已被占用（本期不回退旧装备，直接拒绝） |
| `bool TryUnequip(EquipmentSlotType slot)` | 把槽内装备卸回第一个空格 | 槽为空 / 槽为预留槽 / 背包已满 |
| `bool TrySwapInBag(int a, int b)` | 交换背包两格 | 索引越界 |
| `bool IsSlotUsable(EquipmentSlotType slot)` | 仅 `MeleeWeapon` / `RangedWeapon` 返回 true | — |

**边界规则：**
- 背包满时 `TryUnequip` 拒绝，装备留在槽里，UI 图标归位。
- 本期不实现「装备被替换时旧装备自动回背包」，避免引入"槽已占用"的复杂分支；槽已占用时直接拒绝拖入，玩家需先卸下。

## 5. UI 层设计

### 5.1 预制体结构与挂载

- 新建 `Assets/UI/BattleHUD/Prefabs/BagPanel.prefab`
- 挂载位置：`SampleScene` 的 `HUDCanvas` 下，与 `BattleHUD` 同级，层级位于 `BattleHUD` 之后（保证盖在 HUD 上、能被按钮点击）
- 默认 `SetActive(false)`

节点结构：

```
BagPanel                      (RectTransform + BagPanelController)
├─ Background                 (Image，面板底图，九宫格)
├─ LeftColumn                 (装备区)
│  ├─ WeaponLabel             武器
│  ├─ WeaponRow
│  │  ├─ Slot_MeleeWeapon     (EquipmentSlotView)
│  │  └─ Slot_RangedWeapon    (EquipmentSlotView)
│  ├─ ArmorLabel              防具
│  └─ ArmorRow
│     ├─ Slot_Helmet          (EquipmentSlotView，禁用)
│     ├─ Slot_BodyArmor       (EquipmentSlotView，禁用)
│     └─ Slot_Boots           (EquipmentSlotView，禁用)
├─ RightColumn                (背包区)
│  ├─ BagLabel                背包
│  └─ Grid                    (GridLayoutGroup，5 列 × 4 行)
│     └─ Cell_00 … Cell_19    (BagItemView)
└─ DragLayer                  (空 RectTransform，拖拽图标临时挂在这里，置于最上层)
```

### 5.2 脚本职责

| 脚本 | 位置 | 职责 |
|---|---|---|
| `BagPanelController` | `Assets/UI/BattleHUD/Scripts/` | 持有 `Inventory` 实例；`Toggle()` / `Open()` / `Close()`；订阅 `Changed` 刷新全部格子；提供测试用初始物品列表 |
| `BagItemView` | 同上 | 单个背包格的显示与拖拽源；实现 `IBeginDragHandler` / `IDragHandler` / `IEndDragHandler` / `IDropHandler` |
| `EquipmentSlotView` | 同上 | 单个装备槽的显示与放置目标；实现 `IDropHandler`；预留槽自检并拒绝 |

- `BagPanelController` 上暴露 `[SerializeField] ItemDefinition[] debugStartingItems`，`Awake` 时逐个 `TryAdd`，用于没有掉落系统时验证面板。
- `BagButton` 的 `Button.onClick` 绑定到 `BagPanelController.Toggle`。
- 面板打开/关闭只改 `SetActive`，**不修改 `Time.timeScale`**。

**Inventory 实例归属：** 本期由 `BagPanelController` 在 `Awake` 时 `new Inventory()` 并持有（单场景验证足够）。子项目②接入掉落时，改为由玩家/战斗上下文创建并注入给面板，`BagPanelController` 只负责读取。

### 5.3 拖拽交互规格

1. **拖起**：在一个有物品的背包格或装备槽上按下并拖动 → 在该格位置生成一个跟随指针的图标，挂到 `DragLayer` 下，原格图标置半透明。
2. **悬停反馈**：指针下的目标若可接受该物品（背包空格 / 匹配且为空的可用装备槽）→ 目标显示高亮态；否则不高亮。
3. **松手成功**：
   - 背包 → 可用装备槽：`TryEquipFromBag`
   - 装备槽 → 背包空格：`TryUnequip`
   - 背包 → 背包：`TrySwapInBag`
4. **松手失败**（目标非法、槽位不匹配、背包满等）：拖拽图标销毁，原格恢复，数据不变。
5. 拖拽全程不修改 `Time.timeScale`；指针事件在面板区域内由面板消费，面板外的摇杆与攻击键不受影响。

## 6. 布局与尺寸

参考分辨率 1920×1080（`HUDCanvas` 的 `CanvasScaler`：`ScaleWithScreenSize`，Match = Height，与 [SampleScene.unity](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Scenes/SampleScene.unity#L1462-L1467) 现状一致）。

| 元素 | 尺寸 |
|---|---|
| 背包格子 | 112×112，间距 12 → 网格 608×484 |
| 装备槽 | 128×128，间距 12 → 防具行宽 408 |
| 装备区两行间距 / 标签高 | 12 / 28 |
| 面板内边距 | 24 |
| 面板总尺寸 | 约 1080×572 |
| 锚点 | 居中（`AnchorMin = AnchorMax = (0.5, 0.5)`），`AnchoredPosition` 略向右偏 |

该范围内面板水平约占屏幕 24%–80%、垂直约占 23%–77%，不覆盖左下摇杆与右下攻击键。

## 7. 无素材期间的占位方案

- 面板底板、格子、装备槽用纯色 `Image`：底色 `#2A2F3D`，描边 `#4C5670`，高亮态 `#C8A24A`。
- 物品无 `icon` 时，格子中央用 TMP 显示 `displayName` 前两字。
- 所有 Sprite 引用字段预先留好，素材到位后直接赋值即可生效，无需改代码。

## 8. 测试

沿用 `Assets/Tests/EditMode/` 现有 NUnit 惯例，为 `Inventory` 写 EditMode 测试：

| 用例 | 期望 |
|---|---|
| 加入物品到空格 | 成功，该格非空，`Changed` 触发一次 |
| 背包满后再加入（20 格填满） | 返回 false，数据不变 |
| 近战武器装到近战槽 | 成功，背包该格清空，槽位有值 |
| 近战武器装到远程槽 | 失败，数据不变 |
| 任何物品装到头盔/身甲/鞋子槽 | 失败，数据不变 |
| 槽位已被占用时再次装同类型物品 | 失败，数据不变 |
| 卸下装备且背包有空位 | 成功，槽清空，背包出现该物品 |
| 卸下装备但背包已满 | 失败，槽位保留该装备 |
| 交换背包两格 | 两格内容互换 |
| 越界索引 | 返回 false，不抛异常 |

## 9. 美术素材清单

**必需**

| # | 素材 | 尺寸 | 说明 | 存放路径 |
|---|---|---|---|---|
| 1 | 面板底图 | 256×256 | 圆角 + 边框，需设置九宫格 Border 48/48/48/48 | `Assets/UI/BattleHUD/Sprites/面板底图.png` |
| 2 | 背包格子底图 | 128×128 | 圆角方框，空格状态 | `Assets/UI/BattleHUD/Sprites/格子底图.png` |
| 3 | 背包格子高亮 | 128×128 | 同尺寸完整图，含底色 + 金边/发光，拖拽悬停可放入时整体替换 | `Assets/UI/BattleHUD/Sprites/格子高亮.png` |
| 4 | 装备槽底图 | 144×144 | 圆角方框，空槽状态 | `Assets/UI/BattleHUD/Sprites/装备槽底图.png` |
| 5 | 装备槽高亮 | 144×144 | 同尺寸完整图，含底色 + 金边 | `Assets/UI/BattleHUD/Sprites/装备槽高亮.png` |
| 6 | 装备槽禁用 | 144×144 | 同尺寸完整图，整体更暗更灰，用于头盔/身甲/鞋子预留槽 | `Assets/UI/BattleHUD/Sprites/装备槽禁用.png` |
| 7 | 近战武器图标 | 128×128 | 透明底 PNG，内容留 8px 安全边距 | `Assets/UI/BattleHUD/Sprites/图标_近战武器.png` |
| 8 | 远程武器图标 | 128×128 | 同上 | `Assets/UI/BattleHUD/Sprites/图标_远程武器.png` |

**可选（缺了我用纯色/文字顶）**

| # | 素材 | 尺寸 | 说明 |
|---|---|---|---|
| 9 | 空槽类型剪影 | 96×96 ×5 | 近战/远程/头盔/身甲/鞋子各一张，单色白、半透明，摆在空槽中央提示可放什么 |
| 10 | 关闭叉号 | 48×48 | 若改用叉号关闭而非"再点按钮关闭" |
| 11 | 拖拽描边/发光 | 112×112 | 拖拽中的图标高亮效果 |

**交付规格：** PNG、透明底；按 2 倍尺寸出图；Unity 导入时统一 PPU = 100、Sprite Mode = Single、Mesh Type = Full Rect；面板底图需在 Sprite Editor 里设 Border。

素材可先放到 `C:\Users\16648\Desktop\素材`，或直接拖进 `Assets/UI/BattleHUD/Sprites/`，导入设置由我统一处理。

## 10. 决策记录

| 决策 | 结论 | 理由 |
|---|---|---|
| 功能顺序 | 先做背包，后做掉落拾取 | 掉落是写入方，背包是存储+展示方；先做掉落无处落地 |
| 物品定义 | ScriptableObject 资产 | Inspector 可视化配图标，背包与掉落共用同一份定义 |
| 穿戴数值效果 | 本期不做 | 避免引入属性系统，范围最小 |
| 穿脱交互 | 拖拽 | 触屏手感好，符合 ARPG 习惯 |
| 背包容量 | 20 格（5×4）固定 | 面板不过大，前期够用 |
| 面板布局 | 方案 A：左右分区 | 装备与背包语义分区最清楚 |
| 数据层装配 | 独立 `Inventory.asmdef` | 避免纯逻辑装配反向依赖 UI 装配 |
| 槽位被占用 | 拒绝拖入，需先卸下 | 避免"旧装备自动回背包但背包满"的多重分支 |
