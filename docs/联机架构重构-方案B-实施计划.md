# 联机架构重构（方案 B）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把联机流程从「先各自切场景、再尝试连接」重构为「先建立 NGO 会话、再由 Host 统一切场景」，彻底解决双端不在同一会话、成员角色不生成的问题，同时保证单机模式行为不变。

**Architecture:** 新增常驻 `MultiplayerCoordinator` 状态机统一管理 NGO 生命周期；`NetworkManager` 从 BattleScene 迁到常驻层；服务端新增可查询的 `game_session` 状态；成员端禁止自行加载 BattleScene，改由 Host 通过 NGO `NetworkSceneManager` 同步；传输层用 `IConnectionStrategy` 隔离，本阶段只实现 LAN，后续接自建 Relay。

**Tech Stack:** Unity 2022.3.62f3c1、Unity Netcode for GameObjects 1.5.1、Unity Transport 1.3.4、Go 1.21+（Gin + gorilla/websocket）、C# 协程。

## Global Constraints

- Unity 版本：2022.3.62f3c1，NGO 1.5.1，Unity Transport 1.3.4。
- 单机模式必须完全不受影响：不登录、不进房时，`MultiplayerCoordinator` 状态保持 `Idle`，`NetworkManager` 不启动。
- 联机模式场景切换只能由 Host 调用 `NetworkManager.SceneManager.LoadScene`；成员端禁止调用 `SceneManager.LoadSceneAsync("BattleScene")`。
- 所有新增 C# 代码放在 `Assets/Gameplay/Runtime/` 或 `Assets/Network/Runtime/`，命名空间分别为 `Tower.Netcode` 与 `Tower.Network`。
- `Gameplay.asmdef` 已引用 `Network`、`Unity.Netcode.Runtime`、`Unity.Netcode.Components`，新增文件无需改 asmdef。
- 服务端代码放在 `server/internal/`，遵循现有分层（handler → service → repository）。
- 所有代码注释使用中文，与现有代码风格一致。
- 每个任务结束必须提交一次 git commit。
- 验证命令：Go 用 `go build ./...` 与 `go test ./...`；Unity 用 `unity_get_compilation_errors` 确认 0 错误。

---

## 文件结构

### 服务端（Go）

| 文件 | 职责 |
|------|------|
| `server/internal/room/session.go`（新建） | 游戏会话数据结构与内存存储 |
| `server/internal/room/session_service.go`（新建） | 会话创建 / 查询 / 就绪 / 关闭 |
| `server/internal/room/handler.go`（修改） | 新增 session 相关路由 |
| `server/internal/signal/protocol.go`（修改） | `game_starting` 增加 session_id；新增 `game_session` 消息 |
| `server/internal/signal/handler.go`（修改） | 房主创建会话后广播 `game_session` |
| `server/cmd/api/main.go`（修改） | 接线会话服务 |

### 客户端（Unity）

| 文件 | 职责 |
|------|------|
| `Assets/Network/Runtime/ApiModels.cs`（修改） | 新增 `GameSessionDto` |
| `Assets/Network/Runtime/SignalProtocol.cs`（修改） | `GameStartingPayload` 增加 session_id；新增 `GameSessionPayload` |
| `Assets/Network/Runtime/ApiClient.cs`（修改） | 新增 session 相关 HTTP 调用 |
| `Assets/Network/Runtime/NetworkSession.cs`（修改） | 暴露 `CurrentSession` 与 `SessionChanged` |
| `Assets/Gameplay/Runtime/MultiplayerCoordinator.cs`（新建） | NGO 生命周期状态机 |
| `Assets/Gameplay/Runtime/IConnectionStrategy.cs`（新建） | 传输策略接口 |
| `Assets/Gameplay/Runtime/LanConnectionStrategy.cs`（新建） | LAN 直连实现 |
| `Assets/Gameplay/Runtime/NetworkPlayerSpawner.cs`（新建） | Host 侧玩家生成与出生点 |
| `Assets/Gameplay/Runtime/BattleNetworkBinder.cs`（新建） | 战斗场景内绑定 |
| `Assets/Gameplay/Runtime/NetworkGameBootstrap.cs`（删除） | 旧逻辑移除 |
| `Assets/UI/MainMenu/Scripts/MainMenuRoomBinder.cs`（修改） | 出发改为走 Coordinator |
| `Assets/UI/MainMenu/Scripts/DepartFlow.cs`（修改） | 联机分支交给 Coordinator |
| `Assets/Scenes/BattleScene.unity`（修改） | 移除 NetworkManager 与 NetworkGameBootstrap |
| `Assets/Scenes/MainMenu.unity`（修改） | 新增常驻 NetworkManager 与 Coordinator |

---

## 阶段 1：服务端游戏会话

### Task 1: 服务端游戏会话数据结构与存储

**Files:**
- Create: `server/internal/room/session.go`
- Test: `server/internal/room/session_test.go`

**Interfaces:**
- Consumes: 无
- Produces:
  - `type GameSession struct { ID string; RoomCode string; TransportMode string; HostAccountID int64; HostAddress string; HostPort int; Status string; CreatedAt time.Time }`
  - `type SessionStore struct{}`
  - `func NewSessionStore() *SessionStore`
  - `func (s *SessionStore) Create(roomCode string, hostAccountID int64) *GameSession`
  - `func (s *SessionStore) Get(roomCode string) (*GameSession, bool)`
  - `func (s *SessionStore) SetReady(roomCode, address string, port int) bool`
  - `func (s *SessionStore) SetStatus(roomCode, status string) bool`
  - `func (s *SessionStore) Remove(roomCode string)`
  - 状态常量：`SessionStatusConnecting = "connecting"`、`SessionStatusReady = "ready"`、`SessionStatusInGame = "in_game"`、`SessionStatusClosed = "closed"`

- [ ] **Step 1: 写失败测试**

创建 `server/internal/room/session_test.go`：

```go
package room

import "testing"

func TestSessionStoreCreateAndGet(t *testing.T) {
	store := NewSessionStore()

	session := store.Create("ABCD1234", 24)
	if session == nil {
		t.Fatal("Create 返回 nil")
	}
	if session.ID == "" {
		t.Fatal("session ID 为空")
	}
	if session.Status != SessionStatusConnecting {
		t.Fatalf("初始状态应为 connecting，实际 %s", session.Status)
	}

	got, ok := store.Get("ABCD1234")
	if !ok {
		t.Fatal("Get 未找到刚创建的会话")
	}
	if got.ID != session.ID {
		t.Fatalf("ID 不一致：%s vs %s", got.ID, session.ID)
	}
}

func TestSessionStoreSetReady(t *testing.T) {
	store := NewSessionStore()
	store.Create("ABCD1234", 24)

	if !store.SetReady("ABCD1234", "10.6.136.22", 7777) {
		t.Fatal("SetReady 应返回 true")
	}

	got, _ := store.Get("ABCD1234")
	if got.Status != SessionStatusReady {
		t.Fatalf("状态应为 ready，实际 %s", got.Status)
	}
	if got.HostAddress != "10.6.136.22" || got.HostPort != 7777 {
		t.Fatalf("地址端口未写入：%s:%d", got.HostAddress, got.HostPort)
	}
}

func TestSessionStoreSetReadyMissing(t *testing.T) {
	store := NewSessionStore()
	if store.SetReady("NOPE0000", "1.2.3.4", 7777) {
		t.Fatal("会话不存在时 SetReady 应返回 false")
	}
}

func TestSessionStoreRemove(t *testing.T) {
	store := NewSessionStore()
	store.Create("ABCD1234", 24)
	store.Remove("ABCD1234")

	if _, ok := store.Get("ABCD1234"); ok {
		t.Fatal("Remove 后不应再查到会话")
	}
}

func TestSessionStoreCreateReplaces(t *testing.T) {
	store := NewSessionStore()
	first := store.Create("ABCD1234", 24)
	second := store.Create("ABCD1234", 24)

	if first.ID == second.ID {
		t.Fatal("重复创建应生成新的 session ID")
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd server && go test ./internal/room/ -run TestSessionStore -v`
Expected: FAIL，报 `undefined: NewSessionStore`

- [ ] **Step 3: 实现最小代码**

创建 `server/internal/room/session.go`：

