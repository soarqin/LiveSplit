using System.Collections.Generic;
using System.Drawing;

using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;

using Xunit;

namespace LiveSplit.PinnedSplits.Tests;

/// <summary>
/// Locks down the pinned color override semantics. The contract has TWO axes:
///
///   1. Settings.OverridePinnedColor toggles the entire override on/off.
///   2. SplitComponent.IsPinnedSlot decides WHICH rows the override applies to: only rows
///      that the owning SplitsComponent placed in the top pinned section (PinnedSplitPool).
///      A pinned segment that is ALSO rendered in the normal list (the default behavior when
///      HidePinnedFromNormalList is off) — same ISegment, different SplitComponent instance —
///      keeps its regular Before/Current/After colors so the override stays scoped.
///
/// We exercise this via SplitComponent directly because:
///   - SplitsComponent.Update only drives UpdateAll on its children when an invalidator is
///     provided; the test infrastructure does not have one. SplitComponent.Update calls
///     UpdateAll BEFORE the invalidator branch, so calling it with a null invalidator still
///     resolves NameLabel.ForeColor — exactly what we want.
///   - Driving the production gate at the row level keeps each assertion focused on a single
///     branch of UsePinnedStyle without depending on layout/render plumbing.
///
/// All test colors are intentionally distinct so a swapped slot fails fast: regular palette
/// uses primaries (Red/Green/Blue) and the pinned palette uses secondaries (Cyan/Magenta/Yellow).
/// LayoutSettings.TextColor is set to White so we can also tell the layout-default branch apart.
/// </summary>
public class PinnedSplitsStyleMust
{
    // Distinct colors so a wrong-slot Assert fails on the visible color rather than a subtle
    // shade difference. RGB primaries for normal palette, secondaries for pinned palette.
    private static readonly Color LayoutTextColor = Color.FromArgb(255, 255, 255);
    private static readonly Color BeforeRegular = Color.FromArgb(255, 0, 0);     // Red
    private static readonly Color CurrentRegular = Color.FromArgb(0, 255, 0);    // Green
    private static readonly Color AfterRegular = Color.FromArgb(0, 0, 255);      // Blue
    private static readonly Color PinnedColor = Color.FromArgb(255, 215, 0);     // Gold

    private static LiveSplitState BuildState(IReadOnlyList<string> segmentNames, TimerPhase phase, int currentSplitIndex)
    {
        var run = new Run(new StandardComparisonGeneratorsFactory());
        foreach (string name in segmentNames)
        {
            run.Add(new Segment(name));
        }

        var layoutSettings = new LayoutSettings
        {
            TimerFont = new Font("Arial", 12),
            TimesFont = new Font("Arial", 10),
            TextFont = new Font("Arial", 10),
            TextColor = LayoutTextColor,
        };

        var layout = new Layout { Settings = layoutSettings };

        var settings = new Settings
        {
            HotkeyProfiles = new Dictionary<string, HotkeyProfile>
            {
                ["Default"] = new HotkeyProfile(),
            },
        };

        var state = new LiveSplitState(run, null, layout, layoutSettings, settings)
        {
            CurrentPhase = phase,
            CurrentSplitIndex = currentSplitIndex,
        };

        return state;
    }

    /// <summary>
    /// Builds a SplitsSettings with a regular Names palette + a single Pinned Names color
    /// that is distinct from every regular slot. Other style fields stay at their defaults.
    /// Times/Delta follow the exact same gating code path; the assertions for those would be
    /// a pure copy of the Names assertions.
    /// </summary>
    private static SplitsSettings BuildSettings(LiveSplitState state, bool overridePinnedColor, bool overrideTextColor = true)
    {
        return new SplitsSettings(state)
        {
            OverrideTextColor = overrideTextColor,
            BeforeNamesColor = BeforeRegular,
            CurrentNamesColor = CurrentRegular,
            AfterNamesColor = AfterRegular,
            OverridePinnedColor = overridePinnedColor,
            PinnedNamesColor = PinnedColor,
        };
    }

    /// <summary>
    /// Drives a SplitComponent through one frame for the given segment so UpdateAll runs and
    /// resolves NameLabel.ForeColor. SplitComponent.Update calls UpdateAll first, then
    /// short-circuits the invalidator path when invalidator is null — exactly what we want.
    /// The <paramref name="isPinnedSlot"/> parameter mirrors what the production code does at
    /// pool-creation time: SplitsComponent.EnsurePinnedPoolSize sets IsPinnedSlot=true on
    /// every PinnedSplitPool entry, while regular SplitComponents constructed in
    /// RebuildVisualSplits leave it at the default false.
    /// </summary>
    private static SplitComponent BuildAndUpdateRow(SplitsSettings settings, LiveSplitState state, ISegment segment, bool isPinnedSlot)
    {
        var row = new SplitComponent(settings, [], [])
        {
            Split = segment,
            IsPinnedSlot = isPinnedSlot,
        };
        row.Update(null, state, 0, 0, LayoutMode.Vertical);
        return row;
    }

