# Brand the Building

A Cities: Skylines II mod for placing company signs on building walls and roofs. Choose a building, pick one of its company's compatible branding assets, and position it on the surface.

[Get the mod on Paradox Mods](https://mods.paradoxplaza.com/mods/161581/Windows)

## Installation

Install the mod through Paradox Mods and enable it in your active playset.

The company must have a compatible branding asset available in the game. Keep any asset packs used by placed signs installed.

## How to use

1. Open **Brand the Building** from the Universal Mod Menu.
2. Hover over a company building and click a wall or an upward-facing roof.
3. Use the asset arrows to choose a sign.
4. Click and drag on the selected building to move the preview. For roof signs, hold the right mouse button and drag left or right to rotate.
5. Adjust **Surface offset** to move the sign toward or away from the surface.
6. Click **Place** to confirm, or **Cancel** to discard the preview.

You can assign an activation shortcut under **Options → Mods → Brand the Building**. No key is assigned by default.

### Controls

| Action | Control |
| --- | --- |
| Move the sign | Left-click and drag on the selected building |
| Rotate a roof sign | Hold right-click and drag horizontally |
| Change the sign | Previous / next asset arrows |
| Adjust surface offset | Offset arrows, in 0.05 m steps |
| Adjust offset in larger steps | Hold Shift while clicking an offset arrow, in 0.50 m steps |
| Enter an exact offset | Type a value, then press Enter or leave the field |
| Cancel and exit | Cancel or Esc |

Surface offset accepts values from **−10.00 m to 3.00 m**. Negative values inset the sign; positive values move it outward.

### Placement requirements

- The building must have exactly one company tenant with a brand and a compatible sign asset.
- Billboard, neon, and circular sign assets are supported. Posters and decals are filtered out.
- The sign needs support behind its center and corners. **Place** stays disabled while the surface is being checked or when it cannot support the sign.
- Dragging off the selected building keeps the last valid position. Move back onto the building to continue adjusting it.

## Building from source

Build on Windows with:

- Cities: Skylines II installed.
- The official modding toolchain installed through the game.
- Visual Studio with the components required by the toolchain.
- Node.js 18 or later, with npm available on your PATH.

The toolchain should configure `CSII_TOOLPATH` to point to the folder containing `Mod.props` and `Mod.targets`. Restart your terminal or Visual Studio after installing the toolchain so it picks up the environment variables.

From the repository root, run:

```powershell
dotnet build .\BrandTheBuilding.csproj -c Release
```

You can also open `BrandTheBuilding.csproj` in Visual Studio and build **Release**. The build restores the UI dependencies, builds the UI, and deploys the mod to the local game Mods directory. The standard location is:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\BrandTheBuilding
```

To skip the UI build when troubleshooting the C# project:

```powershell
dotnet build .\BrandTheBuilding.csproj -c Release -p:EnableBrandTheBuildingUIBuild=false
```

## Tests

Tests run separately from the mod build. GitHub Actions runs both test suites and the TypeScript check on every pull request. Run these commands locally from the repository root.

### UI

```powershell
npm --prefix UI ci
npm --prefix UI test
npm --prefix UI run typecheck
```

The UI tests use real React rendering with simulated game bindings. They check offset editing, asset navigation, placement controls, focus, and mouse interactions.

### Placement math

Install the **.NET 9 SDK**, then run:

```powershell
dotnet test .\Tests\BrandTheBuilding.Tests.csproj
```

These tests use the same placement calculations as the mod and reference the game's `Unity.Mathematics.dll`. They check wall and roof orientation, rotation anchors, geometry bounds, and offsets. CI uses the official Unity Mathematics 1.3.2 source package so the GitHub runner does not need the game installed.

If `CSII_MANAGEDPATH` is not configured, pass the path to your game's `Cities2_Data\Managed` folder:

```powershell
dotnet test .\Tests\BrandTheBuilding.Tests.csproj -p:ManagedPath="C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
```

The tests do not launch the game. Changes to surface detection, placement, preview cleanup, or save behavior also need to be checked in-game.

## License

[MIT](LICENSE)
