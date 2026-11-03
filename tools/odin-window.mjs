import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { repoRoot } from './profile.mjs';
const ORB = join(repoRoot, 'src', 'Factory', 'Assets', 'orb');
const H = { 'xi-api-key': process.env.ELEVENLABS_API_KEY };
// node tools/odin-window.mjs [horas=3] -> <tmp>/odin-window.txt (todas as agentes, fala + maos)
const DESDE = Date.now() / 1000 - Number(process.argv[2] || 3) * 3600;
const SAIDA = process.env.ODIN_JANELA_OUT || join(tmpdir(), 'odin-window.txt');
const perfis = { prod: 'agent.json', teste: 'agent-test.json', lab: 'agent-lab.json' };
let out = '';
const hora = (s) => new Date(s * 1000).toLocaleString('pt-BR');
for (const [perfil, file] of Object.entries(perfis)) {
  let id; try { id = JSON.parse(await readFile(join(ORB, file), 'utf-8')).agent_id; } catch { continue; }
  if (!id || id.endsWith('REPLACE_ME')) continue;
  const lst = await (await fetch(`https://api.elevenlabs.io/v1/convai/conversations?agent_id=${id}&page_size=30`, { headers: H })).json();
  for (const c of (lst.conversations || []).filter(c => c.start_time_unix_secs >= DESDE).reverse()) {
    const d = await (await fetch(`https://api.elevenlabs.io/v1/convai/conversations/${c.conversation_id}`, { headers: H })).json();
    out += `\n\n===== ${perfil} ${c.conversation_id} inicio ${hora(c.start_time_unix_secs)} dur ${c.call_duration_secs}s status ${c.status}\n`;
    for (const m of d.transcript || []) {
      const t = m.time_in_call_secs ?? 0;
      if (m.tool_calls?.length) for (const tc of m.tool_calls) out += `[${t}s] TOOL ${tc.tool_name} ${String(tc.params_as_json || '').slice(0, 400)}\n`;
      if (m.tool_results?.length) for (const tr of m.tool_results) out += `[${t}s] RES ${tr.tool_name}: ${String(tr.result_value || '').slice(0, 400)}\n`;
      const msg = (m.message || '').trim();
      if (msg && msg !== '...' && msg !== '.') out += `[${t}s] ${m.role === 'user' ? 'USUARIO' : 'ODIN'}: ${msg}\n`;
    }
  }
}
await writeFile(SAIDA, out, 'utf-8');
console.log('ok', out.length, 'chars ->', SAIDA);
