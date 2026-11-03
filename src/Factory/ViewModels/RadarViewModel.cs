using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Factory.Models;
using Factory.Services;

namespace Factory.ViewModels;

/// <summary>
/// RADAR — "tem gente perto?". Mostrador com aneis de distancia + lista ordenada por proximidade.
/// Bluetooth LE em tempo real, Wi-Fi a cada 10 s, rede local a cada 15 s. NOVO = nunca visto.
/// Bipe so pra NOVO perto (≤10 m) ou alguem entrando na minha rede, e so com o BIPE ligado.
/// </summary>
public sealed partial class RadarViewModel : ObservableObject
{
    private readonly IRadarService _radar;

    public ObservableCollection<RadarTarget> Targets => _radar.Targets;
    public ObservableCollection<string> Events => _radar.Events;
    /// <summary>Presentes primeiro, por distancia; sumidos no fim. Sincronizada por Move (sem recriar a lista).</summary>
    public ObservableCollection<RadarTarget> Sorted { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanLabel))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BeepLabel))]
    private bool _beepEnabled = true;

    [ObservableProperty] private bool _sweeping;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _sources = "";
    [ObservableProperty] private int _nearCount;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private int _presentCount;

    public string ScanLabel => IsScanning ? "■ PARAR" : "▶ VARRER";
    public string BeepLabel => BeepEnabled ? "BIPE ON" : "BIPE OFF";

    public RadarViewModel(IRadarService radar)
    {
        _radar = radar;
        _radar.Tick += (_, _) => Refresh();
        Refresh();
    }

    [RelayCommand]
    private async Task ToggleScanAsync()
    {
        if (_radar.IsScanning) _radar.Stop();
        else await _radar.StartAsync();
        IsScanning = _radar.IsScanning;
        Refresh();
    }

    [RelayCommand]
    private void ToggleBeep()
    {
        BeepEnabled = !BeepEnabled;
        _radar.BeepEnabled = BeepEnabled;
    }

    [RelayCommand]
    private async Task SweepLanAsync()
    {
        if (Sweeping) return;
        Sweeping = true;
        try { await _radar.SweepLanAsync(); }
        finally { Sweeping = false; }
    }

    [RelayCommand]
    private void AcknowledgeAll() { _radar.AcknowledgeAll(); Refresh(); }

    /// <summary>Chamado no Tick do servico (thread de UI).</summary>
    public void Refresh()
    {
        IsScanning = _radar.IsScanning;
        var present = Targets.Where(t => t.Present).ToList();
        PresentCount = present.Count;
        NearCount = present.Count(t => t.Kind != RadarTarget.KindLan && t.Distance <= 5);
        NewCount = present.Count(t => t.IsNew);
        var ble = present.Count(t => t.Kind == RadarTarget.KindBle);
        var wifi = present.Count(t => t.Kind == RadarTarget.KindWifi);
        var lan = present.Count(t => t.Kind == RadarTarget.KindLan);

        Headline = !IsScanning
            ? "parado — VARRER liga Bluetooth LE, Wi-Fi e rede local"
            : NearCount == 0
                ? $"ninguem a menos de 5 m · {present.Count} radios no ar"
                : $"{NearCount} {(NearCount == 1 ? "aparelho" : "aparelhos")} a menos de 5 m · {NewCount} novos";
        Sources = $"BLE {ble} · {_radar.BluetoothState}   |   Wi-Fi {wifi} · {_radar.WifiState}   |   rede {lan} · {_radar.LanState}";

        var order = Targets
            .OrderByDescending(t => t.Present)
            .ThenByDescending(t => t.IsNew)
            .ThenBy(t => t.Kind == RadarTarget.KindLan ? 1 : 0)
            .ThenBy(t => t.Distance)
            .ToList();
        Sync(order);
    }

    private void Sync(List<RadarTarget> order)
    {
        foreach (var t in Sorted.ToList()) if (!order.Contains(t)) Sorted.Remove(t);
        for (var i = 0; i < order.Count; i++)
        {
            var t = order[i];
            var j = Sorted.IndexOf(t);
            if (j < 0) Sorted.Insert(Math.Min(i, Sorted.Count), t);
            else if (j != i) Sorted.Move(j, i);
        }
    }
}
