namespace DbAgent.Models;

public sealed class AppConfigContext
{
    public required AppConfig Config { get; init; }
    public required string ConfigPath { get; init; }
    public required string DatabaseConfigPath { get; init; }
}
