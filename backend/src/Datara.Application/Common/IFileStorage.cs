namespace Datara.Application.Common;

/// <summary>Stores uploaded dataset bytes.</summary>
/// <remarks>
/// Implementations must treat the key as opaque and must never resolve it against
/// user input. Keys are generated server-side in <c>StorageKeyFactory</c>.
/// </remarks>
public interface IFileStorage
{
    Task SaveAsync(string key, byte[] content, CancellationToken cancellationToken);

    Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Builds storage keys. Never accepts client-supplied path components.</summary>
public static class StorageKeyFactory
{
    public static string ForDataset(Guid ownerId, Guid datasetId, string extension) =>
        $"datasets/{ownerId:N}/{datasetId:N}{NormaliseExtension(extension)}";

    /// <summary>
    /// Reduces an extension to a safe token. Anything containing a path separator or
    /// unexpected character is discarded rather than sanitised, so the resulting key
    /// can only ever be a flat file name inside a server-chosen directory.
    /// </summary>
    private static string NormaliseExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".dat";
        }

        var ext = extension.Trim().ToLowerInvariant();
        var allowed = ext.All(c => char.IsLetterOrDigit(c) || c == '.');
        var leaf = allowed ? Path.GetFileName(ext) : string.Empty;

        return string.IsNullOrEmpty(leaf) || leaf.Length > 12 ? ".dat" : leaf;
    }
}