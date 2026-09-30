using System.Collections.Concurrent;
using System.Security.Cryptography;
using Minio;
using Minio.DataModel.Args;

namespace Hadron.Erp.Api.Platform.Storage;

public sealed record ProfilePhotoContent(byte[] Bytes, string ContentType, string Sha256);

public interface IProfilePhotoStore
{
    Task Put(string objectKey, Stream content, long size, string contentType, CancellationToken ct);
    Task<Stream> Get(string objectKey, CancellationToken ct);
    Task Delete(string objectKey, CancellationToken ct);
}

public interface IProfilePhotoStoreHealthProbe
{
    Task CheckAvailability(CancellationToken ct);
}

public sealed class ProfilePhotoStorageOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = "hadron-profile-photos";

    public static ProfilePhotoStorageOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection("ProfileStorage");
        var result = new ProfilePhotoStorageOptions
        {
            Endpoint = section["Endpoint"] ?? configuration["MINIO_ENDPOINT"] ?? string.Empty,
            AccessKey = section["AccessKey"] ?? configuration["MINIO_ACCESS_KEY"] ?? string.Empty,
            SecretKey = section["SecretKey"] ?? configuration["MINIO_SECRET_KEY"] ?? string.Empty,
            Bucket = section["Bucket"] ?? configuration["MINIO_PROFILE_BUCKET"] ?? "hadron-profile-photos"
        };
        if (string.IsNullOrWhiteSpace(result.Endpoint) || string.IsNullOrWhiteSpace(result.AccessKey)
            || string.IsNullOrWhiteSpace(result.SecretKey))
            throw new InvalidOperationException("MinIO profile storage configuration is required.");
        return result;
    }
}

public sealed class MinioProfilePhotoStore : IProfilePhotoStore, IProfilePhotoStoreHealthProbe
{
    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly SemaphoreSlim _bucketGate = new(1, 1);
    private bool _bucketReady;

    public MinioProfilePhotoStore(ProfilePhotoStorageOptions options)
    {
        var uri = new Uri(options.Endpoint);
        _client = new MinioClient()
            .WithEndpoint(uri.Host, uri.Port)
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithSSL(uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            .Build();
        _bucket = options.Bucket;
    }

    public async Task Put(string objectKey, Stream content, long size, string contentType, CancellationToken ct)
    {
        await EnsureBucket(ct);
        var args = new PutObjectArgs().WithBucket(_bucket).WithObject(objectKey)
            .WithStreamData(content).WithObjectSize(size).WithContentType(contentType);
        await _client.PutObjectAsync(args, ct);
    }

    public async Task<Stream> Get(string objectKey, CancellationToken ct)
    {
        await EnsureBucket(ct);
        var output = new MemoryStream();
        var args = new GetObjectArgs().WithBucket(_bucket).WithObject(objectKey)
            .WithCallbackStream((stream, callbackCt) => stream.CopyToAsync(output, callbackCt));
        await _client.GetObjectAsync(args, ct);
        output.Position = 0;
        return output;
    }

    public async Task Delete(string objectKey, CancellationToken ct)
    {
        await EnsureBucket(ct);
        await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(objectKey), ct);
    }

    public async Task CheckAvailability(CancellationToken ct)
    {
        await EnsureBucket(ct);
        var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
        if (exists) return;

        _bucketReady = false;
        throw new InvalidOperationException("The personnel-photo bucket is unavailable.");
    }

    private async Task EnsureBucket(CancellationToken ct)
    {
        if (_bucketReady) return;
        await _bucketGate.WaitAsync(ct);
        try
        {
            if (_bucketReady) return;
            var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
            if (!exists) await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
            _bucketReady = true;
        }
        finally { _bucketGate.Release(); }
    }
}

public sealed class InMemoryProfilePhotoStore : IProfilePhotoStore, IProfilePhotoStoreHealthProbe
{
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _objects = new();
    public async Task Put(string objectKey, Stream content, long size, string contentType, CancellationToken ct)
    {
        using var output = new MemoryStream();
        await content.CopyToAsync(output, ct);
        _objects[objectKey] = (output.ToArray(), contentType);
    }
    public Task<Stream> Get(string objectKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(_objects.TryGetValue(objectKey, out var value)
            ? value.Bytes : throw new FileNotFoundException("Profile photo was not found."), writable: false));
    public Task Delete(string objectKey, CancellationToken ct) { _objects.TryRemove(objectKey, out _); return Task.CompletedTask; }
    public Task CheckAvailability(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Compensates an object-store upload when the database transaction that references it does not commit.
/// The database remains authoritative; completing the lease is only valid after SaveChanges/Commit succeeds.
/// </summary>
public sealed class ProfilePhotoWriteLease(
    IProfilePhotoStore store,
    string objectKey,
    ILogger logger) : IAsyncDisposable
{
    private bool _uploaded;
    private bool _completed;

    public string ObjectKey { get; } = objectKey;

    public async Task Upload(ProfilePhotoContent photo, CancellationToken ct)
    {
        await using var stream = new MemoryStream(photo.Bytes, writable: false);
        try
        {
            await store.Put(ObjectKey, stream, photo.Bytes.LongLength, photo.ContentType, ct);
            _uploaded = true;
        }
        catch
        {
            // A remote write can succeed even when the acknowledgement is lost. Mark it for a
            // best-effort compensating delete before preserving the original exception.
            _uploaded = true;
            throw;
        }
    }

    public void Complete() => _completed = true;

    public async ValueTask DisposeAsync()
    {
        if (!_uploaded || _completed) return;

        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await store.Delete(ObjectKey, cleanupTimeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Compensating deletion failed for uncommitted profile photo {ObjectKey}.", ObjectKey);
        }
    }
}

public static class ProfilePhotoValidator
{
    public const long MaximumBytes = 2 * 1024 * 1024;

    public static async Task<ProfilePhotoContent> Read(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaximumBytes)
            throw new ProfilePhotoValidationException("PHOTO_SIZE_INVALID", "Personnel photo must be between 1 byte and 2 MB.");

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream((int)file.Length);
        await input.CopyToAsync(output, ct);
        var bytes = output.ToArray();
        var detected = Detect(bytes) ?? throw new ProfilePhotoValidationException(
            "PHOTO_TYPE_INVALID", "Only valid JPEG, PNG or WebP personnel photos are accepted.");
        if (!string.Equals(file.ContentType, detected, StringComparison.OrdinalIgnoreCase))
            throw new ProfilePhotoValidationException("PHOTO_CONTENT_TYPE_MISMATCH", "Photo content does not match its MIME type.");
        return new(bytes, detected, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "image/png";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}

public sealed class ProfilePhotoValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
