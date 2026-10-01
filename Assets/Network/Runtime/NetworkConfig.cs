using System;

namespace Tower.Network
{
    /// <summary>
    /// 联机服务端配置。
    ///
    /// 单独抽出来而非散落各处：本地开发、局域网测试、公网部署三种场景
    /// 只改这一处，不必翻遍代码找硬编码的地址。
    /// </summary>
    [Serializable]
    public sealed class NetworkConfig
    {
        /// <summary>服务端 HTTP 基地址（不含 /api/v1）。</summary>
        public string baseUrl = "http://localhost:8080";

        /// <summary>API 版本前缀。</summary>
        public string apiPrefix = "/api/v1";

        /// <summary>HTTP 请求超时（秒）。</summary>
        public int httpTimeoutSeconds = 15;

        /// <summary>WebSocket 连接超时（秒）。</summary>
        public int socketConnectTimeoutSeconds = 10;

        /// <summary>客户端主动 ping 间隔（秒）。服务端读超时 60 秒，20 秒留足余量。</summary>
        public int pingIntervalSeconds = 20;

        /// <summary>断线后自动重连的最大尝试次数。0 表示不重连。</summary>
        public int maxReconnectAttempts = 3;

        /// <summary>重连基础间隔（秒），实际间隔按尝试次数递增。</summary>
        public float reconnectBaseDelaySeconds = 1f;

        /// <summary>API 基地址，如 http://localhost:8080/api/v1。</summary>
        public string ApiBase => baseUrl.TrimEnd('/') + apiPrefix;

        /// <summary>WebSocket 信令地址，如 ws://localhost:8080/api/v1/signal。</summary>
        public string SignalUrl
        {
            get
            {
                var trimmed = baseUrl.TrimEnd('/');

                // http → ws，https → wss。用 https 时若仍是 ws:// 会被系统拒绝。
                if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    trimmed = "wss://" + trimmed.Substring("https://".Length);
                else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    trimmed = "ws://" + trimmed.Substring("http://".Length);

                return trimmed + apiPrefix + "/signal";
            }
        }

        /// <summary>本地开发默认配置。</summary>
        public static NetworkConfig Local => new NetworkConfig();

        /// <summary>按主机地址构造配置，便于局域网测试。</summary>
        public static NetworkConfig ForHost(string host, int port = 8080)
        {
            return new NetworkConfig { baseUrl = $"http://{host}:{port}" };
        }
    }
}
