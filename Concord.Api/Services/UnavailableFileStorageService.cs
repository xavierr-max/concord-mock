namespace Concord.Api.Services;

public sealed class UnavailableFileStorageService : IFileStorageService
{
    private static InvalidOperationException NotConfigured() =>
        new("Configure um provedor externo de IFileStorageService para uploads em produção.");

    public Task<StoredFile> SaveAsync(Stream content, string extension, CancellationToken cancellationToken) =>
        Task.FromException<StoredFile>(NotConfigured());

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromException(NotConfigured());
}
