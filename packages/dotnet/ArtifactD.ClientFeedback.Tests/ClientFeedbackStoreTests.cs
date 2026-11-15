using ArtifactD.ClientFeedback;

namespace ArtifactD.ClientFeedback.Tests;

public sealed class ClientFeedbackStoreTests
{
    [Fact]
    public void WritePending_ThenList_ReturnsPayload()
    {
        string dir = NewQueueDir();
        var store = new ClientFeedbackStore(dir, maxPending: 20);
        var payload = Sample(Guid.NewGuid(), ClientFeedbackKind.Error, "ssh_open");
        store.WritePending(payload);

        IReadOnlyList<ClientFeedbackPayload> pending = store.ListPending();
        Assert.Single(pending);
        Assert.Equal(payload.ReportId, pending[0].ReportId);
        Assert.Equal("ssh_open", pending[0].Operation);
    }

    [Fact]
    public void MarkSent_RemovesPendingFile()
    {
        string dir = NewQueueDir();
        var store = new ClientFeedbackStore(dir, maxPending: 20);
        var payload = Sample(Guid.NewGuid(), ClientFeedbackKind.Crash, "crash");
        store.WritePending(payload);
        store.MarkSent(payload.ReportId);
        Assert.Empty(store.ListPending());
    }

    [Fact]
    public void WritePending_TrimsOldestWhenOverCap()
    {
        string dir = NewQueueDir();
        var store = new ClientFeedbackStore(dir, maxPending: 2);
        var first = Sample(Guid.NewGuid(), ClientFeedbackKind.Error, "a");
        var second = Sample(Guid.NewGuid(), ClientFeedbackKind.Error, "b");
        var third = Sample(Guid.NewGuid(), ClientFeedbackKind.Error, "c");
        store.WritePending(first);
        Thread.Sleep(20);
        store.WritePending(second);
        Thread.Sleep(20);
        store.WritePending(third);

        IReadOnlyList<ClientFeedbackPayload> pending = store.ListPending();
        Assert.Equal(2, pending.Count);
        Assert.DoesNotContain(pending, p => p.ReportId == first.ReportId);
    }

    private static ClientFeedbackPayload Sample(Guid id, ClientFeedbackKind kind, string operation) =>
        new()
        {
            ReportId = id,
            Kind = kind,
            PackageName = "app",
            Version = "1.0.0",
            OccurredAtUtc = DateTime.UtcNow,
            Operation = operation,
            ExceptionType = "System.InvalidOperationException",
            StackTrace = "stack",
        };

    private static string NewQueueDir() =>
        Path.Combine(Path.GetTempPath(), "cer-store-" + Guid.NewGuid().ToString("N"));
}
