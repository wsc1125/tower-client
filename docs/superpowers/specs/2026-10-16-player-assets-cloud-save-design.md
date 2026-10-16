# 玩家资产云存档设计方案

- 日期：2026-10-16
- 状态：设计已在对话中逐节确认，待最终评审
- 范围：Unity 客户端（本仓库）+ 服务端快照存储模块（现有联机后端加模块，服务端代码不在本仓库）

## 1. 背景与目标

当前玩家资产全部存于本机 PlayerPrefs：

| 资产 | 载体 | 存储位置 |
|------|------|----------|
| 仓库 + 装备槽（含实例 ID、耐久，v3 格式） | `PlayerSaveData` | `PlayerSave` / `PlayerSave:account:*` |
| 收藏进度 | `CollectionSaveData` | PlayerPrefs 独立键 |
| 星空币余额 | `PlayerCurrency`（MonoBehaviour） | `StarCoins` |

存在的问题：换设备/重装即丢失；数据完全由客户端控制，无防篡改基础。

**目标**：资产上服务器，路线为**先云存档、后逐步权威化**。第一期实现快照云存档（客户端权威），接口形状与数据结构为未来服务器权威预留升级空间，中途不返工。

## 2. 已确认的关键决策

| 决策点 | 结论 | 理由 |
|--------|------|------|
| 演进路线 | 先云存档后逐步权威 | 渐进式，不返工 |
| 存储方案 | 整包快照（方案 A） | 服务端单表两个接口，复用现有 v3 存档结构，改动最小；未来权威化时接口形状不变 |
| 放弃的备选 | 逐件存储（方案 B）、BaaS 直连（方案 C） | B 开发量 3~5 倍且当前无交易行需求；C 与自建 JWT 体系冲突且有合规风险 |
| 服务端形态 | 复用现有已部署联机后端加模块 | 鉴权、部署、运维全部现成 |
| 登录门槛 | 必须登录才玩 | 单机/联机共用账号资产；`GameSession.SetLocalPlayerId` 机制已存在，登录后绑定 `account_id` |
| 老档迁移 | **放弃 "local" 档认领迁移**，全新开始，无补偿 | 认领 = 把客户端可篡改数据（注册表明文）注入服务器的通道，可被脚本批量刷资产 |
| 上传时机 | 脏标记 + 去抖 + 关键结算点强制上传 | 见第 6 节 |
| 断网行为 | 只影响上传、不阻塞游玩 | 见第 7 节 |

## 3. 总体架构

```
客户端（Unity）                          服务端（现有联机后端加模块）
┌─────────────────────────┐            ┌──────────────────────┐
│ GameSession / Currency / │            │  JWT 鉴权（已有，复用）│
│ Collection（运行时状态）  │            ├──────────────────────┤
│        │ 变化事件         │            │ /assets/snapshot     │
│        ▼                 │   HTTP     │  GET  下发整包        │
│ AssetSyncService（新）    │ ◄────────► │  PUT  整包 upsert    │
│ 脏标记+去抖+退避重试       │            ├──────────────────────┤
│        ▼                 │            │ player_assets 表     │
│ 本地缓存（现有 PlayerPrefs│            │ （每账号一行 JSON）   │
│ 键，降级为缓存）          │            └──────────────────────┘
└─────────────────────────┘
```

- 第一期**客户端权威**：服务端把快照当不透明 JSON 存取，不做游戏逻辑校验
- 客户端心智模型：**本地 PlayerPrefs 什么时候写，服务端就（去抖后）什么时候有同样的数据**；单机/联机不维护两套上传逻辑
- 联机时资产变化还可能来自网络同步事件，单机只来自本机操作，但落点（缓存 + 上传）相同

## 4. 数据库设计

一张表，每账号一行：

```sql
CREATE TABLE player_assets (
    account_id      BIGINT       PRIMARY KEY,          -- 1:1，FK 指向现有账号表，随账号级联删除
    schema_version  INT          NOT NULL DEFAULT 1,   -- 快照信封版本，服务端只透传
    data            JSONB        NOT NULL,             -- 整包快照（MySQL 用 JSON，SQLite 用 TEXT）
    revision        BIGINT       NOT NULL DEFAULT 1,   -- 服务端自增，每次 PUT +1
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now()
);
```

