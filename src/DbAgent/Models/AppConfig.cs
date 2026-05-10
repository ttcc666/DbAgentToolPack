using System.Text.Json.Serialization;

namespace DbAgent.Models;

public sealed class AppConfig
{
    public ModelProfilesConfig Models { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public McpConfig Mcp { get; set; } = new();
    public SafetyConfig Safety { get; set; } = new();

    [JsonPropertyName("OpenAI")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyOpenAIConfig? OpenAI { get; set; }
}

public sealed class ModelProfilesConfig
{
    public string Current { get; set; } = "";
    public List<ModelProfile> Profiles { get; set; } = [];
}

public sealed class ModelProfile
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string? ApiKey { get; set; }
    public string? ApiKeyEnvironmentVariable { get; set; }
}

public sealed class LegacyOpenAIConfig
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
}

public sealed class DatabaseConfig
{
    public string ConfigPath { get; set; } = "databases.json";
    public string? SeqServerUrl { get; set; }
    public string? SeqApiKey { get; set; }
    public string? DdlWhitelist { get; set; }
}

public sealed class McpConfig
{
    public string Command { get; set; } = "DatabaseMcpServer";
    public string[] Arguments { get; set; } = [];
}

public sealed class SafetyConfig
{
    public bool AllowWriteTools { get; set; }
    public bool AllowSchemaWriteTools { get; set; }
}
