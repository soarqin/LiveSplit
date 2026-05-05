# LiveSplit.PinnedSplits

A drop-in replacement for the standard **Splits** component that additionally keeps "pinned" segments visible at the top of the display, regardless of scrolling. Pinned segments are identified by a `^` prefix in the segment name. The `^` is stripped from the displayed name in **both** the pinned section and the normal splits section, while the underlying run keeps the raw name (so saves round-trip cleanly).

## What it does

When added to your layout, PinnedSplits renders:

1. **Top section** — a persistent list of already-completed pinned segments. The `^` prefix is stripped from the displayed name. The size of this section grows and shrinks as the run progresses.
2. **Separator** — visually divides the pinned section from the normal one (only present when at least one pinned segment is shown).
3. **Bottom section** — the standard Splits view of the rest of the run. By default, pinned segments stay visible in this section at their original position even after they have been promoted to the top section, so the user keeps full context. Toggle **Hide Pinned Splits from Normal List** to filter them out instead. Same scroll/column/etc. behavior as the built-in Splits component.

Because PinnedSplits is a superset of Splits, **remove the standard Splits component from your layout** when you add PinnedSplits — otherwise the un-pinned splits will be rendered twice.

## Naming convention

Prefix a segment name with `^` to mark it as pinned:

| Segment name | Pinned? | Displayed as |
|---|---|---|
| `Boss` | No | `Boss` |
| `^Boss` | **Yes** | `Boss` |
| `^^Boss` | No | `^Boss` (escape: `^^` → literal `^`) |
| `^^^Boss` | **Yes** | `^^Boss` (only the outermost `^` is stripped) |
| `^` (single char) | No | *(empty)* |
| `^^` | No | `^` |

To display a literal `^` at the start of a segment name without pinning it, use `^^` as the prefix.

## Settings

PinnedSplits exposes the **same settings as the standard Splits component** (visual split count, columns, colors, fonts, separators, accuracy, etc.) plus the following pinned-section knobs.

### Pinned Splits

| Setting | Default | Description |
|---|---|---|
| **Max Displayed** | `5` | Maximum number of pinned segments to show in the top section (`MaxDisplayed`). `0` = show all. When the limit is reached, the most recently completed pinned segments are shown. The settings UI lets you pick `0`–`20` directly; if you need a finite cap higher than `20`, set the value to `0` (unlimited) or edit the layout XML's `<MaxDisplayed>` element by hand. The cap only affects the top section — pinned segments still show up in the normal list at their original position unless **Hide Pinned Splits from Normal List** is on. |
| **Include Current Split** | `false` | When enabled, the currently active split is also shown in the pinned section if it is pinned (`IncludeCurrentSplit`). |
| **Hide Pinned Splits from Normal List** | `false` | When disabled (default), pinned segments stay visible in the normal list at their original position even after they have been promoted to the top section. When enabled, timed pinned segments are filtered out of the normal list and only render at the top (`HidePinnedFromNormalList`). Untimed pinned segments always remain in the normal list regardless of this flag — they have not been promoted yet. |

### Pinned Splits Style

A single optional override that recolors **only the rows rendered in the top pinned section**. The normal-list copy of the same pinned segment (when **Hide Pinned Splits from Normal List** is off) keeps the regular Splits palette so the override stays scoped to the top section.

| Setting | Default | Description |
|---|---|---|
| **Override Color for Pinned Segments** | `false` | Master switch for the pinned color override (`OverridePinnedColor`). When off, pinned-section rows render with the regular Splits palette. |
| **Pinned Split Names Color** | `White` | Name-label color used in the top pinned section (`PinnedNamesColor`). Active only when **Override Color for Pinned Segments** is on. |
| **Pinned Split Times Color** | `White` | Time/segment/custom-variable column color used in the top pinned section (`PinnedTimesColor`). Pace colors (gold/red/green) returned by LiveSplit's split-color resolver still win — the override only applies on the fallback branches. |
| **Pinned Live Delta Color** | `White` | Live-delta color used in the top pinned section while a pinned segment is the active split (`PinnedDeltasColor`). |

The `CurrentSplit` background gradient is intentionally **not** pinned-overridden — "pinned" and "current split" are orthogonal concepts and the current-split highlight stays consistent across both sections.

