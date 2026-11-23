using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Construction;
using HarmonyLib;
using RedLoader;
using Sons.Atmosphere;
using Sons.Gameplay;
using Sons.Interfaces;
using Sons.Item;
using Sons.TerrainDetail;
using Sons.Weapon;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Player.Actions;
using TheForest.Utils;
using TheForest.World;
using UnityEngine;

namespace RainExtinguishesFires;

internal enum PrecipitationTier
{
    None,
    Light,
    Medium,
    Heavy
}

internal enum FuelKind
{
    Other,
    Firewood,
    BonfireLog,
    Leaves
}

internal enum HandheldKind
{
    Torch,
    Molotov,
    FireArrow
}

public class RainExtinguishesFiresMod : SonsMod, IOnUpdateReceiver
{
    private const float UpdateInterval = 1f;
    private const float FireCheckInterval = 1f;
    private const int FireChecksPerFrame = 4;
    private const float MaxExposureStep = 2f;
    private const float FallbackFireScanInterval = 30f;
    private const float FallbackFireArrowScanInterval = 2f;
    private const float RoofCheckDistance = 12f;
    private const float RaycastUpOffset = 0.35f;
    private const float StoneRingRefreshInterval = 10f;
    private const float MinimumHandheldTimer = 0.5f;
    private const float PrecipitationClearDelay = 20f;
    private const int SmokeBurstCount = 30;
    private const float SmokeLifetime = 8f;
    private const float BlockedMessageCooldown = 1.5f;
    private const float CanopyRefreshInterval = 10f;
    private const float CanopyRadius = 5f;
    private const float SprinklerResetDelay = 0.5f;
    private const float IgnitionCommitWindow = 8f;
    private const float EmberMaxStartSize = 0.15f;
    private const float EmberEmissionScale = 0.6f;
    private const float BlockedMessageDuration = 2.5f;
    private const float FuelRefreshInterval = 10f;
    private const float LeafStokeMinimumCooldown = 5f;
    private const float WindCheckDistance = 10f;
    private const float WindRayElevation = 0.6f;
    private const float MinimumWindIntensity = 0.05f;
    private const float SizzleVolume = 1.3f;
    private const int MaxLoggedNames = 32;
    private const int FullSmallFirewoodPieces = 4;
    private const int BonfireLayerOnePieces = 2;
    private const int BonfireLayerTwoPieces = 6;
    private const float TarpRefreshInterval = 5f;
    private const float TarpSearchHeight = 2f;
    private const float TarpSearchRadius = 3.5f;
    private const float TarpEdgeMargin = 0.25f;
    private static readonly string[] EmberParticleTokens = { "ember", "spark", "glow", "coal" };
    private static readonly string[] SizzleExcludedTokens = { "tree", "metal", "gun", "mutant", "player", "molotov", "torch", "arrow", "flare", "propogate", "extinguish", "whoosh", "shoosh", "quench", "flamer", "alarm", "craft" };
    private static readonly Dictionary<int, float> TorchExposureTimes = new();
    private static readonly Dictionary<int, float> FireArrowExposureTimes = new();
    private static readonly Dictionary<int, float> MolotovExposureTimes = new();
    private static readonly HashSet<int> ActiveTorchIds = new();
    private static readonly HashSet<int> ActiveFireArrowIds = new();
    private static readonly HashSet<int> ActiveMolotovIds = new();
    private static readonly List<int> StaleExposureIds = new();
    private static readonly HashSet<int> WetFires = new();
    private static readonly Dictionary<int, float> PendingWetFires = new();
    private static readonly List<int> ReadyWetFireIds = new();
    private static readonly HashSet<HandheldKind> WetHandhelds = new();
    private static readonly Dictionary<HandheldKind, float> PendingWetHandhelds = new();
    private static readonly List<HandheldKind> ReadyWetHandhelds = new();
    private static readonly HashSet<string> LoggedSmokeMisses = new();
    private static readonly HashSet<string> LoggedFuelNames = new();
    private static readonly List<SprinklerReset> SprinklerResets = new();
    private static readonly Dictionary<int, float> FireIgnitionAllowances = new();
    private static readonly Dictionary<int, TarpCheck> TarpChecks = new();
    private static readonly HashSet<int> TarpCandidateIds = new();
    private static readonly Dictionary<HandheldKind, float> HandheldIgnitionAllowances = new();
    private static readonly HashSet<string> LoggedParticleNames = new();
    private static string _lastBlockedMessage = string.Empty;
    private static GameObject? _torchWarmRoot;
    private static readonly Dictionary<int, WarmEffect> WarmEffects = new();
    private static readonly List<int> WarmEffectIds = new();
    private static readonly ComponentRegistry<CookingFireNew> Fires = new();
    private static readonly ComponentRegistry<FireArrowController> FireArrows = new();
    private static HarmonyLib.Harmony? _harmony;
    private static bool _fireHooksActive;
    private static bool _fireArrowHooksActive;
    private static bool _fireHooksObserved;
    private static bool _fireArrowHooksObserved;
    private static bool _fireScanFallback;
    private static bool _fireArrowScanFallback;
    private static float _fallbackFireScanElapsed;
    private static float _fallbackFireArrowScanElapsed;
    private static bool _fallbackFireScanPending = true;
    private static bool _fallbackFireArrowScanPending = true;
    private static int _fireCursor;
    private static bool _gameStarted;
    private static bool _badWeather;
    private static bool _wasBadWeather;
    private static bool _isAuthority = true;
    private static float _dryElapsed = PrecipitationClearDelay;
    private static PrecipitationTier _tier;
    private static float _lastBlockedMessageTime = -100f;
    private static bool _stoneRingWarningLogged;
    private static bool _smokeWarningLogged;
    private static bool _ignitionWarningLogged;
    private static bool _messageWarningLogged;
    private static bool _warmEffectWarningLogged;
    private static bool _fuelWarningLogged;
    private static bool _canopyWarningLogged;
    private static bool _treeManagerMissingLogged;
    private static bool _windWarningLogged;
    private static bool _sizzlePathResolved;
    private static string? _sizzleEventPath;
    private static ConfigEntry<bool> _campfireEnabled = null!;
    private static ConfigEntry<float> _fireTimer = null!;
    private static ConfigEntry<bool> _playExtinguishSound = null!;
    private static ConfigEntry<bool> _smokeEffectEnabled = null!;
    private static ConfigEntry<bool> _stoneRingEnabled = null!;
    private static ConfigEntry<float> _stoneRingMultiplier = null!;
    private static ConfigEntry<bool> _useWeatherTiers = null!;
    private static ConfigEntry<float> _lightTimer = null!;
    private static ConfigEntry<float> _mediumTimer = null!;
    private static ConfigEntry<float> _heavyTimer = null!;
    private static ConfigEntry<bool> _scaleHandheldsWithTiers = null!;
    private static ConfigEntry<bool> _preventRelightInRain = null!;
    private static ConfigEntry<float> _warmGracePeriod = null!;
    private static ConfigEntry<bool> _blockIgnitionInHeavyWeather = null!;
    private static ConfigEntry<bool> _canopyProtectionEnabled = null!;
    private static ConfigEntry<float> _canopyMultiplier = null!;
    private static ConfigEntry<bool> _windFactorEnabled = null!;
    private static ConfigEntry<bool> _firewoodBonusEnabled = null!;
    private static ConfigEntry<float> _firewoodMultiplier = null!;
    private static ConfigEntry<float> _stoneRingFirewoodMultiplier = null!;
    private static ConfigEntry<bool> _logLayerBonusEnabled = null!;
    private static ConfigEntry<float> _oneLogLayerMultiplier = null!;
    private static ConfigEntry<float> _stoneRingOneLogLayerMultiplier = null!;
    private static ConfigEntry<float> _twoLogLayersMultiplier = null!;
    private static ConfigEntry<float> _stoneRingTwoLogLayersMultiplier = null!;
    private static ConfigEntry<bool> _stoneRingBonfireNeverExtinguishes = null!;
    private static ConfigEntry<bool> _wetFirewoodPenaltyEnabled = null!;
    private static ConfigEntry<bool> _leafStokeEnabled = null!;
    private static ConfigEntry<float> _leafStokeSeconds = null!;
    private static ConfigEntry<bool> _rainSicknessEnabled = null!;
    private static ConfigEntry<float> _rainSicknessTimer = null!;
    private static ConfigEntry<bool> _torchEnabled = null!;
    private static ConfigEntry<float> _torchTimer = null!;
    private static ConfigEntry<bool> _fireArrowsEnabled = null!;
    private static ConfigEntry<float> _fireArrowTimer = null!;
    private static ConfigEntry<bool> _molotovEnabled = null!;
    private static ConfigEntry<bool> _preventHandheldRelightInRain = null!;
    private static ConfigEntry<float> _molotovTimer = null!;
    private static string? _extinguishSoundPath;
    private static FMOD.System _fmodCoreSystem;
    private static FMOD.Sound _extinguishSound;
    private static bool _extinguishSoundLoaded;
    private static bool _extinguishSoundFailed;
    private static bool _fmodWarningLogged;
    private static float _rainSicknessExposure;
    private bool _hasLoggedUpdate;
    private float _updateAccumulator;

    protected override void OnInitializeMod()
    {
        InitializeConfig();
        InitializeAudio();
        InitializeHooks();
        RLog.Msg("[RainExtinguishesFires] Mod initialized.");
    }

    protected override void OnSonsSceneInitialized(ESonsScene sonsScene)
    {
        if (sonsScene == ESonsScene.Game)
        {
            return;
        }

        _gameStarted = false;
        ClearWetFires();
        ClearWetHandhelds();
        SprinklerResets.Clear();
        FireIgnitionAllowances.Clear();
        HandheldIgnitionAllowances.Clear();
        TarpChecks.Clear();
    }

    protected override void OnGameStart()
    {
        _gameStarted = true;
        _fireCursor = 0;
        _wasBadWeather = false;
        _fallbackFireScanPending = true;
        _fallbackFireArrowScanPending = true;
        ClearWetFires();
        ClearWetHandhelds();
        SprinklerResets.Clear();
        FireIgnitionAllowances.Clear();
        HandheldIgnitionAllowances.Clear();
        TarpChecks.Clear();
        _dryElapsed = PrecipitationClearDelay;
        Fires.PruneDestroyed();
        FireArrows.PruneDestroyed();
        VerifyFireHooks();
        if (_playExtinguishSound.Value)
        {
            TryLoadExtinguishSound();
        }
    }

