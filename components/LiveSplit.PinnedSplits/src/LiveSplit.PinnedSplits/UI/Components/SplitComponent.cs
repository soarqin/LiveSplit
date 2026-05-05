using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

using LiveSplit.Model;
using LiveSplit.TimeFormatters;

namespace LiveSplit.UI.Components;

[GlobalFontConsumer(GlobalFont.TimesFont | GlobalFont.TextFont)]
public class SplitComponent : IComponent
{
    public ISegment Split { get; set; }

    /// <summary>
    /// Marks this row as living in the TOP pinned section (i.e. an entry in
    /// SplitsComponent.PinnedSplitPool). Set by the owning SplitsComponent at pool-creation
    /// time and never mutated afterwards. The pinned color override
    /// (Settings.OverridePinnedColor + Settings.Pinned*Color) applies ONLY to slots flagged
    /// here. The same pinned segment, when rendered simultaneously in the normal list (the
    /// default behavior controlled by Settings.HidePinnedFromNormalList), keeps its regular
    /// Before/Current/After colors so the override doesn't bleed into the normal list.
    /// Defaults to false so the regular splits pool behaves like the stock Splits component
    /// without any opt-in.
    /// </summary>
    public bool IsPinnedSlot { get; set; }

    protected SimpleLabel NameLabel { get; set; }
    public SplitsSettings Settings { get; set; }

    protected int FrameCount { get; set; }

    public GraphicsCache Cache { get; set; }
    protected bool NeedUpdateAll { get; set; }
    protected bool IsActive { get; set; }

    protected TimeAccuracy CurrentAccuracy { get; set; }
    protected TimeAccuracy CurrentDeltaAccuracy { get; set; }
    protected bool CurrentDropDecimals { get; set; }

    protected ITimeFormatter TimeFormatter { get; set; }
    protected ITimeFormatter DeltaTimeFormatter { get; set; }

    protected int IconWidth => DisplayIcon ? (int)(Settings.IconSize + 7.5f) : 0;

    public bool DisplayIcon { get; set; }

    public Image ShadowImage { get; set; }
    protected Image OldImage { get; set; }

    public float PaddingTop => 0f;
    public float PaddingLeft => 0f;
    public float PaddingBottom => 0f;
    public float PaddingRight => 0f;

    public IEnumerable<ColumnData> ColumnsList { get; set; }
    public IList<SimpleLabel> LabelsList { get; set; }
    protected List<(int exLength, float exWidth, float width)> ColumnWidths { get; }

    public float VerticalHeight { get; set; }

    public float MinimumWidth
        => CalculateLabelsWidth() + IconWidth + 10;

    public float HorizontalWidth
        => Settings.SplitWidth + CalculateLabelsWidth() + IconWidth;

    public float MinimumHeight { get; set; }

    public IDictionary<string, Action> ContextMenuControls => null;

    // Test surface (LiveSplit.PinnedSplits.Tests is whitelisted via InternalsVisibleTo).
    // Exposes the resolved label colors so tests can assert that a Split rendered in a
    // pinned-section slot resolves to PinnedXxxColor when OverridePinnedColor is on, while
    // the same Split rendered in a normal-list slot keeps the regular palette.
    internal Color NameForeColorForTest => NameLabel?.ForeColor ?? Color.Empty;
    internal IReadOnlyList<Color> LabelForeColorsForTest
        => LabelsList == null
            ? []
            : LabelsList.Select(l => l.ForeColor).ToList();
    internal bool IsPinnedSegmentForTest
        => Split != null && PinnedSegmentParser.Parse(Split.Name).IsPinned;

