using DbAgent.Environment;

namespace DbAgent.Tests.TestDoubles;

internal sealed class FakeAppEnvironment : IAppEnvironment
{
    private readonly HashSet<string> _existingFiles;
    private readonly Dictionary<string, string?> _variables;

    public FakeAppEnvironment(
        string currentDirectory,
        string baseDirectory,
        string userHomeDirectory,
        IEnumerable<string>? existingFiles = null,
        IDictionary<string, string?>? variables = null)
    {
        CurrentDirectory = currentDirectory;
        BaseDirectory = baseDirectory;
        UserHomeDirectory = userHomeDirectory;
        _existingFiles = existingFiles is null
            ? []
            : existingFiles.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _variables = variables is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(variables, StringComparer.OrdinalIgnoreCase);
    }

    public string CurrentDirectory { get; }

    public string BaseDirectory { get; }

    public string UserHomeDirectory { get; }

    public string? GetEnvironmentVariable(string name)
    {
        return _variables.TryGetValue(name, out var value)
            ? value
            : null;
    }

    public bool FileExists(string path)
    {
        return _existingFiles.Contains(Path.GetFullPath(path));
    }
}
