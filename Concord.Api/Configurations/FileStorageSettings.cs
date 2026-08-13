namespace Concord.Api.Configurations;

public sealed class FileStorageSettings
{
    public const string SectionName = "FileStorage";
    public string Provider { get; init; } = "Local";
    public string LocalPath { get; init; } = "wwwroot/uploads";
    public string PublicBaseUrl { get; init; } = "/uploads";
    public long MaxFileSize { get; init; } = 10 * 1024 * 1024;
    public Dictionary<string, string[]> AllowedContentTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
