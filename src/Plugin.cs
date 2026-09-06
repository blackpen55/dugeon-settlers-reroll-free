using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Refactor;
using Refactor.Main;
using Refactor.Tick;
using Refactor.UI;
using UnityEngine;

namespace DungeonSettlers.RerollFree;

[BepInPlugin("com.codex.dungeonsettlers.rerollfree", "Dungeon Settlers Reroll Helper", "1.5.0")]
[BepInProcess("DungeonSettlers.exe")]
public sealed class Plugin : BasePlugin
{
    // The native branch moves when Steam ships a new GameAssembly.dll. Locate
    // it by its stable instruction signature instead of editing the game file.
    private const int BranchLength = 15;
    private const int PanelUnitFieldOffset = 0x48;

    // Deliberate safety ceilings: legendary is much rarer, so it gets a larger
    // cap only in LegendaryOnly mode. Neither path can loop indefinitely.
    private const int MaxAutoRerolls = 50;
    private const int MaxLegendaryAutoRerolls = 300;
    private static bool _autoRerolling;
    private static Plugin _instance;
    private static RarityMode _rarityMode = RarityMode.Both;
    private static DataApplier _dataApplier;
    private static string _overlayText;
    private static float _overlayUntil;
    private static readonly List<TraitSlotInfo> TraitSlots = new List<TraitSlotInfo>();
    private static readonly Dictionary<EntityStatusPanelUI, UnitEntity> PanelUnits =
        new Dictionary<EntityStatusPanelUI, UnitEntity>();
    private static readonly System.Random ReplacementRandom = new System.Random();
    private static FieldInfo _panelUnitField;
    private static int _trackedSlotLogCount;
    // J and L are unused in the game's current keybind screen. T and Y are
    // already assigned to the game's skill/item shortcuts.
    private readonly KeyboardShortcut _toggleRarityMode = new KeyboardShortcut(KeyCode.J);
    private readonly KeyboardShortcut _replaceHoveredInscription = new KeyboardShortcut(KeyCode.L);

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

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageExecuteReadWrite = 0x40;

