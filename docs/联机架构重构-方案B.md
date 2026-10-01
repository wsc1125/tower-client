# 联机架构重构（方案 B）设计文档

## 1. 背景与问题

### 1.1 现象

实测双端（编辑器房主 + NetClient 成员）点击「出发」后：

- 两端都进入了 BattleScene，但**不在同一个网络会话**；
- 成员端**角色生成失败**，看不到任何可控制角色；
- 房主端独自运行一局，成员端独自运行另一局。

### 1.2 根因

当前流程是「**先各自切场景，再尝试建立连接**」：

```
房主点击出发 → 服务端广播 game_starting
    → 房主本地 SceneManager.LoadSceneAsync("BattleScene")
    → 成员本地 SceneManager.LoadSceneAsync("BattleScene")
    → 进入战斗场景后，才通过一次性即时信令交换房主地址
    → 成员收到地址后才 StartClient
```

问题点：

1. **连接依赖场景加载速度**：房主进入战斗场景后才订阅 `SignalReceived`，成员此时发出的地址请求被丢弃（服务端不重放即时消息）。
2. **即时信令被当作可靠状态使用**：请求只发一次，错过即永久丢失。
3. **场景切换不受网络会话约束**：NGO 未连接也能进入 BattleScene，UI 的「出发成功」与网络层的「连接成功」没有绑定。
4. **两端各自加载场景**：NGO 无法保证双方处于同一网络世界。
5. **战斗场景职责过重**：地址协商、Host/Client 启动、角色生成、场景初始化全塞在 `NetworkGameBootstrap`。

结论：这是**架构问题**，不是 NGO 或角色预制体的技术缺陷。

### 1.3 目标

- 彻底改为「**先建立连接，再由 Host 统一切场景**」。
- 本阶段实现**本机双开 + 局域网直连**。
- 预留传输层接口，后续接入**自建 Relay** 支持异地公网。
- **单机模式行为完全不变**。

---

## 2. 总体架构

### 2.1 分层

```
常驻层（DontDestroyOnLoad）
├─ NetworkSession            账号 / HTTP 房间 / WebSocket 信令（已存在）
├─ NetworkManager            NGO 会话宿主（从 BattleScene 迁出）
└─ MultiplayerCoordinator    NGO 会话生命周期状态机（新增）

主菜单层
├─ MainMenuRoomBinder        房间 UI 接线（已存在，需扩展）
└─ DepartFlow                单机出发流程（保持不变）

战斗场景层
├─ BattleNetworkBinder       场景内绑定：相机 / HUD / 摇杆 / TowerRunManager（新增）
└─ NetworkPlayerSpawner      Host 侧玩家生成与出生点（新增）
```

### 2.2 职责边界

| 组件 | 职责 | 不负责 |
|------|------|--------|
| `NetworkSession` | 账号、房间、信令 | NGO 连接 |
| `MultiplayerCoordinator` | Host/Client 启动、连接审批、状态机、网络切场 | 房间业务、UI |
| `NetworkManager` | NGO 底层 | 业务逻辑 |
| `BattleNetworkBinder` | 场景内本地绑定 | 连接建立 |
| `NetworkPlayerSpawner` | Host 生成 PlayerObject、分配出生点 | 输入、相机 |
| `DepartFlow` | 单机切场 | 联机切场 |

---

## 3. 连接生命周期状态机

`MultiplayerCoordinator` 维护唯一状态，禁止跨状态执行操作。

```
Idle
  │ 房主点击出发 / 成员收到 game_session
  ▼
Preparing
  │ 房主：StartHost；成员：StartClient
  ▼
Connecting
  │ 连接审批通过
  ▼
WaitingForPlayers        ← 房主等待所有房间成员连入
  │ 全部连入
  ▼
LoadingBattleScene       ← 仅 Host 调用 NGO NetworkSceneManager
  │ 场景同步完成
  ▼
InGame
  │ 退出 / 断线
  ▼
Disconnecting → Idle
```

失败分支：

```
任意状态 ──失败──▶ Failed（显示原因，留在房间界面，不切场）
```

状态约束：

- `Connecting` / `WaitingForPlayers` 期间**禁止**任何本地场景加载。
- 只有 `WaitingForPlayers → LoadingBattleScene` 允许网络切场。
- `Failed` 状态不自动进入战斗，需玩家重试或退出房间。

