// ============================================================================
//  BetaClock - Earnings: fetch, cache and display lines
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
        //  Earnings: keyless download, cache and the lines to display
        // ---------------------------------------------------------------------
        private struct InfoLine { public string text; public Color col; public float scale; }
        private static InfoLine MakeIL(string t, Color c, float s) { InfoLine il; il.text = t; il.col = c; il.scale = s; return il; }
        private static string ShortDow(DateTime d) { return Tr.DowShort((int)d.DayOfWeek); }
        private static string ShortDate(DateTime d) { return d.Day + "-" + Tr.MonAbbr(d.Month); }

        private HashSet<string> TickerSet()
        {
            HashSet<string> set = new HashSet<string>();
            if (_s.EarningsTickers == null) return set;
            string[] parts = _s.EarningsTickers.Split(new char[] { ',', ' ', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts) { string t = p.Trim().ToUpperInvariant(); if (t.Length > 0) set.Add(t); }
            return set;
        }

        private List<InfoLine> EarningsLines()
        {
            List<InfoLine> r = new List<InfoLine>();
            List<EarningEvent> ev = _earnings;
            if (ev == null || ev.Count == 0) return r;
            DateTime et = EtNow().Date;
            DateTime monday = et.AddDays(-(((int)et.DayOfWeek + 6) % 7));
            DateTime sunday = monday.AddDays(6);
            List<EarningEvent> week = new List<EarningEvent>();
            EarningEvent next = null;
            foreach (EarningEvent e in ev)
            {
                DateTime ed = e.Date.Date;
                if (ed >= monday && ed <= sunday) week.Add(e);
                if (ed >= et && (next == null || e.Date < next.Date)) next = e;
            }
            if (week.Count > 0)
            {
                StringBuilder sb = new StringBuilder(Tr.T("★ Earnings wk: "));
                int shown = 0;
                foreach (EarningEvent e in week)
                {
                    if (shown > 0) sb.Append(", ");
                    sb.Append(e.Symbol).Append("(").Append(ShortDow(e.Date)).Append(")");
                    shown++; if (shown >= 6) break;
                }
                if (week.Count > shown) sb.Append(" +").Append(week.Count - shown);
                r.Add(MakeIL(sb.ToString(), Color.FromArgb(245, 205, 40), 0.18f));
            }
            if (next != null)
            {
                int days = (next.Date.Date - et).Days;
                string dl = days == 0 ? Tr.T("today") : (days == 1 ? Tr.T("tomorrow") : (days < 7 ? ShortDow(next.Date) : ShortDate(next.Date)));
                string wn = next.When.Length > 0 ? " " + next.When : "";
                string cd = days <= 1 ? "" : "  ·  " + days + "d";
                r.Add(MakeIL(Tr.T("▸ Next: ") + next.Symbol + " " + dl + wn + cd, Color.FromArgb(80, 200, 235), 0.18f));
            }
            return r;
        }

        private string EarnKey()
        {
            StringBuilder sb = new StringBuilder();
            foreach (InfoLine il in EarningsLines()) sb.Append(NormalizeDigits(il.text)).Append("|");
            return sb.ToString();
        }

        // --- Download thread ---
        private void EarnLoop()
        {
            while (!_earnStop)
            {
                bool failed = false;
                try
                {
                    string today = EtNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    bool force = _earnForce;   // capture and clear BEFORE fetching (so a refresh arriving mid-download is not lost)
                    _earnForce = false;
                    if ((_earnFetchedDay != today || force) && TickerSet().Count > 0)
                    {
                        FetchEarnings();
                        if (_earnFetchedDay != today) failed = true; // failed (no network) -> retry soon
                    }
                }
                catch { }
                _earnWake.WaitOne(failed ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(3));
            }
        }

        private void FetchEarnings()
        {
            _earnLoading = true; _earnStatus = Tr.T("Updating...");
            try { ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12; }
            catch { }
            try
            {
                HashSet<string> set = TickerSet();
                DateTime today = EtNow().Date;
                DateTime start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // Monday of this week (so already-reported days are included)
                List<EarningEvent> acc = new List<EarningEvent>();
                int scanned = 0;
                for (int i = 0; i < 95 && !_earnStop; i++) // ~Monday + 13 weeks: covers the full quarter
                {
                    DateTime d = start.AddDays(i);
                    if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
                    string url = "https://api.nasdaq.com/api/calendar/earnings?date=" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    try { ParseEarnings(HttpGet(url), d, set, acc); scanned++; }
                    catch { }
                    _earnStatus = string.Format(Tr.T("Updating... ({0} days, {1} events)"), scanned, acc.Count);
                    Thread.Sleep(120);
                }
                if (scanned > 0)
                {
                    acc.Sort(delegate (EarningEvent a, EarningEvent b) { return a.Date.CompareTo(b.Date); });
                    _earnings = acc;
                    _earnFetchedDay = EtNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    SaveEarnCache(acc);
                    _earnStatus = string.Format(Tr.T("{0} earnings · updated {1}"), acc.Count, EtNow().ToString("dd-MM HH:mm", CultureInfo.InvariantCulture));
                    try { this.BeginInvoke((MethodInvoker)delegate { RecalcLayout(); Invalidate(); RefreshEarnForm(); }); }
                    catch { }
                }
                else
                {
                    // total network failure: do NOT touch good data or the cache; the loop retries in 10 min
                    _earnStatus = Tr.T("Offline, will retry...");
                }
            }
            catch (Exception ex) { _earnStatus = "Error: " + ex.Message; }
            _earnLoading = false;
        }

        private static string HttpGet(string url)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                wc.Headers.Add("Accept", "application/json, text/plain, */*");
                wc.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                wc.Encoding = Encoding.UTF8;
                return wc.DownloadString(url);
            }
        }

        private static void ParseEarnings(string json, DateTime date, HashSet<string> set, List<EarningEvent> acc)
        {
            object root = JsonParser.Parse(json);
            Dictionary<string, object> rd = root as Dictionary<string, object>;
            if (rd == null || !rd.ContainsKey("data")) return;
            Dictionary<string, object> data = rd["data"] as Dictionary<string, object>;
            if (data == null || !data.ContainsKey("rows")) return;
            List<object> rows = data["rows"] as List<object>;
            if (rows == null) return;
            foreach (object ro in rows)
            {
                Dictionary<string, object> row = ro as Dictionary<string, object>;
                if (row == null || !row.ContainsKey("symbol")) continue;
                string sym = row["symbol"] as string;
                if (sym == null) continue;
                sym = sym.Trim().ToUpperInvariant();
                if (!set.Contains(sym)) continue;
                string time = row.ContainsKey("time") ? (row["time"] as string) : "";
                if (time == null) time = "";
                string when = time.IndexOf("pre-market", StringComparison.OrdinalIgnoreCase) >= 0 ? "BMO"
                            : (time.IndexOf("after-hours", StringComparison.OrdinalIgnoreCase) >= 0 ? "AMC" : "");
                EarningEvent e = new EarningEvent(); e.Date = date; e.Symbol = sym; e.When = when;
                acc.Add(e);
            }
        }

        private static string EarnCachePath()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BetaClock"), "earnings.cache");
        }
        private void SaveEarnCache(List<EarningEvent> ev)
        {
            try
            {
                StringBuilder b = new StringBuilder();
                b.AppendLine("FETCHED=" + _earnFetchedDay);
                b.AppendLine("TICKERS=" + string.Join(",", new List<string>(TickerSet()).ToArray()));
                foreach (EarningEvent e in ev)
                    b.AppendLine(e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + e.Symbol + "|" + e.When);
                Directory.CreateDirectory(Path.GetDirectoryName(EarnCachePath()));
                File.WriteAllText(EarnCachePath(), b.ToString());
            }
            catch { }
        }
        private void LoadEarnCache()
        {
            try
            {
                string p = EarnCachePath();
                if (!File.Exists(p)) return;
                string[] lines = File.ReadAllLines(p);
                List<EarningEvent> ev = new List<EarningEvent>();
                string fetched = ""; string tickers = "";
                foreach (string line in lines)
                {
                    if (line.StartsWith("FETCHED=")) { fetched = line.Substring(8).Trim(); continue; }
                    if (line.StartsWith("TICKERS=")) { tickers = line.Substring(8).Trim(); continue; }
                    string[] f = line.Split('|');
                    if (f.Length < 2) continue;
                    DateTime dt;
                    if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) continue;
                    EarningEvent e = new EarningEvent(); e.Date = dt; e.Symbol = f[1]; e.When = f.Length > 2 ? f[2] : "";
                    ev.Add(e);
                }
                ev.Sort(delegate (EarningEvent a, EarningEvent b) { return a.Date.CompareTo(b.Date); });
                _earnings = ev;
                // if the ticker list changed, force a refresh
                string curTickers = string.Join(",", new List<string>(TickerSet()).ToArray());
                if (tickers == curTickers) _earnFetchedDay = fetched;
                _earnStatus = string.Format(Tr.T("{0} earnings (cache)"), ev.Count);
            }
            catch { }
        }

        public void TriggerEarningsRefresh()
        {
            _earnForce = true; _earnWake.Set();
        }
        private void RefreshEarnForm()
        {
            if (_earnForm != null && !_earnForm.IsDisposed) _earnForm.RefreshData();
        }
    }
}