```go
package room

import (
	"crypto/rand"
	"encoding/hex"
	"sync"
	"time"
)

// 游戏会话状态。
const (
	// SessionStatusConnecting 房主已创建会话，正在建立 NGO 连接。
	SessionStatusConnecting = "connecting"

	// SessionStatusReady 房主已写入连接地址，成员可以连接。
	SessionStatusReady = "ready"

	// SessionStatusInGame 所有成员已连接，已进入战斗场景。
	SessionStatusInGame = "in_game"

	// SessionStatusClosed 会话已结束。
	SessionStatusClosed = "closed"
)

// GameSession 一局联机游戏的连接描述。
//
// 与 Room 的区别：Room 是「大厅」（可长期存在、成员可进出），
// GameSession 是「本局」（一次出发对应一个会话，连接信息在其中流转）。
// 之所以把连接信息做成可查询状态而非一次性信令：
// 成员可能错过推送，必须能主动补齐，否则会永久卡在等待。
type GameSession struct {
	ID            string    `json:"session_id"`
	RoomCode      string    `json:"room_code"`
	TransportMode string    `json:"transport_mode"`
	HostAccountID int64     `json:"host_account_id"`
	HostAddress   string    `json:"host_address"`
	HostPort      int       `json:"host_port"`
	Status        string    `json:"status"`
	CreatedAt     time.Time `json:"created_at"`
}

// SessionStore 游戏会话内存存储。
//
// 一个房间同时只有一个活跃会话：重新出发会覆盖旧会话。
type SessionStore struct {
	mu       sync.RWMutex
	sessions map[string]*GameSession
}

// NewSessionStore 创建会话存储。
func NewSessionStore() *SessionStore {
	return &SessionStore{sessions: make(map[string]*GameSession)}
}

// Create 为房间创建新会话，覆盖同房间的旧会话。
func (s *SessionStore) Create(roomCode string, hostAccountID int64) *GameSession {
	session := &GameSession{
		ID:            newSessionID(),
		RoomCode:      roomCode,
		TransportMode: "lan",
		HostAccountID: hostAccountID,
		Status:        SessionStatusConnecting,
		CreatedAt:     time.Now(),
	}

	s.mu.Lock()
	s.sessions[roomCode] = session
	s.mu.Unlock()

	return session
}

// Get 按房间码取会话。
func (s *SessionStore) Get(roomCode string) (*GameSession, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	session, ok := s.sessions[roomCode]

	return session, ok
}

// SetReady 写入房主连接地址并置为 ready。会话不存在时返回 false。
func (s *SessionStore) SetReady(roomCode, address string, port int) bool {
	s.mu.Lock()
	defer s.mu.Unlock()

	session, ok := s.sessions[roomCode]
	if !ok {
		return false
	}

	session.HostAddress = address
	session.HostPort = port
	session.Status = SessionStatusReady

	return true
}

// SetStatus 更新会话状态。会话不存在时返回 false。
func (s *SessionStore) SetStatus(roomCode, status string) bool {
	s.mu.Lock()
	defer s.mu.Unlock()

	session, ok := s.sessions[roomCode]
	if !ok {
		return false
	}

	session.Status = status

	return true
}

// Remove 移除会话。
func (s *SessionStore) Remove(roomCode string) {
	s.mu.Lock()
	defer s.mu.Unlock()

	delete(s.sessions, roomCode)
}

// newSessionID 生成 16 位十六进制会话 ID。
func newSessionID() string {
	buf := make([]byte, 8)
	if _, err := rand.Read(buf); err != nil {
		// 随机源失败时退化为时间戳，保证仍能生成唯一 ID。
		return hex.EncodeToString([]byte(time.Now().Format("150405.000000")))
	}

	return hex.EncodeToString(buf)
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd server && go test ./internal/room/ -run TestSessionStore -v`
Expected: PASS，5 个测试全部通过

- [ ] **Step 5: 提交**

```bash
git add server/internal/room/session.go server/internal/room/session_test.go
git commit -m "feat(room): 新增游戏会话数据结构与内存存储"
```

---

### Task 2: 服务端会话服务与 HTTP 接口

**Files:**
- Create: `server/internal/room/session_service.go`
- Modify: `server/internal/room/handler.go`
- Modify: `server/internal/room/service.go`（`Service` 增加 `sessions` 字段）
- Test: `server/internal/room/session_service_test.go`

**Interfaces:**
- Consumes: Task 1 的 `SessionStore`、`GameSession`、状态常量
- Produces:
  - `func (s *Service) CreateSession(ctx context.Context, code string, accountID int64) (*GameSession, error)`
  - `func (s *Service) GetSession(code string) (*GameSession, bool)`
  - `func (s *Service) MarkSessionReady(code string, accountID int64, address string, port int) (*GameSession, error)`
  - `func (s *Service) CloseSession(code string, accountID int64) error`
  - 错误：`ErrNotHost`（已存在）、`ErrSessionNotFound`（新增）
  - HTTP 路由：`POST /rooms/:code/session`、`GET /rooms/:code/session`、`POST /rooms/:code/session/ready`、`DELETE /rooms/:code/session`

- [ ] **Step 1: 写失败测试**

创建 `server/internal/room/session_service_test.go`：

```go
package room

import (
	"context"
	"errors"
	"testing"

	"github.com/rs/zerolog"
)

// newTestService 构造一个只依赖内存注册表与会话存储的 Service。
func newTestService() *Service {
	return &Service{
		registry: NewRegistry(),
		sessions: NewSessionStore(),
		logger:   zerolog.Nop(),
	}
}

// seedRoom 在注册表里放一个房间，房主为 hostID。
func seedRoom(s *Service, code string, hostID int64) *Room {
	room := &Room{
		Code:       code,
		HostID:     hostID,
		Name:       "测试房",
		MaxPlayers: 4,
		Status:     StatusWaiting,
		members:    make(map[int64]*Member),
	}
	room.members[hostID] = &Member{AccountID: hostID, DisplayName: "房主"}
	s.registry.Add(room)

	return room
}

func TestCreateSessionOnlyHost(t *testing.T) {
	svc := newTestService()
	seedRoom(svc, "ABCD1234", 24)

	if _, err := svc.CreateSession(context.Background(), "ABCD1234", 25); !errors.Is(err, ErrNotHost) {
		t.Fatalf("非房主创建会话应返回 ErrNotHost，实际 %v", err)
	}

	session, err := svc.CreateSession(context.Background(), "ABCD1234", 24)
	if err != nil {
		t.Fatalf("房主创建会话失败：%v", err)
	}
	if session.Status != SessionStatusConnecting {
		t.Fatalf("初始状态应为 connecting，实际 %s", session.Status)
	}
}

func TestMarkSessionReadyOnlyHost(t *testing.T) {
	svc := newTestService()
	seedRoom(svc, "ABCD1234", 24)
	svc.CreateSession(context.Background(), "ABCD1234", 24)

	if _, err := svc.MarkSessionReady("ABCD1234", 25, "1.2.3.4", 7777); !errors.Is(err, ErrNotHost) {
		t.Fatalf("非房主标记就绪应返回 ErrNotHost，实际 %v", err)
	}

	session, err := svc.MarkSessionReady("ABCD1234", 24, "10.6.136.22", 7777)
	if err != nil {
		t.Fatalf("房主标记就绪失败：%v", err)
	}
	if session.Status != SessionStatusReady || session.HostAddress != "10.6.136.22" {
		t.Fatalf("会话未正确就绪：%+v", session)
	}
}

func TestGetSessionMissing(t *testing.T) {
	svc := newTestService()
	seedRoom(svc, "ABCD1234", 24)

	if _, ok := svc.GetSession("ABCD1234"); ok {
		t.Fatal("未创建会话时 GetSession 应返回 false")
	}
}

func TestCloseSessionOnlyHost(t *testing.T) {
	svc := newTestService()
	seedRoom(svc, "ABCD1234", 24)
	svc.CreateSession(context.Background(), "ABCD1234", 24)

	if err := svc.CloseSession("ABCD1234", 25); !errors.Is(err, ErrNotHost) {
		t.Fatalf("非房主关闭会话应返回 ErrNotHost，实际 %v", err)
	}

	if err := svc.CloseSession("ABCD1234", 24); err != nil {
		t.Fatalf("房主关闭会话失败：%v", err)
	}
	if _, ok := svc.GetSession("ABCD1234"); ok {
		t.Fatal("关闭后不应再查到会话")
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd server && go test ./internal/room/ -run TestCreateSession -v`
Expected: FAIL，报 `unknown field 'sessions'` 或 `svc.CreateSession undefined`

- [ ] **Step 3: 实现最小代码**

在 `server/internal/room/service.go` 的 `Service` 结构体增加字段：

```go
// Service 房间业务逻辑。
type Service struct {
	repo     *Repository
	registry *Registry
	sessions *SessionStore
	logger   zerolog.Logger
}
```

在 `NewService` 中初始化：

```go
func NewService(repo *Repository, registry *Registry, logger zerolog.Logger) *Service {
	return &Service{
		repo:     repo,
		registry: registry,
		sessions: NewSessionStore(),
		logger:   logger,
	}
}
```

创建 `server/internal/room/session_service.go`：

```go
package room

import (
	"context"
	"errors"
)

// ErrSessionNotFound 会话不存在。
var ErrSessionNotFound = errors.New("本局会话不存在")

// CreateSession 房主创建本局游戏会话。
//
// 只有房主可以创建：会话决定谁提供 Host 连接，非房主创建会让成员连到错误的目标。
func (s *Service) CreateSession(ctx context.Context, code string, accountID int64) (*GameSession, error) {
	room, err := s.mustGetRoom(ctx, code)
	if err != nil {
		return nil, err
	}

	if room.HostID != accountID {
		return nil, ErrNotHost
	}

	session := s.sessions.Create(code, accountID)

	s.logger.Info().
		Str("room_code", code).
		Str("session_id", session.ID).
		Int64("host_account", accountID).
		Msg("游戏会话已创建")

	return session, nil
}

// GetSession 查询房间当前会话。
//
// 成员用它补齐错过的推送：连接信息是可查询状态，不是一次性事件。
func (s *Service) GetSession(code string) (*GameSession, bool) {
	return s.sessions.Get(code)
}

// MarkSessionReady 房主写入自己的连接地址，标记会话可连接。
func (s *Service) MarkSessionReady(code string, accountID int64, address string, port int) (*GameSession, error) {
	room, ok := s.registry.Get(code)
	if !ok {
		return nil, ErrRoomNotFound
	}

	if room.HostID != accountID {
		return nil, ErrNotHost
	}

	if !s.sessions.SetReady(code, address, port) {
		return nil, ErrSessionNotFound
	}

	session, _ := s.sessions.Get(code)

	s.logger.Info().
		Str("room_code", code).
		Str("session_id", session.ID).
		Str("host_address", address).
		Int("host_port", port).
		Msg("游戏会话已就绪")

	return session, nil
}

// CloseSession 房主结束本局会话。
func (s *Service) CloseSession(code string, accountID int64) error {
	room, ok := s.registry.Get(code)
	if !ok {
		return ErrRoomNotFound
	}

	if room.HostID != accountID {
		return ErrNotHost
	}

	s.sessions.Remove(code)

	s.logger.Info().Str("room_code", code).Msg("游戏会话已关闭")

	return nil
}
```

