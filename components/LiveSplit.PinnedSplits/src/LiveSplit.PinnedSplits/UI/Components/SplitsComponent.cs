using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.TimeFormatters;

namespace LiveSplit.UI.Components;

[GlobalFontConsumer(GlobalFont.TimesFont)]
public class SplitsComponent : IComponent
{
    public ComponentRendererComponent InternalComponent { get; protected set; }

    public float PaddingTop => InternalComponent.PaddingTop;
    public float PaddingLeft => InternalComponent.PaddingLeft;
    public float PaddingBottom => InternalComponent.PaddingBottom;
    public float PaddingRight => InternalComponent.PaddingRight;

    protected IList<IComponent> Components { get; set; }
    protected IList<SplitComponent> SplitComponents { get; set; }

    // Pinned-section state: dynamic pool of rows rendered at the top, plus a separator
    // that visually divides the pinned section from the normal one. The combined list
    // is what we hand to InternalComponent.VisibleComponents on every frame.
    protected List<SplitComponent> PinnedSplitPool { get; set; }
    protected SeparatorComponent PinnedSeparator { get; set; }
    private List<IComponent> _displayedComponents;

    // Cache populated each frame by ComputePinnedAndFiltered so Update/Prepare can share results.
    // _pinnedToShow holds the already-timed pinned segments rendered at the top section.
    // _nonPinnedSegments holds everything rendered in the normal list: regular segments PLUS
    // pinned segments that have not yet been timed (so they are still visible to the user
    // before the run reaches them, then promoted to _pinnedToShow once timed).
    // _mappedCurrentSplitIndex is state.CurrentSplitIndex projected into _nonPinnedSegments.
    private List<ISegment> _pinnedToShow;
    private List<ISegment> _nonPinnedSegments;
    private int _mappedCurrentSplitIndex;

    protected SplitsSettings Settings { get; set; }

    protected SimpleLabel MeasureTimeLabel { get; set; }
    protected SimpleLabel MeasureDeltaLabel { get; set; }
    protected SimpleLabel MeasureCharLabel { get; set; }

    protected TimeAccuracy CurrentAccuracy { get; set; }
    protected TimeAccuracy CurrentDeltaAccuracy { get; set; }
    protected bool CurrentDropDecimals { get; set; }

    protected ITimeFormatter TimeFormatter { get; set; }
    protected ITimeFormatter DeltaTimeFormatter { get; set; }

    private Dictionary<Image, Image> ShadowImages { get; set; }

    private int visualSplitCount;
    private int settingsSplitCount;

    protected bool PreviousShowLabels { get; set; }

    protected int ScrollOffset { get; set; }
    protected int LastSplitSeparatorIndex { get; set; }

    protected LiveSplitState CurrentState { get; set; }
    protected LiveSplitState OldState { get; set; }
    protected LayoutMode OldLayoutMode { get; set; }
    protected Color OldShadowsColor { get; set; }

    protected IEnumerable<ColumnData> ColumnsList => Settings.ColumnsList.Select(x => x.Data);
    protected List<(int exLength, float exWidth, float width)> ColumnWidths { get; set; }

    public string ComponentName => "Pinned Splits";

    // Test surface (LiveSplit.PinnedSplits.Tests is whitelisted via InternalsVisibleTo).
    // Exposes the live pinned-section state without forcing tests to reflect on private
    // fields. The list reference itself is reused across frames; tests should snapshot
    // it (e.g. via .ToList()) if they need to compare against a later state.
    internal IReadOnlyList<ISegment> PinnedSegmentsForTest => _pinnedToShow;
    internal IReadOnlyList<SplitComponent> PinnedSplitPoolForTest => PinnedSplitPool;
    // Mirrors PinnedSegmentsForTest for the normal-area segment list so tests can verify that
    // not-yet-timed pinned segments are still surfaced in the regular list (rather than
    // disappearing entirely). Same lifetime contract as PinnedSegmentsForTest: the underlying
    // List<ISegment> is reused across frames, so tests should snapshot if needed.
    internal IReadOnlyList<ISegment> NonPinnedSegmentsForTest => _nonPinnedSegments;
    // Mirrors the pinned-pool accessor for the regular splits pool so style tests can verify
    // that pinned segments rendered in the NORMAL section (i.e. not yet timed, or bumped out
    // of the pinned section by MaxDisplayed) still pick up the Pinned* colors. The list is
    // SplitComponents (a List<SplitComponent>), so direct cast to IReadOnlyList is safe.
    internal IReadOnlyList<SplitComponent> NormalSplitComponentsForTest => (IReadOnlyList<SplitComponent>)SplitComponents;

