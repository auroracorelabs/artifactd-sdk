// Runs the language-independent tables in spec/conformance against this SDK.
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { ClientFeedbackClient } from '../src/index.js';

const table = name => JSON.parse(readFileSync(new URL(`../../../spec/conformance/${name}.json`, import.meta.url), 'utf8'));
const expand = v => typeof v === 'string' || v == null ? v : v.repeat ? v.repeat[0].repeat(v.repeat[1]) : v.concat.map(expand).join('');
const body = response => response.body_repeat ? response.body_repeat[0].repeat(response.body_repeat[1]) : response.body ?? '';
function setup(t, extra = {}) {
  const queueDirectory = mkdtempSync(join(tmpdir(), 'artifactd-conformance-'));
  t.after(() => rmSync(queueDirectory, { recursive: true, force: true }));
  const client = new ClientFeedbackClient({ baseURL: 'https://repo.example', username: 'pull', token: 'secret', packageName: 'my-app', version: '2.0.0', queueDirectory, architecture: 'amd64', format: 'deb', ...extra });
  const queued = () => readdirSync(queueDirectory).filter(n => n.endsWith('.pending.json')).map(n => JSON.parse(readFileSync(join(queueDirectory, n), 'utf8')));
  return { client, queued };
}
const answer = response => async () => {
  if (response === null) throw new TypeError('fetch failed');
  return new Response(body(response) || null, { status: response.status, headers: response.headers });
};

for (const c of table('upload-responses').cases) {
  test(`upload response: ${c.name}`, async t => {
    const { client, queued } = setup(t);
    client.reportFeatureRequest('Export CSV', 'Take my data');
    t.mock.method(globalThis, 'fetch', answer(c.response));
    const [result] = await client.flushPending();
    assert.equal(result.uploaded, c.expect.uploaded);
    assert.equal(result.statusCode, c.expect.status_code);
    assert.equal(result.error, c.expect.error);
    assert.equal(queued().length, c.expect.retained ? 1 : 0);
  });
}

if (['linux', 'win32', 'darwin'].includes(process.platform)) {
  for (const c of table('usage-responses').cases) {
    test(`usage response: ${c.name}`, async t => {
      const { client, queued } = setup(t);
      t.mock.method(globalThis, 'fetch', answer(c.response));
      assert.equal(await client.trackUsage('app_open'), c.expect.sent);
      assert.equal(queued().length, 0);
    });
  }
}

const validation = table('validation');
for (const c of validation.user_requests) {
  for (const method of ['reportFeatureRequest', 'reportBugfixRequest']) {
    test(`user request validation (${method}): ${c.name}`, t => {
      const { client, queued } = setup(t);
      const call = () => client[method](expand(c.title), expand(c.description));
      if (c.valid) { assert.ok(call()); assert.equal(queued().length, 1); }
      else { assert.throws(call); assert.equal(queued().length, 0); }
    });
  }
}
for (const c of validation.correlation_ids.valid) {
  test(`correlation id accepted: ${c.name}`, t => {
    const { client, queued } = setup(t);
    assert.ok(client.reportError('op', new Error('x'), expand(c.value)));
    assert.equal(queued()[0].correlation_id, expand(c.sent));
  });
}
for (const c of validation.correlation_ids.invalid) {
  test(`correlation id rejected: ${c.name}`, t => {
    const { client, queued } = setup(t);
    assert.throws(() => client.reportError('op', new Error('x'), expand(c.value)));
    assert.equal(queued().length, 0);
  });
}
for (const c of [...validation.ga4_client_ids.accepted.map(x => ({ ...x, ok: true })), ...validation.ga4_client_ids.omitted.map(x => ({ ...x, ok: false }))]) {
  test(`ga4 client id ${c.ok ? 'accepted' : 'omitted'}: ${c.name}`, t => {
    const value = expand(c.value);
    const { client, queued } = setup(t, { ga4ClientIdProvider: () => { if (c.throws) throw new Error('provider failed'); return value; } });
    client.reportFeatureRequest('Export', 'CSV');
    const [payload] = queued();
    if (c.ok) assert.equal(payload.ga4_client_id, value); else assert.ok(!('ga4_client_id' in payload));
  });
}
test('diagnostics are truncated, not rejected', t => {
  const { client, queued } = setup(t);
  const error = new Error('m'.repeat(5000));
  error.stack = 's'.repeat(20000);
  assert.ok(client.reportError('op', error));
  const [payload] = queued();
  assert.equal(payload.message.length, validation.diagnostics.message_limit);
  assert.equal(payload.stack_trace.length, validation.diagnostics.stack_trace_limit);
});
