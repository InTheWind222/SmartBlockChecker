using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using SmartBlockChecker.Hooking.Constants;
using System;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Dalamud.Game.ClientState.Objects.Types;

namespace SmartBlockChecker.Hooking.Hooks;

internal sealed unsafe class ActionHook : HookableElement
{
    private const ulong InvalidTargetId = 0xE0000000;

    private readonly BlacklistService _blacklistService;
    private readonly Configuration _configuration;
    private Hook<Delegates.UseActionDelegate>? _useActionHook;
    private bool _loggedUnavailableState;

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
        if (_blacklistService.IsDataUnavailable && !_loggedUnavailableState)
        {
            Log.Warning("Blacklist data is unavailable. Action prevention is using the last known snapshot until the blacklist reader recovers.");
            _loggedUnavailableState = true;
        }
        else if (!_blacklistService.IsDataUnavailable)
        {
            _loggedUnavailableState = false;
        }

        if (_configuration.PreventBlockedActions && targetId != InvalidTargetId && IsBlockedPlayerTarget(targetId))
        {
            if (param7 is not null)
            {
                *(bool*)param7 = false;
            }

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
        var targetObject = ResolveTargetObject(targetId);
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
        string playerName = targetObject.Name.TextValue;
        return _blacklistService.IsBlocked(contentId, accountId, playerName);
    }

    private static IGameObject? ResolveTargetObject(ulong targetId)
    {
        var objectTable = SmartBlockCheckerPlugin.ObjectTable;
        var directMatch = objectTable.SearchById((uint)targetId);
        if (directMatch is not null)
        {
            return directMatch;
        }

        foreach (var obj in objectTable)
        {
            if (obj is null || !obj.IsValid())
            {
                continue;
            }

            if (MatchesTargetId(obj, targetId))
            {
                return obj;
            }
        }

        return null;
    }

    private static bool MatchesTargetId(IGameObject obj, ulong targetId)
    {
        foreach (string propertyName in new[] { "GameObjectId", "EntityId", "ObjectId" })
        {
            var property = obj.GetType().GetProperty(propertyName);
            if (property?.GetValue(obj) is ulong ulongValue && ulongValue == targetId)
            {
                return true;
            }

            if (property?.GetValue(obj) is uint uintValue && uintValue == (uint)targetId)
            {
                return true;
            }
        }

        return false;
    }
}