    public void OnUpdate()
    {
        if (!_hasLoggedUpdate)
        {
            RLog.Msg("[RainExtinguishesFires] Live update callback active.");
            _hasLoggedUpdate = true;
        }

        _updateAccumulator += Time.deltaTime;
        if (_updateAccumulator >= UpdateInterval)
        {
            float elapsed = _updateAccumulator;
            _updateAccumulator = 0f;
            UpdateLiveState(elapsed);
        }

        UpdateFireSlice(Time.time);
        if (SprinklerResets.Count > 0)
        {
            ProcessSprinklerResets(Time.time);
        }
    }

    private static void InitializeConfig()
    {
        ConfigCategory campfires = CreateCategory("Campfires", "Campfires & Fires");
        ConfigCategory weatherTiers = CreateCategory("WeatherIntensity", "Weather Intensity Tiers");
        ConfigCategory wetFires = CreateCategory("WetFires", "Wet Fires & Relighting");
        ConfigCategory shelter = CreateCategory("Shelter", "Shelter & Wind");
        ConfigCategory fuel = CreateCategory("Fuel", "Fuel & Firewood");
        ConfigCategory rainSickness = CreateCategory("RainSickness", "Rain Sickness");
        ConfigCategory handheld = CreateCategory("HandheldFlameItems", "Handheld Flame Items");

        _campfireEnabled = CreateEntry(campfires, "EnableCampfireExtinguishing", "Enable Campfire Extinguishing", true);
        _fireTimer = CreateEntry(campfires, "FireExtinguishTimer", "Fire Extinguish Timer", 5f, 1f, 60f);
        _playExtinguishSound = CreateEntry(campfires, "PlayExtinguishSound", "Play Extinguish Sound", true);
        _smokeEffectEnabled = CreateEntry(campfires, "ExtinguishSmokeEffect", "Extinguish Smoke Effect", true);
        _stoneRingEnabled = CreateEntry(campfires, "StoneRingBonus", "Stone Ring Bonus", true);
        _stoneRingMultiplier = CreateEntry(campfires, "StoneRingTimerMultiplier", "Stone Ring Timer Multiplier", 1.5f, 1f, 3f);

        _useWeatherTiers = CreateEntry(weatherTiers, "UseWeatherIntensityTiers", "Use Weather Intensity Tiers", false);
        _lightTimer = CreateEntry(weatherTiers, "LightPrecipitationTimer", "Light Precipitation Timer", 15f, 1f, 60f);
        _mediumTimer = CreateEntry(weatherTiers, "MediumPrecipitationTimer", "Medium Precipitation Timer", 5f, 1f, 60f);
        _heavyTimer = CreateEntry(weatherTiers, "HeavyPrecipitationTimer", "Heavy Precipitation Timer", 2f, 1f, 60f);
        _scaleHandheldsWithTiers = CreateEntry(weatherTiers, "ScaleHandheldsWithWeatherTiers", "Scale Handhelds With Weather Tiers", true, "Only takes effect when UseWeatherIntensityTiers is on.");

        _preventRelightInRain = CreateEntry(wetFires, "PreventRelightInRain", "Prevent Relight In Rain", false);
        _warmGracePeriod = CreateEntry(wetFires, "LightRainWarmGracePeriod", "Light Rain Warm Grace Period", 3f, 0f, 15f);
        _blockIgnitionInHeavyWeather = CreateEntry(wetFires, "BlockIgnitionInHeavyWeather", "Block Ignition In Heavy Weather", false);

        _canopyProtectionEnabled = CreateEntry(shelter, "TreeCanopyProtection", "Tree Canopy Protection", true);
        _canopyMultiplier = CreateEntry(shelter, "CanopyResistanceMultiplier", "Canopy Resistance Multiplier", 1.5f, 1f, 3f);
        _windFactorEnabled = CreateEntry(shelter, "WindDirectionFactor", "Wind Direction Factor", false);

        _firewoodBonusEnabled = CreateEntry(fuel, "FirewoodResistanceBonus", "Firewood Resistance Bonus", true);
        _firewoodMultiplier = CreateEntry(fuel, "FirewoodResistanceMultiplier", "Firewood Resistance Multiplier", 2f, 1f, 10f);
        _stoneRingFirewoodMultiplier = CreateEntry(fuel, "StoneRingFirewoodMultiplier", "Stone Ring Firewood Multiplier", 3f, 1f, 10f);
        _logLayerBonusEnabled = CreateEntry(fuel, "LogLayerBonus", "Log Layer Bonus", true);
        _oneLogLayerMultiplier = CreateEntry(fuel, "OneLogLayerMultiplier", "One Log Layer Multiplier", 3.5f, 1f, 10f);
        _stoneRingOneLogLayerMultiplier = CreateEntry(fuel, "StoneRingOneLogLayerMultiplier", "Stone Ring One Log Layer Multiplier", 4f, 1f, 10f);
        _twoLogLayersMultiplier = CreateEntry(fuel, "TwoLogLayersMultiplier", "Two Log Layers Multiplier", 5f, 1f, 10f);
        _stoneRingBonfireNeverExtinguishes = CreateEntry(fuel, "StoneRingBonfireNeverExtinguishes", "Stone Ring Bonfire Never Extinguishes", true);
        _stoneRingTwoLogLayersMultiplier = CreateEntry(fuel, "StoneRingTwoLogLayersMultiplier", "Stone Ring Two Log Layers Multiplier", 6f, 1f, 10f, "Only takes effect when StoneRingBonfireNeverExtinguishes is off.");
        _wetFirewoodPenaltyEnabled = CreateEntry(fuel, "WetFirewoodPenalty", "Wet Firewood Penalty", false);
        _leafStokeEnabled = CreateEntry(fuel, "LeafStokeExtension", "Leaf Stoke Extension", false);
        _leafStokeSeconds = CreateEntry(fuel, "LeafStokeSeconds", "Leaf Stoke Seconds", 1f, 0.5f, 5f);

        _rainSicknessEnabled = CreateEntry(rainSickness, "EnableRainSickness", "Enable Rain Sickness", false);
        _rainSicknessTimer = CreateEntry(rainSickness, "RainSicknessTimer", "Rain Sickness Timer", 60f, 10f, 300f);

        _torchEnabled = CreateEntry(handheld, "ExtinguishHandheldTorch", "Extinguish Handheld Torch", false);
        _torchTimer = CreateEntry(handheld, "HandheldTorchTimer", "Handheld Torch Timer", 5f, 1f, 30f);
        _fireArrowsEnabled = CreateEntry(handheld, "ExtinguishFireArrows", "Extinguish Fire Arrows", false);
        _fireArrowTimer = CreateEntry(handheld, "FireArrowTimer", "Fire Arrow Timer", 5f, 1f, 30f);
        _molotovEnabled = CreateEntry(handheld, "ExtinguishHeldMolotovs", "Extinguish Held Molotovs", false);
        _molotovTimer = CreateEntry(handheld, "HeldMolotovTimer", "Held Molotov Timer", 3f, 1f, 30f);
        _preventHandheldRelightInRain = CreateEntry(handheld, "PreventHandheldRelightInRain", "Prevent Handheld Relight In Rain", false);

        campfires.LoadFromFile(false);
        weatherTiers.LoadFromFile(false);
        wetFires.LoadFromFile(false);
        shelter.LoadFromFile(false);
        fuel.LoadFromFile(false);
        rainSickness.LoadFromFile(false);
        handheld.LoadFromFile(false);
    }

    private static ConfigCategory CreateCategory(string id, string displayName)
    {
        string fileName = Path.Combine("RainExtinguishesFires", $"{id}.cfg");
        string? configDirectory = Path.GetDirectoryName(ConfigSystem.GetFilePath(fileName));
        if (configDirectory != null)
        {
            Directory.CreateDirectory(configDirectory);
        }
        return ConfigSystem.CreateFileCategory($"RainExtinguishesFires_{id}", displayName, fileName);
    }

    private static ConfigEntry<T> CreateEntry<T>(ConfigCategory category, string id, string displayName, T defaultValue, string? description = null)
    {
        return category.CreateEntry(id, defaultValue, displayName, description ?? displayName);
    }

    private static ConfigEntry<float> CreateEntry(ConfigCategory category, string id, string displayName, float defaultValue, float minimum, float maximum, string? description = null)
    {
        var entry = category.CreateEntry(id, defaultValue, displayName, description ?? displayName);
        entry.SetRange(minimum, maximum);
        return entry;
    }

    private static void InitializeHooks()
    {
        try
        {
            _harmony = new HarmonyLib.Harmony("Zemo.RainExtinguishesFires");
        }
        catch (Exception exception)
        {
            RLog.Warning($"[RainExtinguishesFires] Harmony unavailable, using periodic scans and disabling relight blocking: {exception.Message}");
            _fireScanFallback = true;
            _fireArrowScanFallback = true;
            return;
        }

        _fireHooksActive = TryPatchLifecycle(typeof(CookingFireNew), nameof(OnFireEnabled), nameof(OnFireDisabled));
        _fireArrowHooksActive = TryPatchLifecycle(typeof(FireArrowController), nameof(OnFireArrowEnabled), nameof(OnFireArrowDisabled));
        _fireScanFallback = !_fireHooksActive;
        _fireArrowScanFallback = !_fireArrowHooksActive;

        int ignitionHooks = 0;
        if (TryPatchPrefix(typeof(PlayerLightFireAction), "LightFire", nameof(BeforePlayerLightFire)))
        {
            ignitionHooks++;
        }
        if (TryPatchPrefix(typeof(PlasmaLighterController), "LightFire", nameof(BeforeLighterLightFire)))
        {
            ignitionHooks++;
        }
        if (TryPatchPrefix(typeof(CookingFireNew), "Sons_Interfaces_ILightableFire_LightFire", nameof(BeforeFireIgnition)))
        {
            ignitionHooks++;
        }
        if (TryPatchPrefix(typeof(CookingFireNew), "LightFireAction", nameof(BeforeFireIgnition)))
        {
            ignitionHooks++;
        }
        if (TryPatchPrefix(typeof(CookingFireNew), "Burn", nameof(BeforeFireIgnition)))
        {
            ignitionHooks++;
        }

        int handheldHooks = 0;
        if (TryPatchPrefix(typeof(TorchWeaponController), "OnLightHeldItem", nameof(BeforeTorchIgnition)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(TorchWeaponController), "Burn", nameof(BeforeTorchBurn)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(MolotovWeaponController), "OnLightHeldItem", nameof(BeforeMolotovIgnition)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(MolotovWeaponController), "Burn", nameof(BeforeMolotovBurn)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(FireArrowController), "Burn", nameof(BeforeFireArrowIgnition)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(PlasmaLighterController), "LightHeldItem", nameof(BeforeLighterLightHeldItem)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(PlasmaLighterController), "OnLightHeldItem", nameof(BeforeLighterLightHeldItem)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(BowWeaponController), "OnLightHeldItem", nameof(BeforeBowLightHeldItem)))
        {
            handheldHooks++;
        }

        bool fuelHook = TryPatchPostfix(typeof(CookingFireNew), "RegisterFuelSource", nameof(OnFuelSourceRegistered));

        RLog.Msg($"[RainExtinguishesFires] Fire tracking hooks: fires={_fireHooksActive}, fireArrows={_fireArrowHooksActive}, ignition={ignitionHooks}/5, handheldIgnition={handheldHooks}/8, fuel={fuelHook}.");
    }