PUT 的 upsert 语句（PostgreSQL 示例）：

```sql
INSERT INTO player_assets (account_id, schema_version, data, revision)
VALUES ($1, $2, $3, 1)
ON CONFLICT (account_id) DO UPDATE SET
    schema_version = EXCLUDED.schema_version,
    data           = EXCLUDED.data,
    revision       = player_assets.revision + 1,
    updated_at     = now();
```

| 决策 | 理由 |
|------|------|
| 仓库/装备/收藏/货币合并为单个 JSON 存一行 | GET/PUT 天然原子，一个接口搞定；拆表就要拆接口、拆事务 |
| 不建 `item_instance` 逐件表 | 属于权威化/交易行阶段的事；`data` 内已带实例 ID，未来拆分时可从历史快照回填 |
| 不建操作日志表 | 第一期无审计需求 |
| `revision` 列 | 现在用于排查"谁覆盖了谁"；未来直接升级为 If-Match 乐观锁，不改表 |
| `account_id` 一律取自 JWT | 永不信任请求体中的账号字段 |

## 5. API 契约

```
GET /assets/snapshot          （Authorization: Bearer <access_token>）
  200 → { "schema_version": 1, "revision": 7, "data": {…} }
        服务端尚无快照（首次登录）时返回 revision=0 且可省略 data，
        客户端走初始装备流程并回传一次 PUT 建档
        注意：JsonUtility 会把 "data":null 物化成完整空对象图（连内层字段都非 null），
        因此「尚无快照」的判定信号是 revision == 0，而不是 JSON 的 null。

PUT /assets/snapshot          （Authorization: Bearer <access_token>）
  body → { "schema_version": 1, "data": {…} }
  200 → { "revision": 8, "updated_at": "…" }

  错误码（沿用现有 {"error":{"code","message"}} 格式）：
    401 TOKEN_INVALID                未登录/令牌失效（现有逻辑复用）
    413 PAYLOAD_TOO_LARGE            data 超过 256KB（当前快照约几 KB，纯防滥用）
    400 SCHEMA_VERSION_UNSUPPORTED   schema_version 高于服务端已知版本
```

`data` 对服务端透明。信封结构（客户端拼装/解析）：

```json
{
  "schema_version": 1,
  "save":       { "version": 3, "warehouse": [...], "carry": [], "equipment": [...] },
  "collection": { "version": 1, "obtainedIds": [...], "litIds": [...], "allCollectedRewarded": false },
  "currency":   20000
}
```

内层直接复用 `PlayerSaveData`（v3）与 `CollectionSaveData` 的 JSON 结构，零转换。注意 `schema_version`（信封版本，服务端认识）与 `save.version`（存档格式版本，客户端认识）是两层版本号。

## 6. 上传时机

原则：**脏标记 + 去抖上传 + 关键结算点强制上传**；上传节奏与本地存档节奏对齐。

| 时刻 | 动作 | 是否上传 |
|------|------|----------|
| 登录 | GET 快照 → 写本地缓存 → Apply | 只下载 |
| 主菜单操作（换装/商店/收藏，连续 N 次） | 每次置脏标记 + 照写本地缓存 | 停止操作 5 秒后合批传一次；持续变化则每 30 秒强制一次（去抖 5s / 上限 30s 实现为常量，可调） |
| 点出发（单机进图） | 携带品移出仓库（现有 `DeployToRaid`） | 立即上传 |
| 地牢内捡战利品 | 只进内存 `RaidBag` | 不传 |
| 撤离成功（`ExtractRaid`） | 战利品并入仓库 | 立即上传 |
| 死亡（`AbandonRaid`） | 装备清空 | 立即上传 |
| 登出 / 切换账号 | — | 立即上传（先传后清身份） |
| 退出游戏 / 切后台 | — | 立即上传 |

- 脏标记挂在现有变化钩子上：`GameSession.HandleLoadoutChanged`、收藏变化事件、`PlayerCurrency.SetBalance`
- 强杀进程的最坏丢失窗口：主菜单最后 5 秒去抖内的操作，或一局未完成的单机局（与现有本地存档行为一致，`RaidBag` 本就不落盘）
- 撤离/死亡两个事件与未来服务器权威化是同一批事件，届时"客户端算好再传"换成"客户端上报请求、服务端算完返回"，客户端流程不改

