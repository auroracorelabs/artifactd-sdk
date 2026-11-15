<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/** Pull identity resolved for an upload. Never persisted in the report queue. */
final readonly class Credentials
{
    public function __construct(
        public string $baseUrl,
        public string $username,
        public string $token,
    ) {
        $url = parse_url($baseUrl);
        if (!filter_var($baseUrl, FILTER_VALIDATE_URL) || !$url
            || !in_array($url['scheme'] ?? '', ['http', 'https'], true)
            || empty($url['host']) || isset($url['user']) || isset($url['pass'])
            || isset($url['query']) || isset($url['fragment'])) {
            throw new \InvalidArgumentException('baseUrl must be an HTTP(S) URL without credentials, query or fragment.');
        }
        if (trim($username) === '' || $token === '' || str_contains($username, ':')) {
            throw new \InvalidArgumentException('A pull username without a colon and a token are required.');
        }
    }
}
