namespace Namovation.Application;

public interface IIdeaDocumentStore
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
}

public sealed record SupportingDocumentUpload(
    Stream Content,
    string OriginalFileName,
    long SizeBytes);
