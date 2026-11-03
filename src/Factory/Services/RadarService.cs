using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Factory.Models;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;

namespace Factory.Services;

public interface IRadarService
{
    ObservableCollection<RadarTarget> Targets { get; }
    ObservableCollection<string> Events { get; }
    bool IsScanning { get; }
    bool BeepEnabled { get; set; }
    string BluetoothState { get; }
    string WifiState { get; }
    string LanState { get; }
    event EventHandler? Tick;
    Task StartAsync();
    void Stop();
    /// <summary>Ping em toda a /24 uma vez (forca o ARP a enxergar quem esta quieto). So por clique.</summary>
    Task SweepLanAsync();
    /// <summary>Tudo que esta NOVO vira conhecido (baseline).</summary>
    void AcknowledgeAll();
}

/// <summary>
/// Tres fontes, uma lista viva:
///   BLE   — BluetoothLEAdvertisementWatcher, tempo real (celular, relogio, fone anunciam sozinhos);
///   Wi-Fi — netsh a cada 10 s (roteadores e hotspots; exige o adaptador Wi-Fi ligado);
///   LAN   — arp -a a cada 15 s (quem esta NA minha rede).
/// "NOVO" = nunca visto: baseline propria em %LOCALAPPDATA%\Factory\radar-baseline.json,
/// mais o radar.db do wifi-radar (so leitura) pra Wi-Fi/LAN. Tudo que toca UI passa pelo dispatcher.
/// </summary>
public sealed partial class RadarService : IRadarService
{
    private static readonly string BaselineFile = Path.Combine(
        AppPaths.Cache, "radar-baseline.json");
    private static readonly string RadarDb = Path.Combine(ProjectScanner.Root, "wifi-radar", "radar.db");
    private static readonly string OuiCsv = Path.Combine(ProjectScanner.Root, "wifi-radar", "oui.csv");

    private static readonly Dictionary<ushort, string> Companies = new()
    {
        [0x004C] = "Apple", [0x0006] = "Microsoft", [0x0075] = "Samsung", [0x00E0] = "Google", [0x038F] = "Xiaomi",
        [0x027D] = "Huawei", [0x0087] = "Garmin", [0x0171] = "Amazon", [0x0059] = "Nordic", [0x000F] = "Broadcom",
        [0x0157] = "Anhui Huami", [0x0310] = "SGL Italia", [0x0499] = "Ruuvi", [0x01DA] = "Logitech", [0x0822] = "Adafruit",
        [0x0D00] = "Realme", [0x03DA] = "OnePlus", [0x0131] = "Cypress", [0x0002] = "Intel", [0x001D] = "Qualcomm",
        [0x0047] = "Plantronics", [0x00D6] = "Sony", [0x012D] = "Sony", [0x02FF] = "Motorola", [0x0104] = "Bose",
        [0x0201] = "JBL/Harman", [0x0111] = "Nike", [0x0154] = "Fitbit", [0x02E5] = "Tile",
    };

    [GeneratedRegex(@"^\s*SSID\s+\d+\s*:\s*(.*)$")] private static partial Regex RxSsid();
    [GeneratedRegex(@"^\s*BSSID\s+\d+\s*:\s*([0-9a-fA-F:]{17})")] private static partial Regex RxBssid();
    [GeneratedRegex(@"(Sinal|Signal)\s*:\s*(\d+)\s*%")] private static partial Regex RxSignal();
    [GeneratedRegex(@"(Canal|Channel)\s*:\s*(\d+)")] private static partial Regex RxChannel();
    [GeneratedRegex(@"^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]{17})\s+(\S+)")] private static partial Regex RxArp();

    private sealed class BaselineEntry { public DateTime First { get; set; } public DateTime Last { get; set; } public int Seen { get; set; } public string Name { get; set; } = ""; }

    private readonly DispatcherQueue _dq;
    private readonly Dictionary<string, RadarTarget> _byId = new();
    private readonly Dictionary<string, BaselineEntry> _baseline = new();
    private readonly HashSet<string> _dbKnown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Dictionary<string, string>> _oui;
    private readonly List<string> _myBases = [];
    private BluetoothLEAdvertisementWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private bool _baselineDirty;

    public ObservableCollection<RadarTarget> Targets { get; } = [];
    public ObservableCollection<string> Events { get; } = [];
    public bool IsScanning { get; private set; }
    public bool BeepEnabled { get; set; } = true;
    public string BluetoothState { get; private set; } = "—";
    public string WifiState { get; private set; } = "—";
    public string LanState { get; private set; } = "—";
    public event EventHandler? Tick;

