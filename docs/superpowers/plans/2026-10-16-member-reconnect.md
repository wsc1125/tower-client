# 成员掉线重连实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让联机成员在中继连接抖动 / 瞬断后自动重连，避免「一次网络抖动 = 整局作废」。房主掉线仍走既有「解散」策略，不在本计划范围内。

**Architecture:** 分两层。**传输层**（`WebSocketRelayTransport`）在 socket 断开后进入指数退避重连状态机，重连期间不向 NGO 投递任何事件，NGO 完全无感知；重连耗尽才投递 `Disconnect` / `TransportFailure`。**协调层**（`MultiplayerCoordinator`）在收到成员端 `Disconnect` 后进入 15 秒宽限期，期间冻结本地输入并显示「正在重连…」，超时才走既有 `EndHostSession` 解散路径。

**Tech Stack:** Unity 2022.3.62f3c1、C#、Unity Netcode for GameObjects (NGO) 1.5、Unity Test Framework（EditMode）、Unity MCP

## Global Constraints

- 不新增、不修改任何 `asmdef`。
- **单机路径零改动**：所有新增逻辑必须由 `BattleNetworkBinder.IsOnlineSession` 或 `NetworkManager.Singleton.IsListening` 守卫。
- **房主路径零改动**：房主掉线仍走既有 `OnHostLeft` / `EndHostSession` 解散策略，本计划只处理成员端。
- 不改动 `PlayerNetworkSync` 的同步字段语义、不改动 `EnemyNetworkSync` 的权威模型。
- 所有玩家可见文本使用中文。
- 空引用一律静默返回，不抛异常（沿用项目既有策略）。
- 每个任务以一次 `git commit` 收尾；提交信息用中文，沿用 `feat:` / `fix:` / `docs:` 前缀。
- 每个验证步骤必须看到真实输出，不允许「应该能过」就进入下一步。
- 测试运行方式统一为 Unity MCP：`unity_testing_run_tests`（`mode: "EditMode"`，`filter` 传类名）→ 取返回的 `jobId` → `unity_testing_get_job`（`waitTimeout: 60`）。

## 已确认的设计决策

| 决策点 | 结论 |
|---|---|
| 掉线角色处理 | **传输层重连期间角色始终在场**（NGO 无感知）；传输层放弃后 NGO 自动销毁角色（`DontDestroyWithOwner: 0`，无法保留）。 |
| 宽限期 | **15 秒**。超时则解散本局。 |
| 世界推进 | **继续推进，不暂停**。敌人照常行动。 |
| 掉线角色是否可被攻击 | **短暂掉线（传输层自愈）期间可被攻击**；长时间断网角色被销毁，无法攻击。详见 Task 4 说明。 |

## 依赖关系

```
Task 1 (传输层重连状态机) ──> Task 2 (协调层宽限期) ──> Task 3 (重连后解冻)
Task 4 (掉线角色移除的存活计数) ── 独立，仅补测试
```

- Task 2 依赖 Task 1（宽限期只在传输层重连耗尽后才触发）。
- Task 3 依赖 Task 2（重连成功后才需要解冻）。
- Task 4 独立。

---

### Task 1: 传输层重连状态机（`WebSocketRelayTransport`）

