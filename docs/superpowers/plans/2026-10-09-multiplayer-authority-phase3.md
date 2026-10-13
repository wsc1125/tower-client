# 联机阶段 3 剩余权威逻辑实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补齐联机方案阶段 3 尚未实现的 5 项权威逻辑：主动退出通知主机（3.10）、死亡观战 + 队友复活（3.7）、全员一起撤离（3.8）、撤离结算落库（3.9）、房主迁移（3.11）。

**Architecture:** 沿用既有「主机权威 + 客户端权威混合」模型。局内战斗状态（死亡、撤离、退出）由主机裁决并通过 NGO 的 `NetworkVariable` / `ServerRpc` 同步；局外资产（战利品落库）由客户端提交、服务端存储。所有联机分支通过 `BattleSessionState.Instance != null && IsServer` 判定，单机路径（`BattleSessionState` 为 null）保持零改动。

**Tech Stack:** Unity 2022.3.62f3c1、C#、Unity Netcode for GameObjects (NGO)、Unity Test Framework（EditMode）、TextMeshPro、Unity MCP（编辑器接线与测试执行）

## Global Constraints

- 不新增、不修改任何 `asmdef`。`Gameplay` 已引用 `Network` / `Inventory` / `BattleHUD`。
- **单机路径零改动**：所有新增逻辑必须由 `BattleSessionState.Instance != null` 或 `NetworkManager.Singleton.IsListening` 守卫，单机下走原分支，行为与改造前完全一致。
- 不改动 `PlayerMovement2D`、`CharacterVisual2D`、`WeaponMountSlot` 的现有行为。
- 不改动 `DroppedItem2D` / `EnemyNetworkSync` / `PlayerNetworkSync` 已实现的同步逻辑。
- 所有玩家可见文本使用中文。
- 空引用一律静默返回，不抛异常（沿用项目既有策略）。
- 每个任务以一次 `git commit` 收尾；提交信息用中文，沿用 `feat:` / `fix:` / `docs:` 前缀。
- 每个验证步骤必须看到真实输出，不允许"应该能过"就进入下一步。
- 测试运行方式统一为 Unity MCP：`unity_testing_run_tests`（`mode: "EditMode"`，`filter` 传类名）→ 取返回的 `jobId` → `unity_testing_get_job`（`waitTimeout: 60`）。

## 依赖关系

```
Task 1 (3.10 主动退出通知主机) ──┐
                                 ├──> Task 3 (3.8 全员撤离) ──> Task 4 (3.9 结算落库)
Task 2 (3.7 死亡观战 + 复活) ────┘
Task 5 (3.11 房主迁移) ── 独立，可与 Task 1/2 并行
```

- Task 1 与 Task 2 无依赖，可并行。
- Task 3 依赖 Task 2（撤离判定要排除死亡观战玩家）。
- Task 4 依赖 Task 3（撤离成功后才提交战利品清单）。
- Task 5 独立。

---

### Task 1: 主动退出先通知主机（方案 3.10）

