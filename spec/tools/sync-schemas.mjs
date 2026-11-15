// Derives the JSON Schemas in spec/schemas from the published OpenAPI document (the source of truth).
//   node spec/tools/sync-schemas.mjs [path/to/openapi.json]   rewrite spec/schemas
//   node spec/tools/sync-schemas.mjs --check [path]            fail when spec/schemas is stale
// The OpenAPI path defaults to $ARTIFACTD_OPENAPI or ../../docs.artifactd.net/spec/openapi.json (sibling checkout).
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const schemasDir = join(here, '..', 'schemas');
const SCHEMAS = {
  'client-feedback-request': 'ClientFeedbackRequest',
  'client-feedback-accepted': 'ClientFeedbackAccepted',
  'usage-event-request': 'UsageEventRequest',
  'error': 'Error',
};

// OpenAPI 3.0 -> JSON Schema 2020-12: `nullable: true` becomes a `null` member of `type` (and of `enum`).
function convert(node) {
  if (Array.isArray(node)) return node.map(convert);
  if (node === null || typeof node !== 'object') return node;
  const out = {};
  for (const [key, value] of Object.entries(node)) {
    if (key === 'nullable') continue;
    out[key] = key === 'properties' ? Object.fromEntries(Object.entries(value).map(([k, v]) => [k, convert(v)])) : convert(value);
  }
  if (node.nullable === true && typeof out.type === 'string') {
    out.type = [out.type, 'null'];
    if (Array.isArray(out.enum)) out.enum = [...out.enum, null];
  }
  return out;
}

export function build(openapi) {
  const result = {};
  for (const [file, name] of Object.entries(SCHEMAS)) {
    const source = openapi.components?.schemas?.[name];
    if (!source) throw new Error(`components.schemas.${name} is missing from the OpenAPI document`);
    result[file] = {
      $schema: 'https://json-schema.org/draft/2020-12/schema',
      $id: `https://docs.artifactd.net/schemas/${file}.schema.json`,
      title: name,
      ...convert(source),
    };
  }
  return result;
}

const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) {
  const args = process.argv.slice(2);
  const check = args.includes('--check');
  const given = args.find(a => !a.startsWith('--')) ?? process.env.ARTIFACTD_OPENAPI;
  const source = given ? resolve(given) : resolve(here, '../../../docs.artifactd.net/spec/openapi.json');
  if (!existsSync(source)) {
    console.error(`OpenAPI document not found at ${source}; pass its path or set ARTIFACTD_OPENAPI.`);
    process.exit(check ? 0 : 1); // --check is skipped when the docs checkout is not available
  }
  const schemas = build(JSON.parse(readFileSync(source, 'utf8')));
  let stale = 0;
  for (const [file, schema] of Object.entries(schemas)) {
    const target = join(schemasDir, `${file}.schema.json`);
    const text = JSON.stringify(schema, null, 2) + '\n';
    if (check) {
      if (!existsSync(target) || readFileSync(target, 'utf8') !== text) { console.error(`stale: spec/schemas/${file}.schema.json`); stale++; }
    } else writeFileSync(target, text);
  }
  if (check && stale) { console.error('Run: node spec/tools/sync-schemas.mjs'); process.exit(1); }
  console.log(check ? 'spec/schemas match the OpenAPI document' : `wrote ${Object.keys(schemas).length} schemas from ${source}`);
}
