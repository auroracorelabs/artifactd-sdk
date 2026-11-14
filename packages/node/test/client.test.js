import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { ClientFeedbackClient } from '../src/index.js';
const fixture = JSON.parse(readFileSync(new URL('../../../spec/fixtures/feature-request.json', import.meta.url)));
function setup(t, extra = {}) {
  const queueDirectory = mkdtempSync(join(tmpdir(), 'artifactd-test-'));
  t.after(() => rmSync(queueDirectory, { recursive: true, force: true }));
  const options = { baseURL: 'https://repo.example', username: 'pull', token: 'secret', packageName: 'my-app', version: '2.0.0', queueDirectory, architecture: 'amd64', format: 'deb', ...extra };
  return { client: new ClientFeedbackClient(options), options, queueDirectory };
}
test('validates, deduplicates and bounds pending requests', t => {
  const { client, options, queueDirectory } = setup(t, { maxPending: 1 });
  assert.throws(() => client.reportFeatureRequest('', 'text'));
  assert.throws(() => client.reportFeatureRequest('a', 'b'.repeat(1025)));
  assert.ok(client.reportFeatureRequest('Export CSV', 'Take my data'));
  assert.equal(client.reportFeatureRequest('Export CSV', 'Take my data'), null);
  client.reportBugfixRequest('Save', 'Fails');
  const rows = readdirSync(queueDirectory).filter(n => n.endsWith('.json'));
  assert.equal(rows.length, 1);
  const text = readFileSync(join(queueDirectory, rows[0]), 'utf8');
  assert.equal(JSON.parse(text).kind, 'bugfix_request');
  assert.ok(!text.includes(options.token));
});
test('shared fixture survives restart, authenticates, preserves attribution and removes on 2xx', async t => {
  const { options, queueDirectory } = setup(t);
  const name = fixture.report_id.replaceAll('-', '') + '.pending.json';
  writeFileSync(join(queueDirectory, name), JSON.stringify(fixture));
  const client = new ClientFeedbackClient({ ...options, ga4ClientIdProvider: () => '999.999' });
  let seen;
  t.mock.method(globalThis, 'fetch', async (url, request) => { seen = {url, request}; return new Response('', {status: 201}); });
  const [result] = await client.flushPending();
  assert.equal(result.uploaded, true);
  assert.equal(readdirSync(queueDirectory).length, 0);
  assert.equal(seen.url, 'https://repo.example/v1/client-error-report');
  assert.equal(seen.request.redirect, 'manual');
  assert.equal(seen.request.headers.Authorization, 'Basic ' + Buffer.from('pull:secret').toString('base64'));
  // The analytics identity travels in the JSON body only, never as a header.
  assert.equal(Object.keys(seen.request.headers).some(name => name.toLowerCase().includes('ga4')), false);
  assert.deepEqual(JSON.parse(seen.request.body), fixture);
});
test('rejections and transport failures remain pending with stable IDs', async t => {
  const { client, queueDirectory } = setup(t);
  const id = client.reportError('save', new Error('failed'));
  const mock = t.mock.method(globalThis, 'fetch', async () => new Response('{"error":"access_denied"}', {status: 403}));
  let [result] = await client.flushPending();
  assert.equal(result.reportId, id); assert.equal(result.statusCode, 403); assert.equal(result.error, 'access_denied'); assert.equal(result.uploaded, false);
  mock.mock.mockImplementation(async () => { throw new Error('offline'); });
  [result] = await client.flushPending();
  assert.equal(result.statusCode, null); assert.equal(result.uploaded, false);
  assert.equal(readdirSync(queueDirectory).length, 1);
});
test('deferred credentials, invalid identity and overlapping flushes', async t => {
  const {client, options, queueDirectory} = setup(t, { resolveCredentials: () => null });
  client.reportFeatureRequest('Export', 'CSV');
  t.mock.method(globalThis, 'fetch', async () => new Response('', {status: 201}));
  assert.equal((await client.flushPending())[0].statusCode, null);
  writeFileSync(join(queueDirectory, 'corrupt.pending.json'), 'bad');
  const restarted = new ClientFeedbackClient({...options, resolveCredentials: async () => ({baseURL: 'https://next.example', username: 'new', token: 'new-token'})});
  const results = await Promise.all([restarted.flushPending(), restarted.flushPending()]);
  assert.equal(results.flat().length, 1);
});
test('usage validates events and requires HTTPS, architecture and format', async t => {
  const { client, options, queueDirectory } = setup(t, { ga4ClientIdProvider: () => '123.456' });
  let seen;
  t.mock.method(globalThis, 'fetch', async (url, request) => { seen = {url, request}; return new Response('', {status: 200}); });
  assert.equal(await new ClientFeedbackClient({...options, baseURL: 'http://repo.example'}).trackUsage('app_open'), false);
  assert.equal(seen, undefined);
  assert.equal(await client.trackUsage('device_connection_start', 'telnet'), false);
  await assert.rejects(client.trackUsage('anything'));
  if (['linux', 'win32', 'darwin'].includes(process.platform)) {
    assert.equal(await client.trackUsage('device_connection_start', 'ssh', 2345), true);
    assert.equal(seen.url, 'https://repo.example/v1/client-usage-event');
    const payload = JSON.parse(seen.request.body);
    assert.equal(payload.client_id, '123.456'); assert.equal(payload.protocol, 'ssh'); assert.equal(typeof payload.session_id, 'number');
    assert.equal(payload.package_name, options.packageName); assert.equal(payload.engagement_time_msec, 2345);
    assert.equal(new Date(payload.occurred_at_utc).toISOString(), payload.occurred_at_utc);
    assert.equal(Object.keys(seen.request.headers).some(name => name.toLowerCase().includes('ga4')), false);
    await assert.rejects(client.trackUsage('app_open', null, -1));
  }
  assert.equal(readdirSync(queueDirectory).length, 0);
});
test('rejects unsafe base URLs and oversized reports', t => {
  const { options, client } = setup(t);
  for (const baseURL of ['file:///tmp/test', 'https://user:pass@repo.example', 'https://repo.example?token=no']) assert.throws(() => new ClientFeedbackClient({...options, baseURL}));
  assert.throws(() => client.reportError('x'.repeat(65536)));
});