---

## 4. 出发流程（新）

### 4.1 房主

```
1. 房主点击「出发」
2. 校验：是房主 && 房间人数 > 1
3. 服务端创建本局 game_session（唯一 session_id）
4. MultiplayerCoordinator.StartHost()
   - NetworkManager 常驻，配置 UnityTransport 绑定 0.0.0.0:7777
   - 开启 ConnectionApproval
5. 房主把连接信息写入服务端（可查询状态，非一次性事件）
6. 等待所有房间成员 NGO 连接成功
7. Host 调用 NetworkManager.SceneManager.LoadScene("BattleScene", Single)
8. NGO 自动同步所有客户端加载
9. Host 生成 PlayerObject
```

### 4.2 成员

```
1. 成员收到 game_session（推送 + 可主动查询）
2. MultiplayerCoordinator.StartClient(hostAddress, port, ticket)
3. 连接审批通过
4. 等待 Host 发起网络切场
5. NGO 自动加载 BattleScene
6. 收到自己的 PlayerObject，绑定相机 / HUD / 摇杆
```

### 4.3 关键变化

| 项目 | 旧 | 新 |
|------|-----|-----|
| 连接时机 | 进入战斗后 | 进入战斗前（主菜单） |
| 场景加载 | 两端各自 `SceneManager` | 仅 Host 用 NGO `NetworkSceneManager` |
| 地址传递 | 一次性即时信令 | 服务端可查询的会话状态 |
| 失败处理 | 静默超时，仍进战斗 | 明确失败，留在房间 |

---

## 5. 服务端改动

### 5.1 游戏会话（game session）

新增本局连接描述，作为**可查询状态**：

```json
{
  "session_id": "唯一会话ID",
  "room_code": "FHNNQ477",
  "transport_mode": "lan",
  "host_account_id": 24,
  "host_address": "10.6.136.22",
  "host_port": 7777,
  "status": "connecting | ready | in_game | closed",
  "created_at": "..."
}
```

### 5.2 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/rooms/:code/session` | 房主创建本局会话，返回 session_id |
| GET | `/rooms/:code/session` | 成员查询当前会话（错过推送时兜底） |
| POST | `/rooms/:code/session/ready` | 房主标记连接信息就绪 |
| DELETE | `/rooms/:code/session` | 结束本局会话 |

### 5.3 信令扩展

- `game_starting` payload 增加 `session_id`、`transport_mode`。
- 新增 `game_session` 消息：携带 `host_address` / `host_port` / `session_id`。
- 成员可随时 `GET /rooms/:code/session` 补齐，避免错过推送。

### 5.4 连接票据（connection ticket）

Host 审批成员时需要验证身份，避免任意 UDP 连接混入：

- 房主创建会话时，服务端为每个房间成员签发短期 `connection_ticket`；
- 成员连接时携带 `account_id` + `room_code` + `session_id` + `ticket`；
- Host 通过后端校验接口验证票据有效性。

> 本阶段可先实现「房间成员校验」，票据机制作为安全增强项，与自建 Relay 一起完善。

---

## 6. 客户端改动

### 6.1 NetworkManager 常驻

- 从 BattleScene 移到主菜单常驻对象；
- `DontDestroyOnLoad`；
- 保留 `EnableSceneManagement: 1`、`PlayerPrefab`、`NetworkPrefabsLists`；
- 开启 `ConnectionApproval`。

### 6.2 新增 `MultiplayerCoordinator`

```csharp
public sealed class MultiplayerCoordinator : MonoBehaviour
{
    public MultiplayerState State { get; }
    public event Action<MultiplayerState> StateChanged;

    public IEnumerator StartHost(string roomCode, string sessionId);
    public IEnumerator StartClient(string address, ushort port, string ticket);
    public IEnumerator WaitForAllMembersConnected(float timeout);
    public void LoadBattleScene();      // 仅 Host
    public void Shutdown();
}
```

### 6.3 新增 `BattleNetworkBinder`

替代 `NetworkGameBootstrap` 的场景内职责：

- 隐藏单机静态玩家；
- 本地 owner 绑定相机 / HUD / 摇杆；
- Host 绑定 `TowerRunManager`；
- 场景就绪确认。

### 6.4 新增 `NetworkPlayerSpawner`

