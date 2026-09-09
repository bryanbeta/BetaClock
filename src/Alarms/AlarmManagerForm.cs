// ============================================================================
//  BetaClock - Alarm manager dialog
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
}
