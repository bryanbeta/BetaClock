// ============================================================================
//  BetaClock - Persistent settings (plain key=value file)
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
    // -------------------------------------------------------------------------
    //  Persistent settings (plain key=value format, no dependencies)
    // -------------------------------------------------------------------------
    public class Settings
    {
        public int PosX = -1;          // -1 = center
        public int PosY = -1;
        public float Scale = 78f;      // digit cell width (px)
        public int ColorMode = 8;      // 0..9 (see OnColor)
        public float Brightness = 1.0f;
        public bool Format24h = true;
        public bool ShowSeconds = true;
        public bool ShowDate = false;
        public bool ShowDay = true;    // day-of-week column (MON..SUN)
        public bool ShowAmPm = true;   // only applies in 12h mode
        public bool ShowSession = true;  // market session line
        public int SessionMarket = 0;    // 0 = US (NYSE), 1 = global Forex
        public bool ShowEarnings = true; // earnings line (this week + next up)
        public string EarningsTickers = "AAPL,MSFT,NVDA,AMZN,GOOGL,META,TSLA,SPY,QQQ"; // edit yours in the Earnings panel
        public int Opacity = 100;      // 100..30
        public bool Topmost = true;
        public bool NtpSync = true;
        public bool Ghost = true;      // dim unlit segments (real LED look)
        public int GhostAlpha = 24;    // opacity (0-255) of the digit ghost/shadow
        public bool Glow = true;       // soft halo
        public bool Blink = false;     // colon blink
        public bool Locked = false;    // lock dragging
        public string NtpServer = "time.google.com";
        public string Lang = "";       // "" = auto-detect (es/en/pt/de/fr/zh)
        public List<Alarm> Alarms = new List<Alarm>();

        private static string Dir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetaClock");
        }
        private static string FilePath() { return Path.Combine(Dir(), "settings.cfg"); }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return s;
                Dictionary<string, string> d = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(p))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                s.PosX = GetI(d, "PosX", s.PosX);
                s.PosY = GetI(d, "PosY", s.PosY);
                s.Scale = GetF(d, "Scale", s.Scale);
                s.ColorMode = GetI(d, "ColorMode", s.ColorMode);
                s.Brightness = GetF(d, "Brightness", s.Brightness);
                s.Format24h = GetB(d, "Format24h", s.Format24h);
                s.ShowSeconds = GetB(d, "ShowSeconds", s.ShowSeconds);
                s.ShowDate = GetB(d, "ShowDate", s.ShowDate);
                s.ShowDay = GetB(d, "ShowDay", s.ShowDay);
                s.ShowAmPm = GetB(d, "ShowAmPm", s.ShowAmPm);
                s.ShowSession = GetB(d, "ShowSession", s.ShowSession);
                s.SessionMarket = GetI(d, "SessionMarket", s.SessionMarket);
                s.ShowEarnings = GetB(d, "ShowEarnings", s.ShowEarnings);
                if (d.ContainsKey("EarningsTickers")) s.EarningsTickers = d["EarningsTickers"];
                s.Opacity = GetI(d, "Opacity", s.Opacity);
                s.Topmost = GetB(d, "Topmost", s.Topmost);
                s.NtpSync = GetB(d, "NtpSync", s.NtpSync);
                s.Ghost = GetB(d, "Ghost", s.Ghost);
                s.GhostAlpha = GetI(d, "GhostAlpha", s.GhostAlpha);
                s.Glow = GetB(d, "Glow", s.Glow);
                s.Blink = GetB(d, "Blink", s.Blink);
                s.Locked = GetB(d, "Locked", s.Locked);
                if (d.ContainsKey("NtpServer") && d["NtpServer"].Length > 0) s.NtpServer = d["NtpServer"];
                if (d.ContainsKey("Lang")) s.Lang = d["Lang"];

                int ac = GetI(d, "AlarmCount", 0);
                for (int i = 0; i < ac; i++)
                {
                    string key = "Alarm" + i;
                    if (d.ContainsKey(key))
                    {
                        Alarm a = Alarm.Parse(d[key]);
                        if (a != null) s.Alarms.Add(a);
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir());
                StringBuilder b = new StringBuilder();
                b.AppendLine("PosX=" + PosX);
                b.AppendLine("PosY=" + PosY);
                b.AppendLine("Scale=" + Scale.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("ColorMode=" + ColorMode);
                b.AppendLine("Brightness=" + Brightness.ToString(CultureInfo.InvariantCulture));
                b.AppendLine("Format24h=" + Format24h);
                b.AppendLine("ShowSeconds=" + ShowSeconds);
                b.AppendLine("ShowDate=" + ShowDate);
                b.AppendLine("ShowDay=" + ShowDay);
                b.AppendLine("ShowAmPm=" + ShowAmPm);
                b.AppendLine("ShowSession=" + ShowSession);
                b.AppendLine("SessionMarket=" + SessionMarket);
                b.AppendLine("ShowEarnings=" + ShowEarnings);
                b.AppendLine("EarningsTickers=" + EarningsTickers);
                b.AppendLine("Opacity=" + Opacity);
                b.AppendLine("Topmost=" + Topmost);
                b.AppendLine("NtpSync=" + NtpSync);
                b.AppendLine("Ghost=" + Ghost);
                b.AppendLine("GhostAlpha=" + GhostAlpha);
                b.AppendLine("Glow=" + Glow);
                b.AppendLine("Blink=" + Blink);
                b.AppendLine("Locked=" + Locked);
                b.AppendLine("NtpServer=" + NtpServer);
                b.AppendLine("Lang=" + Lang);
                b.AppendLine("AlarmCount=" + Alarms.Count);
                for (int i = 0; i < Alarms.Count; i++)
                    b.AppendLine("Alarm" + i + "=" + Alarms[i].Serialize());
                File.WriteAllText(FilePath(), b.ToString());
            }
            catch { }
        }

        private static int GetI(Dictionary<string, string> d, string k, int def)
        { int v; if (d.ContainsKey(k) && int.TryParse(d[k], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v; return def; }
        private static float GetF(Dictionary<string, string> d, string k, float def)
        { float v; if (d.ContainsKey(k) && float.TryParse(d[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v; return def; }
        private static bool GetB(Dictionary<string, string> d, string k, bool def)
        { bool v; if (d.ContainsKey(k) && bool.TryParse(d[k], out v)) return v; return def; }
    }
}
