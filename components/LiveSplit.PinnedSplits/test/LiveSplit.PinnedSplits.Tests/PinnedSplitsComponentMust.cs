using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;

using Xunit;

namespace LiveSplit.PinnedSplits.Tests;

public class PinnedSplitsComponentMust
{
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

    [Fact]
    public void RebuildRows_PromotesOnlyTimed_KeepsAllInNormalListByDefault()
    {
        // ^Boss1 (index 0), Normal (index 1), ^Boss2 (index 2), ^Boss3 (index 3)
        // CurrentSplitIndex = 2 -> only index 0 is timed (< 2); Boss2 is current, Boss3 is future
        var state = BuildState(["^Boss1", "Normal", "^Boss2", "^Boss3"], TimerPhase.Running, 2);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        // Only the timed pinned segment is promoted to the top section.
        Assert.Single(component.PinnedSegmentsForTest);
        Assert.Equal("^Boss1", component.PinnedSegmentsForTest[0].Name);
        // Default (HidePinnedFromNormalList=false): EVERY segment also appears in the normal
        // list, including ^Boss1 which is already shown at the top. The untimed ^Boss2 (current
        // split with default IncludeCurrentSplit=false) and ^Boss3 (future) stay visible in the
        // normal list because they haven't been promoted yet.
        Assert.Equal(4, component.NonPinnedSegmentsForTest.Count);
        Assert.Equal("^Boss1", component.NonPinnedSegmentsForTest[0].Name);
        Assert.Equal("Normal", component.NonPinnedSegmentsForTest[1].Name);
        Assert.Equal("^Boss2", component.NonPinnedSegmentsForTest[2].Name);
        Assert.Equal("^Boss3", component.NonPinnedSegmentsForTest[3].Name);
    }

    [Fact]
    public void RebuildRows_WithHidePinnedFromNormalList_FiltersTimedPinnedFromNormalList()
    {
        // Same fixture as above but with HidePinnedFromNormalList toggled on. Only the timed
        // pinned segment ^Boss1 is filtered out of the normal list (it's already shown at the
        // top). The untimed ^Boss2 / ^Boss3 stay in the normal list either way — they
        // haven't been promoted yet.
        var state = BuildState(["^Boss1", "Normal", "^Boss2", "^Boss3"], TimerPhase.Running, 2);
        var component = new SplitsComponent(state);
        var settings = (SplitsSettings)component.GetSettingsControl(LayoutMode.Vertical);
        settings.HidePinnedFromNormalList = true;

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.Single(component.PinnedSegmentsForTest);
        Assert.Equal("^Boss1", component.PinnedSegmentsForTest[0].Name);
        Assert.Equal(3, component.NonPinnedSegmentsForTest.Count);
        Assert.Equal("Normal", component.NonPinnedSegmentsForTest[0].Name);
        Assert.Equal("^Boss2", component.NonPinnedSegmentsForTest[1].Name);
        Assert.Equal("^Boss3", component.NonPinnedSegmentsForTest[2].Name);
    }

    /// <summary>
    /// Regression: a pinned segment whose index is past the current split is "untimed". It
    /// must stay in the normal list (not vanish), so the user can still see the upcoming
    /// pinned split. Once the run reaches it, the next ComputePinnedAndFiltered will move it
    /// to the top section.
    /// </summary>
    [Fact]
    public void RebuildRows_FuturePinnedSegment_AppearsInNormalList()
    {
        // Normal (index 0, current), ^FuturePin (index 1, after current).
        var state = BuildState(["Normal", "^FuturePin"], TimerPhase.Running, 0);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.Empty(component.PinnedSegmentsForTest);
        Assert.Equal(2, component.NonPinnedSegmentsForTest.Count);
        Assert.Equal("Normal", component.NonPinnedSegmentsForTest[0].Name);
        Assert.Equal("^FuturePin", component.NonPinnedSegmentsForTest[1].Name);
    }

