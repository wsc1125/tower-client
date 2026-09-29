# Go 后端设计文档

> 配套文档：[联机方案.md](./联机方案.md)
> 权威模型：**主机权威 + P2P**（游戏逻辑在房主客户端，服务端不参与）
> 服务端定位：**账号 + 房间 + 信令 + 中继**（不碰任何游戏逻辑）
> 部署目标：**公网云服务器**
> 本文档为设计稿，评审通过后再进入实现。

---

## 一、服务端职责边界

### 1.1 做什么

| 模块 | 职责 | 为什么必须由服务端做 |
|---|---|---|
| **账号** | 注册 / 登录 / Token 签发 | 玩家之间互不信任，必须有一个公共权威验证身份 |
| **房间** | 创建 / 加入 / 列表 / 房间码 | 玩家 B 需要知道玩家 A 开了房，需要一个公共登记处 |
| **信令** | 交换双方的公网地址（IP:Port） | 两个玩家都在 NAT 后，无法直接知道对方地址 |
| **中继** | 打洞失败时转发流量 | 部分 NAT 类型无法打洞，必须有公网中转 |

### 1.2 不做什么

| 不做的事 | 原因 |
|---|---|
| 游戏逻辑（战斗 / 掉落 / 层生成） | 主机权威：这些由房主客户端裁决 |
| 存档读写 | **本期暂不做**（见 1.3） |
| 战利品校验 | 本期不做防作弊（见联机方案 2.5） |
| 玩家位置同步 | P2P 直连，流量不经过服务端 |

### 1.3 本期范围裁剪

**存档模块本期不做**。原因：

- 存档是「局外资产」，与联机核心链路（连上、能玩）解耦
- 先跑通联机，存档后续作为独立模块补上
- 客户端 `PlayerSaveStore` 继续用本地存档，零改动

**影响**：本期玩家资产仍存本地。换设备不还原。这是**已知取舍**，不是遗漏。

### 1.4 架构总览

```
┌─────────────┐         ┌─────────────┐
│  玩家 A      │         │  玩家 B      │
│  (房主/主机)  │         │  (客户端)    │
└──────┬──────┘         └──────┬──────┘
       │                       │
       │  ① HTTPS: 登录/房间    │
       ├───────────────────────┤
       │                       │
       │  ② WSS: 信令交换地址    │
       ├───────────────────────┤
       │                       │
       │  ③ UDP: P2P 直连(游戏流量)
       │◄─────────────────────►│
       │                       │
       │  ③' 打洞失败 → 走中继   │
       └───────────┬───────────┘
                   │
         ┌─────────▼─────────┐
         │   Go 服务端        │
         │  ┌─────────────┐  │
         │  │ HTTP API    │  │  账号 / 房间
         │  ├─────────────┤  │
         │  │ WebSocket   │  │  信令
         │  ├─────────────┤  │
         │  │ PostgreSQL  │  │  账号 / 房间数据
         │  └─────────────┘  │
         └───────────────────┘
                   │
         ┌─────────▼─────────┐
         │  coturn (独立进程)  │  STUN / TURN
         └───────────────────┘
```

**关键点**：游戏流量（③）**不经过 Go 服务端**，只在打洞失败时经过 coturn（③'）。Go 服务端只处理低频的控制面流量。

---

## 二、技术选型

| 组件 | 选型 | 理由 |
|---|---|---|
| 语言 | **Go 1.22+** | 用户指定；并发模型适合长连接 |
| HTTP 框架 | **`gin`** | 生态成熟、中间件丰富；也可用标准库 `net/http`（Go 1.22 路由已增强） |
| WebSocket | **`nhooyr.io/websocket`**（现 `coder/websocket`） | 比 `gorilla/websocket` 更现代，支持 `context` 取消 |
| 数据库 | **PostgreSQL 16** | 账号 / 房间数据；事务可靠 |
| 数据访问 | **`sqlc`** | 从 SQL 生成类型安全代码，比 ORM 可控 |
| 数据库驱动 | **`pgx/v5`** | PostgreSQL 性能最佳驱动 |
| 认证 | **`golang-jwt/jwt/v5`** + **`x/crypto/bcrypt`** | JWT 签发校验、密码哈希 |
| 配置 | **环境变量** + `godotenv`（本地） | 12-Factor，容器友好 |
| 日志 | **`zerolog`** | 结构化日志、零分配 |
| 中继 | **coturn**（独立进程） | 开源 STUN/TURN 事实标准，不自己造 |
| 部署 | **Docker** 多阶段构建 + **docker compose** | 本地一键拉起，云端同构 |

