<?php
declare(strict_types=1);
namespace ArtifactD\ClientFeedback;

/**
 * Correlation id shared by every tier that handles one request (web app, API, backend). Mint it once with
 * {@see newId()}, forward it (conventionally as {@see HEADER}) and wrap the work in {@see run()} (or call
 * {@see set()} at the start of a request): reports raised meanwhile carry it, so Artifactd can join them into one chain.
 */
final class Correlation
{
    public const HEADER = 'X-Correlation-Id';
    public const MAX_LENGTH = 128;

    private static ?string $current = null;

    /** A new opaque id (32 lowercase hex characters). */
    public static function newId(): string
    {
        return bin2hex(random_bytes(16));
    }

    /** Trimmed id, or null when empty, longer than 128 characters or containing whitespace/control characters. */
    public static function normalize(mixed $value): ?string
    {
        if (!is_string($value)) {
            return null;
        }
        $id = trim($value);
        if ($id === '' || strlen($id) > self::MAX_LENGTH || preg_match('/[\x00-\x20\x7f]/', $id) === 1) {
            return null;
        }
        return $id;
    }

    public static function current(): ?string
    {
        return self::$current;
    }

    /** Make the id ambient until {@see clear()}; use at the start of a request. */
    public static function set(string $correlationId): void
    {
        self::$current = self::require($correlationId);
    }

    public static function clear(): void
    {
        self::$current = null;
    }

    /** Run $work with the id ambient, restoring the previous one afterwards. */
    public static function run(string $correlationId, callable $work): mixed
    {
        $id = self::require($correlationId);
        $previous = self::$current;
        self::$current = $id;
        try {
            return $work();
        } finally {
            self::$current = $previous;
        }
    }

    /** @internal */
    public static function require(string $correlationId): string
    {
        $id = self::normalize($correlationId);
        if ($id === null) {
            throw new \InvalidArgumentException('correlationId must be 1-128 characters without whitespace or control characters.');
        }
        return $id;
    }
}