    /// <summary>
    /// Regression: when IncludeCurrentSplit is false (default), a pinned segment that happens
    /// to be the active split is "untimed" by definition. It must still render — in the
    /// normal list — until the player splits past it. Without this, the active split would
    /// silently disappear from the UI the moment the timer reached a pinned segment.
    /// </summary>
    [Fact]
    public void RebuildRows_PinnedCurrentSegmentWithoutInclude_AppearsInNormalList()
    {
        // Normal1 (index 0, timed), ^Boss (index 1, current), Normal2 (index 2, future).
        // IncludeCurrentSplit defaults to false, so ^Boss does NOT graduate to the pinned section.
        var state = BuildState(["Normal1", "^Boss", "Normal2"], TimerPhase.Running, 1);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.Empty(component.PinnedSegmentsForTest);
        Assert.Equal(3, component.NonPinnedSegmentsForTest.Count);
        Assert.Equal("Normal1", component.NonPinnedSegmentsForTest[0].Name);
        Assert.Equal("^Boss", component.NonPinnedSegmentsForTest[1].Name);
        Assert.Equal("Normal2", component.NonPinnedSegmentsForTest[2].Name);
    }

    /// <summary>
    /// _mappedCurrentSplitIndex is the projection of state.CurrentSplitIndex into
    /// _nonPinnedSegments. When untimed pinned segments are surfaced in the normal list,
    /// they participate in this projection — i.e. a pinned segment past the current split
    /// must NOT shift the current-split offset, while a pinned segment behind the current
    /// split (when not promoted, e.g. NotRunning) must shift it. We verify the offset
    /// indirectly via SplitComponents: with VisualSplitCount large enough that no scroll
    /// is needed, the active split should be at its expected position in the normal list.
    /// </summary>
    [Fact]
    public void RebuildRows_FuturePinnedSegment_DoesNotShiftCurrentSplitOffset()
    {
        // Normal1 (index 0, timed), Normal2 (index 1, current), ^FuturePin (index 2).
        // The active split (Normal2) must remain at offset 1 in the normal list, NOT at 2.
        var state = BuildState(["Normal1", "Normal2", "^FuturePin"], TimerPhase.Running, 1);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.Empty(component.PinnedSegmentsForTest);
        Assert.Equal(3, component.NonPinnedSegmentsForTest.Count);
        // Sanity: the segment at the projected current-split index in _nonPinnedSegments
        // is the actual current split. If _mappedCurrentSplitIndex were miscounted as 2
        // (i.e. counting the future pinned segment behind the current index, which it is
        // not), this would point at ^FuturePin instead.
        Assert.Same(state.CurrentSplit, component.NonPinnedSegmentsForTest[1]);
    }

    [Fact]
    public void RebuildRows_HidesAllWhenNotRunning()
    {
        var state = BuildState(["^Boss1", "^Boss2"], TimerPhase.NotRunning, -1);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        // Top pinned section is empty when the run hasn't started — nothing has been timed yet.
        Assert.Empty(component.PinnedSegmentsForTest);
        // But the pinned segments themselves must still be visible in the normal list, so the
        // user can see the upcoming pinned splits before the run begins. Regression: previously
        // they were filtered out of both lists and disappeared entirely.
        Assert.Equal(2, component.NonPinnedSegmentsForTest.Count);
        Assert.Equal("^Boss1", component.NonPinnedSegmentsForTest[0].Name);
        Assert.Equal("^Boss2", component.NonPinnedSegmentsForTest[1].Name);
    }

    [Fact]
    public void RebuildRows_TruncatesToMaxDisplayed()
    {
        // 6 pinned segments, all timed (CurrentSplitIndex = 6 means all 0-5 are timed).
        var names = Enumerable.Range(1, 6).Select(i => $"^Boss{i}").ToList();
        var state = BuildState(names, TimerPhase.Running, 6);
        var component = new SplitsComponent(state);
        // Reach into Settings via the public GetSettingsControl path, which returns the SplitsSettings
        // instance. Casting lets us tweak MaxDisplayed without exposing a new shortcut on the component.
        var settings = (SplitsSettings)component.GetSettingsControl(LayoutMode.Vertical);
        settings.MaxDisplayed = 3;

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        // Cap keeps the most recent N pinned (drops oldest).
        Assert.Equal(3, component.PinnedSegmentsForTest.Count);
        Assert.Equal("^Boss4", component.PinnedSegmentsForTest[0].Name);
        Assert.Equal("^Boss5", component.PinnedSegmentsForTest[1].Name);
        Assert.Equal("^Boss6", component.PinnedSegmentsForTest[2].Name);
        // MaxDisplayed only caps the top section. The normal list (default
        // HidePinnedFromNormalList=false) keeps ALL 6 pinned segments at their original
        // positions — the cap on the top section doesn't affect normal-list membership.
        Assert.Equal(6, component.NonPinnedSegmentsForTest.Count);
    }

