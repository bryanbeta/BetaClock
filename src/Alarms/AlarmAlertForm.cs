// ============================================================================
//  BetaClock - Alarm alert popup
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
}
