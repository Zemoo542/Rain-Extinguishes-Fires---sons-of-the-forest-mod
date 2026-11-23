# Rain Extinguishes Fires

RedLoader mod source for Sons of the Forest.

## Build

Install RedLoader and launch the game once so it generates `_Redloader/Game`, then set `SOTF_GAME_DIR` to the game installation directory or pass `-p:GameDir=<game-directory>` to `dotnet build`.

The project targets `net6.0`, matching the RedLoader runtime. Release builds omit debug symbols so the generated DLL does not carry a local PDB path.

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
| `StoneRingBonus` | `true` | | Campfires with a complete stone ring (every stone slot filled) last longer. |
| `StoneRingTimerMultiplier` | `1.5` | 1.0–3.0× | Timer multiplier for campfires with a complete stone ring. |

### Weather Intensity Tiers (`WeatherIntensity.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `UseWeatherIntensityTiers` | `false` | | Uses separate timers for light, medium and heavy rain or snow. |
| `LightPrecipitationTimer` | `15` | 1–60 s | Timer during light rain or snow. |
| `MediumPrecipitationTimer` | `5` | 1–60 s | Timer during medium rain or snow. |
| `HeavyPrecipitationTimer` | `2` | 1–60 s | Timer during heavy rain or snow. |
| `ScaleHandheldsWithWeatherTiers` | `true` | | Scales handheld timers by the active tier, relative to the medium timer. Only takes effect when `UseWeatherIntensityTiers` is on. |

### Wet Fires & Relighting (`WetFires.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `PreventRelightInRain` | `false` | | Campfires put out by the weather cannot be relit while unsheltered until the rain or snow has stopped for 20 seconds. Changes between rain types do not unlock them. |
| `LightRainWarmGracePeriod` | `3` | 0–15 s | During light rain or snow, the time a campfire or handheld item stays relightable after going out. Campfires give off small floating embers and a sizzle sound during this time, and a held torch gives off small embers. A relight started inside the grace period always finishes. |
| `BlockIgnitionInHeavyWeather` | `false` | | Unsheltered campfires cannot be lit at all during heavy rain or snow. |

### Shelter & Wind (`Shelter.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `TreeCanopyProtection` | `true` | | Unsheltered campfires with a standing tree within 5 m last longer. |
| `CanopyResistanceMultiplier` | `1.5` | 1.0–3.0× | Timer multiplier for campfires under a tree canopy. |
| `WindDirectionFactor` | `false` | | During windy rain or snow, a roofed campfire still counts as exposed if nothing blocks the rain from the upwind side. |

A campfire under a tarp (including a tarp held up by sticks) always counts as sheltered: it can be lit in any weather and rain or snow never puts it out. This has no setting.

### Fuel & Firewood (`Fuel.cfg`)
| Setting | Default | Range | Description |
|---|---|---|---|
| `FirewoodResistanceBonus` | `true` | | Enables the firewood levels below. Small firewood counts once every fuel slot is filled with it. |
| `FirewoodResistanceMultiplier` | `2.0` | 1.0–10.0× | Small firewood. |
| `StoneRingFirewoodMultiplier` | `3.0` | 1.0–10.0× | Full stone ring + small firewood. |
| `LogLayerBonus` | `true` | | Enables the full-log levels below. Layer one is the first 2 full logs, layer two the next 4. |
| `OneLogLayerMultiplier` | `3.5` | 1.0–10.0× | Small firewood + one layer of full logs. |
| `StoneRingOneLogLayerMultiplier` | `4.0` | 1.0–10.0× | Full stone ring + small firewood + one layer of full logs. |
| `TwoLogLayersMultiplier` | `5.0` | 1.0–10.0× | Small firewood + two layers of full logs. |
| `StoneRingBonfireNeverExtinguishes` | `true` | | Full stone ring + small firewood + two layers of full logs: rain and snow can't put the fire out (wind ignored). It still burns through its fuel normally. |
| `StoneRingTwoLogLayersMultiplier` | `6.0` | 1.0–10.0× | Full stone ring + small firewood + two layers of full logs when `StoneRingBonfireNeverExtinguishes` is off. |

