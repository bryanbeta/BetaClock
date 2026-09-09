// ============================================================================
//  BetaClock - Low-level 7-segment and candlestick drawing
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
    }
}
