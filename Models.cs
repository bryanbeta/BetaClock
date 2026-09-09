// ============================================================================
//  BetaClock - Alarm and earnings-event models
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
    //  Alarm
    // -------------------------------------------------------------------------
    public class Alarm
    {
        public int H, M, S;
        public bool Enabled = true;
        public int Repeat = 1;        // 0 = once, 1 = daily, 2 = Mon-Fri
        public string Label = "";
        public DateTime LastFire = DateTime.MinValue; // in memory only, never saved

        public bool AppliesOn(DateTime now)
        {
            if (Repeat == 2)
            {
                DayOfWeek d = now.DayOfWeek;
                return d != DayOfWeek.Saturday && d != DayOfWeek.Sunday;
            }
            return true;
        }
        public string TimeString() { return string.Format("{0:00}:{1:00}:{2:00}", H, M, S); }
        public string RepeatString() { return Repeat == 0 ? "Once" : (Repeat == 1 ? "Daily" : "Mon-Fri"); }

        public string Serialize()
        {
            string lab = (Label == null ? "" : Label).Replace("|", " ").Replace("\r", " ").Replace("\n", " ");
            return TimeString() + "|" + (Enabled ? "1" : "0") + "|" + Repeat + "|" + lab;
        }
        public static Alarm Parse(string s)
        {
            try
            {
                string[] p = s.Split('|');
                string[] t = p[0].Split(':');
                Alarm a = new Alarm();
                a.H = int.Parse(t[0], CultureInfo.InvariantCulture);
                a.M = int.Parse(t[1], CultureInfo.InvariantCulture);
                a.S = t.Length > 2 ? int.Parse(t[2], CultureInfo.InvariantCulture) : 0;
                a.Enabled = p.Length > 1 ? (p[1] == "1") : true;
                a.Repeat = p.Length > 2 ? int.Parse(p[2], CultureInfo.InvariantCulture) : 1;
                a.Label = p.Length > 3 ? p[3] : "";
                return a;
            }
            catch { return null; }
        }
    }

    // -------------------------------------------------------------------------
    //  Earnings (company results calendar)
    // -------------------------------------------------------------------------
    public class EarningEvent
    {
        public DateTime Date;
        public string Symbol;
        public string When; // "BMO" (before market open), "AMC" (after market close), "" (unknown)
    }
}