### 2.1 为什么中继用 coturn 而不是自己写

TURN 协议（RFC 5766）实现复杂，涉及：

- 长期凭证认证机制
- 端口分配与生命周期管理
- 大量边界情况（分片、保活、权限）

coturn 是经过大规模生产验证的实现。自己写 TURN 是**纯粹的重复劳动且极易出错**。Go 服务端只需在信令阶段把 coturn 的地址和临时凭证下发给客户端。

---

## 三、模块设计

### 3.1 账号模块

#### 3.1.1 数据模型

```sql
CREATE TABLE accounts (
    id            BIGSERIAL PRIMARY KEY,
    username      VARCHAR(32)  NOT NULL UNIQUE,
    password_hash VARCHAR(72)  NOT NULL,          -- bcrypt 输出固定 60 字符
    display_name  VARCHAR(32)  NOT NULL,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    last_login_at TIMESTAMPTZ
);

CREATE INDEX idx_accounts_username ON accounts (username);
```

**说明**：

- `username` 唯一，登录用；`display_name` 可重复，游戏内显示用
- `password_hash` 存 bcrypt 输出，**永不存明文**
- 本期不做邮箱 / 找回密码（无邮件服务依赖）

#### 3.1.2 接口

| 方法 | 路径 | 说明 | 认证 |
|---|---|---|---|
| `POST` | `/api/v1/auth/register` | 注册 | 否 |
| `POST` | `/api/v1/auth/login` | 登录，返回 JWT | 否 |
| `POST` | `/api/v1/auth/refresh` | 刷新 Token | 是（Refresh Token） |
| `GET` | `/api/v1/auth/me` | 获取当前账号信息 | 是 |

**注册请求**：

```json
POST /api/v1/auth/register
{
  "username": "player001",
  "password": "********",
  "display_name": "爬塔人"
}
```

**注册响应**：

```json
{
  "account_id": 1001,
  "username": "player001",
  "display_name": "爬塔人",
  "access_token": "eyJhbGci...",
  "refresh_token": "eyJhbGci...",
  "expires_in": 3600
}
```

**登录请求 / 响应**：同注册（去掉 `display_name`）。

**错误响应统一格式**：

```json
{
  "error": {
    "code": "USERNAME_TAKEN",
    "message": "用户名已被占用"
  }
}
```

**错误码表**：

| HTTP | code | 说明 |
|---|---|---|
| 400 | `INVALID_REQUEST` | 参数格式错误 |
| 401 | `INVALID_CREDENTIALS` | 用户名或密码错误 |
| 401 | `TOKEN_EXPIRED` | Token 过期 |
| 401 | `TOKEN_INVALID` | Token 无效 |
| 409 | `USERNAME_TAKEN` | 用户名已存在 |
| 429 | `TOO_MANY_REQUESTS` | 触发限流 |
| 500 | `INTERNAL_ERROR` | 服务端内部错误 |

#### 3.1.3 Token 设计

| 项 | 值 | 说明 |
|---|---|---|
| 类型 | JWT (HS256) | 对称签名，服务端单点验证，无需非对称 |
| Access Token 有效期 | **1 小时** | 短有效期，降低泄露风险 |
| Refresh Token 有效期 | **30 天** | 长有效期，避免频繁登录 |
| 载荷 | `sub`(account_id) / `exp` / `iat` / `type` | `type` 区分 access / refresh |