在 `server/internal/room/handler.go` 的 `Register` 中新增路由：

```go
	group.POST("/:code/session", h.createSession)
	group.GET("/:code/session", h.getSession)
	group.POST("/:code/session/ready", h.markSessionReady)
	group.DELETE("/:code/session", h.closeSession)
```

在 `server/internal/room/handler.go` 末尾新增 handler：

```go
type sessionReadyRequest struct {
	HostAddress string `json:"host_address" binding:"required"`
	HostPort    int    `json:"host_port" binding:"required"`
}

// createSession 房主创建本局游戏会话。
func (h *Handler) createSession(c *gin.Context) {
	accountID, ok := middleware.AccountIDFrom(c)
	if !ok {
		httpx.Unauthorized(c, httpx.CodeTokenInvalid, "未认证")
		return
	}

	session, err := h.service.CreateSession(c.Request.Context(), c.Param("code"), accountID)
	if err != nil {
		h.writeError(c, err, "创建游戏会话失败")
		return
	}

	c.JSON(http.StatusCreated, session)
}

// getSession 查询房间当前会话。
func (h *Handler) getSession(c *gin.Context) {
	session, ok := h.service.GetSession(c.Param("code"))
	if !ok {
		httpx.Fail(c, http.StatusNotFound, httpx.CodeRoomNotFound, "本局会话不存在")
		return
	}

	c.JSON(http.StatusOK, session)
}

// markSessionReady 房主写入连接地址。
func (h *Handler) markSessionReady(c *gin.Context) {
	accountID, ok := middleware.AccountIDFrom(c)
	if !ok {
		httpx.Unauthorized(c, httpx.CodeTokenInvalid, "未认证")
		return
	}

	var req sessionReadyRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		httpx.BadRequest(c, "请求体格式错误：host_address 与 host_port 为必填")
		return
	}

	session, err := h.service.MarkSessionReady(c.Param("code"), accountID, req.HostAddress, req.HostPort)
	if err != nil {
		h.writeError(c, err, "标记会话就绪失败")
		return
	}

	c.JSON(http.StatusOK, session)
}

// closeSession 房主结束本局会话。
func (h *Handler) closeSession(c *gin.Context) {
	accountID, ok := middleware.AccountIDFrom(c)
	if !ok {
		httpx.Unauthorized(c, httpx.CodeTokenInvalid, "未认证")
		return
	}

	if err := h.service.CloseSession(c.Param("code"), accountID); err != nil {
		h.writeError(c, err, "关闭游戏会话失败")
		return
	}

	c.Status(http.StatusNoContent)
}
```

在 `handler.go` 的 `writeError` 中补充 `ErrSessionNotFound` 分支：

```go
	case errors.Is(err, ErrSessionNotFound):
		httpx.Fail(c, http.StatusNotFound, httpx.CodeRoomNotFound, err.Error())
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd server && go test ./internal/room/ -v`
Expected: PASS，全部测试通过

- [ ] **Step 5: 提交**

```bash
git add server/internal/room/session_service.go server/internal/room/session_service_test.go server/internal/room/service.go server/internal/room/handler.go
git commit -m "feat(room): 新增游戏会话服务与 HTTP 接口"
```

---

### Task 3: 信令下发游戏会话

**Files:**
- Modify: `server/internal/signal/protocol.go`
- Modify: `server/internal/signal/handler.go`
- Modify: `server/internal/signal/client.go`
- Modify: `server/cmd/api/main.go`
- Test: `server/cmd/sigtest/main.go`（扩展端到端验证）

**Interfaces:**
- Consumes: Task 2 的 `Service.GetSession`
- Produces:
  - `GameStartingPayload` 增加 `SessionID string \`json:"session_id"\``
  - 新增 `GameSessionPayload { SessionID, RoomCode, TransportMode, HostAccountID, HostAddress, HostPort, Status }`
  - 新增消息类型 `TypeGameSession MessageType = "game_session"`
  - `RoomLookup` 接口增加 `SessionInfo(roomCode string) (SessionInfo, bool)`
  - `type SessionInfo struct { ID, TransportMode, HostAddress string; HostPort int; Status string; HostAccountID int64 }`

- [ ] **Step 1: 写失败测试**

在 `server/cmd/sigtest/main.go` 的 `main()` 中，`A（房主）start_game` 之后追加：

```go
	// 房主创建本局会话并标记就绪，成员应能收到 game_session。
	post(a, "/rooms/"+code+"/session", nil)
	post(a, "/rooms/"+code+"/session/ready", map[string]any{
		"host_address": "10.6.136.22",
		"host_port":    7777,
	})

	gs := readUntil(wsB, "game_session", 3*time.Second)
	check("B 收到 game_session", gs != nil, "")
	if gs != nil {
		p := gs["payload"].(map[string]any)
		check("game_session 带 session_id", p["session_id"] != nil && p["session_id"] != "", "")
		check("game_session 带 host_address", p["host_address"] == "10.6.136.22",
			fmt.Sprintf("host_address=%v", p["host_address"]))
	}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd server && go run ./cmd/sigtest`
Expected: FAIL，`B 收到 game_session` 失败（超时返回 nil）

- [ ] **Step 3: 实现最小代码**

在 `server/internal/signal/protocol.go` 增加消息类型：

```go
	// TypeGameSession 本局游戏会话信息（连接地址等）。
	TypeGameSession MessageType = "game_session"
```

修改 `GameStartingPayload`：

```go
// GameStartingPayload 开始游戏。
type GameStartingPayload struct {
	HostAccountID int64  `json:"host_account_id"`
	SessionID     string `json:"session_id"`
}
```

新增 payload：

```go
// GameSessionPayload 本局游戏会话信息。
//
// 成员据此连接房主。做成可查询状态而非一次性事件：
// 成员可能错过推送，需要能主动补齐。
type GameSessionPayload struct {
	SessionID     string `json:"session_id"`
	RoomCode      string `json:"room_code"`
	TransportMode string `json:"transport_mode"`
	HostAccountID int64  `json:"host_account_id"`
	HostAddress   string `json:"host_address"`
	HostPort      int    `json:"host_port"`
	Status        string `json:"status"`
}
```

在 `server/internal/signal/handler.go` 的 `RoomLookup` 接口增加方法：

```go
	// SessionInfo 返回房间当前游戏会话；不存在时返回 false。
	SessionInfo(roomCode string) (SessionInfo, bool)
```

在同文件新增类型：

```go
// SessionInfo 游戏会话的连接信息。
//
// 定义在消费方（signal）而非实现方（room），避免 signal 直接依赖 room 包。
type SessionInfo struct {
	ID            string
	TransportMode string
	HostAccountID int64
	HostAddress   string
	HostPort      int
	Status        string
}
```

在 `server/internal/signal/handler.go` 的 `NewHandler` 中，`hub.SetDisconnectHandler` 之后增加会话广播钩子：

```go
	hub.SetSessionBroadcaster(func(roomCode string) {
		info, ok := rooms.SessionInfo(roomCode)
		if !ok {
			return
		}

		payload := GameSessionPayload{
			SessionID:     info.ID,
			RoomCode:      roomCode,
			TransportMode: info.TransportMode,
			HostAccountID: info.HostAccountID,
			HostAddress:   info.HostAddress,
			HostPort:      info.HostPort,
			Status:        info.Status,
		}

		if env, err := newEnvelope(TypeGameSession, payload); err == nil {
			hub.Broadcast(roomCode, env, 0)
		}
	})
```

在 `server/internal/signal/hub.go` 的 `Hub` 结构体增加字段与方法：

```go
	// sessionBroadcaster 广播本局会话信息，由 room 模块注入。
	sessionBroadcaster func(roomCode string)
```

```go
// SetSessionBroadcaster 注入会话广播函数。
func (h *Hub) SetSessionBroadcaster(fn func(roomCode string)) {
	h.sessionBroadcaster = fn
}

// BroadcastSession 向房间广播当前会话信息。
func (h *Hub) BroadcastSession(roomCode string) {
	if h.sessionBroadcaster != nil {
		h.sessionBroadcaster(roomCode)
	}
}
```

在 `server/internal/signal/client.go` 的 `handleStartGame` 中，广播 `game_starting` 后追加会话广播：

```go
// handleStartGame 房主发起开始游戏。
func (c *Client) handleStartGame(env Envelope) {
	if !c.isHost() {
		c.sendError(ErrCodeNotHost, "只有房主可以开始游戏")
		return
	}

	payload := GameStartingPayload{HostAccountID: c.accountID}
	c.hub.Broadcast(c.roomCode, c.mustEnvelope(TypeGameStarting, payload), 0)

	// 会话信息单独广播：房主随后会写入连接地址，
	// 成员拿到地址后即可连接，不必再靠一次性信令交换。
	c.hub.BroadcastSession(c.roomCode)

	c.log.Info().
		Str("room_code", c.roomCode).
		Int64("host_account", c.accountID).
		Msg("房主发起开始游戏")
}
```