    public float VerticalHeight => InternalComponent.VerticalHeight;

    public float MinimumWidth => InternalComponent.MinimumWidth;

    public float HorizontalWidth => InternalComponent.HorizontalWidth;

    public float MinimumHeight => InternalComponent.MinimumHeight;

    public IDictionary<string, Action> ContextMenuControls => null;

    public SplitsComponent(LiveSplitState state)
    {
        CurrentState = state;
        Settings = new SplitsSettings(state);
        InternalComponent = new ComponentRendererComponent();

        MeasureTimeLabel = new SimpleLabel();
        MeasureDeltaLabel = new SimpleLabel();
        MeasureCharLabel = new SimpleLabel();
        CurrentAccuracy = Settings.SplitTimesAccuracy;
        CurrentDeltaAccuracy = Settings.DeltasAccuracy;
        CurrentDropDecimals = Settings.DropDecimals;
        TimeFormatter = new SplitTimeFormatter(CurrentAccuracy);
        DeltaTimeFormatter = new DeltaSplitTimeFormatter(CurrentDeltaAccuracy, CurrentDropDecimals);

        ShadowImages = [];
        visualSplitCount = Settings.VisualSplitCount;
        settingsSplitCount = Settings.VisualSplitCount;
        Settings.SplitLayoutChanged += Settings_SplitLayoutChanged;
        ColumnWidths = Settings.ColumnsList.Select(_ => (0, 0f, 0f)).ToList();
        ScrollOffset = 0;
        PinnedSplitPool = [];
        PinnedSeparator = new SeparatorComponent();
        _displayedComponents = [];
        _pinnedToShow = [];
        _nonPinnedSegments = [];
        _mappedCurrentSplitIndex = 0;
        RebuildVisualSplits();
        state.ComparisonRenamed += state_ComparisonRenamed;
    }

    /// <summary>
    /// Walks state.Run once and produces:
    ///  - the list of pinned segments to show at the top (already-timed, capped by MaxDisplayed),
    ///  - the list of segments displayed in the normal area. By default this includes every
    ///    segment in state.Run (so timed pinned segments appear simultaneously at the top AND
    ///    in their original position, giving full context); when Settings.HidePinnedFromNormalList
    ///    is true the timed pinned segments are filtered out of the normal list and only render
    ///    in the top section. Untimed pinned segments always stay in the normal list — they
    ///    haven't been promoted to the top section yet.
    ///  - the position of state.CurrentSplitIndex when projected into the normal-area list.
    /// Caches the results in the _pinned* / _nonPinned* / _mappedCurrentSplitIndex fields so both
    /// Prepare and Update can reuse them within a single frame.
    /// </summary>
    private void ComputePinnedAndFiltered(LiveSplitState state)
    {
        _pinnedToShow.Clear();
        _nonPinnedSegments.Clear();
        _mappedCurrentSplitIndex = 0;

        if (state?.Run == null)
        {
            return;
        }

        bool runActive = state.CurrentPhase != TimerPhase.NotRunning;
        int currentIdx = state.CurrentSplitIndex;
        bool hidePinnedFromNormal = Settings.HidePinnedFromNormalList;

        for (int i = 0; i < state.Run.Count; i++)
        {
            ISegment seg = state.Run[i];
            (bool isPinned, _) = PinnedSegmentParser.Parse(seg.Name);

            // A pinned segment graduates to the top section only after it has been timed
            // (i.e. the run is active AND the split index is behind the current one, or the
            // user opted to also include the active split).
            bool promotedToPinnedSection = isPinned
                && runActive
                && (i < currentIdx
                    || (Settings.IncludeCurrentSplit && i == currentIdx));

            if (promotedToPinnedSection)
            {
                _pinnedToShow.Add(seg);
            }

            // Decide whether this segment also appears in the normal list. Default behavior:
            // every segment shows in the normal list. Optional hide-pinned mode: timed pinned
            // segments are filtered out of the normal list (legacy behavior). Untimed pinned
            // segments and regular segments always appear here.
            bool showInNormalList = !(promotedToPinnedSection && hidePinnedFromNormal);
            if (showInNormalList)
            {
                if (i < currentIdx)
                {
                    _mappedCurrentSplitIndex++;
                }

                _nonPinnedSegments.Add(seg);
            }
        }

        // Apply MaxDisplayed cap (0 = unlimited; otherwise keep the most recent N).
        if (Settings.MaxDisplayed > 0 && _pinnedToShow.Count > Settings.MaxDisplayed)
        {
            int drop = _pinnedToShow.Count - Settings.MaxDisplayed;
            _pinnedToShow.RemoveRange(0, drop);
        }
    }