    private static bool TryPatchLifecycle(Type targetType, string enabledPostfix, string disabledPostfix)
    {
        if (_harmony == null)
        {
            return false;
        }

        try
        {
            MethodInfo? onEnable = AccessTools.Method(targetType, "OnEnable");
            MethodInfo? onDisable = AccessTools.Method(targetType, "OnDisable");
            if (onEnable == null || onDisable == null)
            {
                RLog.Warning($"[RainExtinguishesFires] Could not find OnEnable/OnDisable on {targetType.Name}, using periodic scans instead.");
                return false;
            }

            _harmony.Patch(onEnable, postfix: new HarmonyMethod(typeof(RainExtinguishesFiresMod), enabledPostfix));
            _harmony.Patch(onDisable, postfix: new HarmonyMethod(typeof(RainExtinguishesFiresMod), disabledPostfix));
            return true;
        }
        catch (Exception exception)
        {
            RLog.Warning($"[RainExtinguishesFires] Could not hook {targetType.Name}, using periodic scans instead: {exception.Message}");
            return false;
        }
    }

    private static bool TryPatchPrefix(Type targetType, string methodName, string prefixName)
    {
        if (_harmony == null)
        {
            return false;
        }

        try
        {
            MethodInfo? target = AccessTools.Method(targetType, methodName);
            if (target == null)
            {
                RLog.Warning($"[RainExtinguishesFires] Could not find {targetType.Name}.{methodName}, relight blocking will skip it.");
                return false;
            }

            _harmony.Patch(target, prefix: new HarmonyMethod(typeof(RainExtinguishesFiresMod), prefixName));
            return true;
        }
        catch (Exception exception)
        {
            RLog.Warning($"[RainExtinguishesFires] Could not hook {targetType.Name}.{methodName}, relight blocking will skip it: {exception.Message}");
            return false;
        }
    }

    private static bool TryPatchPostfix(Type targetType, string methodName, string postfixName)
    {
        if (_harmony == null)
        {
            return false;
        }

        try
        {
            MethodInfo? target = AccessTools.Method(targetType, methodName);
            if (target == null)
            {
                RLog.Warning($"[RainExtinguishesFires] Could not find {targetType.Name}.{methodName}, leaf stoking and wet firewood will use periodic checks only.");
                return false;
            }

            _harmony.Patch(target, postfix: new HarmonyMethod(typeof(RainExtinguishesFiresMod), postfixName));
            return true;
        }
        catch (Exception exception)
        {
            RLog.Warning($"[RainExtinguishesFires] Could not hook {targetType.Name}.{methodName}, leaf stoking and wet firewood will use periodic checks only: {exception.Message}");
            return false;
        }
    }

    private static void OnFireEnabled(CookingFireNew __instance)
    {
        _fireHooksObserved = true;
        Fires.Add(__instance);
    }

    private static void OnFireDisabled(CookingFireNew __instance)
    {
        _fireHooksObserved = true;
        if (__instance == null)
        {
            return;
        }

        TarpChecks.Remove(__instance.GetInstanceID());
        Fires.Remove(__instance);
    }

    private static void OnFireArrowEnabled(FireArrowController __instance)
    {
        _fireArrowHooksObserved = true;
        FireArrows.Add(__instance);
    }

    private static void OnFireArrowDisabled(FireArrowController __instance)
    {
        _fireArrowHooksObserved = true;
        FireArrows.Remove(__instance);
    }

    private static bool BeforePlayerLightFire(ILightableFire lightableFire)
    {
        return AllowFireIgnitionStart(lightableFire);
    }

    private static bool BeforeLighterLightFire(ILightableFire fire)
    {
        return AllowFireIgnitionStart(fire);
    }

    private static bool AllowFireIgnitionStart(ILightableFire lightableFire)
    {
        try
        {
            if (lightableFire == null)
            {
                return true;
            }

            CookingFireNew? fire = lightableFire.TryCast<CookingFireNew>();
            if (fire == null)
            {
                return true;
            }

            int id = fire.GetInstanceID();
            if (HasFireAllowance(id))
            {
                return true;
            }

            if (ShouldBlockIgnition(fire))
            {
                return false;
            }

            FireIgnitionAllowances[id] = Time.time + IgnitionCommitWindow;
            return true;
        }
        catch (Exception exception)
        {
            LogIgnitionWarning(exception);
            return true;
        }
    }

    private static bool BeforeFireIgnition(CookingFireNew __instance)
    {
        try
        {
            if (__instance == null || HasFireAllowance(__instance.GetInstanceID()))
            {
                return true;
            }

            return !ShouldBlockIgnition(__instance);
        }
        catch (Exception exception)
        {
            LogIgnitionWarning(exception);
            return true;
        }
    }

    private static bool HasFireAllowance(int id)
    {
        if (!FireIgnitionAllowances.TryGetValue(id, out float until))
        {
            return false;
        }

        if (Time.time <= until)
        {
            return true;
        }

        FireIgnitionAllowances.Remove(id);
        return false;
    }

    private static bool BeforeTorchIgnition()
    {
        return AllowHandheldIgnition(HandheldKind.Torch, true);
    }

    private static bool BeforeMolotovIgnition()
    {
        return AllowHandheldIgnition(HandheldKind.Molotov, true);
    }

    private static bool BeforeTorchBurn()
    {
        return AllowHandheldIgnition(HandheldKind.Torch, false);
    }

    private static bool BeforeMolotovBurn()
    {
        return AllowHandheldIgnition(HandheldKind.Molotov, false);
    }

    private static bool BeforeFireArrowIgnition()
    {
        return AllowHandheldIgnition(HandheldKind.FireArrow, false);
    }

    private static bool BeforeBowLightHeldItem()
    {
        return AllowHandheldIgnition(HandheldKind.FireArrow, true);
    }

    private static bool BeforeLighterLightHeldItem()
    {
        try
        {
            if (!_gameStarted || !_preventHandheldRelightInRain.Value)
            {
                return true;
            }

            GetHeldFlameKinds(out bool torch, out bool molotov);
            bool fireArrow = !torch && !molotov;
            bool allowed = true;
            if (torch)
            {
                allowed &= AllowHandheldIgnition(HandheldKind.Torch, true);
            }
            if (allowed && molotov)
            {
                allowed &= AllowHandheldIgnition(HandheldKind.Molotov, true);
            }
            if (allowed && fireArrow)
            {
                allowed &= AllowHandheldIgnition(HandheldKind.FireArrow, true);
            }
            return allowed;
        }
        catch (Exception exception)
        {
            LogIgnitionWarning(exception);
            return true;
        }
    }

    private static void GetHeldFlameKinds(out bool torch, out bool molotov)
    {
        torch = false;
        molotov = false;
        var inventory = LocalPlayer.Inventory;
        if (inventory == null)
        {
            return;
        }

        GameObject? left = inventory.LeftHandItem == null ? null : inventory.LeftHandItem.ItemObject;
        GameObject? right = inventory.RightHandItem == null ? null : inventory.RightHandItem.ItemObject;
        foreach (GameObject? item in new[] { left, right })
        {
            if (item == null)
            {
                continue;
            }

            torch |= item.GetComponentInChildren<TorchWeaponController>(true) != null;
            molotov |= item.GetComponentInChildren<MolotovWeaponController>(true) != null;
        }
    }

    private static bool AllowHandheldIgnition(HandheldKind kind, bool isStart)
    {
        try
        {
            if (!_gameStarted || !_preventHandheldRelightInRain.Value)
            {
                return true;
            }

            if (HandheldIgnitionAllowances.TryGetValue(kind, out float until))
            {
                if (Time.time <= until)
                {
                    return true;
                }

                HandheldIgnitionAllowances.Remove(kind);
            }

            if (!WetHandhelds.Contains(kind) || IsPlayerSheltered())
            {
                if (isStart)
                {
                    HandheldIgnitionAllowances[kind] = Time.time + IgnitionCommitWindow;
                }
                return true;
            }

            ShowBlockedMessage(kind switch
            {
                HandheldKind.Torch => "Your torch is soaked. Find shelter or wait for the weather to clear.",
                HandheldKind.Molotov => "The molotov rag is soaked. Find shelter or wait for the weather to clear.",
                _ => "Your fire arrows are soaked. Find shelter or wait for the weather to clear."
            });
            return false;
        }
        catch (Exception exception)
        {
            LogIgnitionWarning(exception);
            return true;
        }
    }

    private static bool IsPlayerSheltered()
    {
        GameObject player = LocalPlayer.PlayerBase;
        if (player == null)
        {
            return true;
        }

        Transform playerCamera = LocalPlayer.MainCamTr;
        Vector3 position = playerCamera != null ? playerCamera.position : player.transform.position;
        return HasRoofOverhead(position);
    }

    private static void LogIgnitionWarning(Exception exception)
    {
        if (_ignitionWarningLogged)
        {
            return;
        }

        _ignitionWarningLogged = true;
        RLog.Warning($"[RainExtinguishesFires] Ignition check failed, allowing ignition: {exception.Message}");
    }

