<?php
declare(strict_types=1);
// Standalone bootstrap for examples and extracted archives; Composer uses PSR-4.
spl_autoload_register(static function (string $class): void {
    $prefix = 'ArtifactD\\ClientFeedback\\';
    if (str_starts_with($class, $prefix)) {
        $name = substr($class, strlen($prefix));
        if (preg_match('/\A[A-Za-z][A-Za-z0-9]*\z/', $name)) {
            $file = __DIR__ . '/src/' . $name . '.php';
            if (is_file($file)) {
                require_once $file;
            }
        }
    }
});
