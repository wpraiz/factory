// Ponte da orb online com o Factory ("online identica a offline").
// Serve a orb (esta pasta) e:
//   GET  /api/sessao  -> { signedUrl, vars }   (URL assinada da agente de TESTE + catalogo/memoria/familia do Factory)
//   POST /api/hands/<nome>  -> { result }       (repassa pro Factory em 127.0.0.1:8788 com o token local)
// A chave da ElevenLabs e o token das maos ficam AQUI; o navegador so ve a URL assinada.
// Rodar: node web-orb/server.mjs  (porta 8787). Expor: npx localtunnel --port 8787 --subdomain <seu-subdominio>
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { join, extname } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';

const HERE = fileURLToPath(new URL('.', import.meta.url));
const PORT = Number(process.env.ORB_PORT || 8787);
const KEY = process.env.ELEVENLABS_API_KEY;
if (!KEY) throw new Error('ELEVENLABS_API_KEY nao definida');
const HANDS = 'http://127.0.0.1:8788';
// Cache local do app (mesmo nome que o C# usa): %LOCALAPPDATA%\Factory
const LOCAL = join(process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local'), 'Factory');
const TOKEN = (await readFile(join(LOCAL, 'hands-token.txt'), 'utf-8')).trim();
const ORBDIR = join(HERE, '..', 'src', 'Factory', 'Assets', 'orb');
// Usa a agente LAB se existir; ORB_AGENT=teste forca a de teste.
const AGENT_FILE = process.env.ORB_AGENT === 'teste' ? 'agent-test.json' : (await stat(join(ORBDIR, 'agent-lab.json')).then(() => 'agent-lab.json', () => 'agent-test.json'));
const AGENT = JSON.parse(await readFile(join(ORBDIR, AGENT_FILE), 'utf-8')).agent_id;
if (!AGENT || AGENT.endsWith('REPLACE_ME')) throw new Error(`${AGENT_FILE} sem agent_id: rode node tools/create-orb-agent.mjs --teste`);
const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.json': 'application/json' };
// Senha simples da pagina (passe ?k=... no link). Sem ela, 403: e um tunel na internet.
const PASS = process.env.ORB_PASS || TOKEN.slice(0, 8);

const json = (res, code, obj) => { res.writeHead(code, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }); res.end(JSON.stringify(obj)); };

createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://x');
    const ok = url.searchParams.get('k') === PASS || req.headers.cookie?.includes(`orb=${PASS}`) || req.headers['x-orb-pass'] === PASS;
    if (!ok) { res.writeHead(403, { 'content-type': 'text/plain' }); return res.end('sem senha'); }
    if (url.pathname === '/api/sessao') {
      const r = await fetch(`https://api.elevenlabs.io/v1/convai/conversation/get-signed-url?agent_id=${AGENT}`, { headers: { 'xi-api-key': KEY } });
      const { signed_url } = await r.json();
      let vars = {};
      try { vars = await (await fetch(`${HANDS}/vars`, { headers: { 'X-Odin-Token': TOKEN } })).json(); } catch { vars = { catalogo: '(Factory fechado)', memoria: '', ultima: '', familia: '', hoje: '' }; }
      return json(res, 200, { signedUrl: signed_url, vars });
    }
    if (url.pathname.startsWith('/api/hands/') && req.method === 'POST') {
      const name = url.pathname.slice('/api/hands/'.length);
      let body = ''; for await (const c of req) body += c;
      const r = await fetch(`${HANDS}/hands/${encodeURIComponent(name)}`, { method: 'POST', headers: { 'X-Odin-Token': TOKEN, 'content-type': 'application/json' }, body: body || '{}' });
      return json(res, 200, await r.json());
    }
    // estatico
    let p = url.pathname === '/' ? '/index.html' : url.pathname;
    if (p.includes('..')) { res.writeHead(400); return res.end(); }
    const file = join(HERE, p);
    try { await stat(file); } catch { res.writeHead(404); return res.end('nao achei'); }
    const data = await readFile(file);
    res.writeHead(200, { 'content-type': MIME[extname(file)] || 'application/octet-stream', 'cache-control': 'no-store', 'set-cookie': `orb=${PASS}; Path=/; SameSite=Lax` });
    res.end(data);
  } catch (e) { json(res, 500, { error: String(e.message || e) }); }
}).listen(PORT, '0.0.0.0', () => console.log(`orb online em http://localhost:${PORT}/?k=${PASS}  (agente ${AGENT}, maos em ${HANDS})`));
