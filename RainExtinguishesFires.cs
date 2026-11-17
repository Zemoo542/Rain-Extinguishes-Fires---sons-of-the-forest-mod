using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Construction;
using RedLoader;
using Sons.Gameplay;
using Sons.Weapon;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Utils;
using TheForest.World;
using UnityEngine;

namespace RainExtinguishesFires;

public class RainExtinguishesFiresMod : SonsMod, IOnUpdateReceiver
{
    private const float UpdateInterval = 1f;
    private const float FireRefreshInterval = 4f;
    private const float FireArrowRefreshInterval = 2f;
    private const float RoofCheckDistance = 12f;
    private const float RaycastUpOffset = 0.35f;
    private static readonly Dictionary<int, float> FireExposureTimes = new();
    private static readonly Dictionary<int, float> TorchExposureTimes = new();
    private static readonly Dictionary<int, float> FireArrowExposureTimes = new();
    private static readonly Dictionary<int, float> MolotovExposureTimes = new();
    private static readonly HashSet<int> ActiveFireIds = new();
    private static readonly HashSet<int> ActiveTorchIds = new();
    private static readonly HashSet<int> ActiveFireArrowIds = new();
    private static readonly HashSet<int> ActiveMolotovIds = new();
    private static readonly List<int> StaleExposureIds = new();
    private static CookingFireNew[] _cachedFires = Array.Empty<CookingFireNew>();
    private static FireArrowController[] _cachedFireArrows = Array.Empty<FireArrowController>();
    private static float _fireRefreshElapsed;
    private static float _fireArrowRefreshElapsed;
    private static bool _fireCacheInitialized;
    private static bool _fireArrowCacheInitialized;
    private static ConfigEntry<bool> _campfireEnabled = null!;
    private static ConfigEntry<float> _fireTimer = null!;
    private static ConfigEntry<bool> _playExtinguishSound = null!;
    private static ConfigEntry<bool> _rainSicknessEnabled = null!;
    private static ConfigEntry<float> _rainSicknessTimer = null!;
    private static ConfigEntry<bool> _torchEnabled = null!;
    private static ConfigEntry<float> _torchTimer = null!;
    private static ConfigEntry<bool> _fireArrowsEnabled = null!;
    private static ConfigEntry<float> _fireArrowTimer = null!;
    private static ConfigEntry<bool> _molotovEnabled = null!;
    private static ConfigEntry<float> _molotovTimer = null!;
    private static string? _extinguishSoundPath;
    private static FMOD.System _fmodCoreSystem;
    private static FMOD.Sound _extinguishSound;
    private static bool _extinguishSoundLoaded;
    private static bool _fmodWarningLogged;
    private static float _rainSicknessExposure;
    private static bool _loggedHeldItemDiagnostic;
    private static bool _loggedPlayerExposureDiagnostic;
    private bool _hasLoggedUpdate;
    private float _updateAccumulator;

    protected override void OnInitializeMod()
    {
        InitializeConfig();
        InitializeAudio();
        RLog.Msg("[RainExtinguishesFires] Mod initialized.");
    }

    public void OnUpdate()
    {
        if (!_hasLoggedUpdate)
        {
            RLog.Msg("[RainExtinguishesFires] Live update callback active.");
            _hasLoggedUpdate = true;
        }

        _updateAccumulator += Time.deltaTime;
        if (_updateAccumulator < UpdateInterval)
        {
            return;
        }

        float elapsed = _updateAccumulator;
        _updateAccumulator = 0f;
        UpdateLiveState(elapsed);
    }