在 `server/cmd/api/main.go` 中，`roomService` 已实现 `SessionInfo`，需在 `room` 包补一个适配方法（`server/internal/room/service.go`）：

```go
// SessionInfo 返回房间当前游戏会话的连接信息，供信令模块广播。
func (s *Service) SessionInfo(code string) (signal.SessionInfo, bool) {
	session, ok := s.sessions.Get(code)
	if !ok {
		return signal.SessionInfo{}, false
	}

	return signal.SessionInfo{
		ID:            session.ID,
		TransportMode: session.TransportMode,
		HostAccountID: session.HostAccountID,
		HostAddress:   session.HostAddress,
		HostPort:      session.HostPort,
		Status:        session.Status,
	}, true
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd server && go build ./... && go run ./cmd/sigtest`
Expected: PASS，全部检查项通过，含 `B 收到 game_session`

- [ ] **Step 5: 提交**

```bash
git add server/internal/signal/ server/internal/room/service.go server/cmd/sigtest/main.go
git commit -m "feat(signal): 下发本局游戏会话连接信息"
```

---

## 阶段 2：客户端会话数据层

### Task 4: 客户端会话数据模型与 API

**Files:**
- Modify: `Assets/Network/Runtime/ApiModels.cs`
- Modify: `Assets/Network/Runtime/SignalProtocol.cs`
- Modify: `Assets/Network/Runtime/ApiClient.cs`
- Modify: `Assets/Network/Runtime/NetworkSession.cs`

**Interfaces:**
- Consumes: Task 3 的服务端接口
- Produces:
  - `GameSessionDto { session_id, room_code, transport_mode, host_account_id, host_address, host_port, status }`
  - `GameSessionPayload`（信令侧，字段同上）
  - `ApiClient.CreateSession(roomCode, onSuccess, onError)`
  - `ApiClient.GetSession(roomCode, onSuccess, onError)`
  - `ApiClient.MarkSessionReady(roomCode, address, port, onSuccess, onError)`
  - `ApiClient.CloseSession(roomCode, onSuccess, onError)`
  - `NetworkSession.CurrentSession`（`GameSessionDto`）
  - `NetworkSession.SessionChanged`（`event Action<GameSessionDto>`）
  - `NetworkSession.CreateSession()` / `FetchSession()` / `MarkSessionReady(address, port)` / `CloseSession()`

- [ ] **Step 1: 写失败测试**

Unity 侧无单元测试框架，用编译 + 运行时日志验证。先在 `NetworkSession` 中加一个自检日志，确认字段可访问：

在 `Assets/Network/Runtime/NetworkSession.cs` 的 `OnRoomState` 末尾临时加：

```csharp
            Debug.Log($"[NetworkSession] 房间状态更新，成员数={CurrentRoom?.members?.Length ?? 0}");
```

- [ ] **Step 2: 运行确认当前无会话字段**

Run: Unity 中执行 `Assets/Refresh`，然后 `unity_get_compilation_errors`
Expected: 0 错误（当前代码可编译，但无 `CurrentSession`）

- [ ] **Step 3: 实现**

在 `Assets/Network/Runtime/ApiModels.cs` 末尾（`ErrorBodyDto` 之后、命名空间内）新增：

```csharp
    /// <summary>本局游戏会话。对应服务端 room.GameSession。</summary>
    [Serializable]
    public sealed class GameSessionDto
    {
        public string session_id;
        public string room_code;
        public string transport_mode;
        public long host_account_id;
        public string host_address;
        public int host_port;
        public string status;
    }
```

在 `Assets/Network/Runtime/SignalProtocol.cs` 的 `SignalType` 中新增：

```csharp
        public const string GameSession = "game_session";
```

修改 `GameStartingPayload`：

```csharp
    /// <summary>开始游戏。</summary>
    [Serializable]
    public sealed class GameStartingPayload
    {
        public long host_account_id;
        public string session_id;
    }
```

新增 payload：

```csharp
    /// <summary>本局游戏会话信息。</summary>
    [Serializable]
    public sealed class GameSessionPayload
    {
        public string session_id;
        public string room_code;
        public string transport_mode;
        public long host_account_id;
        public string host_address;
        public int host_port;
        public string status;
    }
```

在 `Assets/Network/Runtime/SignalClient.cs` 的 `SignalType` 事件区新增：

```csharp
        /// <summary>收到本局游戏会话信息。</summary>
        public event Action<GameSessionPayload> GameSessionReceived;
```

在 `SignalClient.Dispatch` 的 `switch` 中新增分支：

```csharp
                case SignalType.GameSession:
                    GameSessionReceived?.Invoke(Parse<GameSessionPayload>(envelope.payload));
                    break;
```

在 `Assets/Network/Runtime/ApiClient.cs` 的「房间」区块末尾新增：

```csharp
        // ---- 游戏会话 ----

        /// <summary>房主创建本局游戏会话。</summary>
        public IEnumerator CreateSession(string roomCode, Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            return Send<GameSessionDto>(UnityWebRequest.kHttpVerbPOST, "/rooms/" + roomCode + "/session", null, true,
                onSuccess, onError);
        }

        /// <summary>查询房间当前会话（成员补齐错过的推送）。</summary>
        public IEnumerator GetSession(string roomCode, Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            return Send<GameSessionDto>(UnityWebRequest.kHttpVerbGET, "/rooms/" + roomCode + "/session", null, true,
                onSuccess, onError);
        }

        /// <summary>房主写入连接地址，标记会话可连接。</summary>
        public IEnumerator MarkSessionReady(string roomCode, string hostAddress, int hostPort,
            Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            var body = new SessionReadyRequestDto { host_address = hostAddress, host_port = hostPort };

            return Send<GameSessionDto>(UnityWebRequest.kHttpVerbPOST, "/rooms/" + roomCode + "/session/ready", body, true,
                onSuccess, onError);
        }

        /// <summary>房主结束本局会话。</summary>
        public IEnumerator CloseSession(string roomCode, Action onSuccess, Action<ApiError> onError)
        {
            return SendRaw(UnityWebRequest.kHttpVerbDELETE, "/rooms/" + roomCode + "/session", null, true,
                _ => onSuccess?.Invoke(), onError);
        }
```

在 `ApiModels.cs` 新增请求体：

```csharp
    /// <summary>标记会话就绪请求。</summary>
    [Serializable]
    public sealed class SessionReadyRequestDto
    {
        public string host_address;
        public int host_port;
    }
```

在 `Assets/Network/Runtime/NetworkSession.cs` 新增字段与事件：

```csharp
        /// <summary>当前本局游戏会话。未创建时为 null。</summary>
        public GameSessionDto CurrentSession { get; private set; }

        /// <summary>本局游戏会话变化（创建 / 就绪 / 关闭）。</summary>
        public event Action<GameSessionDto> SessionChanged;
```

在 `HookSignalEvents` 中订阅：

```csharp
            signal.GameSessionReceived += OnGameSessionReceived;
```

在 `UnhookSignalEvents` 中退订：

```csharp
            signal.GameSessionReceived -= OnGameSessionReceived;
```

新增处理方法：

```csharp
        private void OnGameSessionReceived(GameSessionPayload payload)
        {
            if (payload == null)
                return;

            CurrentSession = new GameSessionDto
            {
                session_id = payload.session_id,
                room_code = payload.room_code,
                transport_mode = payload.transport_mode,
                host_account_id = payload.host_account_id,
                host_address = payload.host_address,
                host_port = payload.host_port,
                status = payload.status,
            };

            Debug.Log($"[NetworkSession] 收到游戏会话 {payload.session_id}，状态={payload.status}，地址={payload.host_address}:{payload.host_port}");
            SessionChanged?.Invoke(CurrentSession);
        }
```

新增公开方法（放在「房间」区块之后）：

```csharp
        // ---- 游戏会话 ----

        /// <summary>房主创建本局会话。</summary>
        public IEnumerator CreateSession(Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onError?.Invoke(new ApiError("ROOM_NOT_FOUND", "当前不在房间内"));
                yield break;
            }

            yield return api.CreateSession(CurrentRoom.room_code, session =>
            {
                CurrentSession = session;
                SessionChanged?.Invoke(session);
                onSuccess?.Invoke(session);
            }, onError);
        }

        /// <summary>查询当前会话（成员补齐错过的推送）。</summary>
        public IEnumerator FetchSession(Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onError?.Invoke(new ApiError("ROOM_NOT_FOUND", "当前不在房间内"));
                yield break;
            }

            yield return api.GetSession(CurrentRoom.room_code, session =>
            {
                CurrentSession = session;
                SessionChanged?.Invoke(session);
                onSuccess?.Invoke(session);
            }, onError);
        }

        /// <summary>房主写入连接地址。</summary>
        public IEnumerator MarkSessionReady(string hostAddress, int hostPort,
            Action<GameSessionDto> onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onError?.Invoke(new ApiError("ROOM_NOT_FOUND", "当前不在房间内"));
                yield break;
            }

            yield return api.MarkSessionReady(CurrentRoom.room_code, hostAddress, hostPort, session =>
            {
                CurrentSession = session;
                SessionChanged?.Invoke(session);
                onSuccess?.Invoke(session);
            }, onError);
        }

        /// <summary>房主结束本局会话。</summary>
        public IEnumerator CloseSession(Action onSuccess, Action<ApiError> onError)
        {
            if (CurrentRoom == null)
            {
                onSuccess?.Invoke();
                yield break;
            }

            yield return api.CloseSession(CurrentRoom.room_code, () =>
            {
                CurrentSession = null;
                SessionChanged?.Invoke(null);
                onSuccess?.Invoke();
            }, onError);
        }
```