- 维护 `clientId → accountId` 映射；
- Host 生成 PlayerObject；
- 分配出生点（`TowerRunManager.FloorCenter`）。

### 6.5 传输策略接口（为自建 Relay 预留）

```csharp
public interface IConnectionStrategy
{
    IEnumerator StartHost(NetworkManager nm, SessionInfo session);
    IEnumerator StartClient(NetworkManager nm, SessionInfo session);
}
```

本阶段实现：

- `LanConnectionStrategy`：`UnityTransport.SetConnectionData(host, port)`

后续新增：

- `RelayConnectionStrategy`：连接自建 Relay 服务，替换 UnityTransport 配置

`MultiplayerCoordinator` 只依赖接口，切换传输方式不改上层。

### 6.6 删除 / 收缩

- `NetworkGameBootstrap` 的地址协商、Host/Client 启动逻辑删除；
- 必要场景绑定逻辑迁移到 `BattleNetworkBinder`；
- 成员端禁止调用普通 `SceneManager.LoadSceneAsync("BattleScene")`。

---

## 7. 单机模式保障

**单机流程完全不经过联机代码路径。**

| 检查点 | 保障方式 |
|--------|----------|
| `NetworkManager` 常驻 | 单机时不启动，`IsListening == false` |
| `MultiplayerCoordinator` | 单机时状态保持 `Idle`，不订阅信令 |
| `DepartFlow` | 单机分支保持现有 `SceneManager.LoadSceneAsync` |
| `BattleNetworkBinder` | 非联机会话时直接 return，不隐藏静态玩家 |
| `PlayerNetworkSync` | 仅存在于网络玩家 prefab，单机静态玩家无此组件 |
| `TowerRunManager` | 单机路径不变 |

回归验证清单：

1. 不登录，直接单机出发 → 正常进入 BattleScene，角色可移动、可战斗；
2. 单机死亡 → 正常结算 / 返回主菜单；
3. 单机存档、装备、掉落 → 行为不变；
4. 联机模式退出房间 → 切回单机，单机流程正常。

---

## 8. 实施阶段

### 阶段 1：连接生命周期重建
- NetworkManager 常驻化；
- `MultiplayerCoordinator` 状态机；
- LAN Host/Client；
- 连接审批；
- 成员连接状态显示。

### 阶段 2：统一网络切场
- Host 使用 NGO `NetworkSceneManager`；
- 成员禁止本地切场；
- 加载失败处理；
- 场景就绪确认。

### 阶段 3：网络玩家生成
- `clientId → accountId` 映射；
- Host 生成 PlayerObject；
- owner 绑定相机 / HUD / 摇杆；
- 双端验证可见与可控。

### 阶段 4：游戏权威化
- `TowerRunManager` 仅 Host 驱动；
- 敌人 / 伤害 / 掉落 Host 权威；
- 客户端只提交输入或请求。

### 阶段 5：自建 Relay
- 实现 `RelayConnectionStrategy`；
- 服务端增加 Relay 会话与转发；
- 不改变房间、切场、角色生成架构。

---

## 9. 验收标准

### 9.1 联机（本机双开 / 局域网）

- [ ] 房主点击出发后，成员**不会**自行进入 BattleScene；
- [ ] 两端进入的是**同一个 NGO 会话**；
- [ ] 成员角色正常生成，可移动、可被看到；
- [ ] 房主能看到成员角色，成员能看到房主角色；
- [ ] 连接失败时留在房间界面并显示原因，不进入战斗；
- [ ] 退出房间后 NGO 正确关闭，无残留连接。

### 9.2 单机

- [ ] 单机出发、战斗、死亡、结算、存档全部正常；
- [ ] 单机流程不产生任何 NGO 连接；
- [ ] 联机退出后切回单机，行为正常。

---

## 10. 风险与对策

| 风险 | 对策 |
|------|------|
| NetworkManager 常驻后单机误启动 | 单机路径不调用 Coordinator，状态保持 Idle |
| NGO 网络切场与单机切场冲突 | 联机走 `NetworkSceneManager`，单机走 `SceneManager`，互斥 |
| 成员连接失败无反馈 | 状态机 `Failed` + UI 明确提示 |
| 后续 Relay 改动波及上层 | 传输策略接口隔离 |
| 场景内静态玩家与网络玩家并存 | `BattleNetworkBinder` 在联机时隐藏静态玩家 |
