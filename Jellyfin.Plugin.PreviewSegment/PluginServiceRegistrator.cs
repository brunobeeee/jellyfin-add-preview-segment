using Jellyfin.Plugin.PreviewSegment.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PreviewSegment;

/// <summary>
/// Registers the plugin's services with Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Registering the provider is what makes Jellyfin discover it, run it via the built-in
        // "Extract Media Segments" task, and surface its segments to clients.
        serviceCollection.AddSingleton<IMediaSegmentProvider, PreviewSegmentProvider>();
    }
}
