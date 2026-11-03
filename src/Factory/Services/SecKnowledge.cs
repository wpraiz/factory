using System.Text.RegularExpressions;
using Factory.Models;

namespace Factory.Services;

/// <summary>Parecer local de UMA linha (botao POR QUE?). Acao: "confiar" | "cortar" | null (so informar).</summary>
public sealed record SecParecer(string Oque, string Risco, string Sugestao, string? Acao);

/// <summary>
/// "POR QUE?" sem gastar nada:
/// primeiro o que o Factory ja SABE (arquivo sumiu, assinatura, pasta, tipo), depois uma tabela de software conhecido
/// do PC. O parecer mais fundo (IA) e o botao PERGUNTAR AO SQUAD, que so gasta quando o usuario clica.
/// A tabela e opiniao minha, nao fato medido: o texto diz "costuma ser", nunca "e".
/// </summary>
public static partial class SecKnowledge
{
    private sealed record Conhecido(string Padrao, string Oque, string Risco, string Sugestao, string? Acao);

    private static readonly Conhecido[] Tabela =
    [
        new(@"HopToDesk", "Acesso remoto (controlar este PC de outro lugar). Escuta rede e liga junto com o Windows.",
            "ALTO se você não usa: é uma porta de entrada pro PC inteiro. Se usa, o risco é a senha/ID vazar.",
            "Não usa → DESLIGAR (e BLOQUEAR a porta). Usa → deixe, mas feche quando não precisar.", "cortar"),
        new(@"Tailscale|tailscaled", "VPN em malha (Tailscale): conecta seus aparelhos numa rede privada.",
            "Baixo. É o que deixa o celular/Mac falarem com este PC.", "Se você usa Tailscale, IGNORAR.", "confiar"),
        new(@"Spotify", "Cliente do Spotify (porta 57621 é a descoberta de aparelhos na rede local).",
            "Baixo no normal. Mas SEM assinatura digital costuma ser mod (tipo SpotX): o arquivo foi alterado. O Defender já bloqueou trojans em mods assim.",
            "Usa mod e sabe o que é → IGNORAR. Não lembra de ter instalado mod → DESLIGAR e reinstale o oficial.", null),
        new(@"Sideloadly", "Ferramenta pra instalar apps em iPhone fora da App Store. O daemon fica rodando em segundo plano.",
            "Médio: roda sem assinatura digital e fala com o iPhone por USB/rede.",
            "Só usa de vez em quando → DESLIGAR (abre a mão quando precisar).", "cortar"),
        new(@"CCleaner", "Limpador do Windows. A tarefa 'Skip UAC' deixa ele abrir com poder de administrador sem perguntar.",
            "Médio: é um programa com poder alto que já teve versão adulterada em 2017. Skip UAC é conveniência que aumenta a superfície.",
            "Não usa mais → DESLIGAR a tarefa (ou desinstale). Usa → pelo menos mantenha atualizado.", "cortar"),
        new(@"AMD(Install|Link)|InstallManagerApp", "Instalador/atualizador de driver da AMD, assinado pela AMD.",
            "Baixo. Só checa atualização de driver.", "Pode DESLIGAR sem perder nada (o driver continua funcionando).", "cortar"),
        new(@"Razer", "Software da Razer (Cortex/AppEngine), assinado pela Razer. Roda pra otimizar jogos e falar com periféricos.",
            "Baixo, mas pesa na inicialização.", "Não usa os periféricos Razer o tempo todo → DESLIGAR.", "cortar"),
        new(@"LGHUB|lghub", "Logitech G HUB: configura mouse/teclado Logitech.", "Baixo (assinado pela Logitech).",
            "Se as macros/DPI dependem dele, deixe. Senão DESLIGAR.", null),
        new(@"GoogleUpdater|Google.*Update", "Atualizador do Chrome/Google, assinado pela Google.", "Baixo. Mantém o Chrome atualizado (isso é segurança).",
            "IGNORAR. Desligar só deixa o Chrome desatualizado.", "confiar"),
        new(@"Gemini", "App do Gemini (Google) que abre com o Windows.", "Baixo (assinado pela Google).", "Não usa → DESLIGAR.", "cortar"),
        new(@"MicrosoftEdgeAutoLaunch|msedge", "Edge pré-carrega na inicialização pra abrir mais rápido. (msedge nas portas = mDNS 5353, descoberta de dispositivos.)",
            "Baixo (assinado pela Microsoft).", "Usa Edge → IGNORAR. Não usa → DESLIGAR.", null),
        new(@"signal-desktop|Signal", "Signal Desktop (mensageiro), assinado pela Signal.", "Baixo.", "Usa → IGNORAR.", "confiar"),
        new(@"Roblox", "Launcher do Roblox, assinado pela Roblox.", "Baixo para o PC; o risco é a conta/chat do jogo.", "Não é de uso diário → DESLIGAR.", "cortar"),
        new(@"RiotClient|Riot", "Launcher da Riot Games (Valorant/LoL).", "Baixo. Se o arquivo sumiu é resíduo de desinstalação.", "Arquivo sumiu → LIMPAR.", null),
        new(@"^Factory", "Atalho/tarefa do próprio Factory.", "Baixo: é seu.", "IGNORAR.", "confiar"),
        new(@"dasHost", "Device Association Framework Host do Windows (porta UDP 3702 = descoberta de impressoras/dispositivos).",
            "Baixo. Costuma ser do Windows; aparece como REVISAR porque, sem admin, o Factory não lê o caminho.", "Normalmente IGNORAR.", "confiar"),
        new(@"IpOverUsb", "Serviço de rede por USB (Windows Mobile/Phone Link).", "Baixo.", "IGNORAR.", "confiar"),
    ];