    /// <summary>
    /// Returns true when this row is a pinned-section slot (IsPinnedSlot, set by the owning
    /// SplitsComponent on entries of PinnedSplitPool) AND the user has opted into the pinned
    /// color override. Critically: this does NOT depend on the segment's `^` marker — the
    /// owning SplitsComponent only routes pinned segments into PinnedSplitPool slots, so
    /// IsPinnedSlot already implies "pinned segment" by construction. The slot-based gate
    /// guarantees that when a pinned segment is rendered simultaneously in the normal list
    /// (the default when HidePinnedFromNormalList is off), the normal-list copy keeps the
    /// regular Before/Current/After colors and the pinned override stays scoped to the top
    /// section only. The CurrentSplit background gradient is intentionally NOT pinned-
    /// overridden — "pinned" and "current split" are orthogonal concepts.
    /// </summary>
    private bool UsePinnedStyle()
        => Split != null
            && Settings.OverridePinnedColor
            && IsPinnedSlot;

    public SplitComponent(SplitsSettings settings, IEnumerable<ColumnData> columnsList, List<(int exLength, float exWidth, float width)> columnWidths)
    {
        NameLabel = new SimpleLabel()
        {
            HorizontalAlignment = StringAlignment.Near,
            X = 8,
        };
        Settings = settings;
        ColumnsList = columnsList;
        ColumnWidths = columnWidths;
        TimeFormatter = new SplitTimeFormatter(Settings.SplitTimesAccuracy);
        DeltaTimeFormatter = new DeltaSplitTimeFormatter(Settings.DeltasAccuracy, Settings.DropDecimals);
        MinimumHeight = 25;
        VerticalHeight = 31;

        NeedUpdateAll = true;
        IsActive = false;

        Cache = new GraphicsCache();
        LabelsList = [];
    }

