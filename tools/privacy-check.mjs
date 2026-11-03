// Trava de privacidade do Factory. Le profile.manifest.json (regras genericas, no repo) + a extensao local do perfil
// (perfil.local.json, fora do git, so na maquina do dono). Bloqueia: arquivo em `nunca_no_git`, nome da tabela privada,
// segredo. Avisa: narrativa pessoal (regras `aviso`) — aviso so passa com PRIVACIDADE_ACEITO=1 depois de revisar.
// Uso:
//   node tools/privacy-check.mjs              stage (pre-commit)
//   node tools/privacy-check.mjs --tudo       arvore inteira
//   node tools/privacy-check.mjs --historico  todo o historico
//   node tools/privacy-check.mjs --base <ref> o que o branch/PR acrescenta desde <ref> (CI)
//   --ci : aviso nao falha (vira anotacao do GitHub); bloqueio falha
import { execFileSync } from 'node:child_process';
import { readFileSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const lerJson = (f) => { try { return JSON.parse(readFileSync(f, 'utf-8')); } catch { return null; } };
const repo = lerJson(join(ROOT, 'profile.manifest.json')) ?? {};
const raiz = process.env[repo.raiz_env ?? 'FACTORY_PROFILE'] || process.env.FACTORY_PERFIL || repo.raiz || '';
const local = (raiz && lerJson(join(raiz, repo.extensao_local ?? 'perfil.local.json'))) ?? {};
const m = {
  nunca: [...(repo.nunca_no_git ?? []), ...(local.nunca_no_git ?? [])],
  bloqueia: [...(repo.conteudo?.bloqueia ?? []), ...(local.conteudo?.bloqueia ?? [])],
  aviso: [...(repo.conteudo?.aviso ?? []), ...(local.conteudo?.aviso ?? [])],
};
const tabela = local.conteudo?.tabela_nomes && raiz ? join(raiz, local.conteudo.tabela_nomes) : '';
const nomes = tabela && existsSync(tabela)
  ? readFileSync(tabela, 'utf-8').split(/\r?\n/).map((l) => l.split('==>')[0].trim()).filter((l) => l && !l.startsWith('#'))
  : [];

// glob simples: ** = qualquer coisa (inclusive /), * = qualquer coisa sem /
const globRe = (g) => new RegExp('^' + g.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*\*\//g, '(?:.*/)?').replace(/\*\*/g, '.*').replace(/\*/g, '[^/]*') + '$', 'i');
const proibidos = m.nunca.map((n) => ({ ...n, re: globRe(n.glob) }));
const reBloq = m.bloqueia.map((b) => ({ ...b, re: new RegExp(b.regex.replace(/^\(\?i\)/, ''), b.regex.startsWith('(?i)') ? 'i' : '') }));
const reAviso = m.aviso.map((a) => ({ ...a, re: new RegExp(a.regex.replace(/^\(\?i\)/, ''), a.regex.startsWith('(?i)') ? 'i' : '') }));

const args = process.argv.slice(2);
const ci = args.includes('--ci');
const base = args.includes('--base') ? args[args.indexOf('--base') + 1] : null;
const modo = base ? 'base' : args.includes('--historico') ? 'historico' : args.includes('--tudo') ? 'tudo' : 'stage';
const git = (...a) => execFileSync('git', a, { cwd: ROOT, encoding: 'utf-8', maxBuffer: 1 << 30 });

// arquivos em jogo + texto acrescentado (diff) ou inteiro (tudo)
let arquivos = [], texto = '';
if (modo === 'stage') { arquivos = git('diff', '--cached', '--name-only', '--diff-filter=ACMR').split('\n'); texto = git('diff', '--cached', '-U0', '--no-color'); }
else if (modo === 'base') { arquivos = git('diff', '--name-only', '--diff-filter=ACMR', `${base}...HEAD`).split('\n'); texto = git('diff', '-U0', '--no-color', `${base}...HEAD`); }
else if (modo === 'tudo') { arquivos = git('ls-files').split('\n'); texto = git('grep', '-I', '-n', '-e', '.'); }
else { arquivos = git('log', '--all', '--name-only', '--format=').split('\n'); texto = git('log', '--all', '-p', '--no-color'); }
arquivos = [...new Set(arquivos.map((a) => a.trim()).filter(Boolean))];
// nao conferir o proprio manifesto/trava (eles DESCREVEM as regras)
const ignorar = /^(perfil\.manifest\.json|tools\/privacy-check\.mjs)$/;
let arquivoAtual = '';
const linhas = texto.split('\n').filter((l) => {
  const cab = l.match(/^\+\+\+ b\/(.*)$/) ?? l.match(/^([^:\s][^:]*):\d+:/);
  if (cab) arquivoAtual = cab[1];
  if (ignorar.test(arquivoAtual)) return false;
  return modo === 'tudo' || (l.startsWith('+') && !l.startsWith('+++'));
});

const bloqueios = [], avisos = [];
for (const a of arquivos) if (!ignorar.test(a)) for (const p of proibidos) if (p.re.test(a)) bloqueios.push(`arquivo proibido (${p.classe}: ${p.porque}): ${a}`);
for (const bruta of linhas) {
  // no modo --tudo a linha vem como "arquivo:N:conteudo": so o conteudo conta (o nome do arquivo nao e dado)
  const l = modo === 'tudo' ? bruta.replace(/^[^:]+:\d+:/, '') : bruta;
  const baixa = l.toLowerCase();
  for (const n of nomes) if (baixa.includes(n.toLowerCase())) bloqueios.push(`nome privado: ${l.slice(0, 120)}`);
  for (const b of reBloq) if (b.re.test(l)) bloqueios.push(`${b.nome}: ${l.slice(0, 70)}…`);
  for (const a of reAviso) if (a.re.test(l)) avisos.push(`${a.nome}: ${l.slice(0, 120)}`);
}

const aceito = process.env.PRIVACIDADE_ACEITO === '1';
if (!local.schema) console.error(`(perfil local nao encontrado em ${raiz || '?'}: so as regras genericas do repo valeram)`);
for (const a of avisos.slice(0, 15)) console.error((ci ? '::warning::' : 'AVISO ') + a);
for (const b of bloqueios.slice(0, 20)) console.error((ci ? '::error::' : 'BLOQUEADO ') + b);
if (bloqueios.length) { console.error(`PRIVACIDADE: ${bloqueios.length} bloqueio(s). Mova o dado pro perfil (${raiz}) ou troque por algo generico.`); process.exit(1); }
if (avisos.length && !ci && !aceito) { console.error(`PRIVACIDADE: ${avisos.length} aviso(s) de narrativa pessoal. Revise; se for tecnico, rode de novo com PRIVACIDADE_ACEITO=1.`); process.exit(1); }
console.log(`privacidade ok (${modo}): ${arquivos.length} arquivos, 0 bloqueios, ${avisos.length} avisos${avisos.length ? ' (aceitos)' : ''}`);