The new fields are persisted via the XML elements named after each property (`MaxDisplayed`, `IncludeCurrentSplit`, `HidePinnedFromNormalList`, `OverridePinnedColor`, `PinnedNamesColor`, `PinnedTimesColor`, `PinnedDeltasColor`) alongside the standard Splits settings. Layout XML version is `1.8` for pin-aware fields.

## ⚠️ Limitations

- **Cannot coexist with Subsplits per segment**: A segment cannot be both pinned (`^` prefix) and a subsplit (`-` prefix) because both prefixes occupy the first character. If you use both the Subsplits component and PinnedSplits, segments must use one prefix or the other, not both.

- **Retroactive semantic change**: Existing runs with segment names starting with `^` (e.g., `^Cool Move`) will be silently interpreted as pinned once this component is loaded into your layout. To keep the original literal display without pinning, rename the segment to `^^Cool Move`.

- **The Splits Editor still shows the raw name**: Editing a pinned segment in LiveSplit's Splits Editor will display the raw `^Cool Move` form. This is intentional so users can change the pinned status by editing the prefix; it also means saved splits files keep the `^` marker untouched.

- **Layout duplication**: If you keep the original Splits component in your layout *alongside* PinnedSplits, the standalone Splits component will still display every segment (including pinned ones) using its own rendering, which renders the `^` prefix verbatim and does not strip pinned segments. Remove the standalone Splits component to get the cleaned/filtered view.

## Architecture

PinnedSplits is a **fork of LiveSplit.Splits** that lives entirely inside this component. The fork:

- Copies all of `LiveSplit.Splits` (`SplitsComponent`, `SplitComponent`, `SplitsSettings`, `ColumnSettings`, `LabelsComponent`, etc.) into this assembly.
- Modifies `SplitComponent` to use `PinnedSegmentParser` when assigning the displayed name (so the `^` is stripped). Adds an `IsPinnedSlot` flag that gates the pinned color override (set to `true` only on rows owned by `PinnedSplitPool`).
- Modifies `SplitsComponent` to:
  - Maintain a dynamic pool of `SplitComponent` rows for the top pinned section, each flagged `IsPinnedSlot=true`.
  - Decide per segment whether it appears in the normal list. Default behavior keeps **every** segment in the normal list (including those promoted to the top section), so a pinned segment shows in two places at once with full context. Toggling `HidePinnedFromNormalList` filters timed pinned segments out of the normal list. Untimed pinned segments always stay in the normal list — they haven't been promoted yet.
  - Map `state.CurrentSplitIndex` into the normal-list projection so scroll / `visualSplitCount` / `AlwaysShowLastSplit` continue to behave correctly regardless of whether pinned segments are filtered out.
  - Recombine `[pinned rows..., separator?, normal components...]` into `InternalComponent.VisibleComponents` on every frame.
- Adds pinned-specific properties to `SplitsSettings` (`MaxDisplayed`, `IncludeCurrentSplit`, `HidePinnedFromNormalList`, `OverridePinnedColor`, `PinnedNamesColor`, `PinnedTimesColor`, `PinnedDeltasColor`) with XML round-trip support.

`state.Run` itself is **never mutated** — segments keep their raw names so save/load round-trips preserve the `^` markers.

### Slot-scoped color override

The pinned color override is scoped strictly to `IsPinnedSlot` rows. The same pinned segment, when also rendered in a normal-list row (the default with `HidePinnedFromNormalList=false`), keeps the regular Before/Current/After palette. This is what makes the dual-display work visually: the user immediately sees the pinned-color row at the top AND the regular-color row in context further down.

## Build & Install

From the LiveSplit repository root:

```powershell
dotnet build components/LiveSplit.PinnedSplits/LiveSplit.PinnedSplits.sln -c Release
```

The DLL is automatically placed at `bin/release/Components/LiveSplit.PinnedSplits.dll` thanks to the shared `components/Directory.Build.props` (`OutputPath=$(BuildPath)\Components`). LiveSplit scans this folder on startup, so no manual copy is needed.

To use outside the LiveSplit source tree, copy the DLL into LiveSplit's installed `Components/` folder.

## Add to layout

1. Open Layout Editor
2. **Remove the standard Splits component** from your layout (if present)
3. Click **Add Component**
4. Select **List → Pinned Splits**
5. Configure settings as needed: Splits-style settings + the **Pinned Splits** group (Max Displayed / Include Current Split / Hide Pinned Splits from Normal List) + the **Pinned Splits Style** group (Override Color for Pinned Segments + Pinned Names / Times / Live Delta colors)
