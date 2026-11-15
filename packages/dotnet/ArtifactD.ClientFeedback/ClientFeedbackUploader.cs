using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ArtifactD.ClientFeedback;

public sealed class ClientFeedbackUploader : IDisposable
{
    public const string ReportPath = "/v1/client-feedback";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ClientFeedbackOptions _options;
    private readonly int _maxBodyBytes;

    public ClientFeedbackUploader(ClientFeedbackOptions options)
    {
        Guard.NotNull(options, nameof(options));
        options.Validate();
        _options = options;
        _maxBodyBytes = options.MaxBodyBytes;

        if (options.HttpClient != null)
        {
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient(new HttpClientHandler(), disposeHandler: true);
            _ownsHttpClient = true;
        }
    }

    public async Task<bool> TryUploadAsync(ClientFeedbackPayload payload, CancellationToken cancellationToken = default)
        => (await UploadAsync(payload, cancellationToken).ConfigureAwait(false)).Uploaded;

    public async Task<ClientFeedbackUploadResult> UploadAsync(ClientFeedbackPayload payload, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(payload, nameof(payload));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
        if (bytes.Length > _maxBodyBytes)
            return new(payload.ReportId, HttpStatusCode.RequestEntityTooLarge);

        ClientUsageCredentials? credentials;
        try
        {
            credentials = _options.ResolveCredentials is { } resolve ? resolve()
                : new ClientUsageCredentials(_options.BaseURL, _options.Username, _options.Token);
        }
        catch (Exception) { return new(payload.ReportId, null); }
        if (credentials is null) return new(payload.ReportId, null);
        using var request = new HttpRequestMessage(HttpMethod.Post, credentials.BaseURL + ReportPath);
        // Queue attribution belongs to the account that created the report, not whoever signs in later.
        string raw = $"{credentials.Username}:{credentials.Token}";
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string? error = null;
            if (!response.IsSuccessStatusCode)
                error = ReadError(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return new(payload.ReportId, response.StatusCode, error);
        }
        catch (HttpRequestException)
        {
            return new(payload.ReportId, null);
        }
        catch (TaskCanceledException)
        {
            return new(payload.ReportId, null);
        }
    }

    private static string? ReadError(string body)
    {
        if (body.Length > 4096)
            return null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
                return error.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
