# VAL Aim Trainer (Unity)

An Aim Lab-style trainer with a Valorant routine: Gridshot, Spidershot, Microshot, Frenzy, Frenzy Flick, Sixshot, Reflexshot, Headshot Line, Motionshot, Strafeshot, Strafetrack.

## Open it
1. Install **Unity Hub** and a **Unity 6 (6000.0 LTS)** editor. Add the "Windows Build Support (Mono)" module if you want an .exe.
2. Unity Hub → **Add → Add project from disk** → pick this `ValAimTrainer` folder. If Hub asks about the editor version, pick your installed Unity 6.
3. When it opens, the `Assets/AimTrainer/Main.unity` scene is created and opened for you. Press **Play**.
   - Use the Game view maximized (Shift+Space) or build the game for the real feel.

## Build a Windows .exe
File → Build Profiles (or Build Settings) → Windows → **Build**. Main.unity is already in the scene list.

## Sensitivity
- Type your Valorant sens (3 decimals, e.g. `0.314`) and DPI in Settings. The game turns 0.07° per mouse count × sens, like Valorant, with a 103° horizontal FOV.
- Keep Edit → Project Settings → Input Manager → Mouse X / Mouse Y **Sensitivity at 0.1** (the default).
- Keep Edit → Project Settings → Player → **Active Input Handling** on "Input Manager (Old)" or "Both".
- Turn off Windows "Enhance pointer precision".

## Code
- `Assets/AimTrainer/Scripts/Scenarios.cs`: every drill. Change sizes, spawn areas, durations or the routine order here.
- `Assets/AimTrainer/Scripts/Game.cs`: room, targets, shooting, scoring, menus and settings.
- `Assets/AimTrainer/Editor/AimTrainerSetup.cs`: creates the scene on first open.
