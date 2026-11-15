using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ArtifactD.ClientFeedback;

public sealed class ClientFeedbackClient : IDisposable
{
    private readonly ClientFeedbackOptions _options;
    private readonly ClientFeedbackStore _store;
    private readonly ClientFeedbackUploader _uploader;
    private readonly ConcurrentDictionary<string, DateTime> _recentFingerprints = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public ClientFeedbackClient(ClientFeedbackOptions options)
    {
        Guard.NotNull(options, nameof(options));
        options.Validate();
        _options = options;
        _store = new ClientFeedbackStore(options.QueueDirectory, options.MaxPending);
        _uploader = new ClientFeedbackUploader(options);
    }

    public ClientFeedbackStore Store => _store;

    /// <param name="correlationId">Optional id shared with the other tiers of the same request; defaults to the ambient <see cref="ClientFeedbackCorrelation.BeginScope"/> id.</param>
    public Guid ReportCrash(Exception exception, string source, string? correlationId = null)
    {
        Guard.NotNull(exception, nameof(exception));
        Guard.NotBlank(source, nameof(source));
        return Enqueue(ClientFeedbackKind.Crash, operation: "crash", exception, source.Trim(), correlationId: correlationId);
    }

    /// <param name="correlationId">Optional id shared with the other tiers of the same request; defaults to the ambient <see cref="ClientFeedbackCorrelation.BeginScope"/> id.</param>
    public Guid ReportError(string operation, Exception? exception = null, string? correlationId = null)
    {
        Guard.NotBlank(operation, nameof(operation));
        return Enqueue(ClientFeedbackKind.Error, operation.Trim(), exception, source: "curated", correlationId: correlationId);
    }

    public Guid ReportBugfixRequest(string title, string description) =>
        ReportUserRequest(ClientFeedbackKind.BugfixRequest, title, description);

    public Guid ReportFeatureRequest(string title, string description) =>
        ReportUserRequest(ClientFeedbackKind.FeatureRequest, title, description);

    private Guid ReportUserRequest(ClientFeedbackKind kind, string title, string description)
    {
        Guard.NotBlank(title, nameof(title));
        Guard.NotBlank(description, nameof(description));
        if (title.Trim().Length > 128)
            throw new ArgumentException("title must be at most 128 characters.", nameof(title));
        if (description.Trim().Length > 1024)
            throw new ArgumentException("description must be at most 1024 characters.", nameof(description));
        return Enqueue(kind, title.Trim(), null, "user", description.Trim());
    }

    public async Task FlushPendingAsync(CancellationToken cancellationToken = default) =>
        await FlushPendingWithResultsAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ClientFeedbackUploadResult>> FlushPendingWithResultsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ClientFeedbackUploadResult>();
        foreach (ClientFeedbackPayload payload in _store.ListPending())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _uploader.UploadAsync(payload, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.Uploaded)
                _store.MarkSent(payload.ReportId);
        }
        return results;
    }

    public void Dispose() => _uploader.Dispose();

    private Guid Enqueue(
        ClientFeedbackKind kind,
        string operation,
        Exception? exception,
        string source,
        string? description = null,
        string? correlationId = null)
    {
        string? resolvedCorrelationId = ClientFeedbackCorrelation.Resolve(correlationId, _options.CorrelationIdProvider);
        lock (_gate)
        {
            var payload = BuildPayload(kind, operation, exception, source);
            payload.CorrelationId = resolvedCorrelationId;
            if (description != null)
                payload.Message = description;
            string fingerprint = ClientFeedbackStore.BuildFingerprint(payload);
            DateTime now = DateTime.UtcNow;
            PruneFingerprints(now);
            if (_recentFingerprints.TryGetValue(fingerprint, out DateTime last)
                && now - last < _options.DedupWindow)
                return Guid.Empty;
            if (_store.HasPendingFingerprint(fingerprint))
                return Guid.Empty;

            _store.WritePending(payload);
            _recentFingerprints[fingerprint] = now;
            return payload.ReportId;
        }
    }

    private ClientFeedbackPayload BuildPayload(
        ClientFeedbackKind kind,
        string operation,
        Exception? exception,
        string source)
    {
        string? stack = exception?.ToString();
        if (stack != null && stack.Length > _options.MaxStackChars)
            stack = stack.Substring(0, _options.MaxStackChars);

        string? message = exception?.Message;
        if (message != null && message.Length > 1024)
            message = message.Substring(0, 1024);

        return new ClientFeedbackPayload
        {
            ReportId = Guid.NewGuid(),
            Ga4ClientId = Ga4ClientIdentity.Resolve(_options.Ga4ClientIdProvider),
            Kind = kind,
            PackageName = _options.PackageName.Trim(),
            Version = _options.ResolveVersion(),
            Architecture = EmptyToNull(_options.Architecture),
            Format = EmptyToNull(_options.Format),
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = HostOperatingSystem.PlatformName(),
            OsVersion = HostOperatingSystem.VersionLabel(),
            OccurredAtUtc = DateTime.UtcNow,
            Operation = operation,
            ExceptionType = exception?.GetType().FullName,
            Message = message,
            StackTrace = stack,
            Source = source,
        };
    }

    private void PruneFingerprints(DateTime now)
    {
        foreach (var pair in _recentFingerprints)
        {
            if (now - pair.Value >= _options.DedupWindow)
                _recentFingerprints.TryRemove(pair.Key, out _);
        }
    }

    private static string? EmptyToNull(string? value) =>
        value is null || string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
