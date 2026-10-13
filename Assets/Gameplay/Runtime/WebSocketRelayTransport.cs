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
                        if (isServer && msg.client_id > 0 && connectedPeers.Add((ulong)msg.client_id))
                            inbox.Enqueue(new TransportEvent { SessionId = activeSession, Type = NetworkEvent.Connect, ClientId = (ulong)msg.client_id });
                        break;

                    case "peer_left":
                        if (isServer && msg.client_id > 0 && connectedPeers.Remove((ulong)msg.client_id))
                            inbox.Enqueue(new TransportEvent { SessionId = activeSession, Type = NetworkEvent.Disconnect, ClientId = (ulong)msg.client_id });
                        break;
                }
            }
        }

        /// <summary>中继连接断开：房主侧报传输失败，成员侧报与房主断开。</summary>
        private void OnSocketClosed(int activeSession)
        {
            lock (sessionLock)
            {
                if (activeSession != sessionId || socketClosed)
                    return;
                socketClosed = true;
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
