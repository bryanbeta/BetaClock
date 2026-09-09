// ============================================================================
//  BetaClock - About dialog
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
}