**为什么用 HS256 而非 RS256**：服务端是单体，签发与验证是同一进程，对称密钥足够。RS256 的价值在于「多方验证」，本项目用不上。

**密钥管理**：`JWT_SECRET` 从环境变量读取，**不写进代码 / 不进版本库**。生产环境用 32 字节以上随机值。

#### 3.1.4 密码安全

| 项 | 做法 |
|---|---|
| 哈希算法 | bcrypt，cost = **12**（约 250ms/次，平衡安全与体验） |
| 密码强度 | 最少 8 位，不强制复杂度（避免用户写便签） |
| 时序攻击 | 用户名不存在时也执行一次 bcrypt 比对（假哈希），避免通过响应时间枚举用户名 |
| 登录限流 | 同 IP + 同用户名，5 次失败后锁定 5 分钟 |

### 3.2 房间模块

#### 3.2.1 数据模型

```sql
CREATE TABLE rooms (
    id            BIGSERIAL PRIMARY KEY,
    room_code     VARCHAR(8)   NOT NULL UNIQUE,   -- 玩家输入的短码
    host_account  BIGINT       NOT NULL REFERENCES accounts(id),
    name          VARCHAR(64)  NOT NULL,
    max_players   SMALLINT     NOT NULL DEFAULT 4,
    is_public     BOOLEAN      NOT NULL DEFAULT true,
    status        VARCHAR(16)  NOT NULL DEFAULT 'waiting',  -- waiting / playing / closed
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    closed_at     TIMESTAMPTZ
);

CREATE INDEX idx_rooms_status ON rooms (status) WHERE status != 'closed';
CREATE INDEX idx_rooms_code   ON rooms (room_code);
```

**说明**：

- `room_code` 是 8 位大写字母数字（去掉易混淆的 `0/O/1/I`），玩家口头分享用
- `status` 三态：`waiting`（等待中）/ `playing`（游戏中）/ `closed`（已关闭）
- 房间**不持久化成员列表**——成员是运行时状态，存在内存中（见 3.2.4）

#### 3.2.2 接口

| 方法 | 路径 | 说明 | 认证 |
|---|---|---|---|
| `POST` | `/api/v1/rooms` | 创建房间 | 是 |
| `GET` | `/api/v1/rooms` | 房间列表（仅 `waiting` + `is_public`） | 是 |
| `GET` | `/api/v1/rooms/{code}` | 查询房间详情 | 是 |
| `POST` | `/api/v1/rooms/{code}/join` | 加入房间 | 是 |
| `POST` | `/api/v1/rooms/{code}/leave` | 离开房间 | 是 |
| `DELETE` | `/api/v1/rooms/{code}` | 关闭房间（仅房主） | 是 |

**创建房间请求**：

```json
POST /api/v1/rooms
{
  "name": "一起爬塔",
  "max_players": 4,
  "is_public": true
}
```

**创建房间响应**：

```json
{
  "room_code": "A3F7K2M9",
  "name": "一起爬塔",
  "host_account": 1001,
  "max_players": 4,
  "status": "waiting",
  "members": [
    { "account_id": 1001, "display_name": "爬塔人", "is_host": true }
  ]
}
```

**房间列表响应**：

```json
{
  "rooms": [
    {
      "room_code": "A3F7K2M9",
      "name": "一起爬塔",
      "host_display_name": "爬塔人",
      "current_players": 2,
      "max_players": 4,
      "status": "waiting"
    }
  ]
}
```

#### 3.2.3 房间码生成

```
字符集：ABCDEFGHJKLMNPQRSTUVWXYZ23456789  (32 个字符，去掉 0/O/1/I/L)
长度：8
空间：32^8 ≈ 1.1 × 10^12
```

**冲突处理**：生成后查库，冲突则重试（最多 5 次）。空间足够大，实际几乎不会冲突。

#### 3.2.4 成员状态：内存 vs 数据库

**决策：成员列表存内存，不落库。**

