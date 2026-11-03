// Amostra de voz de cada membro da matilha: mesma receita da voz do Odin — voz em INGLES da biblioteca,
// readequada pro portugues pelo modelo da agente (eleven_flash_v2_5). Gera mp3 em <FACTORY_DATA>\deliverables\audio\matilha\.
// Gasta credito: so roda quando chamado. Uso: node tools\pack-samples.mjs
import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { factoryData } from './profile.mjs';
const DIR = join(factoryData, 'deliverables', 'audio', 'matilha');
const H = { 'xi-api-key': process.env.ELEVENLABS_API_KEY, 'Content-Type': 'application/json' };
const MEMBROS = [
  ['geri', 'ljo9gAlSqKOvF6D8sOsX', 'Eu sou Geri, o voraz. Dá a ordem, Pai de Todos, e eu devoro o trabalho antes de esfriar. Feito. Quatro, lado a lado.'],
  ['freki', '10NkTYmU7tSz3Kkl3Lex', 'Freki, o faminto! Se o caminho travou, eu acho outro. Música no ar, chefe. Canta comigo!'],
  ['huginn', 'alwWf0yOMMykcrIh8BlE', 'Sou Huginn, o pensamento. Voei o mundo inteiro e voltei. Achei três caminhos. O melhor tem fonte.'],
  ['muninn', 'HH3kybY6uEJ2ebSa9Vy3', 'Muninn. A memória. Terça passada: quatro de quatro. Guardei. E nunca esqueço uma falha.'],
];
await mkdir(DIR, { recursive: true });
for (const [nome, voz, texto] of MEMBROS) {
  const r = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${voz}?output_format=mp3_44100_128`, {
    method: 'POST', headers: H,
    body: JSON.stringify({ text: texto, model_id: 'eleven_flash_v2_5', language_code: 'pt', voice_settings: { stability: 0.45, similarity_boost: 0.8, speed: 0.95 } }),
  });
  if (!r.ok) { console.log(nome, 'ERRO', r.status, (await r.text()).slice(0, 200)); continue; }
  const arq = join(DIR, `${nome}.mp3`);
  await writeFile(arq, Buffer.from(await r.arrayBuffer()));
  console.log(nome, 'ok', arq);
}
