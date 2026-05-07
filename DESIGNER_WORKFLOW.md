# Designer to Mod Workflow

This document explains how the Designer web app and the ValheimFloorPlan mod work together.

## 1. Run the Designer

Options:

1. VS Code task: `Run Designer Local Server`
2. Double-click `Designer/StartLocalhost.bat`

Then open:

- `http://localhost:5500/index.html`

## 2. Create and Save a Plan

In the Designer:

1. Create your layout.
2. Save/export as a `.vfp` file.
3. Keep plans in `Designer/Plans/` (recommended) or another folder you prefer.

## 3. Point the Mod to the Plan

Set in BepInEx config:

- `FloorPlanFile=<full path to your .vfp file>`

Config file path:

- `BepInEx/config/com.alexdroz.valheimfloorplan.cfg`

## 4. Build in Game

1. Start Valheim with the mod enabled.
2. Press build hotkey (default F8) to enter preview.
3. Move/rotate preview as needed.
4. Confirm placement (default E).

## 5. Iterate Quickly

Typical loop:

1. Edit plan in Designer.
2. Save `.vfp`.
3. Rebuild in game.
4. Use undo hotkey (default F9) if needed.

## Compatibility Notes

- The `.vfp` format is the shared contract between Designer and mod.
- If format fields are changed, update:
  - Designer exporter/parser
  - Mod parser (`FloorPlan.cs`)
- Keep sample plans in `Designer/Plans/` for quick validation.