    /// <summary>
    /// Grows <see cref="PinnedSplitPool"/> on demand so the pool has at least <paramref name="size"/>
    /// entries available. The pool only grows; trailing slots from a previous high-water mark are
    /// reused as-is (their <c>Split</c> will be reassigned when needed). Called both from the
    /// per-frame icon/shadow setup in Prepare and from RefreshDisplayedComponents so the two
    /// independent paths can rely on the pool being correctly sized without coupling them.
    ///
    /// Every slot is created with <see cref="SplitComponent.IsPinnedSlot"/> set to true. This is
    /// the single source of truth for the pinned color override: the gate inside SplitComponent
    /// (UsePinnedStyle) checks IsPinnedSlot rather than the segment name, so a pinned segment
    /// that simultaneously renders in the normal list (default behavior when
    /// HidePinnedFromNormalList is off) does NOT pick up the pinned color — only its top-section
    /// counterpart does.
    /// </summary>
    private void EnsurePinnedPoolSize(int size)
    {
        while (PinnedSplitPool.Count < size)
        {
            PinnedSplitPool.Add(new SplitComponent(Settings, ColumnsList, ColumnWidths) { IsPinnedSlot = true });
        }
    }

    /// <summary>
    /// Rebuilds InternalComponent.VisibleComponents to be:
    ///   [optional column labels + separator..., pinned rows..., (separator if any pinned)..., remaining Components...].
    /// The optional column-labels prefix from the original Components list is kept at the very top
    /// so headers stay visible above the pinned section. Pool size grows on demand; trailing slots
    /// whose Split was assigned in a previous frame but are not used this frame are simply omitted
    /// from VisibleComponents and therefore not rendered.
    /// </summary>
    private void RefreshDisplayedComponents()
    {
        // Grow the pinned pool to fit the segments we want to show.
        EnsurePinnedPoolSize(_pinnedToShow.Count);

        for (int i = 0; i < _pinnedToShow.Count; i++)
        {
            PinnedSplitPool[i].Split = _pinnedToShow[i];
        }

        // Determine how many leading items in `Components` are the optional column-labels header
        // (a LabelsComponent followed by a SeparatorComponent). RebuildVisualSplits only emits
        // these when ShowColumnLabels is true and the layout is vertical, so we mirror that gate.
        int headerItemCount = 0;
        if (Settings.ShowColumnLabels && CurrentState?.Layout?.Mode == LayoutMode.Vertical
            && Components.Count >= 2
            && Components[0] is LabelsComponent
            && Components[1] is SeparatorComponent)
        {
            headerItemCount = 2;
        }

        _displayedComponents.Clear();

        // Header (column labels) at the top, before pinned section.
        for (int i = 0; i < headerItemCount; i++)
        {
            _displayedComponents.Add(Components[i]);
        }

        // Pinned section.
        for (int i = 0; i < _pinnedToShow.Count; i++)
        {
            _displayedComponents.Add(PinnedSplitPool[i]);
        }

        if (_pinnedToShow.Count > 0)
        {
            _displayedComponents.Add(PinnedSeparator);
        }

        // Remaining components (split rows, thin separators, last-split separator).
        for (int i = headerItemCount; i < Components.Count; i++)
        {
            _displayedComponents.Add(Components[i]);
        }

        InternalComponent.VisibleComponents = _displayedComponents;
    }

    private void state_ComparisonRenamed(object sender, EventArgs e)
    {
        var args = (RenameEventArgs)e;
        foreach (ColumnData column in ColumnsList)
        {
            if (column.Comparison == args.OldName)
            {
                column.Comparison = args.NewName;
                ((LiveSplitState)sender).Layout.HasChanged = true;
            }
        }
    }

