<?php
declare(strict_types=1);
require __DIR__ . '/../autoload.php';

use ArtifactD\ClientFeedback\{Correlation, ClientFeedbackClient, ClientFeedbackOptions, Credentials, CurlTransport, HttpResponse, Transport};

function check(bool $condition, string $message = 'Assertion failed'): void {
    if (!$condition) throw new RuntimeException($message);
}
function invalid(callable $operation): void {
    try { $operation(); } catch (InvalidArgumentException) { return; }
    throw new RuntimeException('Expected invalid argument.');
}
function removeTree(string $path): void {
    if (!is_dir($path)) return;
    foreach (scandir($path) as $file) {
        if ($file !== '.' && $file !== '..') {
            is_dir($path . '/' . $file) ? removeTree($path . '/' . $file) : unlink($path . '/' . $file);
        }
    }
    rmdir($path);
}
final class FakeTransport implements Transport {
    public array $requests = [];
    public ?HttpResponse $response;
    public bool $throws = false;
    public function __construct(?HttpResponse $response = new HttpResponse(201)) { $this->response = $response; }
    public function post(string $url, array $headers, string $body, float $timeout): ?HttpResponse {
        $this->requests[] = ['url' => $url, 'headers' => $headers, 'payload' => json_decode($body, true), 'timeout' => $timeout];
        if ($this->throws) throw new RuntimeException('offline');
        return $this->response;
    }
}
$temp = sys_get_temp_dir() . '/artifactd-php-' . bin2hex(random_bytes(8));
mkdir($temp, 0700);
$tests = [];
$tests['validation, Unicode, duplicate suppression and queue bounds'] = static function () use ($temp): void {
    $transport = new FakeTransport();
    $options = new ClientFeedbackOptions('my-app', '2.0.0', $temp . '/validation', new Credentials('https://repo.example', 'pull', 'secret'), maxPending: 1);
    $client = new ClientFeedbackClient($options, $transport);
    invalid(fn() => $client->reportFeatureRequest('', 'text'));
    invalid(fn() => $client->reportFeatureRequest('title', str_repeat('x', 1025)));
    invalid(fn() => $client->reportFeatureRequest(str_repeat('é', 129), 'text'));
    $id = $client->reportFeatureRequest(str_repeat('é', 128), str_repeat('😀', 512));
    check(is_string($id));
    check($client->reportFeatureRequest(str_repeat('é', 128), str_repeat('😀', 512)) === null);
    usleep(10000);
    $client->reportBugfixRequest('Save', 'Fails');
    $files = glob($temp . '/validation/*.pending.json');
    check(count($files) === 1);
    $text = file_get_contents($files[0]);
    check(!str_contains($text, 'secret'));
    check(json_decode($text, true)['kind'] === 'bugfix_request');
    if (PHP_OS_FAMILY !== 'Windows') check((fileperms($files[0]) & 0777) === 0600);
};
$tests['shared fixture, Basic authentication, captured identity and successful removal'] = static function () use ($temp): void {
    $queue = $temp . '/fixture'; mkdir($queue, 0700);
    $payload = json_decode(file_get_contents(__DIR__ . '/../../../spec/fixtures/feature-request.json'), true, 512, JSON_THROW_ON_ERROR);
    $file = $queue . '/' . str_replace('-', '', $payload['report_id']) . '.pending.json';
    file_put_contents($file, json_encode($payload));
    $transport = new FakeTransport();
    $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $queue, new Credentials('https://repo.example', 'pull', 'secret'), ga4ClientIdProvider: fn() => '999.999'), $transport);
    $result = $client->flushPending()[0];
    check($result->uploaded() && $result->statusCode === 201);
    check(!file_exists($file));
    $sent = $transport->requests[0];
    check($sent['url'] === 'https://repo.example/v1/client-feedback');
    check($sent['headers']['Authorization'] === 'Basic ' . base64_encode('pull:secret'));
    check(!isset($sent['headers']['X-GA4-Client-Id']), 'The analytics identity travels in the JSON body only.');
    check($sent['payload']['ga4_client_id'] === '123.456');
    check($sent['payload'] === $payload, 'Shared fixture must be sent without changing wire keys.');
};
$tests['retry IDs, rejection details, transport failures and deferred sign-in'] = static function () use ($temp): void {
    $queue = $temp . '/retry';
    $transport = new FakeTransport(new HttpResponse(403, '{"error":"access_denied"}'));
    $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $queue, new Credentials('https://repo.example', 'pull', 'secret')), $transport);
    $id = $client->reportError('save', new RuntimeException('failed'));
    $result = $client->flushPending()[0];
    check(!$result->uploaded() && $result->error === 'access_denied' && $result->reportId === $id);
    $transport->throws = true;
    check($client->flushPending()[0]->statusCode === null);
    $deferred = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $queue, resolveCredentials: fn() => null), $transport);
    check($deferred->flushPending()[0]->statusCode === null);
    $transport->throws = false; $transport->response = new HttpResponse(200);
    $restarted = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $queue, resolveCredentials: fn() => new Credentials('https://next.example', 'next', 'token')), $transport);
    check($restarted->flushPending()[0]->uploaded());
    check($transport->requests[2]['payload']['report_id'] === $id);
    check($transport->requests[2]['url'] === 'https://next.example/v1/client-feedback');
};
$tests['identity is captured before sign-in changes; invalid providers are ignored'] = static function () use ($temp): void {
    $identity = '123.456'; $transport = new FakeTransport();
    $provider = static function () use (&$identity): string { return $identity; };
    $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $temp . '/identity', new Credentials('https://repo.example', 'pull', 'secret'), ga4ClientIdProvider: $provider), $transport);
    $client->reportFeatureRequest('Export', 'CSV');
    $identity = '999.999';
    $client->flushPending();
    check($transport->requests[0]['payload']['ga4_client_id'] === '123.456');
    check(!isset($transport->requests[0]['headers']['X-GA4-Client-Id']));
    $invalid = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $temp . '/bad-identity', new Credentials('https://repo.example', 'pull', 'secret'), ga4ClientIdProvider: fn() => throw new RuntimeException('failed')), $transport);
    $invalid->reportBugfixRequest('Save', 'Fails'); $invalid->flushPending();
    check(!isset($transport->requests[1]['payload']['ga4_client_id']) || $transport->requests[1]['payload']['ga4_client_id'] === null);
};
$tests['corrupt queues and mismatched filenames are skipped'] = static function () use ($temp): void {
    $queue = $temp . '/corrupt'; mkdir($queue, 0700);
    file_put_contents($queue . '/' . str_repeat('a', 32) . '.pending.json', '{');
    file_put_contents($queue . '/' . str_repeat('b', 32) . '.pending.json', file_get_contents(__DIR__ . '/../../../spec/fixtures/feature-request.json'));
    $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $queue, new Credentials('https://repo.example', 'pull', 'secret')), new FakeTransport());
    $client->reportCrash(new RuntimeException('bad'), 'main');
    check(count($client->flushPending()) === 1);
    check(count(glob($queue . '/*.pending.json')) === 2);
};
$tests['usage allow-list, HTTPS and no persistent usage queue'] = static function () use ($temp): void {
    $transport = new FakeTransport(new HttpResponse(204));
    $options = new ClientFeedbackOptions('my-app', '2.0.0', $temp . '/usage', new Credentials('https://repo.example', 'pull', 'secret'), architecture: 'amd64', format: 'deb');
    $client = new ClientFeedbackClient($options, $transport);
    invalid(fn() => $client->trackUsage('anything'));
    check(!$client->trackUsage('device_connection_start', 'telnet'));
    if (in_array(PHP_OS_FAMILY, ['Linux', 'Windows', 'Darwin'], true)) {
        check($client->trackUsage('device_connection_start', 'ssh', 2345));
        check($transport->requests[0]['payload']['protocol'] === 'ssh');
        check($transport->requests[0]['payload']['package_name'] === 'my-app');
        check($transport->requests[0]['payload']['engagement_time_msec'] === 2345);
        check(str_ends_with($transport->requests[0]['payload']['occurred_at_utc'], 'Z'));
        check(!isset($transport->requests[0]['headers']['X-GA4-Client-Id']));
        invalid(fn() => $client->trackUsage('app_open', null, -1));
        check($client->trackUsage('app_open', 'ssh'));
        check($transport->requests[1]['payload']['protocol'] === null);
    }
    check(!is_dir($temp . '/usage'));
    $http = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $temp . '/http', new Credentials('http://repo.example', 'pull', 'secret'), architecture: 'amd64', format: 'deb'), $transport);
    $count = count($transport->requests);
    check(!$http->trackUsage('app_open') && count($transport->requests) === $count);
};
$tests['invalid configuration and diagnostic truncation'] = static function () use ($temp): void {
    foreach (['file:///tmp/no', 'https://user:pass@repo.example', 'https://repo.example?token=no', 'https://repo.example#fragment'] as $url) invalid(fn() => new Credentials($url, 'pull', 'secret'));
    invalid(fn() => new Credentials('https://repo.example', 'bad:name', 'secret'));
    invalid(fn() => new ClientFeedbackOptions('', '2.0.0', $temp));
    invalid(fn() => new ClientFeedbackOptions('app', '2.0.0', $temp, new Credentials('https://repo.example', 'pull', 'secret'), timeout: 0));
    $transport = new FakeTransport();
    $client = new ClientFeedbackClient(new ClientFeedbackOptions('app', '2.0.0', $temp . '/truncate', new Credentials('https://repo.example', 'pull', 'secret')), $transport);
    try {
        $client->reportError(str_repeat('x', 70000));
        throw new RuntimeException('Expected body size rejection.');
    } catch (LengthException) {}
    $client->reportError('save', new RuntimeException(str_repeat('é', 2000)));
    $client->flushPending();
    check(preg_match_all('/./us', $transport->requests[0]['payload']['message']) === 1024);
};
$tests['real cURL transport retains redirects and bounds rejection bodies'] = static function () use ($temp): void {
    $socket = stream_socket_server('tcp://127.0.0.1:0', $errno, $error);
    check($socket !== false, $error);
    $address = stream_socket_get_name($socket, false); fclose($socket);
    $capture = $temp . '/requests.jsonl'; putenv('ARTIFACTD_TEST_CAPTURE=' . $capture);
    $log = $temp . '/server.log';
    $process = proc_open([PHP_BINARY, '-S', $address, __DIR__ . '/router.php'], [['pipe','r'], ['file',$log,'a'], ['file',$log,'a']], $pipes);
    check(is_resource($process));
    try {
        $ready = false;
        for ($i = 0; $i < 100; $i++) {
            $connection = @stream_socket_client('tcp://' . $address, $errno, $error, 0.1);
            if ($connection) { fclose($connection); $ready = true; break; }
            usleep(10000);
        }
        check($ready, 'PHP test server did not start.');
        foreach (['created' => 201, 'duplicate' => 200, 'reject' => 403, 'redirect' => 302, 'large' => 403] as $mode => $status) {
            $queue = $temp . '/real-' . $mode;
            $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $queue, new Credentials('http://' . $address . '/' . $mode, 'pull', 'secret')));
            $client->reportFeatureRequest('Export', 'CSV'); $result = $client->flushPending()[0];
            check($result->statusCode === $status, $mode . ' status');
            check($result->uploaded() === ($status < 300), $mode . ' delivery');
            check(count(glob($queue . '/*.pending.json')) === ($status < 300 ? 0 : 1));
            if ($mode === 'reject') check($result->error === 'access_denied');
            if ($mode === 'large') check($result->error === null);
        }
        $requests = array_map(static fn($line) => json_decode($line, true), file($capture, FILE_IGNORE_NEW_LINES));
        check(count($requests) === 5, 'Redirects must not be followed.');
        check($requests[0]['headers']['authorization'] === 'Basic ' . base64_encode('pull:secret'));
        check($requests[0]['payload']['kind'] === 'feature_request');
        check((new CurlTransport())->post('http://127.0.0.1:1', [], '{}', 0.05) === null);
        $slow = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '2.0.0', $temp . '/slow', new Credentials('http://' . $address . '/slow', 'pull', 'secret'), timeout: 0.05));
        $slow->reportFeatureRequest('Export', 'CSV');
        check($slow->flushPending()[0]->statusCode === null, 'Timeout must return no HTTP status.');
        check(count(glob($temp . '/slow/*.pending.json')) === 1, 'Timeout must retain the report.');
    } finally {
        fclose($pipes[0]); proc_terminate($process); proc_close($process);
    }
};
$tests['shared contract fixtures match the wire contract and survive the queue unchanged'] = static function () use ($temp): void {
    $fixtures = __DIR__ . '/../../../spec/fixtures/';
    $load = static fn(string $path): array => json_decode(file_get_contents($fixtures . $path), true, 512, JSON_THROW_ON_ERROR);
    $reportKeys = ['report_id', 'kind', 'package_name', 'version', 'architecture', 'format', 'runtime', 'os', 'os_version',
        'occurred_at_utc', 'operation', 'exception_type', 'message', 'stack_trace', 'source'];
    foreach (['reports/crash.json', 'reports/error.json', 'reports/bugfix-request.json', 'reports/feature-request.json', 'google/crash-with-ga4.json'] as $path) {
        $payload = $load($path);
        $keys = str_starts_with($path, 'google/') ? [...$reportKeys, 'ga4_client_id'] : $reportKeys;
        check(array_keys($payload) === $keys, $path . ' keys');
        check(in_array($payload['kind'], ['crash', 'error', 'bugfix_request', 'feature_request'], true), $path . ' kind');
        if (str_ends_with($payload['kind'], '_request')) {
            check($payload['source'] === 'user', $path . ' source');
            check(preg_match_all('/./us', $payload['operation']) >= 1 && preg_match_all('/./us', $payload['operation']) <= 128, $path . ' title');
            check(preg_match_all('/./us', $payload['message']) >= 1 && preg_match_all('/./us', $payload['message']) <= 1024, $path . ' description');
        }
        $queue = $temp . '/contract-' . md5($path); mkdir($queue, 0700);
        file_put_contents($queue . '/' . str_replace('-', '', $payload['report_id']) . '.pending.json', json_encode($payload));
        $transport = new FakeTransport();
        $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '1.0.0', $queue, new Credentials('https://repo.example', 'pull', 'secret')), $transport);
        check($client->flushPending()[0]->uploaded(), $path . ' delivery');
        check($transport->requests[0]['payload'] === $payload, $path . ' must be sent unchanged');
        check(!isset($transport->requests[0]['headers']['X-GA4-Client-Id']), $path . ' no analytics header');
    }
    $plain = array_keys($load('usage/app-open.json'));
    check(array_keys($load('usage/update-check.json')) === $plain && array_keys($load('google/app-open-with-ga4.json')) === $plain);
    $engaged = array_keys($load('usage/app-open-with-engagement.json'));
    check($engaged === array_keys($load('google/app-open-with-ga4-and-engagement.json')));
    check(array_values(array_diff($engaged, $plain)) === ['engagement_time_msec'] && !in_array('ga4_client_id', $plain, true));
    // A generated report carries the fixture keys and omits the identity unless one is supplied.
    $transport = new FakeTransport();
    $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '1.0.0', $temp . '/generated', new Credentials('https://repo.example', 'pull', 'secret'), architecture: 'amd64', format: 'deb', ga4ClientIdProvider: fn() => '123.456'), $transport);
    $client->reportFeatureRequest('Export CSV', 'Take my data');
    $client->flushPending();
    check(array_keys($transport->requests[0]['payload']) === [...$reportKeys, 'ga4_client_id']);
    $plainTransport = new FakeTransport();
    $plainClient = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '1.0.0', $temp . '/generated-plain', new Credentials('https://repo.example', 'pull', 'secret')), $plainTransport);
    $plainClient->reportFeatureRequest('Export CSV', 'Take my data');
    $plainClient->flushPending();
    check(array_keys($plainTransport->requests[0]['payload']) === $reportKeys);
    // Usage payloads match the usage fixtures.
    $usage = new FakeTransport(new HttpResponse(204));
    $usageClient = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '1.0.0', $temp . '/generated-usage', new Credentials('https://repo.example', 'pull', 'secret'), architecture: 'amd64', format: 'deb'), $usage);
    if (in_array(PHP_OS_FAMILY, ['Linux', 'Windows', 'Darwin'], true)) {
        check($usageClient->trackUsage('app_open') && $usageClient->trackUsage('app_open', null, 45000));
        check(array_keys($usage->requests[0]['payload']) === $plain);
        check(array_keys($usage->requests[1]['payload']) === $engaged);
    }
};
$tests['correlation id: explicit, ambient, provider fallback, dedup and validation'] = static function () use ($temp): void {
    $transport = new FakeTransport();
    $client = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '1.0.0', $temp . '/corr', new Credentials('https://repo.example', 'pull', 'secret')), $transport);
    $error = new RuntimeException('x');
    check($client->reportError('a', $error, 'req-1') !== null);
    check($client->reportError('a', $error, 'req-1') === null);
    check($client->reportError('a', $error, 'req-9') !== null);
    Correlation::run('req-2', static function () use ($client, $error): void {
        check(Correlation::current() === 'req-2');
        $client->reportError('b', $error);
    });
    check(Correlation::current() === null);
    check($client->reportError('c', $error) !== null);
    invalid(static fn () => $client->reportError('d', $error, 'has space'));
    invalid(static fn () => Correlation::set(''));
    check(preg_match('/^[0-9a-f]{32}$/', Correlation::newId()) === 1);
    $client->flushPending();
    $byOp = [];
    foreach ($transport->requests as $request) { $byOp[$request['payload']['operation'] . ($request['payload']['correlation_id'] ?? '-')] = $request['payload']; }
    $keys = array_keys($byOp); sort($keys);
    check($keys === ['areq-1', 'areq-9', 'breq-2', 'c-'], implode(',', $keys));
    check(!array_key_exists('correlation_id', $byOp['c-']));
    foreach ([[fn () => 'from-provider', 'from-provider'], [fn () => 'bad id', null], [static function () { throw new RuntimeException('boom'); }, null]] as $i => [$provider, $expected]) {
        $t = new FakeTransport();
        $c = new ClientFeedbackClient(new ClientFeedbackOptions('my-app', '1.0.0', $temp . "/corr-p$i", new Credentials('https://repo.example', 'pull', 'secret'), correlationIdProvider: $provider), $t);
        $c->reportError('p');
        $c->flushPending();
        check(($t->requests[0]['payload']['correlation_id'] ?? null) === $expected);
    }
};
try {
    foreach ($tests as $name => $test) { $test(); echo "PASS $name\n"; }
    echo count($tests) . " PHP SDK tests passed.\n";
} finally {
    removeTree($temp);
}