Each level's multiplier is the total for that setup and does not stack with the stone ring multiplier. Tree canopy protection still stacks on top. A stone ring without a full load of small firewood uses `StoneRingTimerMultiplier`.
| `WetFirewoodPenalty` | `false` | | Firewood added to an unroofed campfire while it is raining or snowing is damp and gives half the bonus (2.0× becomes 1.5×) until it burns away. |
| `LeafStokeExtension` | `false` | | Adding leaves to a lit campfire in rain or snow removes some of its built-up exposure. |
| `LeafStokeSeconds` | `1.0` | 0.5–5.0 s | Exposure removed per leaf stoke. Stokes on the same fire count once every 5 seconds, or every 2× this value if longer. |

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

### 1.4.0
- Fixed handheld torches, molotovs and fire arrows playing the lighting animation and sound when relighting was blocked. Blocked attempts are now stopped before the animation starts.
- The "too wet to light" message now uses the game's own notification again.
- A held torch now shows small still-warm embers during the light-rain grace period.
- `PreventHandheldRelightInRain` is now off by default and `ScaleHandheldsWithWeatherTiers` is on by default (it only takes effect when `UseWeatherIntensityTiers` is on).
- Added `StoneRingTwoLogLayersMultiplier` (6× by default), used for stone ring + small firewood + two log layers when `StoneRingBonfireNeverExtinguishes` is off.
- Campfires under a tarp, including a tarp held up by 4 sticks, now count as sheltered: they can be lit and stay lit in any weather.
- Added full-log fuel levels: small firewood + one layer of full logs (3.5×, or 4× with a stone ring) and small firewood + two layers (5×).
- A campfire with a full stone ring, small firewood and two layers of full logs can no longer be put out by rain or snow.
- Stone ring + small firewood now has its own configurable multiplier (3× by default).
- Full logs are no longer counted as small firewood.

### 1.3.1
- Rebuilt for .NET 6 so the mod loads on standard RedLoader. The previous build targeted a newer .NET and could stop RedLoader from loading other mods.

### 1.3.0
- Added still-warm embers: during the light-rain grace period, a campfire that just went out gives off the fire's own small floating embers and sizzles using the game's campfire sound at a slightly higher volume. Enabled automatically with `PreventRelightInRain`.
- Added tree canopy protection (on by default): unsheltered campfires under a tree canopy last 1.5× longer.
- Added a firewood bonus (on by default): campfires with a full load of firewood last 2.0× longer. Stacks with the stone ring and canopy bonuses.
- Added a `StoneRingBonus` toggle (on by default) so the stone ring bonus can be switched off without touching its multiplier.
- `PreventHandheldRelightInRain` is now on by default. Handheld items use the light-rain grace period without the campfire ember and sizzle effects.
- Stone ring and firewood checks only run when their setting is on.
- `LightRainWarmGracePeriod` now defaults to 3 seconds.
- Fixed the light-rain grace period never ending for campfires, which also stopped the still-warm effects from showing.
- Fixed handheld items staying unlightable after the rain put them out, even during the grace period.
- Tree canopy detection now uses the game's own tree map, so it works next to any standing tree.
- Blocked lighting is now stopped before the lighter animation starts, and a warning appears in the top-right corner of the screen.
- A relight started during the grace period always finishes, even if the grace period runs out mid-animation.
- Added an optional wet firewood penalty that halves the firewood bonus for logs added in the rain.
- Added optional leaf stoking: adding leaves to a lit campfire in bad weather buys back some time, with a cooldown so it can't be spammed.
- Added an optional wind direction factor: windy rain can reach roofed campfires that are open on the upwind side.

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
- Initial public release.
