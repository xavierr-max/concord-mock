using Concord.Api.Configurations;
using Microsoft.Extensions.Options;

namespace Concord.Api.Services;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string rootPath;
    private readonly string publicBaseUrl;

    public LocalFileStorageService(IHostEnvironment environment, IOptions<FileStorageSettings> options)
    {
        var settings = options.Value;
        rootPath = Path.GetFullPath(settings.LocalPath, environment.ContentRootPath);
        publicBaseUrl = settings.PublicBaseUrl.TrimEnd('/');
        Directory.CreateDirectory(rootPath);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, string extension, CancellationToken cancellationToken)
    {
        var storageKey = $"{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.GetFullPath(storageKey.Replace('/', Path.DirectorySeparatorChar), rootPath);
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de armazenamento inválido.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        await content.CopyToAsync(target, cancellationToken);
        return new StoredFile($"{publicBaseUrl}/{storageKey}", storageKey);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(storageKey.Replace('/', Path.DirectorySeparatorChar), rootPath);
        if (path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
