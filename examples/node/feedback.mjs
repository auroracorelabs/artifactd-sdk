import { ClientFeedbackClient } from '@artifactd/client-feedback';

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Set ${name} before running this example`);
  return value;
}

const feedback = new ClientFeedbackClient({
  baseURL: required('ARTIFACTD_HOST'),
  username: required('ARTIFACTD_PULL_USERNAME'),
  token: required('ARTIFACTD_PULL_TOKEN'),
  packageName: required('ARTIFACTD_PACKAGE_NAME'),
  version: required('ARTIFACTD_PACKAGE_VERSION'),
  queueDirectory: process.env.ARTIFACTD_QUEUE_DIRECTORY ?? '.artifactd-feedback/node',
});
feedback.reportFeatureRequest('Export CSV', 'Let me take my data anywhere.');
for (const result of await feedback.flushPending()) {
  console.log(result.reportId, result.uploaded, result.statusCode);
}
