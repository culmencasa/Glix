using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Glix;

/// <summary>
/// 解析本机实际使用的 IPv4 地址，过滤代理 TUN、虚拟网卡等非物理链路地址。
/// </summary>
/// <remarks>
/// UDP 路由探测在装有代理 TUN（Clash/Mihomo 等）的机器上会拿到 TUN 网卡地址，
/// 因此优先枚举网卡并按“网关是否同网段”筛选真实链路地址，探测方式仅作为兜底。
/// </remarks>
internal static class LocalIpResolver
{
    /// <summary>
    /// 获取本机 IPv4 地址。
    /// </summary>
    /// <returns>本机 IPv4 地址字符串，全部手段失败时返回空字符串。</returns>
    public static string GetLocalIPv4()
    {
        string address = ResolveFromInterfaces();

        if (!string.IsNullOrEmpty(address))
        {
            return address;
        }

        return ResolveFromRoute();
    }

    /// <summary>
    /// 枚举网卡，选取最贴近真实链路的 IPv4 地址。
    /// </summary>
    /// <returns>选中的 IPv4 地址字符串，没有可用网卡时返回空字符串。</returns>
    private static string ResolveFromInterfaces()
    {
        List<Candidate> candidates = new List<Candidate>();

        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties;

            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            List<IPAddress> gateways = new List<IPAddress>();

            foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
            {
                if (gateway.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    gateways.Add(gateway.Address);
                }
            }

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (IsExcludedAddress(unicast.Address))
                {
                    continue;
                }

                bool hasOnLinkGateway = false;

                foreach (IPAddress gateway in gateways)
                {
                    if (IsSameSubnet(unicast.Address, unicast.IPv4Mask, gateway))
                    {
                        hasOnLinkGateway = true;
                        break;
                    }
                }

                candidates.Add(new Candidate(
                    unicast.Address,
                    gateways.Count > 0,
                    hasOnLinkGateway,
                    IsPrivateAddress(unicast.Address),
                    IsPhysicalLink(adapter.NetworkInterfaceType),
                    adapter.Id));
            }
        }

        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        Candidate best = candidates
            .OrderByDescending(candidate => candidate.HasOnLinkGateway)
            .ThenByDescending(candidate => candidate.HasGateway)
            .ThenByDescending(candidate => candidate.IsPhysicalLink)
            .ThenByDescending(candidate => candidate.IsPrivate)
            .ThenBy(candidate => candidate.AdapterId, System.StringComparer.Ordinal)
            .First();

        return best.Address.ToString();
    }

    /// <summary>
    /// 通过 UDP 路由探测获取本机地址（参考 NINI 的实现），不产生实际流量。
    /// </summary>
    /// <returns>探测到的 IPv4 地址字符串，探测失败时返回空字符串。</returns>
    private static string ResolveFromRoute()
    {
        try
        {
            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);

            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch (SocketException)
        {
            // 无网络或路由不可达：交由下次刷新重试。
        }

        return string.Empty;
    }

    /// <summary>
    /// 判断地址是否属于需要排除的网段。
    /// </summary>
    /// <param name="address">待判断的 IPv4 地址。</param>
    /// <returns>属于自动配置地址或代理基准测试网段时返回 true。</returns>
    private static bool IsExcludedAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        // 169.254.0.0/16 自动配置地址
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        // 198.18.0.0/15 基准测试网段，代理 TUN 常用
        if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判断网卡是否为物理链路类型，用于排除代理 TUN 等虚拟适配器。
    /// </summary>
    /// <param name="type">网卡类型。</param>
    /// <returns>为以太网或无线网卡时返回 true。</returns>
    private static bool IsPhysicalLink(NetworkInterfaceType type)
    {
        return type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211;
    }

    /// <summary>
    /// 判断地址是否为 RFC 1918 私有地址。
    /// </summary>
    /// <param name="address">待判断的 IPv4 地址。</param>
    /// <returns>属于 10.0.0.0/8、172.16.0.0/12 或 192.168.0.0/16 时返回 true。</returns>
    private static bool IsPrivateAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        if (bytes[0] == 10)
        {
            return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        return bytes[0] == 192 && bytes[1] == 168;
    }

    /// <summary>
    /// 判断网关地址与本机地址是否处于同一子网。
    /// </summary>
    /// <param name="address">本机 IPv4 地址。</param>
    /// <param name="mask">本机 IPv4 子网掩码。</param>
    /// <param name="gateway">待判断的网关地址。</param>
    /// <returns>网关与本机地址同网段时返回 true。</returns>
    private static bool IsSameSubnet(IPAddress address, IPAddress? mask, IPAddress gateway)
    {
        if (mask is null || mask.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] addressBytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        byte[] gatewayBytes = gateway.GetAddressBytes();

        for (int i = 0; i < addressBytes.Length; i++)
        {
            if ((addressBytes[i] & maskBytes[i]) != (gatewayBytes[i] & maskBytes[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 候选网卡地址及其筛选特征。
    /// </summary>
    /// <param name="Address">候选 IPv4 地址。</param>
    /// <param name="HasGateway">网卡是否配置了 IPv4 网关。</param>
    /// <param name="HasOnLinkGateway">网关是否与本机地址同网段。</param>
    /// <param name="IsPrivate">地址是否为私有地址。</param>
    /// <param name="IsPhysicalLink">网卡是否为物理链路类型。</param>
    /// <param name="AdapterId">网卡标识，用于稳定排序。</param>
    private readonly record struct Candidate(
        IPAddress Address,
        bool HasGateway,
        bool HasOnLinkGateway,
        bool IsPrivate,
        bool IsPhysicalLink,
        string AdapterId);
}
