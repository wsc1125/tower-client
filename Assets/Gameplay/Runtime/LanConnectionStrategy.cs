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
            var transport = ResolveUnityTransport(networkManager);
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
            var transport = ResolveUnityTransport(networkManager);
            if (transport == null)
            {
                Debug.LogError("[LanConnectionStrategy] NetworkManager 上未找到 UnityTransport。");
                return false;
            }

            transport.SetConnectionData(address, (ushort)port, "0.0.0.0");

            return true;
        }

        /// <summary>
        /// 取 UTP 传输并恢复为 NGO 的传输组件。
        ///
        /// 必须恢复：上一局若用过中继传输，NetworkConfig.NetworkTransport 仍指向
        /// WebSocketRelayTransport，不切回来的话本局会继续走中继。
        /// </summary>
        private static UnityTransport ResolveUnityTransport(NetworkManager networkManager)
        {
            var transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
                return null;

            networkManager.NetworkConfig.NetworkTransport = transport;
            return transport;
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
