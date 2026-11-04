namespace AIFitnessCoach.AI.Configuration;

public sealed class AIOptions
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string Endpoint { get; set; } = "https://api.openai.com/v1/";
    public int MaxOutputTokens { get; set; } = 800;
    public int ConversationHistoryMessages { get; set; } = 12;
    public int MaxConversationHistoryCharacters { get; set; } = 16_000;
    public int ContextLookbackDays { get; set; } = 90;
    public int DailyAnalysisRequestLimit { get; set; } = 20;
    public int DailyCoachTokenLimit { get; set; } = 30_000;
    public int MaximumActiveCoachConversations { get; set; } = 20;
    public int MaximumCoachConversationMessages { get; set; } = 200;
    public int CoachMessagesPerMinute { get; set; } = 10;
    public decimal InputCostPerMillionTokens { get; set; }
    public decimal OutputCostPerMillionTokens { get; set; }

    public bool IsValid(out string error)
    {
        if (MaxOutputTokens is < 1 or > 8192
            || ConversationHistoryMessages is < 1 or > 50
            || MaxConversationHistoryCharacters is < 1_000 or > 100_000
            || ContextLookbackDays is < 1 or > 365
            || DailyAnalysisRequestLimit is < 1 or > 200
            || DailyCoachTokenLimit is < 1_000 or > 1_000_000
            || MaximumActiveCoachConversations is < 1 or > 500
            || MaximumCoachConversationMessages is < 12 or > 200
            || CoachMessagesPerMinute is < 1 or > 60
            || InputCostPerMillionTokens < 0
            || OutputCostPerMillionTokens < 0)
        {
            error = "AI limits and token pricing must be within their allowed ranges.";
            return false;
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            error = "AI:Endpoint must be an absolute HTTPS URL.";
            return false;
        }

        if (Enabled
            && (string.IsNullOrWhiteSpace(ApiKey)
                || string.IsNullOrWhiteSpace(Model)
                || Model.Length > 128
                || InputCostPerMillionTokens <= 0
                || OutputCostPerMillionTokens <= 0))
        {
            error = "AI:ApiKey, AI:Model, and positive token prices are required when AI is enabled.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class AIConfigurationException(string message) : Exception(message);

public sealed class AIProviderException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

public sealed class AIConversationNotFoundException() : Exception("Conversation was not found.");
