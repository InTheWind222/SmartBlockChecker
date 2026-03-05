using System.Collections.Generic;
using Dalamud.Plugin.Services;
using System;

namespace SmartBlockChecker.Hooking;

internal class HookHandler : IDisposable
{
    private readonly IPluginLog _log;
    private readonly List<HookableElement> _hooks = new();

    public readonly Hooks.ActionHook ActionHook;

    public HookHandler(IGameInteropProvider hooker, IPluginLog log, BlacklistChecker blacklistChecker, Configuration config)
    {
        _log = log;

        ActionHook = new Hooks.ActionHook(hooker, log, blacklistChecker, config);
        _hooks.Add(ActionHook);
    }

    public void Initialize()
    {
        _log.Verbose("Initializing HookHandler...");
        foreach (var hook in _hooks)
        {
            hook.Init();
        }
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            hook?.Dispose();
        }
    }
}
