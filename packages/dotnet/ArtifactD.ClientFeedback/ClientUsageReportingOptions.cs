namespace ArtifactD.ClientFeedback;

/// <summary>Best-effort usage events posted to Artifactd (<c>/v1/client-usage-event</c>).</summary>
public sealed class ClientUsageReportingOptions
{
    public const int DefaultQueueCapacity = 32;

    /// <summary>
    /// Returns pull credentials for the next event, or null to skip (locked keyring, unsigned-in).
    /// Resolved again for each queued item so late sign-in can start sending.
    /// </summary>
    public Func<ClientUsageCredentials?> ResolveCredentials { get; set; } = static () => null;

    /// <summary>Exact published artefact <c>version_raw</c> of the running build.</summary>
    /// <summary>Catalogue package name of the running application (the <c>package_name</c> of its releases).</summary>
    public string PackageName { get; set; } = "";

    public Func<string> VersionProvider { get; set; } = static () =>
        throw new InvalidOperationException("VersionProvider is required.");

    public string? Architecture { get; set; }
    public string? Format { get; set; }
    /// <summary>Optional consented GA4 ID, resolved for every outgoing request. Provider errors are ignored.</summary>
    public Func<string?>? Ga4ClientIdProvider { get; set; }
    public HttpClient? HttpClient { get; set; }
    public int QueueCapacity { get; set; } = DefaultQueueCapacity;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        Guard.NotNull(ResolveCredentials, nameof(ResolveCredentials));
        Guard.NotBlank(PackageName, nameof(PackageName));
        Guard.NotNull(VersionProvider, nameof(VersionProvider));
        if (QueueCapacity < 1)
            throw new InvalidOperationException("QueueCapacity must be at least 1.");
        if (Timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Timeout must be greater than zero.");
    }
}
