using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Amazon.Runtime;
using Amazon.S3;
using UnidadPastoral.Api.Data;
using UnidadPastoral.Api.Services;

static void RestoreDemoData(string contentRootPath, string connectionString, string uploadsPath)
{
    var demoDataPath = Path.Combine(contentRootPath, "demo-data");
    var sourceDatabasePath = Path.Combine(demoDataPath, "unidad-pastoral.db");
    if (!File.Exists(sourceDatabasePath))
    {
        throw new FileNotFoundException("Demo database snapshot was not included in the deployment.", sourceDatabasePath);
    }

    var databasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (string.IsNullOrWhiteSpace(databasePath) || databasePath == ":memory:")
    {
        throw new InvalidOperationException("Demo restore requires a file-based SQLite database.");
    }

    if (!Path.IsPathRooted(databasePath))
    {
        databasePath = Path.Combine(contentRootPath, databasePath);
    }

    if (string.Equals(Path.GetFullPath(sourceDatabasePath), Path.GetFullPath(databasePath), StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The demo snapshot and runtime database must use different paths.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    File.Delete(databasePath + "-wal");
    File.Delete(databasePath + "-shm");
    File.Copy(sourceDatabasePath, databasePath, overwrite: true);

    var demoUploadsPath = Path.Combine(demoDataPath, "uploads");
    if (Directory.Exists(demoUploadsPath))
    {
        CopyDemoUploads(demoUploadsPath, uploadsPath);
    }
}

static void CopyDemoUploads(string sourcePath, string destinationPath)
{
    foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
        var destinationFile = Path.Combine(destinationPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        File.Copy(sourceFile, destinationFile, overwrite: true);
    }
}

static void EnsureDatabaseCompatibility(AppDbContext db)
{
    using var connection = db.Database.GetDbConnection();
    connection.Open();

    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PhotoAlbums'";
        var albumTableExists = Convert.ToInt32(command.ExecuteScalar()) > 0;

        if (!albumTableExists)
        {
            using var createTable = connection.CreateCommand();
            createTable.CommandText = @"
                CREATE TABLE ""PhotoAlbums"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PhotoAlbums"" PRIMARY KEY AUTOINCREMENT,
                    ""ParishId"" INTEGER NULL,
                    ""Name"" TEXT NOT NULL DEFAULT '',
                    ""Description"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT ""FK_PhotoAlbums_Parishes_ParishId"" FOREIGN KEY (""ParishId"") REFERENCES ""Parishes"" (""Id"")
                );";
            createTable.ExecuteNonQuery();
        }
    }

    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Photos') WHERE name='AlbumId'";
        var albumColumnExists = Convert.ToInt32(command.ExecuteScalar()) > 0;

        if (!albumColumnExists)
        {
            using var addColumn = connection.CreateCommand();
            addColumn.CommandText = "ALTER TABLE Photos ADD COLUMN AlbumId INTEGER NULL;";
            addColumn.ExecuteNonQuery();
        }
    }

    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PastoraleActivities'";
        var pastoraleTableExists = Convert.ToInt32(command.ExecuteScalar()) > 0;

        if (!pastoraleTableExists)
        {
            using var createTable = connection.CreateCommand();
            createTable.CommandText = @"
                CREATE TABLE ""PastoraleActivities"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PastoraleActivities"" PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""ParishId"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_PastoraleActivities_Parishes_ParishId"" FOREIGN KEY (""ParishId"") REFERENCES ""Parishes"" (""Id"") ON DELETE CASCADE
                );
                CREATE INDEX ""IX_PastoraleActivities_ParishId"" ON ""PastoraleActivities"" (""ParishId"");";
            createTable.ExecuteNonQuery();
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
builder.Services.AddOpenApi();
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = PostgresConnectionString.Normalize(builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=unidad-pastoral.db");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (string.Equals(databaseProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        throw new InvalidOperationException($"Unsupported database provider '{databaseProvider}'. Use Sqlite or PostgreSql.");
    }
});

var jwtKey = builder.Configuration["Jwt:Key"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Configure Jwt:Key with a secret environment variable before starting outside Development.");
}

jwtKey ??= "dev-key-change-me-please-use-32chars";
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key must contain at least 32 bytes.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "unidad-pastoral",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "unidad-pastoral",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();
var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]?
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(new[] { "http://localhost:5173" }.Concat(allowedOrigins).ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()));
builder.Services.AddSingleton<TokenService>();
var storageEndpoint = builder.Configuration["AWS_ENDPOINT_URL_S3"];
var storageAccessKey = builder.Configuration["AWS_ACCESS_KEY_ID"];
var storageSecretKey = builder.Configuration["AWS_SECRET_ACCESS_KEY"];
IAmazonS3? neonStorage = null;
if (!string.IsNullOrWhiteSpace(storageEndpoint))
{
    if (string.IsNullOrWhiteSpace(storageAccessKey) || string.IsNullOrWhiteSpace(storageSecretKey))
    {
        throw new InvalidOperationException("Neon Object Storage endpoint is configured but its S3 credentials are missing.");
    }

    neonStorage = new AmazonS3Client(
        new BasicAWSCredentials(storageAccessKey, storageSecretKey),
        new AmazonS3Config
        {
            ServiceURL = storageEndpoint,
            AuthenticationRegion = builder.Configuration["AWS_REGION"],
            ForcePathStyle = true
        });
    builder.Services.AddSingleton<IAmazonS3>(neonStorage);
}

builder.Services.AddSingleton<UploadService>();
builder.Services.AddScoped<IPastoraleActivityService, PastoraleActivityService>();

var app = builder.Build();

var uploads = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploads);
if (builder.Configuration.GetValue<bool>("Demo:RestoreOnStartup"))
{
    if (!string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("The bundled demo snapshot can only be restored to SQLite.");
    }

    RestoreDemoData(app.Environment.ContentRootPath, connectionString, uploads);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (string.Equals(databaseProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
    {
        var migrationConnectionString = PostgresConnectionString.Normalize(builder.Configuration["DATABASE_URL_UNPOOLED"]
            ?? builder.Configuration.GetConnectionString("Migration")
            ?? connectionString);
        var migrationOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(migrationConnectionString)
            .Options;
        await using (var migrationContext = new AppDbContext(migrationOptions))
        {
            await migrationContext.Database.MigrateAsync();
        }

        if (builder.Configuration.GetValue<bool>("Demo:ImportSnapshot"))
        {
            var storage = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
            var snapshotPath = Path.Combine(app.Environment.ContentRootPath, "demo-data", "unidad-pastoral.db");
            var imported = await DemoDataImporter.ImportIfEmptyAsync(
                db,
                snapshotPath,
                Path.Combine(app.Environment.ContentRootPath, "demo-data", "uploads"),
                storage,
                builder.Configuration["Neon:ObjectStorage:Bucket"] ?? "assets",
                CancellationToken.None);
            app.Logger.LogInformation("Demo snapshot import result: {Imported}", imported ? "imported" : "skipped; database already has data");
        }
    }
    else
    {
        db.Database.EnsureCreated();
        EnsureDatabaseCompatibility(db);
    }
    var initialAdminPassword = builder.Configuration["Admin:InitialPassword"];
    if (string.IsNullOrWhiteSpace(initialAdminPassword) && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Configure Admin:InitialPassword before starting outside Development.");
    }

    DbSeeder.Seed(
        db,
        builder.Configuration["Admin:Username"] ?? "admin",
        initialAdminPassword ?? "CambiaEsto1");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploads),
    RequestPath = "/uploads"
});
app.MapControllers();
app.Run();