    private static bool ShouldBlockIgnition(CookingFireNew fire)
    {
        if (!_gameStarted)
        {
            return false;
        }

        bool preventWet = _preventRelightInRain.Value && WetFires.Count > 0;
        bool blockHeavy = _blockIgnitionInHeavyWeather.Value && _badWeather && _tier == PrecipitationTier.Heavy;
        if (!preventWet && !blockHeavy)
        {
            return false;
        }

        int id = fire.GetInstanceID();
        bool wet = preventWet && WetFires.Contains(id);
        if (!wet && !blockHeavy)
        {
            return false;
        }

        if (IsFireSheltered(fire))
        {
            WetFires.Remove(id);
            RemovePendingWetFire(id);
            return false;
        }

        ShowBlockedMessage(wet ? "The wood is soaked. Find shelter or wait for the weather to clear." : "The rain is too heavy to light a fire out here.");
        return true;
    }

    private static void ShowBlockedMessage(string message)
    {
        float now = Time.time;
        if (now - _lastBlockedMessageTime < BlockedMessageCooldown && message == _lastBlockedMessage)
        {
            return;
        }

        _lastBlockedMessageTime = now;
        _lastBlockedMessage = message;
        RLog.Msg($"[RainExtinguishesFires] Blocked ignition: {message}");
        try
        {
            SonsTools.ShowMessage(message, BlockedMessageDuration);
        }
        catch (Exception exception)
        {
            if (!_messageWarningLogged)
            {
                _messageWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Could not show HUD message: {exception.Message}");
            }
        }
    }

    private static void VerifyFireHooks()
    {
        if (!_fireHooksActive || _fireScanFallback || _fireHooksObserved)
        {
            return;
        }

        CookingFireNew[] found = UnityEngine.Object.FindObjectsByType<CookingFireNew>(FindObjectsSortMode.None);
        int added = Fires.Sync(found);
        if (_fireHooksObserved)
        {
            return;
        }

        if (added > 0)
        {
            _fireScanFallback = true;
            RLog.Warning("[RainExtinguishesFires] Fire hooks are not reporting fires, switching to periodic scans.");
        }
    }

    private static void VerifyFireArrowHooks()
    {
        if (!_fireArrowHooksActive || _fireArrowScanFallback || _fireArrowHooksObserved)
        {
            return;
        }

        FireArrowController[] found = UnityEngine.Object.FindObjectsByType<FireArrowController>(FindObjectsSortMode.None);
        int added = FireArrows.Sync(found);
        if (_fireArrowHooksObserved)
        {
            return;
        }

        if (added > 0)
        {
            _fireArrowScanFallback = true;
            RLog.Warning("[RainExtinguishesFires] Fire arrow hooks are not reporting arrows, switching to periodic scans.");
        }
    }

