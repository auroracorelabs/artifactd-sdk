namespace ArtifactD.ClientFeedback;

public sealed class ClientFeedbackOptions
{
    public const int DefaultMaxPending = 20;
    public const int DefaultMaxStackChars = 8000;
    public const int DefaultMaxBodyBytes = 65536;

    /// <summary>Optional live pull identity; null defers upload until sign-in.</summary>
    public Func<ClientUsageCredentials?>? ResolveCredentials { get; set; }
    public string BaseURL { get; set; } = "";
    public string Username { get; set; } = "";
    public string Token { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string Version { get; set; } = "";
    public Func<string>? VersionProvider { get; set; }
    public string? Architecture { get; set; }
    public string? Format { get; set; }
    public string QueueDirectory { get; set; } = "";
    public TimeSpan DedupWindow { get; set; } = TimeSpan.FromMinutes(15);
    public int MaxPending { get; set; } = DefaultMaxPending;
    public int MaxStackChars { get; set; } = DefaultMaxStackChars;
    public int MaxBodyBytes { get; set; } = DefaultMaxBodyBytes;
    /// <summary>Optional consented GA4 ID, resolved for every outgoing request. Provider errors are ignored.</summary>
    public Func<string?>? Ga4ClientIdProvider { get; set; }
    public HttpClient? HttpClient { get; set; }

    public string ResolveVersion()
    {
        if (VersionProvider != null)
        {
            string provided = VersionProvider();
            if (string.IsNullOrWhiteSpace(provided))
                throw new InvalidOperationException("VersionProvider returned an empty version.");
            return provided.Trim();
        }

        if (string.IsNullOrWhiteSpace(Version))
            throw new InvalidOperationException("Version is required (must match the published artefact version_raw).");
        return Version.Trim();
    }

    public void Validate()
    {
        if (ResolveCredentials is null && string.IsNullOrWhiteSpace(BaseURL))
            throw new InvalidOperationException("BaseURL is required.");
        if (ResolveCredentials is null && string.IsNullOrWhiteSpace(Username))
            throw new InvalidOperationException("Username is required.");
        if (ResolveCredentials is null && string.IsNullOrWhiteSpace(Token))
            throw new InvalidOperationException("Token is required.");
        if (string.IsNullOrWhiteSpace(PackageName))
            throw new InvalidOperationException("PackageName is required.");
        if (string.IsNullOrWhiteSpace(QueueDirectory))
            throw new InvalidOperationException("QueueDirectory is required.");
        if (MaxPending < 1)
            throw new InvalidOperationException("MaxPending must be at least 1.");
        if (MaxStackChars < 1)
            throw new InvalidOperationException("MaxStackChars must be at least 1.");
        if (MaxBodyBytes < 1)
            throw new InvalidOperationException("MaxBodyBytes must be at least 1.");
        _ = ResolveVersion();
        if (ResolveCredentials is null && (!Uri.TryCreate(BaseURL.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            throw new InvalidOperationException("BaseURL must be an absolute http or https URL.");
    }
}
