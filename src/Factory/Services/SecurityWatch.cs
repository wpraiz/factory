using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Factory.Models;

namespace Factory.Services;

public interface ISecurityWatch
{
    bool Ligado { get; }
    /// <summary>"aprendendo (7/15 min)" | "vigiando" | "desligado" | "ativo em outra instancia do Factory".</summary>
    string Estado { get; }
    int NaoVistos { get; }
    int NaoVistosSev { get; }
    event EventHandler? Mudou;
    void Iniciar();
    void Ligar(bool ligar);
    IReadOnlyList<SecAlert> Alertas();
    void Visto(string id);
    void VistoTodos();
    void Silenciar(string id, TimeSpan por);
    string BaselineResumo();
    /// <summary>Levanta um alerta CRITICO de mentira pra ele ver toast/pisca/bipe funcionando.</summary>
    void Testar();
    string ResumoAlertas();
}

/// <summary>
/// VIGIA — monitoramento continuo: alert only on what makes sense (repetition patterns, anomalous increases). Roda em segundo plano enquanto o Factory esta aberto, sem depender da tela:
///   • a cada 5 s: bytes de rede, conexoes TCP, destinos novos, processos que nasceram (e de onde);
///   • a cada 1 min: compara cada metrica com a linha de base DA HORA DO DIA (media/variancia movel, aprendida aqui,
///     salva em seguranca-baseline.json) e so alerta com piso absoluto + desvio grande + persistencia (3 min seguidos);
///   • a cada 5 min: le o Windows (coletor PowerShell) e compara com a leitura anterior: porta nova, item novo que
///     inicia sozinho, ameaca/ASR novos do Defender, repeticao do mesmo evento, protecao que DESLIGOU.
/// Ruido e o inimigo: cada alerta tem chave e cooldown; repetir sobe a contagem em vez de criar linha; so CRITICO
/// incomoda (toast, pisca, bipe). Tudo so le — nada e bloqueado ou desligado sozinho.
/// Honestidade: sem admin/ETW nao da pra atribuir bytes a um processo; o alerta de rede diz isso.
/// </summary>
public sealed class SecurityWatch : ISecurityWatch
{
    private static readonly string Dir = AppPaths.Factory;
    private static readonly string AlertasPath = Path.Combine(Dir, "seguranca-alertas.json");
    private static readonly string BaselinePath = Path.Combine(Dir, "seguranca-baseline.json");
    private static readonly string ConfigPath = Path.Combine(Dir, "seguranca-vigia.json");
    private static readonly string SilenciosPath = Path.Combine(Dir, "seguranca-silenciados.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private const int AmostraSeg = 5, AmostrasPorMin = 12, MinutosParaAprender = 15, SnapshotMin = 5;
    private static readonly string[] Lolbins = ["powershell", "pwsh", "cmd", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "certutil", "bitsadmin", "schtasks", "reg", "whoami", "net", "net1", "nltest", "wmic"];
    private static readonly string[] Raros = ["mshta", "certutil", "bitsadmin", "installutil", "regasm", "regsvcs", "odbcconf"];
    private static readonly string[] PastasDeRisco = [@"\Temp\", @"\Users\Public\", @"\Downloads\"];

    private sealed class Estat
    {
        public double Mean { get; set; }
        public double Var { get; set; }
        public int N { get; set; }
        public void Add(double x)
        {
            N++;
            var a = Math.Max(1.0 / N, 0.05);
            // winsoriza: um minuto absurdo nao envenena a linha de base
            if (N > 20) x = Math.Min(x, Mean + 4 * Math.Sqrt(Var + 1e-6));
            var d = x - Mean;
            Mean += a * d;
            Var = (1 - a) * (Var + a * d * d);
        }
    }

    private readonly IHostSecurityService _svc;
    private readonly object _lock = new();
    private List<SecAlert> _alertas = [];
    private Dictionary<string, DateTimeOffset> _silencios = [];
    private Dictionary<string, Estat> _base = [];
    private CancellationTokenSource? _cts;
    private Mutex? _mutex;
    private bool _outraInstancia;
    private bool _ligado = true;

    // amostrador
    private long _lastIn = -1, _lastOut = -1;
    private DateTime _lastAt;
    private HashSet<int>? _pids;
    private readonly HashSet<string> _ipsVistos = [];
    private readonly List<double> _outMin = [], _inMin = [];
    private int _tcpMax, _novosIp, _nAmostras, _egressMin;
    private readonly Dictionary<string, int> _nascMin = new(StringComparer.OrdinalIgnoreCase);
    private int _nascTotalMin;
    private DateTime _proxSnap = DateTime.MinValue, _proxSalvaBase = DateTime.UtcNow.AddMinutes(10);
    private int _minutosVigiados;

    // diff de snapshots
    private HashSet<string>? _prevPortas, _prevPers;
    private Dictionary<string, string>? _prevPostura;

    public bool Ligado => _ligado && !_outraInstancia;
    public string Estado => _outraInstancia ? "ativo em outra instância do Factory"
        : !_ligado ? "desligado (alavanca VIGIA)"
        : _minutosVigiados < MinutosParaAprender ? $"aprendendo o seu normal ({_minutosVigiados}/{MinutosParaAprender} min): sem alerta de rede ainda" : "vigiando";
    public int NaoVistos { get { lock (_lock) return _alertas.Count(a => !a.Visto && a.Sev >= 2); } }
    public int NaoVistosSev { get { lock (_lock) return _alertas.Where(a => !a.Visto && a.Sev >= 2).Select(a => a.Sev).DefaultIfEmpty(0).Max(); } }
    public event EventHandler? Mudou;

    public SecurityWatch(IHostSecurityService svc)
    {
        _svc = svc;
        _alertas = Load<List<SecAlert>>(AlertasPath) ?? [];
        _silencios = Load<Dictionary<string, DateTimeOffset>>(SilenciosPath) ?? [];
        _base = Load<Dictionary<string, Estat>>(BaselinePath) ?? [];
        _ligado = Load<Config>(ConfigPath)?.Ligado ?? true;
        _minutosVigiados = _base.TryGetValue("out|*", out var e) ? Math.Min(e.N, MinutosParaAprender) : 0;
    }

    private sealed class Config { public bool Ligado { get; set; } = true; }

    // ---------- ciclo de vida ----------

    public void Iniciar()
    {
        if (_cts is not null) return;
        // So UMA instancia do Factory vigia (prod e homolog abertos juntos gravariam por cima um do outro).
        _mutex = new Mutex(false, @"Local\factory-secwatch");
        bool dono;
        try { dono = _mutex.WaitOne(0); } catch (AbandonedMutexException) { dono = true; }
        if (!dono) { _outraInstancia = true; Mudou?.Invoke(this, EventArgs.Empty); return; }
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Ligar(bool ligar)
    {
        _ligado = ligar;
        Save(ConfigPath, new Config { Ligado = ligar });
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct).ContinueWith(_ => { });   // deixa o app subir primeiro
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_ligado)
                {
                    Amostrar();
                    if (++_nAmostras % AmostrasPorMin == 0) Minuto();
                    if (DateTime.UtcNow >= _proxSnap) { _proxSnap = DateTime.UtcNow.AddMinutes(SnapshotMin); await SnapshotAsync(ct); }
                    if (DateTime.UtcNow >= _proxSalvaBase) { _proxSalvaBase = DateTime.UtcNow.AddMinutes(10); lock (_lock) Save(BaselinePath, _base); }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { App.LogCrash("SecurityWatch", ex); }
            try { await Task.Delay(TimeSpan.FromSeconds(AmostraSeg), ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ---------- amostra de 5 s ----------

    private void Amostrar()
    {
        var agora = DateTime.UtcNow;
        var dt = _lastAt == default ? AmostraSeg : Math.Max(1, (agora - _lastAt).TotalSeconds);
        _lastAt = agora;

        long bin = 0, bout = 0;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try { var st = ni.GetIPStatistics(); bin += st.BytesReceived; bout += st.BytesSent; } catch (Exception) { }
        }
        if (_lastIn >= 0)
        {
            _inMin.Add(Math.Max(0, (bin - _lastIn) / 1024.0 / dt));
            _outMin.Add(Math.Max(0, (bout - _lastOut) / 1024.0 / dt));
        }
        _lastIn = bin; _lastOut = bout;

        var tcp = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
            .Where(c => c.State == TcpState.Established && !System.Net.IPAddress.IsLoopback(c.RemoteEndPoint.Address)).ToList();
        _tcpMax = Math.Max(_tcpMax, tcp.Count);
        foreach (var ip in tcp.Select(c => c.RemoteEndPoint.Address.ToString()).Distinct())
            if (_ipsVistos.Add(ip) && _minutosVigiados >= 2) _novosIp++;

        var procs = Process.GetProcesses();
        try
        {
            var pids = procs.Select(p => p.Id).ToHashSet();
            if (_pids is not null)
                foreach (var p in procs.Where(p => !_pids.Contains(p.Id)))
                {
                    var nome = SafeName(p);
                    _nascMin[nome] = _nascMin.GetValueOrDefault(nome) + 1;
                    _nascTotalMin++;
                    VerificaNascido(p, nome);
                }
            _pids = pids;
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    private static string SafeName(Process p) { try { return p.ProcessName; } catch (Exception) { return "?"; } }

    /// <summary>Processo recem-nascido rodando de Temp/Public/Downloads e que ele nunca confiou = vale olhar.</summary>
    private void VerificaNascido(Process p, string nome)
    {
        string? path = null;
        try { path = p.MainModule?.FileName; } catch (Exception) { }
        if (path is null || !PastasDeRisco.Any(r => path.Contains(r, StringComparison.OrdinalIgnoreCase))) return;
        if (path.Contains(@"\Temp\claude", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\dotnet\", StringComparison.OrdinalIgnoreCase)) return;
        if (_svc.EhConfiavel(nome)) return;
        Levantar($"pasta:{path.ToLowerInvariant()}", "processos", 2, $"Programa abriu de pasta de risco: {nome}",
            $"{path}\nTemp/Public/Downloads é onde instalador e malware costumam rodar. Veja o arquivo antes de confiar.", TimeSpan.FromHours(6));
    }

    // ---------- agregado de 1 min ----------

    private void Minuto()
    {
        var hora = DateTime.Now.Hour;
        var outAvg = _outMin.Count > 0 ? _outMin.Average() : 0;
        var inAvg = _inMin.Count > 0 ? _inMin.Average() : 0;
        var tcp = _tcpMax; var novosIp = _novosIp; var nasc = _nascTotalMin;
        var porNome = _nascMin.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        _outMin.Clear(); _inMin.Clear(); _tcpMax = 0; _novosIp = 0; _nascTotalMin = 0; _nascMin.Clear();

        lock (_lock)
        {
            var aprendeu = _minutosVigiados >= MinutosParaAprender;

            // 1) upload sustentado (so com piso absoluto: 1,5 MB/s) — 3 min seguidos acima do normal desta hora
            var (anomOut, mOut, sdOut) = Anomalo("out", hora, outAvg, 5);
            _egressMin = aprendeu && anomOut && outAvg > 1500 ? _egressMin + 1 : 0;
            if (_egressMin >= 3)
                Levantar("rede:upload", "rede", outAvg > 8000 && _egressMin >= 10 ? 3 : 2,
                    $"Upload alto e sustentado: {Kb(outAvg)} há {_egressMin} min",
                    $"O normal desta hora é ~{Kb(mOut)} (±{Kb(sdOut)}). Não dá pra dizer QUAL programa sem admin/ETW; abra o Gerenciador de Tarefas → Rede. Se você não está subindo arquivos/backup, investigue.",
                    TimeSpan.FromMinutes(30));

            // 2) conexoes TCP disparando
            var (anomTcp, mTcp, _) = Anomalo("tcp", hora, tcp, 5);
            if (aprendeu && anomTcp && tcp >= 80 && tcp > mTcp * 2 + 30)
                Levantar("rede:tcp", "rede", 2, $"Conexões abertas dispararam: {tcp}",
                    $"O normal desta hora é ~{mTcp:0}. Muitas conexões de uma vez pode ser download pesado, app travado em loop, ou varredura.", TimeSpan.FromMinutes(30));

            // 3) muitos destinos NOVOS num minuto (fan-out / varredura)
            var (anomIp, mIp, _) = Anomalo("novosIp", hora, novosIp, 5);
            if (aprendeu && anomIp && novosIp >= 40)
                Levantar("rede:fanout", "rede", 2, $"{novosIp} destinos novos em 1 minuto",
                    $"Normal: ~{mIp:0}/min. Um programa falando com dezenas de endereços inéditos de uma vez lembra varredura ou botnet — ou só um site pesado novo (navegador).", TimeSpan.FromMinutes(30));

            // 4) rajada de processos
            var (anomNasc, mNasc, _) = Anomalo("births", hora, nasc, 5);
            if (aprendeu && anomNasc && nasc >= 60)
                Levantar("proc:rajada", "processos", 2, $"Rajada: {nasc} processos nasceram em 1 minuto",
                    $"Normal: ~{mNasc:0}/min. Pode ser build/instalador; se você não estava fazendo nada, olhe a aba ACHADOS.", TimeSpan.FromMinutes(30));

            // 5) repeticao por nome (o mesmo programa nascendo de novo e de novo)
            foreach (var (nome, n) in porNome)
            {
                var lol = Lolbins.Contains(nome, StringComparer.OrdinalIgnoreCase);
                var minimo = lol ? 6 : 15;
                if (Raros.Contains(nome, StringComparer.OrdinalIgnoreCase))
                    Levantar($"proc:raro:{nome.ToLowerInvariant()}", "processos", 2, $"Programa raro abriu: {nome}",
                        $"{nome}.exe é muito usado em ataques (baixar/rodar coisa por script) e quase nunca em uso normal.", TimeSpan.FromHours(6));
                else if (n >= minimo)
                {
                    var (anom, m, _) = Anomalo("nome|" + nome.ToLowerInvariant(), hora, n, 4);
                    var sem = !HaBase("nome|" + nome.ToLowerInvariant(), hora);
                    if (aprendeu && (anom || (sem && n >= minimo * 2)))
                        Levantar($"proc:rep:{nome.ToLowerInvariant()}", "processos", lol ? 2 : 1,
                            $"{nome} nasceu {n}x em 1 minuto" + (m > 0.5 ? $" (normal ~{m:0.#})" : ""),
                            lol ? "Interpretador/ferramenta de sistema repetindo em loop: típico de script malicioso agendado — ou do seu próprio hook/automação."
                                : "Mesmo programa abrindo e fechando em loop (crash-loop ou instalador).", TimeSpan.FromMinutes(30));
                }
            }

            // atualiza a linha de base (hora do dia + global) DEPOIS de avaliar
            Aprende("out", hora, outAvg); Aprende("in", hora, inAvg); Aprende("tcp", hora, tcp);
            Aprende("novosIp", hora, novosIp); Aprende("births", hora, nasc);
            foreach (var (nome, n) in porNome) Aprende("nome|" + nome.ToLowerInvariant(), hora, n);
            _minutosVigiados = _base.TryGetValue("out|*", out var g) ? g.N : 0;
        }
    }

    private bool HaBase(string metrica, int hora) =>
        (_base.TryGetValue($"{metrica}|h{hora}", out var h) && h.N >= 20) || (_base.TryGetValue($"{metrica}|*", out var g) && g.N >= MinutosParaAprender);

    /// <summary>(anomalo?, media, desvio). Sem linha de base suficiente = nunca anomalo.</summary>
    private (bool, double, double) Anomalo(string metrica, int hora, double x, double sigma)
    {
        if (!(_base.TryGetValue($"{metrica}|h{hora}", out var e) && e.N >= 20) && !(_base.TryGetValue($"{metrica}|*", out e) && e.N >= MinutosParaAprender))
            return (false, 0, 0);
        var sd = Math.Sqrt(e.Var + 1e-6);
        return (x > e.Mean + sigma * sd, e.Mean, sd);
    }

    private void Aprende(string metrica, int hora, double x)
    {
        foreach (var k in new[] { $"{metrica}|h{hora}", $"{metrica}|*" })
        {
            if (!_base.TryGetValue(k, out var e)) _base[k] = e = new Estat();
            e.Add(x);
        }
    }

    // ---------- leitura do Windows a cada 5 min ----------

    private async Task SnapshotAsync(CancellationToken ct)
    {
        HostSnapshot s;
        try { s = await _svc.GetAsync(force: true, ct); }
        catch (Exception ex) { if (ex is OperationCanceledException) throw; return; }

        var portas = _svc.Portas(s);
        var pers = _svc.Persistencia(s);
        var primeira = _prevPortas is null;

        lock (_lock)
        {
            // porta nova escutando / item novo que inicia sozinho
            foreach (var r in portas)
            {
                var nova = !(_prevPortas?.Contains(r.Key) ?? false);
                int sev;
                if (r.Estado == SecRow.Novo && (nova || primeira)) sev = r.Risco ? 3 : 2;
                else if (r.Estado == SecRow.Revisar && nova && !primeira) sev = r.Risco ? 3 : 2;
                else continue;
                Levantar($"novo:porta:{r.Key.ToLowerInvariant()}", "rede", sev,
                    $"{(r.Estado == SecRow.Novo ? "Programa NOVO" : "Programa")} escutando na rede: {r.Titulo}",
                    $"{r.Detalhe}\nAba ACHADOS: POR QUÊ? · CONFIAR · BLOQUEAR.", TimeSpan.FromHours(24));
            }
            foreach (var r in pers)
            {
                var nova = !(_prevPers?.Contains($"{r.Tipo}:{r.Key}") ?? false);
                int sev;
                if (r.Estado == SecRow.Novo && (nova || primeira)) sev = r.Risco ? 3 : 2;
                else if (nova && !primeira && r.Estado is SecRow.Revisar or SecRow.Orfao) sev = r.Risco ? 3 : 2;   // apareceu desde a ultima leitura
                else if (primeira && r.Risco && r.Estado == SecRow.Revisar) sev = 2;                                // ja existia com risco: avisa 1 vez, sem alarde
                else continue;
                Levantar($"novo:inicia:{r.Tipo}:{r.Key.ToLowerInvariant()}", "inicia", sev,
                    $"{(r.Estado == SecRow.Novo ? "Item NOVO inicia sozinho" : r.Risco ? "Inicia sozinho com risco" : "Item inicia sozinho")}: {r.Titulo}",
                    $"{r.Detalhe}\nAba ACHADOS: POR QUÊ? · CONFIAR · DESLIGAR.", TimeSpan.FromHours(24));
            }

            // eventos do Defender (cada um 1 vez, persistido; antigos viram historico)
            foreach (var e in s.Eventos)
            {
                var idade = DateTimeOffset.Now - e.Quando;
                var ev = HostSecurityService.Evento(e);
                var sev = e.EventId == 1116 ? 3 : e.EventId is 1117 or 1121 ? 2 : 1;
                var antigo = idade > TimeSpan.FromHours(6);
                if (antigo) sev = 1;
                else if (idade > TimeSpan.FromHours(2) && sev == 3) sev = 2;
                Levantar($"ev:{e.EventId}:{e.Quando.UtcTicks}", "defender", sev, ev.Titulo, $"{ev.Quando} · {ev.Detalhe}", TimeSpan.FromDays(30), visto: antigo);
            }
            // repeticao: mesmo evento 3x+ na ultima hora
            foreach (var g in s.Eventos.Where(e => DateTimeOffset.Now - e.Quando < TimeSpan.FromHours(1) && e.EventId != 1122)
                         .GroupBy(e => $"{e.EventId}|{(e.Ameaca.Length > 0 ? e.Ameaca : e.Regra)}|{Path.GetFileName(e.Processo)}").Where(g => g.Count() >= 3))
            {
                var e = g.First(); var ev = HostSecurityService.Evento(e);
                Levantar($"rep:{g.Key}:{DateTime.Now:yyyyMMddHH}", "defender", e.EventId is 1116 or 1117 ? 3 : 2,
                    $"Repetiu {g.Count()}x na última hora: {ev.Titulo}", $"{ev.Detalhe}\nAlgo está tentando de novo e de novo.", TimeSpan.FromHours(1));
            }

            // protecao que DESLIGOU (verde -> ambar entre duas leituras)
            var postura = HostSecurityService.Postura(s).ToDictionary(b => b.Rotulo, b => b.Accent);
            if (_prevPostura is not null)
                foreach (var (rot, acc) in postura)
                    if (acc == "amber" && _prevPostura.GetValueOrDefault(rot) == "green")
                        Levantar($"postura:{rot}:{DateTime.Now:yyyyMMddHH}", "postura", 3, $"{rot} DESLIGOU", $"Estava ligado na leitura anterior e agora não está. Selo na VISÃO GERAL abre a tela do Windows.", TimeSpan.FromHours(1));
            _prevPostura = postura;

            _prevPortas = portas.Select(r => r.Key).ToHashSet();
            _prevPers = pers.Select(r => $"{r.Tipo}:{r.Key}").ToHashSet();
        }
    }

    // ---------- alertas ----------

    private void Levantar(string chave, string fonte, int sev, string titulo, string detalhe, TimeSpan cooldown, bool visto = false)
    {
        if (sev <= 0) return;
        bool notificar = false; SecAlert? alvo = null;
        lock (_lock)
        {
            if (_silencios.TryGetValue(chave, out var ate) && ate > DateTimeOffset.Now) return;
            var agora = DateTimeOffset.Now;
            var ex = _alertas.FirstOrDefault(a => a.Chave == chave);
            if (ex is not null)
            {
                if (chave.StartsWith("ev:")) return;   // evento do Defender ja alertado (persistido): nunca de novo
                var passou = agora - ex.Ultima;
                ex.Ultima = agora; ex.Contagem++; ex.Detalhe = detalhe;
                if (sev > ex.Sev) { ex.Sev = sev; ex.Visto = false; notificar = ex.Sev >= 3; }
                else if (passou >= cooldown) { ex.Visto = false; notificar = ex.Sev >= 3; }   // voltou depois de quieto: reabre
                else if (ex.Contagem is 3 or 10 or 30 && ex.Sev >= 2) ex.Visto = false;       // escalada por repeticao
                alvo = ex;
            }
            else
            {
                alvo = new SecAlert { Chave = chave, Fonte = fonte, Sev = sev, Titulo = titulo, Detalhe = detalhe, Visto = visto };
                _alertas.Add(alvo);
                if (_alertas.Count > 300) _alertas = _alertas.OrderByDescending(a => a.Ultima).Take(300).ToList();
                notificar = sev >= 3 && !visto;
            }
            Persistir();
        }
        Mudou?.Invoke(this, EventArgs.Empty);
        if (notificar && alvo is not null) Notificar(alvo);
    }

    private void Persistir() => Save(AlertasPath, _alertas);

    public IReadOnlyList<SecAlert> Alertas()
    {
        lock (_lock) return _alertas.OrderBy(a => a.Visto).ThenByDescending(a => a.Sev).ThenByDescending(a => a.Ultima).Take(120).ToList();
    }

    public void Visto(string id)
    {
        lock (_lock) { var a = _alertas.FirstOrDefault(x => x.Id == id); if (a is null) return; a.Visto = true; Persistir(); }
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    public void VistoTodos()
    {
        lock (_lock) { foreach (var a in _alertas) a.Visto = true; Persistir(); }
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    public void Silenciar(string id, TimeSpan por)
    {
        lock (_lock)
        {
            var a = _alertas.FirstOrDefault(x => x.Id == id);
            if (a is null) return;
            _silencios[a.Chave] = DateTimeOffset.Now + por;
            a.Visto = true;
            Persistir(); Save(SilenciosPath, _silencios);
        }
        Mudou?.Invoke(this, EventArgs.Empty);
    }

    public void Testar() =>
        Levantar($"teste:{DateTime.Now.Ticks}", "teste", 3, "TESTE: alerta crítico de mentira", "Se você viu o toast, a barra de tarefas piscou e ouviu o bipe, o vigia consegue te chamar de verdade.", TimeSpan.Zero);

    public string ResumoAlertas()
    {
        var l = Alertas().Where(a => !a.Visto && a.Sev >= 2).Take(5).ToList();
        return l.Count == 0 ? $"Vigia: {Estado}. Nenhum alerta pendente."
            : $"Vigia: {Estado}. {NaoVistos} alerta(s) pendente(s): " + string.Join("; ", l.Select(a => $"[{a.SevTexto}] {a.Titulo}{(a.Contagem > 1 ? $" ({a.Contagem}x)" : "")}"));
    }

    public string BaselineResumo()
    {
        lock (_lock)
        {
            var h = DateTime.Now.Hour;
            string L(string nome, string m, Func<double, string> f)
            {
                var e = _base.TryGetValue($"{m}|h{h}", out var x) && x.N >= 20 ? x : _base.GetValueOrDefault($"{m}|*");
                return e is null || e.N < 3 ? $"{nome}: ainda sem dados" : $"{nome}: normal ~{f(e.Mean)} (±{f(Math.Sqrt(e.Var))}) · {e.N} min medidos";
            }
            return string.Join("\n", L("upload", "out", Kb), L("download", "in", Kb), L("conexões TCP", "tcp", v => v.ToString("0")),
                L("destinos novos/min", "novosIp", v => v.ToString("0.#")), L("processos nascendo/min", "births", v => v.ToString("0.#")));
        }
    }

    // ---------- notificacao (so CRITICO) ----------

    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
    [StructLayout(LayoutKind.Sequential)] private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    private static bool _toastRegistrado;

    private static void Notificar(SecAlert a)
    {
        try { Task.Run(() => { Console.Beep(880, 140); Console.Beep(660, 140); Console.Beep(880, 220); }); } catch (Exception) { }
        App.Window?.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var h = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
                var f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = h, dwFlags = 3 | 12, uCount = 6, dwTimeout = 0 };   // barra + janela, ate focar
                FlashWindowEx(ref f);
            }
            catch (Exception) { }
            try
            {
                var mgr = Microsoft.Windows.AppNotifications.AppNotificationManager.Default;
                if (!_toastRegistrado) { mgr.Register(); _toastRegistrado = true; }
                mgr.Show(new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                    .AddText("Factory · Segurança · " + a.SevTexto).AddText(a.Titulo).AddText(a.Detalhe.Split('\n')[0]).BuildNotification());
            }
            catch (Exception ex) { App.LogCrash("SecurityWatch.toast", ex); }
        });
    }

    // ---------- util ----------

    private static string Kb(double kb) => kb >= 1024 ? $"{kb / 1024:0.0} MB/s" : $"{kb:0} KB/s";

    private static T? Load<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null; }
        catch (Exception) { return null; }
    }

    private static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception) { /* historico e conforto */ }
    }
}