    // -------------------------------------------------------------------------
    // Override OFF: pinned override does nothing, both slots use the regular palette.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Override OFF + pinned-section slot: pinned segment falls through the regular
    /// OverrideTextColor logic. Locks down that toggling OverridePinnedColor from false to
    /// true is a true opt-in — no surprise visual change for users on default.
    /// </summary>
    [Fact]
    public void WhenOverrideOff_PinnedSlot_UsesRegularBeforeColor()
    {
        // ^Boss timed (index 0 < currentIdx 1), shown in pinned-section slot.
        var state = BuildState(["^Boss", "Normal"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: false);

        SplitComponent row = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: true);

        Assert.Equal(BeforeRegular, row.NameForeColorForTest);
    }

    /// <summary>
    /// Override OFF + normal-list slot: same pinned segment rendered in the normal list also
    /// uses the regular palette. Sanity check: with the master switch off, IsPinnedSlot is
    /// irrelevant.
    /// </summary>
    [Fact]
    public void WhenOverrideOff_NormalListSlot_UsesRegularBeforeColor()
    {
        var state = BuildState(["^Boss", "Normal"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: false);

        SplitComponent row = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: false);

        Assert.Equal(BeforeRegular, row.NameForeColorForTest);
    }

    // -------------------------------------------------------------------------
    // Override ON: pinned color applies ONLY to pinned-section slots.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Override ON + pinned-section slot: the pinned segment uses PinnedNamesColor regardless
    /// of its before/current/after position. This is the "目的颜色生效" branch.
    /// </summary>
    [Fact]
    public void WhenOverrideOn_PinnedSlot_UsesPinnedColor()
    {
        // ^Boss timed (before current), pinned-section slot.
        var state = BuildState(["^Boss", "Normal"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: true);

        SplitComponent row = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: true);

        Assert.Equal(PinnedColor, row.NameForeColorForTest);
    }

    /// <summary>
    /// CRITICAL slot-scoping invariant: Override ON + the SAME pinned segment rendered in a
    /// normal-list slot (default behavior when HidePinnedFromNormalList=false) does NOT pick
    /// up PinnedColor. The pinned override is scoped strictly to the top pinned section. This
    /// is the regression that the slot-based gate exists to prevent.
    /// </summary>
    [Fact]
    public void WhenOverrideOn_PinnedSegmentInNormalListSlot_UsesRegularBeforeColor()
    {
        // ^Boss timed (before current), but rendered in a NORMAL list slot.
        var state = BuildState(["^Boss", "Normal"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: true);

        SplitComponent row = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: false);

        Assert.Equal(BeforeRegular, row.NameForeColorForTest);
    }

    /// <summary>
    /// Override ON + a not-yet-timed pinned segment in the normal list (it hasn't been
    /// promoted to the pinned section yet, so it lives in a normal-list slot only). The
    /// pinned override must NOT apply — the segment renders with the regular After color.
    /// Without slot-scoping, this row would falsely render with PinnedColor.
    /// </summary>
    [Fact]
    public void WhenOverrideOn_PinnedFutureSegmentInNormalListSlot_UsesRegularAfterColor()
    {
        // Normal (index 0, current), ^FuturePin (index 1, after current — not yet timed).
        var state = BuildState(["Normal", "^FuturePin"], TimerPhase.Running, 0);
        var settings = BuildSettings(state, overridePinnedColor: true);

        SplitComponent row = BuildAndUpdateRow(settings, state, state.Run[1], isPinnedSlot: false);

        Assert.Equal(AfterRegular, row.NameForeColorForTest);
    }

