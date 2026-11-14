namespace ArtifactD.ClientFeedback;

/// <summary>Pull identity for <c>POST /v1/client-usage-event</c> on the package-feed dataplane.</summary>
public sealed class ClientUsageCredentials
{
    public ClientUsageCredentials(string baseURL, string username, string token)
    {
        Guard.NotBlank(baseURL, nameof(baseURL));
        Guard.NotBlank(username, nameof(username));
        Guard.NotBlank(token, nameof(token));
        BaseURL = baseURL.Trim().TrimEnd('/');
        Username = username.Trim();
        Token = token;
    }

    public string BaseURL { get; }
    public string Username { get; }
    public string Token { get; }
}
