using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Construction;
using HarmonyLib;
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
    private const float FireCheckInterval = 1f;
    private const int FireChecksPerFrame = 4;
    private const float MaxExposureStep = 2f;
    private const float FallbackFireScanInterval = 30f;
    private const float FallbackFireArrowScanInterval = 2f;
    private const float RoofCheckDistance = 12f;
    private const float RaycastUpOffset = 0.35f;
    private static readonly Dictionary<int, float> TorchExposureTimes = new();
    private static readonly Dictionary<int, float> FireArrowExposureTimes = new();
    private static readonly Dictionary<int, float> MolotovExposureTimes = new();
    private static readonly HashSet<int> ActiveTorchIds = new();
    private static readonly HashSet<int> ActiveFireArrowIds = new();
    private static readonly HashSet<int> ActiveMolotovIds = new();
    private static readonly List<int> StaleExposureIds = new();
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
    private static bool _badWeather;
    private static bool _wasBadWeather;
    private static bool _isAuthority = true;
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

    protected override void OnGameStart()
    {
        _fireCursor = 0;
        _wasBadWeather = false;
        _fallbackFireScanPending = true;
        _fallbackFireArrowScanPending = true;
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

        UpdateCampfireSlice(Time.time);
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

    private static void InitializeHooks()
    {
        try
        {
            _harmony = new HarmonyLib.Harmony("Zemo.RainExtinguishesFires");
        }
        catch (Exception exception)
        {
            RLog.Warning($"[RainExtinguishesFires] Harmony unavailable, using periodic scans instead: {exception.Message}");
            _fireScanFallback = true;
            _fireArrowScanFallback = true;
            return;
        }

        _fireHooksActive = TryPatchLifecycle(typeof(CookingFireNew), nameof(OnFireEnabled), nameof(OnFireDisabled));
        _fireArrowHooksActive = TryPatchLifecycle(typeof(FireArrowController), nameof(OnFireArrowEnabled), nameof(OnFireArrowDisabled));
        _fireScanFallback = !_fireHooksActive;
        _fireArrowScanFallback = !_fireArrowHooksActive;
        RLog.Msg($"[RainExtinguishesFires] Fire tracking hooks: fires={_fireHooksActive}, fireArrows={_fireArrowHooksActive}.");
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
        WeatherSystem weather = WeatherSystem.Instance;
        _badWeather = weather != null && (weather.IsRaining || weather.IsSnowing);
        _isAuthority = BoltNetwork.isServerOrNotRunning;
        bool weatherStarted = _badWeather && !_wasBadWeather;
        bool weatherEnded = !_badWeather && _wasBadWeather;
        _wasBadWeather = _badWeather;

        if (weatherEnded)
        {
            Fires.ResetExposure();
        }

        if (_campfireEnabled.Value && _badWeather)
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
        bool playerExposed = false;
        if (_badWeather && needsPlayerExposure)
        {
            GameObject player = LocalPlayer.PlayerBase;
            if (player != null)
            {
                Transform playerCamera = LocalPlayer.MainCamTr;
                Vector3 exposurePosition = playerCamera != null ? playerCamera.position : player.transform.position;
                playerExposed = !HasRoofOverhead(exposurePosition);
            }
        }

        UpdateRainSickness(playerExposed, deltaTime);

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

    private static void UpdateRainSickness(bool exposed, float deltaTime)
    {
        if (!_rainSicknessEnabled.Value)
        {
            _rainSicknessExposure = 0f;
            return;
        }

        Vitals vitals = LocalPlayer.Vitals;
        if (vitals == null || !exposed || vitals.IsSick)
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

    private static void UpdateCampfireSlice(float now)
    {
        if (!_campfireEnabled.Value || !_badWeather)
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

        if (!fire.IsLit || HasRoofOverhead(fire.transform.position))
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
        if (tracked.Exposure < _fireTimer.Value)
        {
            return;
        }

        tracked.Exposure = 0f;
        tracked.Done = true;
        if (_isAuthority)
        {
            fire.SetPutOut();
            PlayExtinguishSound();
            RLog.Msg($"[RainExtinguishesFires] Fire {tracked.Id} extinguished by rain/snow.");
        }
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

                if (!Accumulate(FireArrowExposureTimes, id, deltaTime, _fireArrowTimer.Value))
                {
                    continue;
                }

                arrow.OnSprinklersInfluenceCountChanged(1);
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
