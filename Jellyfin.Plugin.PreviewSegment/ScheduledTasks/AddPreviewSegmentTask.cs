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

            var episodes = _libraryManager.GetItemList(query).OfType<Episode>();
            episodesToProcess.AddRange(episodes);
        }

        _logger.LogInformation("Found {Count} episodes to check", episodesToProcess.Count);

        var processedCount = 0;
        var addedCount = 0;
        var dbPath = System.IO.Path.Combine(_appPaths.DataPath, "library.db");

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var episode in episodesToProcess)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Check if the MediaSegments table exists
                var tableExists = await CheckTableExistsAsync(connection, "MediaSegments", cancellationToken).ConfigureAwait(false);
                if (!tableExists)
                {
                    _logger.LogWarning("MediaSegments table does not exist in the database. This feature may require Jellyfin 10.10 or later.");
                    return;
                }

                // Query segments for this episode
                var segments = await GetSegmentsAsync(connection, episode.Id, cancellationToken).ConfigureAwait(false);
                
                var hasIntro = segments.Any(s => s.Type == "Intro");
                var hasPreview = segments.Any(s => s.Type == "Preview");

                if (hasIntro && !hasPreview)
                {
                    var introSegment = segments.First(s => s.Type == "Intro");
                    
                    // Add preview segment from 0 to the start of the intro
                    if (introSegment.StartTicks > 0)
                    {
                        await AddSegmentAsync(connection, episode.Id, "Preview", 0, introSegment.StartTicks, cancellationToken).ConfigureAwait(false);
                        addedCount++;
                        _logger.LogInformation(
                            "Added preview segment to episode '{Name}' (S{Season}E{Episode}) from 0 to {Duration}s",
                            episode.Name,
                            episode.ParentIndexNumber,
                            episode.IndexNumber,
                            TimeSpan.FromTicks(introSegment.StartTicks).TotalSeconds);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing episode '{Name}'", episode.Name);
            }

            processedCount++;
            progress?.Report((double)processedCount / episodesToProcess.Count * 100);
        }

        _logger.LogInformation("Preview segment processing completed. Processed: {Processed}, Added: {Added}", processedCount, addedCount);
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
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, StreamIndex, Type, StartTicks, EndTicks FROM MediaSegments WHERE ItemId = @itemId";
        command.Parameters.AddWithValue("@itemId", itemId.ToString("N"));
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new SegmentInfo
            {
                Id = reader.GetInt32(0),
                StreamIndex = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                Type = reader.GetString(2),
                StartTicks = reader.GetInt64(3),
                EndTicks = reader.GetInt64(4)
            });
        }
        
        return segments;
    }

    private async Task AddSegmentAsync(SqliteConnection connection, Guid itemId, string type, long startTicks, long endTicks, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO MediaSegments (ItemId, StreamIndex, Type, StartTicks, EndTicks)
            VALUES (@itemId, NULL, @type, @startTicks, @endTicks)";
        command.Parameters.AddWithValue("@itemId", itemId.ToString("N"));
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@startTicks", startTicks);
        command.Parameters.AddWithValue("@endTicks", endTicks);
        
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        public int? StreamIndex { get; set; }
        public string Type { get; set; } = string.Empty;
        public long StartTicks { get; set; }
        public long EndTicks { get; set; }
    }
}