| 方案 | 优点 | 缺点 |
|---|---|---|
| **存内存（选定）** | 快；成员本就是运行时状态；断线即失效 | 服务重启丢失 |
| 存数据库 | 重启不丢 | 需要处理「僵尸成员」（客户端崩溃未清理） |

**理由**：成员列表的生命周期与 WebSocket 连接绑定。连接断了成员就该消失，落库反而要额外处理清理逻辑。服务重启时所有连接都断了，房间成员本就该清空。

**内存结构**：

```go
type Room struct {
    Code       string
    HostID     int64
    Name       string
    MaxPlayers int
    IsPublic   bool
    Status     string
    Members    map[int64]*Member  // account_id -> member
    mu         sync.RWMutex
}

type Member struct {
    AccountID   int64
    DisplayName string
    Conn        *websocket.Conn  // 信令连接
    JoinedAt    time.Time
}
```

**并发保护**：`Room` 用 `sync.RWMutex` 保护 `Members`。房间注册表用 `sync.Map` 或 `map + RWMutex`。

#### 3.2.5 房间生命周期

| 事件 | 行为 |
|---|---|
| 创建 | 落库（`status=waiting`）+ 内存建 Room + 房主加入 |
| 加入 | 校验：房间存在 / 未满 / 状态为 `waiting` → 内存加成员 → 广播 `member_joined` |
| 离开 | 内存移除成员 → 广播 `member_left` |
| 房主离开 | **房间关闭**（本期不做房主迁移）→ 广播 `room_closed` → 落库 `status=closed` |
| 全员离开 | 房间关闭 |
| 开始游戏 | 房主触发 → `status=playing` → 落库 → 广播 `game_starting` |
| 超时清理 | 后台任务：`waiting` 且 2 小时无活动的房间自动关闭 |

### 3.3 信令模块

#### 3.3.1 为什么需要信令

主机权威下，玩家 A（房主）与玩家 B 要 P2P 直连。但双方都在 NAT 后，**彼此不知道对方的公网地址**。信令服务器的作用就是：

```
1. A 连上信令服务器，服务器看到 A 的公网地址是 1.2.3.4:5000
2. B 连上信令服务器，服务器看到 B 的公网地址是 5.6.7.8:6000
3. 服务器把 A 的地址告诉 B，把 B 的地址告诉 A
4. A、B 各自向对方地址发包，尝试打洞
5. 打洞成功 → 直连；失败 → 走 TURN 中继
```

**关键**：信令只负责**交换地址**，不转发游戏流量。

#### 3.3.2 连接方式

**WebSocket**（`wss://`），路径 `/api/v1/signal?room_code=XXX&token=YYY`。

**为什么用 WebSocket 而非 HTTP 轮询**：

- 信令需要**服务端主动推送**（B 加入时要立刻通知 A）
- 轮询有延迟且浪费请求
- WebSocket 天然双向、低延迟

#### 3.3.3 消息协议

统一信封格式：

```json
{
  "type": "message_type",
  "seq": 123,
  "payload": { }
}
```

| 字段 | 说明 |
|---|---|
| `type` | 消息类型 |
| `seq` | 客户端自增序号，用于请求-响应配对（可选） |
| `payload` | 类型相关的载荷 |

**客户端 → 服务端**：

| type | payload | 说明 |
|---|---|---|
| `join_room` | `{ room_code }` | 加入房间信令（WebSocket 连接后首条） |
| `signal` | `{ target_account_id, data }` | 转发信令数据给指定玩家 |
| `ready` | `{ ready: true }` | 标记准备状态 |
| `start_game` | `{}` | 房主发起开始游戏 |
| `ping` | `{}` | 保活 |

**服务端 → 客户端**：

| type | payload | 说明 |
|---|---|---|
| `room_state` | `{ room_code, members[], host_account_id }` | 加入成功后下发完整房间状态 |
| `member_joined` | `{ account_id, display_name }` | 有玩家加入 |
| `member_left` | `{ account_id }` | 有玩家离开 |
| `signal` | `{ from_account_id, data }` | 转发来的信令数据 |
| `game_starting` | `{ host_account_id, host_endpoint }` | 开始游戏，附带主机地址 |
| `room_closed` | `{ reason }` | 房间关闭 |
| `error` | `{ code, message }` | 错误 |
| `pong` | `{}` | 保活响应 |

