// ============================================================================
//  BetaClock - Context menu shared by right-click and tray
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
    }
}
