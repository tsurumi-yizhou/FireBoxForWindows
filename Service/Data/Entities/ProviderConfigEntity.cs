namespace Service.Data.Entities;

public sealed class ProviderConfigEntity
{
    public int Id { get; set; }
    public string ProviderType { get; set; } = string.Empty; // "OpenAI", "Anthropic", "Gemini"
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public byte[] EncryptedApiKey { get; set; } = [];
    public string EnabledModelIdsJson { get; set; } = "[]"; // JSON array
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