    /// <summary>
    /// Override ON + a pinned segment that's the active current split, rendered in a
    /// normal-list slot (IncludeCurrentSplit is off so it never graduates to the pinned
    /// section). Must use the regular Current color, NOT PinnedColor.
    /// </summary>
    [Fact]
    public void WhenOverrideOn_PinnedCurrentSegmentInNormalListSlot_UsesRegularCurrentColor()
    {
        // Normal (index 0, timed), ^Current (index 1, current).
        var state = BuildState(["Normal", "^Current"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: true);

        SplitComponent row = BuildAndUpdateRow(settings, state, state.Run[1], isPinnedSlot: false);

        Assert.Equal(CurrentRegular, row.NameForeColorForTest);
    }

    /// <summary>
    /// Override ON + a non-pinned (regular) segment in a normal-list slot: must NOT use the
    /// pinned palette. The pinned override only affects pinned segments — toggling it must be
    /// visually neutral for every segment that lacks the `^` marker. By construction the
    /// SplitsComponent never routes a regular segment into a pinned slot, so this test only
    /// covers the normal-list path; the pinned-slot path is unreachable for non-pinned
    /// segments under production use.
    /// </summary>
    [Fact]
    public void WhenOverrideOn_NormalSegmentInNormalListSlot_UsesRegularBeforeColor()
    {
        // Normal (index 0, timed), Other (index 1, current).
        var state = BuildState(["Normal", "Other"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: true);

        SplitComponent row = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: false);

        Assert.Equal(BeforeRegular, row.NameForeColorForTest);
    }

    /// <summary>
    /// Override ON but OverrideTextColor OFF + pinned-section slot: PinnedNamesColor still
    /// wins because OverridePinnedColor is the master switch for the pinned palette and is
    /// independent of OverrideTextColor. A normal segment in a normal-list slot with the
    /// same flag config falls back to the layout TextColor.
    /// </summary>
    [Fact]
    public void WhenOverrideOn_AndOverrideTextColorOff_PinnedSlotStillUsesPinnedColor()
    {
        var state = BuildState(["^Pinned", "Normal"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: true, overrideTextColor: false);

        SplitComponent pinnedSlotRow = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: true);
        SplitComponent normalRow = BuildAndUpdateRow(settings, state, state.Run[1], isPinnedSlot: false);

        Assert.Equal(PinnedColor, pinnedSlotRow.NameForeColorForTest);
        // Normal segment + OverrideTextColor=false → layout TextColor.
        Assert.Equal(LayoutTextColor, normalRow.NameForeColorForTest);
    }

    /// <summary>
    /// Override OFF and OverrideTextColor OFF: regular and pinned segments alike fall through
    /// to the layout TextColor, in either kind of slot. Sanity check that with both flags off
    /// no per-segment color branch is reachable.
    /// </summary>
    [Fact]
    public void WhenBothOverridesOff_AllSlotsUseLayoutTextColor()
    {
        var state = BuildState(["^Pinned", "Normal"], TimerPhase.Running, 1);
        var settings = BuildSettings(state, overridePinnedColor: false, overrideTextColor: false);

        SplitComponent pinnedSlot = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: true);
        SplitComponent pinnedInNormalSlot = BuildAndUpdateRow(settings, state, state.Run[0], isPinnedSlot: false);
        SplitComponent normalRow = BuildAndUpdateRow(settings, state, state.Run[1], isPinnedSlot: false);

        Assert.Equal(LayoutTextColor, pinnedSlot.NameForeColorForTest);
        Assert.Equal(LayoutTextColor, pinnedInNormalSlot.NameForeColorForTest);
        Assert.Equal(LayoutTextColor, normalRow.NameForeColorForTest);
    }

    // -------------------------------------------------------------------------
    // Integration: SplitsComponent must wire IsPinnedSlot=true on every
    // PinnedSplitPool entry (the slot-based gate's single source of truth).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Locks down the wiring contract between SplitsComponent and SplitComponent. Every
    /// SplitComponent created in PinnedSplitPool MUST have IsPinnedSlot=true. If this ever
    /// regresses, the production code would render pinned segments with regular colors even
    /// when the user has the override on — breaking the entire "pinned color" feature.
    /// </summary>
    [Fact]
    public void IntegratesWithSplitsComponent_PinnedPoolEntriesAreFlaggedAsPinnedSlots()
    {
        var state = BuildState(["^Boss1", "^Boss2", "Normal"], TimerPhase.Running, 2);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        // Both pinned segments are timed (currentIdx=2 means indices 0,1 are timed).
        Assert.Equal(2, component.PinnedSegmentsForTest.Count);
        Assert.True(component.PinnedSplitPoolForTest.Count >= 2);
        for (int i = 0; i < 2; i++)
        {
            SplitComponent row = component.PinnedSplitPoolForTest[i];
            Assert.True(row.IsPinnedSlot, $"PinnedSplitPool[{i}] must be flagged as a pinned slot");
            Assert.Same(component.PinnedSegmentsForTest[i], row.Split);
        }
    }

    /// <summary>
    /// The dual contract: regular SplitComponents (the "normal list pool" built by
    /// RebuildVisualSplits) MUST keep IsPinnedSlot=false so pinned segments rendered there
    /// fall through to the regular palette.
    /// </summary>
    [Fact]
    public void IntegratesWithSplitsComponent_NormalListEntriesAreNotFlaggedAsPinnedSlots()
    {
        var state = BuildState(["^Boss1", "Normal", "^Boss2"], TimerPhase.Running, 3);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.NotEmpty(component.NormalSplitComponentsForTest);
        foreach (SplitComponent row in component.NormalSplitComponentsForTest)
        {
            Assert.False(row.IsPinnedSlot, "Normal-list SplitComponent must not be flagged as a pinned slot");
        }
    }
}
