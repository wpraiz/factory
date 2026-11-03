using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Factory.Models;

namespace Factory.Services;

public interface IHostSecurityService
{
    HostSnapshot? Last { get; }
    string AllowlistPath { get; }
    bool AuditoriaRodando { get; }
    /// <summary>Le o host (PowerShell, ~7 s). Guarda 60 s: chamar de novo nesse prazo devolve o mesmo retrato.</summary>
    Task<HostSnapshot> GetAsync(bool force = false, CancellationToken ct = default);
    IReadOnlyList<SecRow> Portas(HostSnapshot s);
    IReadOnlyList<SecRow> Persistencia(HostSnapshot s);
    /// <summary>Writes to the allowlist (user data, &lt;data&gt;\factory). Returns what was added.</summary>
    string Aprovar(IEnumerable<SecRow> rows);
    /// <summary>Tira da allowlist o que o ultimo Aprovar gravou (botao DESFAZER).</summary>
    string Desfazer();
    bool PodeDesfazer { get; }
    /// <summary>HardeningKitty em modo Audit (so le), elevado: o Windows pede o UAC e ele decide.</summary>
    Task<string> RodarAuditoriaAsync();
    /// <summary>Resumo falado pro Odin (mao `seguranca`).</summary>
    Task<string> ResumoAsync();
    /// <summary>DESLIGAR (tarefa/Run/Inicializar) ou BLOQUEAR (porta, regra de firewall). Sempre reversivel.</summary>
    Task<string> CortarAsync(SecRow row);
    /// <summary>RELIGAR/LIBERAR o que o Factory desligou/bloqueou.</summary>
    Task<string> ReverterAsync(SecRow row);
    /// <summary>Is the process (name only) in the user's allowlist? The watcher does not alert on what the user already trusts.</summary>
    bool EhConfiavel(string processo);
    string NotaDe(string chave);
    void SetNota(string chave, string texto);
    /// <summary>Tudo que merece olho numa lista so: portas, inicia sozinho, eventos do Defender e protecoes desligadas.</summary>
    IReadOnlyList<SecRow> Achados(HostSnapshot s, bool tudo);
}

/// <summary>
/// SECURITY (guide "Odin Host Security"): postura do Windows, Defender/ASR, persistencia,
/// portas e HardeningKitty. No guia era Node + HTTP + React; aqui o Factory e WinUI, entao o "backend" e este
/// servico e a "API" e a mao `seguranca` do Odin. So LE o sistema — o unico jeito de escrever e a allowlist
/// (CONFIAR), o relatorio do HardeningKitty e DESLIGAR/BLOQUEAR (sempre por clique com confirmacao,
/// reversivel: o que foi feito fica em seguranca-acoes.json pra RELIGAR/LIBERAR). Tudo em &lt;data&gt;\factory\.
/// </summary>
public sealed class HostSecurityService : IHostSecurityService
{
    private static readonly string Dir = AppPaths.Factory;
    public string AllowlistPath { get; } = Path.Combine(Dir, "security-allowlist.json");
    private static readonly string VistosPath = Path.Combine(Dir, "seguranca-vistos.json");
    private static readonly string HardeningDir = Path.Combine(Dir, "hardening");
    private static readonly string NotasPath = Path.Combine(Dir, "seguranca-notas.json");
    /// <summary>User notes (key -> text). Static because Postura() is static.</summary>
    public static Dictionary<string, string> Notas { get; private set; } = [];
    private static readonly string AcoesPath = Path.Combine(Dir, "seguranca-acoes.json");
    private static readonly string AutostartDir = Path.Combine(Dir, "autostart-desligados");
    private static readonly string Script = Path.Combine(AppContext.BaseDirectory, "Assets", "security", "collect-host-security.ps1");
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    /// <summary>Visto pela primeira vez ha menos disso (e depois da primeira leitura) = NOVO.</summary>
    private static readonly TimeSpan JanelaNovo = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> NomesDoWindows = new(StringComparer.OrdinalIgnoreCase)
        { "svchost", "lsass", "services", "wininit", "spoolsv", "csrss", "smss", "winlogon", "explorer", "dasHost", "System" };
    private static readonly string[] Lolbins = ["powershell", "pwsh", "cmd.exe", "wscript", "cscript", "mshta", "rundll32", "regsvr32"];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastAt;
    private SecurityAllowlist _allow;
    private Dictionary<string, DateTimeOffset> _vistos;
    private bool _baseline;   // false so ate a primeira leitura da vida (seguranca-vistos.json ainda nao existia)

    public HostSnapshot? Last { get; private set; }
    public bool AuditoriaRodando { get; private set; }

    public HostSecurityService()
    {
        _allow = LoadAllow();
        Notas = LoadJson<Dictionary<string, string>>(NotasPath) ?? SeedNotas();
        _vistos = LoadJson<Dictionary<string, DateTimeOffset>>(VistosPath) ?? [];
        _baseline = _vistos.Count > 0;
    }

    // ---------- leitura ----------

