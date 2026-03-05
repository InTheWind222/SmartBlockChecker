using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using SmartBlockChecker.Hooking.Constants;
using System;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace SmartBlockChecker.Hooking.Hooks;

internal unsafe class ActionHook : HookableElement
{
    private readonly BlacklistChecker _blacklistChecker;
    private readonly Configuration _config;

    private Hook<Delegates.UseActionDelegate>? _useActionHook = null;

    public ActionHook(IGameInteropProvider hooker, IPluginLog log, BlacklistChecker blacklistChecker, Configuration config) : base(hooker, log)
    {
        _blacklistChecker = blacklistChecker;
        _config = config;
        
        try 
        {
            var address = (nint)FFXIVClientStructs.FFXIV.Client.Game.ActionManager.MemberFunctionPointers.UseAction;
            _useActionHook = Hooker.HookFromAddress<Delegates.UseActionDelegate>(address, UseActionDetour);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to hook UseAction natively");
        }
    }

    public override void Init()
    {
        _useActionHook?.Enable();
        Log.Verbose("ActionManager.UseAction hook enabled.");
    }

    private byte UseActionDetour(nint actionManager, uint actionType, uint actionID, ulong targetID, uint param4, uint param5, uint param6, void* param7)
    {
        if (_config.PreventBlockedActions && targetID != 0xE0000000) 
        {
            var targetObj = SmartBlockChecker.Plugin.ObjectTable.SearchById((uint)targetID);
            if (targetObj != null && targetObj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
            {
                var character = (Character*)targetObj.Address;
                if (character != null)
                {
                    ulong contentId = character->ContentId;
                    ulong accountId = character->AccountId;

                    if (contentId != 0 || accountId != 0)
                    {
                        if (_blacklistChecker.IsBlocked(contentId, accountId))
                        {
                            Log.Information($"Prevented action {actionID} on blocked target {targetObj.Name} ({contentId} / {accountId})");
                            return 0;
                        }
                    }
                }
            }
        }

        if (_useActionHook == null) return 0;
        return _useActionHook.Original(actionManager, actionType, actionID, targetID, param4, param5, param6, param7);
    }

    public override void Dispose()
    {
        _useActionHook?.Dispose();
    }
}