## 7. 断网行为

| 场景 | 行为 |
|------|------|
| 从未登录过 + 断网启动 | 停在登录页，进不了游戏（硬门槛） |
| 登录后断网，继续玩 | 完全可玩，玩家无感。上传失败保留脏标记，指数退避重试（5s→10s→20s…封顶 60s）；撤离/死亡的立即上传失败不阻塞结算、不弹框，主菜单挂"未同步"角标；恢复联网后自动补传 |
| 断网中退出，下次启动仍断网 | 进不了游戏。JWT 会话缓存 + 离线模式第一期内不做 |
| 断网期间另一台设备登录过同一账号 | 恢复联网补传后整包覆盖，断网设备状态获胜（第一期接受后传者胜；权威化后由服务端裁决） |

整包 PUT 幂等，重试零风险（重复传同一次撤离不会多加一分钱）——这是选快照方案而非逐件指令的关键收益之一。

## 8. 客户端改造点

| 文件 | 改动 |
|------|------|
| **新建** `Assets/Network/Runtime/AssetSyncService.cs` | 核心同步服务：标脏 / 去抖 / 退避重试 / 强制点；登录时 GET → Apply |
| `Assets/Network/Runtime/ApiClient.cs` | 加 `GetSnapshot` / `PutSnapshot` 两个协程方法（照现有协程风格） |
| `Assets/Network/Runtime/ApiModels.cs` | 加快照信封 DTO（`SnapshotEnvelopeDto` 等） |
| `Assets/Inventory/Runtime/GameSession.cs` | `InitializePlayer` 改为从同步服务取数（快照 → `PlayerSaveStore.Apply`，PlayerPrefs 键照写作为缓存）；变化钩子调 `MarkDirty` |
| `Assets/UI/MainMenu/Scripts/PlayerCurrency.cs` | 余额并入快照，`prefsKey` 降级为缓存键 |
| `Assets/Inventory/Runtime/CollectionStore.cs` | 收藏并入快照，同上 |
| `Assets/UI/MainMenu/Scripts/DepartFlow.cs` | 未登录禁用出发（登录门槛 UI 门禁） |
| `Assets/Network/Runtime/NetworkSession.cs` | 登出顺序调整：先强制上传，再 `SetLocalPlayerId(null)` |

不动的：`PlayerSaveStore`（仍是缓存读写器）、`PlayerLoadout`、`RaidBag` 与全部局内逻辑。

## 9. 迁移与清理

- 放弃 local 档迁移（决策见第 2 节），首次登录服务端无快照 → 初始装备流程 → PUT 建档
- 现有 PlayerPrefs 键（`PlayerSave`、`PlayerSave:account:*`、`StarCoins`、收藏键）保留为缓存，不再作为数据源
- `"local"` 身份仅剩 EditMode 测试与本地调试用途，生产流程不可达

## 10. 测试策略

- EditMode 新增：
  - 快照信封序列化往返（信封 ↔ DTO ↔ 内层 v3 结构）
  - 去抖状态机（注入时钟手动 tick：连续标脏只触发一次、30 秒上限强制）
  - 重试/退避状态机（失败→退避→成功补传；成功后脏标记清除）
  - GET 失败重试路径（重试耗尽停在菜单，不静默降级）
- 现有 `GameSessionTests`、`CollectionStoreTests` 必须原样通过（存储层行为不变）
- 手动联调清单：登录下发 → 改装备 → 去抖上传 → 登出 → 重登还原；断网玩 → 恢复补传；双设备后传覆盖验证

## 11. 已知限制与演进路径

1. 双设备并发 = 后传覆盖（第一期接受）
2. 客户端权威 = 快照可被篡改（第一期接受；攻击者可直接 PUT 伪造快照）
3. 演进路径（每步只动服务端或局部客户端）：
   - `revision` → If-Match 乐观锁（409 冲突协议）
   - 撤离/死亡结算改服务端事务（`PUT /raid/extract` 服务端改写快照）
   - 需要交易行/逐件审计时，从 `data` 拆出 `item_instance` 表（实例 ID 已在快照中，可回填）
