using Hadron.Erp.Api.Platform.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hadron.Erp.IntegrationTests;

public sealed class ProfilePhotoStoreTests
{
    [Fact]
    public async Task Uncommitted_photo_upload_is_compensated_but_committed_upload_is_retained()
    {
        var store = new InMemoryProfilePhotoStore();
        var photo = new ProfilePhotoContent([1, 2, 3], "image/png", "hash");
        var abandonedKey = $"tests/{Guid.NewGuid():N}.png";
        var committedKey = $"tests/{Guid.NewGuid():N}.png";

        await using (var abandoned = new ProfilePhotoWriteLease(store, abandonedKey,
                         NullLogger.Instance))
            await abandoned.Upload(photo, CancellationToken.None);

        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await using var _ = await store.Get(abandonedKey, CancellationToken.None);
        });

        await using (var committed = new ProfilePhotoWriteLease(store, committedKey,
                         NullLogger.Instance))
        {
            await committed.Upload(photo, CancellationToken.None);
            committed.Complete();
        }

        await using var retained = await store.Get(committedKey, CancellationToken.None);
        Assert.Equal(3, retained.Length);
    }

    [Fact]
    public async Task In_memory_profile_store_health_probe_is_available()
    {
        IProfilePhotoStoreHealthProbe probe = new InMemoryProfilePhotoStore();
        await probe.CheckAvailability(CancellationToken.None);
    }

    [Fact]
    public async Task Minio_download_is_complete_before_the_returned_stream_is_read()
    {
        var store = new MinioProfilePhotoStore(new ProfilePhotoStorageOptions
        {
            Endpoint = Required("MINIO_ENDPOINT"),
            AccessKey = Required("MINIO_ACCESS_KEY"),
            SecretKey = Required("MINIO_SECRET_KEY"),
            Bucket = Required("MINIO_PROFILE_BUCKET")
        });
        var objectKey = $"integration-tests/{Guid.NewGuid():N}.bin";
        var expected = Enumerable.Range(0, 2 * 1024 * 1024).Select(index => (byte)(index % 251)).ToArray();
        var uploaded = false;

        try
        {
            await store.CheckAvailability(CancellationToken.None);
            await using var upload = new MemoryStream(expected, writable: false);
            await store.Put(objectKey, upload, expected.LongLength, "application/octet-stream", CancellationToken.None);
            uploaded = true;

            await using var download = await store.Get(objectKey, CancellationToken.None);
            Assert.Equal(expected.LongLength, download.Length);
            using var actual = new MemoryStream();
            await download.CopyToAsync(actual, CancellationToken.None);

            Assert.Equal(expected, actual.ToArray());
        }
        finally
        {
            if (uploaded) await store.Delete(objectKey, CancellationToken.None);
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required.");
}
