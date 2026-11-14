namespace ArtifactD.ClientFeedback;

public static class ClientFeedbackBootstrap
{
    public static void AttachUnhandledHandlers(ClientFeedbackClient reporter)
    {
        Guard.NotNull(reporter, nameof(reporter));

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                TryReportCrash(reporter, ex, "unhandled");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            TryReportCrash(reporter, args.Exception, "task");
            args.SetObserved();
        };
    }

    public static void ReportDispatcherException(ClientFeedbackClient reporter, Exception exception)
    {
        Guard.NotNull(reporter, nameof(reporter));
        Guard.NotNull(exception, nameof(exception));
        TryReportCrash(reporter, exception, "dispatcher");
    }

    private static void TryReportCrash(ClientFeedbackClient reporter, Exception exception, string source)
    {
        try
        {
            reporter.ReportCrash(exception, source);
        }
        catch (Exception)
        {
            // Crash capture must not throw.
        }
    }
}