    private void DrawGeneral(Graphics g, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        if (NeedUpdateAll)
        {
            UpdateAll(state);
        }

        if (Settings.BackgroundGradient == ExtendedGradientType.Alternating)
        {
            g.FillRectangle(new SolidBrush(
                (state.Run.IndexOf(Split) % 2) + (Settings.ShowColumnLabels ? 1 : 0) == 1
                ? Settings.BackgroundColor2
                : Settings.BackgroundColor
                ), 0, 0, width, height);
        }

        NameLabel.ShadowColor = state.LayoutSettings.ShadowsColor;
        NameLabel.OutlineColor = state.LayoutSettings.TextOutlineColor;
        foreach (SimpleLabel label in LabelsList)
        {
            label.ShadowColor = state.LayoutSettings.ShadowsColor;
            label.OutlineColor = state.LayoutSettings.TextOutlineColor;
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

        if (Split != null)
        {

            if (mode == LayoutMode.Vertical)
            {
                NameLabel.VerticalAlignment = StringAlignment.Center;
                NameLabel.Y = 0;
                NameLabel.Height = height;
                foreach (SimpleLabel label in LabelsList)
                {
                    label.VerticalAlignment = StringAlignment.Center;
                    label.Y = 0;
                    label.Height = height;
                }
            }
            else
            {
                NameLabel.VerticalAlignment = StringAlignment.Near;
                NameLabel.Y = 0;
                NameLabel.Height = 50;
                foreach (SimpleLabel label in LabelsList)
                {
                    label.VerticalAlignment = StringAlignment.Far;
                    label.Y = height - 50;
                    label.Height = 50;
                }
            }

            if (IsActive)
            {
                // CurrentSplit highlight uses the regular gradient unconditionally — there is
                // intentionally no pinned-specific override for the background, because
                // "pinned" and "current split" are orthogonal concepts and the user picks
                // exactly one current-split highlight regardless of pin state.
                var currentSplitBrush = new LinearGradientBrush(
                    new PointF(0, 0),
                    Settings.CurrentSplitGradient == GradientType.Horizontal
                    ? new PointF(width, 0)
                    : new PointF(0, height),
                    Settings.CurrentSplitTopColor,
                    Settings.CurrentSplitGradient == GradientType.Plain
                    ? Settings.CurrentSplitTopColor
                    : Settings.CurrentSplitBottomColor);
                g.FillRectangle(currentSplitBrush, 0, 0, width, height);
            }

            Image icon = Split.Icon;
            if (DisplayIcon && icon != null)
            {
                Image shadow = ShadowImage;

                if (OldImage != icon)
                {
                    ImageAnimator.Animate(icon, (s, o) => { });
                    ImageAnimator.Animate(shadow, (s, o) => { });
                    OldImage = icon;
                }

                float drawWidth = Settings.IconSize;
                float drawHeight = Settings.IconSize;
                float shadowWidth = Settings.IconSize * (5 / 4f);
                float shadowHeight = Settings.IconSize * (5 / 4f);
                if (icon.Width > icon.Height)
                {
                    float ratio = icon.Height / (float)icon.Width;
                    drawHeight *= ratio;
                    shadowHeight *= ratio;
                }
                else
                {
                    float ratio = icon.Width / (float)icon.Height;
                    drawWidth *= ratio;
                    shadowWidth *= ratio;
                }

                ImageAnimator.UpdateFrames(shadow);
                if (Settings.IconShadows && shadow != null)
                {
                    g.DrawImage(
                        shadow,
                        7 + (((Settings.IconSize * (5 / 4f)) - shadowWidth) / 2) - 0.7f,
                        ((height - Settings.IconSize) / 2.0f) + (((Settings.IconSize * (5 / 4f)) - shadowHeight) / 2) - 0.7f,
                        shadowWidth,
                        shadowHeight);
                }

                ImageAnimator.UpdateFrames(icon);

                g.DrawImage(
                    icon,
                    7 + ((Settings.IconSize - drawWidth) / 2),
                    ((height - Settings.IconSize) / 2.0f) + ((Settings.IconSize - drawHeight) / 2),
                    drawWidth,
                    drawHeight);
            }

            NameLabel.Font = state.LayoutSettings.TextFont;
            NameLabel.X = 5 + IconWidth;
            NameLabel.HasShadow = state.LayoutSettings.DropShadows;

            if (ColumnsList.Count() == LabelsList.Count)
            {
                while (ColumnWidths.Count < LabelsList.Count)
                {
                    ColumnWidths.Add((0, 0f, 0f));
                }

                float curX = width - 7;
                float nameX = width - 7;
                foreach (SimpleLabel label in LabelsList.Reverse())
                {
                    int i = LabelsList.IndexOf(label);
                    float labelWidth = ColumnWidths[i].width;

                    label.Width = labelWidth + 20;
                    curX -= labelWidth + 5;
                    label.X = curX - 15;

                    label.Font = state.LayoutSettings.TimesFont;
                    label.HasShadow = state.LayoutSettings.DropShadows;
                    label.IsMonospaced = true;
                    label.Draw(g);

                    if (!string.IsNullOrEmpty(label.Text))
                    {
                        nameX = curX + labelWidth + 5 - label.ActualWidth;
                        if (ColumnWidths[i].exWidth < label.ActualWidth)
                        {
                            ColumnWidths[i] = (label.Text.Length, label.ActualWidth, labelWidth);
                        }
                    }
                }

                NameLabel.Width = (mode == LayoutMode.Horizontal ? width - 10 : nameX) - IconWidth;
                NameLabel.Draw(g);
            }
        }
        else
        {
            DisplayIcon = Settings.DisplayIcons;
        }
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        if (Settings.Display2Rows)
        {
            VerticalHeight = Settings.SplitHeight + (0.85f * (g.MeasureString("A", state.LayoutSettings.TimesFont).Height + g.MeasureString("A", state.LayoutSettings.TextFont).Height));
            DrawGeneral(g, state, width, VerticalHeight, LayoutMode.Horizontal);
        }
        else
        {
            VerticalHeight = Settings.SplitHeight + 25;
            DrawGeneral(g, state, width, VerticalHeight, LayoutMode.Vertical);
        }
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        MinimumHeight = 0.85f * (g.MeasureString("A", state.LayoutSettings.TimesFont).Height + g.MeasureString("A", state.LayoutSettings.TextFont).Height);
        DrawGeneral(g, state, HorizontalWidth, height, LayoutMode.Horizontal);
    }

    public string ComponentName => "Split";

    public Control GetSettingsControl(LayoutMode mode)
    {
        throw new NotSupportedException();
    }

    public void SetSettings(System.Xml.XmlNode settings)
    {
        throw new NotSupportedException();
    }

    public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document)
    {
        throw new NotSupportedException();
    }

