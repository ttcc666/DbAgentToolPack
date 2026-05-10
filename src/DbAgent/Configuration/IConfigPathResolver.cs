namespace DbAgent.Configuration;

public interface IConfigPathResolver
{
    string ResolveConfigPath(string? explicitConfigPath);

    string GetDefaultConfigPath();
}
