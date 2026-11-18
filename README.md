# Rain Extinguishes Fires

RedLoader mod source for Sons of the Forest.

## Build

Install the game's RedLoader/BepInEx interop files, then set `SOTF_GAME_DIR` to the game installation directory or pass `-p:GameDir=<game-directory>` to `dotnet build`.

The project targets `net11.0`, matching the bundled Modstoke runtime. Release builds omit debug symbols so the generated DLL does not carry a local PDB path.

## Mod files

The build places `RainExtinguishesFires.dll` at the output root and copies `manifest.json` and `extinguish.wav` into the `RainExtinguishesFires` subfolder for the RedLoader package layout.

## Configuration

Settings are stored in `UserData/RainExtinguishesFires/` and can be changed from the in-game mod settings.

### Campfires & Fires (`Campfires.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `EnableCampfireExtinguishing` | `true` | | Unsheltered campfires go out in rain or snow. |
| `FireExtinguishTimer` | `5` | 1–60 s | Exposure time before a fire goes out when weather tiers are off. |
| `PlayExtinguishSound` | `true` | | Plays the custom extinguish sound. |
| `ExtinguishSmokeEffect` | `true` | | Plays a short puff of the fire's own smoke particles when rain or snow puts it out. |
| `StoneRingTimerMultiplier` | `1.5` | 1.0–3.0× | Timer multiplier for campfires with a complete stone ring. |

### Weather Intensity Tiers (`WeatherIntensity.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `UseWeatherIntensityTiers` | `false` | | Uses separate timers for light, medium and heavy rain or snow. |
| `LightPrecipitationTimer` | `15` | 1–60 s | Timer during light rain or snow. |
| `MediumPrecipitationTimer` | `5` | 1–60 s | Timer during medium rain or snow. |
| `HeavyPrecipitationTimer` | `2` | 1–60 s | Timer during heavy rain or snow. |
| `ScaleHandheldsWithWeatherTiers` | `false` | | Scales handheld timers by the active tier, relative to the medium timer. |

### Wet Fires & Relighting (`WetFires.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `PreventRelightInRain` | `false` | | Campfires put out by the weather cannot be relit while unsheltered until the rain or snow has stopped for 20 seconds. Changes between rain types do not unlock them. |
| `LightRainWarmGracePeriod` | `4` | 0–15 s | During light rain or snow, the time a campfire or handheld item stays relightable after going out. |
| `BlockIgnitionInHeavyWeather` | `false` | | Unsheltered campfires cannot be lit at all during heavy rain or snow. |

### Rain Sickness (`RainSickness.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `EnableRainSickness` | `false` | | Long exposure to rain or snow makes the player sick. |
| `RainSicknessTimer` | `60` | 10–300 s | Exposure time before sickness. The timer pauses while the player is sheltered. |

### Handheld Flame Items (`HandheldFlameItems.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `ExtinguishHandheldTorch` | `false` | | Held torches go out while the player is unsheltered. |
| `HandheldTorchTimer` | `5` | 1–30 s | Torch timer. |
| `ExtinguishFireArrows` | `false` | | Lit fire arrows go out while the player is unsheltered. |
| `FireArrowTimer` | `5` | 1–30 s | Fire arrow timer. |
| `ExtinguishHeldMolotovs` | `false` | | Lit held molotovs go out while the player is unsheltered. |
| `HeldMolotovTimer` | `3` | 1–30 s | Molotov timer. |
| `PreventHandheldRelightInRain` | `false` | | Torches, molotovs and fire arrows put out by the weather cannot be relit while the player is unsheltered until the rain or snow stops. Uses the same light-rain grace period as campfires. |

## Changelog

### 1.2.0
- Added optional weather intensity tiers with separate light, medium and heavy timers, read from the game's active rain and snow effects.
- Added an option to scale handheld torch, fire arrow and molotov timers with the active weather tier.
- Added a stone ring bonus: campfires with a complete ring of stones last longer in bad weather.
- Added an option to prevent relighting campfires that the weather put out until it clears or a roof covers them, with a short "still warm" grace period during light rain or snow.
- Added the same relight lock as an option for handheld torches, molotovs and fire arrows.
- Added an option to block lighting new unsheltered fires during heavy rain or snow.
- Added a smoke puff when rain or snow puts out a fire.
- The rain sickness timer now pauses under shelter instead of resetting.

### 1.1.1
- Fixed small periodic stutters while it was raining or snowing. Fires and fire arrows are now tracked through `OnEnable`/`OnDisable` hooks instead of repeatedly scanning the scene, and campfire checks are spread across frames.
- If the hooks are unavailable, the mod falls back to a slower periodic scan and logs a warning.
- Removed a one-time diagnostic scan and preloaded the extinguish sound when the game starts.

### 1.1.0
- Added configs (located in UserData) with toggles and timers.
- Added a custom toggleable extinguish sound effect when fires or items are put out (on by default).
- Added configurable Campfire Extinguishing toggle and timer slider (1s – 60s, default 5s).
- Added optional Rain Sickness toggle and exposure timer slider (10s – 300s, off by default).
- Added optional extinguishing for Handheld Torches, Fire Arrows, and Held Molotovs with individual timers (off by default).

Known Issues:

Occasional minor stutters may occur during rain; this is being looked into for a future update.

### 1.0.0
- Initial release.
- Automatically extinguishes campfires and fires after 5 seconds of uninterrupted exposure to rain or snow.
- Checks for overhead roofs and shelters via raycasting to keep covered fires burning.
- Fully compatible with multiplayer (host-only required, untested but should be compatible).
- Safe to install and remove at any time without impacting save games.
