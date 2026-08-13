namespace Concord.Api.Services;

public sealed record StoredFile(string Url, string StorageKey);

public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
