<?php
declare(strict_types=1);
require __DIR__ . '/../../packages/php/autoload.php';

function escape(string $text): string { return htmlspecialchars($text, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8'); }
function signature(ReflectionMethod $method): string {
    $parameters = [];
    foreach ($method->getParameters() as $parameter) {
        $text = ($parameter->hasType() ? (string) $parameter->getType() . ' ' : '')
            . ($parameter->isPassedByReference() ? '&' : '') . ($parameter->isVariadic() ? '...' : '') . '$' . $parameter->getName();
        if ($parameter->isDefaultValueAvailable()) $text .= ' = ' . var_export($parameter->getDefaultValue(), true);
        $parameters[] = $text;
    }
    return 'public ' . ($method->isStatic() ? 'static ' : '') . 'function ' . $method->getName()
        . '(' . implode(', ', $parameters) . ')' . ($method->hasReturnType() ? ': ' . $method->getReturnType() : '');
}
function description(Reflector $reflection): string {
    $comment = $reflection->getDocComment();
    if ($comment === false) return '';
    return preg_replace('/^\s*\* ?/m', '', trim(substr($comment, 3, -2)));
}
$directory = __DIR__ . '/../../packages/php/src';
$html = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Artifactd Client Feedback PHP API</title><link rel="stylesheet" href="../../assets/docs.css"><style>main h2{overflow-wrap:anywhere}main pre code{white-space:pre-wrap;overflow-wrap:anywhere}main nav a{display:inline-block;margin-right:8px}</style></head><body><main style="max-width:1100px;margin:auto"><a href="https://auroracorelabs.github.io/artifactd-sdk-client/">SDK documentation</a><h1>Artifactd Client Feedback PHP API</h1><p>Generated from the public PHP SDK classes, interfaces, properties and method signatures. PHP 8.2+, synchronous API.</p><nav aria-label="PHP types">';
$classes = [];
foreach (glob($directory . '/*.php') as $file) {
    $name = pathinfo($file, PATHINFO_FILENAME);
    $classes[$name] = new ReflectionClass('ArtifactD\\ClientFeedback\\' . $name);
    $html .= '<a href="#' . $name . '">' . $name . '</a> · ';
}
$html .= '</nav>';
foreach ($classes as $name => $class) {
    $html .= '<section><h2 id="' . $name . '">' . escape($name) . '</h2><p><code>' . escape($class->getName()) . '</code></p><p>' . escape(description($class)) . '</p>';
    foreach ($class->getProperties(ReflectionProperty::IS_PUBLIC) as $property) {
        $html .= '<h3>' . escape($property->getName()) . '</h3><pre><code>'
            . escape('public ' . ($property->isReadOnly() ? 'readonly ' : '') . $property->getType() . ' $' . $property->getName())
            . '</code></pre><p>' . escape(description($property)) . '</p>';
    }
    foreach ($class->getMethods(ReflectionMethod::IS_PUBLIC) as $method) {
        if ($method->getDeclaringClass()->getName() !== $class->getName()) continue;
        $html .= '<h3 id="' . $name . '-' . $method->getName() . '">' . escape($method->getName()) . '</h3><pre style="white-space:pre-wrap"><code>'
            . escape(signature($method)) . '</code></pre><p>' . nl2br(escape(description($method))) . '</p>';
    }
    $html .= '</section>';
}
$html .= '</main></body></html>';
$output = __DIR__ . '/../../build/docs-api/php';
if (!is_dir($output) && !mkdir($output, 0755, true) && !is_dir($output)) throw new RuntimeException('Cannot create PHP API output.');
file_put_contents($output . '/index.html', $html);
echo "Generated PHP API reference.\n";
