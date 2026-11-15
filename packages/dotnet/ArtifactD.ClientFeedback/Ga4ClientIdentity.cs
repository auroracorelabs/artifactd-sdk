namespace ArtifactD.ClientFeedback;

/// <summary>Optional browser identity shared by Artifactd request signals.</summary>
public static class Ga4ClientIdentity
{
    public static string? Normalize(string? value)
    {
        if (value is null || string.IsNullOrEmpty(value) || value.Length > 128) return null;
        int dot = value.IndexOf('.');
        if (dot <= 0 || dot == value.Length - 1) return null;
        for (int i = 0; i < value.Length; i++)
            if (i != dot && (value[i] < '0' || value[i] > '9')) return null;
        return value;
    }

    public static string? Resolve(Func<string?>? provider)
    {
        try { return Normalize(provider?.Invoke()); }
        catch (Exception) { return null; }
    }
}
