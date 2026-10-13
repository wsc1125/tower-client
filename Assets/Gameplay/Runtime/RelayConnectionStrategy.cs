using Unity.Netcode;

namespace Tower.Netcode
{
    /// <summary>
    /// 公网中继策略：NGO 流量经自建服务端的 WebSocket 中继转发。
    ///
    /// 房主与成员都只「出站连接」到中继，无监听、无打洞 ——
    /// 这绕开了 NGO 房主被动监听无法穿透 NAT 的根本限制。
    /// 连接目标在构造时给定（房间码 + 令牌），ConfigureClient 的地址参数被忽略。
    /// </summary>
    public sealed class RelayConnectionStrategy : IConnectionStrategy
    {
        private readonly string relayUrl;

        public RelayConnectionStrategy(string relayUrl)
        {
            this.relayUrl = relayUrl;
        }

        public string Mode => "relay";

        public bool ConfigureHost(NetworkManager networkManager, int port)
        {
            if (string.IsNullOrEmpty(relayUrl))
            {
                UnityEngine.Debug.LogError("[RelayConnectionStrategy] 中继地址为空。");
                return false;
            }

            var transport = networkManager.GetComponent<WebSocketRelayTransport>();
            if (transport == null)
                transport = networkManager.gameObject.AddComponent<WebSocketRelayTransport>();

            transport.Configure(relayUrl);

            // NGO 从 NetworkConfig.NetworkTransport 取传输组件 —— 必须从 UTP 切到中继传输。
            networkManager.NetworkConfig.NetworkTransport = transport;

            return true;
        }

        public bool ConfigureClient(NetworkManager networkManager, string address, int port)
        {
            // 中继模式下不需要房主地址：连接目标（房间码 + 令牌）已在构造时给出。
            return ConfigureHost(networkManager, port);
        }
    }
}
