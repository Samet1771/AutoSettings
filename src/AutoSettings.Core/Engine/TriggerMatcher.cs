using AutoSettings.Core.Catalog;
using AutoSettings.Core.Events;
using AutoSettings.Core.Model;

namespace AutoSettings.Core.Engine;

/// <summary>Decides whether a trigger matches an event.</summary>
public static class TriggerMatcher
{
    /// <summary>
    /// Whether <paramref name="trigger"/> (with defaults applied) matches <paramref name="e"/>.
    /// </summary>
    public static bool Matches(ComponentDescriptor descriptor, ComponentConfig trigger, SystemEvent e, EventFacts facts)
    {
        if (descriptor.EventKind != e.Kind)
            return false;

        var users = trigger.GetStringList("user");
        if (users.Count > 0 && !UserPattern.MatchesAny(users, e.User))
            return false;

        switch (e.Kind)
        {
            case SystemEventKind.Boot:
                if (trigger.GetBoolean("include_fast_startup") == false && e.BootType == BootTypes.FastStartup)
                    return false;
                break;

            case SystemEventKind.Logon:
                var session = trigger.GetString("session") ?? "any";
                if (session == "local" && e.IsRemote)
                    return false;
                if (session == "remote" && !e.IsRemote)
                    return false;
                break;

            case SystemEventKind.AppStarted:
                if (!AppPattern.MatchesAny(trigger.GetStringList("app"), e.Process))
                    return false;
                if ((trigger.GetString("instance") ?? "first") == "first" && !facts.IsFirstInstance)
                    return false;
                break;

            case SystemEventKind.AppClosed:
                if (!AppPattern.MatchesAny(trigger.GetStringList("app"), e.Process))
                    return false;
                if ((trigger.GetString("instance") ?? "last") == "last" && !facts.IsLastInstance)
                    return false;
                break;

            case SystemEventKind.AppFocused:
            case SystemEventKind.AppUnfocused:
                if (!AppPattern.MatchesAny(trigger.GetStringList("app"), e.Process))
                    return false;
                var titles = trigger.GetStringList("title");
                if (titles.Count > 0 && !titles.Any(t => Wildcard.IsMatch(t, e.WindowTitle)))
                    return false;
                break;

            case SystemEventKind.Plugin:
                if (!MatchesPluginEvent(descriptor, trigger, e))
                    return false;
                break;
        }
        return true;
    }

    /// <summary>
    /// A plugin trigger matches when the event has its name and every field set in the trigger matches the
    /// value the plugin sent under the same name: text and lists with wildcards (any list item may match),
    /// other values exactly. Fields left empty match anything.
    /// </summary>
    private static bool MatchesPluginEvent(ComponentDescriptor descriptor, ComponentConfig trigger, SystemEvent e)
    {
        if (!string.Equals(descriptor.PluginEventName, e.PluginEvent, StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var field in descriptor.Fields)
        {
            if (field.Name == "user" || !trigger.Parameters.TryGetValue(field.Name, out var expected) || expected is null)
                continue;
            var actual = e.Data?.GetValueOrDefault(field.Name);
            var matches = expected switch
            {
                string pattern => Wildcard.IsMatch(pattern, actual),
                IEnumerable<string> patterns => patterns.Any(p => Wildcard.IsMatch(p, actual)),
                _ => string.Equals(ValueConverter.ToText(expected), actual, StringComparison.OrdinalIgnoreCase),
            };
            if (!matches)
                return false;
        }
        return true;
    }
}
