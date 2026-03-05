using System;

namespace SmartBlockChecker.Hooking.Constants;

internal static unsafe class Delegates
{
    // Client::System::BlacklistManager::IsCharacterBlocked
    public delegate bool IsCharacterBlockedDelegate(nint blacklistManager, ulong contentId, ulong accountId);

    // ActionManager::UseAction
    // ActionManager* self, ActionType type, uint actionID, ulong targetID, uint extraParam, uint comboRouteID, bool* outOptLocation
    public delegate byte UseActionDelegate(nint actionManager, uint actionType, uint actionID, ulong targetID, uint param4, uint param5, uint param6, void* param7);
}
