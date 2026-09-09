// ============================================================================
//  BetaClock - Earnings panel dialog
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
}
