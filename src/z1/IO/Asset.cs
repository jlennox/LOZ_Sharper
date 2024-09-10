﻿using System.Diagnostics;
using System.Text.Json;
using SkiaSharp;
using z1.Common.IO;

namespace z1.IO;

[DebuggerDisplay("{Filename}")]
internal readonly struct Asset
{
    // The assets are so small that we can buffer them all into memory without an issue.
    private static readonly Dictionary<string, byte[]> _assets = new(128);

    public string Filename { get; } // For debug purposes only.
    public bool IsEmpty => _assetData == null || _assetData.Length == 0;

    private readonly byte[] _assetData;

    public Asset(string filename)
    {
        Filename = filename;
        if (!_assets.TryGetValue(filename, out _assetData!))
        {
            throw new Exception($"Unable to find asset {filename}");
        }
    }

    public static void Initialize()
    {
        foreach (var kv in AssetLoader.Initialize())
        {
            _assets.Add(kv.Key, kv.Value);
        }

        AddFontAddendum();
    }

    private static void AddFontAddendum()
    {
        using var fontAddendum = EmbeddedResource.GetFontAddendum();
        using var font = SKBitmap.Decode(_assets[Filenames.Font]);

        if (font.Width != fontAddendum.Width) throw new Exception("Font and font addendum must have the same width.");

        var newHeight = font.Height + fontAddendum.Height;
        using var combinedFont = new SKBitmap(font.Width, newHeight, font.ColorType, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(combinedFont);
        canvas.DrawBitmap(font, 0, 0);
        canvas.DrawBitmap(fontAddendum, 0, font.Height);

        _assets[Filenames.Font] = combinedFont.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    public byte[] ReadAllBytes() => _assetData;
    public MemoryStream GetStream() => new(_assetData);
    public SKBitmap DecodeSKBitmap() => SKBitmap.Decode(_assetData);
    public T ReadJson<T>() => JsonSerializer.Deserialize<T>(_assetData);

    public SKBitmap DecodeSKBitmapTileData()
    {
        return DecodeSKBitmap(SKAlphaType.Unpremul);
    }

    public SKBitmap DecodeSKBitmap(SKAlphaType alphaType)
    {
        using var original = SKBitmap.Decode(_assetData);
        var bitmap = new SKBitmap(original.Width, original.Height, original.ColorType, alphaType);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(original, 0, 0);
        return bitmap;
    }
}