    private static void InitializeConfig()
    {
        ConfigCategory campfires = CreateCategory("Campfires", "Campfires & Fires");
        ConfigCategory rainSickness = CreateCategory("RainSickness", "Rain Sickness");
        ConfigCategory handheld = CreateCategory("HandheldFlameItems", "Handheld Flame Items");

        _campfireEnabled = CreateEntry(campfires, "EnableCampfireExtinguishing", "Enable Campfire Extinguishing", true);
        _fireTimer = CreateEntry(campfires, "FireExtinguishTimer", "Fire Extinguish Timer", 5f, 1f, 60f);
        _playExtinguishSound = CreateEntry(campfires, "PlayExtinguishSound", "Play Extinguish Sound", true);

        _rainSicknessEnabled = CreateEntry(rainSickness, "EnableRainSickness", "Enable Rain Sickness", false);
        _rainSicknessTimer = CreateEntry(rainSickness, "RainSicknessTimer", "Rain Sickness Timer", 60f, 10f, 300f);

        _torchEnabled = CreateEntry(handheld, "ExtinguishHandheldTorch", "Extinguish Handheld Torch", false);
        _torchTimer = CreateEntry(handheld, "HandheldTorchTimer", "Handheld Torch Timer", 5f, 1f, 30f);
        _fireArrowsEnabled = CreateEntry(handheld, "ExtinguishFireArrows", "Extinguish Fire Arrows", false);
        _fireArrowTimer = CreateEntry(handheld, "FireArrowTimer", "Fire Arrow Timer", 5f, 1f, 30f);
        _molotovEnabled = CreateEntry(handheld, "ExtinguishHeldMolotovs", "Extinguish Held Molotovs", false);
        _molotovTimer = CreateEntry(handheld, "HeldMolotovTimer", "Held Molotov Timer", 3f, 1f, 30f);

        campfires.LoadFromFile(false);
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

        if (_extinguishSoundPath == null)
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
        WeatherSystem weather = WeatherSystem.Instance;
        bool badWeather = weather != null && (weather.IsRaining || weather.IsSnowing);
        GameObject player = LocalPlayer.PlayerBase;
        Transform playerCamera = LocalPlayer.MainCamTr;
        bool handheldEnabled = _torchEnabled.Value || _fireArrowsEnabled.Value || _molotovEnabled.Value;
        bool needsPlayerExposure = _rainSicknessEnabled.Value || handheldEnabled;
        bool hasRoof = false;
        if (player != null && badWeather && needsPlayerExposure)
        {
            Vector3 exposurePosition = playerCamera != null ? playerCamera.position : player.transform.position;
            hasRoof = HasRoofOverhead(exposurePosition);
        }
        bool playerExposed = player != null && badWeather && needsPlayerExposure && !hasRoof;

        if (!_loggedPlayerExposureDiagnostic && badWeather && needsPlayerExposure)
        {
            bool rootHasRoof = player != null && HasRoofOverhead(player.transform.position);
            Vector3 exposurePosition = playerCamera != null ? playerCamera.position : player != null ? player.transform.position : Vector3.zero;
            RLog.Msg($"[RainExtinguishesFires] Player exposure check: player={player != null}, camera={playerCamera != null}, rootRoof={rootHasRoof}, selectedRoof={hasRoof}, exposed={playerExposed}, position={exposurePosition}.");
            _loggedPlayerExposureDiagnostic = true;
        }

        if (!_loggedHeldItemDiagnostic && handheldEnabled && playerExposed)
        {
            LogHeldItemDiagnostic();
            _loggedHeldItemDiagnostic = true;
        }

        UpdateRainSickness(playerExposed, deltaTime);
        if (_campfireEnabled.Value && badWeather)
        {
            UpdateCampfires(true, deltaTime);
        }
        else
        {
            FireExposureTimes.Clear();
            _cachedFires = Array.Empty<CookingFireNew>();
            _fireRefreshElapsed = 0f;
            _fireCacheInitialized = false;
        }

        if (handheldEnabled && playerExposed)
        {
            UpdateHandheldItems(true, deltaTime);
        }
        else
        {
            TorchExposureTimes.Clear();
            FireArrowExposureTimes.Clear();
            MolotovExposureTimes.Clear();
            _cachedFireArrows = Array.Empty<FireArrowController>();
            _fireArrowRefreshElapsed = 0f;
            _fireArrowCacheInitialized = false;
        }
    }