    public async Task<HostSnapshot> GetAsync(bool force = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!force && Last is not null && DateTime.UtcNow - _lastAt < Ttl) return Last;
            var sw = Stopwatch.StartNew();
            var json = await RunCollectorAsync(ct);
            var snap = JsonSerializer.Deserialize<HostSnapshot>(json, Json) ?? throw new InvalidOperationException("coletor devolveu vazio");
            snap.Ms = sw.ElapsedMilliseconds;
            _allow = LoadAllow();   // ele pode ter editado o arquivo na mao
            MarcarVistos(snap);
            Last = snap;
            _lastAt = DateTime.UtcNow;
            return snap;
        }
        finally { _gate.Release(); }
    }

    private static async Task<string> RunCollectorAsync(CancellationToken ct)
    {
        if (!File.Exists(Script)) throw new FileNotFoundException("coletor nao esta junto do app", Script);
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Script}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        // Factory started from pwsh 7 inherits its PSModulePath, and Windows PowerShell 5.1 then cannot load
        // Microsoft.PowerShell.Security (Get-AuthenticodeSignature) -> the collector died. Without the variable 5.1 uses its own.
        psi.Environment.Remove("PSModulePath");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell nao abriu");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var outTask = p.StandardOutput.ReadToEndAsync(timeout.Token);
        var errTask = p.StandardError.ReadToEndAsync(timeout.Token);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (Exception) { } throw new TimeoutException("coletor passou de 60 s"); }
        var stdout = (await outTask).Trim();
        var start = stdout.IndexOf('{');
        if (start < 0) throw new InvalidOperationException("coletor sem JSON: " + Primeira((await errTask) is { Length: > 0 } e ? e : stdout));
        return stdout[start..];
    }

    // ---------- classificacao ----------

    public IReadOnlyList<SecRow> Portas(HostSnapshot s)
    {
        var rows = new List<SecRow>();
        foreach (var g in s.Portas.GroupBy(p => (p.Processo, p.Caminho)))
        {
            var nome = g.Key.Processo;
            var pid = g.First().Pid;
            // Nome de processo do Windows rodando FORA de C:\Windows = disfarce classico: nunca vira CONHEÇO nem WINDOWS.
            var disfarce = NomesDoWindows.Contains(nome) && g.Key.Caminho.Length > 0 && !EhWindows(g.Key.Caminho);
            // Porta conhecida so vale quando nao da pra saber o processo (sem nome); com nome, quem decide e o processo.
            var known = !disfarce && (_allow.Processos.Any(a => Match(a, nome)) || (nome == "?" && g.All(p => _allow.Portas.Contains(p.Porta))));
            var sistema = !disfarce && (pid is 0 or 4 || EhWindows(g.Key.Caminho));
            var assin = g.Select(p => p.Assinatura).FirstOrDefault(a => a.Length > 0) ?? "";
            var assinante = g.Select(p => p.Assinante).FirstOrDefault(a => a.Length > 0) ?? "";
            var risco = !sistema && !known && (EmPastaDeRisco(g.Key.Caminho) || assin is not ("" or "Valid"));
            var bloqueado = Acoes().Any(a => a.Tipo == "firewall" && a.Valor.Equals(g.Key.Caminho, StringComparison.OrdinalIgnoreCase) && g.Key.Caminho.Length > 0);
            var estado = bloqueado ? SecRow.Bloqueado : known ? SecRow.Conhecido : sistema ? SecRow.Sistema : disfarce ? SecRow.Novo : NovoOuRevisar("porta:" + nome);
            var tcp = g.Where(p => p.Proto == "TCP").Select(p => p.Porta).Distinct().Order().ToList();
            var udp = g.Where(p => p.Proto == "UDP").Select(p => p.Porta).Distinct().Order().ToList();
            var partes = new List<string>();
            if (tcp.Count > 0) partes.Add("TCP " + Lista(tcp));
            if (udp.Count > 0) partes.Add("UDP " + Lista(udp));
            // 0.0.0.0/:: = escuta em todas as placas (inclusive Wi-Fi publico); IP especifico = so naquela rede
            var todas = g.Any(p => p.Endereco is "0.0.0.0" or "::");
            partes.Add(todas ? "todas as redes" : "so em " + string.Join(", ", g.Select(p => p.Endereco).Distinct().Take(2)));
            partes.Add(TextoAssinatura(assin, assinante, g.Key.Caminho));
            if (disfarce) partes.Insert(0, @"nome do Windows FORA de C:\Windows");
            if (bloqueado) partes.Insert(0, "entrada bloqueada no firewall pelo Factory");
            rows.Add(new SecRow
            {
                Kind = "porta", Key = nome, Titulo = nome, Pid = pid, Caminho = g.Key.Caminho, Estado = estado, Risco = risco,
                Detalhe = string.Join(" · ", partes.Where(x => x.Length > 0)),
            });
        }
        return rows.OrderBy(r => r.Peso).ThenBy(r => r.Titulo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<SecRow> Persistencia(HostSnapshot s)
    {
        var rows = new List<SecRow>();
        foreach (var p in s.Persistencia)
        {
            var known = _allow.Persistencia.Any(a => Match(a, p.Nome));
            // WINDOWS so com o executavel dentro do Windows/Defender: tarefa em \Microsoft\Windows\ apontando pra AppData nao passa.
            var exe = Environment.ExpandEnvironmentVariables(Exe(p.Acao));
            var sistema = p.Tipo == "tarefa"
                ? p.Onde.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)
                  && (EhWindows(exe) || exe.StartsWith(@"C:\ProgramData\Microsoft\Windows Defender\", StringComparison.OrdinalIgnoreCase))
                : EhWindows(exe) && !Lolbins.Any(l => p.Acao.Contains(l, StringComparison.OrdinalIgnoreCase));
            // guia de triagem: arquivo sumiu = residuo de desinstalacao (linha amarela do Autoruns); assinatura invalida = rosa
            var orfao = !known && !sistema && p.Existe == false;
            var risco = !known && !sistema && !orfao && (EmPastaDeRisco(exe) || p.Assinatura is not ("" or "Valid"));
            var estado = known ? SecRow.Conhecido : sistema ? SecRow.Sistema : orfao ? SecRow.Orfao : NovoOuRevisar($"{p.Tipo}:{p.Nome}");
            var tipo = p.Tipo switch { "tarefa" => "tarefa " + p.Onde, "run" => "Run " + (p.Onde.StartsWith("HKLM") ? "(maquina)" : "(usuario)"), _ => "pasta Inicializar" };
            rows.Add(new SecRow
            {
                Kind = "persistencia", Key = p.Nome, Titulo = SemSid(p.Nome), Estado = estado, Caminho = Exe(p.Acao),
                Tipo = p.Tipo, Onde = p.Onde, Valor = p.Acao, Risco = risco,
                // evidencia que decide (assinatura/orfao) na 1a linha; caminho e argumentos na 2a
                Detalhe = ((orfao ? "o arquivo NÃO existe mais (resíduo)" : "")
                           + (TextoAssinatura(p.Assinatura, p.Assinante, exe) is { Length: > 0 } t ? (orfao ? " · " : "") + t : "")).Trim() is { Length: > 0 } ev
                    ? ev + "\n" + $"{tipo} · {p.Acao}" + (p.Autor is { Length: > 0 } a && !a.Contains('\\') ? $" · autor {a}" : "")
                    : $"{tipo} · {p.Acao}" + (p.Autor is { Length: > 0 } a2 && !a2.Contains('\\') ? $" · autor {a2}" : ""),
            });
        }
        // o que o Factory desligou some do Windows; aparece aqui pra poder RELIGAR
        foreach (var a in Acoes().Where(a => a.Tipo != "firewall"))
            rows.Add(new SecRow
            {
                Kind = "persistencia", Key = a.Nome, Titulo = a.Nome, Estado = SecRow.Desligado, Tipo = a.Tipo, Onde = a.Onde, Valor = a.Valor,
                Caminho = a.Tipo == "inicializar" ? "" : Exe(a.Valor),
                Detalhe = $"desligado pelo Factory em {a.Quando.LocalDateTime:dd/MM HH:mm} · {(a.Tipo == "tarefa" ? "tarefa " + a.Onde : a.Valor)}",
            });
        return rows.OrderBy(r => r.Peso).ThenBy(r => r.Titulo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Temp, Users\Public ou AppData\Local\Temp: onde o guia manda desconfiar primeiro.</summary>
    private static bool EmPastaDeRisco(string caminho) =>
        caminho.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) || caminho.Contains(@"\Users\Public\", StringComparison.OrdinalIgnoreCase)
        || caminho.Contains(@"\Downloads\", StringComparison.OrdinalIgnoreCase);

    private static string TextoAssinatura(string assin, string assinante, string caminho)
    {
        var a = assin switch
        {
            "Valid" => assinante.Length > 0 ? "assinado: " + assinante : "assinatura válida",
            "" => "",
            "NotSigned" => "SEM assinatura digital",
            "HashMismatch" => "ASSINATURA ADULTERADA (hash não bate)",
            var x => "assinatura inválida (" + x + ")",
        };
        var pasta = EmPastaDeRisco(caminho) ? "roda de pasta de risco (Temp/Public/Downloads)" : "";
        return string.Join(" · ", new[] { a, pasta }.Where(x => x.Length > 0));
    }

    private string NovoOuRevisar(string chave) =>
        _vistos.TryGetValue(chave, out var t) && t != DateTimeOffset.MinValue && DateTimeOffset.Now - t < JanelaNovo ? SecRow.Novo : SecRow.Revisar;

    private void MarcarVistos(HostSnapshot s)
    {
        // Primeira leitura da vida = linha de base (data minima): o que ja existia hoje nao vira NOVO, so REVISAR.
        var agora = _baseline ? DateTimeOffset.Now : DateTimeOffset.MinValue;
        var mudou = false;
        foreach (var k in s.Portas.Select(p => "porta:" + p.Processo).Concat(s.Persistencia.Select(p => $"{p.Tipo}:{p.Nome}")))
            if (_vistos.TryAdd(k, agora)) mudou = true;
        if (mudou) SaveJson(VistosPath, _vistos);
        _baseline = true;
    }

    // ---------- allowlist ----------

    public string Aprovar(IEnumerable<SecRow> rows)
    {
        var novos = new List<(List<string> Lista, string Key)>();
        foreach (var r in rows.Where(r => r.PodeAprovar))
        {
            var lista = r.Kind == "porta" ? _allow.Processos : _allow.Persistencia;
            if (lista.Any(a => Match(a, r.Key)) || novos.Any(n => n.Lista == lista && n.Key == r.Key)) continue;
            lista.Add(r.Key);
            novos.Add((lista, r.Key));
        }
        if (novos.Count > 0 && SaveJson(AllowlistPath, _allow) is { } erro)
        {
            foreach (var (l, k) in novos) l.Remove(k);   // nao finge que gravou
            return $"nao consegui gravar {Path.GetFileName(AllowlistPath)}: {erro} — feche o arquivo se estiver aberto e tente de novo";
        }
        if (novos.Count > 0) _ultimo = novos;
        return novos.Count == 0 ? "nada novo pra gravar" : $"{novos.Count} marcado(s) como IGNORADO ({Path.GetFileName(AllowlistPath)}): {string.Join(", ", novos.Take(4).Select(n => n.Key))}{(novos.Count > 4 ? "…" : "")}";
    }

    private List<(List<string> Lista, string Key)> _ultimo = [];
    public bool PodeDesfazer => _ultimo.Count > 0;

    public string Desfazer()
    {
        if (_ultimo.Count == 0) return "nada pra desfazer";
        foreach (var (l, k) in _ultimo) l.Remove(k);
        if (SaveJson(AllowlistPath, _allow) is { } erro)
        {
            foreach (var (l, k) in _ultimo) l.Add(k);
            return $"nao consegui desfazer: {erro}";
        }
        var n = _ultimo.Count;
        _ultimo = [];
        return $"desfeito: {n} {(n == 1 ? "item saiu" : "itens sairam")} da allowlist";
    }

    private SecurityAllowlist LoadAllow()
    {
        if (LoadJson<SecurityAllowlist>(AllowlistPath) is { } a)
        {
            // triage guide: 2 Windows/Defender processes that are safe to trust by default
            if (a.Versao != "security-allowlist/v1.1")
            {
                foreach (var n in new[] { "IpOverUsbSvc", "DefenderSessionHelper" })
                    if (!a.Processos.Contains(n, StringComparer.OrdinalIgnoreCase)) a.Processos.Add(n);
                a.Versao = "security-allowlist/v1.1";
                SaveJson(AllowlistPath, a);
            }
            return a;
        }
        // First run: generic seed with core Windows processes and ports only; the user adds the rest with CONFIAR.
        var seed = new SecurityAllowlist
        {
            Processos = ["services", "spoolsv", "svchost", "wininit", "lsass"],
            Portas = [135, 139, 445, 5040, 5357, 7680],
            Persistencia = [],
        };
        SaveJson(AllowlistPath, seed);
        return seed;
    }

    // ---------- HardeningKitty ----------

    public async Task<string> RodarAuditoriaAsync()
    {
        if (AuditoriaRodando) return "auditoria ja esta rodando";
        AuditoriaRodando = true;
        try
        {
            Directory.CreateDirectory(HardeningDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            var csv = Path.Combine(HardeningDir, $"hk-{stamp}.csv");
            var log = Path.Combine(HardeningDir, $"hk-{stamp}.log");
            // Audit so le o registro/politicas; nada e alterado. Elevado porque metade dos testes exige admin.
            var cmd = $"Import-Module HardeningKitty; Invoke-HardeningKitty -Mode Audit -SkipLanguageWarning -Log -LogFile '{log}' -Report -ReportFile '{csv}'";
            var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {enc}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process? p;
            try { p = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "cancelado no UAC — nada rodou"; }
            if (p is null) return "o Windows nao abriu o PowerShell";
            await p.WaitForExitAsync();
            if (!File.Exists(csv)) return $"terminou sem relatorio (codigo {p.ExitCode}); log: {log}";
            await GetAsync(force: true);
            return Last?.Hardening is { } h ? $"nota {h.Score:0.00}/6 · {h.Alto} altos · {Path.GetFileName(csv)}" : $"relatorio em {csv}";
        }
        finally { AuditoriaRodando = false; }
    }

    // ---------- DESLIGAR / BLOQUEAR: real, reversible actions ----------

    public async Task<string> CortarAsync(SecRow r)
    {
        if (!r.PodeCortar) return "nada a fazer nessa linha";
        // Fase 1 do guia: snapshot de seguranca ANTES de mexer (equivale ao File > Save do Autoruns)
        if (Last is { } snap)
        {
            var bk = Path.Combine(Dir, "backups");
            Directory.CreateDirectory(bk);
            var arq = Path.Combine(bk, $"autostart-{DateTime.Now:yyyyMMdd}.json");
            if (!File.Exists(arq)) SaveJson(arq, new { quando = DateTimeOffset.Now, persistencia = snap.Persistencia, portas = snap.Portas });
        }
        var acao = new SecAcao { Tipo = r.Kind == "porta" ? "firewall" : r.Tipo, Nome = r.Key, Onde = r.Onde, Valor = r.Kind == "porta" ? r.Caminho : r.Valor, Quando = DateTimeOffset.Now };
        string script; bool admin;
        switch (acao.Tipo)
        {
            case "firewall":
                script = $"New-NetFirewallRule -DisplayName {Q(Regra(r.Caminho))} -Group 'Factory Seguranca' -Direction Inbound -Program {Q(r.Caminho)} -Action Block | Out-Null";
                admin = true;
                break;
            case "tarefa":
                script = $"Disable-ScheduledTask -TaskPath {Q(r.Onde)} -TaskName {Q(r.Key)} | Out-Null";
                admin = false;   // a user-owned task disables without admin; se der acesso negado, tenta de novo com UAC
                break;
            case "run":
                script = $"Remove-ItemProperty -Path {Q(Reg(r.Onde))} -Name {Q(r.Key)}";
                admin = r.Onde.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
                break;
            default:
                Directory.CreateDirectory(AutostartDir);
                script = $"Move-Item -LiteralPath {Q(Path.Combine(r.Onde, r.Valor))} -Destination {Q(Path.Combine(AutostartDir, r.Valor))} -Force";
                admin = !r.Onde.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase);
                break;
        }
        var (ok, msg) = await Ps(script, admin);
        if (!ok && !admin && msg.Contains("negado", StringComparison.OrdinalIgnoreCase) | msg.Contains("denied", StringComparison.OrdinalIgnoreCase))
            (ok, msg) = await Ps(script, admin: true);
        if (!ok) return $"nao consegui {r.Corte.ToLowerInvariant()} {r.Key}: {msg}";
        var lista = Acoes();
        lista.Add(acao);
        SaveJson(AcoesPath, lista);
        Last = null;   // proxima leitura ja reflete
        return acao.Tipo == "firewall" ? $"{r.Key} bloqueado na entrada (regra '{Regra(r.Caminho)}'). LIBERAR desfaz."
            : $"{r.Key} desligado. RELIGAR desfaz.";
    }

    public async Task<string> ReverterAsync(SecRow r)
    {
        var lista = Acoes();
        var a = r.Estado == SecRow.Bloqueado
            ? lista.FirstOrDefault(x => x.Tipo == "firewall" && x.Valor.Equals(r.Caminho, StringComparison.OrdinalIgnoreCase))
            : lista.FirstOrDefault(x => x.Tipo == r.Tipo && x.Nome == r.Key && x.Onde == r.Onde);
        if (a is null) return "nao achei o registro dessa acao";
        var (script, admin) = a.Tipo switch
        {
            "firewall" => ($"Remove-NetFirewallRule -DisplayName {Q(Regra(a.Valor))}", true),
            "tarefa" => ($"Enable-ScheduledTask -TaskPath {Q(a.Onde)} -TaskName {Q(a.Nome)} | Out-Null", false),
            "run" => ($"New-ItemProperty -Path {Q(Reg(a.Onde))} -Name {Q(a.Nome)} -Value {Q(a.Valor)} -PropertyType String -Force | Out-Null",
                      a.Onde.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)),
            _ => ($"Move-Item -LiteralPath {Q(Path.Combine(AutostartDir, a.Valor))} -Destination {Q(Path.Combine(a.Onde, a.Valor))} -Force",
                  !a.Onde.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)),
        };
        var (ok, msg) = await Ps(script, admin);
        if (!ok && !admin && (msg.Contains("negado", StringComparison.OrdinalIgnoreCase) || msg.Contains("denied", StringComparison.OrdinalIgnoreCase)))
            (ok, msg) = await Ps(script, admin: true);
        if (!ok) return $"nao consegui {r.Reverte.ToLowerInvariant()} {a.Nome}: {msg}";
        lista.Remove(a);
        SaveJson(AcoesPath, lista);
        Last = null;
        return $"{a.Nome} {(a.Tipo == "firewall" ? "liberado" : "religado")}";
    }

    public IReadOnlyList<SecRow> Achados(HostSnapshot s, bool tudo)
    {
        var l = new List<SecRow>();
        l.AddRange(Portas(s).Where(r => tudo || r.Estado is not (SecRow.Sistema or SecRow.Conhecido)));
        l.AddRange(Persistencia(s).Where(r => tudo || r.Estado is not (SecRow.Sistema or SecRow.Conhecido)));
        foreach (var e in s.Eventos.OrderByDescending(e => e.Quando))
        {
            var v = Evento(e);
            // deteccao que o Defender JA bloqueou (1117 logo depois) nao e mais "grave": vira historico
            var bloqueada = e.EventId == 1116 && s.Eventos.Any(x => x.EventId == 1117 && x.Ameaca == e.Ameaca && x.Caminho == e.Caminho && x.Quando >= e.Quando);
            l.Add(new SecRow
            {
                Kind = "defender", Key = $"ev:{e.EventId}:{e.Quando.UtcTicks}", Titulo = v.Titulo, Detalhe = $"{v.Quando} · {v.Detalhe}",
                Estado = e.EventId == 1116 ? SecRow.Ameaca : e.EventId == 1117 ? SecRow.Bloqueada : SecRow.Asr,
                AccentFixo = e.EventId == 1116 && !bloqueada ? "magenta" : e.EventId == 1117 ? "green" : "dim",   // BLOQUEADA = boa noticia: verde, nao cinza
                PesoFixo = e.EventId == 1116 ? (bloqueada ? 1 : -3) : e.EventId == 1117 ? 1 : 1,
                Abrir = e.EventId == 1116 ? "windowsdefender://threat" : "",
            });
        }
        foreach (var b in Postura(s).Where(b => b.Accent == "amber" || b.Nota.Length > 0))
        {
            var intencional = b.Nota.Length > 0;
            l.Add(new SecRow
            {
                Kind = "postura", Key = "postura:" + b.Rotulo, Titulo = $"{Bonito(b.Rotulo)} {b.Valor}", Detalhe = b.Dica,
                Estado = intencional ? SecRow.Intencional : SecRow.Protecao,
                AccentFixo = intencional ? "dim" : "amber", PesoFixo = intencional ? 2 : -2, Abrir = b.Abrir,
            });
        }
        foreach (var r in l) r.Nota = NotaDe(r.NotaChave);
        return l.OrderBy(r => r.Peso).ThenBy(r => r.Titulo, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // First run starts with no notes; the user writes them per item in the detail panel.
    private static Dictionary<string, string> SeedNotas()
    {
        var n = new Dictionary<string, string>();
        SaveJson(NotasPath, n);
        return n;
    }

    public string NotaDe(string chave) => Notas.GetValueOrDefault(chave) ?? "";

    public void SetNota(string chave, string texto)
    {
        texto = texto.Trim();
        if (texto.Length == 0) Notas.Remove(chave); else Notas[chave] = texto;
        SaveJson(NotasPath, Notas);
        Last = null;   // a proxima leitura reflete (selo vira "de proposito")
    }

    /// <summary>Nome de gente pro selo ("BITLOCKER" -> "BitLocker") no titulo da linha.</summary>
    /// <summary>Tarefa por usuario vem com " - S-1-12-1-..." (SID) no nome: so ruido na tela. A chave (Key) continua o nome inteiro.</summary>
    private static string SemSid(string nome) => System.Text.RegularExpressions.Regex.Replace(nome, @"\s*-\s*S-1-[\d-]+$", "");

    private static string Bonito(string rotulo) => rotulo switch
    {
        "BITLOCKER" => "BitLocker", "DEFENDER" => "Defender", "TAMPER" => "Proteção contra violação", "FIREWALL" => "Firewall",
        "SECURE BOOT" => "Secure Boot", "HARDENING" => "Hardening", "ASSINATURAS" => "Assinaturas do Defender", _ => rotulo,
    };

    public bool EhConfiavel(string processo) => _allow.Processos.Any(a => Match(a, processo));

    private static List<SecAcao> Acoes() => LoadJson<List<SecAcao>>(AcoesPath) ?? [];
    private static string Regra(string caminho) => "Factory bloqueio: " + Path.GetFileName(caminho);
    private static string Reg(string onde) => onde.Length > 4 && onde[4] == '\\' ? onde[..4] + ":" + onde[4..] : onde;   // HKCU\... -> HKCU:\...
    private static string Q(string v) => "'" + v.Replace("'", "''") + "'";

    /// <summary>Roda um trecho de PowerShell. admin = elevado por UAC (ele aceita ou cancela); resultado volta por arquivo.</summary>
    private static async Task<(bool Ok, string Msg)> Ps(string script, bool admin)
    {
        var outFile = Path.Combine(Path.GetTempPath(), $"factory-seg-{Guid.NewGuid():N}.txt");
        var full = $"$ErrorActionPreference='Stop'; try {{ {script}; 'OK' | Set-Content -LiteralPath {Q(outFile)} -Encoding UTF8 }} catch {{ $_.Exception.Message | Set-Content -LiteralPath {Q(outFile)} -Encoding UTF8; exit 1 }}";
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {enc}")
        {
            UseShellExecute = admin, CreateNoWindow = !admin, WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (admin) psi.Verb = "runas"; else psi.Environment.Remove("PSModulePath");   // mesmo motivo do coletor
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (false, "o PowerShell nao abriu");
            await p.WaitForExitAsync();
            var msg = File.Exists(outFile) ? File.ReadAllText(outFile).Trim() : "";
            return (p.ExitCode == 0 && msg == "OK", msg.Length > 0 ? msg : $"codigo {p.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return (false, "cancelado no UAC — nada mudou"); }
        finally { try { File.Delete(outFile); } catch (Exception) { } }
    }

    // ---------- Odin ----------

    public async Task<string> ResumoAsync()
    {
        var s = await GetAsync();
        var p = s.Postura;
        var sb = new StringBuilder($"Seguranca do PC, lida agora ({s.Ms / 1000.0:0.0} s, {(s.Admin ? "admin" : "sem admin")}):\n");
        sb.Append("- Postura: ").Append(string.Join(", ", Postura(s).Select(b => $"{b.Rotulo} {b.Valor}"))).Append('\n');
        var ev = s.Eventos.OrderByDescending(e => e.Quando).ToList();
        sb.Append(ev.Count == 0 ? "- Defender: nenhuma deteccao nem bloqueio ASR nas ultimas 24 h.\n"
            : $"- Defender 24 h: {ev.Count} eventos. Ultimo {ev[0].Quando.LocalDateTime:HH:mm}: {Evento(ev[0]).Titulo} — {Evento(ev[0]).Detalhe}\n");
        var portas = Portas(s).Where(r => r.PodeAprovar).ToList();
        sb.Append(portas.Count == 0 ? "- Portas: tudo que escuta na rede e conhecido ou do Windows.\n"
            : $"- Portas abertas a revisar ({portas.Count}): {string.Join("; ", portas.Take(5).Select(r => $"{r.Titulo} {r.Detalhe}"))}\n");
        var pers = Persistencia(s).Where(r => r.PodeAprovar).ToList();
        var novos = pers.Count(r => r.Estado == SecRow.Novo);
        sb.Append(pers.Count == 0 ? "- Inicializacao: nada fora da allowlist.\n"
            : $"- Inicializacao/tarefas a revisar: {pers.Count}{(novos > 0 ? $" ({novos} NOVOS esta semana)" : "")}: {string.Join(", ", pers.Take(6).Select(r => r.Titulo))}\n");
        sb.Append(s.Hardening is { } h
            ? $"- HardeningKitty: nota {h.Score:0.00} de 6 em {h.Quando.LocalDateTime:dd/MM}, {h.Alto} falhas altas.\n"
            : "- HardeningKitty: nunca rodou (botao AUDITAR na secao Seguranca, pede admin).\n");
        sb.Append("Fale curto: o que esta bom, o que merece olhar. Nada aqui foi alterado; aprovar ou mexer e na secao Seguranca do Factory.");
        return sb.ToString();
    }

    // ---------- pra tela e pro Odin ----------

    /// <summary>Posture badges; an amber badge WITH a user note becomes "de proposito" (ciano) e some do alarme.</summary>
    public static IReadOnlyList<SecBadge> Postura(HostSnapshot s) => PosturaBruta(s).Select(b =>
        b.Accent == "amber" && Notas.TryGetValue("postura:" + b.Rotulo, out var n)
            ? new SecBadge { Rotulo = b.Rotulo, Valor = b.Valor + " · de propósito", Dica = b.Dica + "\n" + n, Abrir = b.Abrir, Accent = "cyan", Nota = n }
            : b).ToList();

    private static IReadOnlyList<SecBadge> PosturaBruta(HostSnapshot s)
    {
        var p = s.Postura;
        static SecBadge B(string r, bool? v, string on, string off, string dica, string abrir, bool offIsWarn = true) => new()
        {
            Rotulo = r, Dica = dica, Abrir = abrir,
            Valor = v is null ? "sem leitura" : v.Value ? on : off,
            Accent = v is null ? "violet" : v.Value ? "green" : offIsWarn ? "amber" : "cyan",
        };
        var bl = p.BitLocker?.Status;
        bool? blOn = bl is null or "Unknown" ? null : bl is "On" or "FullyEncrypted";
        var asrBloq = p.Asr.Count(a => a.Acao == 1);
        var asrAud = p.Asr.Count(a => a.Acao is 2 or 6);
        var fwOn = p.Firewall.Count(f => f.Ligado);
        return
        [
            B("DEFENDER", p.DefenderLigado, "ligado", "DESLIGADO", "protecao em tempo real · clique: abre Protecao contra virus", "windowsdefender://threat"),
            B("BITLOCKER", blOn, "C: criptografado", "desligado", $"disco C: · lido via {p.BitLocker?.Fonte ?? "?"} · clique: abre o BitLocker pra ligar", "control.exe /name Microsoft.BitLockerDriveEncryption"),
            B("HVCI", p.Hvci, "ativo", "desligado", "Integridade de Memoria (isolamento de nucleo) · clique: abre onde liga (pede reiniciar)", "windowsdefender://coreisolation"),
            B("TAMPER", p.TamperProtection, "protegido", "desligado", "ninguem desliga o Defender por script · clique: abre as configuracoes", "windowsdefender://threatsettings"),
            new SecBadge
            {
                Rotulo = "ASR", Dica = "regras de reducao de superficie de ataque (bloqueio / auditoria) · clique: abre Protecao contra virus",
                Abrir = "windowsdefender://threat",
                Valor = !p.AsrLeitura ? "sem leitura" : $"{asrBloq} bloqueando" + (asrAud > 0 ? $" · {asrAud} auditando" : ""),
                Accent = !p.AsrLeitura ? "violet" : asrBloq > 0 ? "green" : "amber",
            },
            new SecBadge
            {
                Rotulo = "FIREWALL", Dica = string.Join(" · ", p.Firewall.Select(f => $"{f.FirewallProfile} {(f.Ligado ? "on" : "OFF")}")) + " · clique: abre Firewall e rede",
                Abrir = "windowsdefender://network",
                Valor = p.Firewall.Count == 0 ? "sem leitura" : $"{fwOn}/{p.Firewall.Count} perfis",
                Accent = p.Firewall.Count == 0 ? "violet" : fwOn == p.Firewall.Count ? "green" : "amber",
            },
            B("SECURE BOOT", p.SecureBoot, "ligado", "desligado", "so admin le daqui · clique: abre Informacoes do Sistema (linha 'Estado da Inicializacao Segura')", "msinfo32.exe"),
            HardeningBadge(s),
            new SecBadge
            {
                Rotulo = "ASSINATURAS", Dica = "ultima atualizacao das vacinas do Defender · clique: abre onde tem 'Verificar se ha atualizacoes'",
                Abrir = "windowsdefender://threat",
                Valor = p.Assinaturas is { } a ? Idade(a) : "sem leitura",
                Accent = p.Assinaturas is { } a2 ? (DateTimeOffset.Now - a2 < TimeSpan.FromDays(3) ? "green" : "amber") : "violet",
            },
        ];
    }

    private static SecBadge HardeningBadge(HostSnapshot s) => s.Hardening is { } h
        ? new SecBadge
        {
            Rotulo = "HARDENING", Valor = $"{h.Score:0.0} / 6 · {h.Alto} {(h.Alto == 1 ? "falha alta" : "falhas altas")}", Abrir = "tab:4",
            Dica = $"nota do HardeningKitty (CIS) de {h.Quando.LocalDateTime:dd/MM} · clique: abre a aba HARDENING",
            Accent = h.Score >= 5 ? "green" : "amber",
        }
        : new SecBadge { Rotulo = "HARDENING", Valor = "nunca auditado", Abrir = "tab:4", Accent = "violet", Dica = "AUDITAR roda o HardeningKitty (só lê, pede admin) · clique: abre a aba HARDENING" };

    public static SecEvent Evento(HostEvento e)
    {
        var alvo = e.Caminho.Replace("containerfile:_", "").Replace("file:_", "");
        var quem = Path.GetFileName(e.Processo);
        var (accent, titulo, detalhe) = e.EventId switch
        {
            1116 => ("magenta", $"Ameaça detectada · {e.Ameaca}", $"{alvo} (via {quem})"),
            1117 => ("green", $"Ameaça bloqueada · {e.Ameaca}", $"{alvo} (via {quem})"),
            1121 => ("amber", $"ASR bloqueou · {AsrNome(e.Regra)}", $"{quem} -> {alvo}"),
            _ => ("cyan", $"ASR auditou · {AsrNome(e.Regra)}", $"{quem} -> {alvo}"),
        };
        return new SecEvent { Accent = accent, Titulo = titulo, Detalhe = detalhe, Quando = e.Quando.LocalDateTime.ToString("dd/MM HH:mm") };
    }

    /// <summary>Nomes das regras ASR da doc da Microsoft (as mais comuns); id desconhecido sai abreviado.</summary>
    public static string AsrNome(string id) => id.ToLowerInvariant() switch
    {
        "56a863a9-875e-4185-98a7-b882c64b5ce5" => "driver vulnerável assinado",
        "7674ba52-37eb-4a4f-a9a1-f0f9a1619a2c" => "Adobe Reader criando processo",
        "d4f940ab-401b-4efc-aadc-ad5f3c50688a" => "Office criando processo filho",
        "9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2" => "roubo de credencial do LSASS",
        "be9ba2d9-53ea-4cdc-84e5-9b1eeee46550" => "executável vindo de e-mail",
        "01443614-cd74-433a-b99e-2ecdc07bfc25" => "executável raro ou sem reputação",
        "5beb7efe-fd9a-4556-801d-275e5ffc04cc" => "script ofuscado",
        "d3e037e1-3eb8-44c8-a917-57927947596d" => "JS/VBS baixando executável",
        "3b576479-7ec3-45b3-aa85-81e596619ffc" => "Office criando executável",
        "75668c1f-73b5-4cf0-bb93-3ecf5cb7cc84" => "Office injetando código",
        "26190899-1602-49e8-8b27-eb1d0a1ce869" => "Outlook criando processo",
        "e6db77e5-3df2-4cf1-b95a-636979351e5b" => "persistência via WMI",
        "d1e49aac-8f56-4280-b9ba-993a6d77406c" => "PsExec/WMI criando processo",
        "b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4" => "processo sem assinatura do USB",
        "92e97fa1-2edf-4476-bdd6-9dd0b4dddc7b" => "chamada Win32 em macro",
        "c1db55ab-c21a-4637-bb3f-a12568109d35" => "proteção contra ransomware",
        "33ddedf1-c6e0-47cb-833e-de6133960387" => "reiniciar em modo seguro",
        "c0033c00-d16d-4114-a5a0-dc9b3a7d2ceb" => "ferramenta de sistema copiada",
        "a8f5898e-1dc8-49a9-9878-85004b8a61e6" => "webshell em servidor",
        "" => "regra ?",
        var x => x.Length > 8 ? x[..8] + "…" : x,
    };

    // ---------- util ----------

    private static bool Match(string padrao, string nome) =>
        padrao.EndsWith('*') ? nome.StartsWith(padrao[..^1], StringComparison.OrdinalIgnoreCase) : nome.Equals(padrao, StringComparison.OrdinalIgnoreCase);

    private static bool EhWindows(string caminho) =>
        caminho.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase)
        || caminho.StartsWith("%windir%", StringComparison.OrdinalIgnoreCase) || caminho.StartsWith("%systemroot%", StringComparison.OrdinalIgnoreCase);

    /// <summary>Primeiro executavel de uma linha de comando ("C:\x y\a.exe" -b  ->  C:\x y\a.exe).</summary>
    public static string Exe(string acao)
    {
        var a = acao.Trim();
        if (a.StartsWith('"')) { var f = a.IndexOf('"', 1); return f > 0 ? a[1..f] : a.Trim('"'); }
        var i = a.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? a[..(i + 4)] : a.Split(' ')[0];
    }

    private static string Lista(List<int> portas) =>
        portas.Count <= 4 ? string.Join(" ", portas) : string.Join(" ", portas.Take(3)) + $" +{portas.Count - 3}";

    public static string Idade(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        return d.TotalHours < 1 ? "agora ha pouco" : d.TotalHours < 48 ? $"ha {(int)d.TotalHours} h" : $"ha {(int)d.TotalDays} dias";
    }

    private static string Primeira(string s) => s.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";

    private static T? LoadJson<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Grava atomico (tmp + move). Devolve null se gravou, ou o motivo da falha.</summary>
    private static string? SaveJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
