using System.Collections.Generic;
using System.Drawing;
using System.Xml;

using Xunit;

using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;

namespace LiveSplit.PinnedSplits.Tests;

public class PinnedSplitsSettingsMust
{
    // SplitsSettings requires a LiveSplitState to construct because it bootstraps default
    // ColumnSettings entries (which subscribe to state events). We build a minimal state
    // sufficient for serialization round-trip tests.
    private static LiveSplitState BuildMinimalState()
    {
        var run = new Run(new StandardComparisonGeneratorsFactory()) { new Segment("Dummy") };
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
        return new LiveSplitState(run, null, layout, layoutSettings, settings);
    }

    [Fact]
    public void RoundtripPreservesValues()
    {
        var state = BuildMinimalState();
        var sut = new SplitsSettings(state)
        {
            MaxDisplayed = 7,
            IncludeCurrentSplit = true,
            HidePinnedFromNormalList = true,
        };

        var doc = new XmlDocument();
        XmlNode node = sut.GetSettings(doc);

        var restored = new SplitsSettings(state);
        restored.SetSettings(node);

        Assert.Equal(7, restored.MaxDisplayed);
        Assert.True(restored.IncludeCurrentSplit);
        Assert.True(restored.HidePinnedFromNormalList);
    }

    [Fact]
    public void MissingFieldsDefaultCorrectly()
    {
        var state = BuildMinimalState();
        var sut = new SplitsSettings(state);
        var doc = new XmlDocument();
        doc.LoadXml("<Settings><Version>1.0</Version></Settings>");

        sut.SetSettings(doc.DocumentElement);

        Assert.Equal(5, sut.MaxDisplayed);
        Assert.False(sut.IncludeCurrentSplit);
        // Default keeps pinned segments in the normal list — they show simultaneously at the
        // top (pinned section) and at their original position (normal list). The user has to
        // explicitly opt into the legacy "filter pinned out of the normal list" behavior.
        Assert.False(sut.HidePinnedFromNormalList);
    }

    [Fact]
    public void HashCodeChangesWhenValuesChange()
    {
        var state = BuildMinimalState();
        var s1 = new SplitsSettings(state) { MaxDisplayed = 5 };
        var s2 = new SplitsSettings(state) { MaxDisplayed = 10 };

        Assert.NotEqual(s1.GetSettingsHashCode(), s2.GetSettingsHashCode());
    }

    /// <summary>
    /// Locks down XML round-trip for the version 1.8 pinned color override (replaces the
    /// shelved 1.7 schema with a single 3-color palette). Distinct Color values per slot so a
    /// swapped element name (e.g. saving Names into the Times slot) immediately fails an
    /// Assert rather than producing a silent shuffle.
    /// </summary>
    [Fact]
    public void RoundtripPreservesPinnedStyleValues()
    {
        var state = BuildMinimalState();
        var sut = new SplitsSettings(state)
        {
            OverridePinnedColor = true,
            PinnedNamesColor = Color.FromArgb(255, 0, 0),
            PinnedTimesColor = Color.FromArgb(0, 255, 0),
            PinnedDeltasColor = Color.FromArgb(0, 0, 255),
        };

        var doc = new XmlDocument();
        XmlNode node = sut.GetSettings(doc);

        var restored = new SplitsSettings(state);
        restored.SetSettings(node);

        Assert.True(restored.OverridePinnedColor);
        Assert.Equal(Color.FromArgb(255, 0, 0), restored.PinnedNamesColor);
        Assert.Equal(Color.FromArgb(0, 255, 0), restored.PinnedTimesColor);
        Assert.Equal(Color.FromArgb(0, 0, 255), restored.PinnedDeltasColor);
    }

    /// <summary>
    /// Pre-1.8 layouts (including pre-1.7 stock-Splits layouts) have no
    /// OverridePinnedColor / Pinned*Color elements. SetSettings must hydrate the new fields
    /// with values that visually match the layout TextColor (white) so the on-disk layout
    /// renders the same before and after the upgrade. OverridePinnedColor must default to
    /// false so the behavioral change stays opt-in.
    /// </summary>
    [Fact]
    public void MissingPinnedStyleFieldsDefaultToOptOut()
    {
        var state = BuildMinimalState();
        var sut = new SplitsSettings(state);
        var doc = new XmlDocument();
        doc.LoadXml("<Settings><Version>1.0</Version></Settings>");

        sut.SetSettings(doc.DocumentElement);

        Assert.False(sut.OverridePinnedColor);
        Assert.Equal(Color.FromArgb(255, 255, 255), sut.PinnedNamesColor);
        Assert.Equal(Color.FromArgb(255, 255, 255), sut.PinnedTimesColor);
        Assert.Equal(Color.FromArgb(255, 255, 255), sut.PinnedDeltasColor);
    }

    /// <summary>
    /// Toggling OverridePinnedColor alone must change the hash so LiveSplit's "settings dirty"
    /// detection (which uses GetSettingsHashCode) prompts to save.
    /// </summary>
    [Fact]
    public void HashCodeChanges_WhenOverridePinnedColorToggled()
    {
        var state = BuildMinimalState();
        var s1 = new SplitsSettings(state) { OverridePinnedColor = false };
        var s2 = new SplitsSettings(state) { OverridePinnedColor = true };

        Assert.NotEqual(s1.GetSettingsHashCode(), s2.GetSettingsHashCode());
    }

    /// <summary>
    /// Toggling HidePinnedFromNormalList alone must also dirty the hash, otherwise the user
    /// can flip the option without LiveSplit prompting to save the layout.
    /// </summary>
    [Fact]
    public void HashCodeChanges_WhenHidePinnedFromNormalListToggled()
    {
        var state = BuildMinimalState();
        var s1 = new SplitsSettings(state) { HidePinnedFromNormalList = false };
        var s2 = new SplitsSettings(state) { HidePinnedFromNormalList = true };

        Assert.NotEqual(s1.GetSettingsHashCode(), s2.GetSettingsHashCode());
    }
}
