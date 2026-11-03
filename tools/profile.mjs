// A porta unica pro PERFIL e pros caminhos do usuario nos scripts Node (espelho de src/Factory/Services/OwnerProfile.cs).
// O repo e o produto; a vida do usuario mora no perfil, fora do git. Sem perfil, tudo segue generico.
//   FACTORY_ROOT   raiz dos projetos (o que o catalogo le)          padrao: ~/Projects
//   FACTORY_DATA   dados do usuario fora do repo (notas, recibos)   padrao: <FACTORY_ROOT>/.factory
//   FACTORY_PROFILE perfil (memoria, familia, vocabulario pessoal)   padrao: <FACTORY_DATA>/profile
import { existsSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';

/** Raiz do repo (pasta acima de tools/). */
export const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
export const factoryRoot = process.env.FACTORY_ROOT || join(homedir(), 'Projects');
export const factoryData = process.env.FACTORY_DATA || join(factoryRoot, '.factory');
/** Cache local do app (regenera; mesmo nome que o C# usa). */
export const localData = join(process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local'), 'Factory');
export const raiz = process.env.FACTORY_PROFILE || process.env.FACTORY_PERFIL || join(factoryData, 'profile');
export const existe = existsSync(raiz);
const legado = join(localData, 'chat');

/** Pasta das conversas/memoria do Odin: perfil\chat quando ja migrado, senao o lugar antigo. */
export const chatDir = () => (existsSync(join(raiz, 'chat')) ? join(raiz, 'chat') : legado);

export const arquivo = (slot, nome) => join(raiz, slot, nome);

/** Le um arquivo do perfil; '' se nao existe. */
export const ler = (slot, nome) => { try { return readFileSync(arquivo(slot, nome), 'utf-8'); } catch { return ''; } };