#### 3.3.4 信令数据（`data` 字段）内容

`data` 是**不透明字符串**，服务端不解析，只转发。内容由客户端定义，典型是 WebRTC 的 SDP / ICE candidate：

```json
{
  "type": "signal",
  "payload": {
    "target_account_id": 1002,
    "data": "{\"sdp\":\"v=0\\r\\no=- ...\",\"type\":\"offer\"}"
  }
}
```

**为什么服务端不解析**：信令内容与具体穿透方案（WebRTC / Unity Transport 自定义）耦合。服务端保持不透明转发，客户端换方案时服务端零改动。

#### 3.3.5 保活与超时

| 项 | 值 | 说明 |
|---|---|---|
| 客户端 `ping` 间隔 | 20 秒 | 主动保活 |
| 服务端读超时 | 60 秒 | 超时未收到任何消息则断开 |
| 服务端 `ping` 间隔 | 30 秒 | 服务端也主动探测 |
| 断线处理 | 立即从房间移除成员 + 广播 `member_left` | 不做重连保留（本期） |

### 3.4 中继模块（coturn）

#### 3.4.1 部署形态

coturn 是**独立进程**，与 Go 服务端并列部署：

```
云服务器
├── Go 服务端      :8080 (HTTP/WS)
├── coturn        :3478 (STUN/TURN UDP/TCP)
│                 :5349 (TLS)
│                 :49152-65535 (中继端口范围)
└── PostgreSQL    :5432 (仅内网)
```

#### 3.4.2 临时凭证机制

**不把长期凭证下发给客户端**。使用 TURN REST API 机制（RFC 5766 附录）：

```
username   = <过期时间戳>:<account_id>
password   = base64(HMAC-SHA1(shared_secret, username))
```

| 项 | 说明 |
|---|---|
| `shared_secret` | Go 服务端与 coturn 共享的密钥（环境变量） |
| 有效期 | 24 小时（`username` 里的时间戳） |
| 下发时机 | 客户端请求信令连接时，服务端一并返回 |

**为什么用临时凭证**：长期凭证一旦泄露，任何人都能白嫖中继带宽。临时凭证即使泄露，24 小时后自动失效。

#### 3.4.3 接口

| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/v1/relay/credentials` | 获取 STUN/TURN 地址与临时凭证 |

**响应**：

```json
{
  "ice_servers": [
    { "urls": "stun:turn.example.com:3478" },
    {
      "urls": "turn:turn.example.com:3478",
      "username": "1735689600:1001",
      "credential": "base64encodedhmac..."
    }
  ],
  "ttl": 86400
}
```

#### 3.4.4 带宽与成本控制

中继流量是**真实成本**（云服务器按流量计费）。控制手段：

| 手段 | 说明 |
|---|---|
| 优先打洞 | 打洞成功则零中继流量 |
| 中继仅作回退 | 客户端逻辑：直连失败才用中继 |
| 带宽限制 | coturn 配置 `max-bps` 限制单会话带宽 |
| 会话数限制 | coturn 配置 `user-quota` / `total-quota` |
| 监控 | 记录中继流量，异常时告警 |

---

## 四、项目结构

```
server/
├── cmd/
│   └── api/
│       └── main.go              # 入口：装配依赖、启动 HTTP 服务
├── internal/
│   ├── config/
│   │   └── config.go            # 环境变量读取与校验
│   ├── auth/
│   │   ├── handler.go           # HTTP 处理器
│   │   ├── service.go           # 业务逻辑
│   │   ├── jwt.go               # Token 签发校验
│   │   └── password.go          # bcrypt 封装
│   ├── room/
│   │   ├── handler.go           # HTTP 处理器
│   │   ├── service.go           # 业务逻辑
│   │   ├── registry.go          # 内存房间注册表
│   │   └── code.go              # 房间码生成
│   ├── signal/
│   │   ├── handler.go           # WebSocket 升级
│   │   ├── hub.go               # 连接管理
│   │   └── protocol.go          # 消息类型定义
│   ├── relay/
│   │   └── credentials.go       # TURN 临时凭证生成
│   ├── db/
│   │   ├── db.go                # 连接池
│   │   └── sqlc/                # sqlc 生成代码
│   ├── middleware/
│   │   ├── auth.go              # JWT 校验中间件
│   │   ├── ratelimit.go         # 限流
│   │   └── logging.go           # 请求日志
│   └── httpx/
│       └── response.go          # 统一响应 / 错误格式
├── migrations/
│   ├── 0001_create_accounts.sql
│   └── 0002_create_rooms.sql
├── sqlc.yaml
├── docker-compose.yml           # 本地：PostgreSQL + coturn + api
├── Dockerfile
├── go.mod
└── go.sum
```

**分层原则**：

- `handler` 只做「解析请求 → 调 service → 写响应」，不含业务逻辑
- `service` 含业务逻辑，不依赖 HTTP 类型
- `db` / `sqlc` 只做数据访问
- 依赖方向单向：`handler → service → db`

---

## 五、关键流程

### 5.1 完整联机流程

```
玩家A                          服务端                        玩家B
  │                              │                            │
  │──① POST /auth/login ────────►│                            │
  │◄───── access_token ──────────│                            │
  │                              │                            │
  │──② POST /rooms ─────────────►│                            │
  │◄───── room_code: A3F7K2M9 ───│                            │
  │                              │                            │
  │──③ WSS /signal?room_code ───►│                            │
  │◄───── room_state ────────────│                            │
  │                              │                            │
  │                              │◄──④ POST /auth/login ──────│
  │                              │───── access_token ────────►│
  │                              │                            │
  │                              │◄──⑤ POST /rooms/A3F7K2M9/join
  │                              │───── 200 OK ──────────────►│
  │                              │                            │
  │                              │◄──⑥ WSS /signal?room_code ─│
  │◄───── member_joined ─────────│───── room_state ──────────►│
  │                              │                            │
  │──⑦ GET /relay/credentials ──►│                            │
  │◄───── ice_servers ───────────│                            │
  │                              │◄──⑧ GET /relay/credentials │
  │                              │───── ice_servers ─────────►│
  │                              │                            │
  │──⑨ signal(offer) ───────────►│                            │
  │                              │───── signal(offer) ───────►│
  │                              │◄──── signal(answer) ───────│
  │◄───── signal(answer) ────────│                            │
  │                              │                            │
  │◄══════⑩ P2P 直连（游戏流量，不经服务端）══════════════════►│
  │                              │                            │
  │──⑪ start_game ──────────────►│                            │
  │                              │───── game_starting ───────►│
  │                              │                            │
```

**关键节点**：

- ①④ 登录：拿到 JWT
- ②⑤ 房间：创建 / 加入
- ③⑥ 信令：建立 WebSocket
- ⑦⑧ 中继凭证：拿到 STUN/TURN 地址
- ⑨ 信令交换：交换 SDP / ICE
- ⑩ **P2P 直连**：游戏流量走这里，服务端不参与
- ⑪ 开始游戏：房主触发

### 5.2 打洞失败回退中继

```
A、B 交换地址后尝试直连
  │
  ├─ 成功 → 直连，零中继流量
  │
  └─ 失败（超时 5 秒）
       │
       └─ 双方改用 TURN 中继地址
            │
            └─ 流量经 coturn 转发
```

**客户端判定逻辑**（Unity 侧实现）：

1. 拿到 `ice_servers`（含 STUN 和 TURN）
2. 优先尝试 host / srflx（直连）候选
3. 直连超时 → 使用 relay 候选（中继）

### 5.3 房主离开

```
房主断开 WebSocket
  │
  ├─ 服务端检测到连接关闭
  ├─ 从房间移除房主
  ├─ 房间状态 → closed（落库）
  ├─ 广播 room_closed 给所有成员
  └─ 所有成员回到主菜单