在 `LeaveRoom` 与 `CloseRoom` 中，`CurrentRoom = null;` 之前补一行清理会话：

```csharp
            CurrentSession = null;
```

- [ ] **Step 4: 运行确认编译通过**

Run: Unity 中执行 `Assets/Refresh`，然后 `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 5: 提交**

```bash
git add Assets/Network/Runtime/ApiModels.cs Assets/Network/Runtime/SignalProtocol.cs Assets/Network/Runtime/SignalClient.cs Assets/Network/Runtime/ApiClient.cs Assets/Network/Runtime/NetworkSession.cs
git commit -m "feat(network): 客户端接入本局游戏会话数据"
```

---

## 阶段 3：常驻网络层与状态机

### Task 5: 传输策略接口与 LAN 实现

**Files:**
- Create: `Assets/Gameplay/Runtime/IConnectionStrategy.cs`
- Create: `Assets/Gameplay/Runtime/LanConnectionStrategy.cs`

**Interfaces:**
- Consumes: `Unity.Netcode.NetworkManager`、`Unity.Netcode.Transports.UTP.UnityTransport`
- Produces:
  - `interface IConnectionStrategy { string Mode { get; } bool ConfigureHost(NetworkManager nm, int port); bool ConfigureClient(NetworkManager nm, string address, int port); }`
  - `sealed class LanConnectionStrategy : IConnectionStrategy`
  - `static class ConnectionStrategies { static IConnectionStrategy For(string mode); }`

- [ ] **Step 1: 写失败测试**

无单元测试框架，用编译验证。先创建接口文件，确认引用类型可用。

- [ ] **Step 2: 确认当前无该类型**

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误（类型尚不存在）

- [ ] **Step 3: 实现**

创建 `Assets/Gameplay/Runtime/IConnectionStrategy.cs`：

```csharp
using Unity.Netcode;

namespace Tower.Netcode
{
    /// <summary>
    /// 联机传输策略：把「如何让 Host 监听、Client 连接」从协调器里抽出来。
    ///
    /// 本阶段只有 LAN 直连；后续接入自建 Relay 时新增一个实现即可，
    /// MultiplayerCoordinator 与上层流程不需要改动。
    /// </summary>
    public interface IConnectionStrategy
    {
        /// <summary>策略标识，与服务端 session.transport_mode 对应。</summary>
        string Mode { get; }

        /// <summary>配置 Host 监听。返回 false 表示配置失败。</summary>
        bool ConfigureHost(NetworkManager networkManager, int port);

        /// <summary>配置 Client 连接目标。返回 false 表示配置失败。</summary>
        bool ConfigureClient(NetworkManager networkManager, string address, int port);
    }
}
```

创建 `Assets/Gameplay/Runtime/LanConnectionStrategy.cs`：

```csharp
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// 局域网直连策略：Host 绑定 0.0.0.0，Client 直连房主内网地址。
    ///
    /// 适用于本机双开与同一路由器下的设备。异地公网需要 Relay 策略。
    /// </summary>
    public sealed class LanConnectionStrategy : IConnectionStrategy
    {
        public string Mode => "lan";

        public bool ConfigureHost(NetworkManager networkManager, int port)
        {
            var transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[LanConnectionStrategy] NetworkManager 上未找到 UnityTransport。");
                return false;
            }

            // 监听所有网卡：本机双开走 127.0.0.1，局域网设备走内网地址，同一份配置都覆盖。
            transport.SetConnectionData("0.0.0.0", (ushort)port, "0.0.0.0");

            return true;
        }

        public bool ConfigureClient(NetworkManager networkManager, string address, int port)
        {
            var transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[LanConnectionStrategy] NetworkManager 上未找到 UnityTransport。");
                return false;
            }

            transport.SetConnectionData(address, (ushort)port, "0.0.0.0");

            return true;
        }
    }

    /// <summary>传输策略工厂。</summary>
    public static class ConnectionStrategies
    {
        /// <summary>按服务端下发的 transport_mode 取策略。未知模式回退到 LAN。</summary>
        public static IConnectionStrategy For(string mode)
        {
            switch (mode)
            {
                case "lan":
                default:
                    return new LanConnectionStrategy();
            }
        }
    }
}
```

- [ ] **Step 4: 运行确认编译通过**

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/IConnectionStrategy.cs Assets/Gameplay/Runtime/LanConnectionStrategy.cs
git commit -m "feat(netcode): 新增传输策略接口与局域网实现"
```

---

### Task 6: MultiplayerCoordinator 状态机

**Files:**
- Create: `Assets/Gameplay/Runtime/MultiplayerCoordinator.cs`

**Interfaces:**
- Consumes: Task 4 的 `NetworkSession.CurrentSession` / `SessionChanged` / `MarkSessionReady`；Task 5 的 `IConnectionStrategy`
- Produces:
  - `enum MultiplayerState { Idle, Preparing, Connecting, WaitingForPlayers, LoadingBattleScene, InGame, Disconnecting, Failed }`
  - `sealed class MultiplayerCoordinator : MonoBehaviour`
  - `static MultiplayerCoordinator Instance`
  - `MultiplayerState State { get; }`
  - `event Action<MultiplayerState> StateChanged`
  - `event Action<string> Failed`
  - `IEnumerator StartHostFlow()`
  - `IEnumerator StartClientFlow()`
  - `void LoadBattleScene()`
  - `void Shutdown()`
  - `bool IsHost { get; }`
  - `int ConnectedMemberCount { get; }`
  - `int ExpectedMemberCount { get; }`

- [ ] **Step 1: 写失败测试**

无单元测试框架。先写一个最小可编译版本，用运行时日志验证状态流转。

- [ ] **Step 2: 确认当前无该类型**

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 3: 实现**

