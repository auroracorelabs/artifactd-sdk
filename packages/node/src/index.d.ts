export interface Credentials { baseURL: string; username: string; token: string; }
export interface ClientFeedbackOptions {
  packageName: string;
  version: string;
  queueDirectory: string;
  baseURL?: string;
  username?: string;
  token?: string;
  resolveCredentials?: () => Credentials | null | Promise<Credentials | null>;
  ga4ClientIdProvider?: () => string | null;
  /** Fallback id when no explicit id or withCorrelationId scope is active. Errors and invalid values are ignored. */
  correlationIdProvider?: () => string | null;
  architecture?: string;
  format?: string;
  maxPending?: number;
  timeoutMs?: number;
}
export interface UploadResult { reportId: string; statusCode: number | null; error: string | null; uploaded: boolean; }
export const CORRELATION_HEADER: 'X-Correlation-Id';
export function newCorrelationId(): string;
export function normalizeCorrelationId(value: unknown): string | null;
export function currentCorrelationId(): string | null;
export function withCorrelationId<T>(correlationId: string, fn: () => T): T;
export class ClientFeedbackClient {
  constructor(options: ClientFeedbackOptions);
  reportFeatureRequest(title: string, description: string): string | null;
  reportBugfixRequest(title: string, description: string): string | null;
  reportError(operation: string, error?: Error, correlationId?: string): string | null;
  reportCrash(error: Error, source: string, correlationId?: string): string | null;
  flushPending(): Promise<UploadResult[]>;
  trackUsage(eventName: 'app_open' | 'update_check' | 'device_connection_start', protocol?: string | null, engagementTimeMs?: number | null): Promise<boolean>;
}