    [Fact]
    public void RebuildRows_RespectsIncludeCurrentSplit()
    {
        // ^Boss1 (index 0) is timed, ^Boss2 (index 1) is current
        var state = BuildState(["^Boss1", "^Boss2", "Normal"], TimerPhase.Running, 1);
        var component = new SplitsComponent(state);
        var settings = (SplitsSettings)component.GetSettingsControl(LayoutMode.Vertical);
        settings.IncludeCurrentSplit = true;

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        // Both Boss1 (timed) and Boss2 (current, included) should show in pinned section.
        Assert.Equal(2, component.PinnedSegmentsForTest.Count);
        Assert.Equal("^Boss1", component.PinnedSegmentsForTest[0].Name);
        Assert.Equal("^Boss2", component.PinnedSegmentsForTest[1].Name);
        // Default (HidePinnedFromNormalList=false): all three segments still appear in the
        // normal list at their original positions.
        Assert.Equal(3, component.NonPinnedSegmentsForTest.Count);
    }

    [Fact]
    public void RebuildRows_HandlesEmptyRun()
    {
        var state = BuildState([], TimerPhase.Running, 0);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.Empty(component.PinnedSegmentsForTest);
    }

    [Fact]
    public void RebuildRows_HandlesAllPinnedAllTimed()
    {
        // 4 pinned segments, all timed
        var state = BuildState(["^A", "^B", "^C", "^D"], TimerPhase.Running, 4);
        var component = new SplitsComponent(state);
        var settings = (SplitsSettings)component.GetSettingsControl(LayoutMode.Vertical);
        settings.MaxDisplayed = 0; // unlimited

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.Equal(4, component.PinnedSegmentsForTest.Count);
        // Default keeps every segment in the normal list as well.
        Assert.Equal(4, component.NonPinnedSegmentsForTest.Count);
    }

    [Fact]
    public void RebuildRows_HandlesEscapedPrefix()
    {
        // ^^Boss is NOT pinned (escape sequence)
        var state = BuildState(["^^Boss", "^RealPin"], TimerPhase.Running, 2);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        // Only ^RealPin (index 1) is pinned and timed
        Assert.Single(component.PinnedSegmentsForTest);
        Assert.Equal("^RealPin", component.PinnedSegmentsForTest[0].Name);
    }

    [Fact]
    public void Run_RemainsUnmodified()
    {
        // Critical: state.Run keeps its raw `^` prefix names so that saves preserve pin markers.
        var state = BuildState(["^Boss", "Normal"], TimerPhase.Running, 1);
        var component = new SplitsComponent(state);

        component.Update(null, state, 0, 0, LayoutMode.Vertical);

        Assert.Equal("^Boss", state.Run[0].Name);
        Assert.Equal("Normal", state.Run[1].Name);
    }

