using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Configuration;

namespace HyperKeyX.Application.Abstractions.Persistence;

public interface IConfigurationStore
{
    ConfigurationLoadResult Load();

    OperationResult Save(
        AutomationConfigurationDocument document,
        ConfigurationWriteIntent intent = ConfigurationWriteIntent.Ordinary);

    string ConfigFilePath { get; }
}