    private void Settings_SplitLayoutChanged(object sender, EventArgs e)
    {
        RebuildVisualSplits();
    }

    private void RebuildVisualSplits()
    {
        Components = [];
        SplitComponents = [];
        InternalComponent.VisibleComponents = Components;

        int totalSplits = Settings.ShowBlankSplits ? Math.Max(Settings.VisualSplitCount, visualSplitCount) : visualSplitCount;

        if (Settings.ShowColumnLabels && CurrentState.Layout?.Mode == LayoutMode.Vertical)
        {
            Components.Add(new LabelsComponent(Settings, ColumnsList, ColumnWidths));
            Components.Add(new SeparatorComponent());
        }

        for (int i = 0; i < totalSplits; ++i)
        {
            if (i == totalSplits - 1 && i > 0)
            {
                LastSplitSeparatorIndex = Components.Count;
                if (Settings.AlwaysShowLastSplit && Settings.SeparatorLastSplit)
                {
                    Components.Add(new SeparatorComponent());
                }
                else if (Settings.ShowThinSeparators)
                {
                    Components.Add(new ThinSeparatorComponent());
                }
            }

            var splitComponent = new SplitComponent(Settings, ColumnsList, ColumnWidths);
            Components.Add(splitComponent);
            if (i < visualSplitCount - 1 || i == (Settings.LockLastSplit ? totalSplits - 1 : visualSplitCount - 1))
            {
                SplitComponents.Add(splitComponent);
            }

            if (Settings.ShowThinSeparators && i < totalSplits - 2)
            {
                Components.Add(new ThinSeparatorComponent());
            }
        }
    }

