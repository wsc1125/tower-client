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
