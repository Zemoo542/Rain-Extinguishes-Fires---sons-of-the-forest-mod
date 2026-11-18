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
    private const float BlockedMessageDuration = 2f;
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
    private static ConfigEntry<bool> _campfireEnabled = null!;
    private static ConfigEntry<float> _fireTimer = null!;
    private static ConfigEntry<bool> _playExtinguishSound = null!;
    private static ConfigEntry<bool> _smokeEffectEnabled = null!;
    private static ConfigEntry<float> _stoneRingMultiplier = null!;
    private static ConfigEntry<bool> _useWeatherTiers = null!;
    private static ConfigEntry<float> _lightTimer = null!;
    private static ConfigEntry<float> _mediumTimer = null!;
    private static ConfigEntry<float> _heavyTimer = null!;
    private static ConfigEntry<bool> _scaleHandheldsWithTiers = null!;
    private static ConfigEntry<bool> _preventRelightInRain = null!;
    private static ConfigEntry<float> _warmGracePeriod = null!;
    private static ConfigEntry<bool> _blockIgnitionInHeavyWeather = null!;
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
    }

    private static void InitializeConfig()
    {
        ConfigCategory campfires = CreateCategory("Campfires", "Campfires & Fires");
        ConfigCategory weatherTiers = CreateCategory("WeatherIntensity", "Weather Intensity Tiers");
        ConfigCategory wetFires = CreateCategory("WetFires", "Wet Fires & Relighting");
        ConfigCategory rainSickness = CreateCategory("RainSickness", "Rain Sickness");
        ConfigCategory handheld = CreateCategory("HandheldFlameItems", "Handheld Flame Items");

        _campfireEnabled = CreateEntry(campfires, "EnableCampfireExtinguishing", "Enable Campfire Extinguishing", true);
        _fireTimer = CreateEntry(campfires, "FireExtinguishTimer", "Fire Extinguish Timer", 5f, 1f, 60f);
        _playExtinguishSound = CreateEntry(campfires, "PlayExtinguishSound", "Play Extinguish Sound", true);
        _smokeEffectEnabled = CreateEntry(campfires, "ExtinguishSmokeEffect", "Extinguish Smoke Effect", true);
        _stoneRingMultiplier = CreateEntry(campfires, "StoneRingTimerMultiplier", "Stone Ring Timer Multiplier", 1.5f, 1f, 3f);

        _useWeatherTiers = CreateEntry(weatherTiers, "UseWeatherIntensityTiers", "Use Weather Intensity Tiers", false);
        _lightTimer = CreateEntry(weatherTiers, "LightPrecipitationTimer", "Light Precipitation Timer", 15f, 1f, 60f);
        _mediumTimer = CreateEntry(weatherTiers, "MediumPrecipitationTimer", "Medium Precipitation Timer", 5f, 1f, 60f);
        _heavyTimer = CreateEntry(weatherTiers, "HeavyPrecipitationTimer", "Heavy Precipitation Timer", 2f, 1f, 60f);
        _scaleHandheldsWithTiers = CreateEntry(weatherTiers, "ScaleHandheldsWithWeatherTiers", "Scale Handhelds With Weather Tiers", false);

        _preventRelightInRain = CreateEntry(wetFires, "PreventRelightInRain", "Prevent Relight In Rain", false);
        _warmGracePeriod = CreateEntry(wetFires, "LightRainWarmGracePeriod", "Light Rain Warm Grace Period", 4f, 0f, 15f);
        _blockIgnitionInHeavyWeather = CreateEntry(wetFires, "BlockIgnitionInHeavyWeather", "Block Ignition In Heavy Weather", false);

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

    private static ConfigEntry<T> CreateEntry<T>(ConfigCategory category, string id, string displayName, T defaultValue)
    {
        return category.CreateEntry(id, defaultValue, displayName, displayName);
    }

    private static ConfigEntry<float> CreateEntry(ConfigCategory category, string id, string displayName, float defaultValue, float minimum, float maximum)
    {
        var entry = category.CreateEntry(id, defaultValue, displayName, displayName);
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
        if (TryPatchPrefix(typeof(TorchWeaponController), "Burn", nameof(BeforeTorchIgnition)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(MolotovWeaponController), "OnLightHeldItem", nameof(BeforeMolotovIgnition)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(MolotovWeaponController), "Burn", nameof(BeforeMolotovIgnition)))
        {
            handheldHooks++;
        }
        if (TryPatchPrefix(typeof(FireArrowController), "Burn", nameof(BeforeFireArrowIgnition)))
        {
            handheldHooks++;
        }

        RLog.Msg($"[RainExtinguishesFires] Fire tracking hooks: fires={_fireHooksActive}, fireArrows={_fireArrowHooksActive}, ignition={ignitionHooks}/4, handheldIgnition={handheldHooks}/5.");
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

    private static void OnFireEnabled(CookingFireNew __instance)
    {
        _fireHooksObserved = true;
        Fires.Add(__instance);
    }

    private static void OnFireDisabled(CookingFireNew __instance)
    {
        _fireHooksObserved = true;
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
        try
        {
            if (lightableFire == null)
            {
                return true;
            }

            CookingFireNew? fire = lightableFire.TryCast<CookingFireNew>();
            return fire == null || !ShouldBlockIgnition(fire);
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
            return __instance == null || !ShouldBlockIgnition(__instance);
        }
        catch (Exception exception)
        {
            LogIgnitionWarning(exception);
            return true;
        }
    }

    private static bool BeforeTorchIgnition()
    {
        return AllowHandheldIgnition(HandheldKind.Torch);
    }

    private static bool BeforeMolotovIgnition()
    {
        return AllowHandheldIgnition(HandheldKind.Molotov);
    }

    private static bool BeforeFireArrowIgnition()
    {
        return AllowHandheldIgnition(HandheldKind.FireArrow);
    }

    private static bool AllowHandheldIgnition(HandheldKind kind)
    {
        try
        {
            if (!_gameStarted || !_preventHandheldRelightInRain.Value || !WetHandhelds.Contains(kind))
            {
                return true;
            }

            if (IsPlayerSheltered())
            {
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

        if (HasRoofOverhead(fire.transform.position))
        {
            WetFires.Remove(id);
            PendingWetFires.Remove(id);
            return false;
        }

        ShowBlockedMessage(wet ? "The wood is soaked. Find shelter or wait for the weather to clear." : "The rain is too heavy to light a fire out here.");
        return true;
    }

    private static void ShowBlockedMessage(string message)
    {
        float now = Time.time;
        if (now - _lastBlockedMessageTime < BlockedMessageCooldown)
        {
            return;
        }

        _lastBlockedMessageTime = now;
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
            PendingWetFires.Remove(id);
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
        }
        ReadyWetHandhelds.Clear();
    }

    private static void MarkFireWet(int id)
    {
        if (!_preventRelightInRain.Value)
        {
            return;
        }

        if (IsInWarmGracePeriod())
        {
            PendingWetFires[id] = Time.time + _warmGracePeriod.Value;
            return;
        }

        PendingWetFires.Remove(id);
        WetFires.Add(id);
    }

    private static void MarkHandheldWet(HandheldKind kind)
    {
        if (!_preventHandheldRelightInRain.Value)
        {
            return;
        }

        if (IsInWarmGracePeriod())
        {
            PendingWetHandhelds[kind] = Time.time + _warmGracePeriod.Value;
            return;
        }

        PendingWetHandhelds.Remove(kind);
        WetHandhelds.Add(kind);
    }

    private static void ClearHandheldWet(HandheldKind kind)
    {
        WetHandhelds.Remove(kind);
        PendingWetHandhelds.Remove(kind);
    }

    private static bool IsInWarmGracePeriod()
    {
        return _tier == PrecipitationTier.Light && _warmGracePeriod.Value > 0f;
    }

    private static void ClearWetFires()
    {
        WetFires.Clear();
        PendingWetFires.Clear();
    }

    private static void ClearWetHandhelds()
    {
        WetHandhelds.Clear();
        PendingWetHandhelds.Clear();
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
            if ((WetFires.Contains(id) || PendingWetFires.ContainsKey(id)) && HasRoofOverhead(fire.transform.position))
            {
                WetFires.Remove(id);
                PendingWetFires.Remove(id);
            }
            return;
        }

        if (!tracked.Done)
        {
            WetFires.Remove(id);
            PendingWetFires.Remove(id);
        }

        if (HasRoofOverhead(fire.transform.position))
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
        float requiredTime = GetPrecipitationTimer();
        if (_stoneRingMultiplier.Value > 1.001f && HasFullStoneRing(tracked, fire, now))
        {
            requiredTime *= _stoneRingMultiplier.Value;
        }

        if (tracked.Exposure < requiredTime)
        {
            return;
        }

        tracked.Exposure = 0f;
        tracked.Done = true;
        MarkFireWet(id);
        if (!_isAuthority)
        {
            return;
        }

        PlaySmokePuff(fire);
        fire.SetPutOut();
        PlayExtinguishSound();
        RLog.Msg($"[RainExtinguishesFires] Fire {id} extinguished by rain/snow ({_tier}).");
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
            MarkHandheldWet(HandheldKind.Torch);
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
