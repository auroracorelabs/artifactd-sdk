using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ArtifactD.ClientFeedback;

/// <summary>
/// Best-effort usage events for Artifactd (<c>POST /v1/client-usage-event</c>).
/// No inventory data is collected; callers may supply the consented GA4 identity.
/// </summary>
public sealed class ClientUsageReporter : IDisposable
{
    public const string EventPath = "/v1/client-usage-event";
    public const string AppOpenEvent = "app_open";
    public const string UpdateCheckEvent = "update_check";
    public const string DeviceConnectionStartEvent = "device_connection_start";

    private readonly ClientUsageReportingOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<(string Name, string? Protocol, long? EngagementMilliseconds)> _queue;
    private readonly string _clientId = FormattableString.Invariant(
        $"{BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0)}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
    private readonly long _sessionId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly Task _worker;

    public ClientUsageReporter(ClientUsageReportingOptions options)
    {
        Guard.NotNull(options, nameof(options));
        options.Validate();
        _options = options;
        _queue = Channel.CreateBounded<(string, string?, long?)>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropWrite,
            });

        if (options.HttpClient != null)
        {
            _http = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _http = new HttpClient { Timeout = options.Timeout };
            _ownsHttpClient = true;
        }

        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>
    /// App process started (counted once per reporter lifetime by the caller). <paramref name="engagementMilliseconds"/>
    /// is the optional foreground time since the application started.
    /// </summary>
    public void TrackAppOpen(long? engagementMilliseconds = null) => Enqueue(AppOpenEvent, null, engagementMilliseconds);

    /// <summary>An actual repository update check is starting.</summary>
    public void TrackUpdateCheck(long? engagementMilliseconds = null) => Enqueue(UpdateCheckEvent, null, engagementMilliseconds);

    /// <summary>
    /// Device connection attempt. Only allow-listed protocols are queued
    /// (<c>ssh</c>, <c>sftp</c>, <c>ipmi</c>, <c>proxmox</c>, <c>winbox</c>).
    /// </summary>
    public void TrackDeviceConnectionStart(string protocol, long? engagementMilliseconds = null)
    {
        Guard.NotBlank(protocol, nameof(protocol));
        if (protocol is not ("ssh" or "sftp" or "ipmi" or "proxmox" or "winbox"))
            return;
        Enqueue(DeviceConnectionStartEvent, protocol, engagementMilliseconds);
    }

    /// <summary>Drain the in-memory queue (tests and orderly shutdown).</summary>
    public async Task CompleteAsync()
    {
        _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }

    private void Enqueue(string eventName, string? protocol, long? engagementMilliseconds)
    {
        if (engagementMilliseconds is < 0)
            return;
        _queue.Writer.TryWrite((eventName, protocol, engagementMilliseconds));
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await PostAsync(item.Name, item.Protocol, item.EngagementMilliseconds, _stop.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Usage must never interrupt startup, sessions or error reporting.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PostAsync(string eventName, string? protocol, long? engagementMilliseconds, CancellationToken cancellationToken)
    {
        ClientUsageCredentials? credentials = _options.ResolveCredentials();
        if (credentials is null)
            return;

        var endpoint = new Uri(credentials.BaseURL + EventPath);
        // Never transmit repository credentials over cleartext HTTP.
        if (endpoint.Scheme != Uri.UriSchemeHttps)
            return;

        string os = HostOperatingSystem.PlatformName();
        if (os is not ("windows" or "linux" or "macos"))
            return;

        string version = _options.VersionProvider().Trim();
        if (string.IsNullOrWhiteSpace(version))
            return;

        string? architecture = EmptyToNull(_options.Architecture);
        string? format = EmptyToNull(_options.Format);
        if (architecture is null || format is null)
            return;

        string? ga4ClientId = Ga4ClientIdentity.Resolve(_options.Ga4ClientIdProvider);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("package_name", _options.PackageName.Trim());
            json.WriteString("event_name", eventName);
            json.WriteString("protocol", protocol);
            json.WriteString("client_id", ga4ClientId ?? _clientId);
            json.WriteNumber("session_id", _sessionId);
            json.WriteString("occurred_at_utc", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            if (engagementMilliseconds is { } engagement)
                json.WriteNumber("engagement_time_msec", engagement);
            json.WriteString("version", version);
            json.WriteString("architecture", architecture);
            json.WriteString("format", format);
            json.WriteString("os", os);
            json.WriteEndObject();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials.Username + ":" + credentials.Token)));
        request.Content = new ByteArrayContent(buffer.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = response;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _stop.Cancel();
        if (_ownsHttpClient)
            _http.Dispose();
        _stop.Dispose();
    }

    private static string? EmptyToNull(string? value) =>
        value is null || string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
