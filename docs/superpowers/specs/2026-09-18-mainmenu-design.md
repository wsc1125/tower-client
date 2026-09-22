# 局外主界面（深空回廊）视觉还原 — 设计文档

日期：2026-09-18
本轮范围：**纯视觉还原**，不含任何交互、数据、跳转逻辑。

## 1. 目标

按原型图 `C:\Users\16648\Desktop\素材\主界面ui\界面原型图.png` 在 Unity 中搭建局外主界面，
做到布局、比例、素材用法与原型图一致。完成后可截图与原型图并排比对。

## 2. 素材现状

素材目录：`C:\Users\16648\Desktop\素材\主界面ui`

| 素材 | 尺寸 | 宽高比 | 用途 | 文案是否烘焙 |
|---|---|---|---|---|
| 界面背景.png | 1672×941 | 16:9 | 全屏底图 | — |
| 上方图标.png | 684×162 | 4.2:1 | "深空回廊"标题横幅 | 是 |
| 头像-姓名图标.png | 2092×752 | 2.8:1 | 头像圈 + 名字条 | 否（空白） |
| 货币图标.png | 2057×765 | 2.7:1 | 星空币条 | 部分（"星空币"） |
| 触发按钮.png | 2134×737 | 2.9:1 | 出发 | 是 |
| 装备配置图标.png | 482×157 | 3.1:1 | 装备配置 | 是 |
| 仓库图标.png | 446×157 | 2.8:1 | 仓库 | 是 |
| 商店图标.png | 452×158 | 2.9:1 | 商店 | 是 |
| 设置按钮.png | 123×140 | 0.88:1 | 设置 | 是 |

**关键结论：原型图中所有中文文案都是烘焙在图里的**，不是动态文本。因此界面中真正需要
TMP 渲染的动态文本只有三处：玩家名、等级、货币数字。

## 3. 已识别的缺口

### 3.1 缺素材（需补图）

1. `触发按钮_金色态.png` — 原型图中"出发"是金色主 CTA，但 `触发按钮.png` 只有青色版。
   建议尺寸与 `触发按钮.png` 一致。
2. 单机/联机 Tab — 原型图右下胶囊形分段控件，无任何素材。建议出两张：
   `Tab底框.png` + `Tab选中块.png`（可复用），或 `Tab_单机选中.png` / `Tab_联机选中.png`。

### 3.2 缺字体（P0）

项目当前 **没有任何中文字体资源**：
- `Assets/UI` 与 `BattleScene.unity` 中零个 TMP 文本组件，全部中文靠出图
- `LiberationSans SDF.asset` 字表仅 250 个字形，纯 ASCII，无中日韩
- `TMP Settings.asset` 的 `m_fallbackFontAssets` 为空

因此"舰长"无法渲染。两种解法，待用户确认：
- **(a) 固定称谓** → 再出一张 `名字条_含舰长文字.png`，完全绕开字体问题
- **(b) 玩家自定义名** → 提供中文字体 ttf，用 TMP Dynamic 模式生成字体资源

## 4. 目录与场景

```
Assets/
  Scenes/
    MainMenu.unity              ← 新建，设为 Build Settings 0 号场景
  UI/
    MainMenu/                   ← 与 BattleHUD 平级
      Prefabs/MainMenu.prefab   ← 整个界面收成一个预制体
      Sprites/                  ← 从素材目录拷入 9 张 PNG
```

**不新建 asmdef、不写 C# 脚本。** 本轮只有 Image 与 TMP_Text 组件，没有脚本就落不进
任何程序集，asmdef 是多余的。下一轮接交互时再建 `Scripts/` + asmdef。

`界面原型图.png` 不拷入 Assets（仅作参考，避免误打进包）。

## 5. 层级结构

```
MainMenu (Canvas 1920x1080)
├── Background          Image「界面背景」
├── TopLeft             (锚点 左上)
│   ├── AvatarNameBar   Image「头像-姓名图标」
│   ├── PlayerName      TMP「舰长」
│   └── LevelText       TMP「Lv.1」
├── TopCenter
│   └── TitleBanner     Image「上方图标」
├── TopRight            (锚点 右上)
│   ├── CurrencyBar     Image「货币图标」
│   │   └── CurrencyValue  TMP「1240」
│   └── SettingsButton  Image「设置按钮」
└── RightMenu           (锚点 右侧)
    ├── Btn_Depart      Image「触发按钮」
    ├── Btn_Loadout     Image「装备配置图标」
    ├── Btn_Storage     Image「仓库图标」
    └── Btn_Shop        Image「商店图标」
```

全程**手动摆坐标**，不使用 VerticalLayoutGroup —— 自动布局的等间距与原型图实际间距
不一致，还原优先。

## 6. 布局与缩放

- Canvas Scaler 沿用 `BattleScene.unity` 的参数：参考分辨率 1920×1080，Match=Height
- 背景挂 **AspectRatioFitter (EnvelopeParent)**：手机横屏常见 20:9 等比 16:9 更宽，
  直接拉伸会把走廊拉变形，Envelope 自动放大铺满并裁掉溢出
- 其余元素贴边锚定（左上 / 顶部中 / 右上 / 右侧），宽屏时向外扩，不被拉变形
- 原型图为 1672×941，与 1920×1080 同为 16:9，坐标按 ×1.1483 换算

## 7. 按钮尺寸归一化

四个按钮素材宽高比 2.8:1 ~ 3.1:1 不等，原型图中却是**等宽等高**。

**方案：四个按钮使用完全相同的 RectTransform 尺寸，Image 直接拉伸。**
最大形变 8%，在青色描边风格上不可见。

备选：9-slice 保证图标不变形，但需逐张调 border。若实机截图发现边框明显变形再切换。

## 8. 本轮明确不做

金色态按钮、单机/联机 Tab（等补图）、所有点击响应、面板开关、场景跳转、存档、
星空币数值逻辑、动态星空/视差动画。

按钮**不加 Button 组件**，只放纯 Image，避免出现"看起来能点但没反应"的中间态。

## 9. 验证方式

用 Unity MCP 截图功能，将渲染结果与 `界面原型图.png` 并排比对，分四块迭代校准：
左上栏 → 顶部标题 → 右上 → 右侧菜单。截图存 `Assets/Screenshots/`，沿用既有命名习惯。
