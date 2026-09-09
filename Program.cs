// ============================================================================
//  BetaClock - Entry point, single instance and DPI
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
    //  Entry point
    // -------------------------------------------------------------------------
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        private static Mutex _mtx;

        [STAThread]
        static void Main(string[] args)
        {
            bool openAlarms = false, openAbout = false;
            if (args != null)
                foreach (string a in args)
                {
                    if (a == null) continue;
                    string al = a.ToLowerInvariant();
                    if (al.Contains("alarm")) openAlarms = true;
                    if (al.Contains("about")) openAbout = true;
                }

            bool created;
            _mtx = new Mutex(true, "BetaClock_SingleInstance", out created);
            if (!created) return; // another instance is already running

            // DPI: Per-Monitor V2 -> consistent physical coordinates across monitors
            // with different scaling (keeps the clock from "flying" off-screen while dragging).
            try
            {
                if (!SetProcessDpiAwarenessContext((IntPtr)(-4)))
                {
                    try { SetProcessDpiAwareness(2); }
                    catch { try { SetProcessDPIAware(); } catch { } }
                }
            }
            catch
            {
                try { SetProcessDpiAwareness(2); }
                catch { try { SetProcessDPIAware(); } catch { } }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ClockForm(openAlarms, openAbout));
            GC.KeepAlive(_mtx);
        }
    }
}
