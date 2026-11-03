using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Media.Control;

namespace Factory.Services;

/// <summary>
/// Mao `musica` sem tecla de midia e sem Claude Code (trust phase 1). Before: VK_MEDIA_PLAY_PAUSE ia pra
/// quem o Windows achasse (3 "pausei" com o Spotify tocando) e "toca banda X" caia no `fazer` (2,5 min, 3 de ~25 ok).
/// Agora: play/pause/faixa pela sessao de midia DO SPOTIFY (GSMTC), volume pela sessao de audio DO SPOTIFY (Core
/// Audio), e "toca X" = busca + clique no botao Play pela acessibilidade (UIA), ~3 s. Toda resposta de sucesso traz
/// [prova] lida depois da acao; se o estado nao bater com o pedido, devolve "Nao consegui" (vira falhou no recibo).
/// </summary>
public static class SpotifyHands
{
    public sealed record Estado(bool Tocando, string Titulo, string Artista);

    /// <summary>Opens Spotify. Some Spotify builds die in 2 s with 0xC0000005 when
    /// abrir normal, e vive com --disable-gpu (webview/GPU). Entao abre o exe direto com a flag; so cai no spotify: se o
    /// exe nao existir. Com ele ja aberto, spotify:search: continua funcionando (vai pra instancia viva).</summary>
    public static bool Abrir()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
        try
        {
            if (File.Exists(exe)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--disable-gpu") { UseShellExecute = false });
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("spotify:") { UseShellExecute = true });
            return true;
        }
        catch (Exception) { return false; }
    }

    // Some Spotify builds die by themselves ~2 s after opening (0xC0000005), whatever the way it was opened.
    // Nao e o Odin: e o app. A mao diz isso em vez de "nao consegui tocar".
    private const string CrashMsg = "Nao consegui: o Spotify abriu e FECHOU SOZINHO em segundos (ele esta travando ao abrir neste PC, nao e coisa minha). Diga isso a ele em uma frase: o Spotify precisa ser reinstalado.";

    private static async Task<GlobalSystemMediaTransportControlsSession?> Sessao()
    {
        var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return mgr.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<Estado?> LerEstado()
    {
        var s = await Sessao();
        if (s is null) return null;
        var tocando = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        try
        {
            var m = await s.TryGetMediaPropertiesAsync();
            return new Estado(tocando, m.Title ?? "", m.Artist ?? "");
        }
        catch (Exception) { return new Estado(tocando, "", ""); }
    }

    private static string Faixa(Estado e) => e.Artista.Length > 0 ? $"{e.Artista} - {e.Titulo}" : e.Titulo;

    /// <summary>tocar | pausar | alternar | proxima | anterior.</summary>
    public static async Task<string> Controlar(string acao)
    {
        var a = CommandLine.Fold(acao ?? "");
        a = a switch
        {
            "tocar" or "play" or "continuar" or "retomar" => "tocar",
            "pausar" or "pause" or "parar" or "para" => "pausar",
            "proxima" or "next" or "pular" or "pula" => "proxima",
            "anterior" or "prev" or "voltar" => "anterior",
            _ => a,
        };
        var s = await Sessao();
        if (s is null)
        {
            if (a != "tocar") return "Nao consegui: o Spotify nao esta aberto (nenhuma sessao de midia do Spotify).";
            Abrir();
            var viu = false;
            for (var i = 0; i < 40 && s is null; i++)
            {
                await Task.Delay(500);
                var n = System.Diagnostics.Process.GetProcessesByName("Spotify").Length;
                if (n > 0) viu = true; else if (viu) return CrashMsg;
                s = await Sessao();
            }
            if (s is null) return "Nao consegui: abri o Spotify mas a sessao de midia do Spotify nao apareceu em 20 s.";
        }
        var antes = await LerEstado();
        bool pedido = a switch
        {
            "tocar" => await s.TryPlayAsync(),
            "pausar" => await s.TryPauseAsync(),
            "alternar" => await s.TryTogglePlayPauseAsync(),
            "proxima" => await s.TrySkipNextAsync(),
            "anterior" => await s.TrySkipPreviousAsync(),
            _ => false,
        };
        if (!pedido && a is not ("tocar" or "pausar" or "alternar" or "proxima" or "anterior"))
            return $"Nao entendi a acao '{acao}'. Aceito: tocar, pausar, proxima, anterior, subir, descer, volume.";
        if (!pedido) return $"Nao consegui: o Spotify recusou '{a}'.";

        // Le de volta ate 3 s: o estado tem que bater com o pedido.
        Estado? depois = antes;
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(250);
            depois = await LerEstado();
            if (depois is null) continue;
            if (Bate(a, antes, depois)) break;
        }
        if (depois is null || !Bate(a, antes, depois))
            return $"Nao consegui confirmar '{a}': o Spotify continua {(depois?.Tocando == true ? "tocando" : "pausado")} em '{(depois is null ? "?" : Faixa(depois))}'. Nao diga que fez.";
        var texto = a switch { "tocar" => "Tocando", "pausar" => "Pausado", "proxima" => "Proxima faixa", "anterior" => "Faixa anterior", _ => "Feito" };
        return $"{texto}. Spotify agora: {(depois.Tocando ? "tocando" : "pausado")} '{Faixa(depois)}'.\n" + Prova(a, antes, depois);
    }

    private static bool Bate(string a, Estado? antes, Estado d) => a switch
    {
        "tocar" => d.Tocando,
        "pausar" => !d.Tocando,
        "alternar" => antes is null || d.Tocando != antes.Tocando,
        "proxima" or "anterior" => antes is null || d.Titulo != antes.Titulo || d.Artista != antes.Artista,
        _ => false,
    };

    private static string Prova(string acao, Estado? antes, Estado depois, object? extra = null) =>
        "[prova] " + JsonSerializer.Serialize(new
        {
            mao = "musica", acao, coerente = true,
            antes = antes is null ? null : new { tocando = antes.Tocando, faixa = Faixa(antes) },
            depois = new { tocando = depois.Tocando, faixa = Faixa(depois) },
            extra, fonte = "sessao de midia do Spotify (GSMTC)", lido_em = DateTimeOffset.Now.ToString("o"),
        });

    // ---------- volume interno do Spotify (nao o geral) ----------

    /// <summary>delta em pontos (+15 / -15) ou nivel absoluto 0-100.</summary>
    public static async Task<string> Volume(int? delta, int? nivel)
    {
        var vol = AudioSessoes.Spotify();
        if (vol is null) return "Nao consegui: o Spotify nao tem sessao de audio aberta (toque algo nele primeiro).";
        try
        {
            vol.GetMasterVolume(out var atual);
            var alvo = nivel is { } n ? n / 100f : atual + (delta ?? 0) / 100f;
            alvo = Math.Clamp(alvo, 0f, 1f);
            var g = Guid.Empty;
            vol.SetMasterVolume(alvo, ref g);
            await Task.Delay(150);
            vol.GetMasterVolume(out var lido);
            var ok = Math.Abs(lido - alvo) < 0.02f;
            if (ok && Math.Abs(lido - atual) < 0.01f) return $"O volume do Spotify ja esta em {lido * 100:0}%{(lido >= 0.99f ? " (maximo)" : lido <= 0.01f ? " (mudo)" : "")}; nao mudou nada. Diga isso a ele.";
            if (!ok) return $"Nao consegui: pedi {alvo * 100:0}% e o Windows devolveu {lido * 100:0}%.";
            var e = await LerEstado();
            return $"Volume do Spotify: {atual * 100:0}% -> {lido * 100:0}% (so o Spotify, o geral nao mexeu).\n[prova] "
                + JsonSerializer.Serialize(new { mao = "musica", acao = "volume", coerente = true, antes = Math.Round(atual * 100), depois = Math.Round(lido * 100), tocando = e?.Tocando, fonte = "sessao de audio do Spotify (Core Audio)", lido_em = DateTimeOffset.Now.ToString("o") });
        }
        finally { Marshal.ReleaseComObject(vol); }
    }

    // ---------- tocar por nome: busca + clique no Play pela acessibilidade ----------

    // "toca Massive Attack" once played the track "Massive Attack" do Basswell (3 botoes "Play Massive Attack" na busca).
    // Agora clica antes no filtro do tipo (Artists/Songs/Playlists/Albums) e so considera resultado visivel.
    private const string ScriptPlay = """
param([string]$termo, [string]$tipo, [long]$hwnd)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Fold([string]$s) { ($s.Normalize([Text.NormalizationForm]::FormD) -replace '\p{Mn}', '' -replace "[^a-zA-Z0-9 ]", ' ' -replace ' +', ' ').ToLowerInvariant().Trim() }
if ($hwnd -eq 0) { 'ERRO|spotify sem janela visivel'; exit }
try { $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$hwnd) } catch { 'ERRO|janela do Spotify sumiu (' + $_.Exception.Message + ')'; exit }
trap { 'ERRO|' + $_.Exception.Message; exit }
$win = $root.Current.BoundingRectangle
$A = [System.Windows.Automation.AutomationElement]
$botao = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$t = Fold $termo
$pal = @($t.Split(' ') | Where-Object { $_.Length -ge 2 })
# botoes Play visiveis da area principal (fora da barra lateral Your Library)
function Plays { @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $botao) | Where-Object { ($_.Current.Name -like 'Play *' -or $_.Current.Name -like 'Tocar *') -and -not $_.Current.IsOffscreen -and ($_.Current.BoundingRectangle.X - $win.X) -gt 250 }) }
function Assinatura { (Plays | ForEach-Object { $_.Current.Name }) -join '|' }
# espera a pagina MUDAR (senao o filtro e o clique caem na pagina anterior: 2 of 3 errors were this)
function EsperaMudar([string]$antes, [int]$max) {
  for ($i = 0; $i -lt $max; $i++) { Start-Sleep -Milliseconds 250; $agora = Assinatura; if ($agora -ne $antes -and $agora.Length -gt 0) { return $true } }
  return $false
}
$antes = Assinatura
Start-Process ("spotify:search:" + [uri]::EscapeDataString($termo))
if (-not (EsperaMudar $antes 24)) { if (-not ((Fold $antes).Contains($pal[0]))) { 'ERRO|a busca nao carregou em 6 s'; exit } }
# sometimes Spotify ignores o spotify:search: e fica na pagina anterior (mexeu so a barra lateral): manda de novo
if (-not ((Fold (Assinatura)).Contains($pal[0]))) {
  $antes = Assinatura
  Start-Process ("spotify:search:" + [uri]::EscapeDataString($termo))
  [void](EsperaMudar $antes 24)
}
$chips = @{ artista = @('Artists','Artistas'); musica = @('Songs','Musicas','Músicas'); playlist = @('Playlists'); album = @('Albums','Álbuns','Albuns') }
if ($chips.ContainsKey($tipo)) {
  $link = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Hyperlink)
  # "filtro musica nao apareceu" with the chip present; os chips montam depois dos Play. Espera ate 4 s e, sem chip,
  # segue na aba All em vez de desistir.
  $chip = $null
  for ($i = 0; $i -lt 16 -and -not $chip; $i++) {
    $chip = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $link) | Where-Object { $chips[$tipo] -contains $_.Current.Name -and ($_.Current.BoundingRectangle.X - $win.X) -gt 250 } | Select-Object -First 1
    if (-not $chip) { Start-Sleep -Milliseconds 250 }
  }
  # o chip ja ligado (busca anterior no mesmo filtro) desliga se clicar de novo: so clica se estiver Off
  $caixa = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
  $ligado = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $caixa) | Where-Object { $chips[$tipo] -contains $_.Current.Name } |
    Where-Object { try { $_.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' } catch { $false } } | Select-Object -First 1
  if ($chip -and -not $ligado) {
    $antes = Assinatura
    $chip.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](EsperaMudar $antes 16)
  }
}
$melhor = $null; $nota = -1
for ($volta = 0; $volta -lt 4 -and $nota -lt 100; $volta++) {
  if ($volta -gt 0) { Start-Sleep -Milliseconds 400 }
  foreach ($b in Plays) {
    $n = $b.Current.Name -replace '^(Play|Tocar) ', ''
    $nomeTodo = Fold $n
    # so corta no ULTIMO " by ": o " de " cortava "Ouro De Tolo" em "Ouro" and played the wrong artist
    $nome = Fold ($n -creplace ' by (?!.* by ).*$', '')
    $acha = @($pal | Where-Object { (' ' + $nomeTodo + ' ').Contains(' ' + $_ + ' ') }).Count
    $s = if ($nome -eq $t -or $nomeTodo -eq $t) { 100 } elseif ($pal.Count -gt 0) { [int](90 * $acha / $pal.Count) - [Math]::Min(20, [Math]::Abs($nomeTodo.Length - $t.Length) / 3) } else { 0 }
    if ($s -gt $nota) { $nota = $s; $melhor = $b }
  }
}
if (-not $melhor -or $nota -lt 55) { 'ERRO|nenhum resultado parecido com o termo na busca'; exit }
$nomeEscolhido = $melhor.Current.Name -replace '^(Play|Tocar) ', ''
$melhor.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
"OK|$nomeEscolhido|$nota"
""";

    public static async Task<string> TocarPorNome(string termo, string tipo = "")
    {
        termo = (termo ?? "").Trim();
        if (termo.Length == 0) return "Nao consegui: termo vazio. Pergunte o que tocar.";
        tipo = CommandLine.Fold(tipo ?? "") switch
        {
            "artista" or "banda" or "cantor" or "cantora" or "artist" => "artista",
            "musica" or "faixa" or "song" or "cancao" => "musica",
            "playlist" or "lista" => "playlist",
            "album" or "disco" => "album",
            _ => termo.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 4 ? "musica" : "artista",   // a band name is the most common request
        };
        // Spotify closed -> abrir UMA vez e esperar a janela de verdade
        // (ate 25 s). Antes: abria, esperava 5 s fixos e ja mandava spotify:search:, que subia uma 2a instancia
        // que piscava e fechava, e o script dizia "spotify fechado".
        // Janela visivel do Spotify (EnumWindows). Sem janela (fechado OU escondido na bandeja): spotify: abre/mostra a
        // instancia unica; espera a janela de verdade (ate 25 s). 22:12 falhou com "spotify fechado" por detectar errado.
        var hSpot = WindowsHands.JanelaDoProcesso("Spotify");
        if (hSpot == IntPtr.Zero)
        {
            if (!Abrir()) return "Nao consegui abrir o Spotify (o exe nao respondeu). Diga isso a ele.";
            // 22:27: a janela de carga aparece em 1 s e morre; o script pegava esse handle e dava "sem resposta". Espera
            // a janela E a sessao de midia (so existe com o app inteiro de pe), e re-le o handle depois.
            var pronto = false; var viu = false;
            for (var i = 0; i < 60 && !pronto; i++)
            {
                await Task.Delay(500);
                var n = System.Diagnostics.Process.GetProcessesByName("Spotify").Length;
                if (n > 0) viu = true; else if (viu) return CrashMsg;   // abriu e morreu: crash do proprio Spotify
                hSpot = WindowsHands.JanelaDoProcesso("Spotify"); pronto = hSpot != IntPtr.Zero && await Sessao() is not null;
            }
            if (!pronto) return "Nao consegui: pedi pra abrir o Spotify e em 30 s ele nao ficou pronto. Diga isso a ele e pergunte se o Spotify esta instalado.";
            await Task.Delay(2500);   // a tela interna ainda monta depois da janela
            hSpot = WindowsHands.JanelaDoProcesso("Spotify");
            if (hSpot == IntPtr.Zero) return "Nao consegui: o Spotify abriu mas a janela sumiu. Diga isso a ele.";
        }
        var antes = await LerEstado();
        var eraMinimizado = hSpot != IntPtr.Zero && WindowsHands.Minimizada(hSpot);
        if (eraMinimizado) { WindowsHands.MostrarSemFoco(hSpot); await Task.Delay(400); }
        // coberto tambem nao atualiza: por cima sem foco durante a busca (~4 s), depois volta pro lugar
        var acima = hSpot != IntPtr.Zero ? WindowsHands.TopoSemFoco(hSpot) : IntPtr.Zero;
        try { return await TocarPorNomeCore(termo, tipo, antes, hSpot); }
        finally
        {
            // nao minimiza de volta (era o "abre e fecha"): fica restaurado no fundo da pilha, atras do que ele usa
            if (hSpot != IntPtr.Zero) { WindowsHands.VoltarNaPilha(hSpot, acima); if (eraMinimizado) WindowsHands.ParaOFundo(hSpot); }
        }
    }

    private static async Task<string> TocarPorNomeCore(string termo, string tipo, Estado? antes, IntPtr hSpot)
    {
        var dir = Path.Combine(AppPaths.Cache);
        Directory.CreateDirectory(dir);
        var ps1 = Path.Combine(dir, "spotify-play.ps1");
        if (!File.Exists(ps1) || File.ReadAllText(ps1) != ScriptPlay) File.WriteAllText(ps1, ScriptPlay, new System.Text.UTF8Encoding(true));
        var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ps1}\" -termo \"{termo.Replace("\"", "")}\" -tipo {tipo} -hwnd {hSpot.ToInt64()}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        string saida;
        using (var p = System.Diagnostics.Process.Start(psi)!)
        {
            var leitura = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(40000)) { try { p.Kill(); } catch (Exception) { } return $"Nao consegui: a busca por '{termo}' nao respondeu em 40 s. A busca ficou aberta no Spotify."; }
            saida = (await leitura).Trim();
        }
        var linha = saida.Split('\n').LastOrDefault(l => l.StartsWith("OK|") || l.StartsWith("ERRO|"))?.Trim() ?? "ERRO|sem resposta";
        if (linha.StartsWith("ERRO|")) return $"Nao consegui tocar '{termo}': {linha[5..]}. A busca ficou aberta no Spotify; diga isso a ele.";
        var escolhido = linha.Split('|')[1];

        // Prova: tocando e com faixa diferente de antes (ou estava parado).
        Estado? depois = null;
        for (var i = 0; i < 16; i++)
        {
            await Task.Delay(250);
            depois = await LerEstado();
            if (depois is { Tocando: true, Titulo.Length: > 0 } && (antes is null || !antes.Tocando || Faixa(depois) != Faixa(antes))) break;
        }
        // the same song requested twice: a 2a dava "segue na mesma faixa" com a musica certa tocando
        if (depois is { Tocando: true, Titulo.Length: > 0 } && escolhido.StartsWith(depois.Titulo, StringComparison.OrdinalIgnoreCase))
            return $"Tocando '{escolhido}' no Spotify (ja era ela). Faixa agora: '{Faixa(depois)}'.\n" + Prova("tocar_nome", antes, depois, new { termo, escolhido });
        if (depois is null || !depois.Tocando || (antes is { Tocando: true } && Faixa(depois) == Faixa(antes)))
            return $"Nao consegui confirmar: cliquei Play em '{escolhido}', mas o Spotify segue {(depois?.Tocando == true ? "na mesma faixa" : "parado")}. Nao diga que tocou.";
        return $"Tocando '{escolhido}' no Spotify. Faixa agora: '{Faixa(depois)}'.\n" + Prova("tocar_nome", antes, depois, new { termo, escolhido });
    }
}

