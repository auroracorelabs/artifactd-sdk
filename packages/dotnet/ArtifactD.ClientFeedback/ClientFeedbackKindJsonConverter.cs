using System.Text.Json;

namespace ArtifactD.ClientFeedback;

public sealed class ClientFeedbackKindJsonConverter : System.Text.Json.Serialization.JsonConverter<ClientFeedbackKind>
{
    public override ClientFeedbackKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("kind must be a snake_case string (crash, error, bugfix_request, feature_request).");

        var raw = reader.GetString();
        return raw switch
        {
            "crash" => ClientFeedbackKind.Crash,
            "error" => ClientFeedbackKind.Error,
            "bugfix_request" => ClientFeedbackKind.BugfixRequest,
            "feature_request" => ClientFeedbackKind.FeatureRequest,
            _ => throw new JsonException("kind must be one of: crash, error, bugfix_request, feature_request."),
        };
    }

    public override void Write(Utf8JsonWriter writer, ClientFeedbackKind value, JsonSerializerOptions options)
    {
        var literal = value switch
        {
            ClientFeedbackKind.Crash => "crash",
            ClientFeedbackKind.Error => "error",
            ClientFeedbackKind.BugfixRequest => "bugfix_request",
            ClientFeedbackKind.FeatureRequest => "feature_request",
            _ => throw new JsonException("kind must be one of: crash, error, bugfix_request, feature_request."),
        };
        writer.WriteStringValue(literal);
    }
}
