# Artifactd Client Feedback for Node.js

Turn bug reports and feature requests into your next release. Node.js 22+, ESM, built-in TypeScript declarations, no runtime dependencies. This SDK uses local files and targets Node.js rather than browsers.

Install a built tarball with `npm install ./artifactd-client-feedback-<version>.tgz` or from this checkout with `npm install ./packages/node`. After registry publication, use `npm install @artifactd/client-feedback`.

```js
import { ClientFeedbackClient } from '@artifactd/client-feedback';

const feedback = new ClientFeedbackClient({
  baseURL: process.env.ARTIFACTD_HOST,
  username: process.env.ARTIFACTD_PULL_USERNAME,
  token: process.env.ARTIFACTD_PULL_TOKEN,
  packageName: 'my-app', version: '2.0.0', queueDirectory: '.feedback',
  architecture: 'amd64', format: 'deb',
});
feedback.reportFeatureRequest('Export CSV', 'Let me take my data anywhere.');
feedback.reportBugfixRequest('Save fails', 'Saving an empty draft shows an error.');
for (const result of await feedback.flushPending()) {
  console.log(result.reportId, result.uploaded, result.statusCode);
}
```

`reportError(operation, error?)` and `reportCrash(error, source)` queue diagnostics. Queue operations are synchronous; network methods return promises. Call `flushPending()` at startup and after reporting; overlapping flush calls are serialized.

`resolveCredentials` may return credentials or a promise, or null to defer uploads. `ga4ClientIdProvider` supplies an optional consented ID; report attribution is captured at enqueue time. `trackUsage('app_open')`, `trackUsage('update_check')` and `trackUsage('device_connection_start', 'ssh')` return promises of delivery success, require HTTPS, Linux/Windows/macOS, architecture and format, and never persist events. A third argument, `trackUsage('app_open', null, 12500)`, adds the foreground time in milliseconds. No global exception handlers are installed.

One client owns a queue directory. Defaults: 20 pending reports, 15000 ms timeout. Identical pending reports return null instead of another ID. Keep the queue private and use HTTPS in production.

```sh
npm test --prefix packages/node
npm pack ./packages/node
```

See the shared contract and release instructions in the repository for protocol details and publishing setup.
