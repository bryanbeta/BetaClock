// ============================================================================
//  BetaClock - Main window: state, startup and current time
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
    //  Main clock window
    // -------------------------------------------------------------------------
    public partial class ClockForm : Form
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
    }
}
