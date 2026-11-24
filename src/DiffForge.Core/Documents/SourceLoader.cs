using System.IO.Compression;

namespace DiffForge.Core.Documents;

/// <summary>Bytes de un origen más sus metadatos para el panel de información.</summary>
public sealed record SourceContent(
    string Spec,
    byte[] Bytes,
    bool IsReadOnly,
    DateTime? LastWriteTime,
    bool CanSaveInPlace);

/// <summary>
/// Resuelve especificaciones de origen: archivo local, miembro dentro de un .zip
/// (<c>C:\a.zip\carpeta\archivo.txt</c>) o URL http/https.
/// </summary>
public static class SourceLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<SourceContent> LoadAsync(string spec, CancellationToken ct = default)
    {
        if (Uri.TryCreate(spec, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var bytes = await Http.GetByteArrayAsync(uri, ct);
            return new SourceContent(spec, bytes, IsReadOnly: true, LastWriteTime: null, CanSaveInPlace: false);
        }

        if (File.Exists(spec))
        {
            var info = new FileInfo(spec);
            var bytes = await File.ReadAllBytesAsync(spec, ct);
            return new SourceContent(spec, bytes, info.IsReadOnly, info.LastWriteTime, CanSaveInPlace: !info.IsReadOnly);
        }

        if (TrySplitArchiveSpec(spec, out var archivePath, out var entryPath))
        {
            using var zip = ZipFile.OpenRead(archivePath);
            var entry = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/'), entryPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"No existe '{entryPath}' dentro de '{archivePath}'.");
            await using var stream = entry.Open();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return new SourceContent(spec, ms.ToArray(), IsReadOnly: true, entry.LastWriteTime.LocalDateTime, CanSaveInPlace: false);
        }

        throw new FileNotFoundException($"No se encontró '{spec}'.", spec);
    }

    public static bool TrySplitArchiveSpec(string spec, out string archivePath, out string entryPath)
    {
        archivePath = entryPath = string.Empty;
        int idx = spec.IndexOf(".zip", StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            int end = idx + 4;
            if (end < spec.Length && spec[end] is '\\' or '/' && File.Exists(spec[..end]))
            {
                archivePath = spec[..end];
                entryPath = spec[(end + 1)..].Replace('\\', '/');
                return true;
            }
            idx = spec.IndexOf(".zip", end, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