    public static SecParecer Explicar(SecRow r)
    {
        var chave = $"{r.Titulo} {r.Caminho} {r.Valor}";
        var k = Tabela.FirstOrDefault(t => Regex.IsMatch(chave, t.Padrao, RegexOptions.IgnoreCase));
        var fatos = Fatos(r);

        // fatos medidos vencem a tabela: arquivo sumiu / adulterado / pasta de risco
        if (r.Estado == SecRow.Orfao)
            return new SecParecer($"{(k?.Oque ?? "Uma entrada de inicialização")} — mas o arquivo NÃO existe mais.",
                "Zero: é resíduo de um programa desinstalado.", "LIMPAR (reversível: o Factory guarda o valor pra RELIGAR).", "cortar");
        if (r.Estado == SecRow.Desligado || r.Estado == SecRow.Bloqueado)
            return new SecParecer(k?.Oque ?? "Algo que o Factory desligou/bloqueou.", "Já neutralizado.", $"Se quiser de volta: {r.Reverte}.", null);
        if (r.Estado == SecRow.Sistema)
            return new SecParecer("Item do próprio Windows, dentro de C:\\Windows (ou do Defender).", "Baixo.", "Não mexa; o Factory nem oferece cortar.", null);

        if (k is not null)
        {
            var risco = k.Risco;
            if (r.Risco && !risco.Contains("SEM assinatura", StringComparison.OrdinalIgnoreCase)) risco += " ⚠ ALÉM DISSO: " + fatos;
            return new SecParecer(k.Oque, risco, k.Sugestao, k.Acao);
        }
        if (r.Risco)
            return new SecParecer("Programa que o Factory não reconhece.", "SUSPEITO: " + fatos,
                "Não confie às cegas. DESLIGAR é reversível, então é seguro desligar enquanto você confere o arquivo (hash no VirusTotal) — ou use PERGUNTAR AO SQUAD.", "cortar");
        if (r.Kind == "porta")
            return new SecParecer("Programa escutando conexões vindas da rede.",
                "Depende da rede: em casa atrás do roteador o risco é baixo; em Wi-Fi público, qualquer um alcança. " + fatos,
                "Se você reconhece o programa → IGNORAR. Se não → BLOQUEAR (regra de entrada no firewall, reversível).", null);
        return new SecParecer("Algo que liga sozinho com o Windows e o Factory não conhece.", fatos,
            "Reconhece e usa → IGNORAR. Reconhece mas não precisa → DESLIGAR. Não reconhece → PERGUNTAR AO SQUAD.", null);
    }

    /// <summary>O que o Factory mediu (nao e opiniao): assinatura, pasta, arquivo.</summary>
    private static string Fatos(SecRow r)
    {
        var p = new List<string>();
        if (r.Detalhe.Contains("SEM assinatura", StringComparison.OrdinalIgnoreCase)) p.Add("sem assinatura digital");
        if (r.Detalhe.Contains("ADULTERADA", StringComparison.OrdinalIgnoreCase)) p.Add("assinatura ADULTERADA");
        if (r.Detalhe.Contains("pasta de risco", StringComparison.OrdinalIgnoreCase)) p.Add("roda de Temp/Public/Downloads");
        var assinado = AssinadoRx().Match(r.Detalhe);
        if (assinado.Success) p.Add("assinado por " + assinado.Groups[1].Value.Trim());
        if (!string.IsNullOrEmpty(r.Caminho)) p.Add("arquivo " + r.Caminho);
        return p.Count == 0 ? "sem dados extras." : string.Join("; ", p) + ".";
    }

    [GeneratedRegex(@"assinado: ([^·]+)")]
    private static partial Regex AssinadoRx();
}
