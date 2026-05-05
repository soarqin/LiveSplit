using System;

using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(PinnedSplitsComponentFactory))]

namespace LiveSplit.UI.Components;

public class PinnedSplitsComponentFactory : IComponentFactory
{
    public string ComponentName => "Pinned Splits";

    public string Description => "A Splits-style list that pins segments prefixed with ^ at the top while still showing the rest of the run below (with the ^ marker stripped from the displayed name).";

    public ComponentCategory Category => ComponentCategory.List;

    public IComponent Create(LiveSplitState state)
    {
        return new SplitsComponent(state);
    }

    public string UpdateName => ComponentName;

    public string XMLURL => "";

    public string UpdateURL => "";

    public Version Version => Version.Parse("1.1.0");
}
