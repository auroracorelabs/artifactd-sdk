using System.Text.Json.Serialization;

namespace ArtifactD.ClientFeedback;

[JsonConverter(typeof(ClientFeedbackKindJsonConverter))]
public enum ClientFeedbackKind : byte
{
    Crash = 1,
    Error = 2,
    BugfixRequest = 3,
    FeatureRequest = 4,
}
