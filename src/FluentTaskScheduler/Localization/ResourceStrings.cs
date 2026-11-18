using System.Collections.Concurrent;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FluentTaskScheduler.Localization;

/// <summary>
/// The app's strings (Strings/&lt;language&gt;/Resources.resw), read in one language at a time.
/// This is what <see cref="Loc"/> looks text up in while the app runs.
/// </summary>
public sealed class ResourceStrings
{
    private readonly ResourceManager _manager = new();
    private readonly ResourceMap _strings;
    private volatile Language _language;

    public ResourceStrings(string language)
    {
        _strings = _manager.MainResourceMap.GetSubtree("Resources");
        _language = Open(language);
    }

    public void UseLanguage(string language) => _language = Open(language);

    /// <summary>The text for a resource name such as "Dialog.Common.OK", or null if there is none.</summary>
    public string? Find(string key)
    {
        Language language = _language;
        // The compiled resource index stores a dotted name as a path.
        return language.Found.GetOrAdd(key, name => _strings.TryGetValue(name.Replace('.', '/'), language.Context)?.ValueAsString);
    }

    private Language Open(string language)
    {
        ResourceContext context = _manager.CreateResourceContext();
        context.QualifierValues["Language"] = language;
        return new Language(context, new ConcurrentDictionary<string, string?>());
    }

    private sealed record Language(ResourceContext Context, ConcurrentDictionary<string, string?> Found);
}
