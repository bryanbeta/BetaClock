// ============================================================================
//  BetaClock - Window placement, sizing, mouse and keyboard
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
    }
}
