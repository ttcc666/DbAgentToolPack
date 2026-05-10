using DbAgent.Models;

namespace DbAgent.Configuration;

public interface IAppConfigStore
{
    string DefaultAppSettingsJson { get; }

    string DefaultDatabasesJson { get; }

    AppConfig Load(string path);

    void Save(AppConfig config, string path);

    bool Normalize(AppConfig config);

    void Validate(AppConfig config, string databaseConfigPath);
}
