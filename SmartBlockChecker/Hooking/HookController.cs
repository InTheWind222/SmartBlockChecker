using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace SmartBlockChecker.Hooking;

internal sealed class HookController : IDisposable
{
    private readonly IPluginLog _log;
    private readonly List<HookableElement> _hooks = new();

    public HookController(
        IGameInteropProvider interopProvider,
        IPluginLog log,
        BlacklistService blacklistService,
        Configuration configuration)
    {
        _log = log;
        _hooks.Add(new Hooks.ActionHook(interopProvider, log, blacklistService, configuration));
    }

    public void Initialize()
    {
        _log.Verbose("Initializing hooks.");
        foreach (var hook in _hooks)
        {
            hook.Init();
        }
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            hook.Dispose();
        }
    }
}
