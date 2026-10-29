using Amazon.S3;
using Amazon.S3.Model;

namespace UnidadPastoral.Api.Services;

public sealed class UploadService(IWebHostEnvironment environment, IConfiguration configuration, IAmazonS3? s3)
{
    public async Task<string> SaveAsync(IFormFile file, string folder)
    {
        var extension = Path.GetExtension(file.FileName);
        var name = $"{Guid.NewGuid():N}{extension}";
        var objectKey = $"{folder}/{name}";

        if (s3 is not null)
        {
            await using var input = file.OpenReadStream();
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = configuration["Neon:ObjectStorage:Bucket"] ?? "assets",
                Key = objectKey,
                InputStream = input,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType
            });
            return $"/uploads/{objectKey}";
        }

        var root = Path.Combine(environment.ContentRootPath, "uploads", folder);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        return $"/uploads/{objectKey}";
    }

    public async Task<(byte[] Content, string ContentType)?> ReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        if (s3 is null)
        {
            return null;
        }

        using var response = await s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = configuration["Neon:ObjectStorage:Bucket"] ?? "assets",
            Key = objectKey
        }, cancellationToken);

        await using var content = new MemoryStream();
        await response.ResponseStream.CopyToAsync(content, cancellationToken);
        return (content.ToArray(), response.Headers.ContentType ?? "application/octet-stream");
    }
}
