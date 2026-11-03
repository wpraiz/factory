using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.UI.Dispatching;

namespace Factory.Services;

/// <summary>
/// Telemetria AO VIVO da secao SEGURANCA (realtime charts, 3 columns). Tudo em processo, sem
/// PowerShell: bytes da rede (NetworkInterface), conexoes TCP estabelecidas (IPGlobalProperties) e processos que
/// nasceram (diferenca de PIDs). 1 amostra por segundo, so enquanto a tela esta aberta com o AO VIVO ligado.
/// Janela de 90 s em memoria; nada vai pra disco.
/// </summary>
public sealed class LiveTelemetry
{
    public const int Janela = 90;

    public sealed record Nascido(DateTime Quando, string Nome, int Pid);

    public List<double> RedeIn { get; } = [];     // KB/s
    public List<double> RedeOut { get; } = [];    // KB/s
    public List<double> Conexoes { get; } = [];   // TCP estabelecidas
    public List<double> Nascimentos { get; } = [];// processos novos por segundo
    public List<Nascido> Recentes { get; } = [];  // ultimos que nasceram (mais novo primeiro)
    public List<string> NomesPorSegundo { get; } = []; // alinhado com Nascimentos: quem nasceu naquele segundo (hover)
    public int RemotosUnicos { get; private set; }
    public int Processos { get; private set; }
    public bool Rodando => _timer?.IsRunning == true;

    public event EventHandler? Tick;

    private DispatcherQueueTimer? _timer;
    private long _lastIn = -1, _lastOut = -1;
    private HashSet<int>? _pids;
    private DateTime _lastAt;
    private bool _busy;

    public void Start(DispatcherQueue dq)
    {
        if (Rodando) return;
        _timer ??= dq.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        // religar = comecar do zero: sem pico inventado de bytes nem "nasceram 40" com o que abriu durante a pausa
        _lastIn = _lastOut = -1;
        _pids = null;
        _lastAt = default;
        foreach (var l in new[] { RedeIn, RedeOut, Conexoes, Nascimentos }) l.Clear();
        NomesPorSegundo.Clear();
        Recentes.Clear();
        _timer.Start();
        OnTick(_timer, null);
    }

    public void Stop() => _timer?.Stop();

    private async void OnTick(DispatcherQueueTimer t, object? _)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var s = await Task.Run(Amostrar);
            Push(RedeIn, s.In); Push(RedeOut, s.Out); Push(Conexoes, s.Tcp); Push(Nascimentos, s.Novos.Count);
            NomesPorSegundo.Add(string.Join(", ", s.Novos.Select(n => n.Nome).Distinct().Take(4)));
            if (NomesPorSegundo.Count > Janela) NomesPorSegundo.RemoveAt(0);
            RemotosUnicos = s.Remotos;
            Processos = s.Total;
            foreach (var n in s.Novos) Recentes.Insert(0, n);
            if (Recentes.Count > 8) Recentes.RemoveRange(8, Recentes.Count - 8);
            Tick?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { App.LogCrash("LiveTelemetry", ex); }
        finally { _busy = false; }
    }

    private (double In, double Out, int Tcp, int Remotos, int Total, List<Nascido> Novos) Amostrar()
    {
        var agora = DateTime.UtcNow;
        var dt = _lastAt == default ? 1 : Math.Max(0.2, (agora - _lastAt).TotalSeconds);
        _lastAt = agora;

        long bin = 0, bout = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try { var st = ni.GetIPStatistics(); bin += st.BytesReceived; bout += st.BytesSent; } catch (Exception) { }
        }
        var kin = _lastIn < 0 ? 0 : Math.Max(0, (bin - _lastIn) / 1024.0 / dt);
        var kout = _lastOut < 0 ? 0 : Math.Max(0, (bout - _lastOut) / 1024.0 / dt);
        _lastIn = bin; _lastOut = bout;

        var tcp = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
            .Where(c => c.State == TcpState.Established && !System.Net.IPAddress.IsLoopback(c.RemoteEndPoint.Address)).ToList();

        var procs = Process.GetProcesses();
        var pids = procs.Select(p => p.Id).ToHashSet();
        var novos = new List<Nascido>();
        if (_pids is not null)
            foreach (var p in procs.Where(p => !_pids.Contains(p.Id)))
                novos.Add(new Nascido(DateTime.Now, p.ProcessName, p.Id));
        _pids = pids;
        foreach (var p in procs) p.Dispose();

        return (kin, kout, tcp.Count, tcp.Select(c => c.RemoteEndPoint.Address).Distinct().Count(), pids.Count, novos);
    }

    private static void Push(List<double> l, double v)
    {
        l.Add(v);
        if (l.Count > Janela) l.RemoveAt(0);
    }
}