    static RadarService()
    {
        // netsh/arp escrevem na pagina de codigo OEM (850 no pt-BR); sem isto os acentos viram lixo
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch (Exception) { }
    }

    public RadarService()
    {
        _dq = DispatcherQueue.GetForCurrentThread() ?? App.Window!.DispatcherQueue;
        _oui = new Lazy<Dictionary<string, string>>(LoadOui);
        LoadBaseline();
        LoadRadarDb();
    }

    // ---------- ciclo ----------

    public async Task StartAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Log("varredura iniciada");
        await StartBleAsync();
        _ = Task.Run(() => LoopAsync(WifiPassAsync, TimeSpan.FromSeconds(10), ct), ct);
        _ = Task.Run(() => LoopAsync(LanPassAsync, TimeSpan.FromSeconds(15), ct), ct);
        _ = Task.Run(() => LoopAsync(() => { _dq.TryEnqueue(Prune); return Task.CompletedTask; }, TimeSpan.FromSeconds(2), ct), ct);
        _ = Task.Run(() => LoopAsync(() => { SaveBaseline(); return Task.CompletedTask; }, TimeSpan.FromSeconds(30), ct), ct);
    }

    public void Stop()
    {
        if (!IsScanning) return;
        IsScanning = false;
        _cts?.Cancel();
        try { _watcher?.Stop(); } catch (Exception) { }
        SaveBaseline();
        Log("varredura parada");
    }

    private static async Task LoopAsync(Func<Task> pass, TimeSpan every, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try { await pass(); } catch (Exception) { }
            var wait = every - sw.Elapsed;
            if (wait > TimeSpan.Zero) { try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { } }
        }
    }

    // ---------- BLE (tempo real) ----------

    private async Task StartBleAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (bt is null) { BluetoothState = "sem adaptador Bluetooth"; return; }
            if (bt.State != RadioState.On) { BluetoothState = "Bluetooth desligado — ligue nas Configuracoes"; return; }

            _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            try { _watcher.AllowExtendedAdvertisements = true; } catch (Exception) { /* dongle sem BT5 */ }
            _watcher.Received += OnBleReceived;
            _watcher.Stopped += (_, e) => _dq.TryEnqueue(() => { BluetoothState = "BLE parou: " + e.Error; Tick?.Invoke(this, EventArgs.Empty); });
            _watcher.Start();
            BluetoothState = $"BLE ao vivo · {_watcher.Status} · 0 anuncios";
        }
        catch (Exception ex) { BluetoothState = "BLE indisponivel: " + ex.Message; }
    }

    private long _bleAdverts;

    private void OnBleReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var n = Interlocked.Increment(ref _bleAdverts);
        if (n == 1 || n % 50 == 0) _dq.TryEnqueue(() => { BluetoothState = $"BLE ao vivo · {n} anuncios"; Tick?.Invoke(this, EventArgs.Empty); });
        var addr = FormatAddress(args.BluetoothAddress);
        var id = "ble:" + addr;
        var rssi = args.RawSignalStrengthInDBm;
        var name = args.Advertisement.LocalName ?? "";
        string vendor = "";
        foreach (var m in args.Advertisement.ManufacturerData)
            if (Companies.TryGetValue(m.CompanyId, out var c)) { vendor = c; break; }
        var random = args.BluetoothAddressType == BluetoothAddressType.Random;
        // ate 2 atualizacoes/s por aparelho na UI; o resto so alimenta a media
        _dq.TryEnqueue(() => Upsert(id, RadarTarget.KindBle, addr, name, vendor, rssi, BleDistance(rssi), random, throttleMs: 500));
    }

    private static string FormatAddress(ulong a) => string.Join(":", BitConverter.GetBytes(a).Take(6).Reverse().Select(b => b.ToString("x2")));

    /// <summary>-59 dBm a 1 m, expoente 2.5 (ambiente interno). Erro facil de 2x.</summary>
    private static double BleDistance(int rssi) => Math.Clamp(Math.Pow(10, (-59.0 - rssi) / 25.0), 0.3, 60);

    // ---------- Wi-Fi (netsh) ----------

    private async Task WifiPassAsync()
    {
        var text = await RunAsync("netsh", "wlan show networks mode=bssid");
        if (text.Contains("inativa", StringComparison.OrdinalIgnoreCase) || text.Contains("not running", StringComparison.OrdinalIgnoreCase)
            || text.Contains("não oferece suporte", StringComparison.OrdinalIgnoreCase) || text.Contains("There is no wireless", StringComparison.OrdinalIgnoreCase))
        {
            _dq.TryEnqueue(() => { WifiState = "Wi-Fi desligado — ligue o adaptador pra ver radios"; Tick?.Invoke(this, EventArgs.Empty); });
            return;
        }
        var ssid = "";
        string? bssid = null;
        var count = 0;
        var found = new List<(string bssid, string ssid, int pct, int ch)>();
        int pct = 0, ch = 0;
        void Close() { if (bssid is not null) { found.Add((bssid, ssid, pct, ch)); count++; } bssid = null; pct = 0; ch = 0; }
        foreach (var line in text.Split('\n'))
        {
            var m = RxSsid().Match(line);
            if (m.Success) { Close(); ssid = m.Groups[1].Value.Trim(); if (ssid.Length == 0) ssid = "(oculto)"; continue; }
            m = RxBssid().Match(line);
            if (m.Success) { Close(); bssid = m.Groups[1].Value.ToLowerInvariant(); continue; }
            if (bssid is null) continue;
            m = RxSignal().Match(line);
            if (m.Success) { pct = int.Parse(m.Groups[2].Value); continue; }
            m = RxChannel().Match(line);
            if (m.Success) { ch = int.Parse(m.Groups[2].Value); }
        }
        Close();
        _dq.TryEnqueue(() =>
        {
            foreach (var f in found)
            {
                var dbm = f.pct / 2 - 100;
                var dist = Math.Clamp(Math.Pow(10, (-40.0 - dbm) / 27.0), 0.5, 60);
                var mine = _myBases.Any(b => f.bssid.StartsWith(b, StringComparison.OrdinalIgnoreCase));
                var t = Upsert("wifi:" + f.bssid, RadarTarget.KindWifi, f.bssid, f.ssid, $"canal {f.ch} · {Vendor(f.bssid)}", dbm, dist, false, 0);
                if (mine && !t.IsMine) t.IsMine = true;
            }
            WifiState = $"{count} radios (netsh, 10 s)";
            Tick?.Invoke(this, EventArgs.Empty);
        });
    }

    // ---------- LAN (arp) ----------

    private async Task LanPassAsync()
    {
        var text = await RunAsync("arp", "-a");
        var found = new List<(string mac, string ip)>();
        foreach (var line in text.Split('\n'))
        {
            var m = RxArp().Match(line);
            if (!m.Success) continue;
            var ip = m.Groups[1].Value;
            var mac = m.Groups[2].Value.ToLowerInvariant().Replace('-', ':');
            var type = m.Groups[3].Value.ToLowerInvariant();
            if (!type.StartsWith("din") && !type.StartsWith("dyn")) continue; // so dinamico = aparelho de verdade
            if (ip.EndsWith(".255") || mac.StartsWith("ff:ff") || mac.StartsWith("01:00:5e")) continue;
            var first = ip.Split('.')[0];
            if (first is "224" or "239") continue;
            found.Add((mac, ip));
        }
        _dq.TryEnqueue(() =>
        {
            foreach (var f in found)
                Upsert("lan:" + f.mac, RadarTarget.KindLan, f.mac, f.ip, Vendor(f.mac), 0, 15, false, 0);
            LanState = $"{found.Count} na minha rede (arp, 15 s)";
            Tick?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task SweepLanAsync()
    {
        var prefix = await Task.Run(() =>
        {
            try
            {
                using var s = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
                s.Connect("192.168.0.1", 1);
                var ip = ((System.Net.IPEndPoint)s.LocalEndPoint!).Address.ToString();
                return ip[..(ip.LastIndexOf('.') + 1)];
            }
            catch (Exception) { return "192.168.0."; }
        });
        Log($"varrendo {prefix}0/24 (ping)…");
        await Task.Run(() =>
        {
            Parallel.For(1, 255, new ParallelOptions { MaxDegreeOfParallelism = 48 }, n =>
            {
                try { using var p = new System.Net.NetworkInformation.Ping(); p.Send(prefix + n, 250); } catch (Exception) { }
            });
        });
        await LanPassAsync();
        Log("varredura da rede concluida");
    }

    // ---------- lista viva ----------

    private RadarTarget Upsert(string id, string kind, string addr, string name, string detail, int rssi, double dist, bool random, int throttleMs)
    {
        var now = DateTime.Now;
        if (!_byId.TryGetValue(id, out var t))
        {
            var known = _baseline.TryGetValue(id, out var b) || _dbKnown.Contains(id);
            t = new RadarTarget
            {
                Id = id, Kind = kind, Address = addr, FirstSeen = b?.First ?? now,
                Angle = StableAngle(id),
                Name = name.Length > 0 ? name : b?.Name ?? "",
                Detail = detail, Rssi = rssi, Distance = dist, LastSeen = now, RandomAddress = random,
                State = known ? "conhecido" : "novo",
            };
            if (!known) t.NewUntil = now.AddSeconds(90);
            t.Ema = rssi; t.Baseline = rssi;
            _byId[id] = t;
            Targets.Add(t);
            if (!_baseline.ContainsKey(id)) _baseline[id] = new BaselineEntry { First = now, Last = now, Seen = 0, Name = name };
            if (!known)
            {
                Log($"NOVO {t.KindLabel} {t.Title} {t.DistanceText}");
                if (BeepEnabled && (kind == RadarTarget.KindLan || dist <= 10)) Beep();
            }
            else if (kind == RadarTarget.KindLan) Log($"na rede: {t.Title} ({detail})");
            Tick?.Invoke(this, EventArgs.Empty);
            return t;
        }

        // alimenta a media sempre; a UI so a cada throttleMs
        if (kind != RadarTarget.KindLan)
        {
            t.Ema = double.IsNaN(t.Ema) ? rssi : 0.7 * t.Ema + 0.3 * rssi;
            Trend(t);
        }
        var be = _baseline[id];
        be.Last = now; be.Seen++;
        if (name.Length > 0 && be.Name != name) be.Name = name;
        _baselineDirty = true;

        if ((now - t.LastUiPush).TotalMilliseconds < throttleMs) return t;
        t.LastUiPush = now;
        if (name.Length > 0 && t.Name != name) t.Name = name;
        if (detail.Length > 0 && t.Detail != detail) t.Detail = detail;
        if (kind != RadarTarget.KindLan)
        {
            t.Rssi = (int)Math.Round(t.Ema);
            t.Distance = kind == RadarTarget.KindBle ? BleDistance(t.Rssi) : dist;
        }
        t.LastSeen = now;
        t.Seen = be.Seen;
        if (!t.Present) { t.Present = true; if (t.State == "sumiu") t.State = "conhecido"; Log($"voltou: {t.Title}"); }
        if (t.State == "novo" && now > t.NewUntil) t.State = "conhecido";
        return t;
    }

    /// <summary>Tendencia simples: desvio sustentado de 6 dB por 4 leituras contra a base; depois re-baseia.</summary>
    private static void Trend(RadarTarget t)
    {
        if (double.IsNaN(t.Baseline)) { t.Baseline = t.Ema; return; }
        var dev = t.Ema - t.Baseline;
        var dir = dev >= 6 ? 1 : dev <= -6 ? -1 : 0;
        if (dir == 0 || dir != t.Direction) { t.Streak = dir == 0 ? 0 : 1; t.Direction = dir; }
        else t.Streak++;
        if (dir != 0 && t.Streak >= 4)
        {
            if (t.State != "novo") t.State = dir > 0 ? "aproximando" : "afastando";
            t.Baseline = t.Ema; t.Streak = 0; t.Direction = 0;
        }
        else if (dir == 0)
        {
            t.Baseline = 0.95 * t.Baseline + 0.05 * t.Ema;
            if (t.IsMoving && (DateTime.Now - t.LastSeen).TotalSeconds > 8) t.State = "conhecido";
        }
    }

    private void Prune()
    {
        var now = DateTime.Now;
        foreach (var t in Targets.ToList())
        {
            var gone = t.Kind switch { RadarTarget.KindBle => 20, RadarTarget.KindWifi => 35, _ => 60 };
            var age = (now - t.LastSeen).TotalSeconds;
            if (t.Present && age > gone) { t.Present = false; t.State = "sumiu"; Log($"sumiu: {t.Title}"); }
            if (!t.Present && age > 180) { Targets.Remove(t); _byId.Remove(t.Id); }
            if (t.IsMoving && age > 10) t.State = "conhecido";
            t.Touch();
        }
        Tick?.Invoke(this, EventArgs.Empty);
    }

    public void AcknowledgeAll()
    {
        foreach (var t in Targets.Where(t => t.State == "novo")) t.State = "conhecido";
        SaveBaseline();
        Log("tudo marcado como conhecido");
    }

    private static double StableAngle(string id)
    {
        var h = 17;
        foreach (var c in id) h = unchecked(h * 31 + c);
        return Math.Abs(h) % 360;
    }

    private static void Beep()
    {
        try { Task.Run(() => { Console.Beep(1318, 90); Console.Beep(1760, 120); }); } catch (Exception) { }
    }

    private void Log(string msg)
    {
        void Do()
        {
            Events.Insert(0, $"{DateTime.Now:HH:mm:ss}  {msg}");
            while (Events.Count > 40) Events.RemoveAt(Events.Count - 1);
        }
        if (_dq.HasThreadAccess) Do(); else _dq.TryEnqueue(Do);
    }

    // ---------- fabricante / baseline / radar.db ----------

    private string Vendor(string mac)
    {
        var m = mac.Replace(":", "").Replace("-", "").ToLowerInvariant();
        if (m.Length < 6) return "";
        if (_oui.Value.TryGetValue(m[..6], out var v)) return v;
        var local = (Convert.ToInt32(m[..2], 16) & 2) != 0;
        return local ? "MAC aleatorio" : "fabricante desconhecido";
    }

    private static Dictionary<string, string> LoadOui()
    {
        var d = new Dictionary<string, string>(40000);
        try
        {
            if (!File.Exists(OuiCsv)) return d;
            foreach (var line in File.ReadLines(OuiCsv))
            {
                var i = line.IndexOf(',');
                if (i == 6 && line.Length > 7) d[line[..6].ToLowerInvariant()] = line[(i + 1)..].Trim();
            }
        }
        catch (Exception) { }
        return d;
    }

    private void LoadBaseline()
    {
        try
        {
            if (!File.Exists(BaselineFile)) return;
            var d = JsonSerializer.Deserialize<Dictionary<string, BaselineEntry>>(File.ReadAllText(BaselineFile));
            if (d is null) return;
            foreach (var kv in d) _baseline[kv.Key] = kv.Value;
        }
        catch (Exception) { }
    }

    private void SaveBaseline()
    {
        if (!_baselineDirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaselineFile)!);
            Dictionary<string, BaselineEntry> copy;
            lock (_baseline) copy = new(_baseline);
            File.WriteAllText(BaselineFile, JsonSerializer.Serialize(copy));
            _baselineDirty = false;
        }
        catch (Exception) { }
    }

    /// <summary>radar.db do wifi-radar: radios e aparelhos que ele ja viu contam como conhecidos; minhas_bases marca o que e meu.</summary>
    private void LoadRadarDb()
    {
        if (!File.Exists(RadarDb)) return;
        try
        {
            using var cx = new SqliteConnection($"Data Source={RadarDb};Mode=ReadOnly");
            cx.Open();
            using (var cmd = cx.CreateCommand())
            {
                cmd.CommandText = "SELECT bssid FROM radios WHERE aparicoes >= 3";
                using var r = cmd.ExecuteReader();
                while (r.Read()) _dbKnown.Add("wifi:" + r.GetString(0).ToLowerInvariant());
            }
            using (var cmd = cx.CreateCommand())
            {
                cmd.CommandText = "SELECT mac FROM lan WHERE aparicoes >= 3";
                using var r = cmd.ExecuteReader();
                while (r.Read()) _dbKnown.Add("lan:" + r.GetString(0).ToLowerInvariant().Replace('-', ':'));
            }
            using (var cmd = cx.CreateCommand())
            {
                cmd.CommandText = "SELECT valor FROM config WHERE chave='minhas_bases'";
                var v = cmd.ExecuteScalar() as string ?? "";
                _myBases.AddRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()));
            }
        }
        catch (Exception) { /* sem radar.db, so a baseline propria */ }
    }

    private static async Task<string> RunAsync(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                StandardOutputEncoding = SafeEncoding(850),
            };
            using var p = Process.Start(psi);
            if (p is null) return "";
            var text = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return text;
        }
        catch (Exception) { return ""; }
    }

    private static Encoding SafeEncoding(int cp)
    {
        try { return Encoding.GetEncoding(cp); } catch (Exception) { return Encoding.Default; }
    }
}