```

**本期不做房主迁移**（联机方案 4.4 列为可选）。房主离开 = 本局结束。

---

## 六、安全设计

### 6.1 传输安全

| 项 | 做法 |
|---|---|
| HTTP | **HTTPS**（TLS 1.2+），用 Let's Encrypt 免费证书 |
| WebSocket | **WSS**，与 HTTPS 同证书 |
| 数据库 | 仅内网访问，不暴露公网端口 |
| coturn | TURN over TLS（`:5349`）可选，UDP `:3478` 为主 |

### 6.2 认证与授权

| 项 | 做法 |
|---|---|
| 密码 | bcrypt cost=12，永不存明文、永不记日志 |
| Token | JWT HS256，密钥从环境变量读 |
| 房间操作 | 校验「操作者是否为房主」（关闭房间）/「是否在房间内」（离开房间） |
| 信令转发 | 校验「发送者与目标在同一房间」，防止跨房间骚扰 |

### 6.3 限流

| 维度 | 限制 | 实现 |
|---|---|---|
| 登录 | 同 IP 5 次/分钟 | 内存计数器（本期单实例够用） |
| 注册 | 同 IP 3 次/小时 | 同上 |
| 创建房间 | 同账号 5 次/分钟 | 同上 |
| WebSocket 消息 | 同连接 30 条/秒 | 连接级计数器 |
| 全局 | 单 IP 300 请求/分钟 | 中间件 |

**为什么用内存限流而非 Redis**：本期单实例部署，内存够用。多实例时再换 Redis（接口不变）。

### 6.4 输入校验

| 项 | 规则 |
|---|---|
| `username` | 3-32 字符，`[a-zA-Z0-9_]` |
| `password` | 8-72 字符（bcrypt 上限 72 字节） |
| `display_name` | 1-32 字符，禁止控制字符 |
| `room_code` | 8 字符，字符集内 |
| `max_players` | 2-4 |
| 信令 `data` | 最大 64 KB（防超大 payload 打爆内存） |

---

## 七、部署设计

### 7.1 本地开发（docker compose）

```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: tower
      POSTGRES_USER: tower
      POSTGRES_PASSWORD: devpassword
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]

  coturn:
    image: coturn/coturn:latest
    network_mode: host          # TURN 需要大量端口，host 模式最简单
    command: >
      -n --log-file=stdout
      --realm=tower.local
      --use-auth-secret
      --static-auth-secret=devsecret
      --min-port=49152 --max-port=65535
      --no-cli

  api:
    build: .
    ports: ["8080:8080"]
    environment:
      DATABASE_URL: postgres://tower:devpassword@postgres:5432/tower?sslmode=disable
      JWT_SECRET: dev-jwt-secret-change-me
      TURN_SHARED_SECRET: devsecret
      TURN_REALM: tower.local
      TURN_HOST: 127.0.0.1
    depends_on: [postgres]

volumes:
  pgdata:
