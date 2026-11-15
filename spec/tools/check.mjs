// Language-independent contract check: every fixture must satisfy its wire schema, every invalid mutation must be
// rejected, and spec/schemas must match the OpenAPI document when the docs checkout is available.
//   node spec/tools/check.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative } from 'node:path';
import { spawnSync } from 'node:child_process';
import { validate } from './schema-lite.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = path => JSON.parse(readFileSync(join(root, path), 'utf8'));
const schemas = { report: read('schemas/client-feedback-request.schema.json'), usage: read('schemas/usage-event-request.schema.json') };
const expand = value => value && typeof value === 'object' && 'repeat' in value ? value.repeat[0].repeat(value.repeat[1]) : value;
let failures = 0;
const fail = message => { console.error('FAIL ' + message); failures++; };

function* files(dir) {
  for (const name of readdirSync(join(root, dir)).sort()) {
    const path = `${dir}/${name}`;
    if (statSync(join(root, path)).isDirectory()) yield* files(path); else if (name.endsWith('.json')) yield path;
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
  const payload = { ...read(`fixtures/${item.base}`) };
  for (const [key, value] of Object.entries(item.set ?? {})) payload[key] = expand(value);
  for (const key of item.unset ?? []) delete payload[key];
  if (validate(schemas[item.schema], payload).length === 0) fail(`invalid payload accepted by the ${item.schema} schema: ${item.name}`);
}
const sync = spawnSync(process.execPath, [join(root, 'tools/sync-schemas.mjs'), '--check'], { encoding: 'utf8' });
if (sync.status !== 0) fail(sync.stderr.trim());
if (failures) { console.error(`${failures} contract check(s) failed`); process.exit(1); }
console.log(`contract ok: ${[...files('fixtures')].length} fixtures valid, ${cases.length} invalid payloads rejected`);