    public override void Load()
    {
        _instance = this;

        try
        {
            AddComponent<HotkeyListener>();
            Log.LogInfo("Rarity filter toggle is bound to J: Rare + Legendary -> Rare Only -> Legendary Only.");
            Log.LogInfo("Inscription replacement is bound to L while the mouse is over an inscription icon.");
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

        try
        {
            ApplyUiPatches();
        }
        catch (Exception ex)
        {
            Log.LogError($"Inscription replacement UI hooks were not applied: {ex}");
        }
    }

    private void HandleHotkey()
    {
        if (!_toggleRarityMode.IsDown())
        {
            if (_replaceHoveredInscription.IsDown())
                ReplaceHoveredInscription();

            return;
        }

        _rarityMode = _rarityMode switch
        {
            RarityMode.Both => RarityMode.RareOnly,
            RarityMode.RareOnly => RarityMode.LegendaryOnly,
            _ => RarityMode.Both
        };
        ShowOverlay(GetRarityModeOverlayLabel(_rarityMode));
        Log.LogInfo($"Reroll target changed to {GetRarityModeLabel(_rarityMode)}.");

        if (_replaceHoveredInscription.IsDown())
            ReplaceHoveredInscription();
    }

    private static string GetRarityModeOverlayLabel(RarityMode mode)
    {
        switch (mode)
        {
            case RarityMode.RareOnly:
                return "<color=#42A5F5>Rare Only</color>";
            case RarityMode.LegendaryOnly:
                return "<color=#FFD54F>Legendary Only</color>";
            default:
                return "<color=#42A5F5>Rare</color> + <color=#FFD54F>Legendary</color>";
        }
    }

    private static string GetRarityModeLabel(RarityMode mode)
    {
        switch (mode)
        {
            case RarityMode.RareOnly:
                return "Rare Only (blue)";
            case RarityMode.LegendaryOnly:
                return "Legendary Only (yellow)";
            default:
                return "Rare + Legendary (blue + yellow)";
        }
    }

    private static void ShowOverlay(string text)
    {
        _overlayText = text;
        _overlayUntil = Time.unscaledTime + 2f;
    }

    private static void ApplyUiPatches()
    {
        var harmony = new Harmony("com.codex.dungeonsettlers.rerollfree.inscription");

        _panelUnitField = AccessTools.Field(
            typeof(EntityStatusPanelUI),
            "NativeFieldInfoPtr__unitEntity");

        MethodBase dataApply = AccessTools.Method(typeof(DataApplier), nameof(DataApplier.Apply));
        MethodBase setUnit = AccessTools.Method(
            typeof(EntityStatusPanelUI),
            nameof(EntityStatusPanelUI.SetUnitUI),
            new[] { typeof(UnitEntity) });
        MethodBase setTrait = AccessTools.Method(
            typeof(TraitSlotUI),
            nameof(TraitSlotUI.SetTrait),
            new[] { typeof(string) });

        if (dataApply == null)
            throw new MissingMethodException("DataApplier.Apply");
        if (setUnit == null)
            throw new MissingMethodException("EntityStatusPanelUI.SetUnitUI(UnitEntity)");
        if (setTrait == null)
            throw new MissingMethodException("TraitSlotUI.SetTrait(string)");

        harmony.Patch(
            dataApply,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(CaptureDataApplier))));
        harmony.Patch(
            setUnit,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(CapturePanelUnit))));
        harmony.Patch(
            setTrait,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(TrackTraitSlot))));

        _instance.Log.LogInfo("Inscription replacement hooks are active.");
    }

    private static void CaptureDataApplier(DataApplier __instance)
    {
        if (__instance != null)
            _dataApplier = __instance;
    }

    private static void CapturePanelUnit(EntityStatusPanelUI __instance, UnitEntity playerUnit)
    {
        if (__instance == null)
            return;

        if (playerUnit == null)
            PanelUnits.Remove(__instance);
        else
            PanelUnits[__instance] = playerUnit;
    }

    private static void TrackTraitSlot(TraitSlotUI __instance, string key)
    {
        if (__instance == null || string.IsNullOrEmpty(key) ||
            !key.EndsWith("Inscription", StringComparison.Ordinal))
            return;

        EntityStatusPanelUI panel = __instance.GetComponentInParent<EntityStatusPanelUI>();
        for (int i = 0; i < TraitSlots.Count; i++)
        {
            if (!ReferenceEquals(TraitSlots[i].Slot, __instance))
                continue;

            TraitSlots[i].Key = key;
            TraitSlots[i].Panel = panel;
            return;
        }

        TraitSlots.Add(new TraitSlotInfo(__instance, key, panel));
        if (_trackedSlotLogCount < 3)
        {
            _trackedSlotLogCount++;
            _instance.Log.LogInfo(
                $"Tracked inscription slot {_trackedSlotLogCount}: {key}, " +
                $"panel={(panel == null ? "<none>" : "found")}");
        }
    }

    private static void ReplaceHoveredInscription()
    {
        if (_autoRerolling)
            return;

        _instance.Log.LogInfo(
            $"L pressed: trackedSlots={TraitSlots.Count}, panelUnits={PanelUnits.Count}, " +
            $"dataApplier={_dataApplier != null}.");

        if (!TryGetHoveredTrait(out TraitSlotInfo slotInfo))
        {
            _instance.Log.LogInfo("L skipped: no tracked inscription is under the mouse.");
            ShowOverlay("Move the mouse over an inscription and press L");
            return;
        }

        EntityStatusPanelUI panel = slotInfo.Panel;
        if (panel == null)
            panel = slotInfo.Slot.GetComponentInParent<EntityStatusPanelUI>();

        UnitEntity unit = GetPanelUnit(panel);
        if (unit == null)
        {
            _instance.Log.LogWarning("L skipped: the hovered panel has no selected unit.");
            ShowOverlay("Open a character inscription panel first");
            return;
        }

        if (_dataApplier == null)
        {
            ShowOverlay("Game data is not ready yet");
            _instance.Log.LogWarning("Inscription replacement skipped: DataApplier has not been observed yet.");
            return;
        }

        IEntity entity = AsEntity(unit);
        if (entity == null)
        {
            ShowOverlay("Could not read the selected unit");
            _instance.Log.LogWarning("Inscription replacement skipped: UnitEntity could not be cast to IEntity.");
            return;
        }

        AffecterReader affecter = EntityComponent.GetReader<AffecterReader>(entity);
        if (affecter == null || !affecter.Has(slotInfo.Key))
        {
            ShowOverlay("Inscription data was refreshed");
            return;
        }

        int stack = affecter.GetStack(slotInfo.Key);
        if (stack <= 0)
        {
            ShowOverlay("Could not find the hovered inscription");
            return;
        }

        string replacementKey = PickReplacement(slotInfo.Key);
        if (replacementKey == null)
        {
            ShowOverlay("No replacement is available for this rarity mode");
            return;
        }

        float duration = affecter.GetDuration(slotInfo.Key);
        if (!TryReplaceAffecter(unit, slotInfo.Key, replacementKey, stack, duration))
            return;

        string oldKey = slotInfo.Key;
        slotInfo.Key = replacementKey;
        RefreshTraitPanel(panel, unit);
        ShowOverlay("Inscription replaced");
        _instance.Log.LogInfo(
            $"Replaced inscription {oldKey} on unit {unit.Guid} with {replacementKey} " +
            $"({GetRarityModeLabel(_rarityMode)}).");
    }

    private static bool TryGetHoveredTrait(out TraitSlotInfo result)
    {
        for (int i = TraitSlots.Count - 1; i >= 0; i--)
        {
            TraitSlotInfo candidate = TraitSlots[i];
            if (candidate.Slot == null || !candidate.Slot.gameObject.activeInHierarchy)
                continue;

            RefreshHitRects(candidate);
            for (int j = candidate.HitRects.Count - 1; j >= 0; j--)
            {
                if (IsMouseOver(candidate.HitRects[j]))
                {
                    result = candidate;
                    return true;
                }
            }
        }

        result = null;
        return false;
    }

    private static void RefreshHitRects(TraitSlotInfo slotInfo)
    {
        slotInfo.HitRects.Clear();
        AddHitRects(slotInfo.Slot.transform, slotInfo.HitRects);
    }

    private static void AddHitRects(Transform transform, List<RectTransform> hitRects)
    {
        if (transform == null)
            return;

        RectTransform rect = transform as RectTransform;
        if (rect != null)
            hitRects.Add(rect);

        for (int i = 0; i < transform.childCount; i++)
            AddHitRects(transform.GetChild(i), hitRects);
    }

    private static bool IsMouseOver(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null &&
                             canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        return RectTransformUtility.RectangleContainsScreenPoint(
            rect,
            Input.mousePosition,
            eventCamera);
    }

    private static UnitEntity GetPanelUnit(EntityStatusPanelUI panel)
    {
        if (panel == null)
            return null;

        if (PanelUnits.TryGetValue(panel, out UnitEntity unit) && unit != null)
            return unit;

        // Generated IL2CPP wrappers expose the native field-info pointer rather
        // than a managed `_unitEntity` field. Use it as a fallback when the
        // SetUnitUI postfix was missed during panel initialization.
        try
        {
            if (_panelUnitField != null)
            {
                IntPtr fieldInfo = (IntPtr)_panelUnitField.GetValue(null);
                if (fieldInfo != IntPtr.Zero)
                {
                    IntPtr unitPointer = IL2CPP.il2cpp_field_get_value_object(
                        fieldInfo,
                        panel.Pointer);
                    if (unitPointer != IntPtr.Zero)
                        return new UnitEntity(unitPointer);
                }
            }

            // EntityStatusPanelUI._unitEntity is at this offset in the current
            // IL2CPP layout. This covers panels initialized before our postfix
            // was installed and wrappers whose generated field-info lookup is
            // unavailable.
            IntPtr nativeUnitPointer = Marshal.ReadIntPtr(
                panel.Pointer,
                PanelUnitFieldOffset);
            return nativeUnitPointer == IntPtr.Zero
                ? null
                : new UnitEntity(nativeUnitPointer);
        }
        catch (Exception ex)
        {
            _instance.Log.LogWarning($"Could not read panel unit fallback: {ex.Message}");
            return null;
        }
    }

    private static string PickReplacement(string oldKey)
    {
        var candidates = new List<string>();
        foreach (string key in TargetInscriptions)
        {
            if (key != oldKey && IsTargetInscription(key))
                candidates.Add(key);
        }

        return candidates.Count == 0 ? null : candidates[ReplacementRandom.Next(candidates.Count)];
    }

    private static bool TryReplaceAffecter(
        UnitEntity unit,
        string oldKey,
        string replacementKey,
        int oldStack,
        float oldDuration)
    {
        bool removed = false;
        IEntity entity = AsEntity(unit);
        try
        {
            if (entity == null)
                throw new InvalidOperationException("UnitEntity could not be cast to IEntity.");

            ApplyAffecter(entity, oldKey, -oldStack, 0f);
            removed = true;

            ApplyAffecter(entity, replacementKey, oldStack, oldDuration);
            return true;
        }
        catch (Exception ex)
        {
            _instance.Log.LogError(
                $"Inscription replacement failed ({oldKey} -> {replacementKey}): {ex}");

            if (removed)
            {
                try
                {
                    ApplyAffecter(entity, oldKey, oldStack, oldDuration);
                }
                catch (Exception restoreEx)
                {
                    _instance.Log.LogError($"Could not restore original inscription {oldKey}: {restoreEx}");
                }
            }

            ShowOverlay("Replacement failed; restoring the original inscription");
            return false;
        }
    }

    private static void ApplyAffecter(IEntity entity, string key, int stack, float duration)
    {
        IApplyData data = AsApplyData(new AffecterApplyData(entity, key, stack, duration, false, entity));
        if (data == null)
            throw new InvalidOperationException("AffecterApplyData could not be cast to IApplyData.");

        _dataApplier.Apply(data, null);
    }

    private static void RefreshTraitPanel(EntityStatusPanelUI panel, UnitEntity unit)
    {
        if (panel == null || unit == null)
            return;

        try
        {
            panel.RefreshAffecters(EntityComponent.GetReader<AffecterReader>(AsEntity(unit)), true);
        }
        catch (Exception ex)
        {
            _instance.Log.LogWarning($"Trait panel refresh failed after replacement: {ex.Message}");
        }
    }

    private static IEntity AsEntity(UnitEntity unit)
    {
        return unit == null ? null : unit.TryCast<IEntity>();
    }

    private static IApplyData AsApplyData(AffecterApplyData data)
    {
        return data == null ? null : data.TryCast<IApplyData>();
    }

    private sealed class TraitSlotInfo
    {
        public readonly TraitSlotUI Slot;
        public readonly List<RectTransform> HitRects = new List<RectTransform>();
        public string Key;
        public EntityStatusPanelUI Panel;

        public TraitSlotInfo(TraitSlotUI slot, string key, EntityStatusPanelUI panel)
        {
            Slot = slot;
            Key = key;
            Panel = panel;
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
            $"Auto-reroll active for rare/legendary inscriptions; max extra rolls={MaxAutoRerolls}, " +
            $"legendary-only={MaxLegendaryAutoRerolls}.");
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
        int maxAutoRerolls = _rarityMode == RarityMode.LegendaryOnly
            ? MaxLegendaryAutoRerolls
            : MaxAutoRerolls;
        try
        {
            if (HasTargetInscription(__instance))
                return;

            while (extraRolls < maxAutoRerolls && !HasTargetInscription(__instance))
            {
                __instance.PrayLevelUp(offeringType, lockInscriptions, lockStats);
                extraRolls++;
            }

            _instance.Log.LogInfo(
                extraRolls == maxAutoRerolls
                    ? $"Auto-reroll stopped at safety limit ({maxAutoRerolls}); no target rarity found."
                    : $"Auto-rerolled {extraRolls} extra time(s) until a " +
                      $"{GetRarityModeLabel(_rarityMode)} inscription appeared.");
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
        private GUIStyle _overlayTextStyle;

        public HotkeyListener(IntPtr ptr) : base(ptr) { }

        public void Update()
        {
            if (_instance != null)
                _instance.HandleHotkey();
        }

        public void OnGUI()
        {
            if (_instance == null || string.IsNullOrEmpty(_overlayText) || Time.unscaledTime > _overlayUntil)
                return;

            if (_overlayTextStyle == null)
            {
                _overlayTextStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                    fontStyle = FontStyle.Bold
                };
            }

            float width = Mathf.Min(460f, Screen.width - 40f);
            Rect rect = new Rect(20f, 80f, width, 62f);
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(rect, _overlayText, _overlayTextStyle);
            GUI.color = previousColor;
        }
    }

    private void ApplyRuntimePatch()
    {
        IntPtr gameAssembly = GetModuleHandle("GameAssembly.dll");
        if (gameAssembly == IntPtr.Zero)
            throw new InvalidOperationException("GameAssembly.dll is not loaded.");

        int branchRva;
        int directRollRva;
        IntPtr branch = FindRerollBranch(gameAssembly, out branchRva, out directRollRva);
        IntPtr directRoll = IntPtr.Add(gameAssembly, directRollRva);

        IntPtr continuation = IntPtr.Add(branch, BranchLength);
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
            $"Branch RVA=0x{branchRva:X}, direct-roll RVA=0x{directRollRva:X}, " +
            $"GameAssembly base=0x{gameAssembly.ToInt64():X}, cave=0x{codeCave.ToInt64():X}.");
    }

    private static IntPtr FindRerollBranch(
        IntPtr gameAssembly,
        out int branchRva,
        out int directRollRva)
    {
        IntPtr dosHeader = gameAssembly;
        int peOffset = Marshal.ReadInt32(IntPtr.Add(dosHeader, 0x3C));
        IntPtr ntHeaders = IntPtr.Add(gameAssembly, peOffset);
        short sectionCount = Marshal.ReadInt16(IntPtr.Add(ntHeaders, 6));
        short optionalHeaderSize = Marshal.ReadInt16(IntPtr.Add(ntHeaders, 20));
        IntPtr sectionTable = IntPtr.Add(ntHeaders, 24 + optionalHeaderSize);
        int imageSize = Marshal.ReadInt32(IntPtr.Add(ntHeaders, 24 + 56));
        const int executable = 0x20000000;

        for (int sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
        {
            IntPtr section = IntPtr.Add(sectionTable, sectionIndex * 40);
            int virtualSize = Marshal.ReadInt32(IntPtr.Add(section, 8));
            int sectionRva = Marshal.ReadInt32(IntPtr.Add(section, 12));
            int rawSize = Marshal.ReadInt32(IntPtr.Add(section, 16));
            int characteristics = Marshal.ReadInt32(IntPtr.Add(section, 36));

            if ((characteristics & executable) == 0)
                continue;

            if (sectionRva < 0 || sectionRva >= imageSize)
                continue;

            int sectionSize = Math.Min(
                Math.Max(virtualSize, rawSize),
                imageSize - sectionRva);
            if (sectionSize < BranchLength)
                continue;

            byte[] code = new byte[sectionSize];
            Marshal.Copy(IntPtr.Add(gameAssembly, sectionRva), code, 0, code.Length);

            for (int offset = 0; offset <= code.Length - BranchLength; offset++)
            {
                if (!MatchesRerollSignature(code, offset))
                    continue;

                int relativeTarget = BitConverter.ToInt32(code, offset + 4);
                long target = (long)sectionRva + offset + 8 + relativeTarget;
                if (target < 0 || target > imageSize - 3)
                    continue;
                int candidateDirectRollRva = (int)target;

                byte[] directRollPrefix = new byte[3];
                Marshal.Copy(
                    IntPtr.Add(gameAssembly, candidateDirectRollRva),
                    directRollPrefix,
                    0,
                    directRollPrefix.Length);
                if (directRollPrefix[0] != 0x45 ||
                    directRollPrefix[1] != 0x33 ||
                    directRollPrefix[2] != 0xC0)
                    continue;

                branchRva = sectionRva + offset;
                directRollRva = candidateDirectRollRva;
                return IntPtr.Add(gameAssembly, branchRva);
            }
        }

        throw new InvalidOperationException(
            "Could not locate the retry-cost branch in GameAssembly.dll; patch refused for safety.");
    }

    private static bool MatchesRerollSignature(byte[] code, int offset)
    {
        return code[offset] == 0x85 &&
               code[offset + 1] == 0xFF &&
               code[offset + 2] == 0x0F &&
               code[offset + 3] == 0x84 &&
               code[offset + 8] == 0x49 &&
               code[offset + 9] == 0x8B &&
               code[offset + 10] == 0x8F &&
               code[offset + 11] == 0xF8 &&
               code[offset + 12] == 0x00 &&
               code[offset + 13] == 0x00 &&
               code[offset + 14] == 0x00;
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
