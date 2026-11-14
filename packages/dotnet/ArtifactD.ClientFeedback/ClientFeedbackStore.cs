using System.Text.Json;

namespace ArtifactD.ClientFeedback;

public sealed class ClientFeedbackStore
{
    private readonly string _directory;
    private readonly int _maxPending;

    public ClientFeedbackStore(string queueDirectory, int maxPending)
    {
        Guard.NotBlank(queueDirectory, nameof(queueDirectory));
        if (maxPending < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPending));

        _directory = queueDirectory;
        _maxPending = maxPending;
    }

    public string DirectoryPath => _directory;

    public void WritePending(ClientFeedbackPayload payload)
    {
        Guard.NotNull(payload, nameof(payload));
        Directory.CreateDirectory(_directory);
        TrimOldestPending();

        string path = PendingPath(payload.ReportId);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
        File.WriteAllBytes(path, bytes);
    }

    public IReadOnlyList<ClientFeedbackPayload> ListPending()
    {
        if (!Directory.Exists(_directory))
            return Array.Empty<ClientFeedbackPayload>();

        var rows = new List<ClientFeedbackPayload>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.pending.json"))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                ClientFeedbackPayload? payload = JsonSerializer.Deserialize(
                    bytes,
                    ClientFeedbackJsonContext.Default.ClientFeedbackPayload);
                if (payload != null)
                    rows.Add(payload);
            }
            catch (JsonException)
            {
                // skip corrupt queue files
            }
            catch (IOException)
            {
                // skip locked files
            }
        }

        return rows;
    }

    public void MarkSent(Guid reportId)
    {
        string pending = PendingPath(reportId);
        if (File.Exists(pending))
            File.Delete(pending);
    }

    public bool HasPendingFingerprint(string fingerprint)
    {
        Guard.NotBlank(fingerprint, nameof(fingerprint));
        foreach (ClientFeedbackPayload payload in ListPending())
        {
            if (string.Equals(BuildFingerprint(payload), fingerprint, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static string BuildFingerprint(ClientFeedbackPayload payload)
    {
        Guard.NotNull(payload, nameof(payload));
        string stack = payload.StackTrace ?? "";
        if (stack.Length > 240)
            stack = stack.Substring(0, 240);
        return string.Join(
            "|",
            ((byte)payload.Kind).ToString(),
            payload.Operation ?? "",
            payload.ExceptionType ?? "",
            payload.Kind is ClientFeedbackKind.BugfixRequest or ClientFeedbackKind.FeatureRequest
                ? payload.Message ?? "" : "",
            stack);
    }

    private void TrimOldestPending()
    {
        if (!Directory.Exists(_directory))
            return;

        FileInfo[] files = Directory.GetFiles(_directory, "*.pending.json")
            .Select(p => new FileInfo(p))
            .OrderBy(f => f.CreationTimeUtc)
            .ToArray();
        int extra = files.Length - _maxPending + 1;
        if (extra <= 0)
            return;

        for (int i = 0; i < extra && i < files.Length; i++)
            files[i].Delete();
    }

    private string PendingPath(Guid reportId) =>
        Path.Combine(_directory, $"{reportId:N}.pending.json");
}
