namespace SmartBlockChecker.Hooking.Constants;

internal static class Signatures
{
    public const string IsCharacterBlockedSignature = "48 89 5C 24 ?? 57 48 83 EC 20 48 8B 05 ?? ?? ?? ?? 48 8B D9";
    public const string UseActionSignature = "E8 ?? ?? ?? ?? 89 9F ?? ?? ?? ??";
}
