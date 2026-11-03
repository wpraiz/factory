// Memoria de cada membro da matilha: regenera <FACTORY_DATA>\odin\matilha\<membro>-memoria.md a partir do
// hand-result/v1 (o que cada um fez de verdade, com veredito). So le os recibos; dado do usuario fica fora do repo.
// Uso: node tools\pack-memory.mjs   (tambem roda no fim do odin-evals)
import { readFileSync, writeFileSync, readdirSync, existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { factoryData } from './profile.mjs';
const HANDS = join(factoryData, 'odin', 'hands');
const DIR = join(factoryData, 'odin', 'matilha');
const DONO = {
  geri: ['desenhar', 'janela', 'fazer', 'sessoes', 'terminais', 'digitar_terminal', 'focar_terminal', 'fechar_terminal'],
  freki: ['musica', 'abrir_app', 'obs', 'abrir_arquivo', 'abrir_pasta', 'abrir_projeto'],
  huginn: ['pesquisar', 'perguntar_claude', 'ver_tela'],
  muninn: ['lembrar', 'seguranca', 'reportar_falha', 'criar_nota', 'gravar_status', 'alarme'],
};
const membroDe = Object.fromEntries(Object.entries(DONO).flatMap(([m, ms]) => ms.map((x) => [x, m])));
mkdirSync(DIR, { recursive: true });
const ult = new Map();
for (const f of existsSync(HANDS) ? readdirSync(HANDS).filter((n) => n.endsWith('.jsonl')).sort() : [])
  for (const l of readFileSync(join(HANDS, f), 'utf-8').split('\n')) { try { const j = JSON.parse(l); ult.set(j.request_id, j); } catch {} }
const por = { geri: [], freki: [], huginn: [], muninn: [] };
for (const j of ult.values()) { const m = membroDe[j.mao]; if (m) por[m].push(j); }
const quando = (s) => new Date(s).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
for (const [m, lista] of Object.entries(por)) {
  lista.sort((a, b) => String(b.iniciado_em).localeCompare(String(a.iniciado_em)));
  const ok = lista.filter((j) => j.veredito === 'confirmado').length, falhou = lista.filter((j) => j.veredito === 'falhou').length;
  const linhas = lista.slice(0, 40).map((j) => {
    const v = j.veredito === 'confirmado' ? 'FEITO' : j.veredito === 'falhou' ? 'FALHOU' : j.estado_execucao === 'em_execucao' ? 'INTERROMPIDO' : 'SEM PROVA';
    return `- ${quando(j.iniciado_em)} · ${j.mao} · ${v} · ${String(j.resultado_alegado ?? j.veredito_motivo ?? '').replace(/\s+/g, ' ').slice(0, 140)}`;
  });
  writeFileSync(join(DIR, `${m}-memoria.md`), [
    `# Memória de ${m.toUpperCase()} (gerada de odin\\hands — não editar à mão)`,
    `Total: ${lista.length} trabalhos · ${ok} feitos com prova · ${falhou} falhas. Mais recente primeiro.`, '', ...linhas, '',
  ].join('\n'), 'utf-8');
  console.log(`${m}: ${lista.length} trabalhos, ${ok} feitos, ${falhou} falhas`);
}