    // Internal for test access (LiveSplit.PinnedSplits.Tests is whitelisted via InternalsVisibleTo).
    // Production callers (DrawVertical/DrawHorizontal) treat this exactly as before.
    internal void Prepare(LiveSplitState state)
    {
        if (state != OldState)
        {
            state.OnScrollDown += state_OnScrollDown;
            state.OnScrollUp += state_OnScrollUp;
            state.OnStart += state_OnStart;
            state.OnReset += state_OnReset;
            state.OnSplit += state_OnSplit;
            state.OnSkipSplit += state_OnSkipSplit;
            state.OnUndoSplit += state_OnUndoSplit;
            OldState = state;
        }

        if (Settings.SplitTimesAccuracy != CurrentAccuracy)
        {
            TimeFormatter = new SplitTimeFormatter(Settings.SplitTimesAccuracy);
            CurrentAccuracy = Settings.SplitTimesAccuracy;
        }

        if (Settings.DeltasAccuracy != CurrentDeltaAccuracy || Settings.DropDecimals != CurrentDropDecimals)
        {
            DeltaTimeFormatter = new DeltaSplitTimeFormatter(Settings.DeltasAccuracy, Settings.DropDecimals);
            CurrentDeltaAccuracy = Settings.DeltasAccuracy;
            CurrentDropDecimals = Settings.DropDecimals;
        }

        // Compute pinned-to-show + non-pinned filtered list for this frame. All scroll/visualSplitCount
        // calculations below operate on the FILTERED list so pinned segments are not double-counted.
        ComputePinnedAndFiltered(state);

        int previousSplitCount = visualSplitCount;
        visualSplitCount = Math.Min(_nonPinnedSegments.Count, Settings.VisualSplitCount);
        if (previousSplitCount != visualSplitCount
            || (Settings.ShowBlankSplits && settingsSplitCount != Settings.VisualSplitCount)
            || Settings.ShowColumnLabels != PreviousShowLabels
            || (Settings.ShowColumnLabels && state.Layout.Mode != OldLayoutMode))
        {
            PreviousShowLabels = Settings.ShowColumnLabels;
            OldLayoutMode = state.Layout.Mode;
            RebuildVisualSplits();
        }

        settingsSplitCount = Settings.VisualSplitCount;

        int skipCount = Math.Min(
            Math.Max(
                0,
                _mappedCurrentSplitIndex - (visualSplitCount - 2 - Settings.SplitPreviewCount + (Settings.AlwaysShowLastSplit ? 0 : 1))),
            _nonPinnedSegments.Count - visualSplitCount);
        ScrollOffset = Math.Min(Math.Max(ScrollOffset, -skipCount), _nonPinnedSegments.Count - skipCount - visualSplitCount);
        skipCount += ScrollOffset;

        if (OldShadowsColor != state.LayoutSettings.ShadowsColor)
        {
            ShadowImages.Clear();
        }

        foreach (ISegment split in state.Run)
        {
            if (split.Icon != null && (!ShadowImages.ContainsKey(split.Icon) || OldShadowsColor != state.LayoutSettings.ShadowsColor))
            {
                ShadowImages.Add(split.Icon, IconShadow.Generate(split.Icon, state.LayoutSettings.ShadowsColor));
            }
        }

        bool iconsNotBlank = state.Run.Count(x => x.Icon != null) > 0;
        foreach (SplitComponent split in SplitComponents)
        {
            split.DisplayIcon = iconsNotBlank && Settings.DisplayIcons;

            if (split.Split != null && split.Split.Icon != null)
            {
                split.ShadowImage = ShadowImages[split.Split.Icon];
            }
            else
            {
                split.ShadowImage = null;
            }
        }

        // Apply the same icon/shadow setup to the pinned rows that are actually
        // displayed this frame. Without this, pinned rows render with the default
        // SplitComponent state (no icon, no shadow) even when the layout has icons
        // enabled, breaking visual parity with the normal splits.
        //
        // Note: we read the segment from _pinnedToShow (current-frame data) rather
        // than PinnedSplitPool[p].Split, because the pool's Split is only populated
        // by RefreshDisplayedComponents, which runs at the END of Prepare. Reading
        // from _pinnedToShow makes this loop independent of call ordering and works
        // correctly even on the first frame (when the pool's Split is still null).
        EnsurePinnedPoolSize(_pinnedToShow.Count);
        for (int p = 0; p < _pinnedToShow.Count; p++)
        {
            SplitComponent pinned = PinnedSplitPool[p];
            ISegment pinnedSeg = _pinnedToShow[p];
            pinned.DisplayIcon = iconsNotBlank && Settings.DisplayIcons;

            if (pinnedSeg?.Icon != null)
            {
                pinned.ShadowImage = ShadowImages[pinnedSeg.Icon];
            }
            else
            {
                pinned.ShadowImage = null;
            }
        }

        OldShadowsColor = state.LayoutSettings.ShadowsColor;

        foreach (IComponent component in Components)
        {
            if (component is SeparatorComponent separator)
            {
                int index = Components.IndexOf(separator);
                if (state.CurrentPhase is TimerPhase.Running or TimerPhase.Paused)
                {
                    // Bounds + type guards: when the run filters down to zero non-pinned segments
                    // and ShowColumnLabels is enabled, Components can be just [LabelsComponent,
                    // SeparatorComponent], so a naked cast on Components[index+1] would throw.
                    if (index + 1 < Components.Count
                        && Components[index + 1] is SplitComponent rightSplit
                        && rightSplit.Split == state.CurrentSplit)
                    {
                        separator.LockToBottom = true;
                    }
                    else if (index - 1 >= 0
                        && Components[index - 1] is SplitComponent leftSplit
                        && leftSplit.Split == state.CurrentSplit)
                    {
                        separator.LockToBottom = false;
                    }
                }

                if (Settings.AlwaysShowLastSplit && Settings.SeparatorLastSplit && index == LastSplitSeparatorIndex)
                {
                    if (skipCount >= _nonPinnedSegments.Count - visualSplitCount)
                    {
                        if (Settings.ShowThinSeparators)
                        {
                            separator.DisplayedSize = 1f;
                        }
                        else
                        {
                            separator.DisplayedSize = 0f;
                        }

                        separator.UseSeparatorColor = false;
                    }
                    else
                    {
                        separator.DisplayedSize = 2f;
                        separator.UseSeparatorColor = true;
                    }
                }
            }
            else if (component is ThinSeparatorComponent thinSeparator)
            {
                int index = Components.IndexOf(thinSeparator);
                if (state.CurrentPhase is TimerPhase.Running or TimerPhase.Paused)
                {
                    // Bounds + type guards (same rationale as the SeparatorComponent branch above).
                    if (index + 1 < Components.Count
                        && Components[index + 1] is SplitComponent rightThinSplit
                        && rightThinSplit.Split == state.CurrentSplit)
                    {
                        thinSeparator.LockToBottom = true;
                    }
                    else if (index - 1 >= 0
                        && Components[index - 1] is SplitComponent leftThinSplit
                        && leftThinSplit.Split == state.CurrentSplit)
                    {
                        thinSeparator.LockToBottom = false;
                    }
                }
            }
        }

        // Rebuild VisibleComponents = pinned section + (separator) + Components.
        // Done at the end of Prepare so subsequent DrawVertical/Horizontal pick up the
        // current frame's pinned set without an extra Update call.
        RefreshDisplayedComponents();
    }