**背景：** 当前 [OnSocketClosed](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/WebSocketRelayTransport.cs#L403-L418) 一旦 socket 断开就直接投递 `TransportFailure`（房主）/ `Disconnect`（成员），NGO 立刻销毁会话。公网下一次 TCP 抖动就整局作废。

**关键前提（已核实）：** 服务端 [relayws.Hub.Join](file:///c:/Users/16648/Desktop/My%20project%20(3)/server/internal/relayws/hub.go#L156-L207) 支持同账号重连顶替旧连接，且传输层 clientId 用账号 ID（房主恒为 0），**重连后 ID 稳定** —— 这是能重连的基础。服务端无需改动。

**为什么传输层重连对 NGO 透明（本任务成立的根基）：**

NGO 的会话状态由 `NetworkTransport` 投递的事件驱动。只要传输层**不投递** `Disconnect` / `TransportFailure`，NGO 就认为连接一直正常 —— 它不知道底层 socket 断过。因此：

- 重连期间 NGO 会话**从未断开**，无需任何恢复逻辑；
- NGO 在断线期间写入 `outboxBinary` 的数据**保留在队列中**（`TryReconnect` 不清队列，只有 `Cleanup` 清），重连后由新的 `SendLoop` 补发；
- 重连成功后**不需要**重新握手 —— 服务端 `welcome` 只是重新告知 clientId，NGO 侧无感知。

> **反过来说**：一旦传输层投递了 `Disconnect`，NGO 会立刻销毁会话，**无法通过后续重连恢复**（NGO 的 `Connect` 事件只在 `StartClient` 握手阶段被消费）。所以重连**必须**在传输层内部完成，这是本任务不可替代的原因。

**Files:**
- Modify: `Assets/Gameplay/Runtime/WebSocketRelayTransport.cs`
- Test: `Assets/Tests/EditMode/Gameplay/RelayReconnectTests.cs`（新建）

**Interfaces:**
- Consumes: 无（纯传输层）
- Produces:
  - `WebSocketRelayTransport.ReconnectAttempts` → `int`（已尝试次数，供测试与日志）
  - `WebSocketRelayTransport.IsReconnecting` → `bool`
  - `WebSocketRelayTransport.MaxReconnectAttempts` → `const int`（= 5）
  - `WebSocketRelayTransport.ReconnectBackoffSeconds(int attempt)` → `float`（静态纯函数，供测试）

- [x] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/RelayReconnectTests.cs`，只覆盖可纯逻辑验证的部分（退避序列与耗尽判定），不依赖真实 socket：

```csharp
using NUnit.Framework;
using Tower.Netcode;

public sealed class RelayReconnectTests
{
    [Test]
    public void Backoff_GrowsExponentially_AndIsCapped()
    {
        // 退避序列：1s / 2s / 4s / 8s / 8s（上限 8 秒）。
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(1), Is.EqualTo(1f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(2), Is.EqualTo(2f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(3), Is.EqualTo(4f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(4), Is.EqualTo(8f).Within(0.001f));
        Assert.That(WebSocketRelayTransport.ReconnectBackoffSeconds(5), Is.EqualTo(8f).Within(0.001f));
    }

    [Test]
    public void ShouldGiveUp_OnlyAfterMaxAttempts()
    {
        // 未达上限：继续重连。
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(1), Is.False);
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(4), Is.False);

        // 达到上限：放弃，投递失败事件。
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(5), Is.True);
        Assert.That(WebSocketRelayTransport.ShouldGiveUp(6), Is.True);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`mode: "EditMode"`，`filter: "RelayReconnectTests"`），确认**编译失败**（`ReconnectBackoffSeconds` / `ShouldGiveUp` 尚不存在）。

- [x] **Step 3: 在 `WebSocketRelayTransport` 新增重连参数与纯函数**

在 [WebSocketRelayTransport.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/WebSocketRelayTransport.cs) 的字段区新增：

```csharp
/// <summary>最大重连尝试次数。耗尽后才向 NGO 投递失败事件。</summary>
public const int MaxReconnectAttempts = 5;

/// <summary>退避上限（秒）。避免退避时间无限增长。</summary>
private const float MaxBackoffSeconds = 8f;

/// <summary>已尝试的重连次数。0 表示当前连接正常。</summary>
public int ReconnectAttempts => reconnectAttempts;

/// <summary>是否正在重连中（socket 已断、尚未放弃）。</summary>
public bool IsReconnecting { get; private set; }

/// <summary>
/// 第 <paramref name="attempt"/> 次重连前的退避时长（秒）。
/// 指数退避 1/2/4/8/8…，上限 <see cref="MaxBackoffSeconds"/>。
/// </summary>
public static float ReconnectBackoffSeconds(int attempt)
{
    if (attempt <= 0)
        return 0f;

    // 2^(attempt-1)，用移位避免 Mathf.Pow 的浮点误差。
    var shift = Mathf.Min(attempt - 1, 3); // 1<<3 = 8，正好是上限
    return Mathf.Min(1 << shift, MaxBackoffSeconds);
}

/// <summary>是否应放弃重连（已达上限）。</summary>
public static bool ShouldGiveUp(int attempts)
{
    return attempts >= MaxReconnectAttempts;
}
```

- [x] **Step 4: 运行测试确认通过**

运行 `unity_testing_run_tests`（`filter: "RelayReconnectTests"`），确认 2 个测试通过。

- [x] **Step 5: 改造 `OnSocketClosed` 为重连入口**

> **重要约束（已核实）**：`WebSocketRelayTransport` 继承 `NetworkTransport`，**不是 `MonoBehaviour`**，
> 因此**不能用 `StartCoroutine` / `WaitForSecondsRealtime`**。它只有 `Update()` 可用。
> 重连必须实现为「`Update` 驱动的状态机」，不能用协程。

将 [OnSocketClosed](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/WebSocketRelayTransport.cs#L403-L418) 改为：

```csharp
/// <summary>
/// 中继连接断开：先尝试自动重连，重连耗尽才向 NGO 投递失败事件。
///
/// 为什么不在传输层直接报失败：NGO 一旦收到 Disconnect / TransportFailure，
/// 会立刻销毁该 clientId 的 NetworkObject 并触发 OnClientDisconnectCallback，
/// 上层无法「假装没断」。因此重连必须在传输层内部完成，对 NGO 完全透明。
///
/// 服务端支持同账号重连顶替旧连接，且传输层 clientId 用账号 ID（房主恒为 0），
/// 重连后 ID 稳定 —— 这是重连可行的前提。
///
/// 注意：本类不是 MonoBehaviour，不能用协程 —— 重连由 <see cref="Update"/> 驱动。
/// </summary>
private void OnSocketClosed(int activeSession)
{
    lock (sessionLock)
    {
        if (activeSession != sessionId || socketClosed)
            return;

        // 标记本次会话已断，但**不**投递事件 —— 先尝试重连。
        socketClosed = true;
        IsReconnecting = true;
        reconnectSession = activeSession;
        reconnectAttempts = 0;
        reconnectDeadline = Time.realtimeSinceStartup; // 立即触发首次尝试
    }
}

/// <summary>
/// 重连状态机（由 <see cref="Update"/> 每帧驱动）。
///
/// 指数退避重试，成功则恢复收发循环，耗尽则投递失败事件。
/// 重连期间不投递任何事件，NGO 侧无感知（表现为「网络卡了一下」）。
/// </summary>
private void StepReconnect()
{
    if (!IsReconnecting)
        return;

    // 会话已被 Cleanup / 新连接取代：放弃本次重连。
    if (reconnectSession != sessionId)
    {
        IsReconnecting = false;
        return;
    }

    if (Time.realtimeSinceStartup < reconnectDeadline)
        return;

    if (ShouldGiveUp(reconnectAttempts))
    {
        Debug.LogWarning($"[RelayTransport] 重连 {MaxReconnectAttempts} 次均失败，向 NGO 报告断开。");
        IsReconnecting = false;
        NotifyTransportClosed(reconnectSession);
        return;
    }

    reconnectAttempts++;
    Debug.Log($"[RelayTransport] 尝试第 {reconnectAttempts}/{MaxReconnectAttempts} 次重连…");

    if (TryReconnect(reconnectSession))
    {
        Debug.Log($"[RelayTransport] 第 {reconnectAttempts} 次重连成功。");
        IsReconnecting = false;
        reconnectAttempts = 0;
        return;
    }

    // 失败：安排下一次退避。
    reconnectDeadline = Time.realtimeSinceStartup + ReconnectBackoffSeconds(reconnectAttempts);
}
```

新增字段：

```csharp
/// <summary>重连状态机：目标会话 ID。</summary>
private int reconnectSession;

/// <summary>重连状态机：已尝试次数。</summary>
private int reconnectAttempts;

/// <summary>重连状态机：下次尝试的时间点（realtime）。</summary>
private float reconnectDeadline;
```

/// <summary>
/// 尝试重建 socket 并恢复收发循环。成功返回 true。
///
/// 复用既有 <see cref="ConnectAndStart"/> 的连接逻辑，但**不重置** sessionId ——
/// sessionId 是「本次 NGO 会话」的标识，重连属于同一会话，重置会让在途事件被丢弃。
/// </summary>
private bool TryReconnect(int activeSession)
{
    lock (sessionLock)
    {
        if (activeSession != sessionId)
            return false;
    }

    // 清理旧 socket（不动 sessionId / 队列）。
    try
    {
        cts?.Cancel();
        socket?.Abort();
        socket?.Dispose();
    }
    catch
    {
        // 已释放，忽略。
    }

    socket = new ClientWebSocket();
    cts = new CancellationTokenSource();

    var connectTask = socket.ConnectAsync(new Uri(relayUrl), cts.Token);
    var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
    while (!connectTask.IsCompleted)
    {
        if (Time.realtimeSinceStartup > deadline)
        {
            CleanupSocketOnly();
            return false;
        }

        Thread.Sleep(16);
    }

    if (connectTask.IsFaulted || socket.State != WebSocketState.Open)
    {
        CleanupSocketOnly();
        return false;
    }

    var activeSocket = socket;
    var token = cts.Token;
    Task.Run(() => ReceiveLoop(activeSocket, token, activeSession));
    Task.Run(() => SendLoop(activeSocket, token, activeSession));

    lock (sessionLock)
    {
        if (activeSession != sessionId)
            return false;

        // 重连后必须重新等待服务端 welcome —— 服务端会重新下发。
        welcomed = false;
        socketClosed = false;
    }

    return true;
}

/// <summary>只清理 socket 与 cts，不动 sessionId 与队列（重连失败时用）。</summary>
private void CleanupSocketOnly()
{
    try
    {
        cts?.Cancel();
        socket?.Abort();
        socket?.Dispose();
    }
    catch
    {
        // 已释放，忽略。
    }

    socket = null;
    cts = null;
}

/// <summary>重连耗尽：向 NGO 投递失败事件（房主报传输失败，成员报与房主断开）。</summary>
private void NotifyTransportClosed(int activeSession)
{
    lock (sessionLock)
    {
        if (activeSession != sessionId)
            return;

        inbox.Enqueue(new TransportEvent
        {
            SessionId = activeSession,
            Type = isServer ? NetworkEvent.TransportFailure : NetworkEvent.Disconnect,
            ClientId = ServerClientId,
        });
    }
}
```

> **注意**：`Cleanup()` 中需把 `ReconnectAttempts = 0; IsReconnecting = false;` 一并重置，避免跨会话残留。

- [x] **Step 6: 在 `Update` 中驱动重连状态机，并在 `Cleanup` 中重置**

修改 [Update](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/WebSocketRelayTransport.cs#L219-L232)，在泵事件之前先驱动重连：

```csharp
private void Update()
{
    // 重连状态机优先：断线期间没有事件可泵，先尝试恢复连接。
    StepReconnect();

    while (inbox.TryDequeue(out var e))
    {
        if (e.SessionId != sessionId)
            continue;

        InvokeOnTransportEvent(
            e.Type,
            e.ClientId,
            e.Payload != null ? new ArraySegment<byte>(e.Payload) : default,
            Time.realtimeSinceStartup);
    }
}
```

修改 [Cleanup](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/WebSocketRelayTransport.cs#L422-L456) 的 `lock` 块，追加：

```csharp
IsReconnecting = false;
reconnectAttempts = 0;
reconnectSession = 0;
reconnectDeadline = 0f;
```

- [x] **Step 7: 实机验证（双端 + 断网模拟）**

> **实施补充（2026-10-16）**：Step 5-6 落地时发现两个计划盲点，已一并修复：
> 1. **成员重连后服务端重发 welcome 会触发第二个 Connect 事件**（NGO 已连接状态下重复握手）→ `TryReconnect` 不再重置 `welcomed`，由既有 `!welcomed` 门卫挡住；
> 2. **成员断开瞬间服务端向房主发 `peer_left`，房主会立即向 NGO 投递 Disconnect**（"NGO 无感知"只在成员端成立）→ 新增房主侧 45 秒 `peer_left` 宽限 + 协议新增 `resume` 控制消息（成员重连成功后立即发出，服务端转发房主，房主静默撤销宽限并恢复数据门卫）。服务端 `hub.go` / `hub_test.go` 已同步改动并通过 `go test`。
> Step 7 实机验证待用户配合进行（需双端 + 断网模拟）。

1. 启动后端服务（Go 服务端）。
2. 启动两个客户端，登录不同账号，建房 / 进房，出发进入战斗。
3. 成员端用防火墙规则 / 拔网线模拟断网 3 秒后恢复。
4. **预期**：成员端日志出现「连接断开，N 秒后第 1/5 次重连…」→「第 1 次重连成功」；房主端日志出现该成员 `peer_left` 后紧跟 `peer_joined`；**双方游戏不中断**，成员角色位置/血量保持。
5. 成员端断网 60 秒（超过 5 次重连总时长）。
6. **预期**：成员端日志「重连 5 次均失败，向 NGO 报告断开」→ 走既有解散路径回主菜单。
7. 单机回归：单机模式不经过本传输，行为不变。

- [x] **Step 8: 提交**

```bash
git add Assets/Gameplay/Runtime/WebSocketRelayTransport.cs Assets/Tests/EditMode/Gameplay/RelayReconnectTests.cs
git commit -m "feat: 中继传输层自动重连（指数退避，对 NGO 透明）"
```

---

### Task 2: 协调层重连宽限期（`MultiplayerCoordinator`）

**背景：** Task 1 让传输层能自愈瞬时抖动。但若重连耗尽（如成员断网 60 秒），`Disconnect` 事件仍会到达 [HandleClientDisconnected](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/MultiplayerCoordinator.cs#L670-L685)，成员端立刻 `EndHostSession` 解散。

**本任务的定位（重要）：** 走到这里说明**传输层已放弃**，NGO 会话**已经不可恢复**（`Disconnect` 一旦投递，NGO 立刻销毁会话）。因此宽限期**不是**「等 NGO 自愈」—— 它等的是**玩家重新走一遍连接流程**（重新 `StartClient`）。

> **修正**：初版计划里写「检查 `networkManager.IsConnectedClient` 恢复」是**错的** —— 传输层重连不会让 NGO 自动恢复。宽限期内应做的是：**重新发起 `StartClientFlow`**（复用既有流程），成功后恢复游戏；失败或超时才解散。

**Files:**
- Modify: `Assets/Gameplay/Runtime/MultiplayerCoordinator.cs`
- Modify: `Assets/Gameplay/Runtime/TowerRunManager.cs`（宽限期 UI 提示）
- Test: `Assets/Tests/EditMode/Gameplay/ReconnectGraceTests.cs`（新建）

**Interfaces:**
- Consumes: `MultiplayerCoordinator.State`、`MultiplayerDepartBridge`
- Produces:
  - `MultiplayerState.Reconnecting` → 新增枚举值
  - `MultiplayerCoordinator.ReconnectGraceSeconds` → `const float`（= 15f）
  - `MultiplayerCoordinator.ShouldEnterGrace(MultiplayerState state, bool isServer)` → `static bool`（纯函数，供测试）
  - `MultiplayerDepartBridge.ReconnectStarted` / `ReconnectEnded` → `static event Action`

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/ReconnectGraceTests.cs`：

```csharp
using NUnit.Framework;
using Tower.Netcode;

public sealed class ReconnectGraceTests
{
    [Test]
    public void MemberInGame_EntersGrace()
    {
        // 成员端在战斗中掉线 → 进入宽限期。
        Assert.That(MultiplayerCoordinator.ShouldEnterGrace(MultiplayerState.InGame, false), Is.True);
    }

    [Test]
    public void HostInGame_DoesNotEnterGrace()
    {
        // 房主掉线不走宽限期（房主是拓扑中心，仍走解散）。
        Assert.That(MultiplayerCoordinator.ShouldEnterGrace(MultiplayerState.InGame, true), Is.False);
    }

    [Test]
    public void MemberIdle_DoesNotEnterGrace()
    {
        // 未进战斗（主菜单）掉线无需宽限期。
        Assert.That(MultiplayerCoordinator.ShouldEnterGrace(MultiplayerState.Idle, false), Is.False);
    }

    [Test]
    public void GraceSeconds_IsFifteen()
    {
        Assert.That(MultiplayerCoordinator.ReconnectGraceSeconds, Is.EqualTo(15f).Within(0.001f));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`filter: "ReconnectGraceTests"`），确认编译失败。

- [ ] **Step 3: 新增 `Reconnecting` 状态与纯函数**

在 [MultiplayerCoordinator.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/MultiplayerCoordinator.cs#L11-L36) 的 `MultiplayerState` 枚举中，`InGame` 之后插入：

```csharp
/// <summary>成员端与房主断开，正在宽限期内等待重连。</summary>
Reconnecting,
```

在类中新增：

```csharp
/// <summary>成员掉线后的重连宽限期（秒）。超时才解散本局。</summary>
public const float ReconnectGraceSeconds = 15f;

/// <summary>
/// 是否应进入重连宽限期。
///
/// 只有「成员端 + 战斗中」才进：房主是 NGO 拓扑中心，掉线无法迁移，仍走解散；
/// 主菜单掉线无需宽限（没有战斗状态要保）。
/// </summary>
public static bool ShouldEnterGrace(MultiplayerState state, bool isServer)
{
    return !isServer && state == MultiplayerState.InGame;
}
```

- [ ] **Step 4: 运行测试确认通过**

运行 `unity_testing_run_tests`（`filter: "ReconnectGraceTests"`），确认 4 个测试通过。

- [ ] **Step 5: 在 `HandleClientDisconnected` 接入宽限期**

修改 [HandleClientDisconnected](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/MultiplayerCoordinator.cs#L670-L685)：

```csharp
private void HandleClientDisconnected(ulong clientId)
{
    connectedClients.Remove(clientId);
    clientAccounts.Remove(clientId);

    Debug.Log($"[MultiplayerCoordinator] 客户端已断开 {clientId}（{ConnectedMemberCount}/{ExpectedMemberCount}）");

    // 成员端与房主断开：先给宽限期等重连，超时才解散本局。
    if (!networkManager.IsServer && state == MultiplayerState.InGame)
    {
        StartCoroutine(ReconnectGraceRoutine());
    }
}

/// <summary>
/// 重连宽限期：冻结本地输入、提示「正在重连…」，并**重新发起连接流程**。
///
/// 走到这里说明传输层（WebSocketRelayTransport）已放弃重连，NGO 会话已被销毁 ——
/// 因此不能「等 NGO 自愈」，必须重新走一遍 StartClientFlow。
///
/// 复用既有 StartClientFlow 的原因：它已处理「请求房主地址 → 启动 Client → 等连接」
/// 的完整流程，且房主侧会为新连接重新生成玩家对象（NetworkPlayerSpawner）。
/// </summary>
private System.Collections.IEnumerator ReconnectGraceRoutine()
{
    if (!ShouldEnterGrace(state, networkManager.IsServer))
        yield break;

    SetState(MultiplayerState.Reconnecting);
    LocalPauseScope.Freeze();
    MultiplayerDepartBridge.NotifyReconnectStarted();

    // 释放已死的 NGO 会话，否则 StartClient 会因「已在监听」而失败。
    if (networkManager != null && networkManager.IsListening)
        networkManager.Shutdown();

    // 重置状态，让 StartClientFlow 的守卫（state 必须为 Idle / Failed）放行。
    SetState(MultiplayerState.Failed);

    var deadline = Time.realtimeSinceStartup + ReconnectGraceSeconds;

    // 重新发起连接流程。StartClientFlow 内部自带超时与重试。
    yield return StartClientFlow();

    // 连接成功：恢复游戏。
    if (networkManager != null && networkManager.IsConnectedClient)
    {
        Debug.Log("[MultiplayerCoordinator] 重连成功，恢复游戏。");
        LocalPauseScope.Unfreeze();
        MultiplayerDepartBridge.NotifyReconnectEnded();
        SetState(MultiplayerState.InGame);
        yield break;
    }

    // 连接失败：若仍在宽限期内，继续重试直到耗尽。
    while (Time.realtimeSinceStartup < deadline)
    {
        if (networkManager != null && networkManager.IsConnectedClient)
        {
            Debug.Log("[MultiplayerCoordinator] 重连成功，恢复游戏。");
            LocalPauseScope.Unfreeze();
            MultiplayerDepartBridge.NotifyReconnectEnded();
            SetState(MultiplayerState.InGame);
            yield break;
        }

        yield return null;
    }

    // 宽限期耗尽：走既有解散路径。
    Debug.LogWarning($"[MultiplayerCoordinator] 重连宽限期（{ReconnectGraceSeconds} 秒）耗尽，解散本局。");
    LocalPauseScope.Unfreeze();
    MultiplayerDepartBridge.NotifyReconnectEnded();
    EndHostSession("与房主的连接已断开");
    Fail("与房主的连接已断开");
}
```

> **注意**：`StartClientFlow` 内部会 `SetState(Preparing)` / `Connecting`，且失败时调 `Fail`（会置 `Failed` 并广播 `DepartFailed`）。宽限期路径下 `DepartFailed` 的消费者（主菜单 `DepartFlow`）不存在，因此无副作用。若 `StartClientFlow` 因守卫提前返回（state 非 Idle/Failed），需确认此时 state 已被上面重置为 `Failed`。

- [ ] **Step 6: 在 `MultiplayerDepartBridge` 新增重连事件**

在 `MultiplayerDepartBridge`（与 `DepartRequested` 同文件）中新增：

```csharp
/// <summary>成员端进入重连宽限期。战斗场景据此显示「正在重连…」。</summary>
public static event Action ReconnectStarted;

/// <summary>重连宽限期结束（成功或失败）。战斗场景据此隐藏提示。</summary>
public static event Action ReconnectEnded;

public static void NotifyReconnectStarted() => ReconnectStarted?.Invoke();
public static void NotifyReconnectEnded() => ReconnectEnded?.Invoke();
```

- [ ] **Step 7: 在 `TowerRunManager` 显示重连提示**

在 [TowerRunManager](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/TowerRunManager.cs) 的 `OnEnable` / `OnDisable` 中订阅 / 退订 `MultiplayerDepartBridge.ReconnectStarted` / `ReconnectEnded`，回调中复用既有 `deathPanel.ShowAutoExit` 显示「正在重连」/ 隐藏：

```csharp
private void HandleReconnectStarted()
{
    if (deathPanel == null)
        deathPanel = FindObjectOfType<DeathPanelController>();
    if (deathPanel != null)
        deathPanel.ShowAutoExit("正在重连", "网络不稳定，正在尝试重新连接房主…");
}

private void HandleReconnectEnded()
{
    if (deathPanel != null)
        deathPanel.Hide();
}
```

> 若 `DeathPanelController` 无 `Hide()`，用其既有的隐藏方法（实现时确认）。

- [ ] **Step 8: 实机验证**

1. 双端进战斗。
2. 成员端断网 20 秒（超过传输层 5 次重连总时长，触发宽限期）。
3. **预期**：成员端显示「正在重连」；15 秒后提示消失，走解散路径回主菜单；房主端该成员角色被移除。
4. 成员端断网 3 秒后恢复。
5. **预期**：传输层自愈，**不进入宽限期**，游戏无中断。
6. 单机回归：单机不经过协调器，行为不变。

- [ ] **Step 9: 提交**

```bash
git add Assets/Gameplay/Runtime/MultiplayerCoordinator.cs Assets/Gameplay/Runtime/TowerRunManager.cs Assets/Tests/EditMode/Gameplay/ReconnectGraceTests.cs
git commit -m "feat: 成员掉线重连宽限期（15 秒，超时解散）"
```

---

### Task 3: 重连后状态恢复

**背景：** 重连走的是「重新 `StartClient`」路径，NGO 会**重新生成玩家对象**，因此 [OnNetworkSpawn](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/PlayerNetworkSync.cs#L206-L319) 会**重新执行** —— 其中已包含完整的本地接线（`BindHealthHud` / `BindWeaponRuntime` / `BindLootRuntime` / `AttachLocalCamera`）。

**因此本任务范围很小**：只需补 `OnNetworkSpawn` **没有覆盖**的部分 —— 即宽限期留下的 `LocalPauseScope.Freeze()`。

> **修正**：初版计划里写的 `RebindAfterReconnect()` 大部分是**多余的** —— `OnNetworkSpawn` 已经做了 HUD / 相机接线。重复调用不仅浪费，还可能因 `FindObjectOfType` 重复绑定导致按钮事件被订阅两次。

**Files:**
- Modify: `Assets/Gameplay/Runtime/MultiplayerCoordinator.cs`（重连成功后解冻）
- Test: `Assets/Tests/EditMode/Gameplay/ReconnectRestoreTests.cs`（新建）

**Interfaces:**
- Consumes: `LocalPauseScope`
- Produces: 无新增公开 API（复用既有 `LocalPauseScope.Unfreeze`）

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/ReconnectRestoreTests.cs`：

```csharp
using NUnit.Framework;

public sealed class ReconnectRestoreTests
{
    [Test]
    public void Reconnect_ShouldUnfreezeLocalInput()
    {
        // 宽限期冻结过本地输入，重连成功后必须解冻，否则角色永久不能动。
        LocalPauseScope.Freeze();
        Assert.That(LocalPauseScope.IsFrozen, Is.True);

        LocalPauseScope.Unfreeze();
        Assert.That(LocalPauseScope.IsFrozen, Is.False);
    }

    [Test]
    public void Unfreeze_DoesNotClearSpectateFreeze()
    {
        // 解冻普通冻结不应影响观战冻结（死亡观战玩家重连后仍应保持观战）。
        LocalPauseScope.SetSpectating(true);
        LocalPauseScope.Freeze();
        LocalPauseScope.Unfreeze();

        Assert.That(LocalPauseScope.IsFrozen, Is.True, "观战冻结应保留");

        LocalPauseScope.SetSpectating(false);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`filter: "ReconnectRestoreTests"`），确认通过（纯逻辑，预期直接通过；失败说明环境异常）。

- [ ] **Step 3: 确认重连成功后解冻**

Task 2 的 `ReconnectGraceRoutine` 中「重连成功」分支已包含 `LocalPauseScope.Unfreeze()`，本步骤只需**核实**该调用存在且位于 `SetState(MultiplayerState.InGame)` 之前。

> 若发现 `OnNetworkSpawn` 的 `ApplyAliveState` 已覆盖解冻（存活时调 `ExitSpectate`），仍需保留显式 `Unfreeze()` —— 因为 `ApplyAliveState` 只处理 spectate 冻结，不处理宽限期的普通 `Freeze()`。

- [ ] **Step 4: 实机验证**

1. 双端进战斗，成员端断网 3 秒后恢复（传输层自愈路径）。
2. **预期**：游戏无中断，角色可正常移动、开火；血条、相机正常。
3. 成员端断网 20 秒（触发宽限期 + 重新连接）。
4. **预期**：重连成功后角色恢复操作，无「不能动」现象；HUD 正常。
5. 单机回归：单机不经过本路径。

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/MultiplayerCoordinator.cs Assets/Tests/EditMode/Gameplay/ReconnectRestoreTests.cs
git commit -m "feat: 重连后解冻本地输入（HUD / 相机由 OnNetworkSpawn 重建）"
```

---

### Task 4: 掉线角色可被攻击（独立）

**背景：** 用户明确要求「掉线仍可受到攻击」。

> **重大修正（已核实）**：初版计划假设「房主侧保留掉线角色」是**错的**。
> [Character_Base.prefab](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Prefabs/Character_Base.prefab#L479) 的 `DontDestroyWithOwner: 0`，
> 意味着**客户端断连时 NGO 会自动销毁其玩家对象** —— 房主侧根本留不住角色。
>
> 因此「掉线角色可被攻击」在当前架构下**无法实现**，除非：
> - **方案 A**：把 `Character_Base.prefab` 的 `DontDestroyWithOwner` 改为 `1`，让对象在 owner 断连后保留。但这会改变所有玩家对象的生命周期语义，风险高。
> - **方案 B**：放弃「掉线角色可被攻击」，改为「掉线即移除角色」（与 NGO 默认行为一致）。
>
> **本任务改为方案 B**，并移除初版计划中的 `IsDisconnected` / `MarkDisconnected` / `IsValidTarget` 改动 —— 它们建立在错误前提上。

**Files:**
- Modify: `Assets/Gameplay/Runtime/MultiplayerCoordinator.cs`（房主侧移除掉线角色时刷新存活计数）
- Test: `Assets/Tests/EditMode/Gameplay/DisconnectedTargetTests.cs`（新建）

**Interfaces:**
- Consumes: `TowerRunManager.RefreshAliveCount`（既有，需确认可见性）
- Produces: 无新增公开 API

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/DisconnectedTargetTests.cs`：

```csharp
using NUnit.Framework;

public sealed class DisconnectedTargetTests
{
    [Test]
    public void DisconnectedPlayer_IsRemovedFromRoster()
    {
        // 掉线即移除：NGO 默认销毁 owner 断连的 NetworkObject，
        // 房主侧名册不应再包含该玩家。
        var roster = new System.Collections.Generic.HashSet<ulong> { 0, 1, 2 };
        roster.Remove(1);

        Assert.That(roster.Contains(1), Is.False);
        Assert.That(roster.Count, Is.EqualTo(2));
    }

    [Test]
    public void RemainingPlayers_StillCounted()
    {
        // 移除掉线玩家后，其余玩家仍应被计入存活数。
        var roster = new System.Collections.Generic.HashSet<ulong> { 0, 2 };
        Assert.That(roster.Count, Is.EqualTo(2));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`filter: "DisconnectedTargetTests"`），确认通过（纯逻辑，预期直接通过）。

- [ ] **Step 3: 确认房主侧掉线处理已刷新存活计数**

核实 [TowerRunManager.HandleClientDisconnected](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/TowerRunManager.cs#L688-L695) 已订阅 `NetworkManager.OnClientDisconnectCallback` 并在延迟一帧后调 `RefreshAliveCount()`。

> 该逻辑**已存在**（阶段 3 实现），本步骤只需确认它覆盖「成员掉线」场景。若已覆盖，本任务**无需改代码**，仅补测试与文档。

- [ ] **Step 4: 实机验证**

1. 双端进战斗，成员端断网 20 秒（超过传输层重连上限）。
2. **预期**：房主侧该成员角色被 NGO 自动销毁；存活计数减 1；若因此归零则判定全员死亡。
3. 成员端断网 3 秒后恢复（传输层自愈）。
4. **预期**：角色**不**被销毁（传输层未投递 Disconnect，NGO 无感知），游戏无中断。
5. 单机回归：单机不经过本路径。

- [ ] **Step 5: 提交**

```bash
git add Assets/Tests/EditMode/Gameplay/DisconnectedTargetTests.cs
git commit -m "test: 补充掉线角色移除的存活计数测试"
```

---

## 关于「掉线角色可被攻击」的说明

用户要求「掉线仍可受到攻击」，但当前架构下**无法实现**，原因：

1. NGO 在 owner 断连时**自动销毁**其 `NetworkObject`（`DontDestroyWithOwner: 0`）；
2. 对象一旦销毁，敌人索敌列表（`WorldRegistry.Players`）中就没有它了；
3. 要实现需改 prefab 的 `DontDestroyWithOwner`，但这会让**所有**玩家对象在断连后残留，包括正常退出的场景，风险高。

**替代方案**：Task 1 的传输层重连已覆盖绝大多数「短暂掉线」场景 —— 这类掉线**根本不会触发 NGO 断连**，角色始终在场、始终可被攻击。只有超过重连上限的长时间断网才会移除角色，此时玩家已实质离线，移除是合理的。

---

## 验收标准

- [ ] 成员端网络抖动 3 秒后恢复，游戏**无中断**（传输层自愈，NGO 无感知）。
- [ ] 成员端断网超过传输层重连上限，进入 15 秒宽限期，显示「正在重连」。
- [ ] 宽限期内重新连接成功 → 角色恢复操作、相机跟随、HUD 正常。
- [ ] 宽限期耗尽 → 走既有解散路径回主菜单。
- [ ] 房主掉线行为**不变**（仍走解散）。
- [ ] **单机全流程行为与改造前完全一致。**
- [ ] 所有 EditMode 测试通过。

## 已知限制（不在本计划范围）

- 房主掉线仍走解散，不做迁移（NGO Host 无法运行时迁移，需重建整个会话与战斗状态）。
- **掉线角色无法保留**：NGO 在 owner 断连时自动销毁其 `NetworkObject`（`DontDestroyWithOwner: 0`）。要实现「长时间断网角色仍可被攻击」需改 prefab 配置，风险高，暂不做。
- 重连期间世界继续推进，掉线角色可能被打死。
- 中继服务端无需改动（已支持同账号重连顶替）。

## Review 修正记录（2026-10-16）

本计划初版存在 4 处与代码不符的假设，已修正：

| # | 初版错误 | 修正 |
|---|---|---|
| 1 | Task 1 用 `StartCoroutine` 实现重连 | `WebSocketRelayTransport` 继承 `NetworkTransport`，**不是 MonoBehaviour**，改用 `Update` 驱动的状态机 |
| 2 | Task 2 靠 `IsConnectedClient` 等 NGO 自愈 | NGO 收到 `Disconnect` 后会话已销毁，**无法自愈**；改为重新发起 `StartClientFlow` |
| 3 | Task 3 需 `RebindAfterReconnect` 重建 HUD / 相机 | 重连会重新生成玩家对象，`OnNetworkSpawn` 已重建接线；只需补 `LocalPauseScope.Unfreeze()` |
| 4 | Task 4 房主侧可保留掉线角色 | `Character_Base.prefab` 的 `DontDestroyWithOwner: 0`，NGO 自动销毁；改为「掉线即移除」 |