创建 `Assets/Gameplay/Runtime/MultiplayerCoordinator.cs`：

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using Tower.Network;
using Unity.Netcode;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>联机生命周期状态。</summary>
    public enum MultiplayerState
    {
        /// <summary>未联机（单机或未进房）。</summary>
        Idle,

        /// <summary>正在准备 Host / Client。</summary>
        Preparing,

        /// <summary>正在建立 NGO 连接。</summary>
        Connecting,

        /// <summary>Host 等待所有房间成员连入。</summary>
        WaitingForPlayers,

        /// <summary>Host 正在切换战斗场景。</summary>
        LoadingBattleScene,

        /// <summary>已进入战斗。</summary>
        InGame,

        /// <summary>正在断开。</summary>
        Disconnecting,

        /// <summary>失败，需玩家重试或退出房间。</summary>
        Failed,
    }

    /// <summary>
    /// 联机生命周期协调器（常驻）。
    ///
    /// 核心原则：**先建立 NGO 连接，再由 Host 统一切换场景**。
    /// 旧实现是「两端各自切场景，再尝试交换地址」，会因场景加载速度差异
    /// 导致连接消息丢失、两端各玩各的。这里把顺序倒过来，从架构上消除竞态。
    ///
    /// 单机模式完全不经过本组件：State 保持 Idle，NetworkManager 不启动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerCoordinator : MonoBehaviour
    {
        [Tooltip("房主监听端口（UDP）。")]
        [SerializeField] private int hostPort = 7777;

        [Tooltip("等待所有成员连接的超时（秒）。")]
        [SerializeField] private float connectTimeoutSeconds = 30f;

        [Tooltip("战斗场景名，需已在 Build Settings 登记。")]
        [SerializeField] private string battleSceneName = "BattleScene";

        private static MultiplayerCoordinator instance;

        /// <summary>全局单例。未挂载时返回 null。</summary>
        public static MultiplayerCoordinator Instance => instance;

        private NetworkManager networkManager;
        private MultiplayerState state = MultiplayerState.Idle;

        /// <summary>已连接的客户端 ID 集合（Host 侧维护）。</summary>
        private readonly HashSet<ulong> connectedClients = new HashSet<ulong>();

        /// <summary>clientId → accountId 映射，供玩家生成与身份绑定使用。</summary>
        private readonly Dictionary<ulong, long> clientAccounts = new Dictionary<ulong, long>();

        public MultiplayerState State => state;

        /// <summary>状态变化。</summary>
        public event Action<MultiplayerState> StateChanged;

        /// <summary>失败，参数为可展示的原因。</summary>
        public event Action<string> Failed;

        /// <summary>本端是否为 Host。</summary>
        public bool IsHost => networkManager != null && networkManager.IsHost;

        /// <summary>已连接的成员数（含 Host 自己）。</summary>
        public int ConnectedMemberCount => connectedClients.Count;

        /// <summary>本局预期成员数（房间成员数）。</summary>
        public int ExpectedMemberCount
        {
            get
            {
                var room = NetworkSession.Instance != null ? NetworkSession.Instance.CurrentRoom : null;
                return room?.members?.Length ?? 0;
            }
        }

        /// <summary>取某客户端对应的账号 ID。未登记时返回 0。</summary>
        public long AccountIdOf(ulong clientId)
        {
            return clientAccounts.TryGetValue(clientId, out var accountId) ? accountId : 0L;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            networkManager = GetComponent<NetworkManager>();
            if (networkManager == null)
                networkManager = FindObjectOfType<NetworkManager>();

            if (networkManager == null)
            {
                Debug.LogError("[MultiplayerCoordinator] 未找到 NetworkManager，联机不可用。");
                return;
            }

            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;

            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        // ---- 房主流程 ----

        /// <summary>
        /// 房主出发流程：创建会话 → 启动 Host → 写入地址 → 等待成员 → 切场景。
        /// </summary>
        public IEnumerator StartHostFlow()
        {
            if (state != MultiplayerState.Idle && state != MultiplayerState.Failed)
            {
                Debug.LogWarning($"[MultiplayerCoordinator] 当前状态 {state}，忽略重复的房主启动。");
                yield break;
            }

            var session = NetworkSession.Instance;
            if (session == null || session.CurrentRoom == null)
            {
                Fail("未进入房间，无法开始联机");
                yield break;
            }

            SetState(MultiplayerState.Preparing);

            // 1. 创建本局会话。
            GameSessionDto created = null;
            ApiError createError = null;
            yield return session.CreateSession(s => created = s, e => createError = e);

            if (createError != null || created == null)
            {
                Fail($"创建游戏会话失败：{createError?.Message ?? "未知错误"}");
                yield break;
            }

            // 2. 启动 Host。
            var strategy = ConnectionStrategies.For(created.transport_mode);
            if (!strategy.ConfigureHost(networkManager, hostPort))
            {
                Fail("配置房主监听失败");
                yield break;
            }

            if (!networkManager.StartHost())
            {
                Fail("启动房主失败，端口可能被占用");
                yield break;
            }

            SetState(MultiplayerState.Connecting);

            // 3. 写入连接地址，成员据此连接。
            var address = LocalAddress();
            ApiError readyError = null;
            yield return session.MarkSessionReady(address, hostPort, null, e => readyError = e);

            if (readyError != null)
            {
                Fail($"写入连接地址失败：{readyError.Message}");
                yield break;
            }

            Debug.Log($"[MultiplayerCoordinator] 房主已就绪，地址 {address}:{hostPort}");

            // 4. 等待所有房间成员连入。
            SetState(MultiplayerState.WaitingForPlayers);
            yield return WaitForAllMembersConnected();

            if (state == MultiplayerState.Failed)
                yield break;

            // 5. 统一切换战斗场景。
            LoadBattleScene();
        }

        /// <summary>等待所有房间成员连入 NGO。超时则失败。</summary>
        private IEnumerator WaitForAllMembersConnected()
        {
            var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (ConnectedMemberCount >= ExpectedMemberCount && ExpectedMemberCount > 0)
                {
                    Debug.Log($"[MultiplayerCoordinator] 全部 {ExpectedMemberCount} 名成员已连接。");
                    yield break;
                }

                yield return null;
            }

            Fail($"等待成员连接超时（{ConnectedMemberCount}/{ExpectedMemberCount}）");
        }

        // ---- 成员流程 ----

        /// <summary>
        /// 成员出发流程：等待会话就绪 → 启动 Client → 等待 Host 切场景。
        /// </summary>
        public IEnumerator StartClientFlow()
        {
            if (state != MultiplayerState.Idle && state != MultiplayerState.Failed)
            {
                Debug.LogWarning($"[MultiplayerCoordinator] 当前状态 {state}，忽略重复的成员启动。");
                yield break;
            }

            var session = NetworkSession.Instance;
            if (session == null || session.CurrentRoom == null)
            {
                Fail("未进入房间，无法加入联机");
                yield break;
            }

            SetState(MultiplayerState.Preparing);

            // 1. 取会话信息：先看已有推送，没有则主动查询。
            var info = session.CurrentSession;
            if (info == null || string.IsNullOrEmpty(info.host_address))
            {
                GameSessionDto fetched = null;
                ApiError fetchError = null;
                yield return session.FetchSession(s => fetched = s, e => fetchError = e);

                if (fetchError != null || fetched == null)
                {
                    Fail($"获取游戏会话失败：{fetchError?.Message ?? "会话不存在"}");
                    yield break;
                }

                info = fetched;
            }

            if (string.IsNullOrEmpty(info.host_address))
            {
                Fail("房主尚未就绪，请稍后重试");
                yield break;
            }

            // 2. 启动 Client。
            var strategy = ConnectionStrategies.For(info.transport_mode);
            if (!strategy.ConfigureClient(networkManager, info.host_address, info.host_port))
            {
                Fail("配置连接目标失败");
                yield break;
            }

            if (!networkManager.StartClient())
            {
                Fail("启动客户端失败");
                yield break;
            }

            SetState(MultiplayerState.Connecting);
            Debug.Log($"[MultiplayerCoordinator] 正在连接房主 {info.host_address}:{info.host_port}");

            // 3. 等待连接成功或超时。场景切换由 Host 发起，这里只等。
            var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (networkManager.IsConnectedClient)
                {
                    Debug.Log("[MultiplayerCoordinator] 已连接房主，等待房主切换场景…");
                    yield break;
                }

                yield return null;
            }

            Fail("连接房主超时");
        }

        // ---- 场景切换 ----

        /// <summary>
        /// 由 Host 统一切换战斗场景。成员端不得调用。
        /// </summary>
        public void LoadBattleScene()
        {
            if (!IsHost)
            {
                Debug.LogWarning("[MultiplayerCoordinator] 只有房主可以切换战斗场景。");
                return;
            }

            SetState(MultiplayerState.LoadingBattleScene);

            if (!networkManager.NetworkConfig.EnableSceneManagement)
            {
                Fail("NetworkManager 未启用场景管理，无法同步切换场景");
                return;
            }

            Debug.Log($"[MultiplayerCoordinator] 房主切换场景：{battleSceneName}");
            networkManager.SceneManager.LoadScene(battleSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        /// <summary>战斗场景就绪后由 BattleNetworkBinder 调用。</summary>
        public void NotifyBattleSceneReady()
        {
            SetState(MultiplayerState.InGame);
        }

        // ---- 连接回调 ----

        private void HandleClientConnected(ulong clientId)
        {
            connectedClients.Add(clientId);

            // 记录 clientId → accountId：Host 自己用本机账号，成员由连接审批写入。
            if (networkManager.IsServer && clientId == networkManager.LocalClientId)
            {
                var account = NetworkSession.Instance != null ? NetworkSession.Instance.Account : null;
                if (account != null)
                    clientAccounts[clientId] = account.account_id;
            }

            Debug.Log($"[MultiplayerCoordinator] 客户端已连接 {clientId}（{ConnectedMemberCount}/{ExpectedMemberCount}）");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            connectedClients.Remove(clientId);
            clientAccounts.Remove(clientId);

            Debug.Log($"[MultiplayerCoordinator] 客户端已断开 {clientId}（{ConnectedMemberCount}/{ExpectedMemberCount}）");

            // 成员端与房主断开：进入失败状态，不静默留在战斗里。
            if (!networkManager.IsServer && state == MultiplayerState.InGame)
                Fail("与房主的连接已断开");
        }

        /// <summary>登记某客户端对应的账号（连接审批通过后调用）。</summary>
        public void RegisterClientAccount(ulong clientId, long accountId)
        {
            clientAccounts[clientId] = accountId;
        }

        // ---- 关闭 ----

        /// <summary>关闭联机，回到 Idle。单机不受影响。</summary>
        public void Shutdown()
        {
            if (networkManager != null && networkManager.IsListening)
            {
                SetState(MultiplayerState.Disconnecting);
                networkManager.Shutdown();
            }

            connectedClients.Clear();
            clientAccounts.Clear();
            SetState(MultiplayerState.Idle);
        }

        // ---- 内部 ----

        private void SetState(MultiplayerState next)
        {
            if (state == next)
                return;

            state = next;
            Debug.Log($"[MultiplayerCoordinator] 状态 → {next}");
            StateChanged?.Invoke(next);
        }

        private void Fail(string reason)
        {
            Debug.LogError($"[MultiplayerCoordinator] 失败：{reason}");
            SetState(MultiplayerState.Failed);
            Failed?.Invoke(reason);
        }

        /// <summary>
        /// 取本机对外可用的 IPv4 地址。
        /// 跳过 WSL / Hyper-V / VMware / VirtualBox 等虚拟网卡与链路本地地址，
        /// 否则本机双开时可能广播出虚拟网卡地址，成员连不上。
        /// </summary>
        private string LocalAddress()
        {
            string fallback = null;
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;

                var name = nic.Name ?? string.Empty;
                var desc = nic.Description ?? string.Empty;
                var isVirtual = name.IndexOf("WSL", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0
                                || desc.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;

                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("169.254."))
                        continue;

                    if (!isVirtual)
                        return ip;
                    if (fallback == null)
                        fallback = ip;
                }
            }

            return fallback ?? "127.0.0.1";
        }
    }
}
```

- [ ] **Step 4: 运行确认编译通过**

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/MultiplayerCoordinator.cs
git commit -m "feat(netcode): 新增联机生命周期协调器状态机"
```

---

### Task 7: 玩家生成器与战斗场景绑定

