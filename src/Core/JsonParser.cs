// ============================================================================
//  BetaClock - Minimal dependency-free JSON parser
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
    // Minimal JSON parser (no dependencies) for the calendar response
    public class JsonParser
    {
        private string s; private int i;
        public static object Parse(string text)
        {
            JsonParser j = new JsonParser(); j.s = text == null ? "" : text; j.i = 0;
            try { return j.Val(0); } catch { return null; }
        }
        private void Ws() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }
        private object Val(int depth)
        {
            if (depth > 500) throw new Exception("json demasiado anidado"); // avoids StackOverflow (which cannot be caught)
            Ws(); if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return Obj(depth + 1);
            if (c == '[') return Arr(depth + 1);
            if (c == '"') return Str();
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return Num();
        }
        private Dictionary<string, object> Obj(int depth)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(); i++; Ws();
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (i < s.Length)
            {
                Ws(); string k = Str(); Ws();
                if (i < s.Length && s[i] == ':') i++;
                object v = Val(depth); d[k] = v; Ws();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return d;
        }
        private List<object> Arr(int depth)
        {
            List<object> a = new List<object>(); i++; Ws();
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (i < s.Length)
            {
                object v = Val(depth); a.Add(v); Ws();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return a;
        }
        private string Str()
        {
            StringBuilder sb = new StringBuilder();
            if (i < s.Length && s[i] == '"') i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    if (e == '"') sb.Append('"');
                    else if (e == '\\') sb.Append('\\');
                    else if (e == '/') sb.Append('/');
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 'b') sb.Append('\b');
                    else if (e == 'f') sb.Append('\f');
                    else if (e == 'u' && i + 4 <= s.Length)
                    {
                        int code = Convert.ToInt32(s.Substring(i, 4), 16);
                        sb.Append((char)code); i += 4;
                    }
                    else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
        private object Num()
        {
            int st = i;
            while (i < s.Length && "0123456789+-.eE".IndexOf(s[i]) >= 0) i++;
            double d; double.TryParse(s.Substring(st, i - st), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
            return d;
        }
    }
}
