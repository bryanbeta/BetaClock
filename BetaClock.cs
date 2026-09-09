// ============================================================================
//  BetaClock - LED desktop clock for Windows (always-on-top overlay)
//  Built for trading: accurate time (NTP sync), HH:MM:SS in 7-segment style.
//
//  - Always on top of any window (TopMost + overlay that never steals focus)
//  - Drag it anywhere with the mouse
//  - 7-segment LED style, solid / RGB gradient / animated RGB color modes
//  - Internet time synchronization (SNTP) for accuracy
//  - Menu (right-click or tray icon) with every option
//  - Settings persisted in %APPDATA%\BetaClock\settings.cfg
//
//  Builds with the .NET Framework in-box compiler (C# 5).
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

    // Minimal JSON parser (no dependencies) for the calendar response
    public class JsonParser
    {
        private string s; private int i;
        public static object Parse(string text)
        {
            JsonParser j = new JsonParser(); j.s = text == null ? "" : text; j.i = 0;
            try { return j.Val(0); } catch { return null; }
        }
        private void Ws() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }
        private object Val(int depth)
        {
            if (depth > 500) throw new Exception("json demasiado anidado"); // avoids StackOverflow (which cannot be caught)
            Ws(); if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return Obj(depth + 1);
            if (c == '[') return Arr(depth + 1);
            if (c == '"') return Str();
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return Num();
        }
        private Dictionary<string, object> Obj(int depth)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(); i++; Ws();
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (i < s.Length)
            {
                Ws(); string k = Str(); Ws();
                if (i < s.Length && s[i] == ':') i++;
                object v = Val(depth); d[k] = v; Ws();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return d;
        }
        private List<object> Arr(int depth)
        {
            List<object> a = new List<object>(); i++; Ws();
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (i < s.Length)
            {
                object v = Val(depth); a.Add(v); Ws();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return a;
        }
        private string Str()
        {
            StringBuilder sb = new StringBuilder();
            if (i < s.Length && s[i] == '"') i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    if (e == '"') sb.Append('"');
                    else if (e == '\\') sb.Append('\\');
                    else if (e == '/') sb.Append('/');
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 'b') sb.Append('\b');
                    else if (e == 'f') sb.Append('\f');
                    else if (e == 'u' && i + 4 <= s.Length)
                    {
                        int code = Convert.ToInt32(s.Substring(i, 4), 16);
                        sb.Append((char)code); i += 4;
                    }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
        private object Num()
        {
            int st = i;
            while (i < s.Length && "0123456789+-.eE".IndexOf(s[i]) >= 0) i++;
            double d; double.TryParse(s.Substring(st, i - st), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
            return d;
        }
    }

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

    // -------------------------------------------------------------------------
    //  Main clock window
    // -------------------------------------------------------------------------
    public class ClockForm : Form
    {
        // Segment map per digit [a,b,c,d,e,f,g]
        private static readonly bool[][] DIGITS = new bool[][]
        {
            new bool[]{true,true,true,true,true,true,false},   //0
            new bool[]{false,true,true,false,false,false,false}, //1
            new bool[]{true,true,false,true,true,false,true},  //2
            new bool[]{true,true,true,true,false,false,true},  //3
            new bool[]{false,true,true,false,false,true,true}, //4
            new bool[]{true,false,true,true,false,true,true},  //5
            new bool[]{true,false,true,true,true,true,true},   //6
            new bool[]{true,true,true,false,false,false,false},//7
            new bool[]{true,true,true,true,true,true,true},    //8
            new bool[]{true,true,true,true,false,true,true}    //9
        };

        private Settings _s;
        private System.Windows.Forms.Timer _timer;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private Icon _icon;

        private int _startTick;
        private float _dateW = 0;
        private float _gutter = 0;
        private float _dayW = 0;
        private float _sessionW = 0;
        private float _noticeW = 0;
        private string _noticeText = "";
        private string _sessionStateKey = "";
        private int _lastSec = -1;
        private TimeZoneInfo _tzEast;
        private Dictionary<int, Dictionary<DateTime, string>> _holCache = new Dictionary<int, Dictionary<DateTime, string>>();
        private Dictionary<int, Dictionary<DateTime, string>> _ecCache = new Dictionary<int, Dictionary<DateTime, string>>();

        // Earnings
        private volatile List<EarningEvent> _earnings = new List<EarningEvent>();
        private Thread _earnThread;
        private AutoResetEvent _earnWake = new AutoResetEvent(false);
        private volatile bool _earnStop = false;
        private volatile bool _earnForce = false;
        private volatile bool _earnLoading = false;
        private volatile string _earnStatus = "Not updated";
        private string _earnFetchedDay = "";
        private float _earnW = 0;
        private string _earnKey = "";
        private EarningsForm _earnForm;
        private AboutForm _aboutForm;

        // NTP
        private Thread _ntpThread;
        private AutoResetEvent _ntpWake = new AutoResetEvent(false);
        private volatile bool _ntpStop = false;
        private long _ntpOffsetTicks = 0;
        private volatile bool _ntpOk = false;
        private DateTime _ntpLastUtc = DateTime.MinValue;

        // Alarm
        private SoundPlayer _alarmPlayer;
        private MemoryStream _alarmStream;
        private AlarmAlertForm _alert;
        private AlarmManagerForm _alarmMgr;
        private static byte[] _wavCache;

        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private bool _openAlarms = false;
        private bool _openAbout = false;

        public ClockForm(bool openAlarms, bool openAbout)
        {
            _openAlarms = openAlarms;
            _openAbout = openAbout;
            _s = Settings.Load();
            if (string.IsNullOrEmpty(_s.Lang)) _s.Lang = Tr.DetectCode();
            Tr.SetByCode(_s.Lang);
            _startTick = Environment.TickCount;

            this.Text = "BetaClock";
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.FromArgb(10, 10, 12);
            this.DoubleBuffered = true;
            this.AutoScaleMode = AutoScaleMode.None; // hand-controlled size (do not rescale for DPI)
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                          | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.KeyPreview = true;

            BuildTrayAndMenu();

            this.MouseDown += new MouseEventHandler(OnMouseDownH);
            this.MouseMove += new MouseEventHandler(OnMouseMoveH);
            this.MouseUp += new MouseEventHandler(OnMouseUpH);
            this.MouseWheel += new MouseEventHandler(OnWheelH);
            this.KeyDown += new KeyEventHandler(OnKeyDownH);
            this.FormClosing += new FormClosingEventHandler(OnClosingH);

            try { LoadEarnCache(); } catch { } // load cached earnings before the first layout (avoids clipping)
            RecalcLayout();
            PlaceWindow();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 40; // ~25 fps: exact seconds + smooth RGB
            _timer.Tick += new EventHandler(OnTick);
            _timer.Start();

            _ntpThread = new Thread(new ThreadStart(NtpLoop));
            _ntpThread.IsBackground = true;
            _ntpThread.Start();

            _earnThread = new Thread(new ThreadStart(EarnLoop));
            _earnThread.IsBackground = true;
            _earnThread.Start();
        }

        // Overlay: never steals focus from the trading platform, hidden from Alt-Tab
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { /* painted by OnPaint */ }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_openAlarms) OpenAlarmManager();
            if (_openAbout) OpenAbout();
        }

        // ---------------------------------------------------------------------
        //  Current time (with NTP offset when enabled)
        // ---------------------------------------------------------------------
        private DateTime CurrentTime()
        {
            DateTime t = DateTime.Now;
            if (_s.NtpSync)
            {
                long off = Interlocked.Read(ref _ntpOffsetTicks);
                if (off != 0) t = t.AddTicks(off);
            }
            return t;
        }

        // ---------------------------------------------------------------------
        //  Window placement / size
        // ---------------------------------------------------------------------
        private struct Elem { public int type; public int val; public float x; public float w; } // type 0=digit 1=colon

        private List<Elem> BuildRow(float cw, out float rowW, DateTime now)
        {
            List<Elem> list = new List<Elem>();
            float sp = cw * 0.16f;
            float colW = cw * 0.55f;

            int hh = now.Hour;
            if (!_s.Format24h) { hh = now.Hour % 12; if (hh == 0) hh = 12; }
            int mm = now.Minute, ss = now.Second;
            int d0 = hh / 10, d1 = hh % 10;
            if (!_s.Format24h && hh < 10) d0 = -1; // no leading zero in 12h mode
            int[] digs = new int[] { d0, d1, mm / 10, mm % 10, ss / 10, ss % 10 };

            float x = 0;
            AddDigit(list, digs[0], ref x, cw); x += sp;
            AddDigit(list, digs[1], ref x, cw);
            AddColon(list, ref x, colW);
            AddDigit(list, digs[2], ref x, cw); x += sp;
            AddDigit(list, digs[3], ref x, cw);
            if (_s.ShowSeconds)
            {
                AddColon(list, ref x, colW);
                AddDigit(list, digs[4], ref x, cw); x += sp;
                AddDigit(list, digs[5], ref x, cw);
            }
            rowW = x;
            return list;
        }
        private void AddDigit(List<Elem> l, int val, ref float x, float cw)
        { Elem e = new Elem(); e.type = 0; e.val = val; e.x = x; e.w = cw; l.Add(e); x += cw; }
        private void AddColon(List<Elem> l, ref float x, float cw)
        { Elem e = new Elem(); e.type = 1; e.val = 0; e.x = x; e.w = cw; l.Add(e); x += cw; }

        private string DateString(DateTime now)
        {
            return Tr.DowUp((int)now.DayOfWeek) + "  " + now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private void RecalcLayout()
        {
            float cw = _s.Scale;
            float ch = cw * 1.85f;
            float pad = cw * 0.55f;

            DateTime now = CurrentTime();
            float rowW;
            BuildRow(cw, out rowW, now);

            _gutter = (!_s.Format24h && _s.ShowAmPm) ? cw * 0.62f : 0f;
            _dayW = _s.ShowDay ? cw * 1.05f : 0f;

            _dateW = 0;
            float dateRowH = 0;
            if (_s.ShowDate)
            {
                using (Font f = new Font("Segoe UI", cw * 0.22f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    Size sz = TextRenderer.MeasureText(DateString(now), f);
                    _dateW = sz.Width;
                }
                dateRowH = cw * 0.5f;
            }

            _sessionW = 0;
            float sessionRowH = 0;
            if (_s.ShowSession)
            {
                Color sc; string st = SessionText(out sc);
                _sessionStateKey = NormalizeDigits(st); // normalized digits -> stable width every second
                using (Font f = new Font("Segoe UI", cw * 0.21f, FontStyle.Bold, GraphicsUnit.Pixel))
                    _sessionW = TextRenderer.MeasureText(_sessionStateKey, f).Width;
                sessionRowH = cw * 0.42f;
            }

            _noticeW = 0;
            float noticeRowH = 0;
            _noticeText = BuildNotice();
            if (_s.ShowSession && _noticeText.Length > 0)
            {
                using (Font f = new Font("Segoe UI", cw * 0.17f, FontStyle.Bold, GraphicsUnit.Pixel))
                    _noticeW = TextRenderer.MeasureText(_noticeText, f).Width;
                noticeRowH = cw * 0.34f;
            }

            _earnW = 0;
            float earnH = 0;
            if (_s.ShowEarnings)
            {
                foreach (InfoLine el in EarningsLines())
                {
                    using (Font f = new Font("Segoe UI", cw * el.scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        float ew = TextRenderer.MeasureText(NormalizeDigits(el.text), f).Width;
                        if (ew > _earnW) _earnW = ew;
                    }
                    earnH += cw * el.scale * 1.5f + cw * 0.04f;
                }
                _earnKey = EarnKey();
            }

            float contentW = _dayW + _gutter + Math.Max(Math.Max(Math.Max(Math.Max(rowW, _dateW), _sessionW), _noticeW), _earnW);
            float contentH = ch
                + (_s.ShowDate ? dateRowH + cw * 0.10f : 0)
                + (_s.ShowSession ? sessionRowH + cw * 0.06f : 0)
                + (_noticeText.Length > 0 ? noticeRowH + cw * 0.02f : 0)
                + (earnH > 0 ? earnH + cw * 0.04f : 0);

            int w = (int)Math.Ceiling(contentW + pad * 2);
            int h = (int)Math.Ceiling(contentH + pad * 2);
            this.Size = new Size(w, h);

            int r = (int)(cw * 0.42f);
            this.Region = RoundedRegion(w, h, r);
            this.Opacity = Math.Max(0.30, _s.Opacity / 100.0);
            this.TopMost = _s.Topmost;
        }

        private static Region RoundedRegion(int w, int h, int r)
        {
            GraphicsPath p = new GraphicsPath();
            if (r * 2 > w) r = w / 2;
            if (r * 2 > h) r = h / 2;
            int d = r * 2;
            p.AddArc(0, 0, d, d, 180, 90);
            p.AddArc(w - d, 0, d, d, 270, 90);
            p.AddArc(w - d, h - d, d, d, 0, 90);
            p.AddArc(0, h - d, d, d, 90, 90);
            p.CloseFigure();
            return new Region(p);
        }

        private void PlaceWindow()
        {
            int x = _s.PosX, y = _s.PosY;

            if (x == int.MinValue || y == int.MinValue || x < -100000 || y < -100000 || (x < 0 && y < 0))
            {
                CenterOnPrimary();
                return;
            }

            // Monitor whose area contains the intended window center
            Point probe = new Point(x + this.Width / 2, y + this.Height / 2);
            Screen found = null;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.Bounds.Contains(probe)) { found = sc; break; }
            if (found == null)
            {
                // maybe it only drifted slightly: pick the nearest monitor by intersection
                Rectangle wnd = new Rectangle(x, y, this.Width, this.Height);
                foreach (Screen sc in Screen.AllScreens)
                    if (sc.Bounds.IntersectsWith(wnd)) { found = sc; break; }
            }
            if (found == null) { CenterOnPrimary(); return; }

            Rectangle wa = found.WorkingArea;
            if (x + this.Width > wa.Right) x = wa.Right - this.Width;
            if (y + this.Height > wa.Bottom) y = wa.Bottom - this.Height;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            this.Location = new Point(x, y);
        }

        private void CenterOnPrimary()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(wa.X + (wa.Width - this.Width) / 2, wa.Y + 30);
        }

        // ---------------------------------------------------------------------
        //  Drawing
        // ---------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.FromArgb(10, 10, 12));

            DateTime now = CurrentTime();
            float cw = _s.Scale;
            float ch = cw * 1.85f;
            float pad = cw * 0.55f;

            double phase = (_s.ColorMode == 9) ? (((Environment.TickCount - _startTick)) / 55.0) % 360.0 : 0.0;

            float rowW;
            List<Elem> row = BuildRow(cw, out rowW, now);

            float maxAll = Math.Max(Math.Max(Math.Max(Math.Max(rowW, _dateW), _sessionW), _noticeW), _earnW);
            float baseX = pad + _dayW + _gutter;
            float originX = baseX + (maxAll - rowW) / 2f;
            float originY = pad;

            // Day-of-week column (MON..SUN)
            if (_s.ShowDay)
                DrawDayStrip(g, pad, originY, cw, ch, now, phase);

            bool colonVisible = !_s.Blink || (now.Millisecond < 600);

            foreach (Elem el in row)
            {
                float frac = (el.x + el.w / 2f) / rowW;
                if (el.type == 0)
                    DrawDigit(g, el.val, originX + el.x, originY, cw, ch, frac, phase);
                else
                    DrawColon(g, originX + el.x, el.w, originY, ch, cw, frac, phase, colonVisible);
            }

            // AM/PM (12h mode only)
            if (!_s.Format24h && _s.ShowAmPm)
            {
                string ap = now.Hour < 12 ? "AM" : "PM";
                if (_s.ColorMode == 10) // in the candlestick skin, AM/PM is drawn with candles too
                {
                    float lh = cw * 0.46f, lw = cw * 0.26f, th = cw * 0.075f;
                    DrawCandleLabel(g, ap, pad + _dayW + cw * 0.03f, originY + (ch - lh) / 2f, lw, lh, th);
                }
                else
                {
                    Color c = OnColor(0f, phase);
                    using (Font f = new Font("Segoe UI", cw * 0.30f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(ap, f, b, pad + _dayW + cw * 0.05f, originY + ch * 0.30f);
                }
            }

            // Bottom rows: date and market session
            float yRow = originY + ch + cw * 0.06f;
            if (_s.ShowDate)
            {
                Color c = OnColor(0.5f, phase);
                using (Font f = new Font("Segoe UI", cw * 0.22f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(220, c)))
                {
                    string ds = DateString(now);
                    Size sz = TextRenderer.MeasureText(ds, f);
                    g.DrawString(ds, f, b, baseX + (maxAll - sz.Width) / 2f, yRow);
                }
                yRow += cw * 0.5f;
            }
            if (_s.ShowSession)
            {
                Color sc;
                string st = SessionText(out sc);
                using (Font f = new Font("Segoe UI", cw * 0.21f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(sc))
                {
                    Size sz = TextRenderer.MeasureText(st, f);
                    g.DrawString(st, f, b, baseX + (maxAll - sz.Width) / 2f, yRow);
                }
                yRow += cw * 0.42f;
            }
            if (_s.ShowSession && _noticeText.Length > 0)
            {
                using (Font f = new Font("Segoe UI", cw * 0.17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 180, 40)))
                {
                    Size sz = TextRenderer.MeasureText(_noticeText, f);
                    g.DrawString(_noticeText, f, b, baseX + (maxAll - sz.Width) / 2f, yRow);
                }
                yRow += cw * 0.34f;
            }
            if (_s.ShowEarnings)
            {
                foreach (InfoLine el in EarningsLines())
                {
                    using (Font f = new Font("Segoe UI", cw * el.scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(el.col))
                    {
                        Size sz = TextRenderer.MeasureText(el.text, f);
                        g.DrawString(el.text, f, b, baseX + (maxAll - sz.Width) / 2f, yRow);
                    }
                    yRow += cw * el.scale * 1.5f + cw * 0.04f;
                }
            }

            // NTP indicator (small dot in the bottom-right corner)
            DrawNtpDot(g, cw);

            // Next alarm (bottom-left)
            Alarm na = NextAlarm();
            if (na != null)
            {
                string at = "⏰ " + string.Format("{0:00}:{1:00}", na.H, na.M);
                using (Font f = new Font("Segoe UI", cw * 0.16f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 255, 170, 40)))
                    g.DrawString(at, f, b, cw * 0.16f, this.Height - cw * 0.34f);
            }
        }

        // ---------------------------------------------------------------------
        //  Alarms
        // ---------------------------------------------------------------------
        private void OnTick(object sender, EventArgs e)
        {
            int sec = DateTime.Now.Second;
            if (sec != _lastSec)
            {
                _lastSec = sec;
                bool need = false;
                if (_s.ShowSession) // detects a state/notice change (not every second because of the digits)
                {
                    Color c; string st = SessionText(out c);
                    if (NormalizeDigits(st) != _sessionStateKey || BuildNotice() != _noticeText) need = true;
                }
                if (_s.ShowEarnings && EarnKey() != _earnKey) need = true;
                if (need) RecalcLayout();
            }
            CheckAlarms();
            this.Invalidate();
        }

        private void CheckAlarms()
        {
            if (_s.Alarms.Count == 0) return;
            DateTime now = CurrentTime();
            foreach (Alarm a in _s.Alarms)
            {
                if (!a.Enabled) continue;
                if (!a.AppliesOn(now)) continue;
                DateTime target;
                try { target = new DateTime(now.Year, now.Month, now.Day, a.H, a.M, a.S); }
                catch { continue; }
                double diff = (now - target).TotalSeconds;
                if (diff >= 0 && diff < 30 && a.LastFire < target)
                {
                    a.LastFire = target;
                    if (a.Repeat == 0) a.Enabled = false; // once
                    _s.Save();
                    FireAlarm(a);
                }
            }
        }

        private Alarm NextAlarm()
        {
            DateTime now = CurrentTime();
            Alarm best = null;
            TimeSpan bestDelta = TimeSpan.MaxValue;
            foreach (Alarm a in _s.Alarms)
            {
                if (!a.Enabled) continue;
                for (int dd = 0; dd < 8; dd++)
                {
                    DateTime cand;
                    try { cand = new DateTime(now.Year, now.Month, now.Day, a.H, a.M, a.S).AddDays(dd); }
                    catch { continue; }
                    if (cand <= now) continue;
                    if (!a.AppliesOn(cand)) continue;
                    TimeSpan delta = cand - now;
                    if (delta < bestDelta) { bestDelta = delta; best = a; }
                    break;
                }
            }
            return best;
        }

        private void FireAlarm(Alarm a)
        {
            StartAlarmSound();
            string txt = (a.Label != null && a.Label.Length > 0) ? a.Label : (Tr.T("Alarm") + " " + a.TimeString());
            try { if (_tray != null) _tray.ShowBalloonTip(8000, Tr.T("BetaClock - Alarm"), txt, ToolTipIcon.Info); }
            catch { }
            if (_alert != null && !_alert.IsDisposed)
            {
                _alert.SetMessage(txt, a.TimeString());
            }
            else
            {
                _alert = new AlarmAlertForm(this, txt, a.TimeString());
                _alert.Show();
            }
        }

        public void StartAlarmSound()
        {
            try
            {
                if (_alarmPlayer == null)
                {
                    _alarmStream = new MemoryStream(GetWav());
                    _alarmPlayer = new SoundPlayer(_alarmStream);
                    _alarmPlayer.Load();
                }
                _alarmPlayer.PlayLooping();
            }
            catch { }
        }

        public void StopAlarm()
        {
            try { if (_alarmPlayer != null) _alarmPlayer.Stop(); }
            catch { }
        }

        public void SnoozeAlarm(string label)
        {
            DateTime t = CurrentTime().AddMinutes(5);
            Alarm a = new Alarm();
            a.H = t.Hour; a.M = t.Minute; a.S = t.Second;
            a.Repeat = 0; a.Enabled = true;
            a.Label = (label != null && label.Length > 0 ? label : Tr.T("Alarm")) + Tr.T(" (snoozed)");
            _s.Alarms.Add(a);
            _s.Save();
            AlarmsChanged();
        }

        public void AlarmsChanged()
        {
            _s.Save();
            if (_alarmMgr != null && !_alarmMgr.IsDisposed) _alarmMgr.RefreshList();
            this.Invalidate();
        }

        private void OpenAlarmManager()
        {
            if (_alarmMgr != null && !_alarmMgr.IsDisposed)
            {
                _alarmMgr.Activate();
                return;
            }
            _alarmMgr = new AlarmManagerForm(_s, this);
            _alarmMgr.Show();
        }

        private void OpenEarningsForm()
        {
            if (_earnForm != null && !_earnForm.IsDisposed)
            {
                _earnForm.Activate();
                return;
            }
            _earnForm = new EarningsForm(_s, this);
            _earnForm.Show();
        }

        public List<EarningEvent> EarningsSnapshot() { return _earnings; }
        public string EarningsStatus() { return _earnStatus; }
        public bool EarningsLoading() { return _earnLoading; }

        private void OpenAbout()
        {
            if (_aboutForm != null && !_aboutForm.IsDisposed) { _aboutForm.Activate(); return; }
            _aboutForm = new AboutForm(this);
            _aboutForm.Show();
        }

        private static byte[] GetWav()
        {
            if (_wavCache != null) return _wavCache;
            int sr = 44100;
            int[] freqs = { 988, 0, 1319, 0, 988, 0 };
            int[] durs = { 170, 70, 170, 70, 170, 650 };
            List<short> samples = new List<short>();
            for (int i = 0; i < freqs.Length; i++)
            {
                int n = sr * durs[i] / 1000;
                double att = 0.008 * sr, dec = 0.02 * sr;
                for (int k = 0; k < n; k++)
                {
                    double t = (double)k / sr;
                    double env = 1.0;
                    if (k < att) env = k / att;
                    else if (k > n - dec) env = (n - k) / dec;
                    double val = freqs[i] == 0 ? 0 : Math.Sin(2 * Math.PI * freqs[i] * t) * 0.40 * env;
                    samples.Add((short)(val * 32767));
                }
            }
            _wavCache = BuildWav(samples, sr);
            return _wavCache;
        }

        private static byte[] BuildWav(List<short> s, int sr)
        {
            int dataLen = s.Count * 2;
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataLen);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);   // PCM
                w.Write((short)1);   // mono
                w.Write(sr);
                w.Write(sr * 2);     // byte rate
                w.Write((short)2);   // block align
                w.Write((short)16);  // bits
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataLen);
                for (int i = 0; i < s.Count; i++) w.Write(s[i]);
                w.Flush();
                return ms.ToArray();
            }
        }

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

        private void DrawDayStrip(Graphics g, float x, float y, float cw, float ch, DateTime now, double phase)
        {
            int[] order = { 1, 2, 3, 4, 5, 6, 0 }; // Mon..Sun (DayOfWeek: Sun=0)
            string[] lbl = Tr.DayStrip();
            float rowH = ch / 7f;
            Color act = OnColor(0f, phase);
            using (Font f = new Font("Segoe UI", rowH * 0.64f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Alignment = StringAlignment.Near;
                for (int i = 0; i < 7; i++)
                {
                    bool cur = ((int)now.DayOfWeek == order[i]);
                    Color c = cur ? act : Color.FromArgb(58, 125, 125, 135);
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(lbl[i], f, b, new RectangleF(x, y + i * rowH, cw * 1.0f, rowH), sf);
                }
            }
        }

        private void DrawNtpDot(Graphics g, float cw)
        {
            float r = cw * 0.05f;
            float x = this.Width - r * 2 - cw * 0.18f;
            float y = this.Height - r * 2 - cw * 0.18f;
            Color c;
            if (!_s.NtpSync) c = Color.FromArgb(70, 70, 70);
            else if (_ntpOk) c = Color.FromArgb(40, 220, 90);
            else c = Color.FromArgb(230, 170, 40);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(180, c)))
                g.FillEllipse(b, x, y, r * 2, r * 2);
        }

        private void DrawDigit(Graphics g, int val, float ox, float oy, float cw, float ch, float frac, double phase)
        {
            float t = cw * 0.17f;
            float ht = t / 2f;
            float gap = cw * 0.045f;

            PointF[][] segs = new PointF[7][];
            segs[0] = HSeg(ox + ht + gap, ox + cw - ht - gap, oy + ht, ht);            // a
            segs[1] = VSeg(ox + cw - ht, oy + ht + gap, oy + ch / 2 - gap, ht);        // b
            segs[2] = VSeg(ox + cw - ht, oy + ch / 2 + gap, oy + ch - ht - gap, ht);   // c
            segs[3] = HSeg(ox + ht + gap, ox + cw - ht - gap, oy + ch - ht, ht);       // d
            segs[4] = VSeg(ox + ht, oy + ch / 2 + gap, oy + ch - ht - gap, ht);        // e
            segs[5] = VSeg(ox + ht, oy + ht + gap, oy + ch / 2 - gap, ht);             // f
            segs[6] = HSeg(ox + ht + gap, ox + cw - ht - gap, oy + ch / 2, ht);        // g

            bool[] on = (val >= 0 && val <= 9) ? DIGITS[val] : new bool[] { false, false, false, false, false, false, false };
            Color onc = OnColor(frac, phase);

            // Candlestick skin: every segment is a candle (body + wicks)
            if (_s.ColorMode == 10)
            {
                float[][] lines = new float[7][];
                lines[0] = new float[] { ox + ht + gap, oy + ht, ox + cw - ht - gap, oy + ht };
                lines[1] = new float[] { ox + cw - ht, oy + ht + gap, ox + cw - ht, oy + ch / 2 - gap };
                lines[2] = new float[] { ox + cw - ht, oy + ch / 2 + gap, ox + cw - ht, oy + ch - ht - gap };
                lines[3] = new float[] { ox + ht + gap, oy + ch - ht, ox + cw - ht - gap, oy + ch - ht };
                lines[4] = new float[] { ox + ht, oy + ch / 2 + gap, ox + ht, oy + ch - ht - gap };
                lines[5] = new float[] { ox + ht, oy + ht + gap, ox + ht, oy + ch / 2 - gap };
                lines[6] = new float[] { ox + ht + gap, oy + ch / 2, ox + cw - ht - gap, oy + ch / 2 };
                if (_s.Ghost && _s.GhostAlpha > 0)
                {
                    int gaa = _s.GhostAlpha; if (gaa > 45) gaa = 45;
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(gaa, 90, 90, 100)))
                        for (int i = 0; i < 7; i++) if (!on[i]) g.FillPolygon(gb, segs[i]);
                }
                for (int i = 0; i < 7; i++)
                    if (on[i]) DrawCandle(g, lines[i][0], lines[i][1], lines[i][2], lines[i][3], t);
                return;
            }

            if (_s.Ghost && _s.GhostAlpha > 0)
            {
                int ga = _s.GhostAlpha; if (ga > 255) ga = 255;
                Color gc = Color.FromArgb(ga, onc.R, onc.G, onc.B);
                using (SolidBrush b = new SolidBrush(gc))
                    for (int i = 0; i < 7; i++) if (!on[i]) g.FillPolygon(b, segs[i]);
            }
            if (_s.Glow)
            {
                using (Pen p = new Pen(Color.FromArgb(46, onc.R, onc.G, onc.B), ht * 0.95f))
                {
                    p.LineJoin = LineJoin.Round;
                    for (int i = 0; i < 7; i++) if (on[i]) g.DrawPolygon(p, segs[i]);
                }
            }
            using (SolidBrush b = new SolidBrush(onc))
                for (int i = 0; i < 7; i++) if (on[i]) g.FillPolygon(b, segs[i]);
        }

        // Draws a segment as a candlestick (body + wick), green/red
        // deterministic by position (so they never flicker).
        private void DrawCandle(Graphics g, float x0, float y0, float x1, float y1, float thick)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 2f) return;
            float ux = dx / len, uy = dy / len;   // lengthwise axis
            float px = -uy, py = ux;              // perpendicular axis

            int h = (int)((x0 + x1) * 7.3 + (y0 + y1) * 3.1);
            if (h < 0) h = -h;
            bool up = (h & 1) == 0;
            Color body = ApplyBri(up ? Color.FromArgb(38, 208, 100) : Color.FromArgb(236, 66, 62));

            float bodyFrac = 0.42f + ((h >> 1) & 7) / 7f * 0.30f;   // 0.42..0.72 of the length
            float off = (((h >> 4) & 7) / 7f - 0.5f) * 0.24f;       // offsets the body (uneven wicks)
            float cx = (x0 + x1) / 2f + ux * off * len;
            float cy = (y0 + y1) / 2f + uy * off * len;
            float half = len * bodyFrac / 2f;
            float bt = thick / 2f;                                  // half the body width
            float wickW = Math.Max(1.5f, thick * 0.22f);

            using (Pen wp = new Pen(body, wickW)) { wp.StartCap = LineCap.Round; wp.EndCap = LineCap.Round; g.DrawLine(wp, x0, y0, x1, y1); }

            PointF[] rect = new PointF[]
            {
                new PointF(cx - ux * half - px * bt, cy - uy * half - py * bt),
                new PointF(cx + ux * half - px * bt, cy + uy * half - py * bt),
                new PointF(cx + ux * half + px * bt, cy + uy * half + py * bt),
                new PointF(cx - ux * half + px * bt, cy - uy * half + py * bt)
            };
            using (SolidBrush bb = new SolidBrush(body)) g.FillPolygon(bb, rect);
            using (Pen bp = new Pen(Color.FromArgb(190, up ? Color.FromArgb(15, 110, 60) : Color.FromArgb(120, 25, 25)), Math.Max(1f, thick * 0.06f)))
                g.DrawPolygon(bp, rect);
        }

        // Normalized strokes (0..1) for letters A, M, P (for candlestick AM/PM)
        private static float[][] LetterStrokes(char c)
        {
            if (c == 'A') return new float[][] {
                new float[]{ 0f, 1f, 0.5f, 0f }, new float[]{ 0.5f, 0f, 1f, 1f }, new float[]{ 0.24f, 0.56f, 0.76f, 0.56f } };
            if (c == 'M') return new float[][] {
                new float[]{ 0f, 1f, 0f, 0f }, new float[]{ 0.02f, 0f, 0.5f, 0.58f }, new float[]{ 0.5f, 0.58f, 0.98f, 0f }, new float[]{ 1f, 0f, 1f, 1f } };
            if (c == 'P') return new float[][] {
                new float[]{ 0f, 1f, 0f, 0f }, new float[]{ 0f, 0f, 1f, 0f }, new float[]{ 1f, 0f, 1f, 0.52f }, new float[]{ 1f, 0.52f, 0f, 0.52f } };
            return new float[0][];
        }

        // Draws "AM"/"PM" with candles (each letter stroke is one candle)
        private void DrawCandleLabel(Graphics g, string text, float x, float y, float lw, float lh, float thick)
        {
            float gapX = lw * 0.36f;
            float cx = x;
            foreach (char c in text)
            {
                float[][] strokes = LetterStrokes(c);
                foreach (float[] s in strokes)
                    DrawCandle(g, cx + s[0] * lw, y + s[1] * lh, cx + s[2] * lw, y + s[3] * lh, thick);
                cx += lw + gapX;
            }
        }

        private void DrawColon(Graphics g, float ox, float w, float oy, float ch, float cw, float frac, double phase, bool visible)
        {
            if (!visible) return;
            Color c = OnColor(frac, phase);
            float r = cw * 0.085f;
            float cx = ox + w / 2f;
            float y1 = oy + ch * 0.34f, y2 = oy + ch * 0.66f;
            if (_s.Glow)
            {
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(46, c.R, c.G, c.B)))
                {
                    g.FillEllipse(gb, cx - r * 1.7f, y1 - r * 1.7f, r * 3.4f, r * 3.4f);
                    g.FillEllipse(gb, cx - r * 1.7f, y2 - r * 1.7f, r * 3.4f, r * 3.4f);
                }
            }
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillEllipse(b, cx - r, y1 - r, r * 2, r * 2);
                g.FillEllipse(b, cx - r, y2 - r, r * 2, r * 2);
            }
        }

        private static PointF[] HSeg(float x0, float x1, float yc, float ht)
        {
            return new PointF[]
            {
                new PointF(x0, yc),
                new PointF(x0 + ht, yc - ht),
                new PointF(x1 - ht, yc - ht),
                new PointF(x1, yc),
                new PointF(x1 - ht, yc + ht),
                new PointF(x0 + ht, yc + ht)
            };
        }
        private static PointF[] VSeg(float xc, float y0, float y1, float ht)
        {
            return new PointF[]
            {
                new PointF(xc, y0),
                new PointF(xc + ht, y0 + ht),
                new PointF(xc + ht, y1 - ht),
                new PointF(xc, y1),
                new PointF(xc - ht, y1 - ht),
                new PointF(xc - ht, y0 + ht)
            };
        }

        // ---------------------------------------------------------------------
        //  Color
        // ---------------------------------------------------------------------
        private Color OnColor(float frac, double phaseDeg)
        {
            switch (_s.ColorMode)
            {
                case 0: return ApplyBri(Color.White);
                case 1: return ApplyBri(Color.FromArgb(255, 45, 45));
                case 2: return ApplyBri(Color.FromArgb(40, 255, 70));
                case 3: return ApplyBri(Color.FromArgb(55, 130, 255));
                case 4: return ApplyBri(Color.FromArgb(0, 230, 230));
                case 5: return ApplyBri(Color.FromArgb(255, 210, 0));
                case 6: return ApplyBri(Color.FromArgb(225, 50, 220));
                case 7: return ApplyBri(Color.FromArgb(255, 120, 0));
                case 8: return ApplyBri(FromHSV(40 + frac * 290, 1, 1));
                case 9: return ApplyBri(FromHSV(40 + frac * 290 + phaseDeg, 1, 1));
                case 10: return ApplyBri(Color.FromArgb(38, 208, 100)); // candles: colon/day/date in green
                default: return ApplyBri(Color.White);
            }
        }
        private Color ApplyBri(Color c)
        {
            double b = _s.Brightness;
            return Color.FromArgb(255, Clamp255((int)(c.R * b)), Clamp255((int)(c.G * b)), Clamp255((int)(c.B * b)));
        }
        private static int Clamp255(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }
        private static Color FromHSV(double h, double s, double v)
        {
            h = h % 360; if (h < 0) h += 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r, gg, b;
            if (h < 60) { r = c; gg = x; b = 0; }
            else if (h < 120) { r = x; gg = c; b = 0; }
            else if (h < 180) { r = 0; gg = c; b = x; }
            else if (h < 240) { r = 0; gg = x; b = c; }
            else if (h < 300) { r = x; gg = 0; b = c; }
            else { r = c; gg = 0; b = x; }
            return Color.FromArgb(255, Clamp255((int)Math.Round((r + m) * 255)),
                                       Clamp255((int)Math.Round((gg + m) * 255)),
                                       Clamp255((int)Math.Round((b + m) * 255)));
        }

        // ---------------------------------------------------------------------
        //  Mouse / keyboard
        // ---------------------------------------------------------------------
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private void OnMouseDownH(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_s.Locked)
            {
                // Native Windows drag: behaves correctly across monitors with different DPI
                ReleaseCapture();
                SendMessage(this.Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero); // WM_NCLBUTTONDOWN + HTCAPTION
                _s.PosX = this.Left; _s.PosY = this.Top; _s.Save();
            }
        }
        private void OnMouseMoveH(object sender, MouseEventArgs e) { }
        private void OnMouseUpH(object sender, MouseEventArgs e) { }
        private void OnWheelH(object sender, MouseEventArgs e)
        {
            float step = e.Delta > 0 ? 6f : -6f;
            _s.Scale = Math.Max(30f, Math.Min(220f, _s.Scale + step));
            RecalcLayout(); this.Invalidate(); _s.Save();
        }
        private void OnKeyDownH(object sender, KeyEventArgs e)
        {
            int d = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Left) this.Left -= d;
            else if (e.KeyCode == Keys.Right) this.Left += d;
            else if (e.KeyCode == Keys.Up) this.Top -= d;
            else if (e.KeyCode == Keys.Down) this.Top += d;
            else return;
            _s.PosX = this.Left; _s.PosY = this.Top; _s.Save();
        }

        // ---------------------------------------------------------------------
        //  Menu (shared by right-click and tray)
        // ---------------------------------------------------------------------
        private void BuildTrayAndMenu()
        {
            _icon = MakeIcon();
            _menu = new ContextMenuStrip();
            _menu.Opening += new System.ComponentModel.CancelEventHandler(delegate (object o, System.ComponentModel.CancelEventArgs ev) { PopulateMenu(); });

            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = "BetaClock";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.MouseDoubleClick += new MouseEventHandler(delegate (object o, MouseEventArgs ev) { this.Show(); this.BringToFront(); });

            this.ContextMenuStrip = _menu;
        }

        private void PopulateMenu()
        {
            _menu.Items.Clear();

            ToolStripMenuItem head = new ToolStripMenuItem("BetaClock");
            head.Enabled = false;
            _menu.Items.Add(head);
            _menu.Items.Add(new ToolStripSeparator());

            // Color
            ToolStripMenuItem color = new ToolStripMenuItem(Tr.T("Color"));
            string[] cn = { "White", "Red", "Green", "Blue", "Cyan", "Yellow", "Purple", "Orange", "RGB gradient", "RGB animated", "Candlesticks" };
            for (int i = 0; i < cn.Length; i++)
            {
                int idx = i;
                AddCheck(color, Tr.T(cn[i]), _s.ColorMode == idx, delegate { _s.ColorMode = idx; ApplySave(); });
            }
            _menu.Items.Add(color);

            // Brightness
            ToolStripMenuItem bri = new ToolStripMenuItem(Tr.T("Brightness"));
            int[] brs = { 100, 75, 50, 30 };
            foreach (int bv in brs)
            {
                int v = bv;
                AddCheck(bri, v + "%", Math.Abs(_s.Brightness - v / 100f) < 0.01f, delegate { _s.Brightness = v / 100f; ApplySave(); });
            }
            _menu.Items.Add(bri);

            // Size
            ToolStripMenuItem size = new ToolStripMenuItem(Tr.T("Size"));
            AddCheck(size, Tr.T("Small"), Approx(_s.Scale, 48), delegate { _s.Scale = 48; ApplySave(); });
            AddCheck(size, Tr.T("Medium"), Approx(_s.Scale, 78), delegate { _s.Scale = 78; ApplySave(); });
            AddCheck(size, Tr.T("Large"), Approx(_s.Scale, 110), delegate { _s.Scale = 110; ApplySave(); });
            AddCheck(size, Tr.T("Huge"), Approx(_s.Scale, 150), delegate { _s.Scale = 150; ApplySave(); });
            ToolStripMenuItem hint = new ToolStripMenuItem(Tr.T("(or mouse wheel)")); hint.Enabled = false;
            size.DropDownItems.Add(hint);
            _menu.Items.Add(size);

            // Opacity
            ToolStripMenuItem op = new ToolStripMenuItem(Tr.T("Opacity"));
            int[] ops = { 100, 90, 75, 60, 45 };
            foreach (int ov in ops)
            {
                int v = ov;
                AddCheck(op, v + "%", _s.Opacity == v, delegate { _s.Opacity = v; ApplySave(); });
            }
            _menu.Items.Add(op);

            _menu.Items.Add(new ToolStripSeparator());

            AddCheck(_menu.Items, Tr.T("24-hour format"), _s.Format24h, delegate { _s.Format24h = !_s.Format24h; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Show seconds"), _s.ShowSeconds, delegate { _s.ShowSeconds = !_s.ShowSeconds; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Show day of week"), _s.ShowDay, delegate { _s.ShowDay = !_s.ShowDay; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Show date"), _s.ShowDate, delegate { _s.ShowDate = !_s.ShowDate; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Market session"), _s.ShowSession, delegate { _s.ShowSession = !_s.ShowSession; ApplySave(); });
            if (_s.ShowSession)
            {
                ToolStripMenuItem mk = new ToolStripMenuItem("    " + Tr.T("Market"));
                AddCheck(mk, Tr.T("USA (NYSE / options)"), _s.SessionMarket == 0, delegate { _s.SessionMarket = 0; ApplySave(); });
                AddCheck(mk, Tr.T("Forex (Sydney/Tokyo/London/NY)"), _s.SessionMarket == 1, delegate { _s.SessionMarket = 1; ApplySave(); });
                _menu.Items.Add(mk);
            }
            AddCheck(_menu.Items, Tr.T("Earnings (this week / next)"), _s.ShowEarnings, delegate { _s.ShowEarnings = !_s.ShowEarnings; ApplySave(); });
            ToolStripMenuItem earnItem = new ToolStripMenuItem("    " + Tr.T("Earnings: tickers / refresh..."));
            earnItem.Click += new EventHandler(delegate { OpenEarningsForm(); });
            _menu.Items.Add(earnItem);
            if (!_s.Format24h)
                AddCheck(_menu.Items, Tr.T("Show AM/PM"), _s.ShowAmPm, delegate { _s.ShowAmPm = !_s.ShowAmPm; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Blink the colon"), _s.Blink, delegate { _s.Blink = !_s.Blink; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Glow"), _s.Glow, delegate { _s.Glow = !_s.Glow; ApplySave(); });
            ToolStripMenuItem ghost = new ToolStripMenuItem(Tr.T("Digit shadow (background)"));
            AddCheck(ghost, Tr.T("Off"), !_s.Ghost || _s.GhostAlpha <= 0, delegate { _s.Ghost = false; ApplySave(); });
            int[] gl = { 12, 24, 45, 75, 110, 150 };
            string[] gn = { "Very faint", "Faint", "Moderate", "Strong", "Very strong", "Maximum" };
            for (int gi = 0; gi < gl.Length; gi++)
            {
                int ga = gl[gi];
                AddCheck(ghost, Tr.T(gn[gi]), _s.Ghost && _s.GhostAlpha == ga, delegate { _s.Ghost = true; _s.GhostAlpha = ga; ApplySave(); });
            }
            _menu.Items.Add(ghost);

            _menu.Items.Add(new ToolStripSeparator());

            int enAlarms = 0;
            foreach (Alarm a in _s.Alarms) if (a.Enabled) enAlarms++;
            ToolStripMenuItem alarms = new ToolStripMenuItem(enAlarms > 0
                ? string.Format(Tr.T("Alarms ({0} active)..."), enAlarms) : Tr.T("Alarms..."));
            alarms.Click += new EventHandler(delegate { OpenAlarmManager(); });
            _menu.Items.Add(alarms);

            _menu.Items.Add(new ToolStripSeparator());

            AddCheck(_menu.Items, Tr.T("Always on top"), _s.Topmost, delegate { _s.Topmost = !_s.Topmost; this.TopMost = _s.Topmost; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Lock position"), _s.Locked, delegate { _s.Locked = !_s.Locked; ApplySave(); });
            AddCheck(_menu.Items, Tr.T("Start with Windows"), IsAutostart(), delegate { SetAutostart(!IsAutostart()); PopulateMenu(); });

            _menu.Items.Add(new ToolStripSeparator());

            // NTP
            long offt = Interlocked.Read(ref _ntpOffsetTicks);
            double ms = new TimeSpan(offt).TotalMilliseconds;
            string ntpInfo;
            if (!_s.NtpSync) ntpInfo = Tr.T("NTP: off");
            else if (_ntpOk) ntpInfo = string.Format(CultureInfo.InvariantCulture, Tr.T("NTP: PC {0}{1} ms vs internet"),
                                          (ms <= 0 ? "" : "+"), Math.Round(-ms)); // how far the PC clock runs fast/slow
            else ntpInfo = Tr.T("NTP: offline (using PC time)");
            ToolStripMenuItem info = new ToolStripMenuItem(ntpInfo); info.Enabled = false;
            _menu.Items.Add(info);
            AddCheck(_menu.Items, Tr.T("Sync time over internet"), _s.NtpSync, delegate { _s.NtpSync = !_s.NtpSync; _ntpWake.Set(); ApplySave(); });
            ToolStripMenuItem verify = new ToolStripMenuItem(Tr.T("Check time now"));
            verify.Click += new EventHandler(delegate { _ntpWake.Set(); });
            _menu.Items.Add(verify);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem about = new ToolStripMenuItem(Tr.T("About BetaClock..."));
            about.Click += new EventHandler(delegate { OpenAbout(); });
            _menu.Items.Add(about);

            // Language
            ToolStripMenuItem langMenu = new ToolStripMenuItem(Tr.T("Language / Idioma"));
            for (int li = 0; li < Tr.Names.Length; li++)
            {
                int idx = li;
                AddCheck(langMenu, Tr.Names[li], Tr.Lang == idx, delegate { _s.Lang = Tr.Codes[idx]; Tr.SetByCode(_s.Lang); ApplySave(); });
            }
            _menu.Items.Add(langMenu);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem reset = new ToolStripMenuItem(Tr.T("Center on screen"));
            reset.Click += new EventHandler(delegate { _s.PosX = -1; _s.PosY = -1; PlaceWindow(); _s.PosX = this.Left; _s.PosY = this.Top; _s.Save(); });
            _menu.Items.Add(reset);

            ToolStripMenuItem exit = new ToolStripMenuItem(Tr.T("Exit"));
            exit.Click += new EventHandler(delegate { this.Close(); });
            _menu.Items.Add(exit);
        }

        private void AddCheck(ToolStripMenuItem parent, string text, bool chk, EventHandler onClick)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Checked = chk;
            it.Click += onClick;
            parent.DropDownItems.Add(it);
        }
        private void AddCheck(ToolStripItemCollection parent, string text, bool chk, EventHandler onClick)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Checked = chk;
            it.Click += onClick;
            parent.Add(it);
        }
        private static bool Approx(float a, float b) { return Math.Abs(a - b) < 0.5f; }

        private void ApplySave()
        {
            RecalcLayout();
            this.Invalidate();
            _s.Save();
            RequestMenuRebuild();
        }

        // Rebuilds the menu after a change (language/settings). The Opening event does not
        // fire reliably on this overlay window, so we force it.
        // Coalesced via BeginInvoke to avoid redundant rebuilds (e.g. mouse wheel).
        private bool _menuRebuildPending = false;
        private void RequestMenuRebuild()
        {
            if (_menuRebuildPending || _menu == null) return;
            _menuRebuildPending = true;
            try { this.BeginInvoke((MethodInvoker)delegate { _menuRebuildPending = false; try { PopulateMenu(); } catch { } }); }
            catch { _menuRebuildPending = false; }
        }

        // ---------------------------------------------------------------------
        //  Start with Windows
        // ---------------------------------------------------------------------
        private bool IsAutostart()
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY);
                if (k == null) return false;
                object v = k.GetValue("BetaClock");
                k.Close();
                return v != null;
            }
            catch { return false; }
        }
        private void SetAutostart(bool on)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true);
                if (k == null) k = Registry.CurrentUser.CreateSubKey(RUN_KEY);
                if (on) k.SetValue("BetaClock", "\"" + Application.ExecutablePath + "\"");
                else if (k.GetValue("BetaClock") != null) k.DeleteValue("BetaClock", false);
                k.Close();
            }
            catch { }
        }

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

        // ---------------------------------------------------------------------
        //  Generated tray icon
        // ---------------------------------------------------------------------
        private Icon MakeIcon()
        {
            Bitmap bm = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bm))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(18, 18, 22)))
                    g.FillEllipse(b, 1, 1, 30, 30);
                using (SolidBrush c = new SolidBrush(Color.FromArgb(0, 230, 230)))
                {
                    g.FillEllipse(c, 14, 9, 4, 4);
                    g.FillEllipse(c, 14, 19, 4, 4);
                }
                using (Pen p = new Pen(Color.FromArgb(40, 255, 70), 2))
                {
                    g.DrawLine(p, 7, 8, 7, 24);   // a green "1"
                    g.DrawLine(p, 24, 8, 24, 24); // another stroke
                }
            }
            return Icon.FromHandle(bm.GetHicon());
        }

        // ---------------------------------------------------------------------
        //  Shutdown
        // ---------------------------------------------------------------------
        private void OnClosingH(object sender, FormClosingEventArgs e)
        {
            try
            {
                _ntpStop = true; _ntpWake.Set();
                _earnStop = true; _earnWake.Set();
                if (_timer != null) _timer.Stop();
                StopAlarm();
                if (_alarmPlayer != null) _alarmPlayer.Dispose();
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            }
            catch { }
            _s.PosX = this.Left; _s.PosY = this.Top; _s.Save();
        }
    }

    // -------------------------------------------------------------------------
    //  Alarm manager (add / edit / delete)
    // -------------------------------------------------------------------------
    public class AlarmManagerForm : Form
    {
        private Settings _s;
        private ClockForm _owner;
        private ListBox _list;
        private NumericUpDown _nH, _nM, _nS;
        private ComboBox _cmb;
        private TextBox _txt;
        private CheckBox _chk;
        private System.Windows.Forms.Timer _testTimer;
        private static readonly int[] RepMap = { 1, 0, 2 }; // combo index -> Repeat

        public AlarmManagerForm(Settings s, ClockForm owner)
        {
            _s = s; _owner = owner;
            this.Text = Tr.T("BetaClock - Alarms");
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false; this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(478, 500);
            this.BackColor = Color.FromArgb(28, 28, 32);
            this.ForeColor = Color.White;
            this.AutoScaleMode = AutoScaleMode.None; // fixed size in px (avoids DPI clipping)
            this.Font = new Font("Segoe UI", 14f, GraphicsUnit.Pixel);
            Build();
            RefreshList();
        }

        private Label Lbl(string t, int x, int y, int w)
        {
            Label l = new Label(); l.Text = t; l.ForeColor = Color.White;
            l.Location = new Point(x, y); l.AutoSize = true; // grows with the text -> never clipped
            this.Controls.Add(l); return l;
        }
        private Button Btn(string t, int x, int y, int w, EventHandler h)
        {
            Button b = new Button(); b.Text = t; b.Location = new Point(x, y); b.Size = new Size(w, 30);
            b.FlatStyle = FlatStyle.Flat; b.ForeColor = Color.White;
            b.BackColor = Color.FromArgb(55, 55, 62);
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 100);
            b.Click += h; this.Controls.Add(b); return b;
        }
        private NumericUpDown NUD(int x, int y, int max)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y); n.Size = new Size(46, 24);
            n.Minimum = 0; n.Maximum = max;
            n.BackColor = Color.FromArgb(18, 18, 22); n.ForeColor = Color.White;
            n.BorderStyle = BorderStyle.FixedSingle; n.TextAlign = HorizontalAlignment.Center;
            this.Controls.Add(n); return n;
        }

        private void Build()
        {
            Lbl(Tr.T("Configured alarms:"), 12, 8, 300);
            _list = new ListBox();
            _list.Location = new Point(12, 30); _list.Size = new Size(446, 150);
            _list.BackColor = Color.FromArgb(18, 18, 22); _list.ForeColor = Color.White;
            _list.Font = new Font("Consolas", 14f, GraphicsUnit.Pixel);
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.SelectedIndexChanged += new EventHandler(OnSelect);
            this.Controls.Add(_list);

            Lbl(Tr.T("Time:"), 12, 196, 46);
            _nH = NUD(58, 193, 23);
            Lbl(":", 108, 196, 8);
            _nM = NUD(118, 193, 59);
            Lbl(":", 168, 196, 8);
            _nS = NUD(178, 193, 59);

            Lbl(Tr.T("Repeat:"), 246, 196, 56);
            _cmb = new ComboBox(); _cmb.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmb.Location = new Point(306, 193); _cmb.Size = new Size(152, 24);
            _cmb.Items.AddRange(new object[] { Tr.T("Daily"), Tr.T("Once"), Tr.T("Mon-Fri (market)") });
            _cmb.SelectedIndex = 0; this.Controls.Add(_cmb);

            Lbl(Tr.T("Label:"), 12, 232, 64);
            _txt = new TextBox(); _txt.Location = new Point(78, 230); _txt.Size = new Size(380, 24);
            _txt.BackColor = Color.FromArgb(18, 18, 22); _txt.ForeColor = Color.White;
            _txt.BorderStyle = BorderStyle.FixedSingle; this.Controls.Add(_txt);

            _chk = new CheckBox(); _chk.Text = Tr.T("Active"); _chk.Location = new Point(78, 260);
            _chk.Size = new Size(120, 22); _chk.ForeColor = Color.White; _chk.Checked = true;
            this.Controls.Add(_chk);

            Btn(Tr.T("Add new"), 12, 292, 140, new EventHandler(OnAdd));
            Btn(Tr.T("Update sel."), 160, 292, 140, new EventHandler(OnUpdate));
            Btn(Tr.T("Delete sel."), 308, 292, 150, new EventHandler(OnDelete));

            Btn(Tr.T("Enable / Disable sel."), 12, 328, 215, new EventHandler(OnToggle));
            Btn(Tr.T("Test sound"), 235, 328, 223, new EventHandler(OnTest));

            Lbl(Tr.T("Quick presets (clock local time):"), 12, 368, 446);
            Btn(Tr.T("+ Open 09:30 (M-F)"), 12, 390, 215, new EventHandler(delegate { AddPreset(9, 30, 0, 2, Tr.T("Market open")); }));
            Btn(Tr.T("+ Close 16:00 (M-F)"), 235, 390, 223, new EventHandler(delegate { AddPreset(16, 0, 0, 2, Tr.T("Market close")); }));

            Btn(Tr.T("Close"), 308, 430, 150, new EventHandler(delegate { this.Close(); }));
        }

        public void RefreshList()
        {
            int sel = _list.SelectedIndex;
            _list.BeginUpdate(); _list.Items.Clear();
            foreach (Alarm a in _s.Alarms)
                _list.Items.Add(string.Format("{0}  {1,-18}  {2}  {3}",
                    a.TimeString(), Tr.T(a.RepeatString()), a.Enabled ? "ON " : "off",
                    a.Label == null ? "" : a.Label));
            _list.EndUpdate();
            if (sel >= 0 && sel < _list.Items.Count) _list.SelectedIndex = sel;
        }

        private void OnSelect(object o, EventArgs e)
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _s.Alarms.Count) return;
            Alarm a = _s.Alarms[i];
            _nH.Value = a.H; _nM.Value = a.M; _nS.Value = a.S;
            _cmb.SelectedIndex = RepToIdx(a.Repeat);
            _txt.Text = a.Label; _chk.Checked = a.Enabled;
        }

        private Alarm FromFields()
        {
            Alarm a = new Alarm();
            a.H = (int)_nH.Value; a.M = (int)_nM.Value; a.S = (int)_nS.Value;
            a.Repeat = RepMap[_cmb.SelectedIndex < 0 ? 0 : _cmb.SelectedIndex];
            a.Label = _txt.Text == null ? "" : _txt.Text.Trim();
            a.Enabled = _chk.Checked;
            return a;
        }

        private void OnAdd(object o, EventArgs e)
        {
            _s.Alarms.Add(FromFields());
            _owner.AlarmsChanged(); RefreshList();
            if (_list.Items.Count > 0) _list.SelectedIndex = _list.Items.Count - 1;
        }
        private void OnUpdate(object o, EventArgs e)
        {
            int i = _list.SelectedIndex; if (i < 0 || i >= _s.Alarms.Count) return;
            Alarm a = FromFields(); a.LastFire = DateTime.MinValue;
            _s.Alarms[i] = a; _owner.AlarmsChanged(); RefreshList(); _list.SelectedIndex = i;
        }
        private void OnDelete(object o, EventArgs e)
        {
            int i = _list.SelectedIndex; if (i < 0 || i >= _s.Alarms.Count) return;
            _s.Alarms.RemoveAt(i); _owner.AlarmsChanged(); RefreshList();
        }
        private void OnToggle(object o, EventArgs e)
        {
            int i = _list.SelectedIndex; if (i < 0 || i >= _s.Alarms.Count) return;
            _s.Alarms[i].Enabled = !_s.Alarms[i].Enabled;
            _s.Alarms[i].LastFire = DateTime.MinValue;
            _owner.AlarmsChanged(); RefreshList(); _list.SelectedIndex = i;
        }
        private void OnTest(object o, EventArgs e)
        {
            _owner.StartAlarmSound();
            if (_testTimer == null)
            {
                _testTimer = new System.Windows.Forms.Timer();
                _testTimer.Interval = 3500;
                _testTimer.Tick += new EventHandler(delegate { _owner.StopAlarm(); _testTimer.Stop(); });
            }
            _testTimer.Stop(); _testTimer.Start();
        }
        private void AddPreset(int h, int m, int s, int rep, string label)
        {
            Alarm a = new Alarm(); a.H = h; a.M = m; a.S = s; a.Repeat = rep; a.Label = label; a.Enabled = true;
            _s.Alarms.Add(a); _owner.AlarmsChanged(); RefreshList();
            if (_list.Items.Count > 0) _list.SelectedIndex = _list.Items.Count - 1;
        }
        private static int RepToIdx(int rep) { for (int i = 0; i < RepMap.Length; i++) if (RepMap[i] == rep) return i; return 0; }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_testTimer != null) { _testTimer.Stop(); _owner.StopAlarm(); }
        }
    }

    // -------------------------------------------------------------------------
    //  Alarm alert (sound + flashing, with Dismiss / Snooze)
    // -------------------------------------------------------------------------
    public class AlarmAlertForm : Form
    {
        private ClockForm _owner;
        private string _msg;
        private Label _lblTitle, _lblTime, _lblMsg;
        private System.Windows.Forms.Timer _flash, _auto;
        private bool _on = true;
        private bool _drag; private Point _dragOff;

        public AlarmAlertForm(ClockForm owner, string msg, string timeStr)
        {
            _owner = owner; _msg = msg;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ClientSize = new Size(430, 212);
            this.BackColor = Color.FromArgb(24, 24, 28);
            this.TopMost = true;
            this.ShowInTaskbar = true;
            this.Text = Tr.T("BetaClock - Alarm");
            this.AutoScaleMode = AutoScaleMode.None;
            this.Font = new Font("Segoe UI", 15f, GraphicsUnit.Pixel);

            _lblTitle = new Label();
            _lblTitle.Text = Tr.T("ALARM"); _lblTitle.Font = new Font("Segoe UI", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lblTitle.ForeColor = Color.FromArgb(255, 170, 40);
            _lblTitle.Location = new Point(20, 14); _lblTitle.AutoSize = true;
            _lblTitle.BackColor = Color.Transparent;
            this.Controls.Add(_lblTitle);

            _lblTime = new Label();
            _lblTime.Text = timeStr; _lblTime.Font = new Font("Consolas", 20f, FontStyle.Bold, GraphicsUnit.Pixel);
            _lblTime.ForeColor = Color.White; _lblTime.Location = new Point(270, 26);
            _lblTime.AutoSize = true; _lblTime.BackColor = Color.Transparent;
            this.Controls.Add(_lblTime);

            _lblMsg = new Label();
            _lblMsg.Text = msg; _lblMsg.Font = new Font("Segoe UI", 17f, GraphicsUnit.Pixel);
            _lblMsg.ForeColor = Color.White; _lblMsg.Location = new Point(22, 76);
            _lblMsg.Size = new Size(390, 56); _lblMsg.BackColor = Color.Transparent;
            this.Controls.Add(_lblMsg);

            Button b1 = new Button();
            b1.Text = Tr.T("Dismiss"); b1.Size = new Size(186, 46); b1.Location = new Point(20, 150);
            b1.FlatStyle = FlatStyle.Flat; b1.ForeColor = Color.White; b1.BackColor = Color.FromArgb(60, 60, 68);
            b1.Font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            b1.Click += new EventHandler(delegate { Dismiss(); }); this.Controls.Add(b1);

            Button b2 = new Button();
            b2.Text = Tr.T("Snooze 5 min"); b2.Size = new Size(204, 46); b2.Location = new Point(214, 150);
            b2.FlatStyle = FlatStyle.Flat; b2.ForeColor = Color.White; b2.BackColor = Color.FromArgb(45, 90, 140);
            b2.Font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            b2.Click += new EventHandler(delegate { Snooze(); }); this.Controls.Add(b2);

            this.MouseDown += new MouseEventHandler(delegate (object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _drag = true; _dragOff = new Point(e.X, e.Y); } });
            this.MouseMove += new MouseEventHandler(delegate (object o, MouseEventArgs e) { if (_drag) { Point p = Control.MousePosition; this.Location = new Point(p.X - _dragOff.X, p.Y - _dragOff.Y); } });
            this.MouseUp += new MouseEventHandler(delegate (object o, MouseEventArgs e) { _drag = false; });

            PlaceNearClock();

            _flash = new System.Windows.Forms.Timer(); _flash.Interval = 550;
            _flash.Tick += new EventHandler(Flash); _flash.Start();
            _auto = new System.Windows.Forms.Timer(); _auto.Interval = 90000;
            _auto.Tick += new EventHandler(delegate { Dismiss(); }); _auto.Start();
        }

        public void SetMessage(string msg, string timeStr)
        {
            _msg = msg; _lblMsg.Text = msg; _lblTime.Text = timeStr;
        }

        private void PlaceNearClock()
        {
            Rectangle wa;
            try { wa = Screen.FromControl(_owner).WorkingArea; }
            catch { wa = Screen.PrimaryScreen.WorkingArea; }
            this.Location = new Point(wa.X + (wa.Width - this.Width) / 2, wa.Y + (wa.Height - this.Height) / 2);
        }

        private void Flash(object o, EventArgs e)
        {
            _on = !_on;
            this.BackColor = _on ? Color.FromArgb(44, 24, 24) : Color.FromArgb(24, 24, 28);
            _lblTitle.ForeColor = _on ? Color.FromArgb(255, 90, 60) : Color.FromArgb(255, 170, 40);
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(_on ? Color.FromArgb(255, 90, 60) : Color.FromArgb(255, 170, 40), 3))
                e.Graphics.DrawRectangle(p, 1, 1, this.Width - 3, this.Height - 3);
        }

        private void Dismiss() { _owner.StopAlarm(); Cleanup(); this.Close(); }
        private void Snooze() { _owner.SnoozeAlarm(_msg); _owner.StopAlarm(); Cleanup(); this.Close(); }
        private void Cleanup() { if (_flash != null) _flash.Stop(); if (_auto != null) _auto.Stop(); }

        protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); Cleanup(); }
        protected override bool ShowWithoutActivation { get { return false; } }
    }

    // -------------------------------------------------------------------------
    //  Earnings panel (edit tickers + view calendar)
    // -------------------------------------------------------------------------
    public class EarningsForm : Form
    {
        private Settings _s;
        private ClockForm _owner;
        private TextBox _txt;
        private ListBox _list;
        private Label _status;
        private System.Windows.Forms.Timer _tick;

        public EarningsForm(Settings s, ClockForm owner)
        {
            _s = s; _owner = owner;
            this.Text = Tr.T("BetaClock - Earnings");
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false; this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(520, 524);
            this.BackColor = Color.FromArgb(28, 28, 32);
            this.ForeColor = Color.White;
            this.AutoScaleMode = AutoScaleMode.None;
            this.Font = new Font("Segoe UI", 14f, GraphicsUnit.Pixel);
            Build();
            RefreshData();

            _tick = new System.Windows.Forms.Timer();
            _tick.Interval = 1000;
            _tick.Tick += new EventHandler(delegate { _status.Text = Tr.T(_owner.EarningsStatus()); });
            _tick.Start();
        }

        private Label Lbl(string t, int x, int y)
        {
            Label l = new Label(); l.Text = t; l.ForeColor = Color.White;
            l.Location = new Point(x, y); l.AutoSize = true; this.Controls.Add(l); return l;
        }
        private Button Btn(string t, int x, int y, int w, EventHandler h)
        {
            Button b = new Button(); b.Text = t; b.Location = new Point(x, y); b.Size = new Size(w, 32);
            b.FlatStyle = FlatStyle.Flat; b.ForeColor = Color.White; b.BackColor = Color.FromArgb(55, 55, 62);
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 100); b.Click += h; this.Controls.Add(b); return b;
        }

        private void Build()
        {
            Lbl(Tr.T("Your tickers (comma, space or newline separated):"), 12, 8);
            _txt = new TextBox();
            _txt.Multiline = true; _txt.ScrollBars = ScrollBars.Vertical;
            _txt.Location = new Point(12, 32); _txt.Size = new Size(496, 110);
            _txt.BackColor = Color.FromArgb(18, 18, 22); _txt.ForeColor = Color.White;
            _txt.BorderStyle = BorderStyle.FixedSingle;
            _txt.Font = new Font("Consolas", 13f, GraphicsUnit.Pixel);
            _txt.Text = _s.EarningsTickers == null ? "" : _s.EarningsTickers;
            this.Controls.Add(_txt);

            Btn(Tr.T("Save tickers & refresh"), 12, 150, 250, new EventHandler(OnSave));
            Btn(Tr.T("Refresh now"), 272, 150, 150, new EventHandler(OnRefresh));

            _status = Lbl("", 12, 192);
            _status.ForeColor = Color.FromArgb(120, 210, 120);

            Lbl(Tr.T("Upcoming earnings from your list:"), 12, 220);
            _list = new ListBox();
            _list.Location = new Point(12, 244); _list.Size = new Size(496, 232);
            _list.BackColor = Color.FromArgb(18, 18, 22); _list.ForeColor = Color.White;
            _list.Font = new Font("Consolas", 14f, GraphicsUnit.Pixel);
            _list.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(_list);

            Btn(Tr.T("Close"), 358, 484, 150, new EventHandler(delegate { this.Close(); }));
        }

        private void OnSave(object o, EventArgs e)
        {
            // normalize to a single comma-separated line (avoids breaking settings.cfg)
            string raw = _txt.Text == null ? "" : _txt.Text;
            string[] toks = raw.Split(new char[] { ',', ' ', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> clean = new List<string>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string t in toks)
            {
                string u = t.Trim().ToUpperInvariant();
                if (u.Length > 0 && seen.Add(u)) clean.Add(u);
            }
            _s.EarningsTickers = string.Join(",", clean.ToArray());
            _txt.Text = _s.EarningsTickers;
            _s.Save();
            _owner.TriggerEarningsRefresh();
            _status.Text = string.Format(Tr.T("Saved ({0} tickers). Updating..."), clean.Count);
        }
        private void OnRefresh(object o, EventArgs e)
        {
            _owner.TriggerEarningsRefresh();
            _status.Text = Tr.T("Updating...");
        }

        private static string ShortDow(DateTime d) { return Tr.DowShort((int)d.DayOfWeek); }
        private static string ShortDate(DateTime d) { return string.Format("{0:00}-{1}", d.Day, Tr.MonAbbr(d.Month)); }

        public void RefreshData()
        {
            _status.Text = Tr.T(_owner.EarningsStatus());
            List<EarningEvent> ev = _owner.EarningsSnapshot();
            _list.BeginUpdate(); _list.Items.Clear();
            if (ev != null)
            {
                foreach (EarningEvent x in ev)
                {
                    string wn = x.When.Length > 0 ? x.When : "—";
                    _list.Items.Add(string.Format("{0} ({1})   {2,-6}  {3}", ShortDate(x.Date), ShortDow(x.Date), x.Symbol, wn));
                }
            }
            if (_list.Items.Count == 0) _list.Items.Add(Tr.T("(no data yet — click \"Refresh now\")"));
            _list.EndUpdate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (_tick != null) _tick.Stop();
        }
    }

    // -------------------------------------------------------------------------
    //  About (branding)
    // -------------------------------------------------------------------------
    public class AboutForm : Form
    {
        private ClockForm _owner;
        private static readonly Color Teal = Color.FromArgb(44, 224, 230);

        public AboutForm(ClockForm owner)
        {
            _owner = owner;
            this.Text = Tr.T("About BetaClock...");
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false; this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.Manual;
            this.ClientSize = new Size(440, 250);
            this.BackColor = Color.FromArgb(18, 18, 24);
            this.ForeColor = Color.White;
            this.AutoScaleMode = AutoScaleMode.None;
            this.Font = new Font("Segoe UI", 14f, GraphicsUnit.Pixel);
            this.DoubleBuffered = true;

            Button close = new Button();
            close.Text = Tr.T("Close"); close.Size = new Size(120, 32); close.Location = new Point(440 - 120 - 22, 204);
            close.FlatStyle = FlatStyle.Flat; close.ForeColor = Color.White; close.BackColor = Color.FromArgb(50, 50, 58);
            close.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 100);
            close.Click += new EventHandler(delegate { this.Close(); });
            this.Controls.Add(close);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int px = wa.X + (wa.Width - this.Width) / 2;
            int py = wa.Y + (wa.Height - this.Height) / 2;
            if (px + this.Width > wa.Right) px = wa.Right - this.Width;
            if (py + this.Height > wa.Bottom) py = wa.Bottom - this.Height;
            if (px < wa.Left) px = wa.Left;
            if (py < wa.Top) py = wa.Top;
            this.Location = new Point(px, py);
        }

        private static void Hand(Graphics g, float cx, float cy, float r, double angDeg, float len, float w, Color c)
        {
            double a = angDeg * Math.PI / 180.0;
            float ex = cx + (float)Math.Sin(a) * r * len;
            float ey = cy - (float)Math.Cos(a) * r * len;
            using (Pen p = new Pen(c, w)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, cx, cy, ex, ey); }
        }
        private void CenterText(Graphics g, string t, float px, FontStyle st, Color c, float y)
        {
            using (Font f = new Font("Segoe UI", px, st, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(c))
            {
                SizeF sz = g.MeasureString(t, f);
                g.DrawString(t, f, b, (this.ClientSize.Width - sz.Width) / 2f, y);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.BackColor);
            float cx = this.ClientSize.Width / 2f;

            // Clock (logo)
            float gy = 60, r = 38;
            using (Pen ring = new Pen(Teal, 6f)) g.DrawEllipse(ring, cx - r, gy - r, r * 2, r * 2);
            Color[] tc = { Color.FromArgb(255, 80, 80), Color.FromArgb(80, 200, 255), Color.FromArgb(120, 255, 120), Color.FromArgb(255, 210, 60) };
            int[] angs = { 0, 90, 180, 270 };
            for (int k = 0; k < 4; k++)
            {
                double a = angs[k] * Math.PI / 180.0;
                using (Pen tp = new Pen(tc[k], 3f))
                {
                    tp.StartCap = LineCap.Round; tp.EndCap = LineCap.Round;
                    g.DrawLine(tp, cx + (float)Math.Sin(a) * r * 0.82f, gy - (float)Math.Cos(a) * r * 0.82f,
                                   cx + (float)Math.Sin(a) * r * 0.97f, gy - (float)Math.Cos(a) * r * 0.97f);
                }
            }
            Hand(g, cx, gy, r, 300, 0.5f, 5f, Color.FromArgb(255, 150, 40));
            Hand(g, cx, gy, r, 60, 0.78f, 3.5f, Color.FromArgb(240, 240, 245));
            using (SolidBrush cd = new SolidBrush(Color.FromArgb(240, 240, 245))) g.FillEllipse(cd, cx - 3.5f, gy - 3.5f, 7, 7);

            CenterText(g, "BetaClock", 30f, FontStyle.Bold, Teal, 112);
            CenterText(g, Tr.T("Trading clock · always on top · v1.0"), 13f, FontStyle.Regular, Color.FromArgb(165, 165, 178), 148);

            using (Pen dv = new Pen(Color.FromArgb(48, 48, 58), 1f)) g.DrawLine(dv, 30, 186, this.ClientSize.Width - 30, 186);
        }
    }

    // -------------------------------------------------------------------------
    //  Entry point
    // -------------------------------------------------------------------------
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        private static Mutex _mtx;

        [STAThread]
        static void Main(string[] args)
        {
            bool openAlarms = false, openAbout = false;
            if (args != null)
                foreach (string a in args)
                {
                    if (a == null) continue;
                    string al = a.ToLowerInvariant();
                    if (al.Contains("alarm")) openAlarms = true;
                    if (al.Contains("about")) openAbout = true;
                }

            bool created;
            _mtx = new Mutex(true, "BetaClock_SingleInstance", out created);
            if (!created) return; // another instance is already running

            // DPI: Per-Monitor V2 -> consistent physical coordinates across monitors
            // with different scaling (keeps the clock from "flying" off-screen while dragging).
            try
            {
                if (!SetProcessDpiAwarenessContext((IntPtr)(-4)))
                {
                    try { SetProcessDpiAwareness(2); }
                    catch { try { SetProcessDPIAware(); } catch { } }
                }
            }
            catch
            {
                try { SetProcessDpiAwareness(2); }
                catch { try { SetProcessDPIAware(); } catch { } }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ClockForm(openAlarms, openAbout));
            GC.KeepAlive(_mtx);
        }
    }
}
