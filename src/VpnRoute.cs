using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Veylix
{
    // Идёт ли трафик в интернет через VPN/туннель. Смотрим не на «есть ли VPN-адаптер»
    // (Radmin VPN или ZeroTier держат только локальную сеть), а на то, через какой адаптер
    // Windows реально отправит пакет на внешний адрес.
    public static class VpnRoute
    {
        static readonly string[] Keywords =
        {
            "vpn", "tunnel", "wintun", "wireguard", "tap-windows", "openvpn", "xray", "v2ray", "sing-box", "singbox",
            "happ", "clash", "hiddify", "nekoray", "nekobox", "amnezia", "outline", "warp", "zerotier", "tailscale", "softether", "proton",
        };

        public static bool LooksLikeVpn(string nameAndDescription)
        {
            string s = " " + (nameAndDescription ?? "").ToLowerInvariant() + " ";
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"[\s\-_(]tun\d*[\s\-_)]")) return true;   // «tun», «tun0», «sing-tun»
            foreach (string k in Keywords) if (s.Contains(k)) return true;
            return false;
        }

        [DllImport("iphlpapi.dll")]
        static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

        // Имя адаптера VPN, через который идёт интернет-трафик, или null
        public static string Active()
        {
            try
            {
                uint dest = BitConverter.ToUInt32(IPAddress.Parse("1.1.1.1").GetAddressBytes(), 0);
                uint idx;
                if (GetBestInterface(dest, out idx) != 0) return null;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    IPv4InterfaceProperties p4;
                    try { p4 = ni.GetIPProperties().GetIPv4Properties(); } catch { continue; }
                    if (p4 == null || p4.Index != idx) continue;
                    bool vpn = ni.NetworkInterfaceType == NetworkInterfaceType.Ppp || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                               LooksLikeVpn(ni.Name + " " + ni.Description);
                    return vpn ? (ni.Description.Length > 0 ? ni.Description : ni.Name) : null;
                }
            }
            catch { }
            return null;
        }
    }
}
