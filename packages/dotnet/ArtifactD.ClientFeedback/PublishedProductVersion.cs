using System.Reflection;

namespace ArtifactD.ClientFeedback;

public static class PublishedProductVersion
{
    public static string FromAssembly(Assembly assembly)
    {
        Guard.NotNull(assembly, nameof(assembly));
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is not null && !string.IsNullOrWhiteSpace(informational))
            return informational.Trim();

        Version? version = assembly.GetName().Version;
        if (version == null)
            return "0.0.0";
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public static string FromEntryAssembly()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(PublishedProductVersion).Assembly;
        return FromAssembly(assembly);
    }
}
