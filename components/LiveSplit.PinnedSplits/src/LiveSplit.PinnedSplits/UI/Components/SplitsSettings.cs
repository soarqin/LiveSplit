using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Localization;
using LiveSplit.Model;
using LiveSplit.TimeFormatters;

namespace LiveSplit.UI.Components;

public partial class SplitsSettings : UserControl
{
    private static string T(string source) => UiLocalizer.Translate(source, LanguageResolver.ResolveCurrentCultureLanguage());

    private int _VisualSplitCount { get; set; }
    public int VisualSplitCount
    {
        get => _VisualSplitCount;
        set
        {
            _VisualSplitCount = value;
            int max = Math.Max(0, _VisualSplitCount - (AlwaysShowLastSplit ? 2 : 1));
            if (dmnUpcomingSegments.Value > max)
            {
                dmnUpcomingSegments.Value = max;
            }

            dmnUpcomingSegments.Maximum = max;
        }
    }
    public Color CurrentSplitTopColor { get; set; }
    public Color CurrentSplitBottomColor { get; set; }
    public int SplitPreviewCount { get; set; }
    public float SplitWidth { get; set; }
    public float SplitHeight { get; set; }
    public float ScaledSplitHeight { get => SplitHeight * 10f; set => SplitHeight = value / 10f; }
    public float IconSize { get; set; }

    public bool Display2Rows { get; set; }

    public Color BackgroundColor { get; set; }
    public Color BackgroundColor2 { get; set; }

    public ExtendedGradientType BackgroundGradient { get; set; }
    public string GradientString
    {
        get => BackgroundGradient.ToString();
        set => BackgroundGradient = (ExtendedGradientType)Enum.Parse(typeof(ExtendedGradientType), value);
    }

    public LiveSplitState CurrentState { get; set; }

    public bool DisplayIcons { get; set; }
    public bool IconShadows { get; set; }
    public bool ShowThinSeparators { get; set; }
    public bool AlwaysShowLastSplit { get; set; }
    public bool ShowBlankSplits { get; set; }
    public bool LockLastSplit { get; set; }
    public bool SeparatorLastSplit { get; set; }

    public bool DropDecimals { get; set; }
    public TimeAccuracy DeltasAccuracy { get; set; }

    public bool OverrideDeltasColor { get; set; }
    public Color DeltasColor { get; set; }

    public bool ShowColumnLabels { get; set; }
    public Color LabelsColor { get; set; }

    public bool AutomaticAbbreviations { get; set; }
    public Color BeforeNamesColor { get; set; }
    public Color CurrentNamesColor { get; set; }
    public Color AfterNamesColor { get; set; }
    public bool OverrideTextColor { get; set; }
    public Color BeforeTimesColor { get; set; }
    public Color CurrentTimesColor { get; set; }
    public Color AfterTimesColor { get; set; }
    public bool OverrideTimesColor { get; set; }

    public TimeAccuracy SplitTimesAccuracy { get; set; }
    public GradientType CurrentSplitGradient { get; set; }
    public string SplitGradientString
    {
        get => CurrentSplitGradient.ToString();
        set => CurrentSplitGradient = (GradientType)Enum.Parse(typeof(GradientType), value);
    }

    // Pinned-section knobs.
    public int MaxDisplayed { get; set; }
    public bool IncludeCurrentSplit { get; set; }

    // When true, pinned segments only appear in the top pinned section and are filtered out of
    // the normal splits list (the legacy behavior). When false (default), pinned segments stay
    // visible in the normal list at their original position even after they have been promoted
    // to the pinned section, so the user keeps full context. Untimed pinned segments always
    // appear in the normal list regardless of this flag (they have not been promoted yet).
    public bool HidePinnedFromNormalList { get; set; }

    // Pinned-section render override. When OverridePinnedColor is true, every segment whose name
    // is marked pinned (via the `^` prefix) renders its name / time / delta labels with the three
    // Pinned*Color values below, regardless of which area it appears in (top pinned section, or
    // normal splits area when HidePinnedFromNormalList is off / when the segment hasn't been
    // promoted yet). The CurrentSplit background gradient is intentionally NOT pinned-overridden
    // because "pinned" and "current split" are orthogonal concepts.
    public bool OverridePinnedColor { get; set; }
    public Color PinnedNamesColor { get; set; }
    public Color PinnedTimesColor { get; set; }
    public Color PinnedDeltasColor { get; set; }

    public event EventHandler SplitLayoutChanged;

    public LayoutMode Mode { get; set; }

