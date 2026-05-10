using System.Text.Json;
using System.Text.Json.Serialization;
using DbAgent.Models;
using DbAgent.Services;

namespace DbAgent.Configuration;

public sealed class JsonAppConfigStore : IAppConfigStore
{
    private readonly IModelProfileService _modelProfileService;

    public JsonAppConfigStore(IModelProfileService modelProfileService)
    {
        _modelProfileService = modelProfileService;
    }

    public string DefaultAppSettingsJson => """
    {
      "Models": {
        "Current": "local-main",
        "Profiles": [
          {
            "Name": "local-main",
            "DisplayName": "本地主模型",
            "Endpoint": "http://localhost:1234/v1/",
            "Model": "your-model-name",
            "ApiKey": "local-key"
          }
        ]
      },
      "Database": {
        "ConfigPath": "databases.json"
      },
      "Mcp": {
        "Command": "DatabaseMcpServer",
        "Arguments": []
      },
      "Safety": {
        "AllowWriteTools": false,
        "AllowSchemaWriteTools": false
      }
    }
    """;

    public string DefaultDatabasesJson => """
    {
      "databases": [
        {
          "name": "sqlite-local",
          "connectionString": "Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;",
          "dbType": "Sqlite",
          "description": "本地 SQLite 数据库",
          "isDefault": true
        }
      ]
    }
    """;

    public AppConfig Load(string path)
    {
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidOperationException("appsettings.json 内容为空或格式不正确。");
    }

    public void Save(AppConfig config, string path)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        File.WriteAllText(path, json);
    }

    public bool Normalize(AppConfig config)
    {
        var changed = false;

        if (config.Models.Profiles.Count == 0 && config.OpenAI is not null)
        {
            config.Models.Current = "default";
            config.Models.Profiles.Add(new ModelProfile
            {
                Name = "default",
                DisplayName = "默认模型",
                Endpoint = config.OpenAI.Endpoint,
                Model = config.OpenAI.Model,
                ApiKey = config.OpenAI.ApiKey
            });

            config.OpenAI = null;
            changed = true;
        }

        if (config.Models.Profiles.Count > 0 &&
            string.IsNullOrWhiteSpace(config.Models.Current))
        {
            config.Models.Current = config.Models.Profiles[0].Name;
            changed = true;
        }

        if (config.Models.Profiles.Count > 0 &&
            !config.Models.Profiles.Any(p =>
                p.Name.Equals(config.Models.Current, StringComparison.OrdinalIgnoreCase)))
        {
            config.Models.Current = config.Models.Profiles[0].Name;
            changed = true;
        }

        return changed;
    }

    public void Validate(AppConfig config, string databaseConfigPath)
    {
        if (config.Models.Profiles.Count == 0)
        {
            throw new InvalidOperationException("至少需要配置一个模型。");
        }

        foreach (var profile in config.Models.Profiles)
        {
            _modelProfileService.ValidateModelProfile(profile);
        }

        if (string.IsNullOrWhiteSpace(config.Models.Current))
        {
            throw new InvalidOperationException("Models.Current 不能为空。");
        }

        if (!config.Models.Profiles.Any(p =>
                p.Name.Equals(config.Models.Current, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"找不到当前模型配置：{config.Models.Current}");
        }

        if (config.Models.Profiles
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("模型配置名不能重复。");
        }

        if (string.IsNullOrWhiteSpace(config.Mcp.Command))
        {
            throw new InvalidOperationException("Mcp.Command 不能为空。");
        }

        if (!File.Exists(databaseConfigPath))
        {
            throw new FileNotFoundException($"找不到数据库配置文件: {databaseConfigPath}");
        }
    }
}
