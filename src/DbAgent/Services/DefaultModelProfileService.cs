using DbAgent.Environment;
using DbAgent.Models;

namespace DbAgent.Services;

public sealed class DefaultModelProfileService : IModelProfileService
{
    private readonly IAppEnvironment _appEnvironment;

    public DefaultModelProfileService(IAppEnvironment appEnvironment)
    {
        _appEnvironment = appEnvironment;
    }

    public void ValidateModelProfile(ModelProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("模型配置 Name 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.Endpoint))
        {
            throw new InvalidOperationException($"模型 {profile.Name} 的 Endpoint 不能为空。");
        }

        if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"模型 {profile.Name} 的 Endpoint 必须是绝对 URL。");
        }

        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            throw new InvalidOperationException($"模型 {profile.Name} 的 Model 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey) &&
            string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable))
        {
            throw new InvalidOperationException(
                $"模型 {profile.Name} 至少要配置 ApiKey 或 ApiKeyEnvironmentVariable。");
        }
    }

    public ModelProfile GetStartupProfile(AppConfig config, string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return GetCurrentProfile(config);
        }

        return FindProfile(config, modelName)
            ?? throw new InvalidOperationException($"找不到模型配置：{modelName}");
    }

    public ModelProfile GetCurrentProfile(AppConfig config)
    {
        return config.Models.Profiles.First(profile =>
            profile.Name.Equals(config.Models.Current, StringComparison.OrdinalIgnoreCase));
    }

    public ModelProfile? FindProfile(AppConfig config, string profileName)
    {
        return config.Models.Profiles.FirstOrDefault(profile =>
            profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
    }

    public string ResolveApiKey(ModelProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable))
        {
            var value = _appEnvironment.GetEnvironmentVariable(profile.ApiKeyEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                throw new InvalidOperationException(
                    $"找不到环境变量：{profile.ApiKeyEnvironmentVariable}");
            }
        }

        return profile.ApiKey!;
    }

    public string GetProfileDisplayName(ModelProfile profile)
    {
        return string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.Name
            : profile.DisplayName;
    }

    public string GetApiKeySource(ModelProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable))
        {
            return $"环境变量: {profile.ApiKeyEnvironmentVariable}";
        }

        return "配置文件";
    }

    public void AddProfile(AppConfig config, ModelProfile profile)
    {
        if (FindProfile(config, profile.Name) is not null)
        {
            throw new InvalidOperationException($"已存在同名配置：{profile.Name}");
        }

        ValidateModelProfile(profile);
        config.Models.Profiles.Add(profile);
    }

    public ModelRemovalResult RemoveProfile(
        AppConfig config,
        string profileName,
        string currentProfileName)
    {
        var profile = FindProfile(config, profileName)
            ?? throw new InvalidOperationException($"找不到模型配置：{profileName}");

        if (config.Models.Profiles.Count == 1)
        {
            throw new InvalidOperationException("至少需要保留一个模型配置。");
        }

        config.Models.Profiles.Remove(profile);

        var removedCurrent = currentProfileName.Equals(
            profile.Name,
            StringComparison.OrdinalIgnoreCase);

        ModelProfile? nextCurrentProfile = null;

        if (removedCurrent)
        {
            nextCurrentProfile = config.Models.Profiles[0];
            config.Models.Current = nextCurrentProfile.Name;
        }

        return new ModelRemovalResult
        {
            RemovedProfile = profile,
            RemovedCurrentProfile = removedCurrent,
            NextCurrentProfile = nextCurrentProfile
        };
    }
}
