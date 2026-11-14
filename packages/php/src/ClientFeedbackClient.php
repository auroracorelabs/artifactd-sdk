<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/** Synchronous example client. One instance exclusively owns each queue directory. */
final class ClientFeedbackClient
{
    private Transport $transport;
    private int $session;
    private string $clientId;
    private bool $flushing = false;

    public function __construct(private readonly ClientFeedbackOptions $options, ?Transport $transport = null)
    {
        $this->transport = $transport ?? new CurlTransport();
        $this->session = time();
        $this->clientId = (string) random_int(1, PHP_INT_MAX) . '.' . $this->session;
    }

    /** Queue a feature request; return its UUID, or null if an identical report is pending. */
    public function reportFeatureRequest(string $title, string $description): ?string
    {
        return $this->enqueue('feature_request', $title, $description, null, 'user');
    }

    /** Queue a bugfix request; invalid user text is rejected rather than truncated. */
    public function reportBugfixRequest(string $title, string $description): ?string
    {
        return $this->enqueue('bugfix_request', $title, $description, null, 'user');
    }

    /** Queue a curated error. No global exception or shutdown handlers are installed. */
    public function reportError(string $operation, ?\Throwable $error = null): ?string
    {
        return $this->enqueue('error', $operation, null, $error, 'curated');
    }

    /** Queue a crash explicitly from your application's error handling. */
    public function reportCrash(\Throwable $error, string $source): ?string
    {
        return $this->enqueue('crash', 'crash', null, $error, self::required($source, 'source'));
    }

    /** Retry pending reports with their original IDs; only 2xx removes a queued file.
     * @return list<UploadResult>
     */
    public function flushPending(): array
    {
        if ($this->flushing) {
            throw new \LogicException('A flush is already running on this client.');
        }
        $this->flushing = true;
        try {
            $results = [];
            foreach ($this->pending() as $row) {
                $payload = $row['payload'];
                $response = $this->post('/v1/client-error-report', $payload);
                $error = null;
                if ($response !== null && strlen($response->body) <= 4096) {
                    $data = json_decode($response->body, true);
                    if (is_array($data) && is_string($data['error'] ?? null)) {
                        $error = $data['error'];
                    }
                }
                $result = new UploadResult($payload['report_id'], $response?->statusCode, $error);
                if ($result->uploaded()) {
                    $this->remove($row['path']);
                }
                $results[] = $result;
            }
            return $results;
        } finally {
            $this->flushing = false;
        }
    }

    /**
     * Best-effort, non-persistent usage signal; HTTPS and Linux/Windows/macOS only.
     * `$engagementTimeMs` is the optional foreground time since the application started.
     */
    public function trackUsage(string $eventName, ?string $protocol = null, ?int $engagementTimeMs = null): bool
    {
        if (!in_array($eventName, ['app_open', 'update_check', 'device_connection_start'], true)) {
            throw new \InvalidArgumentException('Unsupported event name.');
        }
        if ($eventName === 'device_connection_start' && !in_array($protocol, ['ssh', 'sftp', 'ipmi', 'proxmox', 'winbox'], true)) {
            return false;
        }
        if ($engagementTimeMs !== null && $engagementTimeMs < 0) {
            throw new \InvalidArgumentException('Engagement time must not be negative.');
        }
        $os = self::os();
        if (!in_array($os, ['windows', 'linux', 'macos'], true)
            || !in_array($this->options->architecture, ['amd64', 'arm64', 'all'], true)
            || !in_array($this->options->format, ['deb', 'arch', 'msi', 'msix', 'dmg', 'nuget'], true)) {
            return false;
        }
        $identity = $this->identity();
        $payload = [
            'package_name' => trim($this->options->packageName), 'event_name' => $eventName,
            'protocol' => $eventName === 'device_connection_start' ? $protocol : null,
            'client_id' => $identity ?? $this->clientId, 'session_id' => $this->session,
            'occurred_at_utc' => (new \DateTimeImmutable('now', new \DateTimeZone('UTC')))->format('Y-m-d\TH:i:s.v\Z'),
        ];
        if ($engagementTimeMs !== null) {
            $payload['engagement_time_msec'] = $engagementTimeMs;
        }
        $response = $this->post('/v1/client-usage-event', $payload + [
            'version' => trim($this->options->version), 'architecture' => $this->options->architecture,
            'format' => $this->options->format, 'os' => $os,
        ], true);
        return $response !== null && $response->statusCode >= 200 && $response->statusCode < 300;
    }