    /// <summary>
    /// Regression test for the all-pinned + ShowColumnLabels OOB crash. When every segment
    /// is pinned AND HidePinnedFromNormalList is on (so timed pinned segments are filtered
    /// out of the normal list), _nonPinnedSegments is empty, visualSplitCount = 0, and
    /// RebuildVisualSplits adds zero SplitComponents. With ShowColumnLabels enabled in
    /// vertical mode, Components becomes [LabelsComponent, SeparatorComponent] only.
    /// Prepare's separator-locking loop must NOT throw IndexOutOfRangeException when
    /// accessing Components[index + 1] on the trailing SeparatorComponent.
    ///
    /// Note: the new default (HidePinnedFromNormalList=false) keeps pinned segments in the
    /// normal list, so _nonPinnedSegments is non-empty and this code path is unreachable
    /// without explicitly opting into the legacy filter behavior. The test pins
    /// HidePinnedFromNormalList=true to keep covering the OOB regression.
    /// </summary>
    [Fact]
    public void Prepare_DoesNotThrow_WhenAllPinnedAndColumnLabelsEnabled()
    {
        var state = BuildState(["^A", "^B", "^C"], TimerPhase.Running, 3);
        var component = new SplitsComponent(state);
        var settings = (SplitsSettings)component.GetSettingsControl(LayoutMode.Vertical);
        settings.ShowColumnLabels = true;
        settings.ShowBlankSplits = false;
        settings.HidePinnedFromNormalList = true;

        // No exception expected. Prepare drives the separator iteration that previously
        // crashed via the unconditional cast `((SplitComponent)Components[index + 1])`.
        var ex = Record.Exception(() => component.Prepare(state));

        Assert.Null(ex);
        // All three pinned segments were timed (CurrentSplitIndex = 3) so all show up.
        Assert.Equal(3, component.PinnedSegmentsForTest.Count);
        // HidePinnedFromNormalList=true filters them out of the normal list, reproducing the
        // empty-_nonPinnedSegments scenario the bug originally hit.
        Assert.Empty(component.NonPinnedSegmentsForTest);
    }

    /// <summary>
    /// Same regression as above but during Paused state — both Running and Paused enter the
    /// separator-locking branch in Prepare, so both must be safe.
    /// </summary>
    [Fact]
    public void Prepare_DoesNotThrow_WhenAllPinnedAndColumnLabelsEnabled_Paused()
    {
        var state = BuildState(["^A", "^B"], TimerPhase.Paused, 2);
        var component = new SplitsComponent(state);
        var settings = (SplitsSettings)component.GetSettingsControl(LayoutMode.Vertical);
        settings.ShowColumnLabels = true;
        settings.HidePinnedFromNormalList = true;

        var ex = Record.Exception(() => component.Prepare(state));

        Assert.Null(ex);
    }

    /// <summary>
    /// Visual-parity smoke test: after Prepare, the pinned pool must have at least as many
    /// entries as the displayed pinned set, and each displayed pinned row must have its
    /// DisplayIcon flag cleared (no icons in this fixture, so iconsNotBlank == false). The
    /// pre-fix code never assigned DisplayIcon to pinned-pool entries at all, so the flag
    /// could have arbitrary leftover state from an earlier reuse. Asserting "false here"
    /// confirms the post-fix loop ran for every displayed pinned row.
    /// Note: a stronger assertion (DisplayIcon == true with actual segment icons) would
    /// require bitmap fixtures that LiveSplit's test infra does not provide; this is left
    /// as a manual layout-test step.
    /// </summary>
    [Fact]
    public void Prepare_AssignsDisplayIcon_OnAllDisplayedPinnedRows()
    {
        var state = BuildState(["^Boss1", "^Boss2", "Normal"], TimerPhase.Running, 3);
        var component = new SplitsComponent(state);
        var settings = (SplitsSettings)component.GetSettingsControl(LayoutMode.Vertical);
        settings.DisplayIcons = true;

        // Pre-poison the pool so we can prove the loop overwrites the leftover state.
        // The pool grows on demand inside Prepare, so we have to trigger an Update first
        // to populate two entries, then mutate them, then call Prepare.
        component.Update(null, state, 0, 0, LayoutMode.Vertical);
        Assert.Equal(2, component.PinnedSegmentsForTest.Count);
        Assert.True(component.PinnedSplitPoolForTest.Count >= 2);
        component.PinnedSplitPoolForTest[0].DisplayIcon = true;
        component.PinnedSplitPoolForTest[1].DisplayIcon = true;

        component.Prepare(state);

        // No segment carries a bitmap, so iconsNotBlank=false → DisplayIcon must be false
        // for every displayed pinned row after Prepare. If the pre-fix code (which never
        // touched PinnedSplitPool) ran here, the poisoned 'true' values would persist.
        Assert.False(component.PinnedSplitPoolForTest[0].DisplayIcon);
        Assert.False(component.PinnedSplitPoolForTest[1].DisplayIcon);
    }
}
