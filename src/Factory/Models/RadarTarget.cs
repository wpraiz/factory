using CommunityToolkit.Mvvm.ComponentModel;

namespace Factory.Models;

/// <summary>
/// Um alvo do RADAR: um radio Bluetooth LE (celular, relogio, fone), um radio Wi-Fi (roteador,
/// hotspot de celular) ou um aparelho na minha rede (ARP). Distancia e estimativa grosseira por
/// perda de percurso — serve pra ordenar por proximidade, nunca pra localizar.
/// </summary>
public sealed partial class RadarTarget : ObservableObject
{
    public const string KindBle = "ble";
    public const string KindWifi = "wifi";
    public const string KindLan = "lan";

    public required string Id { get; init; }      // "ble:aa:bb…", "wifi:bssid", "lan:mac"
    public required string Kind { get; init; }
    public required string Address { get; init; }
    public required DateTime FirstSeen { get; init; }

    /// <summary>Angulo estavel (hash do Id) — o alvo nao pula de lugar entre leituras.</summary>
    public double Angle { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _name = "";

    [ObservableProperty] private string _detail = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RssiText))]
    private int _rssi;

    /// <summary>RSSI suavizado (EMA) — e o que move o ponto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistanceText))]
    private double _distance = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(IsNew), nameof(IsMoving))]
    private string _state = "novo";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgeText))]
    private DateTime _lastSeen;

    [ObservableProperty] private int _seen;
    [ObservableProperty] private bool _present = true;
    [ObservableProperty] private bool _isMine;
    [ObservableProperty] private bool _randomAddress;

    // ---- calculados pra UI ----
    public string Title => Name.Length > 0 ? Name : Address;
    public string KindLabel => Kind switch { KindBle => "BLE", KindWifi => "WI-FI", _ => "REDE" };
    public string Accent => Kind switch { KindBle => "violet", KindWifi => "cyan", _ => "green" };
    public bool IsNew => State == "novo";
    public bool IsMoving => State is "aproximando" or "afastando";
    public string RssiText => Kind == KindLan ? "" : $"{Rssi} dBm";
    public string DistanceText => Kind == KindLan ? "na rede" : Distance < 10 ? $"~{Distance:0.0} m" : $"~{Distance:0} m";
    public string StateText => State switch
    {
        "novo" => "NOVO",
        "aproximando" => "APROXIMANDO",
        "afastando" => "afastando",
        "sumiu" => "sumiu",
        _ => "conhecido",
    };
    public string AgeText
    {
        get
        {
            var d = DateTime.Now - LastSeen;
            return d.TotalSeconds < 5 ? "agora" : d.TotalMinutes < 1 ? $"{(int)d.TotalSeconds} s" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min" : $"{(int)d.TotalHours} h";
        }
    }

    // ---- estado interno de tendencia (nao vai pra UI) ----
    internal double Ema = double.NaN;
    internal double Baseline = double.NaN;
    internal int Streak;
    internal int Direction;
    internal DateTime NewUntil;
    internal DateTime LastUiPush = DateTime.MinValue;

    public void Touch() => OnPropertyChanged(nameof(AgeText));
}
