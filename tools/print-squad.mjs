// Print de teste do squad (FROTA) fora do app: node tools/print-squad.mjs  (precisa de: python -m http.server 8765 na raiz)
import { createRequire } from 'node:module';
import path from 'node:path';
import os from 'node:os';
const require = createRequire(import.meta.url);
const { chromium } = require(path.join(process.env.APPDATA, 'npm/node_modules/@playwright/cli/node_modules/playwright'));
const b = await chromium.launch({ channel: 'chrome', headless: true, args: ['--use-angle=d3d11', '--ignore-gpu-blocklist'] });
const p = await b.newPage({ viewport: { width: 1000, height: 300 } });
p.on('pageerror', e => console.log('[erro]', e.message));
await p.goto('http://127.0.0.1:8765/src/Factory/Assets/orb/squad.html');
await p.waitForTimeout(1500);
await p.evaluate(() => window.squadTeste({ agentes: ['demo-odin-runas: saga viking', 'demo-odin-lobos: saga viking'], vigia: 'ligado · nada critico', vigiaAtivo: true }));
await p.waitForTimeout(2500);
await p.screenshot({ path: process.argv[2] || path.join(os.tmpdir(), 'squad.png') });
await b.close();
console.log('ok');
