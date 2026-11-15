<?php
declare(strict_types=1);
// Runs the language-independent tables in spec/conformance against this SDK.
require __DIR__ . '/../autoload.php';

use ArtifactD\ClientFeedback\{ClientFeedbackClient, ClientFeedbackOptions, Credentials, HttpResponse, Transport};

function check(bool $condition, string $message): void { if (!$condition) throw new RuntimeException($message); }
function table(string $name): array {
    return json_decode(file_get_contents(__DIR__ . "/../../../spec/conformance/$name.json"), true, 512, JSON_THROW_ON_ERROR);
}
function expand(mixed $value): mixed {
    if (!is_array($value)) return $value;
    return isset($value['repeat']) ? str_repeat($value['repeat'][0], $value['repeat'][1]) : implode('', array_map('expand', $value['concat']));
}
function removeTree(string $path): void {
    if (!is_dir($path)) return;
    foreach (scandir($path) as $file) if ($file !== '.' && $file !== '..') is_dir("$path/$file") ? removeTree("$path/$file") : unlink("$path/$file");
    rmdir($path);
}
final class AnswerTransport implements Transport {
    public function __construct(public ?array $response) {}
    public function post(string $url, array $headers, string $body, float $timeout): ?HttpResponse {
        if ($this->response === null) throw new RuntimeException('offline');
        $text = isset($this->response['body_repeat']) ? str_repeat($this->response['body_repeat'][0], $this->response['body_repeat'][1]) : ($this->response['body'] ?? '');
        return new HttpResponse($this->response['status'], $text);
    }
}
$root = sys_get_temp_dir() . '/artifactd-php-conformance-' . bin2hex(random_bytes(8));
mkdir($root, 0700);
$counter = 0;
$make = static function (?array $response = ['status' => 201], array $extra = []) use ($root, &$counter): array {
    $queue = $root . '/q' . $counter++;
    $options = new ClientFeedbackOptions(...['packageName' => 'my-app', 'version' => '2.0.0', 'queueDirectory' => $queue,
        'credentials' => new Credentials('https://repo.example', 'pull', 'secret'), 'architecture' => 'amd64', 'format' => 'deb', ...$extra]);
    return [new ClientFeedbackClient($options, new AnswerTransport($response)), static fn(): array => array_map(
        static fn($f) => json_decode(file_get_contents($f), true), glob($queue . '/*.pending.json') ?: [])];
};
$passed = 0;
$run = static function (string $name, callable $test) use (&$passed): void { try { $test(); $passed++; } catch (Throwable $e) { echo "FAIL $name: {$e->getMessage()}\n"; exit(1); } };

foreach (table('upload-responses')['cases'] as $case) {
    $run("upload response: {$case['name']}", static function () use ($case, $make): void {
        [$client, $queued] = $make($case['response']);
        $client->reportFeatureRequest('Export CSV', 'Take my data');
        $result = $client->flushPending()[0];
        $e = $case['expect'];
        check($result->uploaded() === $e['uploaded'], 'uploaded');
        check($result->statusCode === $e['status_code'], 'status code ' . var_export($result->statusCode, true));
        check($result->error === $e['error'], 'error ' . var_export($result->error, true));
        check(count($queued()) === ($e['retained'] ? 1 : 0), 'queue');
    });
}
if (in_array(PHP_OS_FAMILY, ['Linux', 'Windows', 'Darwin'], true)) {
    foreach (table('usage-responses')['cases'] as $case) {
        $run("usage response: {$case['name']}", static function () use ($case, $make): void {
            [$client, $queued] = $make($case['response']);
            check($client->trackUsage('app_open') === $case['expect']['sent'], 'sent');
            check($queued() === [], 'usage must not be queued');
        });
    }
}
$validation = table('validation');
foreach ($validation['user_requests'] as $case) {
    foreach (['reportFeatureRequest', 'reportBugfixRequest'] as $method) {
        $run("user request validation ($method): {$case['name']}", static function () use ($case, $method, $make): void {
            [$client, $queued] = $make();
            $call = static fn() => $client->$method(expand($case['title']), expand($case['description']));
            if ($case['valid']) { check($call() !== null, 'accepted'); check(count($queued()) === 1, 'queued'); return; }
            try { $call(); } catch (InvalidArgumentException) { check($queued() === [], 'nothing queued'); return; }
            throw new RuntimeException('expected an InvalidArgumentException');
        });
    }
}
foreach ($validation['correlation_ids']['valid'] as $case) {
    $run("correlation id accepted: {$case['name']}", static function () use ($case, $make): void {
        [$client, $queued] = $make();
        check($client->reportError('op', new RuntimeException('x'), expand($case['value'])) !== null, 'accepted');
        check($queued()[0]['correlation_id'] === expand($case['sent']), 'sent value');
    });
}
foreach ($validation['correlation_ids']['invalid'] as $case) {
    $run("correlation id rejected: {$case['name']}", static function () use ($case, $make): void {
        [$client, $queued] = $make();
        try { $client->reportError('op', new RuntimeException('x'), expand($case['value'])); } catch (InvalidArgumentException) { check($queued() === [], 'nothing queued'); return; }
        throw new RuntimeException('expected an InvalidArgumentException');
    });
}
foreach ([true => $validation['ga4_client_ids']['accepted'], false => $validation['ga4_client_ids']['omitted']] as $accepted => $cases) {
    foreach ($cases as $case) {
        $run('ga4 client id ' . ($accepted ? 'accepted' : 'omitted') . ": {$case['name']}", static function () use ($case, $accepted, $make): void {
            $value = expand($case['value'] ?? null);
            [$client, $queued] = $make(['status' => 201], ['ga4ClientIdProvider' => static function () use ($case, $value) {
                if ($case['throws'] ?? false) throw new RuntimeException('provider failed');
                return $value;
            }]);
            $client->reportFeatureRequest('Export', 'CSV');
            $payload = $queued()[0];
            if ($accepted) check(($payload['ga4_client_id'] ?? null) === $value, 'identity sent');
            else check(!array_key_exists('ga4_client_id', $payload) || $payload['ga4_client_id'] === null, 'identity omitted');
        });
    }
}
$run('diagnostics are truncated, not rejected', static function () use ($validation, $make): void {
    [$client, $queued] = $make();
    check($client->reportError('op', new RuntimeException(str_repeat('m', 5000))) !== null, 'accepted');
    $payload = $queued()[0];
    check(mb_strlen($payload['message']) === $validation['diagnostics']['message_limit'], 'message length');
    check(mb_strlen($payload['stack_trace']) <= $validation['diagnostics']['stack_trace_limit'], 'stack length');
});
removeTree($root);
echo "$passed PHP conformance cases passed.\n";
