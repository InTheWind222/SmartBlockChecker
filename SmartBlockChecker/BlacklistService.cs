using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace SmartBlockChecker;

internal sealed class BlacklistEntry
{
    public ulong Identifier { get; init; }

    public string Name { get; init; } = string.Empty;
}

internal unsafe sealed class BlacklistService
{
    private delegate void ProcessChatBoxDelegate(nint uiModule, nint message, nint unused, byte a4);

#pragma warning disable CS0649 // Set by Dalamud signature injection at runtime.
    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9")]
    private ProcessChatBoxDelegate? _processChatBox;
#pragma warning restore CS0649

    private readonly IPluginLog _log;

    public string DiagnosticInfo { get; private set; } = "Blacklist has not been scanned yet.";

    private const int EntryArrayOffset = 0xF0;
    private const int BlockedCountOffset = 0x19F0;
    private const int EntrySize = 0x20;
    private const int MaxEntries = 200;

    private const int NamePointerOffset = 0x00;
    private const int IdentifierOffset = 0x10;

    public BlacklistService(IPluginLog log, IGameInteropProvider interopProvider)
    {
        _log = log;
        interopProvider.InitializeFromAttributes(this);
        _log.Information("Blacklist service initialized. ProcessChatBox signature found: {Found}", _processChatBox is not null);
    }

    public bool ExecuteBlacklistCommand()
    {
        if (_processChatBox is null)
        {
            _log.Error("ProcessChatBox signature was not found. Cannot execute /blacklist command.");
            return false;
        }

        try
        {
            var uiModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule;
            if (uiModule is null)
            {
                _log.Error("UIModule is null. Cannot execute /blacklist command.");
                return false;
            }

            const string command = "/blacklist add <t>";
            byte[] commandBytes = Encoding.UTF8.GetBytes(command + "\0");

            fixed (byte* commandPointer = commandBytes)
            {
                var utfCommand = new FFXIVClientStructs.FFXIV.Client.System.String.Utf8String();
                utfCommand.Ctor();
                utfCommand.SetString(commandPointer);

                _processChatBox((nint)uiModule, (nint)(&utfCommand), IntPtr.Zero, 0);
                utfCommand.Dtor();
            }

            _log.Information("Executed native blacklist command through ProcessChatBox.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to execute the native /blacklist command.");
            return false;
        }
    }

    public bool IsBlocked(ulong contentId, ulong accountId)
    {
        if (contentId == 0 && accountId == 0)
        {
            return false;
        }

        try
        {
            var proxy = InfoProxyBlacklist.Instance();
            if (proxy is null)
            {
                return false;
            }

            byte* proxyBase = (byte*)proxy;
            int blockedCount = *(int*)(proxyBase + BlockedCountOffset);
            if (blockedCount <= 0 || blockedCount > MaxEntries)
            {
                return false;
            }

            byte* entries = proxyBase + EntryArrayOffset;
            for (int index = 0; index < blockedCount; index++)
            {
                byte* entry = entries + (index * EntrySize);
                ulong entryIdentifier = *(ulong*)(entry + IdentifierOffset);

                if (entryIdentifier == 0)
                {
                    continue;
                }

                if (entryIdentifier == contentId || entryIdentifier == accountId)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed while checking whether a player is blacklisted.");
        }

        return false;
    }

    public List<BlacklistEntry> GetEntries()
    {
        var entries = new List<BlacklistEntry>();

        try
        {
            var proxy = InfoProxyBlacklist.Instance();
            if (proxy is null)
            {
                DiagnosticInfo = "Blacklist info proxy is not loaded yet.";
                return entries;
            }

            byte* proxyBase = (byte*)proxy;
            int blockedCount = *(int*)(proxyBase + BlockedCountOffset);
            if (blockedCount <= 0 || blockedCount > MaxEntries)
            {
                DiagnosticInfo = $"Blacklist count is invalid: {blockedCount}.";
                return entries;
            }

            byte* rawEntries = proxyBase + EntryArrayOffset;
            for (int index = 0; index < blockedCount; index++)
            {
                byte* entry = rawEntries + (index * EntrySize);
                ulong identifier = *(ulong*)(entry + IdentifierOffset);
                if (identifier == 0)
                {
                    continue;
                }

                string name = ReadCStringPointer(entry + NamePointerOffset);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"ID:0x{identifier:X}";
                }

                entries.Add(new BlacklistEntry
                {
                    Identifier = identifier,
                    Name = name
                });
            }

            DiagnosticInfo = $"Loaded {entries.Count} blacklist entries.";
        }
        catch (Exception ex)
        {
            DiagnosticInfo = $"Blacklist scan failed: {ex.GetType().Name}";
            _log.Warning(ex, "Failed while reading blacklist entries.");
        }

        return entries;
    }

    private static string ReadCStringPointer(byte* address)
    {
        try
        {
            byte* stringPointer = *(byte**)address;
            if (stringPointer is null || (ulong)stringPointer < 0x10000)
            {
                return string.Empty;
            }

            int length = 0;
            while (length < 64 && stringPointer[length] != 0)
            {
                length++;
            }

            return length == 0 ? string.Empty : Encoding.UTF8.GetString(stringPointer, length);
        }
        catch
        {
            return string.Empty;
        }
    }
}
