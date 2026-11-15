namespace ArtifactD.ClientFeedback;

internal static class Guard
{
    public static void NotNull(object? value, string name)
    {
        if (value is null) throw new ArgumentNullException(name);
    }

    public static void NotBlank(string? value, string name)
    {
        if (value is null) throw new ArgumentNullException(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty or whitespace.", name);
    }
}
