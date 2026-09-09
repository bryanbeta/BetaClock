// ============================================================================
//  BetaClock - Start with Windows, tray icon and shutdown
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
}
