using System.Collections.Concurrent;
using System.Security.Cryptography;
using Minio;
using Minio.DataModel.Args;

namespace Hadron.Erp.Api.Modules.Collaboration.Infrastructure;

public sealed record CollaborationFileContent(byte[] Bytes, string ContentType, string FileName, string Sha256);

public interface ICollaborationFileStore
{
    Task Put(string objectKey, Stream content, long size, string contentType, CancellationToken ct);
    Task<Stream> Get(string objectKey, CancellationToken ct);
    Task Delete(string objectKey, CancellationToken ct);
}

public sealed class CollaborationStorageOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = "hadron-collaboration-files";

    public static CollaborationStorageOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection("CollaborationStorage");
        var result = new CollaborationStorageOptions
        {
            Endpoint = section["Endpoint"] ?? configuration["MINIO_ENDPOINT"] ?? string.Empty,
            AccessKey = section["AccessKey"] ?? configuration["MINIO_ACCESS_KEY"] ?? string.Empty,
            SecretKey = section["SecretKey"] ?? configuration["MINIO_SECRET_KEY"] ?? string.Empty,
            Bucket = section["Bucket"] ?? configuration["MINIO_COLLABORATION_BUCKET"] ?? "hadron-collaboration-files"
        };
        if (string.IsNullOrWhiteSpace(result.Endpoint) || string.IsNullOrWhiteSpace(result.AccessKey) ||
            string.IsNullOrWhiteSpace(result.SecretKey))
            throw new InvalidOperationException("MinIO collaboration storage configuration is required.");
        return result;
    }
}

public sealed class MinioCollaborationFileStore : ICollaborationFileStore
{
    private readonly IMinioClient client;
    private readonly string bucket;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool bucketReady;

    public MinioCollaborationFileStore(CollaborationStorageOptions options)
    {
        var uri = new Uri(options.Endpoint);
        client = new MinioClient().WithEndpoint(uri.Host, uri.Port).WithCredentials(options.AccessKey, options.SecretKey)
            .WithSSL(uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)).Build();
        bucket = options.Bucket;
    }

    public async Task Put(string objectKey, Stream content, long size, string contentType, CancellationToken ct)
    {
        await EnsureBucket(ct);
        await client.PutObjectAsync(new PutObjectArgs().WithBucket(bucket).WithObject(objectKey)
            .WithStreamData(content).WithObjectSize(size).WithContentType(contentType), ct);
    }

    public async Task<Stream> Get(string objectKey, CancellationToken ct)
    {
        await EnsureBucket(ct); var output = new MemoryStream();
        await client.GetObjectAsync(new GetObjectArgs().WithBucket(bucket).WithObject(objectKey)
            .WithCallbackStream((stream, callbackCt) => stream.CopyToAsync(output, callbackCt)), ct);
        output.Position = 0; return output;
    }

    public async Task Delete(string objectKey, CancellationToken ct)
    { await EnsureBucket(ct); await client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey), ct); }

    private async Task EnsureBucket(CancellationToken ct)
    {
        if (bucketReady) return; await gate.WaitAsync(ct);
        try
        {
            if (bucketReady) return;
            if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct))
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);
            bucketReady = true;
        }
        finally { gate.Release(); }
    }
}

public sealed class InMemoryCollaborationFileStore : ICollaborationFileStore
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> objects = new();
    public async Task Put(string objectKey, Stream content, long size, string contentType, CancellationToken ct)
    { using var output = new MemoryStream(); await content.CopyToAsync(output, ct); objects[objectKey] = (output.ToArray(), contentType); }
    public Task<Stream> Get(string objectKey, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(
        objects.TryGetValue(objectKey, out var value) ? value.Bytes : throw new FileNotFoundException(), writable: false));
    public Task Delete(string objectKey, CancellationToken ct) { objects.TryRemove(objectKey, out _); return Task.CompletedTask; }
}

public static class CollaborationFileValidator
{
    public const long MaximumBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "application/pdf", "text/plain", "text/csv",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "video/mp4", "audio/mpeg", "audio/mp4", "audio/ogg"
    };

    public static async Task<CollaborationFileContent> Read(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaximumBytes) throw new Application.CollaborationValidationException(
            Error("file", "حجم فایل باید بین ۱ بایت و ۲۵ مگابایت باشد."));
        var contentType = file.ContentType.Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(contentType)) throw new Application.CollaborationValidationException(
            Error("file", "این نوع فایل مجاز نیست."));
        var fileName = Path.GetFileName(file.FileName).Trim();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255) throw new Application.CollaborationValidationException(
            Error("file", "نام فایل معتبر و حداکثر ۲۵۵ نویسه لازم است."));
        await using var input = file.OpenReadStream(); using var output = new MemoryStream((int)file.Length);
        await input.CopyToAsync(output, ct); var bytes = output.ToArray();
        if (!MatchesSignature(bytes, contentType)) throw new Application.CollaborationValidationException(
            Error("file", "محتوای فایل با نوع اعلام‌شده مطابقت ندارد."));
        return new(bytes, contentType, fileName, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static bool MatchesSignature(ReadOnlySpan<byte> b, string type) => type switch
    {
        "image/jpeg" => b.Length >= 3 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff,
        "image/png" => b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
        "image/webp" => b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8),
        "application/pdf" => b.Length >= 5 && b[..5].SequenceEqual("%PDF-"u8),
        "video/mp4" or "audio/mp4" => b.Length >= 12 && b.Slice(4, 4).SequenceEqual("ftyp"u8),
        "audio/mpeg" => b.Length >= 3 && (b[..3].SequenceEqual("ID3"u8) || (b[0] == 0xff && (b[1] & 0xe0) == 0xe0)),
        "audio/ogg" => b.Length >= 4 && b[..4].SequenceEqual("OggS"u8),
        var x when x.StartsWith("application/vnd.openxmlformats", StringComparison.Ordinal) => b.Length >= 4 && b[..4].SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 }),
        "text/plain" or "text/csv" => !b.Contains((byte)0),
        _ => false
    };
    private static Dictionary<string, string[]> Error(string key, string value) => new() { [key] = [value] };
}

public sealed class CollaborationFileWriteLease(ICollaborationFileStore store, string objectKey, ILogger logger) : IAsyncDisposable
{
    private bool uploaded; private bool completed;
    public async Task Upload(CollaborationFileContent content, CancellationToken ct)
    {
        await using var stream = new MemoryStream(content.Bytes, false);
        try { await store.Put(objectKey, stream, content.Bytes.LongLength, content.ContentType, ct); uploaded = true; }
        catch { uploaded = true; throw; }
    }
    public void Complete() => completed = true;
    public async ValueTask DisposeAsync()
    {
        if (!uploaded || completed) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await store.Delete(objectKey, timeout.Token); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not delete uncommitted collaboration object {ObjectKey}.", objectKey); }
    }
}