    public string UpdateName => throw new NotSupportedException();

    public string XMLURL => throw new NotSupportedException();

    public string UpdateURL => throw new NotSupportedException();

    public Version Version => throw new NotSupportedException();

    protected void UpdateAll(LiveSplitState state)
    {
        if (Split != null)
        {
            RecreateLabels();

            // Strip the leading `^` pin marker from the displayed name. The raw name
            // (with `^`) is preserved on the segment so that saves continue to round-trip.
            string displayName = PinnedSegmentParser.Parse(Split.Name).DisplayName;

            if (Settings.AutomaticAbbreviations)
            {
                if (NameLabel.Text != displayName || NameLabel.AlternateText == null || !NameLabel.AlternateText.Any())
                {
                    NameLabel.AlternateText = displayName.GetAbbreviations().ToList();
                }
            }
            else if (NameLabel.AlternateText != null && NameLabel.AlternateText.Any())
            {
                NameLabel.AlternateText.Clear();
            }

            NameLabel.Text = displayName;

            int splitIndex = state.Run.IndexOf(Split);
            // When the user has opted into the pinned color override, every pinned segment uses
            // the single PinnedNamesColor unconditionally (i.e. the pinned override supersedes
            // the regular OverrideTextColor flag and ignores the before/current/after split-state
            // distinction). Non-pinned segments keep the original layout-vs-Override resolution
            // so toggling OverridePinnedColor never affects normal segments.
            if (UsePinnedStyle())
            {
                NameLabel.ForeColor = Settings.PinnedNamesColor;
            }
            else if (splitIndex < state.CurrentSplitIndex)
            {
                NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.BeforeNamesColor : state.LayoutSettings.TextColor;
            }
            else if (Split == state.CurrentSplit)
            {
                NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.CurrentNamesColor : state.LayoutSettings.TextColor;
            }
            else
            {
                NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.AfterNamesColor : state.LayoutSettings.TextColor;
            }

            foreach (SimpleLabel label in LabelsList)
            {
                ColumnData column = ColumnsList.ElementAt(LabelsList.IndexOf(label));
                UpdateColumn(state, label, column);
            }
        }
    }

