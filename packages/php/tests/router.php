<?php
declare(strict_types=1);
$path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
$headers = getallheaders();
file_put_contents(getenv('ARTIFACTD_TEST_CAPTURE'), json_encode([
    'path' => $path, 'headers' => array_change_key_case($headers, CASE_LOWER),
    'payload' => json_decode(file_get_contents('php://input'), true),
]) . "\n", FILE_APPEND);
header('Content-Type: application/json');
if (str_starts_with($path, '/slow/')) usleep(500000);
if (str_starts_with($path, '/redirect/')) {
    http_response_code(302);
    header('Location: /created/v1/client-feedback');
} elseif (str_starts_with($path, '/reject/')) {
    http_response_code(403);
    echo '{"error":"access_denied"}';
} elseif (str_starts_with($path, '/large/')) {
    http_response_code(403);
    echo str_repeat('x', 16384);
} elseif (str_starts_with($path, '/duplicate/')) {
    http_response_code(200);
    echo '{}';
} else {
    http_response_code(201);
    echo '{}';
}
