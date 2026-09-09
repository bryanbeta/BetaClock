// ============================================================================
//  BetaClock - Alarm scheduling, sound and firing
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
        //  Alarms
        // ---------------------------------------------------------------------
        private void OnTick(object sender, EventArgs e)
        {
            int sec = DateTime.Now.Second;
            if (sec != _lastSec)
            {
                _lastSec = sec;
                bool need = false;
                if (_s.ShowSession) // detects a state/notice change (not every second because of the digits)
                {
                    Color c; string st = SessionText(out c);
                    if (NormalizeDigits(st) != _sessionStateKey || BuildNotice() != _noticeText) need = true;
                }
                if (_s.ShowEarnings && EarnKey() != _earnKey) need = true;
                if (need) RecalcLayout();
            }
            CheckAlarms();
            this.Invalidate();
        }

        private void CheckAlarms()
        {
            if (_s.Alarms.Count == 0) return;
            DateTime now = CurrentTime();
            foreach (Alarm a in _s.Alarms)
            {
                if (!a.Enabled) continue;
                if (!a.AppliesOn(now)) continue;
                DateTime target;
                try { target = new DateTime(now.Year, now.Month, now.Day, a.H, a.M, a.S); }
                catch { continue; }
                double diff = (now - target).TotalSeconds;
                if (diff >= 0 && diff < 30 && a.LastFire < target)
                {
                    a.LastFire = target;
                    if (a.Repeat == 0) a.Enabled = false; // once
                    _s.Save();
                    FireAlarm(a);
                }
            }
        }

        private Alarm NextAlarm()
        {
            DateTime now = CurrentTime();
            Alarm best = null;
            TimeSpan bestDelta = TimeSpan.MaxValue;
            foreach (Alarm a in _s.Alarms)
            {
                if (!a.Enabled) continue;
                for (int dd = 0; dd < 8; dd++)
                {
                    DateTime cand;
                    try { cand = new DateTime(now.Year, now.Month, now.Day, a.H, a.M, a.S).AddDays(dd); }
                    catch { continue; }
                    if (cand <= now) continue;
                    if (!a.AppliesOn(cand)) continue;
                    TimeSpan delta = cand - now;
                    if (delta < bestDelta) { bestDelta = delta; best = a; }
                    break;
                }
            }
            return best;
        }

        private void FireAlarm(Alarm a)
        {
            StartAlarmSound();
            string txt = (a.Label != null && a.Label.Length > 0) ? a.Label : (Tr.T("Alarm") + " " + a.TimeString());
            try { if (_tray != null) _tray.ShowBalloonTip(8000, Tr.T("BetaClock - Alarm"), txt, ToolTipIcon.Info); }
            catch { }
            if (_alert != null && !_alert.IsDisposed)
            {
                _alert.SetMessage(txt, a.TimeString());
            }
            else
            {
                _alert = new AlarmAlertForm(this, txt, a.TimeString());
                _alert.Show();
            }
        }

        public void StartAlarmSound()
        {
            try
            {
                if (_alarmPlayer == null)
                {
                    _alarmStream = new MemoryStream(GetWav());
                    _alarmPlayer = new SoundPlayer(_alarmStream);
                    _alarmPlayer.Load();
                }
                _alarmPlayer.PlayLooping();
            }
            catch { }
        }

        public void StopAlarm()
        {
            try { if (_alarmPlayer != null) _alarmPlayer.Stop(); }
            catch { }
        }

        public void SnoozeAlarm(string label)
        {
            DateTime t = CurrentTime().AddMinutes(5);
            Alarm a = new Alarm();
            a.H = t.Hour; a.M = t.Minute; a.S = t.Second;
            a.Repeat = 0; a.Enabled = true;
            a.Label = (label != null && label.Length > 0 ? label : Tr.T("Alarm")) + Tr.T(" (snoozed)");
            _s.Alarms.Add(a);
            _s.Save();
            AlarmsChanged();
        }

        public void AlarmsChanged()
        {
            _s.Save();
            if (_alarmMgr != null && !_alarmMgr.IsDisposed) _alarmMgr.RefreshList();
            this.Invalidate();
        }

        private void OpenAlarmManager()
        {
            if (_alarmMgr != null && !_alarmMgr.IsDisposed)
            {
                _alarmMgr.Activate();
                return;
            }
            _alarmMgr = new AlarmManagerForm(_s, this);
            _alarmMgr.Show();
        }

        private void OpenEarningsForm()
        {
            if (_earnForm != null && !_earnForm.IsDisposed)
            {
                _earnForm.Activate();
                return;
            }
            _earnForm = new EarningsForm(_s, this);
            _earnForm.Show();
        }

        public List<EarningEvent> EarningsSnapshot() { return _earnings; }
        public string EarningsStatus() { return _earnStatus; }
        public bool EarningsLoading() { return _earnLoading; }

        private void OpenAbout()
        {
            if (_aboutForm != null && !_aboutForm.IsDisposed) { _aboutForm.Activate(); return; }
            _aboutForm = new AboutForm(this);
            _aboutForm.Show();
        }

        private static byte[] GetWav()
        {
            if (_wavCache != null) return _wavCache;
            int sr = 44100;
            int[] freqs = { 988, 0, 1319, 0, 988, 0 };
            int[] durs = { 170, 70, 170, 70, 170, 650 };
            List<short> samples = new List<short>();
            for (int i = 0; i < freqs.Length; i++)
            {
                int n = sr * durs[i] / 1000;
                double att = 0.008 * sr, dec = 0.02 * sr;
                for (int k = 0; k < n; k++)
                {
                    double t = (double)k / sr;
                    double env = 1.0;
                    if (k < att) env = k / att;
                    else if (k > n - dec) env = (n - k) / dec;
                    double val = freqs[i] == 0 ? 0 : Math.Sin(2 * Math.PI * freqs[i] * t) * 0.40 * env;
                    samples.Add((short)(val * 32767));
                }
            }
            _wavCache = BuildWav(samples, sr);
            return _wavCache;
        }

        private static byte[] BuildWav(List<short> s, int sr)
        {
            int dataLen = s.Count * 2;
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataLen);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);   // PCM
                w.Write((short)1);   // mono
                w.Write(sr);
                w.Write(sr * 2);     // byte rate
                w.Write((short)2);   // block align
                w.Write((short)16);  // bits
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataLen);
                for (int i = 0; i < s.Count; i++) w.Write(s[i]);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
