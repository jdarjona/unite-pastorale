using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using UnidadPastoral.Api.Models;
using UnidadPastoral.Api.Services;

namespace UnidadPastoral.Api.Data;

public static class DemoDataImporter
{
    public static async Task<bool> ImportIfEmptyAsync(
        AppDbContext destination,
        string snapshotPath,
        string uploadsPath,
        IAmazonS3 storage,
        string bucket,
        CancellationToken cancellationToken)
    {
        var hasData = await destination.Parishes.AnyAsync(cancellationToken)
            || await destination.Posts.AnyAsync(cancellationToken)
            || await destination.Photos.AnyAsync(cancellationToken)
            || await destination.PhotoAlbums.AnyAsync(cancellationToken)
            || await destination.CalendarEvents.AnyAsync(cancellationToken)
            || await destination.PastoraleActivities.AnyAsync(cancellationToken);

        if (hasData)
        {
            return false;
        }

        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException("Demo SQLite snapshot was not included in the deployment.", snapshotPath);
        }

        var sourceOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={snapshotPath};Mode=ReadOnly")
            .Options;
        await using var source = new AppDbContext(sourceOptions);

        var parishes = await source.Parishes.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var posts = await source.Posts.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var albums = await source.PhotoAlbums.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var photos = await source.Photos.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var events = await source.CalendarEvents.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);
        var activities = await source.PastoraleActivities.AsNoTracking().OrderBy(item => item.Id).ToListAsync(cancellationToken);

        if (parishes.Count == 0)
        {
            throw new InvalidOperationException("The demo SQLite snapshot contains no parishes.");
        }

        foreach (var filePath in Directory.EnumerateFiles(uploadsPath, "*", SearchOption.AllDirectories))
        {
            var key = Path.GetRelativePath(uploadsPath, filePath).Replace(Path.DirectorySeparatorChar, '/');
            await using var stream = File.OpenRead(filePath);
            await storage.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = stream,
                ContentType = ContentTypeFor(filePath)
            }, cancellationToken);
        }

        await using var transaction = await destination.Database.BeginTransactionAsync(cancellationToken);

        destination.Parishes.AddRange(parishes.Select(item => new Parish
        {
            Id = item.Id,
            Name = item.Name,
            Slug = item.Slug,
            Patron = item.Patron,
            Address = item.Address,
            MassSchedule = item.MassSchedule,
            Description = item.Description,
            AccentColor = item.AccentColor
        }));
        await destination.SaveChangesAsync(cancellationToken);

        destination.Posts.AddRange(posts.Select(item => new Post
        {
            Id = item.Id,
            ParishId = item.ParishId,
            Type = item.Type,
            Title = item.Title,
            Slug = item.Slug,
            Body = item.Body,
            PublishedAt = UtcDateTime.Normalize(item.PublishedAt),
            EventStartsAt = item.EventStartsAt is null ? null : UtcDateTime.Normalize(item.EventStartsAt.Value),
            CoverImagePath = item.CoverImagePath,
            AttachmentPath = item.AttachmentPath,
            AttachmentName = item.AttachmentName
        }));
        destination.PhotoAlbums.AddRange(albums.Select(item => new PhotoAlbum
        {
            Id = item.Id,
            ParishId = item.ParishId,
            Name = item.Name,
            Description = item.Description,
            CreatedAt = UtcDateTime.Normalize(item.CreatedAt)
        }));
        await destination.SaveChangesAsync(cancellationToken);

        destination.Photos.AddRange(photos.Select(item => new Photo
        {
            Id = item.Id,
            ParishId = item.ParishId,
            AlbumId = item.AlbumId,
            Title = item.Title,
            Caption = item.Caption,
            FilePath = item.FilePath,
            TakenAt = UtcDateTime.Normalize(item.TakenAt)
        }));
        destination.CalendarEvents.AddRange(events.Select(item => new CalendarEvent
        {
            Id = item.Id,
            ParishId = item.ParishId,
            Title = item.Title,
            Description = item.Description,
            Location = item.Location,
            StartsAt = UtcDateTime.Normalize(item.StartsAt),
            EndsAt = item.EndsAt is null ? null : UtcDateTime.Normalize(item.EndsAt.Value),
            AllDay = item.AllDay
        }));
        destination.PastoraleActivities.AddRange(activities.Select(item => new PastoraleActivity
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            ParishId = item.ParishId
        }));
        await destination.SaveChangesAsync(cancellationToken);

        var sequenceResetStatements = new[]
        {
            "SELECT setval(pg_get_serial_sequence('\"Parishes\"', 'Id'), COALESCE(MAX(\"Id\"), 1), COUNT(*) > 0) FROM \"Parishes\";",
            "SELECT setval(pg_get_serial_sequence('\"Posts\"', 'Id'), COALESCE(MAX(\"Id\"), 1), COUNT(*) > 0) FROM \"Posts\";",
            "SELECT setval(pg_get_serial_sequence('\"Photos\"', 'Id'), COALESCE(MAX(\"Id\"), 1), COUNT(*) > 0) FROM \"Photos\";",
            "SELECT setval(pg_get_serial_sequence('\"PhotoAlbums\"', 'Id'), COALESCE(MAX(\"Id\"), 1), COUNT(*) > 0) FROM \"PhotoAlbums\";",
            "SELECT setval(pg_get_serial_sequence('\"CalendarEvents\"', 'Id'), COALESCE(MAX(\"Id\"), 1), COUNT(*) > 0) FROM \"CalendarEvents\";",
            "SELECT setval(pg_get_serial_sequence('\"PastoraleActivities\"', 'Id'), COALESCE(MAX(\"Id\"), 1), COUNT(*) > 0) FROM \"PastoraleActivities\";"
        };

        foreach (var statement in sequenceResetStatements)
        {
            await destination.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}