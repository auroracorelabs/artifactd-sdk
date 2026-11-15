<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/** Injectable synchronous transport. Implementations must not follow redirects. */
interface Transport
{
    /** @param array<string,string> $headers Return null on transport failure. */
    public function post(string $url, array $headers, string $body, float $timeout): ?HttpResponse;
}
