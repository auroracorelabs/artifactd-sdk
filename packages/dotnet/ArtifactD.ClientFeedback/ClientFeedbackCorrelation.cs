namespace ArtifactD.ClientFeedback;

/// <summary>
/// Correlation id shared by every tier that handles one request (web app, API, backend). Each tier reports its own
/// failure with the same id, so Artifactd can show the whole chain down to the root cause.
/// The application mints the id once (<see cref="NewId"/>), forwards it (conventionally as <see cref="HeaderName"/>)
/// and opens a <see cref="BeginScope"/> around the work; reports raised inside the scope pick it up automatically,
/// including crash hooks that know nothing about the request.
/// </summary>
public static class ClientFeedbackCorrelation
{
    public const string HeaderName = "X-Correlation-Id";
    public const int MaxLength = 128;

    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The id of the innermost active <see cref="BeginScope"/> on this logical call context, if any.</summary>
    public static string? Current => Ambient.Value;

    /// <summary>A new opaque id (32 lowercase hex characters).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Returns the trimmed id, or null when it is empty, longer than <see cref="MaxLength"/> or contains whitespace/control characters.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null) return null;
        string trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength) return null;
        foreach (char c in trimmed)
            if (c <= ' ' || c == '\u007f') return null;
        return trimmed;
    }

    /// <summary>Makes <paramref name="correlationId"/> the ambient id until the returned scope is disposed.</summary>
    public static IDisposable BeginScope(string correlationId)
    {
        string? id = Normalize(correlationId);
        if (id is null)
            throw new ArgumentException("correlationId must be 1-128 characters without whitespace or control characters.", nameof(correlationId));
        string? previous = Ambient.Value;
        Ambient.Value = id;
        return new Scope(previous);
    }

    internal static string? Resolve(string? explicitId, Func<string?>? provider)
    {
        if (explicitId is not null)
        {
            string? id = Normalize(explicitId);
            if (id is null)
                throw new ArgumentException("correlationId must be 1-128 characters without whitespace or control characters.", nameof(explicitId));
            return id;
        }

        string? ambient = Ambient.Value;
        if (ambient is not null) return ambient;
        try { return Normalize(provider?.Invoke()); }
        catch (Exception) { return null; }
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
