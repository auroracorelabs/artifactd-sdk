<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/** HTTPS-verifying cURL transport with a total timeout and bounded response body. */
final class CurlTransport implements Transport
{
    public function post(string $url, array $headers, string $body, float $timeout): ?HttpResponse
    {
        $handle = curl_init($url);
        if ($handle === false) {
            return null;
        }
        $response = '';
        $tooLarge = false;
        try {
            curl_setopt_array($handle, [
                CURLOPT_POST => true,
                CURLOPT_POSTFIELDS => $body,
                CURLOPT_HTTPHEADER => array_map(
                    static fn(string $key, string $value): string => $key . ': ' . $value,
                    array_keys($headers), array_values($headers),
                ),
                CURLOPT_FOLLOWLOCATION => false,
                CURLOPT_PROTOCOLS => CURLPROTO_HTTP | CURLPROTO_HTTPS,
                CURLOPT_SSL_VERIFYPEER => true,
                CURLOPT_SSL_VERIFYHOST => 2,
                CURLOPT_CONNECTTIMEOUT_MS => max(1, (int) ceil($timeout * 1000)),
                CURLOPT_TIMEOUT_MS => max(1, (int) ceil($timeout * 1000)),
                CURLOPT_WRITEFUNCTION => static function ($curl, string $chunk) use (&$response, &$tooLarge): int {
                    if (strlen($response) + strlen($chunk) > 4096) {
                        $tooLarge = true;
                        return 0;
                    }
                    $response .= $chunk;
                    return strlen($chunk);
                },
            ]);
            $success = curl_exec($handle);
            $status = (int) curl_getinfo($handle, CURLINFO_RESPONSE_CODE);
            if (($success === false && !$tooLarge) || $status === 0) {
                return null;
            }
            return new HttpResponse($status, $tooLarge ? '' : $response);
        } finally {
            unset($handle);
        }
    }
}
