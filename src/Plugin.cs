using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Configuration;
using HarmonyLib;
using Refactor.Main;
using UnityEngine;

namespace DungeonSettlers.RerollFree;

[BepInPlugin("com.codex.dungeonsettlers.rerollfree", "Dungeon Settlers Reroll Helper", "1.2.0")]
[BepInProcess("DungeonSettlers.exe")]
public sealed class Plugin : BasePlugin
{
    // Current Steam build: GameAssembly.dll 90,117,120 bytes.
    // This is the start of the native `test edi,edi; je direct_roll` branch.
    private const int BranchRva = 0xA56B9F;
    private const int DirectRollRva = 0xA57006;
    private const int BranchLength = 15;

    // Deliberate safety ceiling: rare + legendary is uncommon, but a bad roll
    // must never leave the game stuck in an unbounded native call loop.
    private const int MaxAutoRerolls = 50;
    private static bool _autoRerolling;
    private static Plugin _instance;
    private static RarityMode _rarityMode = RarityMode.Both;
    private readonly KeyboardShortcut _toggleRarityMode = new KeyboardShortcut(KeyCode.T);

    private enum RarityMode
    {
        Both,
        RareOnly,
        LegendaryOnly
    }

    // Rare and legendary inscription keys from the current wiki table.
    private static readonly HashSet<string> TargetInscriptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "AFFECTER_GiantInscription",
        "AFFECTER_SmashInscription",
        "AFFECTER_JesterInscription",
        "AFFECTER_GoldmineInscription",
        "AFFECTER_BlitzInscription",
        "AFFECTER_ChallengeInscription",
        "AFFECTER_MagicSmiteInscription",
        "AFFECTER_StakeInscription",
        "AFFECTER_CrumbleInscription",
        "AFFECTER_ReflectionInscription",
        "AFFECTER_DeathThroesInscription",
        "AFFECTER_ProtectionInscription",
        "AFFECTER_FlintInscription",
        "AFFECTER_HighGradeGuardDamageReductionInscription",
        "AFFECTER_HighGradeGuardChanceInscription",
        "AFFECTER_HighGradeAttackSpeedInscription",
        "AFFECTER_HighGradeMaxEnergyInscription",
        "AFFECTER_HighGradeMagicAttackPowerInscription",
        "AFFECTER_HighGradeMagicResistanceInscription",
        "AFFECTER_HighGradeHitChanceInscription",
        "AFFECTER_HighGradePhysicalAttackPowerInscription",
        "AFFECTER_HighGradePhysicalResistanceInscription",
        "AFFECTER_HighGradeMaxHealthTotalInscription",
        "AFFECTER_HighGradeHealthPercentageRegenerationInscription",
        "AFFECTER_HighGradeLifeStealInscription",
        "AFFECTER_HighGradeCooldownReductionInscription",
        "AFFECTER_HighGradeMaxWeightInscription",
        "AFFECTER_HighGradeCriticalDamageBonusInscription",
        "AFFECTER_HighGradeCriticalChanceInscription",
        "AFFECTER_HighGradeDodgeChanceInscription",
        "AFFECTER_GuardianInscription",
        "AFFECTER_DivineInscription",
        "AFFECTER_FortressInscription",
        "AFFECTER_GloomInscription",
        "AFFECTER_BurstInscription",
        "AFFECTER_HarmonyInscription",
        "AFFECTER_ExecutionInscription",
        "AFFECTER_ShockWaveInscription",
        "AFFECTER_SinkInscription",
        "AFFECTER_ElasticityInscription",
        "AFFECTER_WarriorInscription",
        "AFFECTER_TideInscription",
        "AFFECTER_BloodshotInscription",
        "AFFECTER_RecoveryInscription",
        "AFFECTER_CapitalInscription",
        "AFFECTER_PerfectionInscription",
        "AFFECTER_StormInscription"
    };

    private static readonly HashSet<string> LegendaryInscriptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "AFFECTER_CapitalInscription",
        "AFFECTER_PerfectionInscription",
        "AFFECTER_StormInscription"
    };

    private static readonly byte[] ExpectedBytes =
    {
        0x85, 0xFF,                         // test edi, edi
        0x0F, 0x84, 0x5F, 0x04, 0x00, 0x00, // je direct_roll
        0x49, 0x8B, 0x8F, 0xF8, 0x00, 0x00, 0x00 // mov rcx,[r15+0xf8]
    };

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageExecuteReadWrite = 0x40;

    public override void Load()
    {
        _instance = this;

        try
        {
            AddComponent<HotkeyListener>();
            Log.LogInfo("Rarity filter toggle is bound to T: Both -> Rare only -> Legendary only.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Rarity filter hotkey was not initialized: {ex}");
        }

        try
        {
            ApplyRuntimePatch();
        }
        catch (Exception ex)
        {
            Log.LogError($"Reroll-free patch was not applied: {ex}");
        }

        try
        {
            ApplyAutoRerollPatch();
        }
        catch (Exception ex)
        {
            Log.LogError($"Auto-reroll patch was not applied: {ex}");
        }
    }

    private void HandleHotkey()
    {
        if (!_toggleRarityMode.IsDown())
            return;

        _rarityMode = (RarityMode)(((int)_rarityMode + 1) % 3);
        Log.LogInfo($"Reroll target changed to {GetRarityModeLabel(_rarityMode)}.");
    }

    private static string GetRarityModeLabel(RarityMode mode)
    {
        switch (mode)
        {
            case RarityMode.RareOnly:
                return "Rare only (blue)";
            case RarityMode.LegendaryOnly:
                return "Legendary only (yellow)";
            default:
                return "Rare + legendary (blue + yellow)";
        }
    }

    private static void ApplyAutoRerollPatch()
    {
        MethodBase target = AccessTools.Method(
            typeof(LevelUpHelper),
            nameof(LevelUpHelper.PrayLevelUp),
            new[] { typeof(OfferingType), typeof(bool), typeof(bool) });

        if (target == null)
            throw new MissingMethodException("LevelUpHelper.PrayLevelUp(OfferingType, bool, bool)");

        var harmony = new Harmony("com.codex.dungeonsettlers.rerollfree.auto");
        harmony.Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(BeforePrayer))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(AfterPrayer))));

        _instance.Log.LogInfo(
            $"Auto-reroll active for rare/legendary inscriptions; max extra rolls={MaxAutoRerolls}.");
    }

    private static void BeforePrayer(LevelUpHelper __instance, ref bool __state)
    {
        // The game uses this flag to distinguish the first prayer from retry.
        __state = __instance != null && __instance.HasLevelUpResult;
    }

    private static void AfterPrayer(
        LevelUpHelper __instance,
        OfferingType offeringType,
        bool lockInscriptions,
        bool lockStats,
        bool __state)
    {
        if (!__state || __instance == null || _autoRerolling)
            return;

        _autoRerolling = true;
        int extraRolls = 0;
        try
        {
            if (HasTargetInscription(__instance))
                return;

            while (extraRolls < MaxAutoRerolls && !HasTargetInscription(__instance))
            {
                __instance.PrayLevelUp(offeringType, lockInscriptions, lockStats);
                extraRolls++;
            }

            _instance.Log.LogInfo(
                extraRolls == MaxAutoRerolls
                    ? $"Auto-reroll stopped at safety limit ({MaxAutoRerolls}); no target rarity found."
                    : $"Auto-rerolled {extraRolls} extra time(s) until a rare/legendary inscription appeared.");
        }
        catch (Exception ex)
        {
            _instance.Log.LogError($"Auto-reroll stopped after {extraRolls} extra roll(s): {ex}");
        }
        finally
        {
            _autoRerolling = false;
        }
    }

    private static bool HasTargetInscription(LevelUpHelper helper)
    {
        var inscriptions = helper.LevelUpInscriptions;
        if (inscriptions == null)
            return false;

        for (int i = 0; i < inscriptions.Count; i++)
        {
            string key = inscriptions[i];
            if (IsTargetInscription(key))
                return true;
        }

        return false;
    }

    private static bool IsTargetInscription(string key)
    {
        if (key == null)
            return false;

        switch (_rarityMode)
        {
            case RarityMode.RareOnly:
                return TargetInscriptions.Contains(key) && !LegendaryInscriptions.Contains(key);
            case RarityMode.LegendaryOnly:
                return LegendaryInscriptions.Contains(key);
            default:
                return TargetInscriptions.Contains(key);
        }
    }

    private sealed class HotkeyListener : MonoBehaviour
    {
        public HotkeyListener(IntPtr ptr) : base(ptr) { }

        public void Update()
        {
            if (_instance != null)
                _instance.HandleHotkey();
        }
    }

    private void ApplyRuntimePatch()
    {
        IntPtr gameAssembly = GetModuleHandle("GameAssembly.dll");
        if (gameAssembly == IntPtr.Zero)
            throw new InvalidOperationException("GameAssembly.dll is not loaded.");

        IntPtr branch = IntPtr.Add(gameAssembly, BranchRva);
        byte[] actual = new byte[ExpectedBytes.Length];
        Marshal.Copy(branch, actual, 0, actual.Length);

        if (!actual.SequenceEqual(ExpectedBytes))
        {
            throw new InvalidOperationException(
                $"Game build mismatch at RVA 0x{BranchRva:X}; patch refused for safety. " +
                $"Expected {ToHex(ExpectedBytes)}, got {ToHex(actual)}.");
        }

        IntPtr continuation = IntPtr.Add(branch, BranchLength);
        IntPtr directRoll = IntPtr.Add(gameAssembly, DirectRollRva);
        IntPtr codeCave = VirtualAlloc(
            IntPtr.Zero,
            (UIntPtr)0x1000,
            MemCommit | MemReserve,
            PageExecuteReadWrite);

        if (codeCave == IntPtr.Zero)
            throw new InvalidOperationException("VirtualAlloc failed.");

        // If HasLevelUpResult is true, zero the already-calculated amount and
        // take the game's existing zero-cost branch. Otherwise execute the
        // original test/branch behavior unchanged.
        byte[] stub = BuildStub(continuation, directRoll);
        Marshal.Copy(stub, 0, codeCave, stub.Length);

        byte[] detour = AbsoluteJump(codeCave);
        Array.Resize(ref detour, BranchLength);
        detour[12] = 0x90;
        detour[13] = 0x90;
        detour[14] = 0x90;

        if (!VirtualProtect(branch, (UIntPtr)BranchLength, PageExecuteReadWrite, out uint oldProtection))
            throw new InvalidOperationException($"VirtualProtect failed: {Marshal.GetLastWin32Error()}.");

        Marshal.Copy(detour, 0, branch, detour.Length);
        VirtualProtect(branch, (UIntPtr)BranchLength, oldProtection, out _);
        FlushInstructionCache(GetCurrentProcess(), branch, (UIntPtr)BranchLength);

        Log.LogInfo(
            $"Reroll-free patch active in memory only. Retry cost is zero; first prayer is unchanged. " +
            $"GameAssembly base=0x{gameAssembly.ToInt64():X}, cave=0x{codeCave.ToInt64():X}.");
    }

    private static byte[] BuildStub(IntPtr continuation, IntPtr directRoll)
    {
        var code = new List<byte>();

        // mov rcx,[r15+0xf8] (LevelUpHelper*)
        code.AddRange(new byte[] { 0x49, 0x8B, 0x8F, 0xF8, 0x00, 0x00, 0x00 });
        // cmp byte ptr [rcx+0x68],0 (HasLevelUpResult)
        code.AddRange(new byte[] { 0x80, 0x79, 0x68, 0x00 });
        // jne zero_amount; target is offset 29, next instruction is offset 13.
        code.AddRange(new byte[] { 0x75, 0x10 });
        // Preserve the original test edi,edi / je direct_roll behavior.
        code.AddRange(new byte[] { 0x85, 0xFF });
        // je direct_roll; target is offset 31, next instruction is offset 17.
        code.AddRange(new byte[] { 0x74, 0x0E });
        code.AddRange(AbsoluteJump(continuation));
        // zero_amount:
        code.AddRange(new byte[] { 0x31, 0xFF }); // xor edi,edi
        code.AddRange(AbsoluteJump(directRoll));

        return code.ToArray();
    }

    private static byte[] AbsoluteJump(IntPtr destination)
    {
        byte[] jump = new byte[12];
        jump[0] = 0x48;
        jump[1] = 0xB8; // mov rax, imm64
        BitConverter.GetBytes(destination.ToInt64()).CopyTo(jump, 2);
        jump[10] = 0xFF;
        jump[11] = 0xE0; // jmp rax
        return jump;
    }

    private static string ToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", " ");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(
        IntPtr address,
        UIntPtr size,
        uint allocationType,
        uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(
        IntPtr address,
        UIntPtr size,
        uint newProtection,
        out uint oldProtection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(
        IntPtr process,
        IntPtr address,
        UIntPtr size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
