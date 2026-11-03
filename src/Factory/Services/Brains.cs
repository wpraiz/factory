namespace Factory.Services;

/// <summary>
/// Cerebros que a ElevenLabs Agents aceita em `agent.prompt.llm` (the list the API itself returns when given an
/// invalid name) + what was measured in the bake-off (tools/bakeoff-llm.mjs).
/// Ordem: novos primeiro dentro de cada casa. O ComboBox e editavel: nome fora da lista tambem vai.
/// </summary>
public static class Brains
{
    public static readonly string[] All =
    [
        // OpenAI
        "gpt-5.6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-6-luna", "gpt-6-sol", "gpt-6-astra",
        "gpt-5.5", "gpt-5.4-mini", "gpt-5.4-nano", "gpt-5.4", "gpt-5.2-chat-latest", "gpt-5.2", "gpt-5.1", "gpt-5", "gpt-5-mini", "gpt-5-nano",
        "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano", "gpt-4o", "gpt-4o-mini",
        // Anthropic
        "claude-sonnet-5", "claude-opus-5-5", "claude-opus-5", "claude-fable-5", "claude-haiku-4-5", "claude-sonnet-4-6", "claude-sonnet-4-5", "claude-opus-4-8", "claude-opus-4-7",
        // Google
        "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.6-flash", "gemini-3.5-flash", "gemini-3.5-flash-lite", "gemini-3.1-pro-preview", "gemini-3.1-flash-lite",
        "gemini-3-pro-preview", "gemini-3-flash-preview", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.0-flash", "gemini-2.0-flash-lite",
        // hospedados pela ElevenLabs (latencia minima)
        "eleven_realtime_v1_max", "eleven_realtime_v1_mini", "qwen36-35b-a3b", "qwen35-397b-a17b", "qwen35-35b-a3b", "qwen3-30b-a3b", "qwen3-4b",
        "deepseek-v41-flash", "deepseek-v4-flash", "glm-52", "glm-45-air-fp8", "gpt-oss-120b", "gpt-oss-20b",
        // outros
        "grok-beta", "custom-llm",
    ];

    /// <summary>TTFB p50 medido na simulacao (mesmo prompt) e preco/min estimado pela ElevenLabs. Vazio = nao medido.</summary>
    public static readonly Dictionary<string, string> Measured = new()
    {
        ["gpt-5.6-luna"] = "1,6-2,1 s · US$0,02/min · honesto so com a regra no prompt",
        ["gpt-5.6-sol"] = "0,9-2,0 s (picos 5 s) · US$0,31/min · falhou fato e honesto na 2a rodada",
        ["gpt-6-luna"] = "2,1-2,9 s · US$0,01/min · prometeu vigiar (mentiu); errou fato 1x",
        ["gpt-5.5"] = "~8 s/turno (reasoning) · US$0,37/min",
        ["gpt-5.4"] = "~8 s/turno (reasoning) · US$0,18/min",
        ["gpt-5.4-mini"] = "1,2-1,9 s (max 2,5) · US$0,06/min · honesto sem regra; tudo ok",
        ["gpt-5.4-nano"] = "1,7 s · US$0,02/min · esqueceu `estado`",
        ["gpt-4.1"] = "1,5 s · US$0,15/min · errou fato e curto (vetado)",
        ["claude-sonnet-5"] = "1,1 s (max 3,0) · US$0,17/min · falhou fato e honesto na 2a rodada",
        ["claude-opus-5-5"] = "2,9 s simulado · 5,7 s AO VIVO · US$0,29/min",
        ["claude-haiku-4-5"] = "0,46 s · US$0,08/min · falou comprido",
        ["gemini-3.8-flash"] = "2,2 s · US$0,06/min · errou o fato",
        ["gemini-3.7-flash"] = "3,6 s · US$0,06/min · tudo ok",
        ["gemini-3.5-flash"] = "0,7 s · US$0,12/min · errou o fato",
        ["gemini-3.5-flash-lite"] = "1,2 s · US$0,02/min · falou comprido, nao foi honesto",
        ["gemini-2.5-flash"] = "0,6 s AO VIVO · US$0,01/min · sendo aposentado pelo Google",
        ["gemini-2.5-flash-lite"] = "0,4 s · US$0,01/min",
        ["qwen3-30b-a3b"] = "0,44 s · US$0,02/min · errou o fato",
    };

    public static string Info(string? llm) => llm is not null && Measured.TryGetValue(llm, out var s) ? s : "nao medido · docs\\bakeoff-llm-2026-09-26.md";
}
