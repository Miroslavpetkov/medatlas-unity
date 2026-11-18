using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MedAtlas.Sims
{
    /// <summary>Small JSON reader/writer: objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double, plus string, bool and null.</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Val(s, ref i); }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Val(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                while (true)
                {
                    Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                    string k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                    d[k] = Val(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                while (true)
                {
                    Ws(s, ref i); if (s[i] == ']') { i++; return l; }
                    l.Add(Val(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
            return num;
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': break;
                        case 'b': case 'f': break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Write(object o)
        {
            var sb = new StringBuilder(); W(sb, o); return sb.ToString();
        }

        static void W(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string str:
                    sb.Append('"');
                    foreach (var ch in str) { if (ch == '"' || ch == '\\') sb.Append('\\'); sb.Append(ch); }
                    sb.Append('"'); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case int n: sb.Append(n); break;
                case Dictionary<string, object> dict:
                    sb.Append('{'); bool first = true;
                    foreach (var kv in dict) { if (!first) sb.Append(','); first = false; W(sb, kv.Key); sb.Append(':'); W(sb, kv.Value); }
                    sb.Append('}'); break;
                case List<object> list:
                    sb.Append('['); for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); W(sb, list[i]); } sb.Append(']'); break;
                default: W(sb, o.ToString()); break;
            }
        }

        // helpers
        public static Dictionary<string, object> O(object o) => o as Dictionary<string, object>;
        public static List<object> L(object o) => o as List<object> ?? new List<object>();
        public static string S(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v != null ? (v is string s ? s : System.Convert.ToString(v, CultureInfo.InvariantCulture)) : "";
        public static double D(Dictionary<string, object> d, string k, double def = 0) => d != null && d.TryGetValue(k, out var v) && v is double x ? x : def;
        public static double Num(object v, double def = double.NaN) => v is double x ? x : v is bool b ? (b ? 1 : 0) : def;
    }
}
