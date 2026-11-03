// Reescreve a secao "## PLACAR" de docs\plano-rumo.md com numeros REAIS:
// - banco de provas: ultimo placar de cada agente em <FACTORY_DATA>\odin\evals\*.json
// - maos no mundo real: % confirmado por mao no hand-result/v1 do mes (ultima linha por request_id)
// Roda sozinho no fim de tools\odin-evals.mjs; tambem a mao: node tools\direction-score.mjs
import { readFileSync, writeFileSync, readdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { repoRoot, factoryData } from './profile.mjs';
const RUMO = join(repoRoot, 'docs', 'plano-rumo.md');
const EVALS = join(factoryData, 'odin', 'evals');
const HANDS = join(factoryData, 'odin', 'hands');

const ultimo = {};
for (const f of existsSync(EVALS) ? readdirSync(EVALS).filter((n) => /^\d.*-(teste|lab|prod)\.json$/.test(n)).sort() : []) {
  try { const j = JSON.parse(readFileSync(join(EVALS, f), 'utf-8')); ultimo[j.perfil] = { placar: j.placar, quando: f.slice(0, 15) }; } catch {}
}
const mes = new Date().toISOString().slice(0, 7);
const por = {};
if (existsSync(join(HANDS, `${mes}.jsonl`))) {
  const ult = new Map();
  for (const l of readFileSync(join(HANDS, `${mes}.jsonl`), 'utf-8').split('\n')) { try { const j = JSON.parse(l); ult.set(j.request_id, j); } catch {} }
  for (const j of ult.values()) {
    if (['estado', 'lembrar', 'reportar_falha', 'sessoes', 'perguntar_claude', 'roteiro', 'acordado'].includes(j.mao)) continue;   // sem objetivo verificavel
    const p = (por[j.mao] ??= { n: 0, ok: 0 }); p.n++; if (j.veredito === 'confirmado') p.ok++;
  }
}
const maos = Object.entries(por).filter(([, p]) => p.n >= 5).sort((a, b) => b[1].n - a[1].n)
  .map(([m, p]) => `- ${m}: ${Math.round((100 * p.ok) / p.n)}% confirmado (${p.ok} de ${p.n})`);
const bloco = [
  '## PLACAR do Odin',
  `Atualizado ${new Date().toLocaleString('pt-BR')}. Meta: banco ≥ 90% em todas as agentes e mãos ≥ 90% confirmadas. (Gerado sozinho pelos testes; não editar.)`,
  '',
  'Banco de provas (o Odin escolhe a mão e os parâmetros certos?):',
  ...['prod', 'lab', 'teste'].map((k) => `- ${k.toUpperCase()}: ${ultimo[k] ? `${ultimo[k].placar} (${ultimo[k].quando})` : 'sem rodada ainda'}`),
  '',
  `Mãos no mundo real em ${mes} (a mão provou que fez?):`,
  ...(maos.length ? maos : ['- sem execuções suficientes']),
  '',
].join('\n');
if (!existsSync(RUMO)) { console.log('sem docs/plano-rumo.md: placar nao gravado'); process.exit(0); }
const txt = readFileSync(RUMO, 'utf-8').replace(/\r/g, '');
const ini = txt.indexOf('## PLACAR');
if (ini < 0) throw new Error('secao ## PLACAR nao achada em plano-rumo.md');
const fim = txt.indexOf('\n## ', ini + 5);
writeFileSync(RUMO, txt.slice(0, ini) + bloco + (fim < 0 ? '' : txt.slice(fim + 1)), 'utf-8');
console.log('placar atualizado em', RUMO);
