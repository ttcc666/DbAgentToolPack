namespace DbAgent.Environment;

public interface IAppEnvironment
{
    string CurrentDirectory { get; }
    string BaseDirectory { get; }
    string UserHomeDirectory { get; }

    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);
}
