using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace SmartBlockChecker;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool PreventBlockedActions { get; set; } = true;
    public bool ShowEspCircles { get; set; } = true;
    public bool ShowTargetWarning { get; set; } = true;

    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface!.SavePluginConfig(this);
    }
}
