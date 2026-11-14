<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/** Configuration for one client that exclusively owns a persistent queue directory. */
final readonly class ClientFeedbackOptions
{
    /** @var \Closure(): ?Credentials|null Return null to defer delivery until sign-in. */
    public ?\Closure $resolveCredentials;
    /** @var \Closure(): ?string|null Optional consented GA4 client identity. */
    public ?\Closure $ga4ClientIdProvider;

    public function __construct(
        public string $packageName,
        public string $version,
        public string $queueDirectory,
        public ?Credentials $credentials = null,
        ?callable $resolveCredentials = null,
        ?callable $ga4ClientIdProvider = null,
        public ?string $architecture = null,
        public ?string $format = null,
        public int $maxPending = 20,
        public float $timeout = 15.0,
    ) {
        if (trim($packageName) === '' || trim($version) === '' || trim($queueDirectory) === '') {
            throw new \InvalidArgumentException('packageName, version and queueDirectory are required.');
        }
        if ($maxPending < 1 || !is_finite($timeout) || $timeout <= 0) {
            throw new \InvalidArgumentException('maxPending and timeout must be positive.');
        }
        if ($credentials === null && $resolveCredentials === null) {
            throw new \InvalidArgumentException('Provide credentials or a credential provider.');
        }
        $this->resolveCredentials = $resolveCredentials === null ? null : \Closure::fromCallable($resolveCredentials);
        $this->ga4ClientIdProvider = $ga4ClientIdProvider === null ? null : \Closure::fromCallable($ga4ClientIdProvider);
    }
}
