using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.PreviewSegment.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreviewSegment.Providers;

/// <summary>
/// A media segment provider that adds a <see cref="MediaSegmentType.Preview"/> segment
/// (from 0 to the start of the intro) for episodes that already have an
/// <see cref="MediaSegmentType.Intro"/> segment.
/// </summary>
/// <remarks>
/// This provider is invoked by Jellyfin's built-in "Extract Media Segments" scheduled task
/// (and on library scans). Jellyfin stores the returned segments under this provider's own,
/// registered provider id, so — unlike the previous direct database writes — the segments are
/// actually surfaced to clients. Enable/disable it per library via the library's
/// "Media Segment Providers" settings.
/// </remarks>
public class PreviewSegmentProvider : IMediaSegmentProvider, IHasOrder
{
    private readonly ILogger<PreviewSegmentProvider> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewSegmentProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="serviceProvider">The service provider, used to resolve the media segment manager lazily.</param>
    public PreviewSegmentProvider(
        ILogger<PreviewSegmentProvider> logger,
        ILibraryManager libraryManager,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name => "Preview Segment";

    /// <summary>
    /// Gets the execution order. A high value makes this provider run after intro-detection
    /// providers, so a freshly created intro is already available within the same extraction pass.
    /// </summary>
    public int Order => 1000;

    /// <inheritdoc />
    public ValueTask<bool> Supports(BaseItem item) => ValueTask.FromResult(item is Episode);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(MediaSegmentGenerationRequest request, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is null)
        {
            return Array.Empty<MediaSegmentDto>();
        }

        // Opt-in per-library gating. Jellyfin's native "Media Segment Providers" toggle is broken on
        // 10.11 (web stores the provider name, the server filters by an MD5 hash of it), so we can
        // neither be off-by-default nor be disabled by unchecking through it. We therefore honour our
        // own EnabledLibraries list instead. Returning an empty result for a non-enabled library also
        // makes the media segment manager delete any previews we created while it was enabled.
        if (!IsLibraryEnabled(item))
        {
            _logger.LogDebug("Preview Segment is not enabled for the library of '{Name}'; skipping", item.Name);
            return Array.Empty<MediaSegmentDto>();
        }

        var libraryOptions = _libraryManager.GetLibraryOptions(item);

        // Resolve the manager lazily: it depends on the set of IMediaSegmentProvider instances,
        // so constructor-injecting it here would create a dependency cycle.
        var segmentManager = _serviceProvider.GetRequiredService<IMediaSegmentManager>();

        // Read intros from ANY provider (e.g. Intro Skipper), not just our own segments.
        var intros = await segmentManager
            .GetSegmentsAsync(item, new[] { MediaSegmentType.Intro }, libraryOptions, filterByProvider: false)
            .ConfigureAwait(false);

        var intro = intros
            .Where(s => s.Type == MediaSegmentType.Intro)
            .OrderBy(s => s.StartTicks)
            .FirstOrDefault();

        // Only add a preview when the intro starts at least 1 second into the episode,
        // otherwise there is no meaningful "before the intro" region to preview.
        if (intro is null || intro.StartTicks < TimeSpan.FromSeconds(1).Ticks)
        {
            _logger.LogDebug("No usable intro segment for '{Name}'; not adding a preview segment", item.Name);
            return Array.Empty<MediaSegmentDto>();
        }

        _logger.LogInformation(
            "Adding preview segment for '{Name}': 0 -> {Seconds:F1}s",
            item.Name,
            TimeSpan.FromTicks(intro.StartTicks).TotalSeconds);

        return new[]
        {
            new MediaSegmentDto
            {
                ItemId = request.ItemId,
                Type = MediaSegmentType.Preview,
                StartTicks = 0,
                EndTicks = intro.StartTicks
            }
        };
    }

    /// <summary>
    /// Determines whether Preview segments are enabled for the library that <paramref name="item"/>
    /// belongs to, based on the plugin's own <see cref="PluginConfiguration.EnabledLibraries"/> list.
    /// Opt-in: an empty list disables the plugin for every library.
    /// </summary>
    private bool IsLibraryEnabled(BaseItem item)
    {
        var enabled = Plugin.Instance?.Configuration.EnabledLibraries;
        if (enabled is null || enabled.Length == 0)
        {
            return false;
        }

        var enabledIds = new HashSet<Guid>();
        foreach (var id in enabled)
        {
            if (Guid.TryParse(id, out var guid))
            {
                enabledIds.Add(guid);
            }
        }

        if (enabledIds.Count == 0)
        {
            return false;
        }

        // GetCollectionFolders returns the library (virtual folder) roots that contain the item;
        // their Id equals the VirtualFolderInfo.ItemId stored by the config page.
        return _libraryManager.GetCollectionFolders(item).Any(folder => enabledIds.Contains(folder.Id));
    }
}
