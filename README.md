# Brand the Building

`BrandTheBuilding` is a clean Cities: Skylines II code-mod project for manually placing a company's compatible branding asset on the real surface of its building.

## Implemented MVP flow (for alpha release)

1. The **Brand the Building** button is added to the game's Universal Mod Menu.
2. Activating it enters targeting mode.
3. A building with exactly one company renter highlights and shows a separate hover prompt: `Add Company Branding Here`.
4. A building without a company shows the corresponding invalid message.
5. Clicking a valid façade resolves `CompanyData.m_Brand`, gathers matching `BrandObjectData` prefabs, and enters editing.
6. The default candidate prioritizes a prefab whose name contains `Billboard Large`, then other billboard, neon, and circular sign variants. Posters and decals are excluded.
7. The temporary preview uses the raycast's real mesh hit position and triangle normal.
8. Click-dragging only accepts raycast hits that resolve back to the originally selected building. Losing the surface keeps the last valid position.
9. Walls and upward-facing roofs are supported; undersides are rejected. Roof signs stand on their base, aligned with the building heading and roof slope. Orientation is automatic.
10. Previous/next cycles compatible assets while retaining the current surface anchor. The picker uses drawn arrows rather than font glyphs.
11. Editing highlights the sign preview, while the targeting highlight is removed from the building.
12. Surface offset ranges from -10.00 m to 3.00 m. Arrows step 0.05 m, or 0.50 m with Shift held. Typed values apply only on Enter/focus loss. Thick billboards start at -0.10 m; thin signs use a less negative initial value if needed to keep their face visible. Asset switching retains the chosen offset.
13. The Universal Mod Menu entry is a square blue button with a white billboard icon and a hover label rendered outside the menu's clipping container. Options → Mods exposes an enabled, optional tool-activation hotkey with no key assigned by default.
14. Cancel or Escape marks the preview for the game's normal deletion pass and exits without committing.
15. Place captures the live preview transform (not the last cached calculation), removes the preview's custom marker/highlight and instance alignment instructions, supplies elevation derived from the snapshot, and requests `ApplyMode.Apply`. It waits for the game to remove `Temp`, restores that same snapshot/elevation once and exits. A four-update timeout restores editing. There is no second creation request or replacement prop.
16. The smaller editing heading uses the building address from `BuildingUtils.GetAddress`, including the road's localized/custom name. A building without an address falls back to its label.

## Save and uninstall design

- The preview retains the branding prefab's rendering components, then removes the live-object `Created` marker and any `Owner` before the game updates it. It has `Temp` and the mod's `BrandPreview` marker. On exit or asset switch it receives `TempFlags.Cancel` and `Deleted`, allowing rendering and cleanup systems to observe removal.
- Place uses the native tool-apply lifecycle on the preview. `BrandPreview` is removed before applying, and the game removes `Temp`. Completed props are excluded from preview cleanup.
- No serialized `AppliedCompanyLogo`-style marker exists in this project.
- No building prefab, authored subobject list, company data, brand data, or asset configuration is modified.
- There is no background synchronization or persistent placement database.
- A native Owner and reciprocal live-instance SubObject reference are added only when Place is requested. No Attached alignment component is added, no shared prefab is modified, and no custom persistent component or background visibility repair is used. Exact placement, override immunity, save/reload, mod removal, building lifecycle and normal prop selection/deletion must be verified against the installed Windows game build.
- Removing the asset pack that supplied the selected branding prefab remains an expected external dependency risk.

## Windows prerequisites

1. Install Cities: Skylines II.
2. In the game, install/update the official modding toolchain from **Options → Mods**.
3. Install Visual Studio with the workload required by the CS2 toolchain.
4. Confirm the user environment variable `CSII_TOOLPATH` points to the toolchain folder containing `Mod.props` and `Mod.targets`.
5. Confirm Node.js is available. The toolchain currently expects Node 18 or later.

## Build

Open `BrandTheBuilding.csproj` in Visual Studio and build `Release`, or run from a Developer PowerShell:

```powershell
dotnet build .\BrandTheBuilding.csproj -c Release
```

The project builds its UI automatically with `npm install` and `npm run build`. The UI bundle is written to:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\BrandTheBuilding
```

If your toolchain does not define `CSII_USERDATAPATH`, the UI build uses that standard Windows path automatically.

To compile only the C# project while diagnosing UI tooling, pass:

```powershell
dotnet build .\BrandTheBuilding.csproj -c Release -p:EnableBrandTheBuildingUIBuild=false
```

## Deliberately excluded

There is no automatic placement, company picker, scaling, manual rotation, numeric XYZ transforms, snapping UI, keyboard nudging, editing of committed props, custom deletion manager, undo/redo, dynamic company-change handling, persistent placement metadata, existing-brand detection, or curved-mesh deformation. The surface-normal offset is the one deliberately added placement control.

## Important implementation note

The preview anchor is a selected-building raycast hit. Five short mesh raycasts check the center and corners before accepting a new anchor or asset; unsupported positions retain the last valid preview. Only one batch is pending at a time, with a four-tool-update timeout. The closest geometry bound starts outside the wall, with clearance for protrusions detected by those probes. Negative user offsets can intentionally inset it. Bounds cannot identify artwork separately from backing/supports, so thick signs can stand farther out than thin signs. No mesh bending or asset-specific guessed face depth is used.