    public IList<ColumnSettings> ColumnsList { get; set; }
    public Size StartingSize { get; set; }
    public Size StartingTableLayoutSize { get; set; }
    public int StartingColumnSettingHeight { get; set; }

    public SplitsSettings(LiveSplitState state)
    {
        InitializeComponent();

        CurrentState = state;

        StartingSize = Size;
        StartingTableLayoutSize = tableColumns.Size;

        VisualSplitCount = 8;
        SplitPreviewCount = 1;
        DisplayIcons = true;
        IconShadows = true;
        ShowThinSeparators = true;
        AlwaysShowLastSplit = true;
        ShowBlankSplits = true;
        LockLastSplit = true;
        SeparatorLastSplit = true;
        SplitTimesAccuracy = TimeAccuracy.Seconds;
        CurrentSplitTopColor = Color.FromArgb(51, 115, 244);
        CurrentSplitBottomColor = Color.FromArgb(21, 53, 116);
        SplitWidth = 20;
        SplitHeight = 3.6f;
        IconSize = 24f;
        AutomaticAbbreviations = false;
        BeforeNamesColor = Color.FromArgb(255, 255, 255);
        CurrentNamesColor = Color.FromArgb(255, 255, 255);
        AfterNamesColor = Color.FromArgb(255, 255, 255);
        OverrideTextColor = false;
        BeforeTimesColor = Color.FromArgb(255, 255, 255);
        CurrentTimesColor = Color.FromArgb(255, 255, 255);
        AfterTimesColor = Color.FromArgb(255, 255, 255);
        OverrideTimesColor = false;
        CurrentSplitGradient = GradientType.Vertical;
        cmbSplitGradient.SelectedIndexChanged += cmbSplitGradient_SelectedIndexChanged;
        BackgroundColor = Color.Transparent;
        BackgroundColor2 = Color.FromArgb(1, 255, 255, 255);
        BackgroundGradient = ExtendedGradientType.Alternating;
        DropDecimals = true;
        DeltasAccuracy = TimeAccuracy.Tenths;
        OverrideDeltasColor = false;
        DeltasColor = Color.FromArgb(255, 255, 255);
        Display2Rows = false;
        ShowColumnLabels = false;
        LabelsColor = Color.FromArgb(255, 255, 255);
        MaxDisplayed = 5;
        IncludeCurrentSplit = false;
        HidePinnedFromNormalList = false;

        // Defaults intentionally match the layout TextColor (white): with OverridePinnedColor
        // off (the default), pinned segments resolve through the regular Settings.* fields, so
        // these values only matter once the user opts in. Defaulting to white means toggling
        // the override on a freshly-loaded layout produces no visible change until the user
        // actually picks a different color.
        OverridePinnedColor = false;
        PinnedNamesColor = Color.FromArgb(255, 255, 255);
        PinnedTimesColor = Color.FromArgb(255, 255, 255);
        PinnedDeltasColor = Color.FromArgb(255, 255, 255);

        dmnTotalSegments.DataBindings.Add("Value", this, "VisualSplitCount", false, DataSourceUpdateMode.OnPropertyChanged);
        dmnUpcomingSegments.DataBindings.Add("Value", this, "SplitPreviewCount", false, DataSourceUpdateMode.OnPropertyChanged);
        btnTopColor.DataBindings.Add("BackColor", this, "CurrentSplitTopColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBottomColor.DataBindings.Add("BackColor", this, "CurrentSplitBottomColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkAutomaticAbbreviations.DataBindings.Add("Checked", this, "AutomaticAbbreviations", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBeforeNamesColor.DataBindings.Add("BackColor", this, "BeforeNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentNamesColor.DataBindings.Add("BackColor", this, "CurrentNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAfterNamesColor.DataBindings.Add("BackColor", this, "AfterNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnBeforeTimesColor.DataBindings.Add("BackColor", this, "BeforeTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnCurrentTimesColor.DataBindings.Add("BackColor", this, "CurrentTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnAfterTimesColor.DataBindings.Add("BackColor", this, "AfterTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkDisplayIcons.DataBindings.Add("Checked", this, "DisplayIcons", false, DataSourceUpdateMode.OnPropertyChanged);
        chkIconShadows.DataBindings.Add("Checked", this, "IconShadows", false, DataSourceUpdateMode.OnPropertyChanged);
        chkThinSeparators.DataBindings.Add("Checked", this, "ShowThinSeparators", false, DataSourceUpdateMode.OnPropertyChanged);
        chkLastSplit.DataBindings.Add("Checked", this, "AlwaysShowLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideTextColor.DataBindings.Add("Checked", this, "OverrideTextColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideTimesColor.DataBindings.Add("Checked", this, "OverrideTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        chkShowBlankSplits.DataBindings.Add("Checked", this, "ShowBlankSplits", false, DataSourceUpdateMode.OnPropertyChanged);
        chkLockLastSplit.DataBindings.Add("Checked", this, "LockLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkSeparatorLastSplit.DataBindings.Add("Checked", this, "SeparatorLastSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkDropDecimals.DataBindings.Add("Checked", this, "DropDecimals", false, DataSourceUpdateMode.OnPropertyChanged);
        chkOverrideDeltaColor.DataBindings.Add("Checked", this, "OverrideDeltasColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnDeltaColor.DataBindings.Add("BackColor", this, "DeltasColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnLabelColor.DataBindings.Add("BackColor", this, "LabelsColor", false, DataSourceUpdateMode.OnPropertyChanged);
        trkIconSize.DataBindings.Add("Value", this, "IconSize", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbSplitGradient.DataBindings.Add("SelectedItem", this, "SplitGradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        cmbGradientType.DataBindings.Add("SelectedItem", this, "GradientString", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor1.DataBindings.Add("BackColor", this, "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnColor2.DataBindings.Add("BackColor", this, "BackgroundColor2", false, DataSourceUpdateMode.OnPropertyChanged);
        dmnMaxDisplayed.DataBindings.Add("Value", this, "MaxDisplayed", false, DataSourceUpdateMode.OnPropertyChanged);
        chkIncludeCurrentSplit.DataBindings.Add("Checked", this, "IncludeCurrentSplit", false, DataSourceUpdateMode.OnPropertyChanged);
        chkHidePinnedFromNormalList.DataBindings.Add("Checked", this, "HidePinnedFromNormalList", false, DataSourceUpdateMode.OnPropertyChanged);

        chkOverridePinnedColor.DataBindings.Add("Checked", this, "OverridePinnedColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnPinnedNamesColor.DataBindings.Add("BackColor", this, "PinnedNamesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnPinnedTimesColor.DataBindings.Add("BackColor", this, "PinnedTimesColor", false, DataSourceUpdateMode.OnPropertyChanged);
        btnPinnedDeltasColor.DataBindings.Add("BackColor", this, "PinnedDeltasColor", false, DataSourceUpdateMode.OnPropertyChanged);

        ColumnsList = [];
        ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.Delta, "Current Comparison", "Current Timing Method") });
        ColumnsList.Add(new ColumnSettings(CurrentState, T("Time"), ColumnsList) { Data = new ColumnData(T("Time"), ColumnType.SplitTime, "Current Comparison", "Current Timing Method") });

        StartingColumnSettingHeight = ColumnsList[0].Height;
    }

    private void chkColumnLabels_CheckedChanged(object sender, EventArgs e)
    {
        btnLabelColor.Enabled = lblLabelsColor.Enabled = chkColumnLabels.Checked;
    }

    private void chkDisplayIcons_CheckedChanged(object sender, EventArgs e)
    {
        trkIconSize.Enabled = label5.Enabled = chkIconShadows.Enabled = chkDisplayIcons.Checked;
    }

    private void chkOverrideTimesColor_CheckedChanged(object sender, EventArgs e)
    {
        label6.Enabled = label9.Enabled = label7.Enabled = btnBeforeTimesColor.Enabled
            = btnCurrentTimesColor.Enabled = btnAfterTimesColor.Enabled = chkOverrideTimesColor.Checked;
    }

    private void chkOverrideDeltaColor_CheckedChanged(object sender, EventArgs e)
    {
        label8.Enabled = btnDeltaColor.Enabled = chkOverrideDeltaColor.Checked;
    }

    private void chkOverrideTextColor_CheckedChanged(object sender, EventArgs e)
    {
        label3.Enabled = label10.Enabled = label13.Enabled = btnBeforeNamesColor.Enabled
        = btnCurrentNamesColor.Enabled = btnAfterNamesColor.Enabled = chkOverrideTextColor.Checked;
    }

    private void chkOverridePinnedColor_CheckedChanged(object sender, EventArgs e)
    {
        // Toggle the three pinned color buttons in lockstep with the master switch. Same UX
        // as chkOverride*Color elsewhere on the form: the values stay bound but become
        // non-interactive while the override is off.
        bool enabled = chkOverridePinnedColor.Checked;
        lblPinnedNames.Enabled = enabled;
        lblPinnedTimes.Enabled = enabled;
        lblPinnedDeltas.Enabled = enabled;
        btnPinnedNamesColor.Enabled = enabled;
        btnPinnedTimesColor.Enabled = enabled;
        btnPinnedDeltasColor.Enabled = enabled;
    }

    private void rdoDeltaHundredths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateDeltaAccuracy();
    }

    private void rdoDeltaTenths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateDeltaAccuracy();
    }

    private void rdoDeltaSeconds_CheckedChanged(object sender, EventArgs e)
    {
        UpdateDeltaAccuracy();
    }

    private void chkSeparatorLastSplit_CheckedChanged(object sender, EventArgs e)
    {
        SeparatorLastSplit = chkSeparatorLastSplit.Checked;
        SplitLayoutChanged(this, null);
    }

    private void cmbGradientType_SelectedIndexChanged(object sender, EventArgs e)
    {
        btnColor1.Visible = cmbGradientType.SelectedItem.ToString() != "Plain";
        btnColor2.DataBindings.Clear();
        btnColor2.DataBindings.Add("BackColor", this, btnColor1.Visible ? "BackgroundColor2" : "BackgroundColor", false, DataSourceUpdateMode.OnPropertyChanged);
        GradientString = cmbGradientType.SelectedItem.ToString();
    }

    private void cmbSplitGradient_SelectedIndexChanged(object sender, EventArgs e)
    {
        btnTopColor.Visible = cmbSplitGradient.SelectedItem.ToString() != "Plain";
        btnBottomColor.DataBindings.Clear();
        btnBottomColor.DataBindings.Add("BackColor", this, btnTopColor.Visible ? "CurrentSplitBottomColor" : "CurrentSplitTopCOlor", false, DataSourceUpdateMode.OnPropertyChanged);
        SplitGradientString = cmbSplitGradient.SelectedItem.ToString();
    }

    private void chkLockLastSplit_CheckedChanged(object sender, EventArgs e)
    {
        LockLastSplit = chkLockLastSplit.Checked;
        SplitLayoutChanged(this, null);
    }

    private void chkShowBlankSplits_CheckedChanged(object sender, EventArgs e)
    {
        ShowBlankSplits = chkLockLastSplit.Enabled = chkShowBlankSplits.Checked;
        SplitLayoutChanged(this, null);
    }

    private void rdoHundredths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateAccuracy();
    }

    private void rdoTenths_CheckedChanged(object sender, EventArgs e)
    {
        UpdateAccuracy();
    }

    private void rdoSeconds_CheckedChanged(object sender, EventArgs e)
    {
        UpdateAccuracy();
    }

    private void UpdateAccuracy()
    {
        if (rdoSeconds.Checked)
        {
            SplitTimesAccuracy = TimeAccuracy.Seconds;
        }
        else if (rdoTenths.Checked)
        {
            SplitTimesAccuracy = TimeAccuracy.Tenths;
        }
        else if (rdoHundredths.Checked)
        {
            SplitTimesAccuracy = TimeAccuracy.Hundredths;
        }
        else
        {
            SplitTimesAccuracy = TimeAccuracy.Milliseconds;
        }
    }

    private void UpdateDeltaAccuracy()
    {
        if (rdoDeltaSeconds.Checked)
        {
            DeltasAccuracy = TimeAccuracy.Seconds;
        }
        else if (rdoDeltaTenths.Checked)
        {
            DeltasAccuracy = TimeAccuracy.Tenths;
        }
        else if (rdoDeltaHundredths.Checked)
        {
            DeltasAccuracy = TimeAccuracy.Hundredths;
        }
        else
        {
            DeltasAccuracy = TimeAccuracy.Milliseconds;
        }
    }

    private void chkLastSplit_CheckedChanged(object sender, EventArgs e)
    {
        AlwaysShowLastSplit = chkLastSplit.Checked;
        VisualSplitCount = VisualSplitCount;
        SplitLayoutChanged(this, null);
    }

    private void chkThinSeparators_CheckedChanged(object sender, EventArgs e)
    {
        ShowThinSeparators = chkThinSeparators.Checked;
        SplitLayoutChanged(this, null);
    }

    private void SplitsSettings_Load(object sender, EventArgs e)
    {
        ResetColumns();

        chkOverrideDeltaColor_CheckedChanged(null, null);
        chkOverrideTextColor_CheckedChanged(null, null);
        chkOverrideTimesColor_CheckedChanged(null, null);
        chkOverridePinnedColor_CheckedChanged(null, null);
        chkColumnLabels_CheckedChanged(null, null);
        chkDisplayIcons_CheckedChanged(null, null);
        chkLockLastSplit.Enabled = chkShowBlankSplits.Checked;

        rdoSeconds.Checked = SplitTimesAccuracy == TimeAccuracy.Seconds;
        rdoTenths.Checked = SplitTimesAccuracy == TimeAccuracy.Tenths;
        rdoHundredths.Checked = SplitTimesAccuracy == TimeAccuracy.Hundredths;
        rdoMilliseconds.Checked = SplitTimesAccuracy == TimeAccuracy.Milliseconds;

        rdoDeltaSeconds.Checked = DeltasAccuracy == TimeAccuracy.Seconds;
        rdoDeltaTenths.Checked = DeltasAccuracy == TimeAccuracy.Tenths;
        rdoDeltaHundredths.Checked = DeltasAccuracy == TimeAccuracy.Hundredths;
        rdoDeltaMilliseconds.Checked = DeltasAccuracy == TimeAccuracy.Milliseconds;

        if (Mode == LayoutMode.Horizontal)
        {
            trkSize.DataBindings.Clear();
            trkSize.Minimum = 5;
            trkSize.Maximum = 120;
            SplitWidth = Math.Min(Math.Max(trkSize.Minimum, SplitWidth), trkSize.Maximum);
            trkSize.DataBindings.Add("Value", this, "SplitWidth", false, DataSourceUpdateMode.OnPropertyChanged);
            lblSplitSize.Text = T("Split Width:");
            chkDisplayRows.Enabled = false;
            chkDisplayRows.DataBindings.Clear();
            chkDisplayRows.Checked = true;
            chkColumnLabels.DataBindings.Clear();
            chkColumnLabels.Enabled = chkColumnLabels.Checked = false;
        }
        else
        {
            trkSize.DataBindings.Clear();
            trkSize.Minimum = 0;
            trkSize.Maximum = 250;
            ScaledSplitHeight = Math.Min(Math.Max(trkSize.Minimum, ScaledSplitHeight), trkSize.Maximum);
            trkSize.DataBindings.Add("Value", this, "ScaledSplitHeight", false, DataSourceUpdateMode.OnPropertyChanged);
            lblSplitSize.Text = T("Split Height:");
            chkDisplayRows.Enabled = true;
            chkDisplayRows.DataBindings.Clear();
            chkDisplayRows.DataBindings.Add("Checked", this, "Display2Rows", false, DataSourceUpdateMode.OnPropertyChanged);
            chkColumnLabels.DataBindings.Clear();
            chkColumnLabels.Enabled = true;
            chkColumnLabels.DataBindings.Add("Checked", this, "ShowColumnLabels", false, DataSourceUpdateMode.OnPropertyChanged);
        }
    }

    public void SetSettings(XmlNode node)
    {
        var element = (XmlElement)node;
        Version version = SettingsHelper.ParseVersion(element["Version"]);

        CurrentSplitTopColor = SettingsHelper.ParseColor(element["CurrentSplitTopColor"]);
        CurrentSplitBottomColor = SettingsHelper.ParseColor(element["CurrentSplitBottomColor"]);
        VisualSplitCount = SettingsHelper.ParseInt(element["VisualSplitCount"]);
        SplitPreviewCount = SettingsHelper.ParseInt(element["SplitPreviewCount"]);
        DisplayIcons = SettingsHelper.ParseBool(element["DisplayIcons"]);
        ShowThinSeparators = SettingsHelper.ParseBool(element["ShowThinSeparators"]);
        AlwaysShowLastSplit = SettingsHelper.ParseBool(element["AlwaysShowLastSplit"]);
        SplitWidth = SettingsHelper.ParseFloat(element["SplitWidth"]);
        AutomaticAbbreviations = SettingsHelper.ParseBool(element["AutomaticAbbreviations"], false);
        ShowColumnLabels = SettingsHelper.ParseBool(element["ShowColumnLabels"], false);
        LabelsColor = SettingsHelper.ParseColor(element["LabelsColor"], Color.FromArgb(255, 255, 255));
        OverrideTimesColor = SettingsHelper.ParseBool(element["OverrideTimesColor"], false);
        BeforeTimesColor = SettingsHelper.ParseColor(element["BeforeTimesColor"], Color.FromArgb(255, 255, 255));
        CurrentTimesColor = SettingsHelper.ParseColor(element["CurrentTimesColor"], Color.FromArgb(255, 255, 255));
        AfterTimesColor = SettingsHelper.ParseColor(element["AfterTimesColor"], Color.FromArgb(255, 255, 255));
        SplitHeight = SettingsHelper.ParseFloat(element["SplitHeight"], 6);
        SplitGradientString = SettingsHelper.ParseString(element["CurrentSplitGradient"], GradientType.Vertical.ToString());
        BackgroundColor = SettingsHelper.ParseColor(element["BackgroundColor"], Color.Transparent);
        BackgroundColor2 = SettingsHelper.ParseColor(element["BackgroundColor2"], Color.Transparent);
        GradientString = SettingsHelper.ParseString(element["BackgroundGradient"], ExtendedGradientType.Plain.ToString());
        SeparatorLastSplit = SettingsHelper.ParseBool(element["SeparatorLastSplit"], true);
        DropDecimals = SettingsHelper.ParseBool(element["DropDecimals"], true);
        DeltasAccuracy = SettingsHelper.ParseEnum(element["DeltasAccuracy"], TimeAccuracy.Tenths);
        OverrideDeltasColor = SettingsHelper.ParseBool(element["OverrideDeltasColor"], false);
        DeltasColor = SettingsHelper.ParseColor(element["DeltasColor"], Color.FromArgb(255, 255, 255));
        Display2Rows = SettingsHelper.ParseBool(element["Display2Rows"], false);
        SplitTimesAccuracy = SettingsHelper.ParseEnum(element["SplitTimesAccuracy"], TimeAccuracy.Seconds);
        ShowBlankSplits = SettingsHelper.ParseBool(element["ShowBlankSplits"], true);
        LockLastSplit = SettingsHelper.ParseBool(element["LockLastSplit"], false);
        IconSize = SettingsHelper.ParseFloat(element["IconSize"], 24f);
        IconShadows = SettingsHelper.ParseBool(element["IconShadows"], true);
        MaxDisplayed = SettingsHelper.ParseInt(element["MaxDisplayed"], 5);
        IncludeCurrentSplit = SettingsHelper.ParseBool(element["IncludeCurrentSplit"], false);
        HidePinnedFromNormalList = SettingsHelper.ParseBool(element["HidePinnedFromNormalList"], false);

        // Pinned color override (introduced in version 1.8 — replaces the 1.7-era
        // OverridePinnedStyle + 11 Pinned*Color/Gradient fields with a single 3-color palette).
        // Older layouts without these elements default to OverridePinnedColor=false plus white
        // colors that match the layout TextColor, so the saved layout renders identically
        // before and after the upgrade. Pre-1.8 Pinned* elements are intentionally NOT read:
        // the 1.7 schema was unreleased / experimental and has been wholesale replaced.
        OverridePinnedColor = SettingsHelper.ParseBool(element["OverridePinnedColor"], false);
        PinnedNamesColor = SettingsHelper.ParseColor(element["PinnedNamesColor"], Color.FromArgb(255, 255, 255));
        PinnedTimesColor = SettingsHelper.ParseColor(element["PinnedTimesColor"], Color.FromArgb(255, 255, 255));
        PinnedDeltasColor = SettingsHelper.ParseColor(element["PinnedDeltasColor"], Color.FromArgb(255, 255, 255));

        if (version >= new Version(1, 5))
        {
            XmlElement columnsElement = element["Columns"];
            ColumnsList.Clear();
            foreach (object child in columnsElement.ChildNodes)
            {
                var columnData = ColumnData.FromXml((XmlNode)child);
                ColumnsList.Add(new ColumnSettings(CurrentState, columnData.Name, ColumnsList) { Data = columnData });
            }
        }
        else
        {
            ColumnsList.Clear();
            string comparison = SettingsHelper.ParseString(element["Comparison"]);
            if (SettingsHelper.ParseBool(element["ShowSplitTimes"]))
            {
                ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.Delta, comparison, "Current Timing Method") });
                ColumnsList.Add(new ColumnSettings(CurrentState, "Time", ColumnsList) { Data = new ColumnData("Time", ColumnType.SplitTime, comparison, "Current Timing Method") });
            }
            else
            {
                ColumnsList.Add(new ColumnSettings(CurrentState, "+/-", ColumnsList) { Data = new ColumnData("+/-", ColumnType.DeltaorSplitTime, comparison, "Current Timing Method") });
            }
        }

        if (version >= new Version(1, 3))
        {
            BeforeNamesColor = SettingsHelper.ParseColor(element["BeforeNamesColor"]);
            CurrentNamesColor = SettingsHelper.ParseColor(element["CurrentNamesColor"]);
            AfterNamesColor = SettingsHelper.ParseColor(element["AfterNamesColor"]);
            OverrideTextColor = SettingsHelper.ParseBool(element["OverrideTextColor"]);
        }
        else
        {
            if (version >= new Version(1, 2))
            {
                BeforeNamesColor = CurrentNamesColor = AfterNamesColor = SettingsHelper.ParseColor(element["SplitNamesColor"]);
            }
            else
            {
                BeforeNamesColor = Color.FromArgb(255, 255, 255);
                CurrentNamesColor = Color.FromArgb(255, 255, 255);
                AfterNamesColor = Color.FromArgb(255, 255, 255);
            }

            OverrideTextColor = !SettingsHelper.ParseBool(element["UseTextColor"], true);
        }
    }

    public XmlNode GetSettings(XmlDocument document)
    {
        XmlElement parent = document.CreateElement("Settings");
        CreateSettingsNode(document, parent);
        return parent;
    }

    public int GetSettingsHashCode()
    {
        return CreateSettingsNode(null, null);
    }

    private int CreateSettingsNode(XmlDocument document, XmlElement parent)
    {
        int hashCode = SettingsHelper.CreateSetting(document, parent, "Version", "1.8") ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitTopColor", CurrentSplitTopColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitBottomColor", CurrentSplitBottomColor) ^
        SettingsHelper.CreateSetting(document, parent, "VisualSplitCount", VisualSplitCount) ^
        SettingsHelper.CreateSetting(document, parent, "SplitPreviewCount", SplitPreviewCount) ^
        SettingsHelper.CreateSetting(document, parent, "DisplayIcons", DisplayIcons) ^
        SettingsHelper.CreateSetting(document, parent, "ShowThinSeparators", ShowThinSeparators) ^
        SettingsHelper.CreateSetting(document, parent, "AlwaysShowLastSplit", AlwaysShowLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "SplitWidth", SplitWidth) ^
        SettingsHelper.CreateSetting(document, parent, "SplitTimesAccuracy", SplitTimesAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "AutomaticAbbreviations", AutomaticAbbreviations) ^
        SettingsHelper.CreateSetting(document, parent, "BeforeNamesColor", BeforeNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentNamesColor", CurrentNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "AfterNamesColor", AfterNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTextColor", OverrideTextColor) ^
        SettingsHelper.CreateSetting(document, parent, "BeforeTimesColor", BeforeTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentTimesColor", CurrentTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "AfterTimesColor", AfterTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideTimesColor", OverrideTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "ShowBlankSplits", ShowBlankSplits) ^
        SettingsHelper.CreateSetting(document, parent, "LockLastSplit", LockLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "IconSize", IconSize) ^
        SettingsHelper.CreateSetting(document, parent, "IconShadows", IconShadows) ^
        SettingsHelper.CreateSetting(document, parent, "SplitHeight", SplitHeight) ^
        SettingsHelper.CreateSetting(document, parent, "CurrentSplitGradient", CurrentSplitGradient) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor", BackgroundColor) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor2", BackgroundColor2) ^
        SettingsHelper.CreateSetting(document, parent, "BackgroundGradient", BackgroundGradient) ^
        SettingsHelper.CreateSetting(document, parent, "SeparatorLastSplit", SeparatorLastSplit) ^
        SettingsHelper.CreateSetting(document, parent, "DeltasAccuracy", DeltasAccuracy) ^
        SettingsHelper.CreateSetting(document, parent, "DropDecimals", DropDecimals) ^
        SettingsHelper.CreateSetting(document, parent, "OverrideDeltasColor", OverrideDeltasColor) ^
        SettingsHelper.CreateSetting(document, parent, "DeltasColor", DeltasColor) ^
        SettingsHelper.CreateSetting(document, parent, "Display2Rows", Display2Rows) ^
        SettingsHelper.CreateSetting(document, parent, "ShowColumnLabels", ShowColumnLabels) ^
        SettingsHelper.CreateSetting(document, parent, "LabelsColor", LabelsColor) ^
        SettingsHelper.CreateSetting(document, parent, "MaxDisplayed", MaxDisplayed) ^
        SettingsHelper.CreateSetting(document, parent, "IncludeCurrentSplit", IncludeCurrentSplit) ^
        SettingsHelper.CreateSetting(document, parent, "HidePinnedFromNormalList", HidePinnedFromNormalList) ^
        SettingsHelper.CreateSetting(document, parent, "OverridePinnedColor", OverridePinnedColor) ^
        SettingsHelper.CreateSetting(document, parent, "PinnedNamesColor", PinnedNamesColor) ^
        SettingsHelper.CreateSetting(document, parent, "PinnedTimesColor", PinnedTimesColor) ^
        SettingsHelper.CreateSetting(document, parent, "PinnedDeltasColor", PinnedDeltasColor);

        XmlElement columnsElement = null;
        if (document != null)
        {
            columnsElement = document.CreateElement("Columns");
            parent.AppendChild(columnsElement);
        }

        int count = 1;
        foreach (ColumnData columnData in ColumnsList.Select(x => x.Data))
        {
            XmlElement settings = null;
            if (document != null)
            {
                settings = document.CreateElement("Settings");
                columnsElement.AppendChild(settings);
            }

            hashCode ^= columnData.CreateElement(document, settings) * count;
            count++;
        }

        return hashCode;
    }

    private void ColorButtonClick(object sender, EventArgs e)
    {
        SettingsHelper.ColorButtonClick((Button)sender, this);
    }

    private void ResetColumns()
    {
        ClearLayout();
        int index = 1;
        foreach (ColumnSettings column in ColumnsList)
        {
            UpdateLayoutForColumn();
            AddColumnToLayout(column, index);
            column.UpdateEnabledButtons();
            index++;
        }
    }

    private void AddColumnToLayout(ColumnSettings column, int index)
    {
        tableColumns.Controls.Add(column, 0, index);
        tableColumns.SetColumnSpan(column, 4);
        column.ColumnRemoved -= column_ColumnRemoved;
        column.MovedUp -= column_MovedUp;
        column.MovedDown -= column_MovedDown;
        column.ColumnRemoved += column_ColumnRemoved;
        column.MovedUp += column_MovedUp;
        column.MovedDown += column_MovedDown;
    }

    private void column_MovedDown(object sender, EventArgs e)
    {
        var column = (ColumnSettings)sender;
        int index = ColumnsList.IndexOf(column);
        ColumnsList.Remove(column);
        ColumnsList.Insert(index + 1, column);
        ResetColumns();
        column.SelectControl();
    }

    private void column_MovedUp(object sender, EventArgs e)
    {
        var column = (ColumnSettings)sender;
        int index = ColumnsList.IndexOf(column);
        ColumnsList.Remove(column);
        ColumnsList.Insert(index - 1, column);
        ResetColumns();
        column.SelectControl();
    }

    private void column_ColumnRemoved(object sender, EventArgs e)
    {
        var column = (ColumnSettings)sender;
        int index = ColumnsList.IndexOf(column);
        ColumnsList.Remove(column);
        ResetColumns();
        if (ColumnsList.Count > 0)
        {
            ColumnsList.Last().SelectControl();
        }
        else
        {
            chkColumnLabels.Select();
        }
    }

    private void ClearLayout()
    {
        tableColumns.RowCount = 1;
        tableColumns.RowStyles.Clear();
        tableColumns.RowStyles.Add(new RowStyle(SizeType.Absolute, StartingTableLayoutSize.Height));
        tableColumns.Size = StartingTableLayoutSize;
        foreach (ColumnSettings control in tableColumns.Controls.OfType<ColumnSettings>().ToList())
        {
            tableColumns.Controls.Remove(control);
        }

        Size = StartingSize;
    }

    private void UpdateLayoutForColumn()
    {
        tableColumns.RowCount++;
        tableColumns.RowStyles.Add(new RowStyle(SizeType.Absolute, StartingColumnSettingHeight));
        tableColumns.Size = new Size(tableColumns.Size.Width, tableColumns.Size.Height + StartingColumnSettingHeight);
        Size = new Size(Size.Width, Size.Height + StartingColumnSettingHeight);
        groupColumns.Size = new Size(groupColumns.Size.Width, groupColumns.Size.Height + StartingColumnSettingHeight);
    }

    private void btnAddColumn_Click(object sender, EventArgs e)
    {
        UpdateLayoutForColumn();

        var columnControl = new ColumnSettings(CurrentState, "", ColumnsList);
        ColumnsList.Add(columnControl);
        AddColumnToLayout(columnControl, ColumnsList.Count);

        foreach (ColumnSettings column in ColumnsList)
        {
            column.UpdateEnabledButtons();
        }
    }
}
