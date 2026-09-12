# Battle HUD UI 设计说明

**目标**

基于 `C:\Users\16648\Desktop\素材` 中现有图片资源，在 Unity 项目中制作一套可复用的移动端战斗 HUD。产物同时包含独立模块预制体和一套总装 `BattleHUD` 预制体，方便后续在不同场景中直接复用或自由拼装。

**范围**

- 使用 `UGUI Canvas` 体系制作 HUD
- 导入并配置以下素材：
  - `状态.png`
  - `背包按钮.png`
  - `移动底盘.png`
  - `移动中心.png`
  - `攻击.png`
  - `切换攻击.png`
- 产出独立模块预制体：
  - `StatusPanel`
  - `BagButton`
  - `Joystick`
  - `AttackSwitchButton`
  - `AttackButton`
- 产出总装预制体：
  - `BattleHUD`

**结构设计**

`BattleHUD` 使用一个屏幕空间 `Canvas` 承载，并按移动端 HUD 常见布局将模块锚定在对应角落和顶部区域。

- 左上：`StatusPanel`
- 顶部中间：资源/货币展示区
- 右上：`BagButton`
- 左下：`Joystick`
- 右下：`AttackSwitchButton` 与 `AttackButton`

`Joystick` 作为可扩展输入组件，内部拆为：

- `Base`：使用 `移动底盘.png`
- `Handle`：使用 `移动中心.png`

这样后续如需接虚拟摇杆脚本，只需要在 `Joystick` 上继续挂输入脚本并移动 `Handle`。

**适配策略**

- 使用 `CanvasScaler`，以参考图布局作为初始视觉基准
- 通过 `RectTransform` 锚点固定核心模块位置，保证刘海屏、长屏、平板比例下相对位置稳定
- 攻击按钮与背包按钮保留足够热区，优先保证实机点击手感
- 摇杆区域放在左下角，避免和右侧战斗按钮互相遮挡

**资源组织**

计划在 `Assets/UI/BattleHUD/` 下组织内容：

- `Sprites/`：复制后的 UI 图片资源
- `Prefabs/Modules/`：独立模块预制体
- `Prefabs/`：总装 `BattleHUD` 预制体
- `Scenes/`：如需单独验证，可补一个演示场景或直接使用现有场景

**验证方式**

由于当前任务以 Unity 资源装配和预制体制作为主，不以行为脚本开发为主，验证将以 Unity 编辑器内检查为主：

- 素材是否成功导入为 `Sprite (2D and UI)`
- 预制体层级是否正确
- 各模块锚点、位置、缩放是否接近参考图
- `BattleHUD` 是否可直接实例化到场景中使用

**约束**

- 优先使用 `Unity MCP` 完成项目检查、资源导入、对象创建、组件装配和验证
- 不改动与当前 HUD 无关的场景内容
- 当前项目不是 Git 仓库，本次仅落地文件与资源，不执行提交

## 摇杆扩展设计

**目标**

在现有 `Joystick` 模块上补齐标准虚拟摇杆行为，使其能够在移动端 HUD 中响应拖拽、驱动前轮盘转向，并对外暴露可复用的输入值和事件接口。当前阶段不直接绑定角色移动逻辑，以保证后续角色系统加入时可以低耦合接入。

**范围**

- 保留现有 `Joystick -> Base + Handle` 预制体结构
- 在 `Joystick` 根节点挂载 `VirtualJoystickController`
- 支持按下、拖拽、抬起三种触摸流程
- `Handle` 跟随拖拽方向移动并限制在最大半径内
- `Handle` 可根据方向旋转，表现为“前轮盘可转动”
- 松手后 `Handle` 自动回中，输入归零
- 对外暴露只读输入值与事件接口，供后续角色系统订阅

**接口设计**

`VirtualJoystickController` 提供以下只读状态：

- `Input : Vector2`，范围 `-1 ~ 1`
- `Magnitude : float`，范围 `0 ~ 1`
- `IsDragging : bool`

同时提供以下事件接口：

- `OnValueChanged(Vector2 input, float magnitude)`
- `OnPointerDown()`
- `OnPointerUp()`

其中 `Input` 用于后续角色或相机系统直接轮询，`OnValueChanged` 用于事件驱动接法。两种方式同时提供，避免后续接入时必须改摇杆本体。

**交互规则**

- 当指针按下摇杆区域时，开始记录拖拽状态并触发 `OnPointerDown`
- 拖拽时将屏幕坐标转换为以摇杆中心为原点的局部偏移
- 偏移向量长度超过最大半径时进行裁切，保证 `Handle` 不会超出底盘
- `Handle` 的位置跟随裁切后的局部偏移
- `Handle` 的旋转根据输入方向更新，使视觉上可以转向
- `Input` 为偏移向量除以最大半径后的归一化值
- `Magnitude` 为偏移长度与最大半径的比值，限制在 `0 ~ 1`
- 指针抬起时，`Handle` 回到中心、旋转回默认值、输入值清零并触发 `OnPointerUp`

**实现边界**

本阶段不包含以下功能：

- 不做动态底座
- 不做死区配置
- 不做平滑插值
- 不直接驱动角色移动

这样可以将实现聚焦在“摇杆输入本体可用”这一层，先完成复用接口，再由后续角色系统消费。

**验证方式**

摇杆扩展完成后需要验证以下内容：

- 拖拽时 `Handle` 会随方向移动且不会超出底盘边界
- 拖拽时 `Handle` 会按输入方向旋转
- 松手后 `Handle` 回中，`Input` 变为 `Vector2.zero`
- 编辑模式测试可验证输入归一化、半径限制和回中行为
- 场景中的 `BattleHUD/Joystick` 实例已更新为带控制脚本的版本

**后续接入示例**

后续角色系统加入后，可直接在 `Update` 中轮询摇杆输入：

```csharp
public class PlayerMover : MonoBehaviour
{
    [SerializeField] private VirtualJoystickController joystick;

    private void Update()
    {
        Vector2 input = joystick.Input;
        Vector3 move = new Vector3(input.x, 0f, input.y);
        transform.position += move * Time.deltaTime * 3f;
    }
}
```

如果你更偏向事件流，也可以监听 `OnValueChanged`，在回调中拿到 `Vector2 input` 和 `float magnitude`。

**人工验证清单**

- 在 `Game` 视图按下左下角摇杆区域
- 向任意方向拖拽，确认 `Handle` 跟随移动
- 观察 `Handle` 旋转方向是否跟拖拽方向一致
- 松手，确认 `Handle` 回到中心
- 如后续接角色，可额外观察角色是否与 `Input` 同步移动
