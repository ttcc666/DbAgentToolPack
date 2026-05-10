using DbAgent.Models;

namespace DbAgent.Services;

public interface IModelProfileService
{
    void ValidateModelProfile(ModelProfile profile);

    ModelProfile GetStartupProfile(AppConfig config, string? modelName);

    ModelProfile GetCurrentProfile(AppConfig config);

    ModelProfile? FindProfile(AppConfig config, string profileName);

    string ResolveApiKey(ModelProfile profile);

    string GetProfileDisplayName(ModelProfile profile);

    string GetApiKeySource(ModelProfile profile);

    void AddProfile(AppConfig config, ModelProfile profile);

    ModelRemovalResult RemoveProfile(AppConfig config, string profileName, string currentProfileName);
}