    private void state_OnUndoSplit(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnSkipSplit(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnSplit(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnReset(object sender, TimerPhase e)
    {
        ScrollOffset = 0;
    }

    private void state_OnStart(object sender, EventArgs e)
    {
        ScrollOffset = 0;
    }

    private void state_OnScrollUp(object sender, EventArgs e)
    {
        ScrollOffset--;
    }

    private void state_OnScrollDown(object sender, EventArgs e)
    {
        ScrollOffset++;
    }

    private void DrawBackground(Graphics g, float width, float height)
    {
        if (Settings.BackgroundGradient != ExtendedGradientType.Alternating
            && (Settings.BackgroundColor.A > 0
            || (Settings.BackgroundGradient != ExtendedGradientType.Plain
            && Settings.BackgroundColor2.A > 0)))
        {
            var gradientBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        Settings.BackgroundGradient == ExtendedGradientType.Horizontal
                        ? new PointF(width, 0)
                        : new PointF(0, height),
                        Settings.BackgroundColor,
                        Settings.BackgroundGradient == ExtendedGradientType.Plain
                        ? Settings.BackgroundColor
                        : Settings.BackgroundColor2);
            g.FillRectangle(gradientBrush, 0, 0, width, height);
        }
    }

    private void SetMeasureLabels(Graphics g, LiveSplitState state)
    {
        MeasureTimeLabel.Text = TimeFormatter.Format(new TimeSpan(24, 0, 0));
        MeasureDeltaLabel.Text = DeltaTimeFormatter.Format(new TimeSpan(0, 9, 0, 0));
        MeasureCharLabel.Text = "W";

        MeasureTimeLabel.Font = state.LayoutSettings.TimesFont;
        MeasureTimeLabel.IsMonospaced = true;
        MeasureDeltaLabel.Font = state.LayoutSettings.TimesFont;
        MeasureDeltaLabel.IsMonospaced = true;
        MeasureCharLabel.Font = state.LayoutSettings.TimesFont;
        MeasureCharLabel.IsMonospaced = true;

        MeasureTimeLabel.SetActualWidth(g);
        MeasureDeltaLabel.SetActualWidth(g);
        MeasureCharLabel.SetActualWidth(g);
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        Prepare(state);
        DrawBackground(g, width, VerticalHeight);
        SetMeasureLabels(g, state);
        InternalComponent.DrawVertical(g, state, width, clipRegion);
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        Prepare(state);
        DrawBackground(g, HorizontalWidth, height);
        SetMeasureLabels(g, state);
        InternalComponent.DrawHorizontal(g, state, height, clipRegion);
    }

    public Control GetSettingsControl(LayoutMode mode)
    {
        Settings.Mode = mode;
        return Settings;
    }

    public void SetSettings(System.Xml.XmlNode settings)
    {
        Settings.SetSettings(settings);
        RebuildVisualSplits();
    }

    public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document)
    {
        return Settings.GetSettings(document);
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        // Recompute the pinned/non-pinned partition for this frame. Update is called every tick
        // by LiveSplit's main loop, so this also keeps the pinned section in sync with run progress
        // even between Draw cycles.
        ComputePinnedAndFiltered(state);

        int skipCount = Math.Min(
            Math.Max(
                0,
                _mappedCurrentSplitIndex - (visualSplitCount - 2 - Settings.SplitPreviewCount + (Settings.AlwaysShowLastSplit ? 0 : 1))),
            _nonPinnedSegments.Count - visualSplitCount);
        ScrollOffset = Math.Min(Math.Max(ScrollOffset, -skipCount), _nonPinnedSegments.Count - skipCount - visualSplitCount);
        skipCount += ScrollOffset;

        int i = 0;
        if (SplitComponents.Count >= visualSplitCount)
        {
            foreach (ISegment split in _nonPinnedSegments.Skip(skipCount).Take(visualSplitCount - 1 + (Settings.AlwaysShowLastSplit ? 0 : 1)))
            {
                SplitComponents[i].Split = split;
                i++;
            }

            if (Settings.AlwaysShowLastSplit && _nonPinnedSegments.Count > 0)
            {
                SplitComponents[i].Split = _nonPinnedSegments[_nonPinnedSegments.Count - 1];
            }
        }

        CalculateColumnWidths(state.Run);

        // Refresh the combined visible-components list before forwarding Update so the
        // ComponentRendererComponent picks up the right number of pinned rows on this frame.
        RefreshDisplayedComponents();

        if (invalidator != null)
        {
            InternalComponent.Update(invalidator, state, width, height, mode);
        }
    }

    private void CalculateColumnWidths(IRun run)
    {
        if (ColumnsList != null)
        {
            while (ColumnWidths.Count < ColumnsList.Count())
            {
                ColumnWidths.Add((0, 0f, 0f));
            }

            TimeSpan longestTime = new TimeSpan(9, 0, 0);
            TimeSpan longestDelta = new TimeSpan(0, 0, 59, 0);
            foreach (ISegment split in run.Reverse())
            {
                if (split.SplitTime.RealTime is TimeSpan splitRealTime && longestTime < splitRealTime)
                {
                    longestTime = splitRealTime;
                }

                foreach (KeyValuePair<string, Time> kv in split.Comparisons)
                {
                    if (kv.Value.RealTime is TimeSpan cmpRealTime && longestTime < cmpRealTime)
                    {
                        longestTime = cmpRealTime;
                    }

                    if (split.SplitTime.RealTime - kv.Value.RealTime is TimeSpan deltaRealTime)
                    {
                        if (longestDelta < deltaRealTime)
                        {
                            longestDelta = deltaRealTime;
                        }
                        else if (longestDelta < (- deltaRealTime))
                        {
                            longestDelta = - deltaRealTime;
                        }
                    }
                }
            }

            int timeLength = TimeFormatter.Format(longestTime).Length;
            int deltaLength = DeltaTimeFormatter.Format(longestDelta).Length;
            float timeCharWidth = MeasureTimeLabel.Text.Length > 0 ? MeasureTimeLabel.ActualWidth / MeasureTimeLabel.Text.Length : MeasureCharLabel.ActualWidth;
            float timeWidth = Math.Max(MeasureTimeLabel.ActualWidth, timeCharWidth * (timeLength + 1));
            float deltaWidth = Math.Max(MeasureDeltaLabel.ActualWidth, timeCharWidth * (deltaLength + 1));

            for (int i = 0; i < ColumnsList.Count(); i++)
            {
                ColumnData column = ColumnsList.ElementAt(i);

                float labelWidth = 0f;
                if (column.Type is ColumnType.DeltaorSplitTime or ColumnType.SegmentDeltaorSegmentTime)
                {
                    labelWidth = Math.Max(deltaWidth, timeWidth);
                }
                else if (column.Type is ColumnType.Delta or ColumnType.SegmentDelta)
                {
                    labelWidth = deltaWidth;
                }
                else if (column.Type is ColumnType.SplitTime or ColumnType.SegmentTime)
                {
                    labelWidth = timeWidth;
                }
                else if (column.Type is ColumnType.CustomVariable)
                {
                    int longestLength = run.Metadata.CustomVariableValue(column.Name)?.Length ?? 0;
                    foreach (ISegment split in run)
                    {
                        if (split.CustomVariableValues.TryGetValue(column.Name, out string value) && !string.IsNullOrEmpty(value))
                        {
                            longestLength = Math.Max(longestLength, value.Length);
                        }
                    }

                    float exCharWidth = ColumnWidths[i].exLength > 0 ? ColumnWidths[i].exWidth / ColumnWidths[i].exLength : MeasureCharLabel.ActualWidth;
                    labelWidth = exCharWidth * (longestLength + 1);
                }

                ColumnWidths[i] = (ColumnWidths[i].exLength, ColumnWidths[i].exWidth, labelWidth);
            }
        }
    }

    public void Dispose()
    {
    }

    public int GetSettingsHashCode()
    {
        return Settings.GetSettingsHashCode();
    }
}