```

**本地验证要点**：`TURN_HOST` 用 `127.0.0.1` 时只能本机测试。要验证真实打洞，需要**两台不同网络的机器**（见 7.3）。

### 7.2 云端部署

```
云服务器（推荐 2C4G 起）
├── Docker: api        (Go 服务端)
├── Docker: coturn     (host 网络模式)
├── Docker: postgres   (仅内网)
└── Nginx / Caddy      (TLS 终止 + 反向代理)
```

| 项 | 配置 |
|---|---|
| 域名 | `api.example.com` → Go 服务端 |
| 域名 | `turn.example.com` → coturn |
| TLS | Caddy 自动申请 Let's Encrypt 证书（比 Nginx + certbot 简单） |
| 防火墙 | 开放 80/443（HTTP/WS）、3478（STUN/TURN）、49152-65535（中继） |
| 数据库 | 数据卷持久化 + 每日备份 |
| 日志 | 结构化 JSON 输出，Docker 日志驱动收集 |

**带宽估算**：

| 场景 | 流量 |
|---|---|
| 打洞成功 | 仅信令（每房间几 KB/秒） |
| 打洞失败（走中继） | 游戏流量 × 玩家数（可能几 MB/秒） |

**这是成本关键**：打洞成功率直接决定带宽成本。1A.7 穿透验证必须做。

### 7.3 穿透验证方案（1A.7）

**目标**：确认在真实网络环境下打洞成功率可接受。

**方法**：

| 步骤 | 做法 |
|---|---|
| 1 | 云服务器部署 coturn + Go 信令服务 |
| 2 | 两台不同网络的机器（如家里 + 手机热点）各跑一个 Unity 客户端 |
| 3 | 客户端交换 ICE 候选，尝试直连 |
| 4 | 记录：是否直连成功 / 耗时 / 失败时是否成功回退中继 |
| 5 | 换几种网络组合重复（家庭宽带 / 4G / 5G / 公司网络） |

**判定标准**：

| 结果 | 结论 |
|---|---|
| 直连成功率 > 70% | 可接受，继续 |
| 直连成功率 30%-70% | 可接受但中继成本高，需优化 |
| 直连成功率 < 30% | 需重新评估（考虑纯中继或 Dedicated Server） |

---

## 八、可观测性

### 8.1 日志

结构化 JSON（`zerolog`），关键字段：

| 字段 | 说明 |
|---|---|
| `level` | debug / info / warn / error |
| `time` | 时间戳 |
| `msg` | 日志内容 |
| `account_id` | 关联账号（有则填） |
| `room_code` | 关联房间（有则填） |
| `request_id` | 请求追踪 ID |
| `latency_ms` | 请求耗时 |

**禁止记录**：密码、Token 原文、信令 `data` 内容。

### 8.2 健康检查

| 路径 | 说明 |
|---|---|
| `GET /healthz` | 存活检查（进程在跑） |
| `GET /readyz` | 就绪检查（数据库可连） |

### 8.3 关键指标

| 指标 | 说明 |
|---|---|
| 在线账号数 | 当前活跃连接 |
| 活跃房间数 | `status != closed` |
| 信令消息速率 | 每秒转发消息数 |
| 中继会话数 | coturn 当前中继会话 |
| 中继流量 | 累计转发字节数（成本监控） |
| 请求延迟 P50/P95/P99 | HTTP 接口性能 |
| 错误率 | 5xx 占比 |

---

## 九、实施顺序

| 步骤 | 内容 | 依赖 |
|---|---|---|
| 1 | 项目骨架 + 配置 + 日志 + 健康检查 | 无 |
| 2 | 数据库迁移 + sqlc 接入 | 1 |
| 3 | 账号模块（注册 / 登录 / JWT） | 2 |
| 4 | 认证中间件 + 限流 | 3 |
| 5 | 房间模块（CRUD + 内存注册表） | 4 |
| 6 | 信令模块（WebSocket + 消息协议） | 5 |
| 7 | 中继凭证接口 + coturn 接入 | 6 |
| 8 | docker compose 本地一键拉起 | 7 |
| 9 | **穿透验证（1A.7）** | 8 |
| 10 | 云端部署 + TLS + 监控 | 9 |

**步骤 9 是关卡**：验证不通过则需重新评估方案。

---

## 十、待确认事项

| 编号 | 事项 | 建议 |
|---|---|---|
| S1 | HTTP 框架：`gin` 还是标准库 `net/http` | 标准库（Go 1.22 路由已够用，少一个依赖） |
| S2 | 是否需要 Redis | 本期不需要（单实例，内存限流够用） |
| S3 | 房间是否需要密码 | 本期不需要（房间码本身即门槛） |
| S4 | 是否支持观战者（非玩家进入房间） | 本期不支持 |
| S5 | 云服务器规格与厂商 | 待定（2C4G 起，需确认带宽计费方式） |
| S6 | 域名 | 待定 |
| S7 | 中继带宽上限 | 待定（需先看 1A.7 打洞成功率） |