    protected void UpdateColumn(LiveSplitState state, SimpleLabel label, ColumnData data)
    {
        string comparison = data.Comparison == "Current Comparison" ? state.CurrentComparison : data.Comparison;
        if (!state.Run.Comparisons.Contains(comparison))
        {
            comparison = state.CurrentComparison;
        }

        TimingMethod timingMethod = state.CurrentTimingMethod;
        if (data.TimingMethod == "Real Time")
        {
            timingMethod = TimingMethod.RealTime;
        }
        else if (data.TimingMethod == "Game Time")
        {
            timingMethod = TimingMethod.GameTime;
        }

        ColumnType type = data.Type;

        int splitIndex = state.Run.IndexOf(Split);
        // Pinned color override gate. Same semantics as in UpdateAll: when set, pinned segments
        // use Settings.PinnedTimesColor for any time / segment / custom-variable column and
        // Settings.PinnedDeltasColor for the live-delta indicator, ignoring the before/current/
        // after split-state distinction. For Delta columns, the pace color (returned by
        // GetSplitColor) still wins when non-null — pace signals are intentional indicators and
        // shouldn't be repainted. The pinned override only kicks in on the fallback branches
        // that would otherwise pick a regular TimesColor.
        bool usePinned = UsePinnedStyle();
        if (splitIndex < state.CurrentSplitIndex)
        {
            if (type is ColumnType.SplitTime or ColumnType.SegmentTime or ColumnType.CustomVariable)
            {
                label.ForeColor = usePinned
                    ? Settings.PinnedTimesColor
                    : (Settings.OverrideTimesColor ? Settings.BeforeTimesColor : state.LayoutSettings.TextColor);

                if (type == ColumnType.SplitTime)
                {
                    label.Text = TimeFormatter.Format(Split.SplitTime[timingMethod]);
                }
                else if (type == ColumnType.SegmentTime)
                {
                    TimeSpan? segmentTime = LiveSplitStateHelper.GetPreviousSegmentTime(state, splitIndex, timingMethod);
                    label.Text = TimeFormatter.Format(segmentTime);
                }
                else if (type == ColumnType.CustomVariable)
                {
                    Split.CustomVariableValues.TryGetValue(data.Name, out string text);
                    label.Text = text ?? "";
                }
            }

            if (type is ColumnType.DeltaorSplitTime or ColumnType.Delta)
            {
                TimeSpan? deltaTime = Split.SplitTime[timingMethod] - Split.Comparisons[comparison][timingMethod];
                Color? color = LiveSplitStateHelper.GetSplitColor(state, deltaTime, splitIndex, true, true, comparison, timingMethod);
                if (color == null)
                {
                    color = usePinned
                        ? Settings.PinnedTimesColor
                        : (Settings.OverrideTimesColor ? Settings.BeforeTimesColor : state.LayoutSettings.TextColor);
                }

                label.ForeColor = color.Value;

                if (type == ColumnType.DeltaorSplitTime)
                {
                    if (deltaTime != null)
                    {
                        label.Text = DeltaTimeFormatter.Format(deltaTime);
                    }
                    else
                    {
                        label.Text = TimeFormatter.Format(Split.SplitTime[timingMethod]);
                    }
                }

                else if (type == ColumnType.Delta)
                {
                    label.Text = DeltaTimeFormatter.Format(deltaTime);
                }
            }

            else if (type is ColumnType.SegmentDeltaorSegmentTime or ColumnType.SegmentDelta)
            {
                TimeSpan? segmentDelta = LiveSplitStateHelper.GetPreviousSegmentDelta(state, splitIndex, comparison, timingMethod);
                Color? color = LiveSplitStateHelper.GetSplitColor(state, segmentDelta, splitIndex, false, true, comparison, timingMethod);
                if (color == null)
                {
                    color = usePinned
                        ? Settings.PinnedTimesColor
                        : (Settings.OverrideTimesColor ? Settings.BeforeTimesColor : state.LayoutSettings.TextColor);
                }

                label.ForeColor = color.Value;

                if (type == ColumnType.SegmentDeltaorSegmentTime)
                {
                    if (segmentDelta != null)
                    {
                        label.Text = DeltaTimeFormatter.Format(segmentDelta);
                    }
                    else
                    {
                        label.Text = TimeFormatter.Format(LiveSplitStateHelper.GetPreviousSegmentTime(state, splitIndex, timingMethod));
                    }
                }
                else if (type == ColumnType.SegmentDelta)
                {
                    label.Text = DeltaTimeFormatter.Format(segmentDelta);
                }
            }
        }
        else
        {
            if (type is ColumnType.SplitTime or ColumnType.SegmentTime or ColumnType.DeltaorSplitTime or ColumnType.SegmentDeltaorSegmentTime or ColumnType.CustomVariable)
            {
                if (Split == state.CurrentSplit)
                {
                    label.ForeColor = usePinned
                        ? Settings.PinnedTimesColor
                        : (Settings.OverrideTimesColor ? Settings.CurrentTimesColor : state.LayoutSettings.TextColor);
                }
                else
                {
                    label.ForeColor = usePinned
                        ? Settings.PinnedTimesColor
                        : (Settings.OverrideTimesColor ? Settings.AfterTimesColor : state.LayoutSettings.TextColor);
                }

                if (type is ColumnType.SplitTime or ColumnType.DeltaorSplitTime)
                {
                    label.Text = TimeFormatter.Format(Split.Comparisons[comparison][timingMethod]);
                }
                else if (type is ColumnType.SegmentTime or ColumnType.SegmentDeltaorSegmentTime)
                {
                    TimeSpan previousTime = TimeSpan.Zero;
                    for (int index = splitIndex - 1; index >= 0; index--)
                    {
                        TimeSpan? comparisonTime = state.Run[index].Comparisons[comparison][timingMethod];
                        if (comparisonTime != null)
                        {
                            previousTime = comparisonTime.Value;
                            break;
                        }
                    }

                    label.Text = TimeFormatter.Format(Split.Comparisons[comparison][timingMethod] - previousTime);
                }
                else if (type is ColumnType.CustomVariable)
                {
                    if (splitIndex == state.CurrentSplitIndex)
                    {
                        label.Text = state.Run.Metadata.CustomVariableValue(data.Name) ?? "";
                    }
                    else if (splitIndex > state.CurrentSplitIndex)
                    {
                        label.Text = "";
                    }
                }
            }

            //Live Delta
            bool splitDelta = type is ColumnType.DeltaorSplitTime or ColumnType.Delta;
            TimeSpan? bestDelta = LiveSplitStateHelper.CheckLiveDelta(state, splitDelta, comparison, timingMethod);
            if (bestDelta != null && Split == state.CurrentSplit &&
                (type == ColumnType.DeltaorSplitTime || type == ColumnType.Delta || type == ColumnType.SegmentDeltaorSegmentTime || type == ColumnType.SegmentDelta))
            {
                label.Text = DeltaTimeFormatter.Format(bestDelta);
                label.ForeColor = usePinned
                    ? Settings.PinnedDeltasColor
                    : (Settings.OverrideDeltasColor ? Settings.DeltasColor : state.LayoutSettings.TextColor);
            }
            else if (type is ColumnType.Delta or ColumnType.SegmentDelta)
            {
                label.Text = "";
            }
        }
    }

