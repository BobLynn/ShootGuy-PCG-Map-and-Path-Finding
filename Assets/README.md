# Shoot-Guy-Guard-and-Target-AI-Systems-for-Hitman-Style-Gameplay

## Guard Testing

## Target Testing

## 🛠 Features

* **Aim-Lock Mechanics:** Right-click hides the hardware cursor, activates the aim camera zoom, enables the upper-body aiming layer, and displays an on-screen crosshair UI.
* **Basic Shooting:** Left-clicking while aiming fires a `Shoot` trigger parameter to play a recoil animation frame-accurately before returning to the steady aim pose.
* **Inventory & Crosshair Infrastructure:** Integrates an item database foundation alongside required UI rendering assets to handle item data management.

## 📁 Key Files Added or Changed

### Core Scripts
* **`PlayerAimController.cs`:** Manages mouse inputs, switches camera priorities, enables weapon visibility, and drives the animator's state parameters.
* **`ThirdPersonController.cs`:** Modified to reverse camera look direction relative to mouse and add sensitivity variable
* **`ItemDatabase.cs`:** Code architecture providing structural data lookup for weapons and equipment.

### Project & Animation Assets
* **`UpperBodyMask.mask`:** An avatar body mask restricting the aiming layer's motion exclusively to the spine, arms, and head.
* **`MainItemDatabase.asset` & `InventoryAsset`:** ScriptableObject data files storing the global inventory definition table.
* **SMC Pack:** Imported package assets used to implement the UI target crosshair system.
* **TextMeshPro:** Package essentials imported to handle clear UI text rendering overlays.

## ⚙️ Animator State Matrix (`AimLayer`)

* **Layer Weight:** `1.0` (Blended using `UpperBodyMask`).
* **Flow Loop:**
    * `Aiming_Pose` ─── `Trigger: Shoot` (Has Exit Time: `False`) ───► `Shooting_Action`
    * `Shooting_Action` ─── `Conditions: Empty` (Has Exit Time: `True`) ───► `Aiming_Pose`

## 🚀 Controls

* `W`, `A`, `S`, `D`: Character Movement.
* `Right Mouse Button` (Hold): Look Lock, Shoulder Zoom, Toggle UI Crosshair, and Draw Weapon.
* `Left Mouse Button` (Click while Aiming): Fire standard `Shoot` recoil clip.

## Development Notes

See [DEV_NOTES.md](DEV_NOTES.md) for engineering notes on PCG workflow, debugging tools, guard clauses, singleton lifecycle, and Unity git hygiene.
