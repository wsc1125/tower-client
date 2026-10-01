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