    private function enqueue(string $kind, string $operation, ?string $description, ?\Throwable $error, string $source): ?string
    {
        $operation = self::required($operation, 'operation/title');
        if (str_ends_with($kind, '_request')) {
            $description = self::required($description ?? '', 'description');
            if (self::length($operation) > 128 || self::length($description) > 1024) {
                throw new \InvalidArgumentException('Title and description must be at most 128 and 1024 characters.');
            }
        }
        $payload = [
            'report_id' => self::uuid(), 'kind' => $kind,
            'package_name' => trim($this->options->packageName), 'version' => trim($this->options->version),
            'architecture' => $this->options->architecture, 'format' => $this->options->format,
            'runtime' => 'PHP ' . PHP_VERSION, 'os' => self::os(), 'os_version' => php_uname('r'),
            'occurred_at_utc' => (new \DateTimeImmutable('now', new \DateTimeZone('UTC')))->format('Y-m-d\TH:i:s.u\Z'), 'operation' => $operation,
            'exception_type' => $error === null ? null : get_class($error),
            'message' => $description ?? ($error === null ? null : self::truncate($error->getMessage(), 1024)),
            'stack_trace' => $error === null ? null : self::truncate($error->getTraceAsString(), 8000),
            'source' => $source,
        ];
        if (($ga4ClientId = $this->identity()) !== null) {
            $payload['ga4_client_id'] = $ga4ClientId; // only present when the app supplied a consented identity
        }
        $body = json_encode($payload, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE);
        if (strlen($body) > 65536) {
            throw new \LengthException('Report exceeds 65536 UTF-8 bytes.');
        }
        $rows = $this->pending();
        $keys = ['kind', 'operation', 'exception_type', 'message', 'stack_trace'];
        foreach ($rows as $row) {
            $same = true;
            foreach ($keys as $key) {
                if (($row['payload'][$key] ?? null) !== $payload[$key]) {
                    $same = false;
                    break;
                }
            }
            if ($same) {
                return null;
            }
        }
        $directory = $this->options->queueDirectory;
        if (!is_dir($directory) && !@mkdir($directory, 0700, true) && !is_dir($directory)) {
            throw new \RuntimeException('Cannot create queue directory.');
        }
        $target = $directory . DIRECTORY_SEPARATOR . str_replace('-', '', $payload['report_id']) . '.pending.json';
        $temp = $target . '.tmp';
        $stream = @fopen($temp, 'x+b');
        if ($stream === false) {
            throw new \RuntimeException('Cannot create queue file.');
        }
        try {
            if (PHP_OS_FAMILY !== 'Windows' && !chmod($temp, 0600)) {
                throw new \RuntimeException('Cannot restrict queue file permissions.');
            }
            $offset = 0;
            while ($offset < strlen($body)) {
                $written = fwrite($stream, substr($body, $offset));
                if ($written === false || $written === 0) {
                    throw new \RuntimeException('Cannot write queue file.');
                }
                $offset += $written;
            }
            if (!fflush($stream) || !fsync($stream)) {
                throw new \RuntimeException('Cannot persist queue file.');
            }
            fclose($stream);
            $stream = null;
            if (!rename($temp, $target)) {
                throw new \RuntimeException('Cannot commit queue file.');
            }
        } finally {
            if (is_resource($stream)) {
                fclose($stream);
            }
            if (is_file($temp)) {
                @unlink($temp);
            }
        }
        for ($i = 0; $i < max(0, count($rows) - $this->options->maxPending + 1); $i++) {
            $this->remove($rows[$i]['path']);
        }
        return $payload['report_id'];
    }

