// Language-independent contract check: every fixture must satisfy its wire schema, every invalid mutation must be
// rejected, and spec/schemas must match the OpenAPI document when the docs checkout is available.
//   node spec/tools/check.ts
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';
import { spawnSync } from 'node:child_process';
import { validate, type Schema } from './schema-lite.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path: string) => JSON.parse(readFileSync(join(root, path), 'utf8'));
const schemas: Record<string, Schema> = { report: read('schemas/client-feedback-request.schema.json'), usage: read('schemas/usage-event-request.schema.json') };
const expand = (value: unknown) => value && typeof value === 'object' && 'repeat' in value ? (value as { repeat: [string, number] }).repeat[0].repeat((value as { repeat: [string, number] }).repeat[1]) : value;
let failures = 0;
const fail = (message: string) => { console.error('FAIL ' + message); failures++; };

function* files(dir: string): Generator<string> {
  // walks dir recursively, yielding .json paths relative to the spec root
  yield* walk(dir);
}
function* walk(dir: string): Generator<string> {
  for (const name of readdirSync(join(root, dir)).sort()) {
    const path = `${dir}/${name}`;
    if (statSync(join(root, path)).isDirectory()) yield* walk(path); else if (name.endsWith('.json')) yield path;
  }
}
for (const path of files('fixtures')) {
  const payload = read(path);
  const kind = /(^|\/)(usage\/|app-open)/.test(path) ? 'usage' : 'report';
  const errors = validate(schemas[kind], payload);
  if (errors.length) fail(`${path} violates the ${kind} schema:\n  ${errors.join('\n  ')}`);
}
const { cases } = read('conformance/invalid-payloads.json');
for (const item of cases) {
  const payload: Record<string, unknown> = { ...read(`fixtures/${item.base}`) };
  for (const [key, value] of Object.entries(item.set ?? {})) payload[key] = expand(value);
  for (const key of item.unset ?? []) delete payload[key];
  if (validate(schemas[item.schema], payload).length === 0) fail(`invalid payload accepted by the ${item.schema} schema: ${item.name}`);
}
const sync = spawnSync(process.execPath, [join(root, 'tools/sync-schemas.ts'), '--check'], { encoding: 'utf8' });
if (sync.status !== 0) fail(sync.stderr.trim());
if (failures) { console.error(`${failures} contract check(s) failed`); process.exit(1); }
console.log(`contract ok: ${[...files('fixtures')].length} fixtures valid, ${cases.length} invalid payloads rejected`);
