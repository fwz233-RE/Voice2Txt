// Voice2Txt - Windows voice-to-text (port of voicestick-mindex for macOS)
// Hold the hotkey (default: Fn) to talk, release -> Xiaomi MiMo-V2.5-ASR
// (Token Plan API) transcribes, result is pasted into the focused window.
// API key is read from environment variables or a local config file only.
//
// NOTE: this file is compiled both by Windows PowerShell 5.1 Add-Type (C# 5
// compiler) and by the .NET SDK, so keep the syntax C# 5 compatible:
// no string interpolation, no ?., no out var, no nameof, no expression bodies.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Voice2Txt
{
    // ------------------------------------------------------------------
    // Mini JSON (no external dependencies; must work on .NET Framework 4.5+
    // and .NET 8 alike)
    // ------------------------------------------------------------------
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int pos = 0;
            return ParseValue(json, ref pos);
        }

        public static object Get(object node, string key)
        {
            Dictionary<string, object> map = node as Dictionary<string, object>;
            if (map == null) return null;
            object v;
            if (map.TryGetValue(key, out v)) return v;
            return null;
        }

        public static object Index(object node, int i)
        {
            System.Collections.IList list = node as System.Collections.IList;
            if (list == null || i < 0 || i >= list.Count) return null;
            return list[i];
        }

        public static string GetString(object node, string key)
        {
            object v = Get(node, key);
            return v is string ? (string)v : null;
        }

        public static string Write(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string) { WriteString(sb, (string)value); return; }
            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }
            if (value is int || value is long || value is short || value is byte)
            { sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            if (value is double || value is float || value is decimal)
            { sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            Dictionary<string, object> map = value as Dictionary<string, object>;
            if (map != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            System.Collections.IList list = value as System.Collections.IList;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, value.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        static object ParseValue(string s, ref int pos)
        {
            SkipWhite(s, ref pos);
            if (pos >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[pos];
            if (c == '{') return ParseObject(s, ref pos);
            if (c == '[') return ParseArray(s, ref pos);
            if (c == '"') return ParseString(s, ref pos);
            if (c == 't') { Expect(s, ref pos, "true"); return true; }
            if (c == 'f') { Expect(s, ref pos, "false"); return false; }
            if (c == 'n') { Expect(s, ref pos, "null"); return null; }
            return ParseNumber(s, ref pos);
        }

        static Dictionary<string, object> ParseObject(string s, ref int pos)
        {
            Dictionary<string, object> map = new Dictionary<string, object>();
            pos++; // {
            SkipWhite(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return map; }
            while (true)
            {
                SkipWhite(s, ref pos);
                string key = ParseString(s, ref pos);
                SkipWhite(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') throw new FormatException("Expected ':' in JSON.");
                pos++;
                object value = ParseValue(s, ref pos);
                map[key] = value;
                SkipWhite(s, ref pos);
                if (pos >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return map; }
                throw new FormatException("Unexpected character in JSON object.");
            }
        }

        static List<object> ParseArray(string s, ref int pos)
        {
            List<object> list = new List<object>();
            pos++; // [
            SkipWhite(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return list; }
            while (true)
            {
                object value = ParseValue(s, ref pos);
                list.Add(value);
                SkipWhite(s, ref pos);
                if (pos >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return list; }
                throw new FormatException("Unexpected character in JSON array.");
            }
        }

        static string ParseString(string s, ref int pos)
        {
            if (s[pos] != '"') throw new FormatException("Expected string in JSON.");
            pos++;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length) throw new FormatException("Unterminated JSON string.");
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (pos >= s.Length) throw new FormatException("Unterminated escape in JSON.");
                    char e = s[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > s.Length) throw new FormatException("Bad unicode escape.");
                            sb.Append((char)Convert.ToInt32(s.Substring(pos, 4), 16));
                            pos += 4;
                            break;
                        default: throw new FormatException("Bad escape in JSON.");
                    }
                }
                else sb.Append(c);
            }
        }

        static object ParseNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && ("+-0123456789.eE".IndexOf(s[pos]) >= 0)) pos++;
            string token = s.Substring(start, pos - start);
            double d;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("Bad number in JSON.");
            return d;
        }

        static void Expect(string s, ref int pos, string literal)
        {
            if (pos + literal.Length > s.Length || s.Substring(pos, literal.Length) != literal)
                throw new FormatException("Bad literal in JSON.");
            pos += literal.Length;
        }

        static void SkipWhite(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }
    }

    // ------------------------------------------------------------------
    // App icon: assets\icon.ico next to the exe (or %APPDATA%\Voice2Txt),
    // Material Design "mic" glyph on a Blue 600 rounded tile.
    // ------------------------------------------------------------------
    public static class AppIcon
    {
        static Icon cached;

        public static Icon Get()
        {
            if (cached != null) return cached;
            try
            {
                // 1) embedded resource (standalone exe needs no side files)
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                Stream res = asm.GetManifestResourceStream("Voice2Txt.icon.ico");
                if (res != null) { cached = new Icon(res); AppLog.Log("icon loaded: embedded"); return cached; }
            }
            catch (Exception ex) { AppLog.Log("icon (embedded) failed: " + ex.Message); }
            try
            {
                string[] paths = new string[] {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voice2Txt", "icon.ico")
                };
                foreach (string p in paths)
                {
                    if (File.Exists(p)) { cached = new Icon(p); AppLog.Log("icon loaded: " + p); return cached; }
                }
            }
            catch (Exception ex) { AppLog.Log("icon load failed: " + ex.Message); }
            AppLog.Log("icon: fallback to SystemIcons");
            cached = SystemIcons.Application;
            return cached;
        }

        // Autostart via HKCU ...\Run. Uses the exe path when running compiled;
        // falls back to the dev launcher when running from PowerShell.
        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk == null) return;
                    if (enable)
                    {
                        string exe = Application.ExecutablePath;
                        if (exe.EndsWith("pwsh.exe", StringComparison.OrdinalIgnoreCase)
                            || exe.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "run.ps1");
                            if (!File.Exists(script)) script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "Voice2Txt.exe");
                            if (File.Exists(script) && script.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                exe = script;
                            else
                                exe = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"";
                        }
                        rk.SetValue("Voice2Txt", exe.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ? exe : "\"" + exe + "\"");
                    }
                    else rk.DeleteValue("Voice2Txt", false);
                    AppLog.Log("autostart=" + enable);
                }
            }
            catch (Exception ex) { AppLog.Log("autostart failed: " + ex.Message); }
        }

        public static bool GetAutoStart()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey rk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return rk != null && rk.GetValue("Voice2Txt") != null;
                }
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------
    // Breadcrumb / crash log (%APPDATA%\Voice2Txt\debug.log)
    // ------------------------------------------------------------------
    public static class AppLog
    {
        static readonly object Sync = new object();

        public static void Log(string message)
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Voice2Txt", "debug.log");
                lock (Sync)
                    File.AppendAllText(path,
                        DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Configuration: environment first, then %APPDATA%\Voice2Txt\config.txt
    // ------------------------------------------------------------------
    public class AppConfig
    {
        public string ApiKey = "";
        public string Hotkey = "Fn";
        public string Model = "mimo-v2.5-asr";
        public string BaseUrl = "";   // empty = auto by key prefix (see ResolveBaseUrl)
        public string Language = "auto";
        public bool AutoPaste = true;

        public const string TokenPlanUrl = "https://token-plan-cn.xiaomimimo.com/v1/chat/completions";
        public const string OfficialUrl = "https://api.xiaomimimo.com/v1/chat/completions";

        // Token Plan keys start with "tp-" and use the token-plan-* clusters;
        // regular MiMo open-platform keys use the official endpoint.
        public string ResolveBaseUrl()
        {
            string url = BaseUrl == null ? "" : BaseUrl.Trim();
            if (url.Length > 0) return url;
            return ApiKey.Trim().StartsWith("tp-") ? TokenPlanUrl : OfficialUrl;
        }

        static string ConfigDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voice2Txt");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static string ConfigPath() { return Path.Combine(ConfigDir(), "config.txt"); }
        public static string HistoryPath() { return Path.Combine(ConfigDir(), "history.jsonl"); }

        public static AppConfig Load()
        {
            AppConfig cfg = new AppConfig();
            Dictionary<string, string> values = new Dictionary<string, string>();
            string path = ConfigPath();
            if (File.Exists(path))
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim().ToUpperInvariant()] = line.Substring(eq + 1).Trim();
                }
            }

            string key = Environment.GetEnvironmentVariable("MIMO_API_KEY");
            if (string.IsNullOrEmpty(key)) key = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY");
            if (string.IsNullOrEmpty(key)) key = Environment.GetEnvironmentVariable("ALIYUN_API_KEY");
            if (string.IsNullOrEmpty(key)) key = Environment.GetEnvironmentVariable("VOICE_TO_TEXT_API_KEY");
            string fileKey;
            if (string.IsNullOrEmpty(key) && values.TryGetValue("API_KEY", out fileKey)) key = fileKey;
            if (string.IsNullOrEmpty(key) && values.TryGetValue("MIMO_API_KEY", out fileKey)) key = fileKey;
            cfg.ApiKey = key == null ? "" : key.Trim();

            string v;
            if (values.TryGetValue("HOTKEY", out v) && v.Length > 0) cfg.Hotkey = v;
            if (values.TryGetValue("MODEL", out v) && v.Length > 0) cfg.Model = v;
            if (values.TryGetValue("BASE_URL", out v)) cfg.BaseUrl = v;
            if (values.TryGetValue("LANGUAGE", out v) && v.Length > 0) cfg.Language = v;
            if (values.TryGetValue("AUTO_PASTE", out v)) cfg.AutoPaste = (v == "1" || v.ToLowerInvariant() == "true");
            return cfg;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Voice2Txt local config. Keep this file private; it contains your API key.");
            sb.AppendLine("API_KEY=" + ApiKey);
            sb.AppendLine("HOTKEY=" + Hotkey);
            sb.AppendLine("MODEL=" + Model);
            sb.AppendLine("BASE_URL=" + BaseUrl);
            sb.AppendLine("LANGUAGE=" + Language);
            sb.AppendLine("AUTO_PASTE=" + (AutoPaste ? "1" : "0"));
            File.WriteAllText(ConfigPath(), sb.ToString());
        }
    }

    // ------------------------------------------------------------------
    // History store (JSON lines)
    // ------------------------------------------------------------------
    public static class HistoryStore
    {
        public static void Append(string text)
        {
            try
            {
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                row["text"] = text;
                File.AppendAllText(AppConfig.HistoryPath(), MiniJson.Write(row) + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static List<string> Load()
        {
            List<string> rows = new List<string>();
            try
            {
                if (!File.Exists(AppConfig.HistoryPath())) return rows;
                foreach (string line in File.ReadAllLines(AppConfig.HistoryPath(), Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) continue;
                    try
                    {
                        object node = MiniJson.Parse(line);
                        string time = MiniJson.GetString(node, "time");
                        string text = MiniJson.GetString(node, "text");
                        rows.Add(time + "  " + text);
                    }
                    catch { rows.Add(line); }
                }
                rows.Reverse();
            }
            catch { }
            return rows;
        }
    }

    // ------------------------------------------------------------------
    // WAV container helper (PCM16 mono)
    // ------------------------------------------------------------------
    public static class WavCodec
    {
        public static byte[] Wrap(byte[] pcm, int sampleRate)
        {
            byte[] wav = new byte[44 + pcm.Length];
            WriteAscii(wav, 0, "RIFF");
            WriteU32(wav, 4, (uint)(36 + pcm.Length));
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteU32(wav, 16, 16);
            WriteU16(wav, 20, 1);            // PCM
            WriteU16(wav, 22, 1);            // mono
            WriteU32(wav, 24, (uint)sampleRate);
            WriteU32(wav, 28, (uint)(sampleRate * 2));
            WriteU16(wav, 32, 2);            // block align
            WriteU16(wav, 34, 16);           // bits per sample
            WriteAscii(wav, 36, "data");
            WriteU32(wav, 40, (uint)pcm.Length);
            Buffer.BlockCopy(pcm, 0, wav, 44, pcm.Length);
            return wav;
        }

        static void WriteAscii(byte[] buf, int offset, string s)
        {
            for (int i = 0; i < s.Length; i++) buf[offset + i] = (byte)s[i];
        }

        static void WriteU16(byte[] buf, int offset, ushort v)
        {
            buf[offset] = (byte)(v & 0xFF);
            buf[offset + 1] = (byte)(v >> 8);
        }

        static void WriteU32(byte[] buf, int offset, uint v)
        {
            buf[offset] = (byte)(v & 0xFF);
            buf[offset + 1] = (byte)((v >> 8) & 0xFF);
            buf[offset + 2] = (byte)((v >> 16) & 0xFF);
            buf[offset + 3] = (byte)(v >> 24);
        }
    }

    // ------------------------------------------------------------------
    // Microphone capture via winmm waveIn (16 kHz mono PCM16 preferred)
    // ------------------------------------------------------------------
    public class MicCapture : IDisposable
    {
        public event Action<byte[]> Data;
        public int SampleRate { get { return sampleRate; } }

        const uint WAVE_MAPPER = 0xFFFFFFFF;
        const uint CALLBACK_FUNCTION = 0x00030000;
        const uint WIM_OPEN = 0x3BE;
        const uint WIM_CLOSE = 0x3BF;
        const uint WIM_DATA = 0x3C0;
        const int BufferCount = 4;
        const int BufferMillis = 100;

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        delegate void WaveInDelegate(IntPtr hwi, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll")]
        static extern int waveInOpen(out IntPtr hwi, uint uDeviceID, ref WAVEFORMATEX pwfx, WaveInDelegate dwCallback, IntPtr dwInstance, uint dwFlags);

        [DllImport("winmm.dll")]
        static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, uint cbwh);

        [DllImport("winmm.dll")]
        static extern int waveInStart(IntPtr hwi);

        [DllImport("winmm.dll")]
        static extern int waveInReset(IntPtr hwi);

        [DllImport("winmm.dll")]
        static extern int waveInClose(IntPtr hwi);

        [DllImport("winmm.dll")]
        static extern int waveInGetNumDevs();

        IntPtr hwi = IntPtr.Zero;
        WaveInDelegate callback;
        readonly List<IntPtr> dataBuffers = new List<IntPtr>();
        readonly List<IntPtr> headers = new List<IntPtr>();
        readonly object sync = new object();
        volatile bool recording;
        int sampleRate = 16000;
        int bytesPerBuffer;
        int dataEvents;
        long dataBytes;
        PcmResampler resampler;

        public void Start()
        {
            lock (sync)
            {
                if (recording) return;
                AppLog.Log("MicCapture: waveInGetNumDevs=" + waveInGetNumDevs());
                int[] rates = new int[] { 16000, 44100, 48000 };
                Exception last = null;
                foreach (int rate in rates)
                {
                    try { OpenDevice(rate); last = null; break; }
                    catch (Exception ex) { last = ex; }
                }
                if (last != null) throw last;
                AppLog.Log("MicCapture: opened at " + sampleRate + " Hz, resample=" + (resampler != null));
                recording = true;
                for (int i = 0; i < BufferCount; i++)
                {
                    IntPtr hdr = headers[i];
                    waveInPrepareHeader(hwi, hdr, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                    waveInAddBuffer(hwi, hdr, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                }
                int r = waveInStart(hwi);
                AppLog.Log("MicCapture: waveInStart=" + r);
                if (r != 0) throw new InvalidOperationException("waveInStart failed: " + r);
            }
        }

        void OpenDevice(int preferredRate)
        {
            WAVEFORMATEX fmt = new WAVEFORMATEX();
            fmt.wFormatTag = 1;
            fmt.nChannels = 1;
            fmt.nSamplesPerSec = (uint)preferredRate;
            fmt.wBitsPerSample = 16;
            fmt.nBlockAlign = 2;
            fmt.nAvgBytesPerSec = (uint)preferredRate * 2;
            fmt.cbSize = 0;

            callback = new WaveInDelegate(OnWaveInMessage);
            IntPtr handle;
            int r = waveInOpen(out handle, WAVE_MAPPER, ref fmt, callback, IntPtr.Zero, CALLBACK_FUNCTION);
            AppLog.Log("MicCapture: waveInOpen(" + preferredRate + ")=" + r);
            if (r != 0)
                throw new InvalidOperationException("waveInOpen failed: " + r);
            hwi = handle;
            sampleRate = preferredRate;
            resampler = (preferredRate == 16000) ? null : new PcmResampler(preferredRate, 16000);
            bytesPerBuffer = preferredRate * 2 * BufferMillis / 1000;
            for (int i = 0; i < BufferCount; i++)
            {
                IntPtr data = Marshal.AllocHGlobal(bytesPerBuffer);
                dataBuffers.Add(data);
                IntPtr hdr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WAVEHDR)));
                WAVEHDR wh = new WAVEHDR();
                wh.lpData = data;
                wh.dwBufferLength = (uint)bytesPerBuffer;
                Marshal.StructureToPtr(wh, hdr, false);
                headers.Add(hdr);
            }
        }

        void OnWaveInMessage(IntPtr hwi, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg != WIM_DATA) { AppLog.Log("waveIn msg: 0x" + uMsg.ToString("x")); return; }
            WAVEHDR hdr = (WAVEHDR)Marshal.PtrToStructure(dwParam1, typeof(WAVEHDR));
            bool stillRecording = recording;
            dataEvents++;
            if (dataEvents <= 10 || dataEvents % 50 == 0)
                AppLog.Log("WIM_DATA #" + dataEvents + ": bytes=" + hdr.dwBytesRecorded + " recording=" + stillRecording);
            if (hdr.dwBytesRecorded > 0 && stillRecording)
            {
                byte[] chunk = new byte[hdr.dwBytesRecorded];
                Marshal.Copy(hdr.lpData, chunk, 0, (int)hdr.dwBytesRecorded);
                dataBytes += chunk.Length;
                byte[] outChunk = resampler == null ? chunk : resampler.Process(chunk);
                Action<byte[]> handler = Data;
                if (handler != null && outChunk != null && outChunk.Length > 0) handler(outChunk);
            }
            if (stillRecording)
            {
                waveInUnprepareHeader(hwi, dwParam1, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                waveInPrepareHeader(hwi, dwParam1, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                waveInAddBuffer(hwi, dwParam1, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (!recording && hwi == IntPtr.Zero) return;
                recording = false;
                AppLog.Log("MicCapture: stop, events=" + dataEvents + " bytes=" + dataBytes);
                dataEvents = 0;
                dataBytes = 0;
                if (hwi != IntPtr.Zero)
                {
                    waveInReset(hwi);
                    foreach (IntPtr hdr in headers)
                    {
                        waveInUnprepareHeader(hwi, hdr, (uint)Marshal.SizeOf(typeof(WAVEHDR)));
                        Marshal.FreeHGlobal(hdr);
                    }
                    foreach (IntPtr data in dataBuffers) Marshal.FreeHGlobal(data);
                    headers.Clear();
                    dataBuffers.Clear();
                    waveInClose(hwi);
                    hwi = IntPtr.Zero;
                }
            }
        }

        public void Dispose() { Stop(); }
    }

    // Simple streaming linear resampler, 16-bit mono.
    public class PcmResampler
    {
        readonly double step;
        double position;
        short lastSample;
        bool hasLast;

        public PcmResampler(int inRate, int outRate)
        {
            this.step = (double)inRate / (double)outRate;
        }

        public byte[] Process(byte[] input)
        {
            int samples = input.Length / 2;
            short[] src = new short[hasLast ? samples + 1 : samples];
            int offset = 0;
            if (hasLast) { src[0] = lastSample; offset = 1; }
            for (int i = 0; i < samples; i++)
                src[offset + i] = (short)(input[i * 2] | (input[i * 2 + 1] << 8));
            if (samples > 0)
            {
                lastSample = src[src.Length - 1];
                hasLast = true;
            }

            List<short> output = new List<short>();
            double pos = position;
            while (true)
            {
                int idx = (int)Math.Floor(pos);
                if (idx + 1 >= src.Length) break;
                double frac = pos - idx;
                double value = src[idx] * (1.0 - frac) + src[idx + 1] * frac;
                output.Add((short)value);
                pos += step;
            }
            position = pos - (src.Length - (hasLast ? 1 : 0));
            if (position < 0) position = 0;

            byte[] bytes = new byte[output.Count * 2];
            for (int i = 0; i < output.Count; i++)
            {
                bytes[i * 2] = (byte)(output[i] & 0xFF);
                bytes[i * 2 + 1] = (byte)((output[i] >> 8) & 0xFF);
            }
            return bytes;
        }
    }

    // ------------------------------------------------------------------
    // MiMo-V2.5-ASR client (Token Plan API, OpenAI-compatible chat format)
    // POST {base} with header "api-key: tp-...", body:
    //   {"model":"mimo-v2.5-asr","messages":[{"role":"user","content":[
    //     {"type":"input_audio","input_audio":{"data":"data:audio/wav;base64,..."}}]}],
    //    "asr_options":{"language":"auto"}}
    // Result: choices[0].message.content
    // ------------------------------------------------------------------
    public class AsrResult
    {
        public string Text = "";
        public int TotalTokens = 0;
    }

    public class MimoAsrClient
    {
        readonly AppConfig config;

        public MimoAsrClient(AppConfig config) { this.config = config; }

        public async Task<AsrResult> RecognizeAsync(byte[] wavBytes)
        {
            string apiKey = config.ApiKey.Trim();
            if (apiKey.Length == 0)
                throw new InvalidOperationException("请先在设置中填入 API Key（Token Plan 为 tp- 开头，官方订阅为 MIMO_API_KEY）。");
            string baseUrl = config.ResolveBaseUrl();

            string dataUri = "data:audio/wav;base64," + Convert.ToBase64String(wavBytes);

            Dictionary<string, object> inputAudio = new Dictionary<string, object>();
            inputAudio["data"] = dataUri;
            Dictionary<string, object> audioPart = new Dictionary<string, object>();
            audioPart["type"] = "input_audio";
            audioPart["input_audio"] = inputAudio;
            List<object> content = new List<object>();
            content.Add(audioPart);
            Dictionary<string, object> userMessage = new Dictionary<string, object>();
            userMessage["role"] = "user";
            userMessage["content"] = content;
            List<object> messages = new List<object>();
            messages.Add(userMessage);
            Dictionary<string, object> asrOptions = new Dictionary<string, object>();
            asrOptions["language"] = config.Language;
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = config.Model;
            body["messages"] = messages;
            body["asr_options"] = asrOptions;

            using (HttpClient http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(180);
                using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, baseUrl))
                {
                    // both auth styles accepted by the platform: send both so any
                    // subscription type (official / Token Plan) just works
                    req.Headers.Add("api-key", apiKey);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    req.Content = new StringContent(MiniJson.Write(body), Encoding.UTF8, "application/json");
                    HttpResponseMessage resp = await http.SendAsync(req);
                    string json = await resp.Content.ReadAsStringAsync();
                    object node = null;
                    try { node = MiniJson.Parse(json); }
                    catch { }
                    if (!resp.IsSuccessStatusCode)
                    {
                        string message = MiniJson.GetString(MiniJson.Get(node, "error"), "message");
                        if (message == null && json.Length > 0) message = json.Substring(0, Math.Min(200, json.Length));
                        if (message == null) message = "HTTP " + (int)resp.StatusCode;
                        if ((int)resp.StatusCode == 401)
                            message = message + "（请核对 Key 类型：tp- 开头走 Token Plan 集群，官方订阅走 api.xiaomimimo.com；可在 config.txt 用 BASE_URL 覆盖，当前：" + baseUrl + "）";
                        throw new InvalidOperationException(message);
                    }
                    AsrResult result = new AsrResult();
                    object message0 = MiniJson.Get(MiniJson.Index(MiniJson.Get(node, "choices"), 0), "message");
                    string text = MiniJson.GetString(message0, "content");
                    result.Text = text == null ? "" : text.Trim();
                    object usage = MiniJson.Get(node, "usage");
                    object tokens = MiniJson.Get(usage, "total_tokens");
                    if (tokens is double) result.TotalTokens = (int)(double)tokens;
                    return result;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Hotkey combo + raw-input keyboard monitor.
    // Fn and other "invisible" keys never show up in GetAsyncKeyState; they do
    // arrive as WM_INPUT raw data (often with VKey 255), so we match on raw
    // key ids instead of polling virtual keys.
    // ------------------------------------------------------------------
    public class HotkeyCombo
    {
        public const int Unbound = -1;
        public const int FnId = 255;      // raw-input VKey "no mapping" - where Fn usually lands

        public int KeyId = Unbound;
        public readonly List<int> Mods = new List<int>();   // 0x10 shift, 0x11 ctrl, 0x12 alt, 0x5B win
        public readonly List<HotkeyCombo> Alternates = new List<HotkeyCombo>();
        public string Display = "未设置";

        public static HotkeyCombo Parse(string text)
        {
            HotkeyCombo combo = new HotkeyCombo();
            if (text == null || text.Trim().Length == 0) { combo.Display = "未设置"; return combo; }
            combo.Display = text.Trim();
            string[] alts = text.Split('|');
            combo.ParseOne(alts[0]);
            for (int i = 1; i < alts.Length; i++)
            {
                HotkeyCombo alt = new HotkeyCombo();
                alt.ParseOne(alts[i]);
                if (alt.KeyId != Unbound) combo.Alternates.Add(alt);
            }
            return combo;
        }

        void ParseOne(string text)
        {
            string[] parts = text.Split('+');
            foreach (string raw in parts)
            {
                string p = raw.Trim().ToUpperInvariant();
                if (p.Length == 0) continue;
                if (p == "CTRL" || p == "CONTROL") Mods.Add(0x11);
                else if (p == "SHIFT") Mods.Add(0x10);
                else if (p == "ALT") Mods.Add(0x12);
                else if (p == "WIN") Mods.Add(0x5B);
                else if (p == "FN") KeyId = FnId;
                else if (p.StartsWith("VK:")) KeyId = int.Parse(p.Substring(3), CultureInfo.InvariantCulture);
                else if (p.Length == 1 && ((p[0] >= 'A' && p[0] <= 'Z') || (p[0] >= '0' && p[0] <= '9')))
                    KeyId = (int)p[0];
                else if (p == "SPACE") KeyId = 0x20;
                else if (p == "ENTER" || p == "RETURN") KeyId = 0x0D;
                else if (p == "TAB") KeyId = 0x09;
                else if (p.Length > 1 && p[0] == 'F')
                {
                    int n;
                    if (int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 12)
                        KeyId = 0x70 + (n - 1);
                }
            }
        }

        public bool Matches(HashSet<int> down)
        {
            if (SelfMatches(down)) return true;
            foreach (HotkeyCombo alt in Alternates)
                if (alt.SelfMatches(down)) return true;
            return false;
        }

        bool SelfMatches(HashSet<int> down)
        {
            if (KeyId == Unbound)
            {
                // Modifier-only combo (e.g. "Ctrl+Alt"): all modifiers down.
                if (Mods.Count == 0) return false;
                foreach (int mod in Mods)
                    if (!ModDown(down, mod)) return false;
                return true;
            }
            foreach (int mod in Mods)
                if (!ModDown(down, mod)) return false;
            if (KeyId == FnId)
            {
                // "Fn" without a captured id: accept any unmapped raw key.
                if (down.Contains(FnId)) return true;
                foreach (int id in down) if (id >= 0x100) return true;
                return false;
            }
            return down.Contains(KeyId);
        }

        static bool ModDown(HashSet<int> down, int mod)
        {
            // raw input reports left/right variants for modifiers
            if (mod == 0x10) return down.Contains(0x10) || down.Contains(0xA0) || down.Contains(0xA1);
            if (mod == 0x11) return down.Contains(0x11) || down.Contains(0xA2) || down.Contains(0xA3);
            if (mod == 0x12) return down.Contains(0x12) || down.Contains(0xA4) || down.Contains(0xA5);
            if (mod == 0x5B) return down.Contains(0x5B) || down.Contains(0x5C);
            return down.Contains(mod);
        }
    }

    public class RawKeyMonitor : NativeWindow, IDisposable
    {
        // Fired with true after the hotkey has been held for HoldMillis,
        // and with false when it is released.
        public event Action<bool> TalkSignal;
        // Fired while capturing: raw key id of the next pressed key.
        public event Action<int> KeyCaptured;
        public event Action CaptureTimedOut;

        public const int HoldMillis = 250;

        public HotkeyCombo Combo = HotkeyCombo.Parse("Fn");

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [DllImport("user32.dll")]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        const int WM_INPUT = 0x00FF;
        const uint RIDEV_INPUTSINK = 0x00000100;
        const uint RID_INPUT = 0x10000003;
        const ushort RI_KEY_BREAK = 0x0001;

        readonly HashSet<int> down = new HashSet<int>();
        readonly System.Windows.Forms.Timer holdTimer;
        readonly System.Windows.Forms.Timer captureTimer;
        bool comboDown;
        bool talking;
        bool capturing;
        public int rawCount;
        public uint lastRet;
        public uint lastSize;

        public RawKeyMonitor()
        {
            CreateParams cp = new CreateParams();
            // Hidden top-level tool window. Message-only windows (HWND_MESSAGE)
            // do NOT receive WM_INPUT, so this must be a real top-level window.
            cp.ExStyle = 0x00000080 | 0x08000000;  // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            cp.Style = 0;                          // not visible
            CreateHandle(cp);

            RAWINPUTDEVICE[] devices = new RAWINPUTDEVICE[1];
            devices[0].usUsagePage = 0x01;
            devices[0].usUsage = 0x06;   // keyboard
            devices[0].dwFlags = RIDEV_INPUTSINK;
            devices[0].hwndTarget = Handle;
            bool registered = RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            AppLog.Log("RawKeyMonitor: RegisterRawInputDevices=" + registered + " hwnd=" + Handle);

            holdTimer = new System.Windows.Forms.Timer();
            holdTimer.Interval = HoldMillis;
            holdTimer.Tick += delegate
            {
                holdTimer.Stop();
                if (!comboDown || talking) return;
                talking = true;
                AppLog.Log("holdTimer fire -> TalkSignal(true)");
                Action<bool> handler = TalkSignal;
                if (handler != null) handler(true);
            };

            captureTimer = new System.Windows.Forms.Timer();
            captureTimer.Interval = 8000;
            captureTimer.Tick += delegate
            {
                captureTimer.Stop();
                capturing = false;
                Action handler = CaptureTimedOut;
                if (handler != null) handler();
            };
        }

        public void BeginCapture()
        {
            capturing = true;
            captureTimer.Stop();
            captureTimer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) OnRawInput(m.LParam);
            base.WndProc(ref m);
        }

        void OnRawInput(IntPtr hRawInput)
        {
            if (rawCount < 30) rawCount++;
            uint size = 0;
            uint ret = GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
            lastRet = ret;
            lastSize = size;
            if (size == 0) return;
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                RAWINPUTHEADER header = (RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(RAWINPUTHEADER));
                if (header.dwType != 1) return; // RIM_TYPEKEYBOARD=1 (0 is mouse!)
                IntPtr kbPtr = new IntPtr(buffer.ToInt64() + Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                RAWKEYBOARD kb = (RAWKEYBOARD)Marshal.PtrToStructure(kbPtr, typeof(RAWKEYBOARD));
                if (rawCount < 30) { rawCount++; AppLog.Log("WM_INPUT #" + rawCount + ": vk=" + kb.VKey + " mc=" + kb.MakeCode + " flags=" + kb.Flags); }
                if (kb.VKey == 0) return;  // skip fake shifts / focus changes

                int id = KeyIdOf(kb);
                bool isUp = (kb.Flags & RI_KEY_BREAK) != 0;
                if (!isUp)
                    AppLog.Log("raw key DOWN: id=" + id + " vk=" + kb.VKey + " mc=" + kb.MakeCode);
                if (isUp) down.Remove(id);
                else
                {
                    down.Add(id);
                    if (capturing)
                    {
                        capturing = false;
                        captureTimer.Stop();
                        Action<int> handler = KeyCaptured;
                        if (handler != null) handler(id);
                    }
                }

                bool now = Combo.Matches(down);
                if (now && !comboDown)
                {
                    AppLog.Log("combo DOWN edge");
                    comboDown = true;
                    if (!talking) holdTimer.Start();
                }
                else if (!now && comboDown)
                {
                    AppLog.Log("combo UP edge (talking=" + talking + ")");
                    comboDown = false;
                    holdTimer.Stop();
                    if (talking)
                    {
                        talking = false;
                        Action<bool> handler = TalkSignal;
                        if (handler != null) handler(false);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        static int KeyIdOf(RAWKEYBOARD kb)
        {
            if (kb.VKey == 255 || kb.VKey == 0)
                return 0x100 | (kb.MakeCode & 0xFF);   // unmapped key (Fn & friends)
            return (int)kb.VKey;
        }

        public void Dispose()
        {
            holdTimer.Stop();
            holdTimer.Dispose();
            captureTimer.Stop();
            captureTimer.Dispose();
            DestroyHandle();
        }
    }

    // ------------------------------------------------------------------
    // Keyboard / window helpers: Ctrl+V insertion
    // ------------------------------------------------------------------
    public static class InputInserter
    {
        const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public static void Paste()
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);            // Ctrl down
            keybd_event(0x56, 0, 0, UIntPtr.Zero);            // V down
            keybd_event(0x56, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    // ------------------------------------------------------------------
    // Settings dialog
    // ------------------------------------------------------------------
    public class SettingsForm : Form
    {
        readonly AppConfig config;
        readonly RawKeyMonitor monitor;
        readonly TextBox keyBox;
        readonly TextBox hotkeyBox;
        readonly ComboBox langBox;
        readonly CheckBox pasteBox;
        readonly CheckBox autoStartBox;
        readonly Button captureBtn;
        readonly Label captureStatus;

        public SettingsForm(AppConfig config, RawKeyMonitor monitor)
        {
            this.config = config;
            this.monitor = monitor;
            Text = "Voice2Txt 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 300);

            Label keyLabel = new Label();
            keyLabel.Text = "API Key:";
            keyLabel.SetBounds(12, 12, 400, 20);
            keyBox = new TextBox();
            keyBox.SetBounds(12, 34, 416, 24);
            keyBox.Text = config.ApiKey;

            Label hotkeyLabel = new Label();
            hotkeyLabel.Text = "按住说话热键（长按触发，点按忽略）:";
            hotkeyLabel.SetBounds(12, 68, 400, 20);
            hotkeyBox = new TextBox();
            hotkeyBox.SetBounds(12, 90, 190, 24);
            hotkeyBox.Text = config.Hotkey;

            captureBtn = new Button();
            captureBtn.Text = "录制热键…";
            captureBtn.SetBounds(208, 89, 96, 26);
            captureBtn.Click += delegate
            {
                monitor.BeginCapture();
                captureStatus.Text = "请按下按键（如 Fn）… 8 秒内有效";
            };
            monitor.KeyCaptured += OnKeyCaptured;
            monitor.CaptureTimedOut += OnCaptureTimeout;

            captureStatus = new Label();
            captureStatus.SetBounds(12, 122, 416, 20);
            captureStatus.ForeColor = Color.Gray;
            captureStatus.Text = "Fn 等非常规键请用「录制热键」实测绑定";

            Label langLabel = new Label();
            langLabel.Text = "识别语言:";
            langLabel.SetBounds(12, 152, 380, 20);
            langBox = new ComboBox();
            langBox.SetBounds(12, 174, 120, 24);
            langBox.DropDownStyle = ComboBoxStyle.DropDownList;
            langBox.Items.AddRange(new object[] { "auto", "zh", "en" });
            langBox.SelectedItem = config.Language;
            if (langBox.SelectedIndex < 0) { langBox.Items.Add(config.Language); langBox.SelectedItem = config.Language; }

            pasteBox = new CheckBox();
            pasteBox.Text = "识别完成后自动粘贴到前台窗口";
            pasteBox.SetBounds(150, 174, 278, 24);
            pasteBox.Checked = config.AutoPaste;

            autoStartBox = new CheckBox();
            autoStartBox.Text = "开机自启动";
            autoStartBox.SetBounds(12, 204, 200, 24);
            autoStartBox.Checked = AppIcon.GetAutoStart();

            Button ok = new Button();
            ok.Text = "保存";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(252, 240, 85, 30);
            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(343, 240, 85, 30);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] {
                keyLabel, keyBox, hotkeyLabel, hotkeyBox, captureBtn, captureStatus,
                langLabel, langBox, pasteBox, autoStartBox, ok, cancel });
            ClientSize = new Size(440, 284);
        }

        void OnKeyCaptured(int id)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                string spec = id >= 0x100 ? "VK:" + id.ToString(CultureInfo.InvariantCulture)
                            : id == HotkeyCombo.FnId ? "Fn"
                            : "VK:" + id.ToString(CultureInfo.InvariantCulture);
                hotkeyBox.Text = spec;
                captureStatus.Text = "已捕获按键 id=" + id.ToString(CultureInfo.InvariantCulture)
                    + "，保存后生效";
            });
        }

        void OnCaptureTimeout()
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                captureStatus.Text = "没有捕获到按键。若 Fn 完全无事件，请换一个热键";
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            monitor.KeyCaptured -= OnKeyCaptured;
            monitor.CaptureTimedOut -= OnCaptureTimeout;
            if (DialogResult == DialogResult.OK)
            {
                config.ApiKey = keyBox.Text.Trim();
                config.Hotkey = hotkeyBox.Text.Trim().Length > 0 ? hotkeyBox.Text.Trim() : "Fn";
                config.Language = langBox.SelectedItem == null ? "auto" : langBox.SelectedItem.ToString();
                config.AutoPaste = pasteBox.Checked;
                config.Save();
                AppIcon.SetAutoStart(autoStartBox.Checked);
            }
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------------
    // History dialog
    // ------------------------------------------------------------------
    public class HistoryForm : Form
    {
        readonly ListBox list;

        public HistoryForm()
        {
            Text = "识别历史（双击复制）";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 380);
            list = new ListBox();
            list.Dock = DockStyle.Fill;
            list.IntegralHeight = false;
            foreach (string row in HistoryStore.Load()) list.Items.Add(row);
            list.DoubleClick += delegate
            {
                if (list.SelectedItem != null)
                {
                    try { Clipboard.SetText(list.SelectedItem.ToString()); }
                    catch { }
                }
            };
            Controls.Add(list);
        }
    }

    // ------------------------------------------------------------------
    // Main window
    // ------------------------------------------------------------------
    public class MainForm : Form
    {
        readonly AppConfig config;
        readonly MimoAsrClient asr;
        readonly MicCapture mic = new MicCapture();
        readonly RawKeyMonitor monitor;
        readonly TextBox text;
        readonly Label status;
        readonly Button talkButton;
        readonly List<byte[]> recorded = new List<byte[]>();
        // ---- segmented "streaming" recognition (MiMo is one-shot per call,
        // so we cut on silence / length and show text as segments complete) ----
        const int SpeechRms = 200;          // quiet-mic friendly (was 350)
        const int SilenceFlushMs = 500;     // natural breath pauses trigger a cut
        const int MaxSegmentMs = 4500;      // worst-case latency for live display
        readonly List<byte[]> currentSeg = new List<byte[]>();
        readonly object resultLock = new object();
        readonly List<string> segResults = new List<string>();
        int segCounter;
        int segInFlight;
        int segErrors;
        bool segHasSpeech;
        int segSilenceMs;
        int segPeakRms;
        long segBytes;
        int rateCache = 16000;
        bool finishing;
        readonly object recordLock = new object();
        volatile bool recording;
        volatile bool starting;
        volatile bool stopRequested;
        bool shownByTalk;
        int sessionId;
        IntPtr pasteTarget = IntPtr.Zero;

        public MainForm(AppConfig config, RawKeyMonitor monitor)
        {
            AppLog.Log("MainForm: ctor start");
            this.config = config;
            this.monitor = monitor;
            this.asr = new MimoAsrClient(config);

            Text = "Voice2Txt — 长按 " + monitor.Combo.Display + " 说话";
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(480, 320);
            ClientSize = new Size(560, 380);
            BackColor = Color.White;

            status = new Label();
            status.Text = "就绪";
            status.Dock = DockStyle.Top;
            status.Height = 26;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(8, 0, 0, 0);
            status.BackColor = Color.FromArgb(245, 245, 245);

            text = new TextBox();
            text.Multiline = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.Dock = DockStyle.Fill;
            text.Font = new Font("Microsoft YaHei UI", 11.0f);
            text.AcceptsReturn = true;

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 48;
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.Padding = new Padding(6, 8, 6, 6);

            talkButton = new Button();
            talkButton.Text = "按住说话";
            talkButton.Size = new Size(96, 32);
            talkButton.MouseDown += delegate { StartSession(); };
            talkButton.MouseUp += delegate { StopSession(); };

            Button copyBtn = MakeButton("复制", delegate
            {
                if (text.Text.Length > 0) { try { Clipboard.SetText(text.Text); SetStatus("已复制到剪贴板"); } catch { } }
            });
            Button reinsertBtn = MakeButton("再次插入", delegate { Reinsert(); });
            Button historyBtn = MakeButton("历史", delegate
            {
                using (HistoryForm form = new HistoryForm()) form.ShowDialog(this);
            });
            Button settingsBtn = MakeButton("设置", delegate
            {
                using (SettingsForm form = new SettingsForm(config, monitor))
                {
                    if (form.ShowDialog(this) == DialogResult.OK)
                    {
                        monitor.Combo = HotkeyCombo.Parse(config.Hotkey);
                        Text = "Voice2Txt — 长按 " + monitor.Combo.Display + " 说话";
                        SetStatus("设置已保存");
                    }
                }
            });
            Button clearBtn = MakeButton("清空", delegate { text.Clear(); SetStatus("就绪"); });

            panel.Controls.Add(talkButton);
            panel.Controls.Add(copyBtn);
            panel.Controls.Add(reinsertBtn);
            panel.Controls.Add(historyBtn);
            panel.Controls.Add(settingsBtn);
            panel.Controls.Add(clearBtn);

            Controls.Add(text);
            Controls.Add(panel);
            Controls.Add(status);

            monitor.TalkSignal += OnTalkSignal;
            mic.Data += OnMicData;
            AppLog.Log("MainForm: ctor done");
            SetStatus("就绪 — 长按 " + monitor.Combo.Display + " 说话（点按无效），或按住「按住说话」按钮");
        }

        Button MakeButton(string label, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = label;
            b.Size = new Size(84, 32);
            b.Click += onClick;
            return b;
        }

        void OnTalkSignal(bool pressed)
        {
            if (pressed)
            {
                // Hold the hotkey: summon the panel near the cursor (without
                // stealing focus, so auto-paste goes to the target app).
                shownByTalk = !Visible;
                ShowOverlay();
                StartSession();
            }
            else
            {
                // Release: the panel disappears unconditionally; the text is
                // inserted when the recognition result arrives.
                StopSession();
                shownByTalk = false;
                TopMost = false;
                Hide();
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        void ShowOverlay()
        {
            Point pt = Cursor.Position;
            Screen scr = Screen.FromPoint(pt);
            int x = pt.X + 30;
            int y = pt.Y - Height - 30;
            if (x + Width > scr.WorkingArea.Right) x = scr.WorkingArea.Right - Width - 8;
            if (x < scr.WorkingArea.Left) x = scr.WorkingArea.Left + 8;
            if (y < scr.WorkingArea.Top) y = pt.Y + 40;
            Location = new Point(x, y);
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Show();
        }

        public void StartSession()
        {
            if (recording || starting) return;
            if (config.ApiKey.Trim().Length == 0)
            {
                SetStatus("尚未配置 API Key");
                using (SettingsForm form = new SettingsForm(config, monitor))
                {
                    if (form.ShowDialog(this) == DialogResult.OK)
                        SetStatus("设置已保存");
                }
                return;
            }
            lock (recordLock)
            {
                recorded.Clear();
                currentSeg.Clear();
                segBytes = 0; segHasSpeech = false; segSilenceMs = 0;
            }
            lock (resultLock)
            {
                segResults.Clear();
                segCounter = 0; segInFlight = 0; segErrors = 0; finishing = false;
            }
            text.Clear();   // new session starts blank; old text lives in history
            stopRequested = false;
            starting = true;
            sessionId++;
            int sid = sessionId;
            IntPtr fg = InputInserter.GetForegroundWindow();
            if (fg != Handle && fg != IntPtr.Zero) pasteTarget = fg;
            SetStatus("● 正在开始录音…");
            talkButton.Text = "松开结束";
            // mic.Start hits winmm P/Invoke several times; never do that on the
            // UI thread (it froze the whole app under x64 emulation).
            Task.Run(delegate
            {
                try { mic.Start(); }
                catch (Exception ex)
                {
                    starting = false;
                    if (sid == sessionId)
                        OnError("麦克风错误: " + ex.Message);
                    return;
                }
                starting = false;
                if (stopRequested || sid != sessionId)
                {
                    stopRequested = false;
                    try { mic.Stop(); }
                    catch { }
                    return;
                }
                recording = true;
                rateCache = mic.SampleRate;
                InvokeIfRequired(delegate
                {
                    talkButton.Text = "松开结束";
                    SetStatus("● 说话中…（松开 " + monitor.Combo.Display + " 结束）");
                });
            });
        }

        public void StopSession()
        {
            if (starting) { stopRequested = true; return; }
            if (!recording) return;
            recording = false;
            talkButton.Text = "按住说话";
            Task.Run(delegate { try { mic.Stop(); } catch { } });

            int sid = sessionId;
            lock (recordLock) FlushCurrentSegmentLocked();
            bool immediate;
            lock (resultLock)
            {
                finishing = true;
                immediate = (segInFlight == 0);
            }
            if (immediate) FinishSegmented(sid);
            else SetStatus("收尾识别中…");
        }

        void OnMicData(byte[] data)
        {
            lock (recordLock)
            {
                if (!recording) return;
                recorded.Add(data);
                currentSeg.Add(data);
                segBytes += data.Length;
                int chunkMs = (int)(data.Length / 2L * 1000L / Math.Max(1, rateCache));
                if (RootMeanSquare(data) >= SpeechRms) { segHasSpeech = true; segSilenceMs = 0; if (RootMeanSquare(data) > segPeakRms) segPeakRms = RootMeanSquare(data); }
                else segSilenceMs += chunkMs;
                bool silenceCut = segHasSpeech && segSilenceMs >= SilenceFlushMs;
                bool lengthCut = segBytes >= (long)rateCache * 2L * MaxSegmentMs / 1000L;
                if (silenceCut || lengthCut) FlushCurrentSegmentLocked();
            }
        }

        static int RootMeanSquare(byte[] data)
        {
            int n = data.Length / 2;
            if (n == 0) return 0;
            long sum = 0;
            for (int i = 0; i < n; i++)
            {
                short s = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                sum += (long)s * s;
            }
            return (int)Math.Sqrt((double)sum / n);
        }

        void FlushCurrentSegmentLocked()
        {
            if (currentSeg.Count == 0) return;
            if (!segHasSpeech) { currentSeg.Clear(); segBytes = 0; segSilenceMs = 0; segPeakRms = 0; return; }
            int total = 0;
            foreach (byte[] c in currentSeg) total += c.Length;
            byte[] pcm = new byte[total];
            int off = 0;
            foreach (byte[] c in currentSeg) { Buffer.BlockCopy(c, 0, pcm, off, c.Length); off += c.Length; }
            currentSeg.Clear();
            segBytes = 0; segHasSpeech = false; segSilenceMs = 0;

            int index = segCounter++;
            lock (resultLock)
            {
                while (segResults.Count <= index) segResults.Add("");
                segInFlight++;
            }
            byte[] wav = WavCodec.Wrap(pcm, rateCache);
            int sid = sessionId;
            AppLog.Log("segment " + index + " sent (" + (pcm.Length / 2 / rateCache) + "s, peakRms=" + segPeakRms + ")");
            segPeakRms = 0;
            Task.Run(async delegate { await RecognizeSegmentAsync(index, wav, sid); });
        }

        async Task RecognizeSegmentAsync(int index, byte[] wav, int sid)
        {
            string got = "";
            try
            {
                AsrResult r = await asr.RecognizeAsync(wav);
                got = r.Text;
            }
            catch (Exception ex)
            {
                lock (resultLock) segErrors++;
                AppLog.Log("segment " + index + " failed: " + ex.Message);
            }
            bool last = false;
            lock (resultLock)
            {
                segResults[index] = got;
                segInFlight--;
                last = (segInFlight == 0 && finishing);
            }
            InvokeIfRequired(delegate { if (!finishing) UpdateLiveText(); });
            if (last) FinishSegmented(sid);
        }

        void UpdateLiveText()
        {
            StringBuilder sb = new StringBuilder();
            lock (resultLock)
            {
                foreach (string s in segResults) sb.Append(s);
            }
            text.Text = sb.ToString();
        }

        void FinishSegmented(int sid)
        {
            if (sid != sessionId) return;
            string full;
            int errors;
            lock (resultLock)
            {
                StringBuilder sb = new StringBuilder();
                foreach (string s in segResults) sb.Append(s);
                full = sb.ToString();
                errors = segErrors;
                segResults.Clear();
                segCounter = 0; segErrors = 0; finishing = false;
            }
            OnFinal(full, errors);
        }

        void OnFinal(string full, int errors)
        {
            InvokeIfRequired(delegate
            {
                text.Text = full;
                if (full.Length == 0)
                {
                    SetStatus(errors > 0 ? "识别失败（" + errors + " 段出错）" : "没有识别到语音");
                    TopMost = false;
                    Hide();
                    return;
                }
                HistoryStore.Append(full);
                string suffix = errors > 0 ? "（" + errors + " 段识别失败）" : "";
                if (config.AutoPaste)
                {
                    try { Clipboard.SetText(full); }
                    catch { SetStatus("识别完成（剪贴板写入失败）" + suffix); return; }
                    DoPaste(pasteTarget);
                    SetStatus("已识别并粘贴" + suffix);
                }
                else SetStatus("已识别（自动粘贴已关闭）" + suffix);
                TopMost = false;
                Hide();   // result inserted -> panel auto-closes
            });
        }

        void OnError(string message)
        {
            InvokeIfRequired(delegate { SetStatus("错误: " + message); talkButton.Text = "按住说话"; });
        }

        void Reinsert()
        {
            if (text.Text.Length == 0) { SetStatus("没有可插入的文字"); return; }
            try { Clipboard.SetText(text.Text); }
            catch { return; }
            DoPaste(pasteTarget);
            SetStatus("已再次插入");
        }

        void DoPaste(IntPtr target)
        {
            IntPtr fg = InputInserter.GetForegroundWindow();
            bool needSwitch = (fg == Handle || fg == IntPtr.Zero) && target != IntPtr.Zero;
            if (needSwitch) InputInserter.SetForegroundWindow(target);
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = needSwitch ? 180 : 40;
            t.Tick += delegate { t.Stop(); t.Dispose(); InputInserter.Paste(); };
            t.Start();
        }

        void SetStatus(string message)
        {
            InvokeIfRequired(delegate { status.Text = message; });
        }

        void InvokeIfRequired(MethodInvoker action)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); }
                catch { }
            }
            else action();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            monitor.TalkSignal -= OnTalkSignal;
            mic.Dispose();
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------------
    // Tray / application context
    // ------------------------------------------------------------------
    public class TrayContext : ApplicationContext
    {
        readonly AppConfig config;
        readonly RawKeyMonitor monitor;
        readonly MainForm form;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;

        public TrayContext(AppConfig config)
        {
            AppLog.Log("TrayContext: start");
            this.config = config;
            monitor = new RawKeyMonitor();
            AppLog.Log("TrayContext: RawKeyMonitor ok (handle=" + monitor.Handle + ")");
            monitor.Combo = HotkeyCombo.Parse(config.Hotkey);
            form = new MainForm(config, monitor);
            AppLog.Log("TrayContext: MainForm constructed");

            menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { ShowMain(); });
            menu.Items.Add("设置", null, delegate
            {
                ShowMain();
                using (SettingsForm s = new SettingsForm(config, monitor))
                {
                    if (s.ShowDialog(form) == DialogResult.OK)
                        monitor.Combo = HotkeyCombo.Parse(config.Hotkey);
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Exit(); });

            tray = new NotifyIcon();
            tray.Icon = AppIcon.Get();
            tray.Text = "Voice2Txt — 长按 " + monitor.Combo.Display + " 说话";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowMain();   // single click opens the panel
            };
            tray.DoubleClick += delegate { ShowMain(); };
            tray.Visible = true;

            AppLog.Log("TrayContext: form.Show()");
            form.Show();
            AppLog.Log("TrayContext: form shown, handle=" + form.Handle);
        }

        void ShowMain()
        {
            form.TopMost = false;
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        void Exit()
        {
            tray.Visible = false;
            tray.Dispose();
            monitor.Dispose();
            Application.Exit();
        }
    }

    // ------------------------------------------------------------------
    // Entry point
    // ------------------------------------------------------------------
    public static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main()
        {
            Run();
        }

        // .NET Framework apps without an app.config default to TLS 1.0/1.1 and
        // every HTTPS call fails. ServicePointManager is obsolete on .NET 8 and
        // breaks the pwsh compile with SYSLIB0014-as-error, so set it by
        // reflection: works (and silent) on both toolchains.
        static void EnableTls12()
        {
            try
            {
                Type t = Type.GetType("System.Net.ServicePointManager, System")
                    ?? Type.GetType("System.Net.ServicePointManager, System.Net.Requests");
                if (t == null)
                {
                    foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = a.GetType("System.Net.ServicePointManager");
                        if (t != null) break;
                    }
                }
                if (t == null) { AppLog.Log("TLS: ServicePointManager type NOT found"); return; }
                System.Reflection.PropertyInfo p = t.GetProperty("SecurityProtocol");
                object current = p.GetValue(null, null);
                int upgraded = Convert.ToInt32(current) | 3072;   // Tls12
                p.SetValue(null, Enum.ToObject(p.PropertyType, upgraded), null);
                AppLog.Log("TLS: SecurityProtocol " + current + " -> " + upgraded);
            }
            catch (Exception ex) { AppLog.Log("TLS setup failed: " + ex.Message); }
        }

        public static void Run()
        {
            AppLog.Log("==== Run() start, pid=" + Process.GetCurrentProcess().Id
                + " os=" + Environment.OSVersion + " 64bit=" + Environment.Is64BitProcess
                + " arch=" + RuntimeInformation.ProcessArchitecture);
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                AppLog.Log("FATAL: " + e.ExceptionObject);
            };
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                AppLog.Log("UI EXCEPTION: " + e.Exception);
            };
            try { SetProcessDPIAware(); }
            catch { }
            EnableTls12();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppConfig config = AppConfig.Load();
            AppLog.Log("config loaded: key=" + (config.ApiKey.Length > 0 ? "yes" : "NO")
                + " hotkey=" + config.Hotkey + " model=" + config.Model);
            if (config.ApiKey.Trim().Length == 0)
            {
                using (SettingsForm form = new SettingsForm(config, new RawKeyMonitor()))
                {
                    form.Text = "Voice2Txt 设置（首次运行）";
                    form.ShowDialog();
                }
            }
            try
            {
                AppLog.Log("Application.Run(TrayContext)");
                Application.Run(new TrayContext(config));
                AppLog.Log("Application.Run returned");
            }
            catch (Exception ex)
            {
                AppLog.Log("STARTUP FAILED: " + ex);
                try { MessageBox.Show("Voice2Txt 启动失败：\n" + ex.Message, "Voice2Txt"); }
                catch { }
            }
        }
    }
}
