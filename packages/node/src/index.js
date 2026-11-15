import { randomUUID } from 'node:crypto';
import { mkdirSync, readdirSync, readFileSync, writeFileSync, renameSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { platform, release } from 'node:os';
import { AsyncLocalStorage } from 'node:async_hooks';

const kinds = new Set(['crash', 'error', 'bugfix_request', 'feature_request']);
function required(value, name) {
  if (typeof value !== 'string' || !value.trim()) throw new TypeError(`${name} is required`);
  return value.trim();
}
function credentials(value) {
  const url = new URL(value.baseURL);
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash)
    throw new TypeError('baseURL must be an absolute HTTP(S) URL without credentials, query or fragment');
  required(value.username, 'username'); required(value.token, 'token');
  if (value.username.includes(':')) throw new TypeError('username cannot contain a colon');
  return value;
}
function identity(provider) {
  try { const value = provider?.(); return typeof value === 'string' && value.length <= 128 && /^[0-9]+\.[0-9]+$/.test(value) ? value : null; }
  catch { return null; }
}
export const CORRELATION_HEADER = 'X-Correlation-Id';
const ambientCorrelation = new AsyncLocalStorage();
/** Trimmed id, or null when empty, longer than 128 characters or containing whitespace/control characters. */
export function normalizeCorrelationId(value) {
  if (typeof value !== 'string') return null;
  const id = value.trim();
  return id && id.length <= 128 && !/[\x00-\x20\x7f]/.test(id) ? id : null;
}
/** A new opaque id (32 lowercase hex characters) to mint once per request and forward to every tier. */
export function newCorrelationId() { return randomUUID().replaceAll('-', ''); }
export function currentCorrelationId() { return ambientCorrelation.getStore() ?? null; }
/** Runs fn with the id ambient: reports raised inside (including across awaits) carry it. */
export function withCorrelationId(correlationId, fn) {
  const id = normalizeCorrelationId(correlationId);
  if (!id) throw new TypeError('correlationId must be 1-128 characters without whitespace or control characters');
  return ambientCorrelation.run(id, fn);
}
export class ClientFeedbackClient {
  #options; #queue; #flush = Promise.resolve(); #session = Math.floor(Date.now() / 1000);
  #clientId = `${BigInt('0x' + randomUUID().replaceAll('-', '').slice(0, 15))}.${this.#session}`;
  constructor(options) {
    this.#options = { maxPending: 20, timeoutMs: 15000, ...options };
    for (const key of ['packageName', 'version', 'queueDirectory']) required(this.#options[key], key);
    if (!Number.isInteger(this.#options.maxPending) || this.#options.maxPending < 1 || !Number.isFinite(this.#options.timeoutMs) || this.#options.timeoutMs <= 0)
      throw new TypeError('maxPending and timeoutMs must be positive');
    if (!options.resolveCredentials) credentials(options);
    this.#queue = options.queueDirectory;
  }
  #pending() {
    let names;
    try { names = readdirSync(this.#queue); } catch (error) { if (error.code === 'ENOENT') return []; throw error; }
    return names.filter(name => /^[0-9a-f]{32}\.pending\.json$/.test(name)).sort().flatMap(name => {
      try {
        const payload = JSON.parse(readFileSync(join(this.#queue, name), 'utf8'));
        if (!kinds.has(payload.kind) || typeof payload.report_id !== 'string' || payload.report_id.replaceAll('-', '') !== name.slice(0, 32)) return [];
        return [{ name, payload }];
      } catch { return []; }
    });
  }
  #correlation(explicit) {
    if (explicit !== undefined && explicit !== null) {
      const id = normalizeCorrelationId(explicit);
      if (!id) throw new TypeError('correlationId must be 1-128 characters without whitespace or control characters');
      return id;
    }
    const ambient = ambientCorrelation.getStore();
    if (ambient) return ambient;
    try { return normalizeCorrelationId(this.#options.correlationIdProvider?.()); } catch { return null; }
  }
  #enqueue(kind, operation, description, error, source, correlationId) {
    const correlation = this.#correlation(correlationId);
    operation = required(operation, 'operation/title');
    if (kind.endsWith('_request')) {
      description = required(description, 'description');
      // The server counts UTF-16 code units of the trimmed value (C# String.Length), which is what String#length returns.
      if (operation.length > 128 || description.length > 1024) throw new RangeError('title/description exceed 128/1024 characters');
    }
    const payload = {
      report_id: randomUUID(), kind, package_name: this.#options.packageName.trim(), version: this.#options.version.trim(),
      architecture: this.#options.architecture ?? null, format: this.#options.format ?? null,
      runtime: `Node.js ${process.version}`, os: ({win32: 'windows', darwin: 'macos'})[platform()] ?? platform(), os_version: release(),
      occurred_at_utc: new Date().toISOString(), operation,
      exception_type: error?.name ?? null, message: description ?? error?.message?.slice(0, 1024) ?? null,
      stack_trace: error?.stack?.slice(0, 8000) ?? null, source,
    };
    const ga4ClientId = identity(this.#options.ga4ClientIdProvider);
    if (ga4ClientId) payload.ga4_client_id = ga4ClientId; // only present when the app supplied a consented identity
    if (correlation) payload.correlation_id = correlation; // only present when the app supplied one
    const body = JSON.stringify(payload);
    if (Buffer.byteLength(body) > 65536) throw new RangeError('report exceeds 65536 bytes');
    const rows = this.#pending();
    const keys = ['kind', 'operation', 'exception_type', 'message', 'stack_trace', 'correlation_id'];
    if (rows.some(({payload: p}) => keys.every(key => (p[key] ?? null) === (payload[key] ?? null)))) return null;
    mkdirSync(this.#queue, { recursive: true, mode: 0o700 });
    const target = join(this.#queue, payload.report_id.replaceAll('-', '') + '.pending.json');
    const temp = target + '.tmp';
    try { writeFileSync(temp, body, { mode: 0o600, flag: 'wx', flush: true }); renameSync(temp, target); }
    finally { try { unlinkSync(temp); } catch (e) { if (e.code !== 'ENOENT') throw e; } }
    rows.sort((a, b) => a.payload.occurred_at_utc.localeCompare(b.payload.occurred_at_utc));
    for (const {name} of rows.slice(0, Math.max(0, rows.length - this.#options.maxPending + 1))) this.#remove(name);
    return payload.report_id;
  }
  #remove(name) { try { unlinkSync(join(this.#queue, name)); } catch (e) { if (e.code !== 'ENOENT') throw e; } }
  reportFeatureRequest(title, description) { return this.#enqueue('feature_request', title, description, null, 'user'); }
  reportBugfixRequest(title, description) { return this.#enqueue('bugfix_request', title, description, null, 'user'); }
  reportError(operation, error, correlationId) { return this.#enqueue('error', operation, null, error, 'curated', correlationId); }
  reportCrash(error, source, correlationId) {
    if (!(error instanceof Error)) throw new TypeError('error must be an Error');
    return this.#enqueue('crash', 'crash', null, error, required(source, 'source'), correlationId);
  }
  async #post(path, payload, httpsOnly = false) {
    try {
      const auth = this.#options.resolveCredentials ? await this.#options.resolveCredentials() : this.#options;
      if (!auth) return { statusCode: null, error: null };
      credentials(auth);
      if (httpsOnly && new URL(auth.baseURL).protocol !== 'https:') return { statusCode: null, error: null };
      const headers = { 'Content-Type': 'application/json', Authorization: 'Basic ' + Buffer.from(`${auth.username}:${auth.token}`).toString('base64') };
      const body = JSON.stringify(payload);
      if (Buffer.byteLength(body) > 65536) return { statusCode: 413, error: null };
      const response = await fetch(auth.baseURL.replace(/\/+$/, '') + path, {
        method: 'POST', headers, body, redirect: 'manual', signal: AbortSignal.timeout(this.#options.timeoutMs),
      });
      let error = null;
      // Bound error bodies before parsing; never retain arbitrary server responses.
      if (!response.ok && response.body) {
        const reader = response.body.getReader(); const chunks = []; let size = 0;
        try {
          while (size <= 4096) { const item = await reader.read(); if (item.done) break; size += item.value.length; chunks.push(item.value); }
          if (size <= 4096) { try { const data = JSON.parse(Buffer.concat(chunks).toString()); if (typeof data.error === 'string') error = data.error; } catch {} }
        } finally { await reader.cancel(); }
      } else { await response.body?.cancel(); }
      return { statusCode: response.status, error };
    } catch { return { statusCode: null, error: null }; }
  }
  flushPending() {
    const operation = this.#flush.catch(() => {}).then(async () => {
      const results = [];
      for (const { name, payload } of this.#pending()) {
        const result = await this.#post('/v1/client-feedback', payload);
        const uploaded = result.statusCode >= 200 && result.statusCode < 300;
        if (uploaded) this.#remove(name);
        results.push({ reportId: payload.report_id, ...result, uploaded });
      }
      return results;
    });
    this.#flush = operation; return operation;
  }
  async trackUsage(eventName, protocol = null, engagementTimeMs = null) {
    if (!['app_open', 'update_check', 'device_connection_start'].includes(eventName)) throw new TypeError('unsupported eventName');
    if (eventName === 'device_connection_start' && !['ssh', 'sftp', 'ipmi', 'proxmox', 'winbox'].includes(protocol)) return false;
    const os = { win32: 'windows', darwin: 'macos' }[platform()] ?? platform();
    if (engagementTimeMs !== null && !(Number.isSafeInteger(engagementTimeMs) && engagementTimeMs >= 0)) throw new TypeError('engagementTimeMs must be a non-negative integer');
    if (!['windows', 'linux', 'macos'].includes(os) || !this.#options.architecture || !this.#options.format) return false;
    const clientId = identity(this.#options.ga4ClientIdProvider);
    const result = await this.#post('/v1/client-usage-event', {
      package_name: this.#options.packageName, event_name: eventName, protocol: eventName === 'device_connection_start' ? protocol : null,
      client_id: clientId ?? this.#clientId, session_id: this.#session, occurred_at_utc: new Date().toISOString(),
      ...(engagementTimeMs === null ? {} : { engagement_time_msec: engagementTimeMs }),
      version: this.#options.version.trim(),
      architecture: this.#options.architecture, format: this.#options.format, os,
    }, true);
    return result.statusCode >= 200 && result.statusCode < 300;
  }
}