    private static void UpdateRainSickness(bool exposed, float deltaTime)
    {
        Vitals vitals = LocalPlayer.Vitals;
        if (!_rainSicknessEnabled.Value || vitals == null)
        {
            _rainSicknessExposure = 0f;
            return;
        }

        if (!exposed || vitals.IsSick)
        {
            _rainSicknessExposure = 0f;
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

    private static void UpdateCampfires(bool badWeather, float deltaTime)
    {
        ActiveFireIds.Clear();
        _fireRefreshElapsed += deltaTime;
        if (!_fireCacheInitialized || _fireRefreshElapsed >= FireRefreshInterval)
        {
            _cachedFires = UnityEngine.Object.FindObjectsByType<CookingFireNew>(FindObjectsSortMode.None);
            _fireRefreshElapsed = 0f;
            _fireCacheInitialized = true;
        }

        foreach (CookingFireNew fire in _cachedFires)
        {
            if (fire == null)
            {
                continue;
            }

            int fireId = fire.GetInstanceID();
            ActiveFireIds.Add(fireId);
            if (!_campfireEnabled.Value || !fire.IsLit || !badWeather || HasRoofOverhead(fire.transform.position))
            {
                FireExposureTimes.Remove(fireId);
                continue;
            }

            if (!Accumulate(FireExposureTimes, fireId, deltaTime, _fireTimer.Value))
            {
                continue;
            }

            FireExposureTimes.Remove(fireId);
            if (BoltNetwork.isServerOrNotRunning)
            {
                fire.SetPutOut();
                PlayExtinguishSound();
                RLog.Msg($"[RainExtinguishesFires] Fire {fireId} extinguished by rain/snow.");
            }
        }

        RemoveMissing(FireExposureTimes, ActiveFireIds);
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

        var leftItem = inventory.LeftHandItem;
        var rightItem = inventory.RightHandItem;
        GameObject? leftItemObject = leftItem == null ? null : leftItem.ItemObject;
        GameObject? rightItemObject = rightItem == null ? null : rightItem.ItemObject;
        UpdateTorches(leftItemObject, rightItemObject, exposed, deltaTime);
        UpdateFireArrows(exposed, deltaTime);
        UpdateMolotovs(leftItemObject, rightItemObject, exposed, deltaTime);
    }

    private static void LogHeldItemDiagnostic()
    {
        var inventory = LocalPlayer.Inventory;
        GameObject? leftItem = inventory == null || inventory.LeftHandItem == null ? null : inventory.LeftHandItem.ItemObject;
        GameObject? rightItem = inventory == null || inventory.RightHandItem == null ? null : inventory.RightHandItem.ItemObject;
        int torchCount = UnityEngine.Object.FindObjectsOfType<TorchWeaponController>().Length;
        int molotovCount = UnityEngine.Object.FindObjectsOfType<MolotovWeaponController>().Length;
        int fireArrowCount = UnityEngine.Object.FindObjectsOfType<FireArrowController>().Length;
        RLog.Msg($"[RainExtinguishesFires] Held-item scan: inventory={inventory != null}, left={DescribeItemObject(leftItem)}, right={DescribeItemObject(rightItem)}, scene controllers torch={torchCount}, molotov={molotovCount}, fireArrow={fireArrowCount}.");
    }

    private static string DescribeItemObject(GameObject? itemObject)
    {
        if (itemObject == null)
        {
            return "none";
        }

        int torches = itemObject.GetComponentsInChildren<TorchWeaponController>(true).Length;
        int molotovs = itemObject.GetComponentsInChildren<MolotovWeaponController>(true).Length;
        return $"{itemObject.name}(torch={torches},molotov={molotovs})";
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

        if (Accumulate(TorchExposureTimes, id, deltaTime, _torchTimer.Value))
        {
            torch.OnSprinklersInfluenceCountChanged(1);
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

        if (Accumulate(MolotovExposureTimes, id, deltaTime, _molotovTimer.Value))
        {
            molotov.OnSprinklersInfluenceCountChanged(1);
            PlayExtinguishSound();
        }
    }

    private static void UpdateFireArrows(bool exposed, float deltaTime)
    {
        ActiveFireArrowIds.Clear();
        if (_fireArrowsEnabled.Value && exposed)
        {
            _fireArrowRefreshElapsed += deltaTime;
            if (!_fireArrowCacheInitialized || _fireArrowRefreshElapsed >= FireArrowRefreshInterval)
            {
                _cachedFireArrows = UnityEngine.Object.FindObjectsByType<FireArrowController>(FindObjectsSortMode.None);
                _fireArrowRefreshElapsed = 0f;
                _fireArrowCacheInitialized = true;
            }

            foreach (FireArrowController arrow in _cachedFireArrows)
            {
                if (arrow == null || !arrow.gameObject.activeInHierarchy)
                {
                    continue;
                }

                int id = arrow.GetInstanceID();
                ActiveFireArrowIds.Add(id);
                if (!arrow.IsBurning)
                {
                    FireArrowExposureTimes.Remove(id);
                    continue;
                }

                if (!Accumulate(FireArrowExposureTimes, id, deltaTime, _fireArrowTimer.Value))
                {
                    continue;
                }

                arrow.OnSprinklersInfluenceCountChanged(1);
                PlayExtinguishSound();
            }
        }
        else
        {
            _cachedFireArrows = Array.Empty<FireArrowController>();
            _fireArrowRefreshElapsed = 0f;
            _fireArrowCacheInitialized = false;
        }
        RemoveMissing(FireArrowExposureTimes, ActiveFireArrowIds);
    }

    private static bool Accumulate(Dictionary<int, float> times, int id, float deltaTime, float requiredTime)
    {
        if (times.TryGetValue(id, out float elapsed) && elapsed < 0f)
        {
            return false;
        }

        times.TryGetValue(id, out elapsed);
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
