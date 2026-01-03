using amFTPd.Config.Daemon;
using amFTPd.Core.Pre;

namespace amFTPd.Core.Runtime;

/// <summary>
/// Provides methods to manage the recovery state and persistence of runtime configuration data during application
/// recovery operations.
/// </summary>
/// <remarks>Use this class to coordinate the beginning and end of recovery processes and to load or save all
/// relevant runtime configuration data. This class is not thread-safe; callers should ensure appropriate
/// synchronization if accessed from multiple threads.</remarks>
public sealed class RuntimeRecoveryManager
{
    private readonly AmFtpdRuntimeConfig _runtime;
    private readonly PreRegistryPersistence _prePersistence;

    public bool IsRecovering { get; private set; }

    public RuntimeRecoveryManager(AmFtpdRuntimeConfig runtime)
    {
        _runtime = runtime;
        _prePersistence = new PreRegistryPersistence();
    }

    public void BeginRecovery()
    {
        IsRecovering = true;
    }

    public void EndRecovery()
    {
        IsRecovering = false;
    }

    public void LoadAll()
    {
        var configDir = Path.GetDirectoryName(_runtime.ConfigFilePath)
                        ?? AppContext.BaseDirectory;

        var preFile = Path.Combine(
            configDir,
            "pre_registry.json");


        _prePersistence.Load(_runtime.PreRegistry, preFile);
    }

    public void SaveAll()
    {
        var configDir = Path.GetDirectoryName(_runtime.ConfigFilePath)
                        ?? AppContext.BaseDirectory;

        var preFile = Path.Combine(
            configDir,
            "pre_registry.json");


        _prePersistence.Save(_runtime.PreRegistry, preFile);
    }
}