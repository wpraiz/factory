using System.Runtime.InteropServices;
using System.Text;
using Factory.ViewModels;

namespace Factory.Services;

/// <summary>Uma janela de terminal vista pelo Odin.</summary>
public sealed record TerminalWindow(IntPtr Handle, string Title, string Class, int Pid);

/// <summary>
/// Mãos do Odin sobre as janelas do Windows, sem windows-mcp (rapido, sem subir processo):
/// listar terminais (Windows Terminal, consoles), trazer pra frente, fechar com WM_CLOSE e
/// digitar texto (SendInput unicode). Design rule: Odin manages the Claude Code
/// terminals and types, the user presses Enter. Fechar e digitar so acontecem por pedido falado do usuario.
/// </summary>
public static class WindowsHands
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    /// <summary>Primeira janela visivel cujo titulo contem o texto (sem acento/caixa).</summary>
    public static IntPtr FindByTitle(string contem)
    {
        var alvo = CommandLine.Fold(contem);
        var found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var t = new StringBuilder(512); GetWindowText(h, t, t.Capacity);
            if (t.Length == 0 || !CommandLine.Fold(t.ToString()).Contains(alvo)) return true;
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Desenha um circulo com o mouse de verdade (arrasto com o botao esquerdo) no centro da janela, abaixo da
    /// barra de ferramentas. Deterministico: sem LLM, sem screenshot, ~1 s.
    /// ("e uma piada Odin nao conseguir fazer um circulo no PC").
    /// </summary>
    public static string DrawCircle(IntPtr h, int raio)
    {
        if (PrepararPaint(h) is { } erro) return erro;
        if (!GetWindowRect(h, out var r)) return "nao li o retangulo da janela";
        var cx = (r.L + r.R) / 2; var cy = (r.T + r.B) / 2 + 60;   // 60 px abaixo do centro: foge da faixa de ferramentas
        raio = Math.Clamp(raio, 20, Math.Max(20, Math.Min((r.R - r.L) / 3, (r.B - r.T) / 3)));
        var pontos = Enumerable.Range(0, 24).Select(i => ((int)Math.Round(cx + raio * Math.Cos(2 * Math.PI * i / 24)), (int)Math.Round(cy + raio * Math.Sin(2 * Math.PI * i / 24)))).ToList();
        var antes = Pixels(pontos);
        const uint MOVE = 1, DOWN = 2, UP = 4;
        var passos = 120;
        int px(double a) => (int)Math.Round(cx + raio * Math.Cos(a));
        int py(double a) => (int)Math.Round(cy + raio * Math.Sin(a));
        SetCursorPos(px(0), py(0)); Thread.Sleep(60);
        mouse_event(DOWN, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(40);
        for (var i = 1; i <= passos; i++)
        {
            var a = 2 * Math.PI * i / passos;
            Arrastar(px(a), py(a));
            Thread.Sleep(30);
        }
        mouse_event(UP, 0, 0, 0, UIntPtr.Zero);
        var mudou = Conferir(antes, pontos, 21);
        return mudou >= 21 ? $"circulo de raio {raio} px desenhado em ({cx},{cy}); {mudou} de 24 pontos do contorno mudaram de cor"
            : $"o circulo saiu incompleto ou nao saiu: so {mudou} de 24 pontos do contorno mudaram (Paint coberto, travado ou ferramenta errada)";
    }

    /// <summary>
    /// Pac-Man com o mouse de verdade (o `fazer` levou
    /// 536 s e falhou). Corpo = centro -> canto da boca -> arco de 330 graus -> centro; olho e 3 pastilhas na
    /// direcao da boca. Mesma regra do circulo: sem LLM, sem screenshot, ~2 s.
    /// </summary>
    public static string DrawPacman(IntPtr h, int raio)
    {
        if (PrepararPaint(h) is { } erro) return erro;
        if (!GetWindowRect(h, out var r)) return "nao li o retangulo da janela";
        raio = Math.Clamp(raio, 30, Math.Max(30, Math.Min((r.R - r.L) / 5, (r.B - r.T) / 3)));
        var cx = (r.L + r.R) / 2 - raio; var cy = (r.T + r.B) / 2 + 60;   // puxa pra esquerda: sobra espaco pras pastilhas
        (int, int) P(double x, double y) => ((int)Math.Round(x), (int)Math.Round(y));
        (int, int) Arco(double ox, double oy, double rr, double a) => P(ox + rr * Math.Cos(a), oy + rr * Math.Sin(a));

        var boca = Math.PI / 6;   // 30 graus pra cada lado
        var corpo = new List<(int, int)> { P(cx, cy) };
        for (var i = 0; i <= 100; i++) corpo.Add(Arco(cx, cy, raio, boca + (2 * Math.PI - 2 * boca) * i / 100));
        corpo.Add(P(cx, cy));
        var tracos = new List<List<(int, int)>> { corpo };
        tracos.Add(Circulo(cx + raio * 0.15, cy - raio * 0.5, Math.Max(4, raio * 0.1)));        // olho
        for (var k = 1; k <= 3; k++) tracos.Add(Circulo(cx + raio * (0.6 + 0.55 * k), cy, Math.Max(4, raio * 0.09)));   // pastilhas
        var amostra = corpo.Where((_, i) => i % 4 == 2).ToList();
        var antes = Pixels(amostra);
        foreach (var t in tracos) Traco(t);
        var mudou = Conferir(antes, amostra, amostra.Count * 85 / 100);
        return mudou >= amostra.Count * 85 / 100 ? $"pacman de raio {raio} px desenhado em ({cx},{cy}), com olho e 3 pastilhas; {mudou} de {amostra.Count} pontos do corpo mudaram de cor"
            : $"o pacman saiu incompleto ou nao saiu: so {mudou} de {amostra.Count} pontos do corpo mudaram (Paint coberto, travado ou ferramenta errada)";

        List<(int, int)> Circulo(double ox, double oy, double rr)
        {
            var l = new List<(int, int)>();
            for (var i = 0; i <= 24; i++) l.Add(Arco(ox, oy, rr, 2 * Math.PI * i / 24));
            return l;
        }
    }

    // o desenho "confirmava" com o Paint vazio (3x) e quebrava com o Paint minimizado ("'20' cannot be greater
    // than 9": a janela minimizada tem 27 px). Agora: maximiza, exige o Paint na frente e confere pixel antes/depois.
    internal static string? PrepararPaint(IntPtr h)
    {
        const int SW_MAXIMIZE = 3;
        ShowWindow(h, SW_MAXIMIZE);
        Thread.Sleep(300);
        if (!Focus(h) || GetForegroundWindow() != h) return "o Windows nao deixou trazer o Paint pra frente; nao arrastei o mouse pra nao riscar outra janela";
        Thread.Sleep(250);
        return null;
    }

    // SetCursorPos + MOVE relativo zero vira so pontinhos no Paint novo (o arrasto nao chega como movimento
    // com botao apertado). Movimento ABSOLUTO pela area de trabalho virtual chega como arrasto de verdade.
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    private static void Arrastar(int x, int y)
    {
        const uint MOVE = 0x0001, VIRTUALDESK = 0x4000, ABSOLUTE = 0x8000;
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var nx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
        var ny = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
        mouse_event(MOVE | ABSOLUTE | VIRTUALDESK, nx, ny, 0, UIntPtr.Zero);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);

    // o traco fino passa a 1 px do ponto calculado (arredondamento): vale o pixel MAIS ESCURO num quadrado 5x5.
    // Copia a regiao uma vez pra memoria (BitBlt): GetPixel direto na tela custava ~12 s por desenho.
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    internal static uint[] Pixels(List<(int X, int Y)> pts)
    {
        static int Luz(uint c) => (int)(c & 0xFF) + (int)((c >> 8) & 0xFF) + (int)((c >> 16) & 0xFF);
        int x0 = pts.Min(p => p.X) - 3, y0 = pts.Min(p => p.Y) - 3, w = pts.Max(p => p.X) - x0 + 4, h = pts.Max(p => p.Y) - y0 + 4;
        var tela = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(tela);
        var bmp = CreateCompatibleBitmap(tela, w, h);
        var velho = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, w, h, tela, x0, y0, 0x00CC0020 /*SRCCOPY*/);
            return pts.Select(p =>
            {
                var escuro = GetPixel(mem, p.X - x0, p.Y - y0);
                for (var dx = -2; dx <= 2; dx++)
                    for (var dy = -2; dy <= 2; dy++)
                    {
                        var c = GetPixel(mem, p.X - x0 + dx, p.Y - y0 + dy);
                        if (c != 0xFFFFFFFF && Luz(c) < Luz(escuro)) escuro = c;
                    }
                return escuro;
            }).ToArray();
        }
        finally { SelectObject(mem, velho); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, tela); }
    }

    // o Paint desenha o traco com atraso: confere ate 3x (0,5 s, 1 s, 1,5 s) antes de dizer que nao saiu
    internal static int Conferir(uint[] antes, List<(int X, int Y)> pts, int minimo)
    {
        var mudou = 0;
        for (var i = 0; i < 3 && mudou < minimo; i++) { Thread.Sleep(500); mudou = Mudaram(antes, Pixels(pts)); }
        return mudou;
    }

    // ponto "mudou" = cor diferente e mais escura (traco preto do lapis sobre a tela branca)
    internal static int Mudaram(uint[] antes, uint[] depois)
    {
        static int Luz(uint c) => (int)(c & 0xFF) + (int)((c >> 8) & 0xFF) + (int)((c >> 16) & 0xFF);
        return antes.Zip(depois).Count(p => p.First != p.Second && Luz(p.Second) < Luz(p.First) - 60);
    }

    /// <summary>Copia uma regiao da tela uma vez (BitBlt) e entrega um leitor de pixel (x, y em coords de tela).</summary>
    internal static T NaTela<T>(int x0, int y0, int w, int h, Func<Func<int, int, uint>, T> ler)
    {
        var tela = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(tela);
        var bmp = CreateCompatibleBitmap(tela, w, h);
        var velho = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, w, h, tela, x0, y0, 0x00CC0020 /*SRCCOPY*/);
            return ler((x, y) => GetPixel(mem, x - x0, y - y0));
        }
        finally { SelectObject(mem, velho); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, tela); }
    }

    private static bool Branco(uint c) => (c & 0xFF) >= 248 && ((c >> 8) & 0xFF) >= 248 && ((c >> 16) & 0xFF) >= 248 && c != 0xFFFFFFFF;
    internal static bool Tinta(uint c) => c != 0xFFFFFFFF && (c & 0xFF) + ((c >> 8) & 0xFF) + ((c >> 16) & 0xFF) < 600;

    // maior trecho branco de uma linha/coluna, aceitando buracos de ate 8 px (traco de desenho cruzando)
    private static (int A, int B) Trecho(Func<int, bool> branco, int de, int ate)
    {
        (int A, int B) melhor = (0, -1); var ini = -1; var ultimo = -1;
        for (var i = de; i <= ate; i++)
        {
            if (!branco(i)) { if (ini >= 0 && i - ultimo > 8) { if (ultimo - ini > melhor.B - melhor.A) melhor = (ini, ultimo); ini = -1; } continue; }
            if (ini < 0) ini = i;
            ultimo = i;
        }
        if (ini >= 0 && ultimo - ini > melhor.B - melhor.A) melhor = (ini, ultimo);
        return melhor;
    }

    /// <summary>o desenho ia pro centro da JANELA, nao da tela branca (canvas de 450 px: Pac-Man cortado e um
    /// em cima do outro). Acha a parte branca visivel do Paint lendo a tela: retangulo em coords de tela ou null.</summary>
    internal static (int L, int T, int R, int B) Retangulo(IntPtr h) => GetWindowRect(h, out var r) ? (r.L, r.T, r.R, r.B) : (0, 0, 0, 0);

    internal static (int L, int T, int R, int B)? CanvasBranco(IntPtr h)
    {
        if (!GetWindowRect(h, out var r)) return null;
        int w = r.R - r.L, hh = r.B - r.T;
        if (w < 300 || hh < 300) return null;
        return NaTela(r.L, r.T, w, hh, px =>
        {
            (int A, int B, int Y) linha = (0, -1, 0);
            for (var y = r.T + hh * 30 / 100; y < r.T + hh * 85 / 100; y += 12)
            {
                var yy = y;
                var t = Trecho(x => Branco(px(x, yy)), r.L + 4, r.R - 5);
                if (t.B - t.A > linha.B - linha.A) linha = (t.A, t.B, y);
            }
            if (linha.B - linha.A < 200) return ((int, int, int, int)?)null;
            var cols = new[] { linha.A + 6, (linha.A + linha.B) / 2, linha.B - 6 }
                .Select(x => Trecho(y => Branco(px(x, y)), r.T + 4, r.B - 5)).OrderBy(t => t.A).ToList();
            var col = cols[1];   // mediana: um traco no meio de uma coluna nao estraga
            if (col.B - col.A < 150) return null;
            return (linha.A, col.A, linha.B, col.B);
        });
    }

    /// <summary>Traco continuo com ritmo controlado (ms entre pontos) e passos de no maximo 6 px, pra Paint nao
    /// perder segmento e pra ele VER o desenho acontecendo.</summary>
    internal static void TracoLento(List<(int X, int Y)> pts, int msPorPonto)
    {
        const uint DOWN = 2, UP = 4;
        var densos = new List<(int X, int Y)> { pts[0] };
        for (var i = 1; i < pts.Count; i++)
        {
            var (ax, ay) = densos[^1]; var (bx, by) = pts[i];
            var d = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
            var n = Math.Max(1, (int)Math.Ceiling(d / 6.0));
            for (var k = 1; k <= n; k++) densos.Add((ax + (bx - ax) * k / n, ay + (by - ay) * k / n));
        }
        SetCursorPos(densos[0].X, densos[0].Y); Thread.Sleep(80);
        Arrastar(densos[0].X, densos[0].Y); Thread.Sleep(20);
        mouse_event(DOWN, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(60);
        foreach (var (x, y) in densos.Skip(1)) { Arrastar(x, y); Thread.Sleep(Math.Max(4, msPorPonto * 6 / Math.Max(6, 6))); }
        mouse_event(UP, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(120);
    }

    // Um traco continuo: aperta no 1o ponto, arrasta pelos outros, solta.
    internal static void Traco(List<(int X, int Y)> pts)
    {
        const uint MOVE = 1, DOWN = 2, UP = 4;
        SetCursorPos(pts[0].X, pts[0].Y); Thread.Sleep(60);
        mouse_event(DOWN, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(40);
        foreach (var (x, y) in pts.Skip(1)) { Arrastar(x, y); Thread.Sleep(30); }
        mouse_event(UP, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(60);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public KEYBDINPUT ki; public long pad1; public long pad2; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    private const uint WM_CLOSE = 0x0010;
    private const int SW_RESTORE = 9;
    private const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;

    private static readonly HashSet<string> TerminalClasses = new(StringComparer.Ordinal)
    {
        "CASCADIA_HOSTING_WINDOW_CLASS",   // Windows Terminal
        "ConsoleWindowClass",              // conhost (cmd/pwsh solto)
    };

    /// <summary>Janelas de terminal visiveis, na ordem do Z (a da frente primeiro).</summary>
    public static List<TerminalWindow> ListTerminals()
    {
        var list = new List<TerminalWindow>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var cls = new StringBuilder(128); GetClassName(h, cls, cls.Capacity);
            if (!TerminalClasses.Contains(cls.ToString())) return true;
            var t = new StringBuilder(512); GetWindowText(h, t, t.Capacity);
            GetWindowThreadProcessId(h, out var pid);
            list.Add(new TerminalWindow(h, t.ToString(), cls.ToString(), (int)pid));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Acha pelo titulo (pedaco, sem acento/caixa) ou por indice "1", "2"…</summary>
    public static TerminalWindow? Find(string nome)
    {
        var all = ListTerminals();
        if (all.Count == 0) return null;
        var q = CommandLine.Fold(nome ?? "");
        if (q.Length == 0) return all[0];
        if (int.TryParse(q, out var idx) && idx >= 1 && idx <= all.Count) return all[idx - 1];
        return all.FirstOrDefault(w => CommandLine.Fold(w.Title).Contains(q))
            ?? all.Select(w => (w, s: PaletteMatcher.Score(q, w.Title, ""))).Where(x => x.s > 60).OrderByDescending(x => x.s).Select(x => x.w).FirstOrDefault();
    }

    /// <summary>Traz pra frente. O Windows bloqueia SetForegroundWindow de quem nao tem foco; o toque de ALT libera.</summary>
    public static bool Focus(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        var ok = SetForegroundWindow(h);
        Thread.Sleep(120);
        return ok || GetForegroundWindow() == h;
    }

    public static bool Close(IntPtr h) => PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    /// <summary>A janela ainda existe e esta visivel (registro de abertos: some sozinho quando fecha).</summary>
    public static bool Existe(IntPtr h) => h != IntPtr.Zero && IsWindow(h) && IsWindowVisible(h);
    public static uint Pid(IntPtr h) { GetWindowThreadProcessId(h, out var pid); return pid; }
    public static string Titulo(IntPtr h) { var t = new StringBuilder(512); GetWindowText(h, t, t.Capacity); return t.ToString(); }

    // ---------- janelas quaisquer: mover de monitor, maximizar (pelo `fazer` levava 146-157 s) ----------

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT Monitor; public RECT Work; public uint Flags; }

    /// <summary>Monitores: 1 = principal, depois os outros da esquerda pra direita. Area util (sem barra de tarefas).</summary>
    private static List<(RECT Work, bool Principal)> Monitores()
    {
        var l = new List<(RECT, bool)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(m, ref mi)) l.Add((mi.Work, (mi.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return l.OrderByDescending(x => x.Item2).ThenBy(x => x.Item1.L).ToList();
    }

    public static int MonitorCount => Monitores().Count;

    public static IntPtr Foreground() => GetForegroundWindow();
    public static bool Minimizada(IntPtr h) => IsIconic(h);
    /// <summary>Restaura sem roubar o foco (SW_SHOWNOACTIVATE): o Chromium do Spotify minimizado nao atualiza a tela.</summary>
    public static void MostrarSemFoco(IntPtr h) => ShowWindow(h, 4);
    public static void Minimizar(IntPtr h) => ShowWindow(h, 6);
    /// <summary>Janela VISIVEL com titulo de um processo pelo nome (EnumWindows). Process.MainWindowHandle do .NET 8
    /// devolvia handle do Spotify escondido na bandeja e o script PowerShell nao achava ("spotify fechado").</summary>
    public static IntPtr JanelaDoProcesso(string processo)
    {
        var pids = System.Diagnostics.Process.GetProcessesByName(processo).Select(p => (uint)p.Id).ToHashSet();
        if (pids.Count == 0) return IntPtr.Zero;
        var found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid)) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, t.Capacity);
            if (t.Length == 0) return true;
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }
    /// <summary>Manda pro fundo da pilha sem roubar foco (HWND_BOTTOM).</summary>
    public static void ParaOFundo(IntPtr h) => SetWindowPos(h, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);

    // Chromium (Spotify) coberto por outra janela para de atualizar a tela e a acessibilidade ("a busca nao
    // carregou"). Pra mao musica: poe por cima SEM foco durante a busca e devolve a posicao na pilha depois.
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    public static IntPtr TopoSemFoco(IntPtr h)
    {
        const uint NOSIZE = 0x0001, NOMOVE = 0x0002, NOACTIVATE = 0x0010;
        var acima = GetWindow(h, 3 /*GW_HWNDPREV*/);
        SetWindowPos(h, new IntPtr(-1) /*TOPMOST*/, 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
        return acima;
    }
    public static void VoltarNaPilha(IntPtr h, IntPtr acima)
    {
        const uint NOSIZE = 0x0001, NOMOVE = 0x0002, NOACTIVATE = 0x0010;
        SetWindowPos(h, new IntPtr(-2) /*NOTOPMOST*/, 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
        if (acima != IntPtr.Zero) SetWindowPos(h, acima, 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
    }

    /// <summary>Print do monitor N (numeracao do Factory) em PNG, pelo System.Drawing do PowerShell do Windows (~1 s, sem
    /// pacote novo). o ver_tela subia claude + uvx windows-mcp so pra tirar o print (53-85 s). Devolve o caminho
    /// ou null. E dado do usuario (tela): quem chama APAGA o arquivo depois de usar.</summary>
    public static string? CapturarMonitor(string monitor)
    {
        var r = MonitorRect(monitor);
        if (r is null) return null;
        var dir = Path.Combine(AppPaths.Cache, "olhos");
        Directory.CreateDirectory(dir);
        var arq = Path.Combine(dir, $"tela-{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.png");
        var (L, T, R, B) = r.Value;
        var ps = $"Add-Type -AssemblyName System.Drawing; $b=New-Object System.Drawing.Bitmap {R - L},{B - T}; $g=[System.Drawing.Graphics]::FromImage($b); "
               + $"$g.CopyFromScreen({L},{T},0,0,$b.Size); $b.Save('{arq.Replace("'", "''")}',[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose()";
        var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"") { UseShellExecute = false, CreateNoWindow = true };
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit(8000);
        return File.Exists(arq) && new FileInfo(arq).Length > 0 ? arq : null;
    }

    /// <summary>A numeracao do Factory com coordenadas, pra quem olha a tela (ver_tela/fiscal) nao usar a do windows-mcp.
    /// 3 of 5 ver_tela asked for "monitor 1" and the executor described monitor 2.</summary>
    public static string DescreverMonitores()
    {
        var m = Monitores();
        var esq = m.Count > 1 ? m.Min(x => x.Work.L) : 0;
        return string.Join("; ", m.Select((x, i) =>
            $"monitor {i + 1} ({string.Join(", ", new[] { x.Principal ? "principal" : null, m.Count > 1 ? (x.Work.L == esq ? "o da ESQUERDA" : "o da DIREITA") : null }.Where(s => s is not null))}) = x de {x.Work.L} a {x.Work.R}, y de {x.Work.T} a {x.Work.B}"));
    }

    /// <summary>"Monitor 2 (o da esquerda)" na numeracao do Factory, pro rotulo nao ficar a cargo do modelo.</summary>
    public static string RotuloMonitor(string monitor)
    {
        var m = Monitores();
        var r = MonitorRect(monitor);
        if (r is null || m.Count == 0) return "Monitor";
        var idx = m.FindIndex(x => x.Work.L == r.Value.L && x.Work.T == r.Value.T);
        var lado = m.Count < 2 ? "" : r.Value.L == m.Min(x => x.Work.L) ? " (o da esquerda)" : " (o da direita)";
        return $"Monitor {idx + 1}{lado}";
    }

    /// <summary>Retangulo do monitor N na numeracao do Factory (1 = principal); null se nao existe.</summary>
    public static (int L, int T, int R, int B)? MonitorRect(string monitor)
    {
        var m = Monitores();
        var f = CommandLine.Fold(monitor ?? "");
        var i = f switch { "principal" or "" => 0, "esquerda" => m.FindIndex(x => x.Work.L == m.Min(y => y.Work.L)), "direita" => m.FindIndex(x => x.Work.L == m.Max(y => y.Work.L)), _ => int.TryParse(f, out var n) ? n - 1 : -1 };
        if (i < 0 || i >= m.Count) return null;
        var w = m[i].Work; return (w.L, w.T, w.R, w.B);
    }

    /// <summary>Janelas de app visiveis com titulo (sem as de sistema), na ordem do Z.</summary>
    public static List<(IntPtr Handle, string Title)> ListWindows()
    {
        var list = new List<(IntPtr, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var t = new StringBuilder(512); GetWindowText(h, t, t.Capacity);
            var s = t.ToString();
            if (s.Length == 0 || s is "Program Manager" or "Settings" or "Configurações" || s.StartsWith("Windows Input")) return true;
            list.Add((h, s));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Executa e RELE o retangulo depois: devolve o estado real, nao o pedido.</summary>
    public static string Arrange(IntPtr h, string acao, string monitor)
    {
        const int SW_MAXIMIZE = 3, SW_MINIMIZE = 6;
        const uint NOZORDER = 0x0004, NOACTIVATE = 0x0010;
        var a = CommandLine.Fold(acao ?? "");
        if (a is "minimizar" or "esconder") { ShowWindow(h, SW_MINIMIZE); return IsIconic(h) ? "minimizada" : "o Windows nao minimizou"; }
        if (a is "frente" or "focar" or "trazer") return Focus(h) ? "na frente" : "o Windows nao deixou trazer pra frente";
        if (a is "restaurar") { ShowWindow(h, SW_RESTORE); return Onde(h); }
        var mons = Monitores();
        if (a is "mover" or "monitor" || !string.IsNullOrWhiteSpace(monitor))
        {
            var m = CommandLine.Fold(monitor ?? "");
            var alvoIdx = m switch
            {
                "esquerda" or "esquerdo" => mons.Select((x, i) => (x, i)).OrderBy(p => p.x.Work.L).First().i,
                "direita" or "direito" => mons.Select((x, i) => (x, i)).OrderByDescending(p => p.x.Work.L).First().i,
                "principal" or "" => 0,
                _ => int.TryParse(m, out var n) ? n - 1 : -1,
            };
            if (alvoIdx < 0 || alvoIdx >= mons.Count) return $"monitor '{monitor}' nao existe (tem {mons.Count})";
            var w = mons[alvoIdx].Work;
            var eraMax = IsZoomed(h) || a is "maximizar";
            ShowWindow(h, SW_RESTORE);   // maximizada nao move: restaura, move, maximiza de novo
            GetWindowRect(h, out var r);
            int cw = Math.Min(r.R - r.L, w.R - w.L), ch = Math.Min(r.B - r.T, w.B - w.T);
            SetWindowPos(h, IntPtr.Zero, w.L + (w.R - w.L - cw) / 2, w.T + (w.B - w.T - ch) / 2, cw, ch, NOZORDER | NOACTIVATE);
            if (eraMax) ShowWindow(h, SW_MAXIMIZE);
            Focus(h);
            return Onde(h);
        }
        if (a is "maximizar" or "tela cheia") { ShowWindow(h, SW_MAXIMIZE); return Onde(h); }
        return $"acao '{acao}' desconhecida";
    }

    /// <summary>Linha "[prova] {json}": estado LIDO do Windows depois de agir; o HandLog guarda como
    /// evidencia confiavel e tira do texto falado.</summary>
    /// <summary>Janelas em colunas iguais na area util do monitor (padrao: principal). Devolve "no monitor N" ou erro.</summary>
    public static (string Onde, int NoLugar) LadoALado(List<IntPtr> janelas, string monitor)
    {
        const uint NOZORDER = 0x0004;
        var mons = Monitores();
        var m = CommandLine.Fold(monitor ?? "");
        var idx = m is "" or "principal" ? 0 : int.TryParse(m, out var n) ? n - 1 : -1;
        if (idx < 0 || idx >= mons.Count) return ($"monitor '{monitor}' nao existe (tem {mons.Count})", 0);
        var w = mons[idx].Work;
        var largura = (w.R - w.L) / janelas.Count;
        for (var i = 0; i < janelas.Count; i++)
        {
            ShowWindow(janelas[i], SW_RESTORE);
            SetWindowPos(janelas[i], IntPtr.Zero, w.L + i * largura, w.T, largura, w.B - w.T, NOZORDER);
        }
        Thread.Sleep(200);
        // lido de volta: cada janela tem que estar na sua coluna (tolerancia da borda invisivel do Windows 10/11)
        var noLugar = 0;
        for (var i = 0; i < janelas.Count; i++)
            if (GetWindowRect(janelas[i], out var r) && !IsIconic(janelas[i]) && Math.Abs(r.L - (w.L + i * largura)) <= 16 && Math.Abs(r.T - w.T) <= 16) noLugar++;
        return ($"no monitor {idx + 1}", noLugar);
    }

    public static string Prova(IntPtr h, string acao)
    {
        GetWindowRect(h, out var r);
        var t = new StringBuilder(512); GetWindowText(h, t, t.Capacity);
        var cx = (r.L + r.R) / 2; var cy = (r.T + r.B) / 2;
        var idx = Monitores().FindIndex(m => cx >= m.Work.L && cx < m.Work.R && cy >= m.Work.T && cy < m.Work.B);
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            mao = "janela", acao, hwnd = $"0x{h.ToInt64():X}", titulo = t.ToString(), foreground = GetForegroundWindow() == h,
            minimizada = IsIconic(h), maximizada = IsZoomed(h), monitor = IsIconic(h) || idx < 0 ? (int?)null : idx + 1,
            rect = new[] { r.L, r.T, r.R, r.B }, lido_em = DateTimeOffset.Now.ToString("o"),
        });
        return "[prova] " + json;
    }

    /// <summary>Onde a janela esta de verdade: monitor, tamanho, maximizada.</summary>
    private static string Onde(IntPtr h)
    {
        if (IsIconic(h)) return "minimizada";
        GetWindowRect(h, out var r);
        var cx = (r.L + r.R) / 2; var cy = (r.T + r.B) / 2;
        var mons = Monitores();
        var idx = mons.FindIndex(m => cx >= m.Work.L && cx < m.Work.R && cy >= m.Work.T && cy < m.Work.B);
        return $"no monitor {(idx < 0 ? "?" : (idx + 1).ToString())}{(IsZoomed(h) ? ", maximizada" : "")}, {r.R - r.L}x{r.B - r.T} em ({r.L},{r.T})";
    }

    /// <summary>Tecla de midia global (VK_MEDIA_*): funciona com Spotify, YouTube, qualquer player.</summary>
    public static void MediaKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Digita texto unicode na janela em foco; Enter no fim so se pedido.</summary>
    public static void Type(string text, bool enter)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n') { Key(inputs, 0x0D); continue; }
            inputs.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } });
            inputs.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } });
        }
        if (enter) Key(inputs, 0x0D);
        // Em lotes: SendInput com milhares de eventos de uma vez pode ser recusado.
        for (var i = 0; i < inputs.Count; i += 200)
        {
            var chunk = inputs.Skip(i).Take(200).ToArray();
            SendInput((uint)chunk.Length, chunk, Marshal.SizeOf<INPUT>());
            Thread.Sleep(15);
        }
    }

    private static void Key(List<INPUT> inputs, ushort vk)
    {
        inputs.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk } });
        inputs.Add(new INPUT { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } });
    }
}
