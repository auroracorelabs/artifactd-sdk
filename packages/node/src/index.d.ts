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
  architecture?: string;
  format?: string;
  maxPending?: number;
  timeoutMs?: number;
}
export interface UploadResult { reportId: string; statusCode: number | null; error: string | null; uploaded: boolean; }
export class ClientFeedbackClient {
  constructor(options: ClientFeedbackOptions);
  reportFeatureRequest(title: string, description: string): string | null;
  reportBugfixRequest(title: string, description: string): string | null;
  reportError(operation: string, error?: Error): string | null;
  reportCrash(error: Error, source: string): string | null;
  flushPending(): Promise<UploadResult[]>;
  trackUsage(eventName: 'app_open' | 'update_check' | 'device_connection_start', protocol?: string | null, engagementTimeMs?: number | null): Promise<boolean>;
}
