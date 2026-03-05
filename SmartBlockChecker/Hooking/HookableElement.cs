using System;
using Dalamud.Plugin.Services;

namespace SmartBlockChecker.Hooking;

internal abstract class HookableElement : IDisposable
{
    protected readonly IGameInteropProvider Hooker;
    protected readonly IPluginLog Log;

    public HookableElement(IGameInteropProvider hooker, IPluginLog log)
    {
        Hooker = hooker;
        Log = log;

        try
        {
            Hooker.InitializeFromAttributes(this);
        }
        catch (Exception e)
        {
            Log.Error(e, "Error initializing hooks via attributes.");
        }
    }

    public abstract void Init();
    public abstract void Dispose();
}
