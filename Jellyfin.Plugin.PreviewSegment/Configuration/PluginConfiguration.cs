using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PreviewSegment.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// This plugin no longer needs any configuration: it registers a media segment provider that
/// Jellyfin runs automatically. Which libraries it applies to is controlled by Jellyfin's own
/// per-library "Media Segment Providers" settings, not by this plugin.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
}
