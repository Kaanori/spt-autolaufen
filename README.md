# AutoLaufen (auto run)

**Tap W twice quickly** — and the player character keeps running forward,
as if W were held down. **Press W (or A, S, D, Space) once** — and it
stops immediately.

Mouse, looking around, reloading, leaning, weapon switching and everything
else do **not** disturb the run. Only what is on the cancel list ends it.
**The inventory no longer stops you** — you keep running while you sort.

## Requirements

- SPT 4.1.6 (EFT 0.16.9). BepInEx ships with SPT.

## Installation

1. Download `AutoLaufen-1.2.0.zip` from the [Releases](https://github.com/Kaanori/spt-autolaufen/releases) page.
2. Extract it **into your SPT game folder** (the folder that contains `EscapeFromTarkov.exe`).
   The ZIP already has the right layout; the mod ends up in `BepInEx/plugins/`.
   On Windows, make sure "Extract All" does not add an extra subfolder.
3. Restart the game.

## Uninstall

Delete the mod's files from `BepInEx/plugins/` (settings: `BepInEx/config/` for client mods) and restart the game.

## Usage

| What | Effect |
|---|---|
| W, W (quickly in succession) | Continuous run on |
| W, A, S, D or Space | Continuous run off immediately |
| Home | On/off, in addition to the double tap |
| Tab (inventory) | **no longer stops you** |
| Death, end of raid, leaving the map | Off |
| Mouse, all other keys | **no** effect on the run |

The time window for the double tap is **0.35 seconds** by default.

## Changing keys: the + button

**F12 -> Auto-Laufen.** For the three key lists there is a button
**"+ Taste hinzufuegen"** (add key) below the entries:

1. Click **+ Taste hinzufuegen**.
2. *"Druecke jetzt eine Taste oder Maustaste ..."* (press a key or mouse
   button now) appears.
3. Press the desired key — **mouse buttons work just as well**, including
   the thumb side buttons.
4. Done, it is in the list.

To remove an entry, use the **X** next to it. **Esc** cancels recording.

If you hold **Ctrl, Alt or Shift** while recording, it becomes a
combination (`LeftControl+W`). A modifier key alone (just `LeftShift`) can
also be assigned.

The click on the + button does **not** register itself as a mouse button —
the plugin first waits until you have released everything. So if you really
want to assign the left mouse button, just release briefly after the click
and then click anywhere.

The three lists are also stored as text in
`BepInEx/config/com.kaan.autolaufen.cfg` — you can maintain them there
with a text editor too (`W, A, S, D, Space`).

## Settings (F12 -> Auto-Laufen)

### 1. Allgemein (general)
| Setting | Default | Meaning |
|---|---|---|
| **Eingeschaltet** (enabled) | on | Off: no key does anything |
| **Weiterlaufen mit offenem Inventar** (keep running with inventory open) | on | Off: the inventory stops you again, as in the stock game |

### 2. Starten (start)
| Setting | Default | Meaning |
|---|---|---|
| **Startertasten** (starter keys) | `Home` | List. Each one switches the run on and off |
| **Doppeltipp verwenden** (use double tap) | on | Two quick taps start the run |
| **Doppeltipptasten** (double-tap keys) | `W` | List. Each one starts the run via double tap |
| **Doppeltipp-Zeitfenster** (double-tap time window) | 0.35 s | 0.1 to 1.0 |
| **Betriebsart der Startertasten** (mode of the starter keys) | Umschalten | `Umschalten` (toggle) = on/off per press, `Halten` (hold) = only while held down |

### 3. Stoppen (stop)
| Setting | Default |
|---|---|
| **Abbruchtasten** (cancel keys) | `W, A, S, D, Space` |

Anything not listed never disturbs the run. Empty list = only the starter
keys end it.

### 4. Verhalten (behaviour) / 5. Anzeige (display) / 6. Diagnose (diagnostics)
| Setting | Default | Meaning |
|---|---|---|
| **Dauerhaft sprinten** (sprint permanently) | **off** | On: sprint instead of walk — see warning below |
| **Hinweis einblenden** (show notice) | on | "Dauerlauf" (continuous run) at the top left while it is running |
| **Protokoll** (log) | off | Writes every on/off **including the trigger** to the BepInEx console |

If the run ends too early: turn on the log. The console then shows
`aus (Abbruchtaste C)` (off, cancel key C) — so you see exactly who was to
blame.

**About sprinting:** Sprint eats stamina. When it is empty, the game falls
back to walking, the plugin switches sprint back on as soon as it can —
and that then visibly stutters. Hence off by default.

**About key choice:** The plugin reads the keyboard directly from Unity,
bypassing Tarkov's own key bindings. If you have remapped movement in the
game (say to ESDF), you must adjust the lists by hand.

## Three quirks that are intentional

**In the inventory you run straight ahead but do not steer.** The mouse
belongs to the inventory there — turning and strafing would only be in the
way while sorting.

**The cancel keys also apply in the inventory.** If you type into a search
field there, a `W` or `A` ends the run. That is deliberate: an emergency
brake that sometimes does not work is none at all.

**After stopping with W it takes three taps, not two.** Anyone who ends
the running continuous run with W should not accidentally restart it with
the next tap. So every cancel clears the tap history.

**In "Halten" (hold) mode the cancel keys have no effect.** There the held
key says "run" anew every frame — a cancel would be reverted in the next
frame and would only flicker. Releasing ends the run.

## How it works

### The movement

In EFT, keyboard and mouse do not go directly to the player character. The
`InputManager` collects them every frame into a `float[]` with seven axes
(`EAxis`: MoveX, MoveY, TurnX, TurnY, LookX, LookY, LeanX) and sends this
array through:

    GamePlayerOwner.TranslateAxes(ref float[] axes)
      -> PlayerOwner.TranslateAxes
        -> _moveInputTranslator.TranslateAxes
          -> Player.Move(Vector2)

`axes[1]` is MoveY: `+1` means "W pressed", `-1` means "S". The plugin
attaches a Harmony prefix **before** this method and writes a `1` there.
From then on the rest of the game is oblivious — stamina, footstep sounds,
animation and inertia behave as with a real key press.

**Why the mouse does not interfere:** only `axes[1]` is touched. Strafing
(`axes[0]`), mouse and looking around (`axes[2]` to `axes[5]`) and leaning
(`axes[6]`) pass through unchanged.

**Why not patch `Player.Move()`?** That is the more obvious spot, but it
sits *behind* the translator. Speed level (mouse wheel), sway step and
`IsForwardInputDirection` are already computed from the old direction
there. The result would be a character that runs while its state machine
does not notice.

### Why you stop in the inventory

The input travels through a tree of nodes
(`InputNodeAbstract.TranslateInput`) that is processed from top to bottom —
most recently opened screen first, the player character at the very end.
`InputNode.TranslateInput` contains:

    if (axes != null) TranslateAxes(ref axes);

and `EFT.UI.Screens.UIScreen.TranslateAxes` consists of a single line:
`axes = null;`. The array is passed on via `ref` — from there the chain is
dead, and the player no longer gets any axes at all.

The raid HUD (`BattleUIScreen`), the map (`TextGameMapPanel`) and the action
bar override `TranslateAxes` with an **empty** body, thus letting the axes
through — which is why you run normally there. Only whoever sticks with
`UIScreen`, and that is what `InventoryScreen` does, blocks.

**The solution:** instead of deleting the array, the plugin swaps in its
own in which everything is 0 except forward. The game's own array is not
touched. It only applies while the continuous run is active — otherwise
the inventory stops you as usual.

### Why the key query is hand-built

BepInEx ships a ready-made one with `KeyboardShortcut.IsDown()` — it is
unsuitable here. Its `ModifierKeyTest()` demands that **no** other key
besides the combination is pressed. Anyone who holds Shift or the strafe
key during the double tap would trigger nothing. The plugin therefore
checks only what the combination really requires: modifier keys held, main
key newly pressed. Everything else may happen at the same time. Also, a
`KeyboardShortcut` stores only **one** combination — lists are needed.

### How the + button gets into the F12 menu

The ConfigurationManager (the F12 menu) allows custom controls: you attach
to the `ConfigDescription` an object whose class is named exactly
`ConfigurationManagerAttributes` and set its field `CustomDrawer`. The
manager finds the fields **via reflection by their name** — hence the class
in `src/Tasten.cs` and **no** reference to the ConfigurationManager DLL. If
it were missing, the plugin would not even load otherwise; this way it
stays optional.

Only drawing happens; **nothing is ever changed during drawing**.

## Building from source

Needs the .NET 10 SDK and an SPT 4.1.6 installation to build against.

```bash
./build.sh             # Linux: build only
./build.sh --install   # build and copy into the game folder
```
```powershell
.\build.ps1            # Windows
.\build.ps1 -Install
```

The game folder is set in `Directory.Build.props` (Windows default `C:/SPT`)
or via the environment variable `SPT_DIR`.

## Support

Bugs and questions: [GitHub issues](https://github.com/Kaanori/spt-autolaufen/issues).

## License

MIT — see [LICENSE](LICENSE).
