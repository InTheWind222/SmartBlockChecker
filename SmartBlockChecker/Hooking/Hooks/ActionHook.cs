using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using SmartBlockChecker.Hooking.Constants;
using System;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace SmartBlockChecker.Hooking.Hooks;

internal sealed unsafe class ActionHook : HookableElement
{
    private const ulong InvalidTargetId = 0xE0000000;

    private readonly BlacklistService _blacklistService;
    private readonly Configuration _configuration;
    private Hook<Delegates.UseActionDelegate>? _useActionHook;

    public ActionHook(
        IGameInteropProvider interopProvider,
        IPluginLog log,
        BlacklistService blacklistService,
        Configuration configuration)
        : base(interopProvider, log)
    {
        _blacklistService = blacklistService;
        _configuration = configuration;
        
        try
        {
            var address = (nint)FFXIVClientStructs.FFXIV.Client.Game.ActionManager.MemberFunctionPointers.UseAction;
            _useActionHook = Hooker.HookFromAddress<Delegates.UseActionDelegate>(address, UseActionDetour);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to hook ActionManager.UseAction.");
        }
    }

    public override void Init()
    {
        _useActionHook?.Enable();
        Log.Verbose("ActionManager.UseAction hook enabled.");
    }

    private byte UseActionDetour(nint actionManager, uint actionType, uint actionId, ulong targetId, uint param4, uint param5, uint param6, void* param7)
    {
        if (_configuration.PreventBlockedActions && targetId != InvalidTargetId && IsBlockedPlayerTarget(targetId))
        {
            Log.Information("Prevented action {ActionId} on a blocked target.", actionId);
            return 0;
        }

        if (_useActionHook is null)
        {
            return 0;
        }

        return _useActionHook.Original(actionManager, actionType, actionId, targetId, param4, param5, param6, param7);
    }

    public override void Dispose()
    {
        _useActionHook?.Dispose();
    }

    private bool IsBlockedPlayerTarget(ulong targetId)
    {
        var targetObject = SmartBlockCheckerPlugin.ObjectTable.SearchById((uint)targetId);
        if (targetObject is null || targetObject.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
        {
            return false;
        }

        var character = (Character*)targetObject.Address;
        if (character is null)
        {
            return false;
        }

        ulong contentId = character->ContentId;
        ulong accountId = character->AccountId;
        return (contentId != 0 || accountId != 0) && _blacklistService.IsBlocked(contentId, accountId);
    }
}