/// <summary>Volume por programa (o mixer do Windows), so o que a mao musica precisa: achar a sessao do Spotify.</summary>
internal static class AudioSessoes
{
    public static ISimpleAudioVolume? Spotify()
    {
        var pids = System.Diagnostics.Process.GetProcessesByName("Spotify").Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return null;
        var enumType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!;
        var en = (IMMDeviceEnumerator)Activator.CreateInstance(enumType)!;
        try
        {
            en.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var dev);
            var iid = typeof(IAudioSessionManager2).GUID;
            dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var o);
            var mgr = (IAudioSessionManager2)o;
            mgr.GetSessionEnumerator(out var se);
            se.GetCount(out var n);
            for (var i = 0; i < n; i++)
            {
                se.GetSession(i, out var ctl);
                if (ctl is IAudioSessionControl2 c2 && c2.GetProcessId(out var pid) == 0 && pids.Contains(pid))
                    return (ISimpleAudioVolume)ctl;
                Marshal.ReleaseComObject(ctl);
            }
            return null;
        }
        finally { Marshal.ReleaseComObject(en); }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int NotImpl1();
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int NotImpl1();
        int NotImpl2();
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetSession(int i, [MarshalAs(UnmanagedType.IUnknown)] out object s);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl (9 metodos) + GetSessionIdentifier, GetSessionInstanceIdentifier
        int N1(); int N2(); int N3(); int N4(); int N5(); int N6(); int N7(); int N8(); int N9(); int N10(); int N11();
        [PreserveSig] int GetProcessId(out uint pid);
    }
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolume(out float level);
}
