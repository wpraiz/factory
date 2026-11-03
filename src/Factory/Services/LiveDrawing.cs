using System.Runtime.InteropServices;
using System.Text.Json;

namespace Factory.Services;

/// <summary>
/// Desenho AO VIVO no Paint, traco por traco com o mouse, so the user can WATCH
/// Odin draw. Cada forma e uma lista de tracos em coordenadas de -1..1;
/// a prova le os pixels de uma amostra dos pontos antes/depois (WindowsHands.Conferir). Se nao sair inteiro, a
/// mao diz isso e oferece a versao em imagem (Assets\drawings.ps1), que e a garantida.
/// </summary>
public static class LiveDrawing
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    private static List<(double X, double Y)> Circulo(double cx, double cy, double r, int n = 72, double de = 0, double ate = 2 * Math.PI)
        => Enumerable.Range(0, n + 1).Select(i => (cx + r * Math.Cos(de + (ate - de) * i / n), cy + r * Math.Sin(de + (ate - de) * i / n))).ToList();
    private static List<(double X, double Y)> Linha(params (double X, double Y)[] p) => p.ToList();
    private static List<(double X, double Y)> Fechada(params (double X, double Y)[] p) => p.Append(p[0]).ToList();

    /// <summary>Tracos da forma, em -1..1 (y pra baixo). Null = forma sem versao ao vivo.</summary>
    public static List<List<(double X, double Y)>>? Tracos(string tipo) => tipo switch
    {
        "circulo" => [Circulo(0, 0, 0.9)],
        "pacman" =>
        [
            // corpo: centro -> boca -> arco de 300 graus -> centro
            [(0, 0), .. Circulo(0, 0, 0.75, 80, Math.PI / 6, 2 * Math.PI - Math.PI / 6), (0, 0)],
            Circulo(0.12, -0.38, 0.07, 16),
            Circulo(0.95, 0, 0.05, 12), Circulo(1.15, 0, 0.05, 12), Circulo(1.35, 0, 0.05, 12),
        ],
        "estrela" => [Fechada(Enumerable.Range(0, 10).Select(i => { var r = i % 2 == 0 ? 0.95 : 0.4; var a = -Math.PI / 2 + i * Math.PI / 5; return (r * Math.Cos(a), r * Math.Sin(a)); }).ToArray())],
        "coracao" => [Enumerable.Range(0, 81).Select(i => { var t = 2 * Math.PI * i / 80; return (0.055 * 16 * Math.Pow(Math.Sin(t), 3), -0.055 * (13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t))); }).ToList()],
        "sol" =>
        [
            Circulo(0, 0, 0.5),
            .. Enumerable.Range(0, 12).Select(i => { var a = i * Math.PI / 6; return Linha((0.6 * Math.Cos(a), 0.6 * Math.Sin(a)), (0.9 * Math.Cos(a), 0.9 * Math.Sin(a))); }),
            Circulo(-0.17, -0.12, 0.05, 12), Circulo(0.17, -0.12, 0.05, 12),
            Circulo(0, 0.05, 0.22, 24, Math.PI * 0.15, Math.PI * 0.85),
        ],
        "lua" => [[.. Circulo(0, 0, 0.85, 60, Math.PI * 0.3, Math.PI * 1.7), .. Circulo(0.35, 0, 0.75, 60, Math.PI * 1.55, Math.PI * 0.45)]],
        "foguete" =>
        [
            // corpo com bico arredondado
            // comeca pela base: o 1o segmento logo depois de apertar o botao e o que o Paint mais perde (bico sumiu no teste)
            [(-0.3, 0.6), (-0.3, -0.2), .. Circulo(0, -0.2, 0.3, 24, Math.PI, 2 * Math.PI), (0.3, 0.6), (-0.3, 0.6)],
            Fechada((-0.3, 0.2), (-0.6, 0.7), (-0.3, 0.6)), Fechada((0.3, 0.2), (0.6, 0.7), (0.3, 0.6)),
            Circulo(0, -0.1, 0.12, 20),
            Linha((-0.18, 0.6), (0, 0.95), (0.18, 0.6)),
        ],
        "pikachu" =>
        [
            Circulo(0, 0.15, 0.62, 80),                                                        // cabeca
            Fechada((-0.25, -0.35), (-0.85, -1.0), (-0.55, -0.15)), Fechada((0.25, -0.35), (0.85, -1.0), (0.55, -0.15)),   // orelhas
            Linha((-0.72, -0.85), (-0.55, -0.6)), Linha((0.72, -0.85), (0.55, -0.6)),            // ponta preta
            Circulo(-0.24, 0.02, 0.08, 16), Circulo(0.24, 0.02, 0.08, 16),                       // olhos
            Circulo(-0.45, 0.32, 0.11, 16), Circulo(0.45, 0.32, 0.11, 16),                       // bochechas
            Circulo(0, 0.2, 0.03, 10),                                                           // nariz
            Circulo(-0.1, 0.22, 0.1, 16, Math.PI * 0.1, Math.PI * 0.9), Circulo(0.1, 0.22, 0.1, 16, Math.PI * 0.1, Math.PI * 0.9),   // boca
        ],
        "buraconegro" =>
        [
            Circulo(0, 0, 0.9), Circulo(0, 0, 0.72), Circulo(0, 0, 0.5),
            // espiral pro centro
            Enumerable.Range(0, 120).Select(i => { var t = i / 120.0; var a = t * 6 * Math.PI; var r = 0.45 * (1 - t); return (r * Math.Cos(a), r * Math.Sin(a)); }).ToList(),
            // raios de materia caindo
            .. Enumerable.Range(0, 6).Select(i => { var a = i * Math.PI / 3 + 0.3; return Linha((0.95 * Math.Cos(a), 0.95 * Math.Sin(a)), (1.15 * Math.Cos(a + 0.25), 1.15 * Math.Sin(a + 0.25))); }),
        ],
        "sonic" =>
        [
            Circulo(0, 0.05, 0.55, 80, Math.PI * 0.55, Math.PI * 2.45),                           // cabeca (aberta atras, onde entram os espinhos)
            // 3 espinhos pra tras (esquerda), de cima pra baixo
            Linha((-0.2, -0.5), (-0.95, -0.75), (-0.42, -0.2)),
            Linha((-0.42, -0.2), (-1.05, -0.25), (-0.5, 0.1)),
            Linha((-0.5, 0.1), (-1.0, 0.3), (-0.45, 0.4)),
            Linha((-0.45, 0.4), (-0.8, 0.75), (-0.3, 0.55)),
            // orelha
            Fechada((0.1, -0.5), (0.3, -0.9), (0.42, -0.42)),
            // olhos grandes juntos (oval) + divisao + pupilas
            Circulo(0.18, -0.08, 0.3, 48),
            Linha((0.18, -0.38), (0.18, 0.2)),
            Circulo(0.08, -0.02, 0.06, 14), Circulo(0.32, -0.02, 0.06, 14),
            // nariz e boca (sem focinho: no teste o arco sobrepunha o olho e virava bola)
            Circulo(0.6, 0.22, 0.06, 14),
            Circulo(0.3, 0.32, 0.16, 20, Math.PI * 0.15, Math.PI * 0.85),
        ],
        _ => null,
    };

    // ---------- composicao: N desenhos lado a lado, cada um no seu espaco, conferidos um a um ----------
    // before, every drawing ia pro centro da JANELA,
    // por cima do anterior e fora da tela branca (canvas de 450 px). Agora e plano -> execucao -> conferencia:
    // (1) acha a tela branca de verdade na tela; (2) planeja uma grade de espacos vazios (le a tinta que ja existe);
    // sem espaco pra todos, abre uma tela branca nova do tamanho da janela; (3) recusa antes de mexer no mouse se
    // algum ponto cair fora da tela branca; (4) desenha um por vez e confere os pixels de cada um no proprio espaco;
    // (5) no fim confere que nenhum traco novo caiu fora dos espacos planejados.

    private static (double X0, double Y0, double X1, double Y1) Caixa(List<List<(double X, double Y)>> tr)
    {
        var p = tr.SelectMany(t => t).ToList();
        return (p.Min(q => q.X), p.Min(q => q.Y), p.Max(q => q.X), p.Max(q => q.Y));
    }

    private static int TintaNa((int L, int T, int R, int B) area, IEnumerable<(int L, int T, int R, int B)> exceto, int passo = 4)
    {
        var fora = exceto.ToList();
        return WindowsHands.NaTela(area.L, area.T, area.R - area.L + 1, area.B - area.T + 1, px =>
        {
            var n = 0;
            for (var y = area.T + 2; y < area.B - 1; y += passo)
                for (var x = area.L + 2; x < area.R - 1; x += passo)
                    if (!fora.Any(f => x >= f.L && x <= f.R && y >= f.T && y <= f.B) && WindowsHands.Tinta(px(x, y))) n++;
            return n;
        });
    }

    /// <summary>Tela branca nova: BMP branco do tamanho pedido aberto no Paint. Devolve a janela ou Zero.</summary>
    private static IntPtr TelaNova(string dir, string tipo, int w, int h)
    {
        Directory.CreateDirectory(dir);
        var nome = $"{DateTime.Now:yyyy-MM-dd-HHmmss}-tela-{tipo}";
        var arq = Path.Combine(dir, nome + ".bmp");
        var linha = (w * 3 + 3) & ~3;
        using (var fs = File.Create(arq))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0x4D42); bw.Write(54 + linha * h); bw.Write(0); bw.Write(54);
            bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((ushort)1); bw.Write((ushort)24); bw.Write(0); bw.Write(linha * h); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
            var branca = Enumerable.Repeat((byte)255, linha).ToArray();
            for (var y = 0; y < h; y++) bw.Write(branca);
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("mspaint", $"\"{arq}\"") { UseShellExecute = true }); }
        catch (Exception) { return IntPtr.Zero; }
        var jan = IntPtr.Zero;
        for (var i = 0; i < 40 && jan == IntPtr.Zero; i++) { Thread.Sleep(250); jan = WindowsHands.FindByTitle(nome); }
        if (jan != IntPtr.Zero) { OpenedWindows.Registrar(jan, "desenhar"); Thread.Sleep(1500); }   // o Paint novo demora a montar a tela; entra no registro de abertos
        return jan;
    }

    public static (bool Ok, string Texto, object? Prova) Compor(IntPtr h, string tipo, int quantidade, int raio, string dirDesenhos, int msPorPonto = 15)
    {
        var tracos = Tracos(tipo);
        if (tracos is null) return (false, $"'{tipo}' nao tem versao ao vivo", null);
        quantidade = Math.Clamp(quantidade, 1, 12);
        var (bx0, by0, bx1, by1) = Caixa(tracos);
        double bw = bx1 - bx0, bh = by1 - by0;
        if (WindowsHands.PrepararPaint(h) is { } erro) return (false, erro, null);

        // (2) plano: espacos livres na tela branca atual, do tamanho pedido (ou menor se nao couber)
        List<(int L, int T, int R, int B)> Grade((int L, int T, int R, int B) c, double esc)
        {
            int cw = (int)Math.Ceiling(bw * esc / 0.85), ch = (int)Math.Ceiling(bh * esc / 0.85);
            int cols = Math.Max(0, (c.R - c.L - 8) / cw), lins = Math.Max(0, (c.B - c.T - 8) / ch);
            int ox = c.L + (c.R - c.L - cols * cw) / 2, oy = c.T + (c.B - c.T - lins * ch) / 2;
            return [.. from l in Enumerable.Range(0, lins) from k in Enumerable.Range(0, cols) select (ox + k * cw, oy + l * ch, ox + (k + 1) * cw - 1, oy + (l + 1) * ch - 1)];
        }
        // maior escala em que N espacos cabem na tela c (busca por colunas)
        double Encaixe((int L, int T, int R, int B) c, int n) => Enumerable.Range(1, n)
            .Max(cols => 0.85 * Math.Min((c.R - c.L - 8) / (double)cols / bw, (c.B - c.T - 8) / (double)((n + cols - 1) / cols) / bh));

        var canvas = WindowsHands.CanvasBranco(h);
        var novaTela = false;
        List<(int L, int T, int R, int B)> espacos = [];
        double escala = 0;
        if (canvas is { } c0)
        {
            escala = Math.Min(raio, Encaixe(c0, quantidade));
            if (escala >= 45)
                espacos = Grade(c0, escala).Where(e => TintaNa((Math.Max(e.L, c0.L), Math.Max(e.T, c0.T), Math.Min(e.R, c0.R), Math.Min(e.B, c0.B)), []) == 0).Take(quantidade).ToList();
        }
        if (espacos.Count < quantidade)
        {
            // sem espaco livre pra todos: tela branca nova do tamanho da janela maximizada (a "parte branca gigante")
            var j = WindowsHands.Retangulo(h);
            int w = Math.Clamp(j.R - j.L - 160, 400, 3000), hh = Math.Clamp(j.B - j.T - 330, 300, 1800);
            var nova = TelaNova(dirDesenhos, tipo, w, hh);
            if (nova == IntPtr.Zero) return (false, "nao consegui abrir uma tela branca nova no Paint", null);
            h = nova; novaTela = true;
            if (WindowsHands.PrepararPaint(h) is { } erro2) return (false, erro2, null);
            Thread.Sleep(400);
            canvas = WindowsHands.CanvasBranco(h);
            if (canvas is not { } c1) return (false, "abri a tela branca nova mas nao achei a parte branca dela na tela", null);
            escala = Math.Min(Math.Max(raio, 160), Encaixe(c1, quantidade));   // tela nova: o maior que couber
            if (escala < 30) return (false, $"{quantidade} nao cabem nem pequenos na tela; peca menos", null);
            espacos = Grade(c1, escala).Take(quantidade).ToList();
            if (espacos.Count < quantidade) return (false, "o plano nao fechou: espacos a menos que o pedido", null);
        }
        var tela = canvas!.Value;

        // pixels de cada desenho, centrado no seu espaco
        var itens = espacos.Select(e =>
        {
            double cx = (e.L + e.R) / 2.0 - (bx0 + bw / 2) * escala, cy = (e.T + e.B) / 2.0 - (by0 + bh / 2) * escala;
            return tracos.Select(t => t.Select(p => ((int)Math.Round(cx + p.X * escala), (int)Math.Round(cy + p.Y * escala))).ToList()).ToList();
        }).ToList();
        // (3) recusa antes de mexer no mouse: ponto fora da tela branca
        if (itens.SelectMany(i => i).SelectMany(t => t).Any(p => p.Item1 < tela.L + 3 || p.Item1 > tela.R - 3 || p.Item2 < tela.T + 3 || p.Item2 > tela.B - 3))
            return (false, "o plano jogava traco fora da tela branca; nao desenhei nada", null);

        var foraAntes = TintaNa(tela, espacos);
        var t0 = DateTime.Now;
        var porItem = new List<object>();
        var feitos = 0;
        for (var k = 0; k < itens.Count; k++)
        {
            var pix = itens[k];
            var amostra = pix.SelectMany(t => t.Where((_, i) => i % 6 == 3)).ToList();
            if (amostra.Count < 8) amostra = pix.SelectMany(t => t).ToList();
            var antes = WindowsHands.Pixels(amostra);
            foreach (var t in pix) WindowsHands.TracoLento(t, msPorPonto);
            var minimo = amostra.Count * 7 / 10;
            var mudou = WindowsHands.Conferir(antes, amostra, minimo);
            porItem.Add(new { n = k + 1, pontos_pintados = mudou, pontos_conferidos = amostra.Count });
            if (mudou < minimo) break;   // mouse mexido ou Paint coberto: para e diz quantos sairam
            feitos++;
        }
        // (5) conferencia final: traco novo fora dos espacos planejados = vazou / caiu em cima de outro
        var vazou = Math.Max(0, TintaNa(tela, espacos) - foraAntes);
        var lins = espacos.Select(e => e.T).Distinct().Count();
        var ok = feitos == quantidade && vazou <= 6;
        var prova = new
        {
            mao = "desenhar", forma = tipo, modo = "ao_vivo", coerente = ok, pedidos = quantidade, desenhados = feitos,
            grade = $"{lins} linha(s) x {(espacos.Count + lins - 1) / lins}", tela_branca = $"{tela.R - tela.L}x{tela.B - tela.T}", tela_nova = novaTela,
            escala = Math.Round(escala), tinta_fora_do_lugar = vazou, por_item = porItem,
            segundos = Math.Round((DateTime.Now - t0).TotalSeconds, 1), fonte = "pixels da tela: tela branca achada, espaco de cada desenho e tinta fora deles", lido_em = DateTimeOffset.Now.ToString("o"),
        };
        var onde = $"{prova.grade} numa tela branca {(novaTela ? "NOVA " : "")}de {prova.tela_branca}";
        if (ok) return (true, $"{feitos} de {quantidade} {tipo} desenhados ao vivo, cada um no seu espaco ({onde}), em {prova.segundos} s; todos conferidos na tela e nenhum traco fora do lugar", prova);
        if (feitos < quantidade) return (false, $"so {feitos} de {quantidade} {tipo} sairam inteiros ({onde}); o {feitos + 1}o falhou (mouse mexido ou Paint coberto)", prova);
        return (false, $"os {quantidade} {tipo} sairam, mas {vazou} pontos de tinta cairam fora dos espacos planejados ({onde}); confira a tela", prova);
    }

    /// <summary>Desenha no Paint (janela h) e devolve (ok, texto, prova). msPorPonto ~25 = visivel e confiavel.</summary>
    public static (bool Ok, string Texto, object? Prova) Desenhar(IntPtr h, string tipo, int raio, int msPorPonto = 25)
    {
        var tracos = Tracos(tipo);
        if (tracos is null) return (false, $"'{tipo}' nao tem versao ao vivo", null);
        if (WindowsHands.PrepararPaint(h) is { } erro) return (false, erro, null);
        if (!GetWindowRect(h, out var r)) return (false, "nao li o retangulo da janela", null);
        // centro da tela util do Paint (abaixo da faixa de ferramentas), escala = raio
        var cx = (r.L + r.R) / 2.0; var cy = (r.T + r.B) / 2.0 + 60;
        var esc = Math.Clamp(raio, 60, Math.Min((r.R - r.L) / 3, (r.B - r.T) / 3) - 20);
        var pix = tracos.Select(t => t.Select(p => ((int)Math.Round(cx + p.X * esc), (int)Math.Round(cy + p.Y * esc))).ToList()).ToList();
        // amostra de prova: 1 ponto a cada 6 de cada traco (todos os tracos entram)
        var amostra = pix.SelectMany(t => t.Where((_, i) => i % 6 == 3)).ToList();
        if (amostra.Count < 8) amostra = pix.SelectMany(t => t).ToList();
        var antes = WindowsHands.Pixels(amostra);
        var t0 = DateTime.Now;
        foreach (var t in pix) WindowsHands.TracoLento(t, msPorPonto);
        var minimo = amostra.Count * 7 / 10;
        var mudou = WindowsHands.Conferir(antes, amostra, minimo);
        var prova = new { mao = "desenhar", forma = tipo, modo = "ao_vivo", coerente = mudou >= minimo, pontos_conferidos = amostra.Count, pontos_pintados = mudou, tracos = pix.Count, segundos = Math.Round((DateTime.Now - t0).TotalSeconds, 1), fonte = "pixels da tela antes/depois de cada traco", lido_em = DateTimeOffset.Now.ToString("o") };
        return mudou >= minimo
            ? (true, $"{tipo} desenhado ao vivo no Paint em {prova.segundos} s, {pix.Count} tracos; {mudou} de {amostra.Count} pontos conferidos na tela", prova)
            : (false, $"o {tipo} saiu incompleto: so {mudou} de {amostra.Count} pontos apareceram na tela (mouse mexido ou Paint coberto)", prova);
    }
}
