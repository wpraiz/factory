// Confere que toda mao do switch do OdinHands tem entrada (com tags) em src\Factory\Assets\hands.json e
// vice-versa. Mao sem tag de seguranca nao entra. Uso: node tools\check-hands.mjs (exit 0/1).
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const raiz = join(dirname(fileURLToPath(import.meta.url)), '..');
const cs = readFileSync(join(raiz, 'src', 'Factory', 'Services', 'OdinHands.cs'), 'utf-8');
const manifesto = JSON.parse(readFileSync(join(raiz, 'src', 'Factory', 'Assets', 'hands.json'), 'utf-8'));

// So o switch das maos (Executar): do "name switch" ate o caso padrao "Nao tenho a mao". Antes lia o arquivo
// inteiro e pegava "x" => de outro switch (apelidos de janela) como mao — falso positivo "calculadora".
const ini = cs.indexOf('name switch');
const fim = cs.indexOf('Nao tenho a mao', ini);
if (ini < 0 || fim < 0) { console.log('ERRO  nao achei o switch das maos no OdinHands.cs (mudou o formato?)'); process.exit(1); }
const noSwitch = new Set([...cs.slice(ini, fim).matchAll(/^\s*"([a-z_]+)"\s*=>/gm)].map((m) => m[1]));
const noManifesto = new Map(manifesto.maos.map((m) => [m.nome, m.tags]));
const validas = new Set(['leitura', 'escrita', 'executa', 'mouse', 'rede', 'gasta']);

const erros = [];
// Regra dos 3 lugares (CLAUDE.md): alem do switch, a mao precisa estar no NOMES do factory-orb.js (senao o app
// nunca recebe a chamada) e declarada no create-orb-agent.mjs (senao o agente nao sabe que ela existe). Ja aconteceu
// de uma mao passar "ok" sem estar no NOMES porque o checador nao olhava o JS.
const js = readFileSync(join(raiz, 'src', 'Factory', 'Assets', 'orb', 'factory-orb.js'), 'utf-8');
const bloco = js.match(/const NOMES = \[([\s\S]*?)\];/);
if (!bloco) erros.push('nao achei `const NOMES = [...]` no factory-orb.js (mudou o formato?)');
const noJs = new Set(bloco ? [...bloco[1].matchAll(/'([a-z_]+)'/g)].map((m) => m[1]) : []);
// Maos registradas a parte no cliente (ex.: `clientTools.desligar = ...`, que corta a sessao localmente).
for (const m of js.matchAll(/clientTools\.([a-z_]+)\s*=/g)) noJs.add(m[1]);
const agente = readFileSync(join(raiz, 'tools', 'create-orb-agent.mjs'), 'utf-8');
const noAgente = new Set([...agente.matchAll(/\btool\('([a-z_]+)'/g)].map((m) => m[1]));
for (const n of noSwitch) {
  if (bloco && !noJs.has(n)) erros.push(`mao "${n}" existe no OdinHands mas nao no NOMES do factory-orb.js`);
  if (!noAgente.has(n)) erros.push(`mao "${n}" existe no OdinHands mas nao e declarada no create-orb-agent.mjs`);
}
for (const n of noJs) if (!noSwitch.has(n)) erros.push(`"${n}" no NOMES do factory-orb.js nao existe no OdinHands`);
for (const n of noAgente) if (!noSwitch.has(n)) erros.push(`"${n}" declarada no create-orb-agent.mjs nao existe no OdinHands`);
for (const n of noSwitch) if (!noManifesto.has(n)) erros.push(`mao "${n}" existe no OdinHands mas nao no hands.json`);
for (const [n, tags] of noManifesto) {
  if (!noSwitch.has(n)) erros.push(`mao "${n}" no hands.json nao existe no OdinHands`);
  if (!Array.isArray(tags) || tags.length === 0) erros.push(`mao "${n}" sem tag de seguranca`);
  for (const t of tags || []) if (!validas.has(t)) erros.push(`mao "${n}": tag "${t}" fora de ${[...validas].join('|')}`);
}

if (erros.length) { for (const e of erros) console.log('ERRO  ' + e); process.exit(1); }
console.log(`ok: ${noSwitch.size} maos, todas com tag; OdinHands, hands.json, NOMES do factory-orb.js e criar-agente batem`);