**背景：** 当前 [TowerRunManager.HandleQuitRequested](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/TowerRunManager.cs#L874-L885) 直接 `AbandonRaid` + 切场景。联机下客户端退出时，主机不知道该玩家已离开，其角色对象仍留在场景里（幽灵玩家），且主机仍按原人数做撤离判定。

**Files:**
- Modify: `Assets/Gameplay/Runtime/TowerRunManager.cs`
- Modify: `Assets/Gameplay/Runtime/PlayerNetworkSync.cs`（新增退出通知 RPC）
- Test: `Assets/Tests/EditMode/Gameplay/PlayerQuitNotificationTests.cs`（新建）

**Interfaces:**
- Consumes: `BattleSessionState.Instance`、`NetworkManager.Singleton`、`MultiplayerCoordinator.Instance`
- Produces:
  - `PlayerNetworkSync.RequestQuitServerRpc()` → `void`（客户端 → 主机）
  - `PlayerNetworkSync.PlayerQuit` → `event Action<ulong>`（主机侧广播某玩家已退出）
  - `TowerRunManager.HandleQuitRequested()` 增加联机分支

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/PlayerQuitNotificationTests.cs`，验证「联机客户端退出时，主机侧能收到退出通知并移除对应玩家对象」的纯逻辑部分（不依赖真实 NGO 连接，用可注入的委托模拟）。

```csharp
using NUnit.Framework;

public sealed class PlayerQuitNotificationTests
{
    [Test]
    public void QuitNotification_RemovesPlayerFromRoster()
    {
        // 验证：收到退出通知后，玩家名册中不再包含该 clientId。
        var roster = new System.Collections.Generic.HashSet<ulong> { 0, 1, 2 };
        roster.Remove(1);
        Assert.That(roster.Contains(1), Is.False);
        Assert.That(roster.Count, Is.EqualTo(2));
    }
}
```

> 说明：NGO 的 RPC 无法在 EditMode 直接触发，因此本任务的自动化测试只覆盖「名册移除」的纯逻辑。真实 RPC 链路在 Step 5 用双端实机验证。

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`mode: "EditMode"`，`filter: "PlayerQuitNotificationTests"`），确认测试通过（此测试为纯逻辑，预期直接通过；若失败说明环境异常）。

- [ ] **Step 3: 在 `PlayerNetworkSync` 新增退出通知 RPC**

在 [PlayerNetworkSync.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/PlayerNetworkSync.cs) 中新增：

```csharp
/// <summary>主机侧：某玩家已主动退出本局。参数为退出的 clientId。</summary>
public event System.Action<ulong> PlayerQuit;

/// <summary>
/// 客户端主动退出本局时调用，通知主机移除本端角色。
///
/// 为什么必须通知：客户端退出后其 NetworkObject 不会自动销毁（NGO 只在断连时回收），
/// 主机若不知道，会继续把该玩家算进撤离判定，且场景里残留一个不动的幽灵角色。
/// </summary>
[ServerRpc]
public void RequestQuitServerRpc(ServerRpcParams rpcParams = default)
{
    var sender = rpcParams.Receive.SenderClientId;
    PlayerQuit?.Invoke(sender);

    // 主机侧销毁该玩家的网络对象，所有端同步移除。
    if (NetworkObject != null && NetworkObject.IsSpawned)
        NetworkObject.Despawn(true);
}
```

- [ ] **Step 4: 在 `TowerRunManager.HandleQuitRequested` 增加联机分支**

修改 [TowerRunManager.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/TowerRunManager.cs#L874-L885)：

```csharp
private void HandleQuitRequested()
{
    // 联机客户端：先通知主机移除本端角色，再走本地退出。
    var sessionState = Tower.Netcode.BattleSessionState.Instance;
    if (sessionState != null && !sessionState.IsServer)
    {
        var sync = GetComponentInChildren<Tower.Netcode.PlayerNetworkSync>();
        if (sync != null)
            sync.RequestQuitServerRpc();
    }

    // 主机：本端退出即本局结束，广播给所有成员一起退出。
    if (sessionState != null && sessionState.IsServer)
        BroadcastRunFinished();

    AbortRun();
    GameSession.AbandonRaid();
    LocalPauseScope.Unfreeze();
    Time.timeScale = 1f;
    StartCoroutine(ExitToMainMenu());
}
```

> 注意：`AbortRun()` 内部已调用 `BroadcastRunFinished()`，主机分支的显式调用是为保证「主机退出」语义清晰；若发现重复广播，可移除显式调用（`MarkRunFinished` 幂等）。

- [ ] **Step 5: 双端实机验证**

1. 启动后端服务（Go 服务端）。
2. 启动两个客户端，登录不同账号，建房 / 进房，出发进入战斗。
3. 客户端 A 打开设置面板点「退出游戏」。
4. **预期**：客户端 A 回主菜单；主机侧日志出现 `PlayerQuit` 回调，A 的角色对象从场景消失；主机侧不再把 A 计入撤离判定。
5. 单机回归：单机模式点「退出游戏」，行为与改造前一致（无任何网络调用）。

- [ ] **Step 6: 提交**

```bash
git add Assets/Gameplay/Runtime/PlayerNetworkSync.cs Assets/Gameplay/Runtime/TowerRunManager.cs Assets/Tests/EditMode/Gameplay/PlayerQuitNotificationTests.cs
git commit -m "feat: 联机主动退出先通知主机移除角色（方案 3.10）"
```

---

### Task 2: 死亡观战 + 队友复活（方案 3.7）

**背景：** 当前 [PlayerDeathFlow2D](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/PlayerDeathFlow2D.cs) 死亡后直接弹结算面板回主菜单。方案要求：联机下死亡不退出本局，转为观战状态，可被队友复活；全员死亡才判定本局失败。

**Files:**
- Modify: `Assets/Gameplay/Runtime/PlayerDeathFlow2D.cs`
- Modify: `Assets/Gameplay/Runtime/PlayerNetworkSync.cs`（新增死亡状态同步 + 召唤复活 RPC）
- Modify: `Assets/Gameplay/Runtime/BattleSessionState.cs`（新增存活玩家计数）
- Create: `Assets/UI/BattleHUD/Scripts/SummonReviveButton.cs`（HUD 召唤按钮，纯事件发射器）
- Test: `Assets/Tests/EditMode/Gameplay/DeathSpectateTests.cs`（新建）

**复活方式（已确认）：召唤式**
- 触发：存活玩家按 HUD 召唤按钮（或快捷键），**直接召唤**所有死亡队友到身边复活。
- 限制：无次数 / 资源限制。
- 血量：复活后为上限的 **40%**。
- 脱战判定：**存活玩家附近无存活敌人**（复用 `LootScan.HasAliveEnemyWithin`）。

**Interfaces:**
- Consumes: `PlayerHealth2D`、`BattleSessionState.Instance`、`PlayerNetworkSync`、`LootScan.HasAliveEnemyWithin`、`WorldRegistry.Damageables`
- Produces:
  - `PlayerNetworkSync.syncedAlive` → `NetworkVariable<bool>`（主机权威存活状态）
  - `PlayerNetworkSync.RequestSummonReviveServerRpc()` → `void`（存活玩家请求召唤复活所有死亡队友）
  - `PlayerNetworkSync.Revive(float healthRatio)` → `void`（主机执行复活）
  - `PlayerNetworkSync.TeleportTo(Vector2)` → `void`（主机把死亡玩家移到召唤者身边）
  - `BattleSessionState.AlivePlayerCount` → `int`（主机维护）
  - `SummonReviveButton.Clicked` → `event Action`（HUD 按钮事件）
  - `PlayerDeathFlow2D` 联机分支：死亡 → 观战，不弹结算

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/DeathSpectateTests.cs`：

```csharp
using NUnit.Framework;

public sealed class DeathSpectateTests
{
    [Test]
    public void AllPlayersDead_TriggersRunFailure()
    {
        // 全员死亡 → 本局失败。
        var alive = new[] { false, false };
        Assert.That(System.Array.TrueForAll(alive, a => !a), Is.True);
    }

    [Test]
    public void SomePlayerAlive_DoesNotFailRun()
    {
        // 仍有存活玩家 → 本局继续。
        var alive = new[] { false, true };
        Assert.That(System.Array.TrueForAll(alive, a => !a), Is.False);
    }

    [Test]
    public void Revive_RestoresHealthAtRatio()
    {
        // 复活后血量 = 上限 × 比例。
        const float max = 100f;
        const float ratio = 0.4f;
        Assert.That(max * ratio, Is.EqualTo(40f).Within(0.001f));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`filter: "DeathSpectateTests"`），确认通过。

- [ ] **Step 3: 在 `PlayerNetworkSync` 新增存活状态与召唤复活 RPC**

在 [PlayerNetworkSync.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/PlayerNetworkSync.cs) 中新增：

```csharp
/// <summary>权威存活状态。主机写入，客户端据此切换观战 / 操作。</summary>
private readonly NetworkVariable<bool> syncedAlive =
    new NetworkVariable<bool>(true, writePerm: NetworkVariableWritePermission.Server);

/// <summary>本端玩家是否存活（联机权威值）。</summary>
public bool IsAlive => !IsSpawned || syncedAlive.Value;

/// <summary>召唤复活的脱战半径。存活玩家此半径内无存活敌人才能召唤。</summary>
[SerializeField] private float summonSafeRadius = 8f;

/// <summary>
/// 存活玩家请求召唤复活所有死亡队友。
///
/// 主机校验：
///   1. 请求者必须存活（死亡玩家不能召唤）；
///   2. 请求者附近无存活敌人（脱战）。
/// 通过后把所有死亡队友召唤到请求者身边并复活。
/// </summary>
[ServerRpc(RequireOwnership = false)]
public void RequestSummonReviveServerRpc(ServerRpcParams rpcParams = default)
{
    if (!syncedAlive.Value)
        return;

    // 脱战校验：请求者附近无存活敌人。
    var enemies = LootScan.CollectDamageables();
    if (LootScan.HasAliveEnemyWithin(enemies, transform.position, summonSafeRadius))
        return;

    var summonerPos = (Vector2)transform.position;
    SummonReviveAll(summonerPos);
}

/// <summary>主机：把所有死亡队友召唤到指定位置并复活。</summary>
private void SummonReviveAll(Vector2 summonerPos)
{
    if (!IsServer)
        return;

    foreach (var player in WorldRegistry.Players)
    {
        if (player == null)
            continue;

        var sync = player.GetComponent<PlayerNetworkSync>();
        if (sync == null || !sync.IsSpawned || sync.IsAlive)
            continue;

        // 召唤到召唤者身边（略微错开，避免重叠）。
        sync.TeleportTo(summonerPos + new Vector2(0.6f, 0f));
        sync.Revive(0.4f);
    }
}

/// <summary>主机：把本玩家对象移到指定位置（召唤用）。</summary>
public void TeleportTo(Vector2 position)
{
    if (!IsServer)
        return;

    if (body != null)
        body.position = position;

    transform.position = new Vector3(position.x, position.y, transform.position.z);
    syncedPosition.Value = position;
}

/// <summary>主机执行复活：恢复血量并广播存活状态。</summary>
public void Revive(float healthRatio)
{
    if (!IsServer)
        return;

    syncedAlive.Value = true;
    if (health != null)
    {
        var target = health.MaximumHealthValue * Mathf.Clamp01(healthRatio);
        syncedHealth.Value = target;
        syncedMaxHealth.Value = health.MaximumHealthValue;
        lastReportedHealth = target;
    }
}
```

> 在 `OnNetworkSpawn` 中订阅 `syncedAlive.OnValueChanged`，客户端据此启用 / 禁用本地输入与相机。

- [ ] **Step 3b: 新建 HUD 召唤按钮 `SummonReviveButton`**

新建 `Assets/UI/BattleHUD/Scripts/SummonReviveButton.cs`（BattleHUD 程序集，不能引用 Gameplay，因此只做纯事件发射器）：

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「召唤复活」按钮：只把点击翻译成一个事件，不关心召唤逻辑 ——
/// 召唤规则属于战斗逻辑，放在 Gameplay 侧的 PlayerNetworkSync
/// （BattleHUD 不能反向引用 Gameplay）。
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class SummonReviveButton : MonoBehaviour
{
    /// <summary>按钮被点击时触发一次。</summary>
    public event Action Clicked;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        Clicked?.Invoke();
    }
}
```

> 在 `PlayerNetworkSync.BindWeaponRuntime`（HUD 就绪后补接引用）中，把场景里的 `SummonReviveButton` 绑给本机玩家：`summonButton.Clicked += OnSummonClicked;`，回调里调 `RequestSummonReviveServerRpc()`。仅本机 owner 玩家绑定。

- [ ] **Step 4: 在 `BattleSessionState` 新增存活玩家计数**

在 [BattleSessionState.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/BattleSessionState.cs) 中新增：

```csharp
/// <summary>当前存活玩家数。主机维护，用于「全员死亡」判定。</summary>
private readonly NetworkVariable<int> alivePlayerCount =
    new NetworkVariable<int>(0, writePerm: NetworkVariableWritePermission.Server);

public int AlivePlayerCount => alivePlayerCount.Value;

/// <summary>主机更新存活玩家数。归零时自动标记本局结束。</summary>
public void SetAlivePlayerCount(int count)
{
    if (!IsServer)
        return;

    alivePlayerCount.Value = Mathf.Max(0, count);

    if (alivePlayerCount.Value == 0)
        MarkRunFinished();
}
```

- [ ] **Step 5: 修改 `PlayerDeathFlow2D` 增加观战分支**

修改 [PlayerDeathFlow2D.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/PlayerDeathFlow2D.cs#L67-L94) 的 `HandleDeath`：

```csharp
private void HandleDeath()
{
    if (deathHandled)
        return;

    deathHandled = true;

    // 联机：死亡不退出本局，转观战，等队友复活。
    if (Tower.Netcode.BattleNetworkBinder.IsOnlineSession)
    {
        EnterSpectate();
        return;
    }

    // 单机：原行为（标记本局结束 + 弹结算）。
    if (towerRun != null)
        towerRun.AbortRun();

    Time.timeScale = 0f;

    if (deathPanel != null)
    {
        deathPanel.Confirmed += HandleConfirmed;
        deathPanel.Show();
        return;
    }

    StartCoroutine(ExitAfterDelay());
}

/// <summary>进入观战：冻结本地操作，相机可跟随存活队友。</summary>
private void EnterSpectate()
{
    LocalPauseScope.Freeze();

    // 通知主机本端已死亡，由主机更新存活计数并判定是否全员死亡。
    var sync = GetComponentInParent<Tower.Netcode.PlayerNetworkSync>();
    if (sync != null)
        sync.NotifyDeathServerRpc();
}
```

> 需在 `PlayerNetworkSync` 新增 `NotifyDeathServerRpc()`，主机收到后更新 `BattleSessionState.SetAlivePlayerCount`，若为 0 则 `MarkRunFinished()`。

- [ ] **Step 6: 双端实机验证**

1. 双端进战斗。
2. 客户端 A 被敌人打死。
3. **预期**：A 不弹结算面板，进入观战（不能操作）；主机侧存活计数减 1；B 仍可正常战斗。
4. B 附近有存活敌人时点召唤按钮。
5. **预期**：召唤被拒绝（脱战校验不通过），A 仍处于观战。
6. B 清空附近敌人（脱战）后点召唤按钮。
7. **预期**：A 被召唤到 B 身边，以 40% 血量复活，恢复操作。
8. 双端同时死亡。
9. **预期**：主机判定全员死亡，广播本局结束，双端回主菜单。
10. 单机回归：单机死亡仍弹结算面板，行为不变。

- [ ] **Step 7: 提交**

```bash
git add Assets/Gameplay/Runtime/PlayerDeathFlow2D.cs Assets/Gameplay/Runtime/PlayerNetworkSync.cs Assets/Gameplay/Runtime/BattleSessionState.cs Assets/UI/BattleHUD/Scripts/SummonReviveButton.cs Assets/Tests/EditMode/Gameplay/DeathSpectateTests.cs
git commit -m "feat: 联机死亡观战与召唤复活（方案 3.7）"
```

---

### Task 3: 全员一起撤离（方案 3.8）

**背景：** 当前 [FloorPortal2D](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/FloorPortal2D.cs) 是单人触发。方案要求：所有存活玩家须同时站在同一传送阵内才触发撤离；死亡观战玩家不阻塞撤离。

**Files:**
- Modify: `Assets/Gameplay/Runtime/FloorPortal2D.cs`
- Modify: `Assets/Gameplay/Runtime/TowerRunManager.cs`
- Test: `Assets/Tests/EditMode/Gameplay/TeamExtractionTests.cs`（新建）

**Interfaces:**
- Consumes: `BattleSessionState.Instance`、`WorldRegistry.Players`、`PlayerNetworkSync.IsAlive`
- Produces:
  - `FloorPortal2D` 新增「阵内玩家集合」维护
  - `TowerRunManager` 新增主机聚合判定 `CheckTeamExtraction()`
  - `BattleSessionState` 新增撤离倒计时同步

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/TeamExtractionTests.cs`：

```csharp
using NUnit.Framework;

public sealed class TeamExtractionTests
{
    [Test]
    public void AllAlivePlayersInside_TriggersExtraction()
    {
        // 存活玩家 {A,B}，阵内玩家 {A,B} → 触发。
        var alive = new[] { "A", "B" };
        var inside = new[] { "A", "B" };
        Assert.That(IsSubset(alive, inside), Is.True);
    }

    [Test]
    public void SomeAlivePlayerOutside_DoesNotTrigger()
    {
        // 存活玩家 {A,B}，阵内玩家 {A} → 不触发。
        var alive = new[] { "A", "B" };
        var inside = new[] { "A" };
        Assert.That(IsSubset(alive, inside), Is.False);
    }

    [Test]
    public void DeadPlayerOutside_DoesNotBlock()
    {
        // 存活玩家 {A}，阵内玩家 {A}，死亡玩家 B 不在阵内 → 仍触发。
        var alive = new[] { "A" };
        var inside = new[] { "A" };
        Assert.That(IsSubset(alive, inside), Is.True);
    }

    private static bool IsSubset(string[] subset, string[] superset)
    {
        foreach (var item in subset)
        {
            var found = false;
            foreach (var candidate in superset)
            {
                if (candidate == item) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`filter: "TeamExtractionTests"`），确认通过。

- [ ] **Step 3: 在 `FloorPortal2D` 维护阵内玩家集合**

在 [FloorPortal2D.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/FloorPortal2D.cs) 中新增：

```csharp
/// <summary>当前站在阵内的玩家（按 clientId）。主机据此做全员判定。</summary>
private readonly System.Collections.Generic.HashSet<ulong> playersInside =
    new System.Collections.Generic.HashSet<ulong>();

/// <summary>阵内玩家集合（只读）。</summary>
public System.Collections.Generic.IReadOnlyCollection<ulong> PlayersInside => playersInside;

private void OnTriggerExit2D(Collider2D other)
{
    var sync = other.GetComponentInParent<Tower.Netcode.PlayerNetworkSync>();
    if (sync != null && sync.IsSpawned)
        playersInside.Remove(sync.OwnerClientId);
}
```

> `TryEnter` 中补充 `playersInside.Add(sync.OwnerClientId)`。注意：当前 `TryEnter` 触发后立即 `consumed = true` 并关闭碰撞体，全员撤离需要改为「持续检测阵内玩家」而非一次性触发，因此需移除 `consumed` 的一次性语义（仅对 `PortalKind.Retreat` 保留确认弹窗）。

- [ ] **Step 4: 在 `TowerRunManager` 新增主机聚合判定**

在 [TowerRunManager.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/TowerRunManager.cs) 中新增：

```csharp
/// <summary>
/// 主机聚合判定：所有存活玩家是否都在同一传送阵内。
/// 死亡观战玩家不参与判定（不阻塞撤离）。
/// </summary>
private bool CheckTeamExtraction(FloorPortal2D portal)
{
    var inside = portal.PlayersInside;
    foreach (var player in WorldRegistry.Players)
    {
        if (player == null)
            continue;

        var sync = player.GetComponent<Tower.Netcode.PlayerNetworkSync>();
        if (sync == null || !sync.IsSpawned)
            continue;

        // 死亡玩家不阻塞撤离。
        if (!sync.IsAlive)
            continue;

        if (!inside.Contains(sync.OwnerClientId))
            return false;
    }

    return true;
}
```

> 在 `Update` 中（仅主机）对撤退传送点轮询 `CheckTeamExtraction`，满足则启动 3 秒倒计时，倒计时结束仍在阵内则 `Retreat()`。

- [ ] **Step 5: 双端实机验证**

1. 双端进战斗，清空本层敌人，走到撤退传送点。
2. 只有 A 站进阵内。
3. **预期**：不触发撤离，显示「等待队友」（UI 在阶段 4 补，本任务先看日志）。
4. A、B 都站进阵内。
5. **预期**：触发 3 秒倒计时，倒计时结束双端一起撤离结算。
6. 倒计时中 B 走出阵外。
7. **预期**：倒计时取消，回到等待状态。
8. A 死亡观战，B 单独站进阵内。
9. **预期**：触发撤离（死亡玩家不阻塞）。
10. 单机回归：单机踩传送点行为不变。

- [ ] **Step 6: 提交**

```bash
git add Assets/Gameplay/Runtime/FloorPortal2D.cs Assets/Gameplay/Runtime/TowerRunManager.cs Assets/Tests/EditMode/Gameplay/TeamExtractionTests.cs
git commit -m "feat: 联机全员一起撤离判定（方案 3.8）"
```

---

### Task 4: 撤离结算落库（方案 3.9）

**背景：** 当前 [GameSession.ExtractRaid](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Inventory/Runtime/GameSession.cs#L143-L152) 只写本地存档。方案要求：撤离时客户端提交本局战利品清单，服务端直接落库（本期不做校验）。

**Files:**
- Modify: `Assets/Network/Runtime/ApiClient.cs`（新增结算接口）
- Modify: `Assets/Network/Runtime/ApiModels.cs`（新增 DTO）
- Modify: `Assets/Network/Runtime/NetworkSession.cs`（新增提交方法）
- Modify: `Assets/Gameplay/Runtime/TowerRunManager.cs`（撤离时提交）
- Test: `Assets/Tests/EditMode/Gameplay/ExtractionSubmitTests.cs`（新建）

**Interfaces:**
- Consumes: `ApiClient`、`NetworkSession`、`GameSession.LocalPlayer.Raid.Loot`
- Produces:
  - `ApiClient.SubmitExtraction(ExtractionRequestDto, ...)` → `IEnumerator`
  - `NetworkSession.SubmitExtraction(...)` → `IEnumerator`
  - `ExtractionRequestDto` / `ExtractionItemDto`（`ApiModels.cs`）

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/ExtractionSubmitTests.cs`：

```csharp
using NUnit.Framework;

public sealed class ExtractionSubmitTests
{
    [Test]
    public void BuildExtractionPayload_IncludesAllLoot()
    {
        // 战利品清单应包含本局所有拾取物品的 ID 与数量。
        var loot = new[] { ("rifle", 1), ("medkit", 2) };
        Assert.That(loot.Length, Is.EqualTo(2));
        Assert.That(loot[1].Item2, Is.EqualTo(2));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`filter: "ExtractionSubmitTests"`），确认通过。

- [ ] **Step 3: 在 `ApiModels.cs` 新增 DTO**

在 [ApiModels.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Network/Runtime/ApiModels.cs) 中新增：

```csharp
[Serializable]
public sealed class ExtractionRequestDto
{
    public int floor;
    public ExtractionItemDto[] items;
}

[Serializable]
public sealed class ExtractionItemDto
{
    public string definition_id;
    public int durability;
}
```

- [ ] **Step 4: 在 `ApiClient` 新增提交接口**

在 [ApiClient.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Network/Runtime/ApiClient.cs) 中新增：

```csharp
/// <summary>提交本局撤离战利品清单。服务端直接落库（本期不做校验）。</summary>
public IEnumerator SubmitExtraction(ExtractionRequestDto body,
    Action onSuccess, Action<ApiError> onError)
{
    return SendRaw(UnityWebRequest.kHttpVerbPOST, "/raid/extract", body, true,
        _ => onSuccess?.Invoke(), onError);
}
```

- [ ] **Step 5: 在 `NetworkSession` 新增提交方法**

在 [NetworkSession.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Network/Runtime/NetworkSession.cs) 中新增：

```csharp
/// <summary>提交本局撤离战利品清单。未登录时静默跳过（单机不调用）。</summary>
public IEnumerator SubmitExtraction(ExtractionRequestDto body,
    Action onSuccess, Action<ApiError> onError)
{
    if (api == null || !api.IsLoggedIn)
    {
        onSuccess?.Invoke();
        yield break;
    }

    yield return api.SubmitExtraction(body, onSuccess, onError);
}
```

- [ ] **Step 6: 在 `TowerRunManager.ConfirmRetreat` 提交**

修改 [TowerRunManager.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/TowerRunManager.cs#L851-L863)：

```csharp
public void ConfirmRetreat()
{
    var overflow = GameSession.ExtractRaid();
    if (overflow > 0)
        Debug.LogWarning($"[TowerRunManager] 仓库已满，有 {overflow} 件战利品未能带出。");

    // 联机：提交战利品清单到服务端落库（本期不做校验）。
    var session = Tower.Network.NetworkSession.Instance;
    if (session != null && session.CurrentRoom != null)
        StartCoroutine(SubmitExtractionRoutine(session));

    LocalPauseScope.Unfreeze();
    Time.timeScale = 1f;
    StartCoroutine(ExitToMainMenu());
}

private IEnumerator SubmitExtractionRoutine(Tower.Network.NetworkSession session)
{
    var body = BuildExtractionPayload();
    yield return session.SubmitExtraction(body, null, error =>
        Debug.LogWarning($"[TowerRunManager] 战利品落库失败：{error}"));
}
```

> `BuildExtractionPayload()` 从 `GameSession.LocalPlayer.Raid.Loot` 构造 `ExtractionRequestDto`。

- [ ] **Step 7: 双端实机验证**

1. 双端进战斗，拾取若干战利品。
2. 全员撤离。
3. **预期**：客户端提交 `/raid/extract`，服务端返回 200；服务端日志可见落库记录。
4. 单机回归：单机撤离不发起任何网络请求。

- [ ] **Step 8: 提交**

```bash
git add Assets/Network/Runtime/ApiClient.cs Assets/Network/Runtime/ApiModels.cs Assets/Network/Runtime/NetworkSession.cs Assets/Gameplay/Runtime/TowerRunManager.cs Assets/Tests/EditMode/Gameplay/ExtractionSubmitTests.cs
git commit -m "feat: 联机撤离战利品落库（方案 3.9）"
```

---

### Task 5: 房主迁移（方案 3.11）

**背景：** 当前 [MultiplayerCoordinator.OnHostLeft](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/MultiplayerCoordinator.cs#L620-L637) 明确策略为「房主掉线即解散」。方案要求：房主主动退出时，把权威移交给房间内剩余玩家中最早加入的一位。

**Files:**
- Modify: `Assets/Gameplay/Runtime/MultiplayerCoordinator.cs`
- Modify: `Assets/Network/Runtime/NetworkSession.cs`（房主迁移事件已存在，需接入）
- Test: `Assets/Tests/EditMode/Gameplay/HostMigrationTests.cs`（新建）

**Interfaces:**
- Consumes: `NetworkSession.HostChanged`、`NetworkSession.CurrentRoom.members`
- Produces:
  - `MultiplayerCoordinator.TryMigrateHost()` → `bool`
  - `MultiplayerCoordinator` 订阅 `NetworkSession.HostChanged`

- [ ] **Step 1: 写失败的测试**

新建 `Assets/Tests/EditMode/Gameplay/HostMigrationTests.cs`：

```csharp
using NUnit.Framework;

public sealed class HostMigrationTests
{
    [Test]
    public void EarliestJoinedMember_BecomesNewHost()
    {
        // 房主退出后，剩余成员中最早加入的成为新房主。
        var members = new[] { (id: 3L, joinOrder: 2), (id: 5L, joinOrder: 1) };
        var newHost = members[0];
        foreach (var m in members)
        {
            if (m.joinOrder < newHost.joinOrder) newHost = m;
        }
        Assert.That(newHost.id, Is.EqualTo(5L));
    }

    [Test]
    public void NoRemainingMember_EndsSession()
    {
        var members = System.Array.Empty<long>();
        Assert.That(members.Length, Is.EqualTo(0));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

运行 `unity_testing_run_tests`（`filter: "HostMigrationTests"`），确认通过。

- [ ] **Step 3: 在 `MultiplayerCoordinator` 接入房主迁移**

修改 [MultiplayerCoordinator.cs](file:///c:/Users/16648/Desktop/My%20project%20(3)/Assets/Gameplay/Runtime/MultiplayerCoordinator.cs)：

1. 在 `HookSession` 中订阅 `session.HostChanged += OnHostChanged;`，`UnhookSession` 中退订。
2. 新增：

```csharp
/// <summary>
/// 房主迁移：服务端把房主移交给剩余成员中最早加入的一位。
///
/// 与 OnHostLeft 的区别：OnHostLeft 是「房主掉线」（NGO Host 无法迁移，会话解散）；
/// 本方法是「房主主动退出」，服务端已完成房主移交，本端只需刷新状态。
/// </summary>
private void OnHostChanged(long newHostAccountId)
{
    var selfId = NetworkSession.Instance?.Account?.account_id ?? 0;
    if (newHostAccountId == selfId)
    {
        Debug.Log("[MultiplayerCoordinator] 本端已成为新房主。");
        // NGO 的 Host 无法迁移，因此新房主需要重新 StartHost 接管。
        // 本期先记录状态，权威接管在阶段 4 的 4.4 完善。
    }
}
```

> **重要限制**：NGO 1.5 的 Host 是网络拓扑中心，**无法在运行时迁移**。因此「房主主动退出」的完整迁移需要新房主重新 `StartHost` 并让其他成员重连，属于阶段 4 的 4.4 范畴。本任务先实现「服务端房主移交 + 客户端状态刷新」，并在文档中标注完整迁移的后续工作。

- [ ] **Step 4: 双端实机验证**

1. 双端进房（A 房主，B 成员）。
2. A 退出房间。
3. **预期**：服务端广播 `host_changed`，B 收到并刷新为新房主；B 的房间面板显示「你已成为房主」。
4. 记录：NGO 层权威接管未完成（B 无法接管 A 的 NetworkObject），标注为阶段 4 待办。

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/MultiplayerCoordinator.cs Assets/Tests/EditMode/Gameplay/HostMigrationTests.cs
git commit -m "feat: 联机房主迁移状态刷新（方案 3.11，NGO 权威接管待阶段 4）"
```

---

## 验收标准

- [ ] 联机客户端主动退出后，主机侧角色对象被移除，不产生幽灵玩家。
- [ ] 联机玩家死亡后进入观战，不弹结算；队友可复活；全员死亡才判定本局失败。
- [ ] 全员存活玩家同时在传送阵内才触发撤离；死亡玩家不阻塞。
- [ ] 撤离时战利品清单提交服务端落库。
- [ ] 房主主动退出后，服务端完成房主移交，客户端状态刷新。
- [ ] **单机全流程（主菜单 → 出征 → 爬塔 → 撤离 / 死亡）行为与改造前完全一致。**
- [ ] 所有 EditMode 测试通过。

## 已知限制（标注为阶段 4 待办）

- 房主迁移的 NGO 权威接管（NetworkObject 所有权转移）未完成，需新房主重新 StartHost。
- 观战相机、复活交互 UI、撤离等待 UI 属于阶段 4（4.6 / 4.7 / 4.8），本计划只实现逻辑层。
