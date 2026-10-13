using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Tower.Network
{
    /// <summary>
    /// 信令 WebSocket 客户端。
    ///
    /// 线程模型（关键）：
    ///   - <see cref="ClientWebSocket"/> 的收发是异步的，回调发生在**线程池线程**上。
    ///   - Unity 的 API 只能在主线程调用，因此收到的消息先入队，
    ///     由主线程的 <see cref="Pump"/> 取出并派发事件。
    ///   - 发送同理：主线程调用 Send 时入队，后台发送循环负责实际写出。
    ///
    /// 这样调用方（UI / 游戏逻辑）始终在主线程收到回调，不必自己 Marshal。
    /// </summary>
    public sealed class SignalClient
    {
        private readonly NetworkConfig config;

        private ClientWebSocket socket;
        private CancellationTokenSource cts;

        /// <summary>收到的原始消息队列，由后台接收循环写入、主线程 Pump 读取。</summary>
        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();

        /// <summary>待发送的消息队列，由主线程写入、后台发送循环读取。</summary>
        private readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();

        private Task receiveTask;
        private Task sendTask;

        /// <summary>当前房间码。</summary>
        private string roomCode;

        /// <summary>当前 access token。</summary>
        private string token;

        /// <summary>是否已连接。</summary>
        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        /// <summary>当前房间码。</summary>
        public string RoomCode => roomCode;

        /// <summary>
        /// 最近一次连接失败的描述。连接成功或重新发起连接时清空。
        ///
        /// 握手被拒（如 403「你不在该房间内」）时 ClientWebSocket 会抛异常，
        /// Connect 据此把真实原因记录在这里，供调用方上报 ——
        /// 否则调用方只能等 room_state 超时，看不到服务端给的具体原因。
        /// </summary>
        public string ConnectError { get; private set; }

        // ---- 事件（全部在主线程触发）----

        /// <summary>连接建立。</summary>
        public event Action Connected;

        /// <summary>连接关闭。参数为关闭原因描述。</summary>
        public event Action<string> Disconnected;

        /// <summary>收到房间状态（加入成功 / 准备状态变化）。</summary>
        public event Action<RoomStatePayload> RoomStateReceived;

        /// <summary>有玩家加入。</summary>
        public event Action<MemberJoinedPayload> MemberJoined;

        /// <summary>有玩家离开。</summary>
        public event Action<MemberLeftPayload> MemberLeft;

        /// <summary>收到转发的信令数据。</summary>
        public event Action<SignalForwardPayload> SignalReceived;

        /// <summary>房主发起开始游戏。</summary>
        public event Action<GameStartingPayload> GameStarting;

        /// <summary>房主迁移。客户端据此执行权威接管。</summary>
        public event Action<HostChangedPayload> HostChanged;

        /// <summary>房间关闭。</summary>
        public event Action<RoomClosedPayload> RoomClosed;

        /// <summary>服务端返回错误。</summary>
        public event Action<SignalErrorPayload> ErrorReceived;

        public SignalClient(NetworkConfig config)
        {
            this.config = config ?? NetworkConfig.Local;
        }

        /// <summary>
        /// 连接信令服务器。连接建立后**立即返回**，不负责后续消息泵送。
        ///
        /// 历史教训：本方法曾在尾部死循环泵消息直到断开 —— 调用方若
        /// `yield return Connect(...)` 会挂起到断线，断线后恢复时还会把
        /// 旧房间状态复活成 CurrentRoom（幽灵房间）。泵送改由
        /// <see cref="NetworkSession"/> 每帧调用 <see cref="Pump"/> 驱动。
        /// </summary>
        public IEnumerator Connect(string roomCode, string accessToken)
        {
            if (IsConnected)
                yield return Disconnect();

            ConnectError = null;
            this.roomCode = roomCode;
            token = accessToken;

            socket = new ClientWebSocket();
            cts = new CancellationTokenSource();

            var url = $"{config.SignalUrl}?room_code={Uri.EscapeDataString(roomCode)}&token={Uri.EscapeDataString(accessToken)}";

            var connectTask = socket.ConnectAsync(new Uri(url), cts.Token);

            // 带超时地等待连接完成，避免服务端不可达时永久卡住。
            var deadline = Time.realtimeSinceStartup + config.socketConnectTimeoutSeconds;
            while (!connectTask.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    AbortSocket();
                    ConnectError = "连接超时";
                    yield break;
                }

                yield return null;
            }

            if (connectTask.IsFaulted || socket.State != WebSocketState.Open)
            {
                var reason = connectTask.Exception?.GetBaseException().Message ?? "连接失败";
                AbortSocket();
                ConnectError = reason;
                yield break;
            }

            receiveTask = Task.Run(() => ReceiveLoop(cts.Token));
            sendTask = Task.Run(() => SendLoop(cts.Token));

            Connected?.Invoke();

            // 连接后立刻发一条 join_room，让服务端下发房间状态。
            // 服务端在握手时已按 query 参数登记，这里只是显式触发状态下发。
            // 首条 room_state 会由 NetworkSession 的 Update → Pump 派发。
            SendJoinRoom(roomCode);
        }

        /// <summary>断开连接。</summary>
        public IEnumerator Disconnect()
        {
            if (socket == null)
                yield break;

            // 注意：C# 迭代器不允许在带 catch 的 try 块里 yield。
            // 因此这里把「发起关闭」与「等待关闭」拆开：
            // try 块只负责发起（无 yield），等待循环放在 try 之外。
            Task closeTask = null;

            try
            {
                if (socket.State == WebSocketState.Open)
                    closeTask = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
            }
            catch
            {
                // 发起关闭失败无所谓，下面直接 Abort。
            }

            if (closeTask != null)
            {
                var deadline = Time.realtimeSinceStartup + 3f;
                while (!closeTask.IsCompleted && Time.realtimeSinceStartup < deadline)
                    yield return null;
            }

            AbortSocket();
        }

        // ---- 发送 ----

        /// <summary>发送 join_room。</summary>
        public void SendJoinRoom(string code)
        {
            Enqueue(SignalType.JoinRoom, new JoinRoomPayload { room_code = code });
        }

        /// <summary>向指定玩家转发信令数据。</summary>
        public void SendSignal(long targetAccountId, string data)
        {
            Enqueue(SignalType.Signal, new SignalPayload
            {
                target_account_id = targetAccountId,
                data = data,
            });
        }

        /// <summary>标记准备状态。</summary>
        public void SendReady(bool ready)
        {
            Enqueue(SignalType.Ready, new ReadyPayload { ready = ready });
        }

        /// <summary>房主发起开始游戏。</summary>
        public void SendStartGame()
        {
            Enqueue(SignalType.StartGame, null);
        }

        /// <summary>保活。</summary>
        public void SendPing()
        {
            Enqueue(SignalType.Ping, null);
        }

        /// <summary>把消息序列化后入队。</summary>
        private void Enqueue(string type, object payload)
        {
            if (!IsConnected)
                return;

            // 用 Codec 而非 JsonUtility 序列化信封：
            // payload 需要作为**嵌套对象**输出，而 JsonUtility 会把 string 字段
            // 序列化成带引号的字符串（"payload":"{...}"），服务端解析不了。
            var payloadJson = payload == null ? null : JsonUtility.ToJson(payload);
            outbox.Enqueue(SignalEnvelopeCodec.Encode(type, 0, payloadJson));
        }

        // ---- 后台循环 ----

        /// <summary>接收循环（线程池线程）。</summary>
        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();

            try
            {
                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    builder.Clear();

                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            inbox.Enqueue(null); // null 作为「连接已关闭」的哨兵
                            return;
                        }

                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    inbox.Enqueue(builder.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略。
            }
            catch (Exception ex)
            {
                inbox.Enqueue(null);
                Debug.LogWarning($"[SignalClient] 接收循环异常: {ex.Message}");
            }
        }

        /// <summary>发送循环（线程池线程）。</summary>
        private async Task SendLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    if (!outbox.TryDequeue(out var message))
                    {
                        await Task.Delay(10, ct);
                        continue;
                    }

                    var bytes = Encoding.UTF8.GetBytes(message);
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略。
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SignalClient] 发送循环异常: {ex.Message}");
            }
        }

        // ---- 主线程派发 ----

        /// <summary>
        /// 把队列里的消息取出并派发事件。必须在主线程调用。
        ///
        /// 由 <see cref="NetworkSession"/> 每帧驱动（挂在 DontDestroyOnLoad 的
        /// 常驻对象上）—— 不能挂在「发起连接的场景对象」上，否则出发进战斗后
        /// 发起方随场景销毁，泵停转，战斗中将收不到任何信令（含 room_closed）。
        /// </summary>
        public void Pump()
        {
            while (inbox.TryDequeue(out var raw))
            {
                if (raw == null)
                {
                    // 连接已关闭的哨兵（服务端主动断开 / 接收循环异常）。
                    AbortSocket();
                    Disconnected?.Invoke("连接已关闭");
                    return;
                }

                Dispatch(raw);
            }
        }

        /// <summary>解析并派发单条消息。</summary>
        private void Dispatch(string raw)
        {
            // 用 Codec 而非 JsonUtility 解析信封：
            // payload 在线上是嵌套对象，JsonUtility 解进 string 字段会得到空串。
            var envelope = SignalEnvelopeCodec.Decode(raw);

            if (envelope == null)
            {
                Debug.LogWarning($"[SignalClient] 消息解析失败，原文: {raw}");
                return;
            }

            switch (envelope.type)
            {
                case SignalType.RoomState:
                    RoomStateReceived?.Invoke(Parse<RoomStatePayload>(envelope.payload));
                    break;

                case SignalType.MemberJoined:
                    MemberJoined?.Invoke(Parse<MemberJoinedPayload>(envelope.payload));
                    break;

                case SignalType.MemberLeft:
                    MemberLeft?.Invoke(Parse<MemberLeftPayload>(envelope.payload));
                    break;

                case SignalType.Signal:
                    SignalReceived?.Invoke(Parse<SignalForwardPayload>(envelope.payload));
                    break;

                case SignalType.GameStarting:
                    GameStarting?.Invoke(Parse<GameStartingPayload>(envelope.payload));
                    break;

                case SignalType.HostChanged:
                    HostChanged?.Invoke(Parse<HostChangedPayload>(envelope.payload));
                    break;

                case SignalType.RoomClosed:
                    RoomClosed?.Invoke(Parse<RoomClosedPayload>(envelope.payload));
                    break;

                case SignalType.Error:
                    ErrorReceived?.Invoke(Parse<SignalErrorPayload>(envelope.payload));
                    break;

                case SignalType.Pong:
                    // 保活响应，无需处理。
                    break;

                default:
                    Debug.LogWarning($"[SignalClient] 未知消息类型: {envelope.type}");
                    break;
            }
        }

        /// <summary>解析 payload，失败时返回 null 而非抛异常。</summary>
        private static T Parse<T>(string payload) where T : class
        {
            if (string.IsNullOrEmpty(payload))
                return null;

            try
            {
                return JsonUtility.FromJson<T>(payload);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SignalClient] payload 解析失败 ({typeof(T).Name}): {ex.Message}");
                return null;
            }
        }

        /// <summary>强制关闭 socket 并清理后台任务。</summary>
        private void AbortSocket()
        {
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

            cts?.Dispose();
            cts = null;

            receiveTask = null;
            sendTask = null;
        }
    }
}