    /** @return list<array{path:string,payload:array}> */
    private function pending(): array
    {
        $rows = [];
        $directory = $this->options->queueDirectory;
        if (!is_dir($directory)) {
            return [];
        }
        $names = scandir($directory);
        if ($names === false) {
            throw new \RuntimeException('Cannot read queue directory.');
        }
        foreach ($names as $name) {
            if (!preg_match('/\A[0-9a-f]{32}\.pending\.json\z/', $name)) {
                continue;
            }
            $file = $directory . DIRECTORY_SEPARATOR . $name;
            if (is_link($file) || !is_file($file) || filesize($file) > 65536) {
                continue;
            }
            $text = file_get_contents($file);
            $payload = $text === false ? null : json_decode($text, true);
            $id = is_array($payload) ? ($payload['report_id'] ?? null) : null;
            if (!is_string($id) || !preg_match('/\A[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\z/', $id)
                || str_replace('-', '', $id) . '.pending.json' !== $name
                || !in_array($payload['kind'] ?? null, ['crash', 'error', 'bugfix_request', 'feature_request'], true)
                || !is_string($payload['occurred_at_utc'] ?? null)) {
                continue;
            }
            $rows[] = ['path' => $file, 'payload' => $payload];
        }
        usort($rows, static fn(array $a, array $b): int => strcmp($a['payload']['occurred_at_utc'], $b['payload']['occurred_at_utc']) ?: (filemtime($a['path']) <=> filemtime($b['path'])) ?: strcmp($a['path'], $b['path']));
        return $rows;
    }

    private function post(string $path, array $payload, bool $httpsOnly = false): ?HttpResponse
    {
        try {
            $credentials = $this->options->resolveCredentials !== null
                ? ($this->options->resolveCredentials)() : $this->options->credentials;
            if (!$credentials instanceof Credentials || ($httpsOnly && parse_url($credentials->baseUrl, PHP_URL_SCHEME) !== 'https')) {
                return null;
            }
            $headers = ['Content-Type' => 'application/json', 'Authorization' => 'Basic ' . base64_encode($credentials->username . ':' . $credentials->token)];
            return $this->transport->post(rtrim($credentials->baseUrl, '/') . $path, $headers, json_encode($payload, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE), $this->options->timeout);
        } catch (\Throwable) {
            return null;
        }
    }

    private function identity(): ?string
    {
        try {
            return self::validIdentity($this->options->ga4ClientIdProvider === null ? null : ($this->options->ga4ClientIdProvider)());
        } catch (\Throwable) {
            return null;
        }
    }

    private static function validIdentity(mixed $value): ?string
    {
        return is_string($value) && strlen($value) <= 128 && preg_match('/\A[0-9]+\.[0-9]+\z/', $value) ? $value : null;
    }

    private static function required(string $value, string $name): string
    {
        $value = trim($value);
        if ($value === '') {
            throw new \InvalidArgumentException($name . ' is required.');
        }
        self::length($value);
        return $value;
    }

    private static function length(string $value): int
    {
        $count = preg_match_all('/./us', $value);
        if ($count === false) {
            throw new \InvalidArgumentException('Text must be valid UTF-8.');
        }
        return $count;
    }

    private static function truncate(string $value, int $limit): string
    {
        $characters = preg_split('//u', $value, -1, PREG_SPLIT_NO_EMPTY);
        if ($characters === false) {
            throw new \InvalidArgumentException('Text must be valid UTF-8.');
        }
        return implode('', array_slice($characters, 0, $limit));
    }

    private function remove(string $file): void
    {
        if (is_file($file) && !unlink($file)) {
            throw new \RuntimeException('Cannot remove delivered queue file.');
        }
    }

    private static function os(): string
    {
        return match (PHP_OS_FAMILY) { 'Windows' => 'windows', 'Darwin' => 'macos', default => strtolower(PHP_OS_FAMILY) };
    }

    private static function uuid(): string
    {
        $bytes = random_bytes(16);
        $bytes[6] = chr((ord($bytes[6]) & 0x0f) | 0x40);
        $bytes[8] = chr((ord($bytes[8]) & 0x3f) | 0x80);
        $hex = bin2hex($bytes);
        return substr($hex, 0, 8) . '-' . substr($hex, 8, 4) . '-' . substr($hex, 12, 4) . '-' . substr($hex, 16, 4) . '-' . substr($hex, 20);
    }
}
