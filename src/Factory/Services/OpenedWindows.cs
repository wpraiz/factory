using System.Text.Json;

namespace Factory.Services;

/// <summary>
/// Registry of what the HANDS opened (opened #12 → closes #12). Odin once asked `fazer` to "close the
/// Notepads opened by Factory", nobody knew which ones, and the agent closed the user's own Notepads. Agora toda janela que uma
/// mao abre entra aqui pelo HANDLE, e `janela acao=fechar` so fecha o que esta aqui (o resto exige forcar=true).
/// Copia em <dados>\odin\abertos.json (user data). Janela que sumiu sai sozinha na leitura.
/// </summary>
public static class OpenedWindows
{
    public sealed record Item(long Hwnd, uint Pid, string Processo, string Titulo, string Mao, string RequestId, DateTimeOffset Quando);

    private static readonly object Trava = new();
    private static readonly List<Item> Lista = [];
    private static readonly string Arquivo = Path.Combine(AppPaths.Odin, "abertos.json");
    private static bool _carregado;

    // a janela do proprio Factory e os terminais nunca entram (e nunca sao fechados por aqui)
    private static bool Protegida(IntPtr h, string processo) =>
        h == System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle
        || processo is "Factory" or "WindowsTerminal" or "OpenConsole" or "conhost" or "explorer";

    private static string NomeProcesso(uint pid)
    {
        try { return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch (Exception) { return "?"; }
    }

    private static void Carregar()
    {
        if (_carregado) return;
        _carregado = true;
        try
        {
            if (File.Exists(Arquivo) && JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(Arquivo)) is { } l) Lista.AddRange(l);
        }
        catch (Exception) { /* arquivo corrompido: comeca vazio, o Windows e a fonte da verdade */ }
    }

    private static void Gravar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(Lista, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* registro em disco e copia; o da memoria vale */ }
    }

    /// <summary>Registra uma janela aberta por uma mao. Ignora protegidas e repetidas.</summary>
    public static void Registrar(IntPtr h, string mao, string requestId = "")
    {
        if (!WindowsHands.Existe(h)) return;
        var pid = WindowsHands.Pid(h);
        var proc = NomeProcesso(pid);
        if (Protegida(h, proc)) return;
        lock (Trava)
        {
            Carregar();
            if (Lista.Any(i => i.Hwnd == h.ToInt64())) return;
            Lista.Add(new Item(h.ToInt64(), pid, proc, WindowsHands.Titulo(h), mao, requestId, DateTimeOffset.Now));
            Gravar();
        }
    }

    /// <summary>Foto das janelas agora (pra diff antes/depois de uma tarefa longa).</summary>
    public static HashSet<IntPtr> Foto() => WindowsHands.ListWindows().Select(w => w.Handle).ToHashSet();

    /// <summary>Registra tudo que apareceu desde a foto (tarefa do `fazer`/`sessoes` que mexeu na tela).</summary>
    public static int RegistrarNovas(HashSet<IntPtr> antes, string mao, string requestId)
    {
        var n = 0;
        foreach (var (h, _) in WindowsHands.ListWindows())
            if (!antes.Contains(h)) { Registrar(h, mao, requestId); n++; }
        return n;
    }

    /// <summary>O que ainda esta aberto e foi aberto por nos (limpa o que ja fechou).</summary>
    public static List<Item> Vivos()
    {
        lock (Trava)
        {
            Carregar();
            var antes = Lista.Count;
            Lista.RemoveAll(i => !WindowsHands.Existe(new IntPtr(i.Hwnd)) || WindowsHands.Pid(new IntPtr(i.Hwnd)) != i.Pid);
            if (Lista.Count != antes) Gravar();
            return [.. Lista];
        }
    }

    public static bool EhNosso(IntPtr h) => Vivos().Any(i => i.Hwnd == h.ToInt64());

    public static bool EhProtegida(IntPtr h) => Protegida(h, NomeProcesso(WindowsHands.Pid(h)));
}
