<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/** Delivery result. A null status means transport failure or deferred credentials. */
final readonly class UploadResult
{
    public function __construct(
        public string $reportId,
        public ?int $statusCode,
        public ?string $error = null,
    ) {}

    public function uploaded(): bool
    {
        return $this->statusCode !== null && $this->statusCode >= 200 && $this->statusCode < 300;
    }
}
