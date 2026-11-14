using System.Runtime.InteropServices;

namespace ArtifactD.ClientFeedback;

public static class HostOperatingSystem
{
    public const int MaxOsChars = 256;
    public const int MaxOsVersionChars = 128;

    public static string PlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macos";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD")))
            return "freebsd";
        return "unknown";
    }

    public static string VersionLabel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string? fromRelease = ParseOsReleasePretty(TryReadOsRelease());
            if (fromRelease is not null && !string.IsNullOrWhiteSpace(fromRelease))
                return Truncate(fromRelease, MaxOsVersionChars);
        }

        string description = RuntimeInformation.OSDescription.Trim();
        if (!string.IsNullOrWhiteSpace(description))
            return Truncate(description, MaxOsVersionChars);

        return Truncate(Environment.OSVersion.Version.ToString(), MaxOsVersionChars);
    }

    internal static string? ParseOsReleasePretty(string? contents)
    {
        if (contents is null || string.IsNullOrWhiteSpace(contents))
            return null;

        string? pretty = null;
        string? version = null;
        string? versionId = null;
        foreach (string rawLine in contents.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line.Substring(0, eq);
            string value = Unquote(line.Substring(eq + 1).Trim());
            if (key.Equals("PRETTY_NAME", StringComparison.Ordinal))
                pretty = value;
            else if (key.Equals("VERSION", StringComparison.Ordinal))
                version = value;
            else if (key.Equals("VERSION_ID", StringComparison.Ordinal))
                versionId = value;
        }

        if (!string.IsNullOrWhiteSpace(pretty))
            return pretty;
        if (!string.IsNullOrWhiteSpace(version))
            return version;
        if (!string.IsNullOrWhiteSpace(versionId))
            return versionId;
        return null;
    }

    private static string? TryReadOsRelease()
    {
        const string path = "/etc/os-release";
        try
        {
            if (!File.Exists(path))
                return null;
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\'')))
            return value.Substring(1, value.Length - 2);
        return value;
    }

    private static string Truncate(string value, int maxChars)
    {
        string trimmed = value.Trim();
        if (trimmed.Length <= maxChars)
            return trimmed;
        return trimmed.Substring(0, maxChars);
    }
}
