using System;
using System.Collections.Generic;
using System.IO;

namespace JellyfinBookReader.Utils;

public static class MimeTypeHelper
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".epub", "application/epub+zip" },
        { ".pdf", "application/pdf" },
        { ".mobi", "application/x-mobipocket-ebook" },
        { ".azw3", "application/x-mobi8-ebook" },
        { ".azw", "application/x-mobipocket-ebook" },
        { ".cbz", "application/x-cbz" },
        { ".cbr", "application/x-cbr" },
        { ".fb2", "application/x-fictionbook+xml" },
        { ".txt", "text/plain" },
        { ".djvu", "image/vnd.djvu" },
    };

    public static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return MimeTypes.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }

    public static bool IsSupportedBookFormat(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return MimeTypes.ContainsKey(ext);
    }
}