    protected float CalculateLabelsWidth()
    {
        if (ColumnWidths != null)
        {
            return ColumnWidths.Sum(e => e.width) + (5 * ColumnWidths.Count());
        }

        return 0f;
    }

    protected void RecreateLabels()
    {
        if (ColumnsList != null && LabelsList.Count != ColumnsList.Count())
        {
            LabelsList.Clear();
            foreach (ColumnData column in ColumnsList)
            {
                LabelsList.Add(new SimpleLabel
                {
                    HorizontalAlignment = StringAlignment.Far
                });
            }
        }
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        if (Split != null)
        {
            UpdateAll(state);
            NeedUpdateAll = false;

            IsActive = (state.CurrentPhase == TimerPhase.Running
                        || state.CurrentPhase == TimerPhase.Paused) &&
                                                state.CurrentSplit == Split;

            Cache.Restart();
            Cache["Icon"] = Split.Icon;
            if (Cache.HasChanged)
            {
                if (Split.Icon == null)
                {
                    FrameCount = 0;
                }
                else
                {
                    FrameCount = Split.Icon.GetFrameCount(new FrameDimension(Split.Icon.FrameDimensionsList[0]));
                }
            }

            Cache["DisplayIcon"] = DisplayIcon;
            Cache["SplitName"] = NameLabel.Text;
            Cache["IsActive"] = IsActive;
            Cache["NameColor"] = NameLabel.ForeColor.ToArgb();
            Cache["ColumnsCount"] = ColumnsList.Count();
            for (int index = 0; index < LabelsList.Count; index++)
            {
                SimpleLabel label = LabelsList[index];
                Cache["Columns" + index + "Text"] = label.Text;
                Cache["Columns" + index + "Color"] = label.ForeColor.ToArgb();
                if (index < ColumnWidths.Count)
                {
                    Cache["Columns" + index + "Width"] = ColumnWidths[index].width;
                }
            }

            if (invalidator != null && (Cache.HasChanged || FrameCount > 1))
            {
                invalidator.Invalidate(0, 0, width, height);
            }
        }
    }

    public void Dispose()
    {
    }
}