**Files:**
- Create: `Assets/Gameplay/Runtime/NetworkPlayerSpawner.cs`
- Create: `Assets/Gameplay/Runtime/BattleNetworkBinder.cs`
- Modify: `Assets/Gameplay/Runtime/PlayerNetworkSync.cs`

**Interfaces:**
- Consumes: Task 6 的 `MultiplayerCoordinator.Instance` / `AccountIdOf` / `NotifyBattleSceneReady`
- Produces:
  - `sealed class NetworkPlayerSpawner : MonoBehaviour`（Host 侧，挂 BattleScene）
  - `sealed class BattleNetworkBinder : MonoBehaviour`（挂 BattleScene）
  - `PlayerNetworkSync` 改为通过 `BattleNetworkBinder.Instance` 绑定相机

- [ ] **Step 1: 写失败测试**

无单元测试框架。先实现，再用双端运行日志验证。

- [ ] **Step 2: 确认当前无该类型**

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 3: 实现**

创建 `Assets/Gameplay/Runtime/NetworkPlayerSpawner.cs`：

```csharp
using Unity.Netcode;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// Host 侧玩家生成与出生点分配。
    ///
    /// 玩家对象由 NGO 的 NetworkConfig.PlayerPrefab 自动生成（每个连接一个 owner 玩家），
    /// 本组件只负责把它们摆到本层房间的出生点。
    /// 之所以不手动 Instantiate + SpawnAsPlayerObject：PlayerPrefab 已配置，
    /// 手动再生成会导致 Host 出现双玩家。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerSpawner : MonoBehaviour
    {
        [Tooltip("爬塔流程，用于取本层房间出生点。留空自动查找。")]
        [SerializeField] private TowerRunManager towerRun;

        private NetworkManager networkManager;

        private void Awake()
        {
            networkManager = FindObjectOfType<NetworkManager>();
            if (towerRun == null)
                towerRun = FindObjectOfType<TowerRunManager>();
        }

        private void OnEnable()
        {
            if (networkManager != null)
                networkManager.OnClientConnectedCallback += HandleClientConnected;
        }

        private void OnDisable()
        {
            if (networkManager != null)
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (networkManager == null || !networkManager.IsServer)
                return;

            StartCoroutine(PlacePlayerAfterSpawn(clientId));
        }

        /// <summary>
        /// 等玩家对象生成后摆到出生点。
        /// 自动生成的玩家初始位置是 prefab 原点，不会落在房间几何中心。
        /// </summary>
        private System.Collections.IEnumerator PlacePlayerAfterSpawn(ulong clientId)
        {
            GameObject born = null;
            for (var i = 0; i < 10; i++)
            {
                yield return new WaitForEndOfFrame();

                var netObj = networkManager.SpawnManager.GetPlayerNetworkObject(clientId);
                if (netObj != null)
                {
                    born = netObj.gameObject;
                    break;
                }
            }

            if (born == null)
            {
                Debug.LogWarning($"[NetworkPlayerSpawner] 客户端 {clientId} 的玩家对象未在 10 帧内生成。");
                yield break;
            }

            // 服务器端直接改位置；客户端由 PlayerNetworkSync 的 owner 上报覆盖，
            // 因此这里只需在 Host（服务器）上摆一次即可全局生效。
            var target = ResolveSpawnPosition();
            var body = born.GetComponent<Rigidbody2D>();
            if (body != null)
                body.position = target;

            born.transform.position = new Vector3(target.x, target.y, born.transform.position.z);

            Debug.Log($"[NetworkPlayerSpawner] 客户端 {clientId} 的玩家已摆到 {target}");
        }

        private Vector2 ResolveSpawnPosition()
        {
            if (towerRun != null && towerRun.HasRoomBounds)
                return towerRun.FloorCenter;

            return Vector2.zero;
        }
    }
}
```

创建 `Assets/Gameplay/Runtime/BattleNetworkBinder.cs`：

```csharp
using Tower.Network;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// 战斗场景内的联机绑定。
    ///
    /// 只做场景内的事：隐藏单机静态玩家、通知协调器场景就绪。
    /// 连接建立与场景切换都在常驻层完成，本组件不参与。
    /// 单机模式（非联机会话）直接返回，不隐藏静态玩家。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleNetworkBinder : MonoBehaviour
    {
        private static BattleNetworkBinder instance;

        /// <summary>全局单例。单机时为 null 或未激活。</summary>
        public static BattleNetworkBinder Instance => instance;

        [Tooltip("相机跟随。联机时指向本端控制的玩家。")]
        [SerializeField] private CameraFollow2D cameraFollow;

        private GameObject staticPlayer;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void Start()
        {
            // 单机：不隐藏静态玩家，不通知协调器。
            if (!IsOnlineSession)
            {
                Debug.Log("[BattleNetworkBinder] 非联机会话，跳过联机绑定。");
                return;
            }

            if (cameraFollow == null)
                cameraFollow = FindObjectOfType<CameraFollow2D>();

            HideStaticPlayer();

            // 通知协调器：战斗场景已就绪。
            if (MultiplayerCoordinator.Instance != null)
                MultiplayerCoordinator.Instance.NotifyBattleSceneReady();
        }

        /// <summary>是否处于联机会话（已登录且在房间内）。</summary>
        public static bool IsOnlineSession =>
            NetworkSession.Instance != null && NetworkSession.Instance.CurrentRoom != null;

        /// <summary>
        /// 把相机跟随目标重定向到某个网络玩家。由本端 owner 玩家的
        /// <see cref="PlayerNetworkSync"/> 在 OnNetworkSpawn 时调用。
        /// </summary>
        public void AttachLocalCamera(Transform player)
        {
            if (cameraFollow == null)
                cameraFollow = FindObjectOfType<CameraFollow2D>();

            if (cameraFollow != null && player != null)
                cameraFollow.SetTarget(player);
        }

        /// <summary>
        /// 隐藏场景静态玩家。联机模式下所有角色都由 Host 动态生成，
        /// 静态玩家若继续存在会与网络玩家并存，出现「不归网络支配的幽灵角色」。
        /// </summary>
        private void HideStaticPlayer()
        {
            if (staticPlayer != null)
                return;

            var gameplay = GameObject.Find("Gameplay");
            staticPlayer = gameplay != null
                ? gameplay.transform.Find("Character_Base")?.gameObject
                : null;

            if (staticPlayer != null)
                staticPlayer.SetActive(false);
        }
    }
}
```

修改 `Assets/Gameplay/Runtime/PlayerNetworkSync.cs` 的相机绑定行：

```csharp
                // 让相机跟着自己控制的角色走（联机时才有 Instance，单机为 null 无副作用）。
                if (Tower.Netcode.BattleNetworkBinder.Instance != null)
                    Tower.Netcode.BattleNetworkBinder.Instance.AttachLocalCamera(transform);
```

- [ ] **Step 4: 运行确认编译通过**

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 5: 提交**

```bash
git add Assets/Gameplay/Runtime/NetworkPlayerSpawner.cs Assets/Gameplay/Runtime/BattleNetworkBinder.cs Assets/Gameplay/Runtime/PlayerNetworkSync.cs
git commit -m "feat(netcode): 新增玩家生成器与战斗场景绑定"
```

---

## 阶段 4：接线与场景调整

### Task 8: 主菜单出发流程接入协调器

**Files:**
- Modify: `Assets/UI/MainMenu/Scripts/MainMenuRoomBinder.cs`
- Modify: `Assets/UI/MainMenu/Scripts/DepartFlow.cs`

**Interfaces:**
- Consumes: Task 6 的 `MultiplayerCoordinator`
- Produces:
  - `MainMenuRoomBinder.OnDepartRequested` 改为启动 `MultiplayerCoordinator.StartHostFlow()`
  - `MainMenuRoomBinder` 订阅 `GameStarting` 后启动 `StartClientFlow()`
  - `DepartFlow` 联机分支不再自行切场

- [ ] **Step 1: 写失败测试**

无单元测试框架。用双端运行验证。

- [ ] **Step 2: 确认当前行为**

Run: 阅读 `MainMenuRoomBinder.OnDepartRequested`，确认当前直接调用 `departFlow.Depart()`
Expected: 确认现状

- [ ] **Step 3: 实现**

修改 `Assets/UI/MainMenu/Scripts/MainMenuRoomBinder.cs`：

`OnDepartRequested` 改为：

```csharp
    /// <summary>房主点了「出发」：启动联机协调器，由它建立连接并统一切场。</summary>
    private void OnDepartRequested()
    {
        if (session == null || session.CurrentRoom == null)
            return;

        if (!IsLocalHost())
        {
            roomPanel?.ShowError("只有房主可以开始游戏");
            return;
        }

        if (MemberCount() <= 1)
        {
            roomPanel?.ShowError("至少需要 2 名玩家才能开始");
            return;
        }

        var coordinator = MultiplayerCoordinator.Instance;
        if (coordinator == null)
        {
            roomPanel?.ShowError("联机组件未就绪");
            return;
        }

        // 广播开始信号，成员端收到 GameStarting 后启动 Client。
        session.Signal?.SendStartGame();

        // 房主启动 Host 流程：创建会话 → 监听 → 等成员 → 切场景。
        StartCoroutine(coordinator.StartHostFlow());
    }
```

`OnGameStarting` 改为：

