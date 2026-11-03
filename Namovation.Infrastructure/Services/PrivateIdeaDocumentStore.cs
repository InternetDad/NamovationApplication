using Namovation.Application;

namespace Namovation.Infrastructure.Services;

public sealed class PrivateIdeaDocumentStore(string storageDirectory) : IIdeaDocumentStore
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx", ".txt"
    };

    private string StorageDirectory => storageDirectory;

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        extension = extension.ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) throw new InvalidOperationException("This document type is not supported.");

        Directory.CreateDirectory(StorageDirectory);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(StorageDirectory, storedFileName);
        try
        {
            await using var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(destination, cancellationToken);
            return storedFileName;
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storedFileName) || Path.GetFileName(storedFileName) != storedFileName)
            return Task.FromResult<Stream?>(null);

        var fullPath = Path.Combine(StorageDirectory, storedFileName);
        if (!File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(storedFileName) && Path.GetFileName(storedFileName) == storedFileName)
        {
            var fullPath = Path.Combine(StorageDirectory, storedFileName);
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        return Task.CompletedTask;
    }
}