// Contract fixtures shared by every SDK (spec/fixtures).
const fixtures = new URL('../../../spec/fixtures/', import.meta.url);
const load = path => JSON.parse(readFileSync(new URL(path, fixtures), 'utf8'));
const REPORT_KEYS = ['report_id', 'kind', 'package_name', 'version', 'architecture', 'format', 'runtime', 'os', 'os_version',
  'occurred_at_utc', 'operation', 'exception_type', 'message', 'stack_trace', 'source'];
const reportFixtures = ['reports/crash.json', 'reports/error.json', 'reports/bugfix-request.json', 'reports/feature-request.json', 'google/crash-with-ga4.json'];

for (const path of reportFixtures) {
  test(`report fixture ${path} matches the wire contract and survives the queue unchanged`, async t => {
    const payload = load(path);
    const google = path.startsWith('google/');
    assert.deepEqual(Object.keys(payload), google ? [...REPORT_KEYS, 'ga4_client_id'] : REPORT_KEYS);
    assert.match(payload.report_id, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    assert.ok(['crash', 'error', 'bugfix_request', 'feature_request'].includes(payload.kind));
    assert.match(payload.occurred_at_utc, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/);
    if (payload.kind.endsWith('_request')) {
      assert.equal(payload.source, 'user');
      assert.ok(payload.operation.length >= 1 && payload.operation.length <= 128);
      assert.ok(payload.message.length >= 1 && payload.message.length <= 1024);
    }
    const { options, queueDirectory } = setup(t);
    writeFileSync(join(queueDirectory, payload.report_id.replaceAll('-', '') + '.pending.json'), JSON.stringify(payload));
    let seen;
    t.mock.method(globalThis, 'fetch', async (url, request) => { seen = request; return new Response('', { status: 201 }); });
    const [result] = await new ClientFeedbackClient(options).flushPending();
    assert.equal(result.uploaded, true);
    assert.deepEqual(JSON.parse(seen.body), payload);
  });
}

test('usage fixtures list exactly the keys the SDK sends and carry no report fields', async t => {
  const expected = Object.keys(load('usage/app-open.json'));
  assert.deepEqual(Object.keys(load('usage/update-check.json')), expected);
  assert.deepEqual(Object.keys(load('google/app-open-with-ga4.json')), expected);
  const withEngagement = Object.keys(load('usage/app-open-with-engagement.json'));
  assert.deepEqual(withEngagement, Object.keys(load('google/app-open-with-ga4-and-engagement.json')));
  assert.deepEqual(withEngagement.filter(key => !expected.includes(key)), ['engagement_time_msec']);
  assert.ok(!expected.includes('ga4_client_id'));
  if (!['linux', 'win32', 'darwin'].includes(process.platform)) return;
  const { client } = setup(t);
  const bodies = [];
  t.mock.method(globalThis, 'fetch', async (url, request) => { bodies.push(JSON.parse(request.body)); return new Response('', { status: 204 }); });
  await client.trackUsage('app_open');
  await client.trackUsage('app_open', null, 45000);
  assert.deepEqual(Object.keys(bodies[0]), expected);
  assert.deepEqual(Object.keys(bodies[1]), withEngagement);
});

test('a generated report has the fixture keys and omits the analytics identity unless one is supplied', t => {
  const { client, queueDirectory } = setup(t);
  client.reportFeatureRequest('Export CSV', 'Take my data');
  const [file] = readdirSync(queueDirectory);
  assert.deepEqual(Object.keys(JSON.parse(readFileSync(join(queueDirectory, file), 'utf8'))), REPORT_KEYS);
  const consented = setup(t, { ga4ClientIdProvider: () => '123.456' });
  consented.client.reportFeatureRequest('Export CSV', 'Take my data');
  const [saved] = readdirSync(consented.queueDirectory);
  assert.deepEqual(Object.keys(JSON.parse(readFileSync(join(consented.queueDirectory, saved), 'utf8'))), [...REPORT_KEYS, 'ga4_client_id']);
});
