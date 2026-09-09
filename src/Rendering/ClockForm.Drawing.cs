// ============================================================================
//  BetaClock - Painting the clock face and color modes
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
    }
}
