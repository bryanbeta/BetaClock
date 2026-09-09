// ============================================================================
//  BetaClock - SNTP time synchronization
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BetaClock
{
    public partial class ClockForm
    {
        // ---------------------------------------------------------------------
        //  NTP (SNTP)
        // ---------------------------------------------------------------------
        private const double MAX_NTP_OFFSET_HOURS = 24.0;

        private void NtpLoop()
        {
            string[] servers = { _s.NtpServer, "pool.ntp.org", "time.windows.com", "time.nist.gov" };
            while (!_ntpStop)
            {
                if (_s.NtpSync)
                {
                    bool ok = false;
                    foreach (string srv in servers)
                    {
                        TimeSpan? off = GetNtpOffset(srv);
                        // Sanity check: NTP is unauthenticated UDP, so a hostile reply on the
                        // same network could shift the clock arbitrarily. A real PC clock is
                        // never off by more than a few hours, so anything wilder is rejected.
                        if (off.HasValue && Math.Abs(off.Value.TotalHours) > MAX_NTP_OFFSET_HOURS) off = null;
                        if (off.HasValue)
                        {
                            Interlocked.Exchange(ref _ntpOffsetTicks, off.Value.Ticks);
                            _ntpLastUtc = DateTime.UtcNow;
                            _ntpOk = true; ok = true;
                            break;
                        }
                    }
                    if (!ok) _ntpOk = false;
                }
                else
                {
                    Interlocked.Exchange(ref _ntpOffsetTicks, 0);
                }
                _ntpWake.WaitOne(_s.NtpSync ? 600000 : 3600000);
            }
        }

        private static TimeSpan? GetNtpOffset(string host)
        {
            try
            {
                byte[] data = new byte[48];
                data[0] = 0x1B; // LI=0, VN=3, Mode=3 (client)

                IPAddress[] addrs = Dns.GetHostAddresses(host);
                IPAddress ip = null;
                foreach (IPAddress a in addrs)
                    if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                if (ip == null && addrs.Length > 0) ip = addrs[0];
                if (ip == null) return null;

                IPEndPoint ep = new IPEndPoint(ip, 123);
                using (Socket sock = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
                {
                    sock.ReceiveTimeout = 3000;
                    sock.SendTimeout = 3000;
                    sock.Connect(ep);
                    DateTime t0 = DateTime.UtcNow;
                    sock.Send(data);
                    sock.Receive(data);
                    DateTime t3 = DateTime.UtcNow;

                    int mode = data[0] & 0x07;
                    int stratum = data[1];
                    if (mode != 4 || stratum == 0 || stratum > 15) return null; // not a valid server reply

                    ulong intPart = ((ulong)data[40] << 24) | ((ulong)data[41] << 16) | ((ulong)data[42] << 8) | (ulong)data[43];
                    ulong fracPart = ((ulong)data[44] << 24) | ((ulong)data[45] << 16) | ((ulong)data[46] << 8) | (ulong)data[47];
                    if (intPart == 0 && fracPart == 0) return null;

                    double seconds = intPart + ((double)fracPart) / 4294967296.0;
                    DateTime serverUtc = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
                    DateTime localMid = t0.AddTicks((t3 - t0).Ticks / 2);
                    return serverUtc - localMid;
                }
            }
            catch { return null; }
        }
    }
}
