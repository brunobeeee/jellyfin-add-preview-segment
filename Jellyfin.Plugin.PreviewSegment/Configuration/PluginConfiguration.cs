using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PreviewSegment.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// The plugin registers an <see cref="MediaBrowser.Controller.MediaSegments.IMediaSegmentProvider"/>,
/// but it deliberately does NOT rely on Jellyfin's native per-library "Media Segment Providers"
/// toggle: on Jellyfin 10.11 that toggle is ineffective, because jellyfin-web stores the provider
/// <em>name</em> in <c>LibraryOptions.DisabledMediaSegmentProviders</c> while the server filters by
/// an MD5 <em>hash</em> of the name — so the two never match and the provider is neither disabled by
/// unchecking it nor off by default. To give real, opt-in per-library control, the plugin keeps its
/// own list of enabled libraries here (empty = disabled everywhere) and checks it in the provider.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the ids (library / virtual-folder <c>ItemId</c>s) of the libraries for which
    /// Preview segments should be generated. Empty means the plugin is disabled for every library.
    /// </summary>
    public string[] EnabledLibraries { get; set; } = Array.Empty<string>();
}
