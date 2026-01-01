using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PreviewSegment.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        LibraryIds = Array.Empty<Guid>();
    }

    /// <summary>
    /// Gets or sets the library IDs to process.
    /// </summary>
    public Guid[] LibraryIds { get; set; }
}
