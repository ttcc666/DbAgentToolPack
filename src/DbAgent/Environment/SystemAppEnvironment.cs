namespace DbAgent.Environment;

public sealed class SystemAppEnvironment : IAppEnvironment
{
    public string CurrentDirectory => Directory.GetCurrentDirectory();

    public string BaseDirectory => AppContext.BaseDirectory;

    public string UserHomeDirectory =>
        global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.UserProfile);

    public string? GetEnvironmentVariable(string name)
    {
        return global::System.Environment.GetEnvironmentVariable(name);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }
}
