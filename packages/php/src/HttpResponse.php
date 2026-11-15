<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/** Bounded HTTP response used by the transport abstraction. */
final readonly class HttpResponse
{
    public function __construct(public int $statusCode, public string $body = '') {}
}