    private static void InitializeAudio()
    {
        string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(assemblyDirectory, "extinguish.wav"),
            Path.Combine(assemblyDirectory, "RainExtinguishesFires", "extinguish.wav")
        };

        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            _extinguishSoundPath = candidate;
            RLog.Msg($"[RainExtinguishesFires] Found extinguish sound at '{candidate}'.");
            break;
        }

        if (_extinguishSoundPath == null)
        {
            RLog.Warning("[RainExtinguishesFires] extinguish.wav was not found beside the mod DLL or in its subfolder.");
        }
    }

    private static bool TryLoadExtinguishSound()
    {
        if (_extinguishSoundLoaded)
        {
            return true;
        }

        if (_extinguishSoundPath == null || _extinguishSoundFailed)
        {
            return false;
        }

        if (!FMOD_StudioSystem.TryGetInstance(out FMOD_StudioSystem studioSystem))
        {
            LogFmodWarning("FMOD CoreSystem was not ready when the extinguish sound was requested.");
            return false;
        }

        _fmodCoreSystem = studioSystem.CoreSystem;
        FMOD.RESULT result = _fmodCoreSystem.createSound(
            _extinguishSoundPath,
            FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D,
            out _extinguishSound);
        if (result != FMOD.RESULT.OK)
        {
            _extinguishSoundFailed = true;
            LogFmodWarning($"Could not load WAV (FMOD result: {result}).");
            return false;
        }

        _extinguishSoundLoaded = true;
        RLog.Msg("[RainExtinguishesFires] Extinguish sound loaded through FMOD in 2D mode.");
        return true;
    }

    private static void LogFmodWarning(string message)
    {
        if (!_fmodWarningLogged)
        {
            RLog.Warning($"[RainExtinguishesFires] {message}");
            _fmodWarningLogged = true;
        }
    }

    private static void UpdateLiveState(float deltaTime)
    {
        float now = Time.time;
        WeatherSystem weather = WeatherSystem.Instance;
        _badWeather = weather != null && (weather.IsRaining || weather.IsSnowing);
        _isAuthority = BoltNetwork.isServerOrNotRunning;
        bool weatherStarted = _badWeather && !_wasBadWeather;
        bool weatherEnded = !_badWeather && _wasBadWeather;
        _wasBadWeather = _badWeather;

        if (_badWeather)
        {
            _tier = ReadPrecipitationTier(weather!);
            _dryElapsed = 0f;
        }
        else
        {
            _tier = PrecipitationTier.None;
            _dryElapsed += deltaTime;
        }

        if (weatherEnded)
        {
            Fires.ResetExposure();
        }

        UpdateWetLocks(now);
        CleanupDestroyedWarmFires();

        if (FireExtinguishingEnabled() && _badWeather)
        {
            if (weatherStarted)
            {
                VerifyFireHooks();
            }

            if (_fireScanFallback)
            {
                RefreshFallbackFires(deltaTime);
            }
        }
        else
        {
            _fallbackFireScanPending = true;
            _fallbackFireScanElapsed = 0f;
        }

        bool handheldEnabled = _torchEnabled.Value || _fireArrowsEnabled.Value || _molotovEnabled.Value;
        bool needsPlayerExposure = _rainSicknessEnabled.Value || handheldEnabled;
        bool playerSheltered = false;
        bool playerExposed = false;
        if (_badWeather && needsPlayerExposure)
        {
            GameObject player = LocalPlayer.PlayerBase;
            if (player != null)
            {
                Transform playerCamera = LocalPlayer.MainCamTr;
                Vector3 exposurePosition = playerCamera != null ? playerCamera.position : player.transform.position;
                playerSheltered = HasRoofOverhead(exposurePosition);
                playerExposed = !playerSheltered;
            }
        }

        UpdateRainSickness(playerExposed, playerSheltered, deltaTime);

        if (handheldEnabled && playerExposed)
        {
            UpdateHandheldItems(true, deltaTime);
        }
        else
        {
            TorchExposureTimes.Clear();
            FireArrowExposureTimes.Clear();
            MolotovExposureTimes.Clear();
            _fallbackFireArrowScanPending = true;
            _fallbackFireArrowScanElapsed = 0f;
        }
    }

    private static bool FireExtinguishingEnabled()
    {
        return _campfireEnabled.Value;
    }

    private static PrecipitationTier ReadPrecipitationTier(WeatherSystem weather)
    {
        try
        {
            RainTypes rainTypes = WeatherSystem.GetRainTypes();
            if (rainTypes != null)
            {
                PrecipitationTier effectTier = PrecipitationTier.None;
                if (IsEffectActive(rainTypes.RainHeavy) || IsEffectActive(rainTypes.SnowHeavy))
                {
                    effectTier = PrecipitationTier.Heavy;
                }
                else if (IsEffectActive(rainTypes.RainMedium) || IsEffectActive(rainTypes.SnowMedium))
                {
                    effectTier = PrecipitationTier.Medium;
                }
                else if (IsEffectActive(rainTypes.RainLight) || IsEffectActive(rainTypes.SnowLight))
                {
                    effectTier = PrecipitationTier.Light;
                }

                if (effectTier != PrecipitationTier.None)
                {
                    return effectTier;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            switch (weather.CurrentRainType)
            {
                case WeatherSystem.RainType.Light:
                    return PrecipitationTier.Light;
                case WeatherSystem.RainType.Medium:
                    return PrecipitationTier.Medium;
                case WeatherSystem.RainType.Heavy:
                    return PrecipitationTier.Heavy;
            }
        }
        catch (Exception)
        {
        }

        return PrecipitationTier.None;
    }

    private static bool IsEffectActive(RainEffectController controller)
    {
        return controller != null && controller._effectIsActive;
    }

    private static float GetPrecipitationTimer()
    {
        if (!_useWeatherTiers.Value)
        {
            return _fireTimer.Value;
        }

        return _tier switch
        {
            PrecipitationTier.Light => _lightTimer.Value,
            PrecipitationTier.Heavy => _heavyTimer.Value,
            _ => _mediumTimer.Value
        };
    }

    private static float GetHandheldTimer(float baseTimer)
    {
        if (!_useWeatherTiers.Value || !_scaleHandheldsWithTiers.Value)
        {
            return baseTimer;
        }

        float reference = Math.Max(_mediumTimer.Value, 0.01f);
        return Math.Max(MinimumHandheldTimer, baseTimer * GetPrecipitationTimer() / reference);
    }

    private static void UpdateWetLocks(float now)
    {
        bool precipitationStopped = _dryElapsed >= PrecipitationClearDelay;
        if (!_preventRelightInRain.Value || precipitationStopped)
        {
            ClearWetFires();
        }
        else
        {
            PromotePendingWetFires(now);
        }

        if (!_preventHandheldRelightInRain.Value || precipitationStopped)
        {
            ClearWetHandhelds();
        }
        else
        {
            PromotePendingWetHandhelds(now);
        }
    }

    private static void PromotePendingWetFires(float now)
    {
        if (PendingWetFires.Count == 0)
        {
            return;
        }

        ReadyWetFireIds.Clear();
        foreach (KeyValuePair<int, float> pending in PendingWetFires)
        {
            if (now >= pending.Value)
            {
                ReadyWetFireIds.Add(pending.Key);
            }
        }

        foreach (int id in ReadyWetFireIds)
        {
            RemovePendingWetFire(id);
            Tracked<CookingFireNew>? tracked = Fires.TryGet(id);
            if (tracked != null && tracked.Target != null && tracked.Target.IsLit)
            {
                continue;
            }

            WetFires.Add(id);
        }
        ReadyWetFireIds.Clear();
    }

    private static void PromotePendingWetHandhelds(float now)
    {
        if (PendingWetHandhelds.Count == 0)
        {
            return;
        }

        ReadyWetHandhelds.Clear();
        foreach (KeyValuePair<HandheldKind, float> pending in PendingWetHandhelds)
        {
            if (now >= pending.Value)
            {
                ReadyWetHandhelds.Add(pending.Key);
            }
        }

        foreach (HandheldKind kind in ReadyWetHandhelds)
        {
            PendingWetHandhelds.Remove(kind);
            WetHandhelds.Add(kind);
            if (kind == HandheldKind.Torch)
            {
                StopTorchWarmEffect();
            }
        }
        ReadyWetHandhelds.Clear();
    }

    private static void MarkFireWet(CookingFireNew fire, int id)
    {
        if (!_preventRelightInRain.Value)
        {
            return;
        }

        if (IsInWarmGracePeriod())
        {
            PendingWetFires[id] = Time.time + _warmGracePeriod.Value;
            StartWarmEffect(fire, id);
            return;
        }

        RemovePendingWetFire(id);
        WetFires.Add(id);
    }

    private static void RemovePendingWetFire(int id)
    {
        PendingWetFires.Remove(id);
        StopWarmEffect(id);
    }

    private static void MarkHandheldWet(HandheldKind kind, TorchWeaponController? torch = null)
    {
        if (!_preventHandheldRelightInRain.Value)
        {
            return;
        }

        if (IsInWarmGracePeriod())
        {
            PendingWetHandhelds[kind] = Time.time + _warmGracePeriod.Value;
            if (kind == HandheldKind.Torch && torch != null)
            {
                StartTorchWarmEffect(torch);
            }
            return;
        }

        PendingWetHandhelds.Remove(kind);
        WetHandhelds.Add(kind);
    }

    private static void ClearHandheldWet(HandheldKind kind)
    {
        WetHandhelds.Remove(kind);
        PendingWetHandhelds.Remove(kind);
        if (kind == HandheldKind.Torch)
        {
            StopTorchWarmEffect();
        }
    }

    private static bool IsInWarmGracePeriod()
    {
        return _tier == PrecipitationTier.Light && _warmGracePeriod.Value > 0f;
    }

    private static void ClearWetFires()
    {
        WetFires.Clear();
        PendingWetFires.Clear();
        StopAllWarmEffects();
    }

    private static void ClearWetHandhelds()
    {
        WetHandhelds.Clear();
        PendingWetHandhelds.Clear();
        StopTorchWarmEffect();
    }

    private static void RefreshFallbackFires(float deltaTime)
    {
        _fallbackFireScanElapsed += deltaTime;
        if (!_fallbackFireScanPending && _fallbackFireScanElapsed < FallbackFireScanInterval)
        {
            return;
        }

        Fires.Sync(UnityEngine.Object.FindObjectsByType<CookingFireNew>(FindObjectsSortMode.None));
        _fallbackFireScanElapsed = 0f;
        _fallbackFireScanPending = false;
    }

    private static void UpdateRainSickness(bool exposed, bool sheltered, float deltaTime)
    {
        if (!_rainSicknessEnabled.Value || !_badWeather)
        {
            _rainSicknessExposure = 0f;
            return;
        }

        Vitals vitals = LocalPlayer.Vitals;
        if (vitals == null)
        {
            return;
        }

        if (vitals.IsSick)
        {
            _rainSicknessExposure = 0f;
            return;
        }

        if (sheltered || !exposed)
        {
            return;
        }

        _rainSicknessExposure += deltaTime;
        if (_rainSicknessExposure >= _rainSicknessTimer.Value)
        {
            vitals.SetSickness(25f);
            _rainSicknessExposure = 0f;
            RLog.Msg("[RainExtinguishesFires] Rain/snow exposure caused sickness.");
        }
    }

    private static void UpdateFireSlice(float now)
    {
        if (!_badWeather || !FireExtinguishingEnabled())
        {
            return;
        }

        List<Tracked<CookingFireNew>> items = Fires.Items;
        int budget = Math.Min(FireChecksPerFrame, items.Count);
        int visited = 0;
        while (budget > 0 && visited < items.Count)
        {
            if (_fireCursor >= items.Count)
            {
                _fireCursor = 0;
            }

            Tracked<CookingFireNew> tracked = items[_fireCursor];
            visited++;
            if (now < tracked.NextCheck)
            {
                _fireCursor++;
                continue;
            }

            budget--;
            CookingFireNew fire = tracked.Target;
            if (fire == null)
            {
                Fires.RemoveAt(_fireCursor);
                continue;
            }

            _fireCursor++;
            CheckFire(tracked, fire, now);
        }
    }

    private static void CheckFire(Tracked<CookingFireNew> tracked, CookingFireNew fire, float now)
    {
        float step = tracked.LastCheck > 0f ? Math.Min(now - tracked.LastCheck, MaxExposureStep) : 0f;
        tracked.LastCheck = now;
        tracked.NextCheck = now + FireCheckInterval;

        if (!_campfireEnabled.Value)
        {
            tracked.Exposure = 0f;
            tracked.Done = false;
            return;
        }

        int id = tracked.Id;
        if (!fire.IsLit)
        {
            tracked.Exposure = 0f;
            tracked.Done = false;
            if ((WetFires.Contains(id) || PendingWetFires.ContainsKey(id)) && IsFireSheltered(fire))
            {
                WetFires.Remove(id);
                RemovePendingWetFire(id);
            }
            return;
        }

        if (!tracked.Done)
        {
            WetFires.Remove(id);
            RemovePendingWetFire(id);
        }

        if (IsWeatherproofBonfire(tracked, fire, now) || IsFireSheltered(fire))
        {
            tracked.Exposure = 0f;
            tracked.Done = false;
            return;
        }

        if (tracked.Done)
        {
            return;
        }

        tracked.Exposure += step;
        float requiredTime = GetPrecipitationTimer() * GetFireResistance(tracked, fire, now);
        if (tracked.Exposure < requiredTime)
        {
            return;
        }

        tracked.Exposure = 0f;
        tracked.Done = true;
        MarkFireWet(fire, id);
        if (!_isAuthority)
        {
            return;
        }

        PlaySmokePuff(fire);
        fire.SetPutOut();
        PlayExtinguishSound();
        RLog.Msg($"[RainExtinguishesFires] Fire {id} extinguished by rain/snow ({_tier}).");
    }

    private static float GetFireResistance(Tracked<CookingFireNew> tracked, CookingFireNew fire, float now)
    {
        bool stoneRing = _stoneRingEnabled.Value && HasFullStoneRing(tracked, fire, now);
        float resistance = 1f;
        float fuelMultiplier = GetFuelMultiplier(tracked, fire, now, stoneRing);
        if (fuelMultiplier > 0f)
        {
            if (_wetFirewoodPenaltyEnabled.Value && tracked.DampFirewood)
            {
                fuelMultiplier = 1f + (fuelMultiplier - 1f) * 0.5f;
            }
            resistance = fuelMultiplier;
        }
        else if (stoneRing)
        {
            resistance = _stoneRingMultiplier.Value;
        }

        if (_canopyProtectionEnabled.Value && _canopyMultiplier.Value > 1.001f && IsUnderCanopy(tracked, fire, now))
        {
            resistance *= _canopyMultiplier.Value;
        }

        return Math.Max(1f, resistance);
    }

    private static float GetFuelMultiplier(Tracked<CookingFireNew> tracked, CookingFireNew fire, float now, bool stoneRing)
    {
        if (!_firewoodBonusEnabled.Value)
        {
            return 0f;
        }

        RefreshFuel(tracked, fire, now, false);
        if (!tracked.FullFirewood)
        {
            return 0f;
        }

        int layers = GetLogLayers(tracked);
        if (layers >= 2)
        {
            return stoneRing ? _stoneRingTwoLogLayersMultiplier.Value : _twoLogLayersMultiplier.Value;
        }

        if (layers == 1)
        {
            return stoneRing ? _stoneRingOneLogLayerMultiplier.Value : _oneLogLayerMultiplier.Value;
        }

        return stoneRing ? _stoneRingFirewoodMultiplier.Value : _firewoodMultiplier.Value;
    }

    private static int GetLogLayers(Tracked<CookingFireNew> tracked)
    {
        if (!_logLayerBonusEnabled.Value)
        {
            return 0;
        }

        if (tracked.BonfireLogCount >= BonfireLayerTwoPieces)
        {
            return 2;
        }

        return tracked.BonfireLogCount >= BonfireLayerOnePieces ? 1 : 0;
    }

    private static bool IsWeatherproofBonfire(Tracked<CookingFireNew> tracked, CookingFireNew fire, float now)
    {
        if (!_stoneRingBonfireNeverExtinguishes.Value || !_firewoodBonusEnabled.Value || !_logLayerBonusEnabled.Value || !_stoneRingEnabled.Value)
        {
            return false;
        }

        RefreshFuel(tracked, fire, now, false);
        if (!tracked.FullFirewood || GetLogLayers(tracked) < 2)
        {
            return false;
        }

        return HasFullStoneRing(tracked, fire, now);
    }

    private static bool IsFireSheltered(CookingFireNew fire)
    {
        Vector3 position = fire.transform.position;
        Transform ownStructure = GetFireStructureRoot(fire);
        if (HasRoofOverhead(position, ownStructure) && !IsExposedToWindDrivenRain(position, ownStructure))
        {
            return true;
        }

        return IsUnderTarp(fire, position);
    }

    private static Transform GetFireStructureRoot(CookingFireNew fire)
    {
        FireStructure structure = fire.GetComponentInParent<FireStructure>();
        return structure != null ? structure.transform : fire.transform;
    }

    private static bool IsUnderTarp(CookingFireNew fire, Vector3 position)
    {
        int id = fire.GetInstanceID();
        float now = Time.time;
        if (TarpChecks.TryGetValue(id, out TarpCheck? cached) && now < cached.NextCheck)
        {
            return cached.UnderTarp;
        }

        bool underTarp = false;
        try
        {
            TarpCandidateIds.Clear();
            Collider[] colliders = Physics.OverlapSphere(position + Vector3.up * TarpSearchHeight, TarpSearchRadius, Physics.AllLayers, QueryTriggerInteraction.Collide);
            foreach (Collider collider in colliders)
            {
                if (collider == null)
                {
                    continue;
                }

                Structure? tarp = collider.GetComponentInParent<TarpStructure>();
                if (tarp == null)
                {
                    tarp = collider.GetComponentInParent<PresetTarpStructure>();
                }

                if (tarp == null || !TarpCandidateIds.Add(tarp.GetInstanceID()))
                {
                    continue;
                }

                if (IsPositionUnderStructure(tarp, position))
                {
                    underTarp = true;
                    break;
                }
            }
            TarpCandidateIds.Clear();
        }
        catch (Exception exception)
        {
            if (!_canopyWarningLogged)
            {
                _canopyWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Tarp check failed for a fire: {exception.Message}");
            }
        }

        if (cached == null)
        {
            cached = new TarpCheck();
            TarpChecks[id] = cached;
        }
        cached.UnderTarp = underTarp;
        cached.NextCheck = now + TarpRefreshInterval;
        return underTarp;
    }

    private static bool IsPositionUnderStructure(Structure structure, Vector3 position)
    {
        bool hasBounds = false;
        Bounds bounds = default;
        Renderer[] renderers = structure.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            Collider[] colliders = structure.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        return position.x >= min.x - TarpEdgeMargin && position.x <= max.x + TarpEdgeMargin
            && position.z >= min.z - TarpEdgeMargin && position.z <= max.z + TarpEdgeMargin
            && max.y > position.y + 0.3f;
    }

    private static bool IsExposedToWindDrivenRain(Vector3 position, Transform ownStructure)
    {
        if (!_windFactorEnabled.Value || !_badWeather)
        {
            return false;
        }

        try
        {
            WeatherSystem weather = WeatherSystem.Instance;
            if (weather == null || weather._windIntensity < MinimumWindIntensity)
            {
                return false;
            }

            Vector3 wind = weather._windDirection;
            Vector3 upwind = new(-wind.x, 0f, -wind.z);
            if (upwind.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            Vector3 direction = (upwind.normalized + Vector3.up * WindRayElevation).normalized;
            Vector3 origin = position + Vector3.up * RaycastUpOffset;
            return !RayHitsOutside(origin, direction, WindCheckDistance, ownStructure);
        }
        catch (Exception exception)
        {
            if (!_windWarningLogged)
            {
                _windWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Could not read wind, wind direction factor disabled: {exception.Message}");
            }
            return false;
        }
    }

    private static bool IsUnderCanopy(Tracked<CookingFireNew> tracked, CookingFireNew fire, float now)
    {
        if (now < tracked.NextCanopyCheck)
        {
            return tracked.UnderCanopy;
        }

        tracked.NextCanopyCheck = now + CanopyRefreshInterval;
        tracked.UnderCanopy = false;
        try
        {
            if (!TreeManager.TryGetInstance(out TreeManager treeManager) || treeManager == null)
            {
                if (!_treeManagerMissingLogged)
                {
                    _treeManagerMissingLogged = true;
                    RLog.Warning("[RainExtinguishesFires] Tree manager not available yet, canopy protection will retry.");
                }
                return false;
            }

            tracked.UnderCanopy = treeManager.GetTreeCountInSphere(fire.transform.position, CanopyRadius) > 0;
        }
        catch (Exception exception)
        {
            if (!_canopyWarningLogged)
            {
                _canopyWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Canopy check failed for a fire: {exception.Message}");
            }
        }
        return tracked.UnderCanopy;
    }

    private static void OnFuelSourceRegistered(CookingFireNew __instance, FireFuelSource fireFuelSource)
    {
        try
        {
            if (!_gameStarted || __instance == null || fireFuelSource == null || (!_firewoodBonusEnabled.Value && !_leafStokeEnabled.Value))
            {
                return;
            }

            FuelKind kind = ClassifyFuel(fireFuelSource);
            if (kind == FuelKind.Other)
            {
                return;
            }

            Tracked<CookingFireNew>? tracked = Fires.TryGet(__instance.GetInstanceID());
            if (tracked == null)
            {
                return;
            }

            float now = Time.time;
            if (kind == FuelKind.BonfireLog)
            {
                if (_firewoodBonusEnabled.Value)
                {
                    tracked.NextFuelCheck = 0f;
                }
                return;
            }

            if (kind == FuelKind.Firewood)
            {
                if (!_firewoodBonusEnabled.Value)
                {
                    return;
                }

                tracked.NextFuelCheck = 0f;
                if (_badWeather && !IsFireSheltered(__instance))
                {
                    tracked.DampFirewood = true;
                }
                return;
            }

            if (!_leafStokeEnabled.Value || !_badWeather || !__instance.IsLit || now < tracked.NextLeafStoke)
            {
                return;
            }

            float seconds = _leafStokeSeconds.Value;
            tracked.Exposure = Math.Max(0f, tracked.Exposure - seconds);
            tracked.NextLeafStoke = now + Math.Max(LeafStokeMinimumCooldown, seconds * 2f);
        }
        catch (Exception exception)
        {
            if (!_fuelWarningLogged)
            {
                _fuelWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Fuel check failed: {exception.Message}");
            }
        }
    }

    private static void RefreshFuel(Tracked<CookingFireNew> tracked, CookingFireNew fire, float now, bool force)
    {
        if (!force && now < tracked.NextFuelCheck)
        {
            return;
        }

        tracked.NextFuelCheck = now + FuelRefreshInterval;
        int firewood = 0;
        int bonfireLogs = 0;
        try
        {
            var sources = fire._fuelSources;
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    FireFuelSource source = sources[i];
                    if (source == null)
                    {
                        continue;
                    }

                    FuelKind kind = ClassifyFuel(source);
                    if (kind == FuelKind.Firewood)
                    {
                        firewood++;
                    }
                    else if (kind == FuelKind.BonfireLog)
                    {
                        bonfireLogs++;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (!_fuelWarningLogged)
            {
                _fuelWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Could not read fire fuel: {exception.Message}");
            }
        }

        if (firewood != tracked.FirewoodCount || bonfireLogs != tracked.BonfireLogCount)
        {
            RLog.Msg($"[RainExtinguishesFires] Fire {tracked.Id} fuel: {firewood} small firewood pieces, {bonfireLogs} full log pieces.");
        }

        tracked.FirewoodCount = firewood;
        tracked.BonfireLogCount = bonfireLogs;
        tracked.FullFirewood = firewood >= FullSmallFirewoodPieces;
        if (firewood == 0)
        {
            tracked.DampFirewood = false;
        }
    }

    private static FuelKind ClassifyFuel(FireFuelSource source)
    {
        string name = source.name ?? string.Empty;
        StructureElement element = source.GetComponent<StructureElement>();
        string profileName = element != null && element.Profile != null ? element.Profile.name ?? string.Empty : string.Empty;
        string combined = $"{name} {profileName}".ToLowerInvariant();
        FuelKind kind = FuelKind.Other;
        if (combined.Contains("leaf") || combined.Contains("leaves"))
        {
            kind = FuelKind.Leaves;
        }
        else if (combined.Contains("bonfire"))
        {
            kind = FuelKind.BonfireLog;
        }
        else if (combined.Contains("firewood") || combined.Contains("log"))
        {
            kind = FuelKind.Firewood;
        }

        string logKey = $"{name}|{profileName}";
        if (LoggedFuelNames.Count < MaxLoggedNames && LoggedFuelNames.Add(logKey))
        {
            RLog.Msg($"[RainExtinguishesFires] Fuel '{name}' (profile '{profileName}') treated as {kind}.");
        }
        return kind;
    }

    private static bool HasFullStoneRing(Tracked<CookingFireNew> tracked, CookingFireNew fire, float now)
    {
        if (now < tracked.NextStoneCheck)
        {
            return tracked.StoneRing;
        }

        tracked.NextStoneCheck = now + StoneRingRefreshInterval;
        tracked.StoneRing = false;
        try
        {
            FireStructure structure = fire.GetComponentInParent<FireStructure>();
            if (structure == null)
            {
                return false;
            }

            structure.CalcElementCounts(out int _, out int _, out int reinforcementElements);
            PresetElementsTemplate template = structure.Construction_IReinforceableStructure_GetReinforcementTemplate();
            int maximum = template != null ? template.ElementCount : 0;
            tracked.StoneRing = maximum > 0 && reinforcementElements >= maximum;
        }
        catch (Exception exception)
        {
            if (!_stoneRingWarningLogged)
            {
                _stoneRingWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Could not read campfire stones, stone ring bonus disabled for that fire: {exception.Message}");
            }
        }
        return tracked.StoneRing;
    }

    private static void StartWarmEffect(CookingFireNew fire, int id)
    {
        StopWarmEffect(id);
        WarmEffect effect = new() { Fire = fire };
        WarmEffects[id] = effect;
        try
        {
            Vector3 position = fire.transform.position;
            GameObject root = new("RainExtinguishesFires_WarmEmbers");
            root.transform.position = position;
            effect.Root = root;
            SpawnWarmEmbers(FindEmberTemplates(GetFlameSearchRoots(fire)), root.transform);
            StartSizzle(fire, position, effect);
        }
        catch (Exception exception)
        {
            if (!_warmEffectWarningLogged)
            {
                _warmEffectWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Could not create still-warm effect: {exception.Message}");
            }
        }
    }

    private static void StartTorchWarmEffect(TorchWeaponController torch)
    {
        StopTorchWarmEffect();
        try
        {
            Transform anchor = torch._burnerLocator != null ? torch._burnerLocator : torch.transform;
            GameObject root = new("RainExtinguishesFires_TorchEmbers");
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = Vector3.zero;
            _torchWarmRoot = root;
            SpawnWarmEmbers(FindEmberTemplates(new List<Transform> { torch.transform }), root.transform);
        }
        catch (Exception exception)
        {
            if (!_warmEffectWarningLogged)
            {
                _warmEffectWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Could not create torch still-warm effect: {exception.Message}");
            }
        }
    }

    private static void StopTorchWarmEffect()
    {
        if (_torchWarmRoot != null)
        {
            UnityEngine.Object.Destroy(_torchWarmRoot);
        }
        _torchWarmRoot = null;
    }

    private static void SpawnWarmEmbers(List<ParticleSystem> templates, Transform root)
    {
        if (templates.Count == 0)
        {
            RLog.Msg("[RainExtinguishesFires] No ember particles found, skipping still-warm embers.");
            return;
        }

        foreach (ParticleSystem template in templates)
        {
            GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, template.transform.position, template.transform.rotation);
            copy.transform.SetParent(root, true);
            Transform copyTransform = copy.transform;
            for (int i = copyTransform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(copyTransform.GetChild(i).gameObject);
            }

            DisableScripts(copy, false);
            copy.SetActive(true);
            ParticleSystem system = copy.GetComponent<ParticleSystem>();
            if (system == null)
            {
                continue;
            }

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTimeMultiplier *= EmberEmissionScale;
            system.Play(false);
        }
    }

    private static List<ParticleSystem> FindEmberTemplates(List<Transform> searchRoots)
    {
        List<ParticleSystem> named = new();
        List<ParticleSystem> small = new();
        foreach (Transform searchRoot in searchRoots)
        {
            ParticleSystem[] systems = searchRoot.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                if (system == null)
                {
                    continue;
                }

                string name = system.name ?? string.Empty;
                string normalized = name.ToLowerInvariant();
                float startSize = system.main.startSizeMultiplier;
                if (LoggedParticleNames.Count < MaxLoggedNames && LoggedParticleNames.Add(name))
                {
                    RLog.Msg($"[RainExtinguishesFires] Fire particle '{name}' (start size {startSize:0.###}).");
                }

                if (normalized.Contains("smoke") || normalized.Contains("steam"))
                {
                    continue;
                }

                bool emberName = false;
                foreach (string token in EmberParticleTokens)
                {
                    if (normalized.Contains(token))
                    {
                        emberName = true;
                        break;
                    }
                }

                if (emberName && !named.Contains(system))
                {
                    named.Add(system);
                }
                else if (startSize > 0f && startSize <= EmberMaxStartSize && !small.Contains(system))
                {
                    small.Add(system);
                }
            }
        }
        return named.Count > 0 ? named : small;
    }

    private static List<Transform> GetFlameSearchRoots(CookingFireNew fire)
    {
        List<Transform> roots = new();
        try
        {
            GameObject? flamesRoot = fire._flameVisual?.FlamesRoot;
            if (flamesRoot != null)
            {
                roots.Add(flamesRoot.transform);
            }
        }
        catch (Exception)
        {
        }

        roots.Add(fire.transform);
        return roots;
    }

    private static void StartSizzle(CookingFireNew fire, Vector3 position, WarmEffect effect)
    {
        string? path = ResolveSizzlePath(fire);
        if (string.IsNullOrEmpty(path) || !FMOD_StudioSystem.TryGetInstance(out FMOD_StudioSystem studioSystem))
        {
            return;
        }

        FMOD.Studio.EventInstance instance = studioSystem.GetEvent(path);
        if (!instance.isValid())
        {
            return;
        }

        instance.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(position));
        instance.setVolume(SizzleVolume);
        instance.start();
        effect.Sound = instance;
        effect.HasSound = true;
    }

    private static string? ResolveSizzlePath(CookingFireNew fire)
    {
        if (_sizzlePathResolved)
        {
            return _sizzleEventPath;
        }

        string? path = ReadFireSpherePath(fire) ?? FindFireLoopInEventList();
        _sizzlePathResolved = true;
        _sizzleEventPath = path;
        RLog.Msg(path != null ? $"[RainExtinguishesFires] Still-warm sizzle uses '{path}'." : "[RainExtinguishesFires] No campfire fire sound found, still-warm sizzle disabled.");
        return _sizzleEventPath;
    }

    private static string? ReadFireSpherePath(CookingFireNew fire)
    {
        try
        {
            FireSphere? sphere = fire._flameVisual?.FireSphere;
            string path = sphere != null ? sphere._fireSfxEvent ?? string.Empty : string.Empty;
            if (path.Length > 0)
            {
                return path;
            }

            foreach (Transform searchRoot in GetFlameSearchRoots(fire))
            {
                FireSphere[] spheres = searchRoot.GetComponentsInChildren<FireSphere>(true);
                foreach (FireSphere candidate in spheres)
                {
                    string candidatePath = candidate != null ? candidate._fireSfxEvent ?? string.Empty : string.Empty;
                    if (candidatePath.Length > 0)
                    {
                        return candidatePath;
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static string? FindFireLoopInEventList()
    {
        try
        {
            if (!FMOD_StudioSystem.TryGetInstance(out FMOD_StudioSystem studioSystem))
            {
                return null;
            }

            var events = studioSystem.DumpAllEvents();
            if (events == null)
            {
                return null;
            }

            string? best = null;
            int bestScore = 0;
            int logged = 0;
            for (int i = 0; i < events.Count; i++)
            {
                string path = events[i] ?? string.Empty;
                string normalized = path.ToLowerInvariant();
                if (!normalized.Contains("fire") && !normalized.Contains("ember"))
                {
                    continue;
                }

                if (logged < MaxLoggedNames)
                {
                    logged++;
                    RLog.Msg($"[RainExtinguishesFires] Fire sound candidate '{path}'.");
                }

                bool excluded = false;
                foreach (string token in SizzleExcludedTokens)
                {
                    if (normalized.Contains(token))
                    {
                        excluded = true;
                        break;
                    }
                }

                if (excluded)
                {
                    continue;
                }

                int score = 1;
                if (normalized.Contains("campfire") || normalized.Contains("cookingfire") || normalized.Contains("camp_fire"))
                {
                    score += 4;
                }
                if (normalized.Contains("loop") || normalized.Contains("burn") || normalized.Contains("crackl"))
                {
                    score += 2;
                }
                if (normalized.Contains("ember"))
                {
                    score += 1;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = path;
                }
            }
            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void DisableScripts(GameObject root, bool keepLightData)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            if (keepLightData && behaviour.GetIl2CppType().Name.Contains("LightData"))
            {
                continue;
            }

            behaviour.enabled = false;
        }
    }

    private static void StopWarmEffect(int id)
    {
        if (!WarmEffects.Remove(id, out WarmEffect? effect))
        {
            return;
        }

        ReleaseWarmEffect(effect);
    }

    private static void CleanupDestroyedWarmFires()
    {
        if (WarmEffects.Count == 0)
        {
            return;
        }

        WarmEffectIds.Clear();
        foreach (KeyValuePair<int, WarmEffect> pair in WarmEffects)
        {
            if (pair.Value.Fire == null)
            {
                WarmEffectIds.Add(pair.Key);
            }
        }

        foreach (int id in WarmEffectIds)
        {
            RemovePendingWetFire(id);
        }
        WarmEffectIds.Clear();
    }

    private static void QueueSprinklerReset(TorchWeaponController? torch, MolotovWeaponController? molotov, FireArrowController? arrow)
    {
        SprinklerResets.Add(new SprinklerReset { Time = Time.time + SprinklerResetDelay, Torch = torch, Molotov = molotov, Arrow = arrow });
    }

    private static void ProcessSprinklerResets(float now)
    {
        for (int i = SprinklerResets.Count - 1; i >= 0; i--)
        {
            SprinklerReset reset = SprinklerResets[i];
            if (now < reset.Time)
            {
                continue;
            }

            SprinklerResets.RemoveAt(i);
            try
            {
                if (reset.Torch != null)
                {
                    reset.Torch.OnSprinklersInfluenceCountChanged(0);
                }
                if (reset.Molotov != null)
                {
                    reset.Molotov.OnSprinklersInfluenceCountChanged(0);
                }
                if (reset.Arrow != null)
                {
                    reset.Arrow.OnSprinklersInfluenceCountChanged(0);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private static void StopAllWarmEffects()
    {
        if (WarmEffects.Count == 0)
        {
            return;
        }

        WarmEffectIds.Clear();
        WarmEffectIds.AddRange(WarmEffects.Keys);
        foreach (int id in WarmEffectIds)
        {
            StopWarmEffect(id);
        }
        WarmEffectIds.Clear();
    }

    private static void ReleaseWarmEffect(WarmEffect effect)
    {
        try
        {
            if (effect.HasSound)
            {
                effect.HasSound = false;
                if (effect.Sound.isValid())
                {
                    effect.Sound.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                    effect.Sound.release();
                }
            }
        }
        catch (Exception)
        {
        }

        effect.Fire = null;
        if (effect.Root != null)
        {
            UnityEngine.Object.Destroy(effect.Root);
            effect.Root = null;
        }
    }

    private static void PlaySmokePuff(CookingFireNew fire)
    {
        if (!_smokeEffectEnabled.Value)
        {
            return;
        }

        try
        {
            ParticleSystem? template = FindSmokeTemplate(fire.transform);
            Transform parent = fire.transform.parent;
            if (template == null && parent != null)
            {
                template = FindSmokeTemplate(parent);
            }

            if (template == null)
            {
                string name = fire.name;
                if (LoggedSmokeMisses.Add(name))
                {
                    RLog.Msg($"[RainExtinguishesFires] No smoke particles found on '{name}', skipping smoke puff.");
                }
                return;
            }

            Transform source = template.transform;
            GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, source.position, source.rotation);
            copy.SetActive(true);
            ParticleSystem[] systems = copy.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                system.gameObject.SetActive(true);
                ParticleSystem.MainModule main = system.main;
                main.loop = false;
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Emit(SmokeBurstCount);
            }
            UnityEngine.Object.Destroy(copy, SmokeLifetime);
        }
        catch (Exception exception)
        {
            if (!_smokeWarningLogged)
            {
                _smokeWarningLogged = true;
                RLog.Warning($"[RainExtinguishesFires] Could not play smoke puff: {exception.Message}");
            }
        }
    }

    private static ParticleSystem? FindSmokeTemplate(Transform root)
    {
        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
        ParticleSystem? smoke = null;
        foreach (ParticleSystem system in systems)
        {
            if (system == null)
            {
                continue;
            }

            string name = system.name.ToLowerInvariant();
            if (name.Contains("steam"))
            {
                return system;
            }

            if (smoke == null && name.Contains("smoke"))
            {
                smoke = system;
            }
        }
        return smoke;
    }

    private static void UpdateHandheldItems(bool exposed, float deltaTime)
    {
        var inventory = LocalPlayer.Inventory;
        if (inventory == null)
        {
            TorchExposureTimes.Clear();
            FireArrowExposureTimes.Clear();
            MolotovExposureTimes.Clear();
            return;
        }

        GameObject? leftItemObject = null;
        GameObject? rightItemObject = null;
        if (_torchEnabled.Value || _molotovEnabled.Value)
        {
            var leftItem = inventory.LeftHandItem;
            var rightItem = inventory.RightHandItem;
            leftItemObject = leftItem == null ? null : leftItem.ItemObject;
            rightItemObject = rightItem == null ? null : rightItem.ItemObject;
        }

        UpdateTorches(leftItemObject, rightItemObject, exposed, deltaTime);
        UpdateFireArrows(exposed, deltaTime);
        UpdateMolotovs(leftItemObject, rightItemObject, exposed, deltaTime);
    }

    private static void UpdateTorches(GameObject? leftItem, GameObject? rightItem, bool exposed, float deltaTime)
    {
        ActiveTorchIds.Clear();
        if (_torchEnabled.Value && exposed)
        {
            UpdateTorch(leftItem, deltaTime);
            if (rightItem != leftItem)
            {
                UpdateTorch(rightItem, deltaTime);
            }
        }
        RemoveMissing(TorchExposureTimes, ActiveTorchIds);
    }

    private static void UpdateTorch(GameObject? itemObject, float deltaTime)
    {
        if (itemObject == null)
        {
            return;
        }

        TorchWeaponController torch = itemObject.GetComponentInChildren<TorchWeaponController>(true);
        if (torch == null || !torch.gameObject.activeInHierarchy)
        {
            return;
        }

        int id = torch.GetInstanceID();
        ActiveTorchIds.Add(id);
        if (!torch.IsBurning)
        {
            TorchExposureTimes.Remove(id);
            return;
        }

        if (!TorchExposureTimes.ContainsKey(id))
        {
            ClearHandheldWet(HandheldKind.Torch);
        }

        if (Accumulate(TorchExposureTimes, id, deltaTime, GetHandheldTimer(_torchTimer.Value)))
        {
            torch.OnSprinklersInfluenceCountChanged(1);
            QueueSprinklerReset(torch, null, null);
            MarkHandheldWet(HandheldKind.Torch, torch);
            PlayExtinguishSound();
        }
    }

    private static void UpdateMolotovs(GameObject? leftItem, GameObject? rightItem, bool exposed, float deltaTime)
    {
        ActiveMolotovIds.Clear();
        if (_molotovEnabled.Value && exposed)
        {
            UpdateMolotov(leftItem, deltaTime);
            if (rightItem != leftItem)
            {
                UpdateMolotov(rightItem, deltaTime);
            }
        }
        RemoveMissing(MolotovExposureTimes, ActiveMolotovIds);
    }

    private static void UpdateMolotov(GameObject? itemObject, float deltaTime)
    {
        if (itemObject == null)
        {
            return;
        }

        MolotovWeaponController molotov = itemObject.GetComponentInChildren<MolotovWeaponController>(true);
        if (molotov == null || !molotov.gameObject.activeInHierarchy)
        {
            return;
        }

        int id = molotov.GetInstanceID();
        ActiveMolotovIds.Add(id);
        if (!molotov.IsBurning)
        {
            MolotovExposureTimes.Remove(id);
            return;
        }

        if (!MolotovExposureTimes.ContainsKey(id))
        {
            ClearHandheldWet(HandheldKind.Molotov);
        }

        if (Accumulate(MolotovExposureTimes, id, deltaTime, GetHandheldTimer(_molotovTimer.Value)))
        {
            molotov.OnSprinklersInfluenceCountChanged(1);
            QueueSprinklerReset(null, molotov, null);
            MarkHandheldWet(HandheldKind.Molotov);
            PlayExtinguishSound();
        }
    }

    private static void UpdateFireArrows(bool exposed, float deltaTime)
    {
        ActiveFireArrowIds.Clear();
        if (_fireArrowsEnabled.Value && exposed)
        {
            if (_fallbackFireArrowScanPending && !_fireArrowScanFallback)
            {
                VerifyFireArrowHooks();
                _fallbackFireArrowScanPending = false;
            }

            if (_fireArrowScanFallback)
            {
                _fallbackFireArrowScanElapsed += deltaTime;
                if (_fallbackFireArrowScanPending || _fallbackFireArrowScanElapsed >= FallbackFireArrowScanInterval)
                {
                    FireArrows.Sync(UnityEngine.Object.FindObjectsByType<FireArrowController>(FindObjectsSortMode.None));
                    _fallbackFireArrowScanElapsed = 0f;
                    _fallbackFireArrowScanPending = false;
                }
            }

            float arrowTimer = GetHandheldTimer(_fireArrowTimer.Value);
            List<Tracked<FireArrowController>> arrows = FireArrows.Items;
            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                Tracked<FireArrowController> tracked = arrows[i];
                FireArrowController arrow = tracked.Target;
                if (arrow == null)
                {
                    FireArrows.RemoveAt(i);
                    continue;
                }

                if (!arrow.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int id = tracked.Id;
                ActiveFireArrowIds.Add(id);
                if (!arrow.IsBurning)
                {
                    FireArrowExposureTimes.Remove(id);
                    continue;
                }

                if (!FireArrowExposureTimes.ContainsKey(id))
                {
                    ClearHandheldWet(HandheldKind.FireArrow);
                }

                if (!Accumulate(FireArrowExposureTimes, id, deltaTime, arrowTimer))
                {
                    continue;
                }

                arrow.OnSprinklersInfluenceCountChanged(1);
                QueueSprinklerReset(null, null, arrow);
                MarkHandheldWet(HandheldKind.FireArrow);
                PlayExtinguishSound();
            }
        }
        RemoveMissing(FireArrowExposureTimes, ActiveFireArrowIds);
    }

    private static bool Accumulate(Dictionary<int, float> times, int id, float deltaTime, float requiredTime)
    {
        if (times.TryGetValue(id, out float elapsed) && elapsed < 0f)
        {
            return false;
        }

        elapsed += deltaTime;
        if (elapsed < requiredTime)
        {
            times[id] = elapsed;
            return false;
        }

        times[id] = -1f;
        return true;
    }

    private static void RemoveMissing(Dictionary<int, float> times, HashSet<int> activeIds)
    {
        if (times.Count == 0)
        {
            return;
        }

        StaleExposureIds.Clear();
        foreach (int id in times.Keys)
        {
            if (!activeIds.Contains(id))
            {
                StaleExposureIds.Add(id);
            }
        }
        foreach (int id in StaleExposureIds)
        {
            times.Remove(id);
        }
        StaleExposureIds.Clear();
    }

    private static bool HasRoofOverhead(Vector3 position)
    {
        return Physics.Raycast(
            position + Vector3.up * RaycastUpOffset,
            Vector3.up,
            RoofCheckDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
    }

    private static bool HasRoofOverhead(Vector3 position, Transform ownStructure)
    {
        return RayHitsOutside(position + Vector3.up * RaycastUpOffset, Vector3.up, RoofCheckDistance, ownStructure);
    }

    private static bool RayHitsOutside(Vector3 origin, Vector3 direction, float distance, Transform ownStructure)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            Collider collider = hit.collider;
            if (collider == null)
            {
                continue;
            }

            if (!collider.transform.IsChildOf(ownStructure))
            {
                return true;
            }
        }
        return false;
    }

    private static void PlayExtinguishSound()
    {
        if (!_playExtinguishSound.Value || !TryLoadExtinguishSound())
        {
            return;
        }

        FMOD.RESULT result = _fmodCoreSystem.playSound(_extinguishSound, default, false, out _);
        if (result != FMOD.RESULT.OK)
        {
            LogFmodWarning($"Could not play WAV (FMOD result: {result}).");
        }
    }
}

internal sealed class TarpCheck
{
    public bool UnderTarp;
    public float NextCheck;
}

internal sealed class SprinklerReset
{
    public float Time;
    public TorchWeaponController? Torch;
    public MolotovWeaponController? Molotov;
    public FireArrowController? Arrow;
}

internal sealed class WarmEffect
{
    public CookingFireNew? Fire;
    public GameObject? Root;
    public FMOD.Studio.EventInstance Sound;
    public bool HasSound;
}

internal sealed class Tracked<T> where T : UnityEngine.Object
{
    public readonly T Target;
    public readonly int Id;
    public float Exposure;
    public float LastCheck;
    public float NextCheck;
    public bool Done;
    public bool StoneRing;
    public float NextStoneCheck;
    public bool UnderCanopy;
    public float NextCanopyCheck;
    public int FirewoodCount;
    public int BonfireLogCount;
    public bool FullFirewood;
    public bool DampFirewood;
    public float NextFuelCheck;
    public float NextLeafStoke;

    public Tracked(T target, int id)
    {
        Target = target;
        Id = id;
    }
}

internal sealed class ComponentRegistry<T> where T : UnityEngine.Object
{
    private readonly Dictionary<int, Tracked<T>> _byId = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _stale = new();

    public readonly List<Tracked<T>> Items = new();

    public bool Add(T target)
    {
        if (target == null)
        {
            return false;
        }

        int id = target.GetInstanceID();
        if (_byId.ContainsKey(id))
        {
            return false;
        }

        Tracked<T> tracked = new(target, id);
        _byId[id] = tracked;
        Items.Add(tracked);
        return true;
    }

    public void Remove(T target)
    {
        if (target == null)
        {
            return;
        }

        RemoveId(target.GetInstanceID());
    }

    public Tracked<T>? TryGet(int id)
    {
        return _byId.TryGetValue(id, out Tracked<T>? tracked) ? tracked : null;
    }

    public void RemoveAt(int index)
    {
        Tracked<T> tracked = Items[index];
        Items.RemoveAt(index);
        _byId.Remove(tracked.Id);
    }

    public void PruneDestroyed()
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Target == null)
            {
                RemoveAt(i);
            }
        }
    }

    public void ResetExposure()
    {
        foreach (Tracked<T> tracked in Items)
        {
            tracked.Exposure = 0f;
            tracked.LastCheck = 0f;
            tracked.NextCheck = 0f;
            tracked.Done = false;
        }
    }

    public int Sync(T[] found)
    {
        int added = 0;
        _seen.Clear();
        foreach (T target in found)
        {
            if (target == null)
            {
                continue;
            }

            int id = target.GetInstanceID();
            _seen.Add(id);
            if (_byId.ContainsKey(id))
            {
                continue;
            }

            Tracked<T> tracked = new(target, id);
            _byId[id] = tracked;
            Items.Add(tracked);
            added++;
        }

        _stale.Clear();
        foreach (int id in _byId.Keys)
        {
            if (!_seen.Contains(id))
            {
                _stale.Add(id);
            }
        }
        foreach (int id in _stale)
        {
            RemoveId(id);
        }
        _stale.Clear();
        _seen.Clear();
        return added;
    }

    private void RemoveId(int id)
    {
        if (!_byId.Remove(id, out Tracked<T>? tracked))
        {
            return;
        }

        Items.Remove(tracked);
    }
}
