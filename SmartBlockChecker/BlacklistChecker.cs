using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using System;
using System.Collections.Generic;
using System.Text;

namespace SmartBlockChecker;

public class BlockedPlayerInfo
{
    public ulong Id { get; set; }         // AccountId (new blocks) or ContentId (old blocks)
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Reads the in-game blacklist using the official FFXIVClientStructs layout.
/// </summary>
internal unsafe class BlacklistChecker
{
    private delegate void ProcessChatBoxDelegate(nint uiModule, nint message, nint unused, byte a4);
    
    // We get UIModule, then we call ProcessChatBox on it.
    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9")]
    private ProcessChatBoxDelegate? _processChatBox = null;

    private readonly IPluginLog _log;
    public string DiagnosticInfo { get; private set; } = "Not yet scanned.";

    // From official FFXIVClientStructs
    private const int EntryArrayOffset         = 0xF0;    // _blockedCharacters
    private const int BlockedCountOffset       = 0x19F0;  // BlockedCharactersCount
    private const int EntrySize                = 0x20;    // sizeof(BlockedCharacter)
    private const int MaxEntries               = 200;

    // BlockedCharacter field offsets
    private const int OffsetName = 0x00;   // CStringPointer (8-byte pointer to char*)
    private const int OffsetId   = 0x10;   // ulong — accountId or contentId
    private const int OffsetFlag = 0x18;   // byte

    public BlacklistChecker(IPluginLog log, IGameInteropProvider interopProvider)
    {
        _log = log;
        interopProvider.InitializeFromAttributes(this);
        _log.Information($"BlacklistChecker initialized. ProcessChatBox signature found = {_processChatBox != null}");
    }

    public bool SmartBlockViaChat(string name)
    {
        if (_processChatBox == null)
        {
            _log.Error("ProcessChatBox signature was not found, cannot execute command.");
            return false;
        }

        try
        {
            var uiModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule;
            if (uiModule == null)
            {
                _log.Error("UIModule is null, cannot execute chat command.");
                return false;
            }

            // The native /blacklist command from macros works best with the <t> placeholder.
            // Since /smartblock requires a target anyway, this perfectly mimics a user macro.
            string command = $"/blacklist add <t>";
            _log.Information($"Executing Native Chat Command via ProcessChatBox: {command}");

            byte[] commandBytes = Encoding.UTF8.GetBytes(command + "\0");
            fixed (byte* pCommand = commandBytes)
            {
                var utfCommand = new FFXIVClientStructs.FFXIV.Client.System.String.Utf8String();
                utfCommand.Ctor();
                utfCommand.SetString(pCommand);

                // Arg 1: UIModule pointer. Arg 2: Utf8String pointer. Arg 3: IntPtr.Zero. Arg 4: 0 (byte)
                _processChatBox((nint)uiModule, (nint)(&utfCommand), IntPtr.Zero, 0);
                
                utfCommand.Dtor();
            }

            return true;
        }
        catch (Exception e)
        {
            _log.Error(e, "Exception while natively injecting /blacklist into chat via ProcessChatBox");
            return false;
        }
    }

    public bool IsBlocked(ulong contentId, ulong accountId)
    {
        if (contentId == 0 && accountId == 0) return false;

        try
        {
            var proxy = InfoProxyBlacklist.Instance();
            if (proxy == null) return false;

            byte* proxyBase = (byte*)proxy;
            int count = *(int*)(proxyBase + BlockedCountOffset);
            if (count <= 0 || count > MaxEntries) return false;

            byte* entries = proxyBase + EntryArrayOffset;

            for (int i = 0; i < count; i++)
            {
                byte* entry = entries + (i * EntrySize);
                ulong entryId = *(ulong*)(entry + OffsetId);

                if (entryId == 0) continue;

                // Id can be either accountId or contentId depending on block age
                if (entryId == contentId || entryId == accountId) return true;
            }
        }
        catch (Exception) { }

        return false;
    }

    public List<BlockedPlayerInfo> GetBlockedEntries()
    {
        var result = new List<BlockedPlayerInfo>();

        try
        {
            var proxy = InfoProxyBlacklist.Instance();
            if (proxy == null)
            {
                DiagnosticInfo = "Proxy = null (not loaded yet).";
                return result;
            }

            byte* proxyBase = (byte*)proxy;
            int count = *(int*)(proxyBase + BlockedCountOffset);

            if (count <= 0 || count > MaxEntries)
            {
                DiagnosticInfo = $"Proxy=0x{(nint)proxy:X} | Count={count} (invalid)";
                return result;
            }

            byte* entries = proxyBase + EntryArrayOffset;

            for (int i = 0; i < count; i++)
            {
                byte* entry = entries + (i * EntrySize);
                ulong entryId = *(ulong*)(entry + OffsetId);

                if (entryId == 0) continue;

                // Read name via CStringPointer at +0x00
                string name = ReadCStringPointer(entry + OffsetName);

                if (string.IsNullOrWhiteSpace(name))
                    name = $"ID:0x{entryId:X}";

                result.Add(new BlockedPlayerInfo
                {
                    Id = entryId,
                    Name = name,
                });
            }

            DiagnosticInfo = $"Proxy=0x{(nint)proxy:X} | Count={count} | Found {result.Count}";
        }
        catch (Exception e)
        {
            DiagnosticInfo = $"Exception: {e.GetType().Name}: {e.Message}";
        }

        return result;
    }

    /// <summary>
    /// Reads a CStringPointer: dereferences the 8-byte pointer at addr,
    /// then reads the null-terminated UTF8 string at the target.
    /// </summary>
    private string ReadCStringPointer(byte* addr)
    {
        try
        {
            // Read the pointer value (8 bytes)
            byte* strPtr = *(byte**)addr;
            if (strPtr == null) return string.Empty;

            // Validate it looks like a real pointer (in process memory space)
            ulong ptrVal = (ulong)strPtr;
            if (ptrVal < 0x10000) return string.Empty; // Too low, probably garbage

            // Read up to 64 chars of null-terminated UTF8
            int len = 0;
            while (len < 64 && strPtr[len] != 0) len++;

            if (len == 0) return string.Empty;

            return Encoding.UTF8.GetString(strPtr, len);
        }
        catch
        {
            return string.Empty;
        }
    }
}