```csharp
    /// <summary>成员端收到房主的开始信号：启动 Client 流程，等待房主切场景。</summary>
    private void OnGameStarting(GameStartingPayload payload)
    {
        // 房主自己已通过 StartHostFlow 进入，这里只处理成员端。
        if (IsLocalHost())
            return;

        var coordinator = MultiplayerCoordinator.Instance;
        if (coordinator == null)
        {
            roomPanel?.ShowError("联机组件未就绪");
            return;
        }

        StartCoroutine(coordinator.StartClientFlow());
    }
```

在 `Awake` 中订阅协调器失败事件（放在 `roomPanel` 订阅之后）：

```csharp
        if (MultiplayerCoordinator.Instance != null)
            MultiplayerCoordinator.Instance.Failed += OnMultiplayerFailed;
```

在 `OnDestroy` 中退订：

```csharp
        if (MultiplayerCoordinator.Instance != null)
            MultiplayerCoordinator.Instance.Failed -= OnMultiplayerFailed;
```

新增失败处理：

```csharp
    /// <summary>联机失败：留在房间界面并显示原因，不进入战斗。</summary>
    private void OnMultiplayerFailed(string reason)
    {
        roomPanel?.ShowError(reason);
    }
```

在文件顶部补充 using：

```csharp
using Tower.Netcode;
```

修改 `Assets/UI/MainMenu/Scripts/DepartFlow.cs` 的 `Depart()`：

```csharp
    /// <summary>供 Btn_Depart 的 onClick 调用。</summary>
    public void Depart()
    {
        if (isLoading)
            return;

        // 联机模式：必须先建房或进房，否则只弹房间面板、不切场。
        // 联机切场由 MultiplayerCoordinator 在连接建立后统一发起，这里不自行加载场景。
        if (IsOnlineMode)
        {
            if (roomBinder != null && !roomBinder.EnsureReadyForDepart())
                return;

            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogError($"[DepartFlow] 场景「{targetSceneName}」不在 Build Settings 中，出发已取消。");
            return;
        }

        StartCoroutine(LoadTargetScene());
    }
```

- [ ] **Step 4: 运行确认编译通过**

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 5: 提交**

```bash
git add Assets/UI/MainMenu/Scripts/MainMenuRoomBinder.cs Assets/UI/MainMenu/Scripts/DepartFlow.cs
git commit -m "feat(ui): 主菜单出发流程接入联机协调器"
```

---

### Task 9: 场景调整与旧组件移除

**Files:**
- Modify: `Assets/Scenes/BattleScene.unity`
- Modify: `Assets/Scenes/MainMenu.unity`
- Delete: `Assets/Gameplay/Runtime/NetworkGameBootstrap.cs`

**Interfaces:**
- Consumes: Task 6、Task 7 的新组件
- Produces: 常驻 NetworkManager + MultiplayerCoordinator；BattleScene 只保留 BattleNetworkBinder + NetworkPlayerSpawner

- [ ] **Step 1: 确认当前场景结构**

Run: `unity_scene_hierarchy` 查看 BattleScene 根对象
Expected: 看到 `NetworkManager` 与 `NetworkGameBootstrap`

- [ ] **Step 2: 调整 BattleScene**

用 `unity_execute_code` 执行：

```csharp
// 1. 删除 BattleScene 中的 NetworkGameBootstrap 对象
var bootstrap = GameObject.Find("NetworkGameBootstrap");
if (bootstrap != null) Object.DestroyImmediate(bootstrap);

// 2. 删除 BattleScene 中的 NetworkManager（迁到常驻层）
var nm = GameObject.Find("NetworkManager");
if (nm != null) Object.DestroyImmediate(nm);

// 3. 在 BattleScene 新增 BattleNetworkBinder 与 NetworkPlayerSpawner
var binder = new GameObject("BattleNetworkBinder");
binder.AddComponent<Tower.Netcode.BattleNetworkBinder>();

var spawner = new GameObject("NetworkPlayerSpawner");
spawner.AddComponent<Tower.Netcode.NetworkPlayerSpawner>();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
```

- [ ] **Step 3: 调整 MainMenu 场景**

用 `unity_execute_code` 执行：

```csharp
// 在 MainMenu 场景新增常驻 NetworkManager + MultiplayerCoordinator。
// NetworkManager 的 NetworkConfig（PlayerPrefab / NetworkPrefabsLists / EnableSceneManagement）
// 需与 BattleScene 原配置一致，这里从原 prefab 资源复制。
var existing = GameObject.Find("NetworkManager");
if (existing == null)
{
    var nmGo = new GameObject("NetworkManager");
    var nm = nmGo.AddComponent<Unity.Netcode.NetworkManager>();
    var utp = nmGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();

    nm.NetworkConfig.NetworkTransport = utp;
    nm.NetworkConfig.EnableSceneManagement = true;
    nm.NetworkConfig.ConnectionApproval = true;

    // PlayerPrefab 与 NetworkPrefabsLists 需在 Inspector 中确认已指向 Character_Base。
    nmGo.AddComponent<Tower.Netcode.MultiplayerCoordinator>();
}

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
```

> 注意：`PlayerPrefab` 与 `NetworkPrefabsLists` 是对象引用，无法在代码里凭空构造，必须在 Inspector 中把 `Character_Base` prefab 与 `Character_NetworkPrefabsList` 拖到 MainMenu 的 NetworkManager 上。执行完脚本后手动确认这两项。

- [ ] **Step 4: 删除旧组件并确认编译**

删除 `Assets/Gameplay/Runtime/NetworkGameBootstrap.cs`，然后：

Run: Unity `Assets/Refresh` + `unity_get_compilation_errors`
Expected: 0 错误

- [ ] **Step 5: 提交**

```bash
git add Assets/Scenes/BattleScene.unity Assets/Scenes/MainMenu.unity
git rm Assets/Gameplay/Runtime/NetworkGameBootstrap.cs
git commit -m "refactor(scene): NetworkManager 常驻化，移除旧连接引导"
```

---

## 阶段 5：验证

### Task 10: 单机回归验证

**Files:** 无代码改动，纯验证

- [ ] **Step 1: 单机出发**

不登录，主菜单选单机，点出发。
Expected: 正常进入 BattleScene，角色可移动、可射击、可拾取。

- [ ] **Step 2: 单机战斗与死亡**

Expected: 敌人正常、伤害正常、死亡结算正常、返回主菜单正常。

- [ ] **Step 3: 确认无 NGO 连接**

检查 Console 无 `[MultiplayerCoordinator]` 状态日志。
Expected: 无联机日志，`MultiplayerCoordinator.State` 保持 `Idle`。

- [ ] **Step 4: 提交验证记录**

```bash
git commit --allow-empty -m "test: 单机模式回归验证通过"
```

---

### Task 11: 联机双端验证

**Files:** 无代码改动，纯验证

- [ ] **Step 1: 重新构建 NetClient**

Run: `unity_build` target=StandaloneWindows64, outputPath=Build/NetClient/NetClient.exe, developmentBuild=true
Expected: 构建成功

- [ ] **Step 2: 启动双端**

编辑器 Play（房主 `nettest_a`）+ NetClient（成员 `nettest_b`），双方进入同一房间。

- [ ] **Step 3: 房主点击出发**

Expected:
- 房主日志：`状态 → Preparing` → `Connecting` → `WaitingForPlayers` → `LoadingBattleScene` → `InGame`
- 成员日志：`状态 → Preparing` → `Connecting` → 已连接房主
- 成员**不会**自行加载 BattleScene

- [ ] **Step 4: 验证同一会话**

Expected:
- 两端进入同一个 BattleScene
- 房主能看到成员角色，成员能看到房主角色
- 成员角色可移动、可被房主看到移动
- 相机各自跟随自己的角色

- [ ] **Step 5: 验证失败处理**

让成员在房主出发前退出房间。
Expected: 房主等待超时后进入 `Failed`，显示原因，不进入战斗。

- [ ] **Step 6: 提交验证记录**

```bash
git commit --allow-empty -m "test: 联机双端验证通过"
```

---

## 自检

**Spec 覆盖检查：**

| 设计文档章节 | 对应任务 |
|--------------|----------|
| 3. 状态机 | Task 6 |
| 4. 出发流程 | Task 6、Task 8 |
| 5. 服务端改动 | Task 1、Task 2、Task 3 |
| 6.1 NetworkManager 常驻 | Task 9 |
| 6.2 MultiplayerCoordinator | Task 6 |
| 6.3 BattleNetworkBinder | Task 7 |
| 6.4 NetworkPlayerSpawner | Task 7 |
| 6.5 传输策略接口 | Task 5 |
| 6.6 删除旧逻辑 | Task 9 |
| 7. 单机保障 | Task 10 |
| 9. 验收标准 | Task 10、Task 11 |

**类型一致性检查：**

- `MultiplayerCoordinator.Instance` 在 Task 6 定义，Task 7、Task 8 使用 ✓
- `BattleNetworkBinder.Instance` 在 Task 7 定义，Task 7 修改 `PlayerNetworkSync` 使用 ✓
- `NetworkSession.CurrentSession` / `SessionChanged` 在 Task 4 定义，Task 6 使用 ✓
- `GameSessionDto` 在 Task 4 定义，Task 6 使用 ✓
- `IConnectionStrategy` / `ConnectionStrategies.For` 在 Task 5 定义，Task 6 使用 ✓
- `Service.SessionInfo` 在 Task 3 定义，`RoomLookup` 接口在 Task 3 扩展 ✓
