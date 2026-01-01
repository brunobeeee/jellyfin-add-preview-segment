using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreviewSegment.ScheduledTasks;

/// <summary>
/// Scheduled task to add preview segments to episodes.
/// </summary>
public class AddPreviewSegmentTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<AddPreviewSegmentTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddPreviewSegmentTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="appPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{AddPreviewSegmentTask}"/> interface.</param>
    public AddPreviewSegmentTask(ILibraryManager libraryManager, IApplicationPaths appPaths, ILogger<AddPreviewSegmentTask> logger)
    {
        _libraryManager = libraryManager;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Add Preview Segments";

    /// <inheritdoc />
    public string Key => "AddPreviewSegments";

    /// <inheritdoc />
    public string Description => "Adds preview segments to episodes that have intro segments but no preview segments.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || config.LibraryIds.Length == 0)
        {
            _logger.LogInformation("No libraries configured for preview segment processing");
            return;
        }

        _logger.LogInformation("Starting preview segment processing for {Count} libraries", config.LibraryIds.Length);

        var episodesToProcess = new List<Episode>();

        // Get all episodes from the selected libraries
        foreach (var libraryId in config.LibraryIds)
        {
            var library = _libraryManager.GetItemById(libraryId);
            if (library == null)
            {
                _logger.LogWarning("Library with ID {LibraryId} not found", libraryId);
                continue;
            }

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Parent = library,
                Recursive = true
            };

            var episodes = _libraryManager.GetItemsResult(query).Items.OfType<Episode>();
            episodesToProcess.AddRange(episodes);
        }

        _logger.LogInformation("Found {Count} episodes to check", episodesToProcess.Count);

        if (episodesToProcess.Count == 0)
        {
            _logger.LogInformation("No episodes found to process");
            return;
        }

        var processedCount = 0;
        var addedCount = 0;
        var dbPath = System.IO.Path.Combine(_appPaths.DataPath, "jellyfin.db");

        if (!System.IO.File.Exists(dbPath))
        {
            _logger.LogError("Database file not found at path: {DbPath}", dbPath);
            return;
        }

        _logger.LogInformation("Opening database connection to: {DbPath}", dbPath);

        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Check if the MediaSegments table exists before processing episodes
            var tableExists = await CheckTableExistsAsync(connection, "MediaSegments", cancellationToken).ConfigureAwait(false);
            if (!tableExists)
            {
                _logger.LogWarning("MediaSegments table does not exist in the database. This feature may require Jellyfin 10.10 or later.");
                return;
            }

            _logger.LogInformation("MediaSegments table found, starting to process episodes");

            // Determine GUID format once at the start for efficiency
            var guidFormat = await DetectGuidFormatAsync(connection, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Using GUID format: {Format}", guidFormat);

            foreach (var episode in episodesToProcess)
            {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Query segments for this episode
                var segments = await GetSegmentsAsync(connection, episode.Id, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug(
                    "Episode '{Name}' (S{Season}E{Episode}, ID: {Id}) has {Count} segments: {Types}",
                    episode.Name,
                    episode.ParentIndexNumber,
                    episode.IndexNumber,
                    episode.Id,
                    segments.Count,
                    string.Join(", ", segments.Select(s => $"Type={s.Type} Start={TimeSpan.FromTicks(s.StartTicks).TotalSeconds:F1}s End={TimeSpan.FromTicks(s.EndTicks).TotalSeconds:F1}s")));
                
                // Type values: 5=Intro, 2=Preview
                var hasIntro = segments.Any(s => s.Type == "5");
                var hasPreview = segments.Any(s => s.Type == "2");

                _logger.LogDebug(
                    "Episode '{Name}' - hasIntro: {HasIntro}, hasPreview: {HasPreview}",
                    episode.Name,
                    hasIntro,
                    hasPreview);

                if (hasIntro && !hasPreview)
                {
                    var introSegment = segments.FirstOrDefault(s => s.Type == "5");
                    
                    // Validate intro segment before adding preview
                    if (introSegment != null && introSegment.StartTicks > 0)
                    {
                        // Additional validation: ensure intro starts at least 1 second into the episode
                        if (introSegment.StartTicks >= TimeSpan.FromSeconds(1).Ticks)
                        {
                            await AddSegmentAsync(connection, episode.Id, "2", 0, introSegment.StartTicks, guidFormat, cancellationToken).ConfigureAwait(false);
                            addedCount++;
                            _logger.LogInformation(
                                "Added preview segment to episode '{Name}' (S{Season}E{Episode}) from 0 to {Duration}s",
                                episode.Name,
                                episode.ParentIndexNumber,
                                episode.IndexNumber,
                                TimeSpan.FromTicks(introSegment.StartTicks).TotalSeconds);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Skipping episode '{Name}' (S{Season}E{Episode}) - intro starts too early at {Duration}s",
                                episode.Name,
                                episode.ParentIndexNumber,
                                episode.IndexNumber,
                                TimeSpan.FromTicks(introSegment.StartTicks).TotalSeconds);
                        }
                    }
                    else if (introSegment != null)
                    {
                        _logger.LogDebug(
                            "Skipping episode '{Name}' (S{Season}E{Episode}) - intro starts at 0 ticks",
                            episode.Name,
                            episode.ParentIndexNumber,
                            episode.IndexNumber);
                    }
                }
                else if (hasIntro && hasPreview)
                {
                    _logger.LogDebug(
                        "Episode '{Name}' (S{Season}E{Episode}) already has preview segment",
                        episode.Name,
                        episode.ParentIndexNumber,
                        episode.IndexNumber);
                }
                else if (!hasIntro)
                {
                    _logger.LogDebug(
                        "Episode '{Name}' (S{Season}E{Episode}) has no intro segment - skipping",
                        episode.Name,
                        episode.ParentIndexNumber,
                        episode.IndexNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing episode '{Name}' (ID: {Id})", episode.Name, episode.Id);
                // Continue processing other episodes even if one fails
            }

            processedCount++;
            progress?.Report((double)processedCount / episodesToProcess.Count * 100);
        }

        _logger.LogInformation("Preview segment processing completed. Processed: {Processed}, Added: {Added}", processedCount, addedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening database connection to {DbPath}", dbPath);
        }
    }

    private async Task<bool> CheckTableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@tableName";
        command.Parameters.AddWithValue("@tableName", tableName);
        
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result != null;
    }

    private async Task<List<SegmentInfo>> GetSegmentsAsync(SqliteConnection connection, Guid itemId, CancellationToken cancellationToken)
    {
        var segments = new List<SegmentInfo>();
        
        // Convert to uppercase as Jellyfin stores GUIDs in uppercase
        var itemIdWithoutHyphens = itemId.ToString("N").ToUpperInvariant();
        var itemIdWithHyphens = itemId.ToString("D").ToUpperInvariant();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Type, StartTicks, EndTicks FROM MediaSegments WHERE ItemId = @itemId1 OR ItemId = @itemId2";
        command.Parameters.AddWithValue("@itemId1", itemIdWithoutHyphens);
        command.Parameters.AddWithValue("@itemId2", itemIdWithHyphens);
        
        try
        {
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                segments.Add(new SegmentInfo
                {
                    Id = reader.GetInt32(0),
                    Type = reader.GetString(1),
                    StartTicks = reader.GetInt64(2),
                    EndTicks = reader.GetInt64(3)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading segments for ItemId {ItemId}", itemId);
            throw;
        }
        
        return segments;
    }

    private async Task AddSegmentAsync(SqliteConnection connection, Guid itemId, string type, long startTicks, long endTicks, string guidFormat, CancellationToken cancellationToken)
    {
        // Use the pre-determined GUID format and convert to uppercase as Jellyfin stores GUIDs in uppercase
        var itemIdFormatted = guidFormat == "WithHyphens" ? itemId.ToString("D").ToUpperInvariant() : itemId.ToString("N").ToUpperInvariant();
        
        // Generate a new GUID for the segment Id
        var segmentId = guidFormat == "WithHyphens" ? Guid.NewGuid().ToString("D").ToUpperInvariant() : Guid.NewGuid().ToString("N").ToUpperInvariant();
        
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO MediaSegments (Id, ItemId, SegmentProviderId, Type, StartTicks, EndTicks)
            VALUES (@id, @itemId, @segmentProviderId, @type, @startTicks, @endTicks)";
        command.Parameters.AddWithValue("@id", segmentId);
        command.Parameters.AddWithValue("@itemId", itemIdFormatted);
        command.Parameters.AddWithValue("@segmentProviderId", "jellyfin-plugin-previewsegment");
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@startTicks", startTicks);
        command.Parameters.AddWithValue("@endTicks", endTicks);
        
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Successfully inserted preview segment for ItemId {ItemId}", itemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting segment for ItemId {ItemId}", itemId);
            throw;
        }
    }

    private async Task<string> DetectGuidFormatAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Detect the GUID format used in the database by checking existing segments
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemId FROM MediaSegments LIMIT 1";
        
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        
        if (result != null)
        {
            var itemIdString = result.ToString();
            // Check if the GUID contains hyphens (36 chars with hyphens vs 32 without)
            return itemIdString?.Length == 36 ? "WithHyphens" : "WithoutHyphens";
        }
        
        // Default to without hyphens (Jellyfin's typical format)
        return "WithoutHyphens";
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
            }
        };
    }

    private class SegmentInfo
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public long StartTicks { get; set; }
        public long EndTicks { get; set; }
    }
}
