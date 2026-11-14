using System.Text.Json.Serialization;

namespace ArtifactD.ClientFeedback;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    Converters = [typeof(ClientFeedbackKindJsonConverter)])]
[JsonSerializable(typeof(ClientFeedbackPayload))]
[JsonSerializable(typeof(ClientFeedbackKind))]
public partial class ClientFeedbackJsonContext : JsonSerializerContext;
