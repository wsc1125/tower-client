using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// WebSocket 中继传输：所有 NGO 流量经自建服务端的 WebSocket 中继转发。
    ///
    /// 为什么需要它：NGO 房主是网络拓扑中心且通常在 NAT 后 —— UDP 直连要求
    /// 打洞（成功率不稳、Symmetric NAT 直接失败），而 NGO 的 UTP 不支持自建
    /// STUN/TURN（只支持 Unity 云 Relay）。本传输用纯出站 TCP（WebSocket）
    /// 连接服务端中继，天然穿越一切 NAT/防火墙，连通率 100%。
    ///
    /// 线路协议（与服务端 relayws 模块对应）：
    ///   - 二进制帧 = 战斗数据：[0x01][8 字节大端 ID][payload]。
    ///     成员发的 ID 恒为 ServerClientId(0)，服务端改写为发送者账号 ID 转给房主；
    ///     房主发的 ID 为目标账号 ID；服务端下发给成员的帧 ID 恒为 0。
    ///   - 文本帧 = 控制消息：welcome（分配 ID）/ peer_joined（成员连接）/ peer_left（成员断线）。
    ///
    /// 事件模型与 UnityTransport 相同：主线程 Update 泵队列 →
    /// <see cref="NetworkTransport.InvokeOnTransportEvent"/>，
    /// <see cref="PollEvent"/> 恒返回 Nothing（NGO 1.5 两种机制二选一，不可双发）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WebSocketRelayTransport : NetworkTransport
    {
        [Tooltip("中继 WebSocket 完整地址（含 room_code 与 token 查询参数）。由 RelayConnectionStrategy 注入。")]
        [SerializeField] private string relayUrl;

        [Tooltip("连接中继的超时（秒）。期间阻塞主线程（出发加载页已展示）。")]
        [SerializeField] private int connectTimeoutSeconds = 5;

        /// <summary>战斗数据帧的标记字节，与服务端 dataMessageType 对应。</summary>
        private const byte DataMessageType = 0x01;

        /// <summary>帧头大小：1 字节类型 + 8 字节大端 ID。</summary>
        private const int HeaderSize = 9;

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private bool isServer;
        private readonly object sessionLock = new object();
        private readonly HashSet<ulong> connectedPeers = new HashSet<ulong>();
        private int sessionId;
        private bool welcomed;
        private bool socketClosed;

        /// <summary>最大重连尝试次数。耗尽后才向 NGO 投递失败事件。</summary>
        public const int MaxReconnectAttempts = 5;

        /// <summary>退避上限（秒）。避免退避时间无限增长。</summary>
        private const float MaxBackoffSeconds = 8f;

        /// <summary>
        /// 房主侧 peer_left 宽限（秒）。成员断开中继后，房主等这么久才向 NGO 投递 Disconnect，
        /// 期间收到该成员的 resume（传输层重连成功）则静默恢复，NGO 全程无感知。
        /// 取值须覆盖成员端传输层重连总耗时上限（5 次尝试 × 连接超时 + 15 秒退避 ≈ 40 秒）。
        /// </summary>
        public const float PeerLeftGraceSeconds = 45f;

        /// <summary>重连状态机：目标会话 ID。</summary>
        private int reconnectSession;

        /// <summary>重连状态机：已尝试次数。</summary>
        private int reconnectAttempts;

        /// <summary>重连状态机：下次尝试的时间点（realtime）。</summary>
        private float reconnectDeadline;

        /// <summary>已尝试的重连次数。0 表示当前连接正常。</summary>
        public int ReconnectAttempts => reconnectAttempts;

        /// <summary>是否正在重连中（socket 已断、尚未放弃）。</summary>
        public bool IsReconnecting { get; private set; }

        /// <summary>
        /// 房主侧宽限中的成员（账号 ID → 宽限截止时刻，realtime）。
        /// peer_left 到达后不立即投递 Disconnect —— 成员可能正在传输层重连，
        /// 收到 resume / peer_joined 即撤销；宽限耗尽才投递 Disconnect。
        /// </summary>
        private readonly Dictionary<ulong, float> pendingLefts = new Dictionary<ulong, float>();

        private readonly ConcurrentQueue<TransportEvent> inbox = new ConcurrentQueue<TransportEvent>();
        private readonly ConcurrentQueue<byte[]> outboxBinary = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<string> outboxControl = new ConcurrentQueue<string>();

        private struct TransportEvent
        {
            public int SessionId;
            public NetworkEvent Type;
            public ulong ClientId;
            public byte[] Payload;
        }

        /// <summary>控制消息（welcome / peer_joined / peer_left）。</summary>
        [Serializable]
        private class ControlMessage
        {
            public string type;
            public long client_id;
        }

        public override ulong ServerClientId => 0;

        private bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        /// <summary>注入中继地址。必须在 StartServer / StartClient 之前调用。</summary>
        public void Configure(string url)
        {
            relayUrl = url;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            // 本传输不依赖 NetworkManager 引用，保留空实现满足接口。
        }

        public override bool StartServer()
        {
            isServer = true;
            return ConnectAndStart();
        }

        public override bool StartClient()
        {
            isServer = false;
            return ConnectAndStart();
        }

        /// <summary>连接中继并启动收发循环。阻塞至连接建立或超时。</summary>
        private bool ConnectAndStart()
        {
            Cleanup();

            if (string.IsNullOrEmpty(relayUrl))
            {
                Debug.LogError("[RelayTransport] 未配置中继地址。");
                return false;
            }

            socket = new ClientWebSocket();
            cts = new CancellationTokenSource();

            var connectTask = socket.ConnectAsync(new Uri(relayUrl), cts.Token);
            var deadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
            while (!connectTask.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError("[RelayTransport] 连接中继超时。");
                    Cleanup();
                    return false;
                }

                // StartServer/StartClient 的契约是同步返回 bool，
                // 只能阻塞等待；此时 UI 已切到出发加载页，短暂冻结可接受。
                Thread.Sleep(16);
            }

            if (connectTask.IsFaulted || socket.State != WebSocketState.Open)
            {
                var reason = connectTask.Exception?.GetBaseException().Message ?? "连接失败";
                Debug.LogError($"[RelayTransport] 连接中继失败：{reason}");
                Cleanup();
                return false;
            }

            var activeSocket = socket;
            var token = cts.Token;
            var activeSession = sessionId;
            Task.Run(() => ReceiveLoop(activeSocket, token, activeSession));
            Task.Run(() => SendLoop(activeSocket, token, activeSession));

            Debug.Log($"[RelayTransport] 已连接中继（isServer={isServer}）");
            return true;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            // TCP 全可靠：所有 NetworkDelivery 都按普通帧发送，无需区分 QoS。
            if (!IsConnected || payload.Count <= 0)
                return;

            // 成员只与房主通信（NGO 星型拓扑），目标恒为 ServerClientId；
            // 房主发给本地客户端的回环由 NGO 内部处理，不走传输层。
            var target = isServer ? clientId : ServerClientId;
            if (isServer && target == ServerClientId)
                return;

            var frame = new byte[HeaderSize + payload.Count];
            frame[0] = DataMessageType;
            WriteBE64(frame, 1, target);
            Buffer.BlockCopy(payload.Array, payload.Offset, frame, HeaderSize, payload.Count);

            outboxBinary.Enqueue(frame);
        }

        /// <summary>
        /// NGO 1.5 对事件驱动传输的约定（同 UnityTransport）：恒返回 Nothing，
        /// 事件经 Update 泵入 <see cref="NetworkTransport.OnTransportEvent"/>。
        /// </summary>
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = default;
            payload = default;
            receiveTime = default;
            return NetworkEvent.Nothing;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            // 仅房主可踢人：通知中继关闭该成员连接。
            if (!isServer || !IsConnected)
                return;

            outboxControl.Enqueue(JsonUtility.ToJson(new ControlMessage
            {
                type = "kick",
                client_id = (long)clientId,
            }));
        }

        public override void DisconnectLocalClient()
        {
            // 成员主动离开：直接断开，服务端会通知房主（peer_left）。
            Cleanup();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            // NGO 的时间同步用自己的 TimeSync 消息测量 RTT，不依赖本值。
            return 0;
        }

        public override void Shutdown()
        {
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        /// <summary>主线程泵：把后台线程收到的事件派发给 NGO。</summary>
        private void Update()
        {
            // 重连状态机优先：断线期间没有事件可泵，先尝试恢复连接。
            StepReconnect();
            StepPendingLefts();

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

        // ---- 后台循环 ----

        private async Task ReceiveLoop(ClientWebSocket activeSocket, CancellationToken ct, int activeSession)
        {
            var buffer = new byte[64 * 1024];

            try
            {
                using var builder = new MemoryStream();

                while (!ct.IsCancellationRequested && activeSocket.State == WebSocketState.Open)
                {
                    builder.SetLength(0);
                    WebSocketMessageType messageType = WebSocketMessageType.Close;

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await activeSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            OnSocketClosed(activeSession);
                            return;
                        }

                        builder.Write(buffer, 0, result.Count);
                        messageType = result.MessageType;
                    }
                    while (!result.EndOfMessage);

                    var data = builder.ToArray();
                    if (messageType == WebSocketMessageType.Text)
                        HandleControl(Encoding.UTF8.GetString(data), activeSession);
                    else
                        HandleData(data, activeSession);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略。
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[RelayTransport] 接收循环异常：{ex.Message}");
                    OnSocketClosed(activeSession);
                }
            }
        }

        private async Task SendLoop(ClientWebSocket activeSocket, CancellationToken ct, int activeSession)
        {
            try
            {
                while (!ct.IsCancellationRequested && activeSocket.State == WebSocketState.Open)
                {
                    byte[] binary;
                    string text = null;
                    bool hasMessage;
                    lock (sessionLock)
                    {
                        if (activeSession != sessionId || socketClosed)
                            return;
                        hasMessage = outboxBinary.TryDequeue(out binary) || outboxControl.TryDequeue(out text);
                    }

                    if (!hasMessage)
                    {
                        await Task.Delay(5, ct);
                        continue;
                    }

                    if (binary != null)
                    {
                        await activeSocket.SendAsync(new ArraySegment<byte>(binary), WebSocketMessageType.Binary, true, ct);
                    }
                    else
                    {
                        var bytes = Encoding.UTF8.GetBytes(text);
                        await activeSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略。
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning($"[RelayTransport] 发送循环异常：{ex.Message}");
                    OnSocketClosed(activeSession);
                }
            }
        }

        // ---- 消息处理 ----

        /// <summary>处理战斗数据帧：剥掉帧头，按本端角色还原 NGO clientId。</summary>
        private void HandleData(byte[] data, int activeSession)
        {
            if (data.Length < HeaderSize || data[0] != DataMessageType)
                return;

            var payload = new byte[data.Length - HeaderSize];
            if (payload.Length > 0)
                Buffer.BlockCopy(data, HeaderSize, payload, 0, payload.Length);

            // 房主收到的帧 ID 是发送者账号 ID（服务端已改写）；
            // 成员收到的帧 ID 恒为 0，统一映射为 ServerClientId。
            lock (sessionLock)
            {
                if (activeSession != sessionId || socketClosed)
                    return;
                var clientId = isServer ? ReadBE64(data, 1) : ServerClientId;
                if (isServer ? !connectedPeers.Contains(clientId) : !welcomed)
                    return;
                inbox.Enqueue(new TransportEvent { SessionId = activeSession, Type = NetworkEvent.Data, ClientId = clientId, Payload = payload });
            }
        }

        private void HandleControl(string json, int activeSession)
        {
            ControlMessage msg;
            try
            {
                msg = JsonUtility.FromJson<ControlMessage>(json);
            }
            catch
            {
                return;
            }

            if (msg == null || string.IsNullOrEmpty(msg.type))
                return;

            lock (sessionLock)
            {
                if (activeSession != sessionId || socketClosed)
                    return;

                switch (msg.type)
                {
                    case "welcome":
                        // 成员收到后视为「已连上房主」，NGO 开始握手。
                        // 房主无需自连事件（本地客户端由 NGO 内部管理）。
                        if (!isServer && !welcomed)
                        {
                            welcomed = true;
                            inbox.Enqueue(new TransportEvent { SessionId = activeSession, Type = NetworkEvent.Connect, ClientId = ServerClientId });
                        }
                        break;

                    case "peer_joined":
                        if (isServer && msg.client_id > 0)
                        {
                            var joinedId = (ulong)msg.client_id;

                            // 宽限中的成员重新加入（resume 丢失时的兜底）：静默恢复，
                            // 不投递 Connect —— NGO 认为该成员从未断开。
                            if (pendingLefts.Remove(joinedId))
                            {
                                connectedPeers.Add(joinedId);
                                break;
                            }

                            if (connectedPeers.Add(joinedId))
                                inbox.Enqueue(new TransportEvent { SessionId = activeSession, Type = NetworkEvent.Connect, ClientId = joinedId });
                        }
                        break;

                    case "peer_left":
                        if (isServer && msg.client_id > 0 && connectedPeers.Remove((ulong)msg.client_id))
                        {
                            // 不立即投递 Disconnect：成员可能正在传输层重连（瞬断）。
                            // 进入宽限，收到 resume / peer_joined 即静默恢复；
                            // 宽限耗尽（StepPendingLefts）才投递 Disconnect，走既有移除语义。
                            pendingLefts[(ulong)msg.client_id] = Time.realtimeSinceStartup + PeerLeftGraceSeconds;
                        }
                        break;

                    case "resume":
                        // 成员传输层重连成功：撤销宽限并恢复收发门卫，不投递任何事件。
                        if (isServer && msg.client_id > 0)
                        {
                            var resumedId = (ulong)msg.client_id;
                            if (pendingLefts.Remove(resumedId))
                                connectedPeers.Add(resumedId);
                        }
                        break;
                }
            }
        }

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

        /// <summary>
        /// 房主侧宽限扫描（由 <see cref="Update"/> 每帧驱动）：
        /// 宽限耗尽的成员投递 Disconnect（走既有移除语义）。
        /// </summary>
        private void StepPendingLefts()
        {
            if (pendingLefts.Count == 0)
                return;

            List<ulong> expired = null;
            var now = Time.realtimeSinceStartup;
            foreach (var pair in pendingLefts)
            {
                if (now >= pair.Value)
                    (expired ??= new List<ulong>()).Add(pair.Key);
            }

            if (expired == null)
                return;

            lock (sessionLock)
            {
                foreach (var id in expired)
                {
                    if (!pendingLefts.Remove(id))
                        continue;

                    Debug.LogWarning($"[RelayTransport] 成员 {id} 断开超过 {PeerLeftGraceSeconds} 秒未重连，向 NGO 报告断开。");
                    inbox.Enqueue(new TransportEvent { SessionId = sessionId, Type = NetworkEvent.Disconnect, ClientId = id });
                }
            }
        }

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
            CleanupSocketOnly();

            if (string.IsNullOrEmpty(relayUrl))
                return false;

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

            // 成员端重连成功：先发 resume，让服务端通知房主撤销 peer_left 宽限。
            // 必须在启动收发循环前入队 —— 保证 resume 先于补发的战斗数据到达房主。
            // 房主侧无需 resume：房主重连耗尽走 TransportFailure 解散，无「静默恢复」语义。
            if (!isServer)
                outboxControl.Enqueue(JsonUtility.ToJson(new ControlMessage { type = "resume", client_id = 0 }));

            Task.Run(() => ReceiveLoop(activeSocket, token, activeSession));
            Task.Run(() => SendLoop(activeSocket, token, activeSession));

            lock (sessionLock)
            {
                if (activeSession != sessionId)
                    return false;

                // welcomed 保持 true：重连属于同一 NGO 会话，服务端重发的 welcome
                // 会被 HandleControl 的 !welcomed 门卫挡住 —— 否则会向 NGO 投递第二个
                // Connect 事件，触发重复握手。保持 true 也让数据帧门卫立即放行。
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

        // ---- 工具 ----

        private void Cleanup()
        {
            lock (sessionLock)
            {
                sessionId++;
                connectedPeers.Clear();
                welcomed = false;
                socketClosed = false;
                IsReconnecting = false;
                reconnectAttempts = 0;
                reconnectSession = 0;
                reconnectDeadline = 0f;
                pendingLefts.Clear();
                while (inbox.TryDequeue(out _)) { }
                while (outboxBinary.TryDequeue(out _)) { }
                while (outboxControl.TryDequeue(out _)) { }
            }

            try
            {
                cts?.Cancel();
            }
            catch
            {
                // 已释放，忽略。
            }

            try
            {
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

        private static void WriteBE64(byte[] buffer, int offset, ulong value)
        {
            for (var i = 7; i >= 0; i--)
            {
                buffer[offset + i] = (byte)(value & 0xFF);
                value >>= 8;
            }
        }

        private static ulong ReadBE64(byte[] buffer, int offset)
        {
            ulong value = 0;
            for (var i = 0; i < 8; i++)
                value = (value << 8) | buffer[offset + i];

            return value;
        }
    }
}
