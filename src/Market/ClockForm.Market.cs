// ============================================================================
//  BetaClock - Market sessions, NYSE holidays and Forex
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
        //  Market sessions
        // ---------------------------------------------------------------------
        private DateTime CurrentUtc()
        {
            DateTime u = DateTime.UtcNow;
            if (_s.NtpSync)
            {
                long off = Interlocked.Read(ref _ntpOffsetTicks);
                if (off != 0) u = u.AddTicks(off);
            }
            return u;
        }

        private TimeZoneInfo EastTz()
        {
            if (_tzEast == null)
            {
                try { _tzEast = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { _tzEast = null; }
            }
            return _tzEast;
        }

        private static string FmtDelta(TimeSpan d)
        {
            if (d.Ticks < 0) d = TimeSpan.Zero;
            if (d.TotalHours >= 24)
                return string.Format("{0}d {1:00}:{2:00}", d.Days, d.Hours, d.Minutes);
            return string.Format("{0:00}:{1:00}:{2:00}", (int)d.TotalHours, d.Minutes, d.Seconds);
        }

        private string SessionText(out Color col)
        {
            try
            {
                if (_s.SessionMarket == 1) return ForexText(out col);
                return UsText(out col);
            }
            catch { col = Color.FromArgb(150, 150, 160); return ""; }
        }

        private DateTime EtNow()
        {
            DateTime utc = CurrentUtc();
            TimeZoneInfo tz = EastTz();
            return tz != null ? TimeZoneInfo.ConvertTimeFromUtc(utc, tz) : utc.AddHours(-4);
        }

        private string UsText(out Color col)
        {
            DateTime et = EtNow();
            DateTime today = et.Date;
            TimeSpan PRE = new TimeSpan(4, 0, 0), OPEN = new TimeSpan(9, 30, 0), AFTER = new TimeSpan(20, 0, 0);
            TimeSpan closeT = CloseTimeFor(today);
            bool trading = IsTradingDay(today);
            TimeSpan tod = et.TimeOfDay;

            string state, action; TimeSpan delta;
            if (trading && tod >= OPEN && tod < closeT)
            {
                col = Color.FromArgb(40, 220, 90); action = Tr.T("closes"); delta = today.Add(closeT) - et;
                state = IsEarlyCloseDate(today) ? Tr.T("OPEN (1pm close)") : Tr.T("OPEN");
            }
            else
            {
                DateTime nx = NextUsOpen(et);
                delta = nx - et; action = Tr.T("opens");
                if (!trading && IsHolidayDate(today)) { state = Tr.T("HOLIDAY: ") + Tr.T(HolidayName(today)); col = Color.FromArgb(195, 120, 235); }
                else if (!IsWd(today)) { state = Tr.T("WEEKEND"); col = Color.FromArgb(150, 150, 160); }
                else if (tod >= PRE && tod < OPEN) { state = Tr.T("PRE-MARKET"); col = Color.FromArgb(245, 205, 40); }
                else if (tod >= closeT && tod < AFTER) { state = Tr.T("AFTER-HOURS"); col = Color.FromArgb(255, 150, 40); }
                else { state = Tr.T("CLOSED"); col = Color.FromArgb(150, 150, 160); }
            }
            return "● NYSE " + state + "  " + action + " " + FmtDelta(delta);
        }

        private DateTime NextUsOpen(DateTime et)
        {
            TimeSpan OPEN = new TimeSpan(9, 30, 0);
            for (int i = 0; i < 30; i++)
            {
                DateTime cand = et.Date.AddDays(i).Add(OPEN);
                if (cand > et && IsTradingDay(cand)) return cand;
            }
            return et.Date.Add(OPEN).AddDays(1);
        }

        // Upcoming holiday / early-close notice (looks up to 4 days ahead)
        private string BuildNotice()
        {
            if (!_s.ShowSession || _s.SessionMarket != 0) return "";
            try
            {
                DateTime today = EtNow().Date;
                for (int i = 1; i <= 4; i++)
                {
                    DateTime d = today.AddDays(i);
                    if (IsHolidayDate(d))
                    {
                        string when = (i == 1) ? Tr.T("Tomorrow") : CapDow(d);
                        return string.Format(Tr.T("⚠ {0} holiday: {1}"), when, Tr.T(HolidayName(d)));
                    }
                }
                if (IsEarlyCloseDate(today.AddDays(1))) return Tr.T("⚠ Tomorrow early close (1 pm)");
                return "";
            }
            catch { return ""; }
        }

        private static string NormalizeDigits(string s)
        {
            if (s == null) return "";
            char[] c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++) if (c[i] >= '0' && c[i] <= '9') c[i] = '0';
            return new string(c);
        }

        // ---- NYSE holidays (rule-based, cached per year) ----
        private Dictionary<DateTime, string> Holidays(int y)
        {
            if (!_holCache.ContainsKey(y)) BuildYear(y);
            return _holCache[y];
        }
        private Dictionary<DateTime, string> EarlyCloses(int y)
        {
            if (!_ecCache.ContainsKey(y)) BuildYear(y);
            return _ecCache[y];
        }
        private void BuildYear(int y)
        {
            Dictionary<DateTime, string> h = new Dictionary<DateTime, string>();
            h[Observed(y, 1, 1).Date] = "New Year's Day";
            h[NthWeekday(y, 1, DayOfWeek.Monday, 3).Date] = "MLK Day";
            h[NthWeekday(y, 2, DayOfWeek.Monday, 3).Date] = "Presidents' Day";
            h[Easter(y).AddDays(-2).Date] = "Good Friday";
            h[LastWeekday(y, 5, DayOfWeek.Monday).Date] = "Memorial Day";
            if (y >= 2022) h[Observed(y, 6, 19).Date] = "Juneteenth";
            h[Observed(y, 7, 4).Date] = "Independence Day";
            h[NthWeekday(y, 9, DayOfWeek.Monday, 1).Date] = "Labor Day";
            h[NthWeekday(y, 11, DayOfWeek.Thursday, 4).Date] = "Thanksgiving";
            h[Observed(y, 12, 25).Date] = "Christmas";
            _holCache[y] = h;

            Dictionary<DateTime, string> e = new Dictionary<DateTime, string>();
            DateTime thx = NthWeekday(y, 11, DayOfWeek.Thursday, 4);
            e[thx.AddDays(1).Date] = "Day after Thanksgiving";
            DateTime d24 = new DateTime(y, 12, 24);
            if (IsWd(d24) && !h.ContainsKey(d24.Date)) e[d24.Date] = "Christmas Eve";
            DateTime j3 = new DateTime(y, 7, 3);
            if (IsWd(j3) && !h.ContainsKey(j3.Date)) e[j3.Date] = "July 3rd (pre-holiday)";
            _ecCache[y] = e;
        }
        private bool IsHolidayDate(DateTime d) { return Holidays(d.Year).ContainsKey(d.Date); }
        private bool IsEarlyCloseDate(DateTime d) { return EarlyCloses(d.Year).ContainsKey(d.Date); }
        private string HolidayName(DateTime d) { Dictionary<DateTime, string> h = Holidays(d.Year); return h.ContainsKey(d.Date) ? h[d.Date] : ""; }
        private bool IsTradingDay(DateTime d) { return IsWd(d) && !IsHolidayDate(d); }
        private TimeSpan CloseTimeFor(DateTime d) { return IsEarlyCloseDate(d) ? new TimeSpan(13, 0, 0) : new TimeSpan(16, 0, 0); }

        private static bool IsWd(DateTime d) { return d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday; }
        private static string CapDow(DateTime d) { return Tr.DowFull((int)d.DayOfWeek); }
        private static DateTime NthWeekday(int y, int month, DayOfWeek dow, int n)
        {
            DateTime d = new DateTime(y, month, 1);
            int add = ((int)dow - (int)d.DayOfWeek + 7) % 7;
            return d.AddDays(add + 7 * (n - 1));
        }
        private static DateTime LastWeekday(int y, int month, DayOfWeek dow)
        {
            DateTime d = new DateTime(y, month, DateTime.DaysInMonth(y, month));
            int sub = ((int)d.DayOfWeek - (int)dow + 7) % 7;
            return d.AddDays(-sub);
        }
        private static DateTime Observed(int y, int m, int day)
        {
            DateTime d = new DateTime(y, m, day);
            if (d.DayOfWeek == DayOfWeek.Saturday) return d.AddDays(-1);
            if (d.DayOfWeek == DayOfWeek.Sunday) return d.AddDays(1);
            return d;
        }
        private static DateTime Easter(int y)
        {
            int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4,
                f = (b + 8) / 25, g = (b - f + 1) / 3,
                hh = (19 * a + b - d - g + 15) % 30,
                i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - hh - k) % 7,
                m = (a + 11 * hh + 22 * l) / 451,
                month = (hh + l - 7 * m + 114) / 31,
                day = ((hh + l - 7 * m + 114) % 31) + 1;
            return new DateTime(y, month, day);
        }

        private static readonly string[] FxNames = { "Sydney", "Tokyo", "London", "NY" }; // display names come from Tr.Fx()
        private static readonly string[] FxZoneIds = { "AUS Eastern Standard Time", "Tokyo Standard Time", "GMT Standard Time", "Eastern Standard Time" };
        private static readonly int[] FxOpen = { 7, 9, 8, 8 };
        private static readonly int[] FxClose = { 16, 18, 17, 17 };

        private string ForexText(out Color col)
        {
            DateTime utc = CurrentUtc();
            List<string> openNow = new List<string>();
            TimeSpan minClose = TimeSpan.MaxValue; string minCloseName = "";
            TimeSpan minOpen = TimeSpan.MaxValue; string minOpenName = "";

            for (int i = 0; i < FxNames.Length; i++)
            {
                TimeZoneInfo tz;
                try { tz = TimeZoneInfo.FindSystemTimeZoneById(FxZoneIds[i]); }
                catch { tz = null; }
                DateTime loc = tz != null ? TimeZoneInfo.ConvertTimeFromUtc(utc, tz) : utc;
                DayOfWeek d = loc.DayOfWeek;
                bool wd = d != DayOfWeek.Saturday && d != DayOfWeek.Sunday;
                TimeSpan o = new TimeSpan(FxOpen[i], 0, 0), c = new TimeSpan(FxClose[i], 0, 0);
                bool isOpen = wd && loc.TimeOfDay >= o && loc.TimeOfDay < c;
                if (isOpen)
                {
                    openNow.Add(Tr.Fx(i));
                    TimeSpan dc = loc.Date.Add(c) - loc;
                    if (dc < minClose) { minClose = dc; minCloseName = Tr.Fx(i); }
                }
                else
                {
                    DateTime no = NextFxOpen(loc, o);
                    TimeSpan d2 = no - loc;
                    if (d2 < minOpen) { minOpen = d2; minOpenName = Tr.Fx(i); }
                }
            }

            if (openNow.Count > 0)
            {
                col = Color.FromArgb(40, 220, 90);
                return "● " + Tr.T("Forex: ") + string.Join("+", openNow.ToArray()) + "  " + minCloseName + " " + Tr.T("closes") + " " + FmtDelta(minClose);
            }
            col = Color.FromArgb(150, 150, 160);
            return "● " + Tr.T("Forex closed ") + minOpenName + " " + Tr.T("opens") + " " + FmtDelta(minOpen);
        }

        private static DateTime NextFxOpen(DateTime loc, TimeSpan o)
        {
            for (int i = 0; i < 8; i++)
            {
                DateTime cand = loc.Date.AddDays(i).Add(o);
                DayOfWeek d = cand.DayOfWeek;
                if (cand > loc && d != DayOfWeek.Saturday && d != DayOfWeek.Sunday) return cand;
            }
            return loc.Date.Add(o).AddDays(1);
        }
    }
}
