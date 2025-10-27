using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace z1.Render;

internal static class GraphicPalettes
{
    private static readonly int _paletteBmpWidth;
    private static readonly int _paletteBmpHeight;

    private static readonly byte[] _paletteBuf;
    private static readonly int _paletteStride;
    private static readonly uint[] _systemPalette;
    private static readonly uint[] _grayscalePalette;
    private static uint[] _activeSystemPalette;
    private static readonly byte[] _palettes;

    static GraphicPalettes()
    {
        _paletteBmpWidth = Math.Max(Global.PaletteLength, 16);
        _paletteBmpHeight = Math.Max(Global.PaletteCount, 16);
        _paletteStride = _paletteBmpWidth * Unsafe.SizeOf<SKColor>();
        var size = _paletteBmpWidth * _paletteStride * _paletteBmpHeight;
        _paletteBuf = new byte[size];

        _systemPalette = new uint[Global.SysPaletteLength];
        _grayscalePalette = new uint[Global.SysPaletteLength];
        _activeSystemPalette = _systemPalette;
        _palettes = new byte[Global.PaletteCount * Global.PaletteLength];
    }

    public static void LoadSystemPalette(uint[] colorsArgb8)
    {
        colorsArgb8.CopyTo(_systemPalette.AsSpan());

        for (var i = 0; i < Global.SysPaletteLength; i++)
        {
            _grayscalePalette[i] = _systemPalette[i & 0x30];
        }
    }

    public static SKColor GetSystemColor(int sysColor)
    {
        var argb8 = _activeSystemPalette[sysColor];
        return new SKColor(
            (byte)(argb8 >> 16 & 0xFF),
            (byte)(argb8 >> 8 & 0xFF),
            (byte)(argb8 >> 0 & 0xFF),
            (byte)(argb8 >> 24 & 0xFF)
        );
    }

    // TODO: this method has to consider the picture format
    public static void SetColor(Palette paletteIndex, int colorIndex, uint colorArgb8)
    {
        var y = (int)paletteIndex;
        var x = colorIndex;

        var line = MemoryMarshal.Cast<byte, uint>(_paletteBuf.AsSpan()[(y * _paletteStride)..]);
        line[x] = colorArgb8;
    }

    public static void SetColor(Palette paletteIndex, int colorIndex, int colorArgb8) => SetColor(paletteIndex, colorIndex, (uint)colorArgb8);

    public static void SetPalette(Palette paletteIndex, ReadOnlySpan<uint> colorsArgb8)
    {
        var y = (int)paletteIndex;
        var line = MemoryMarshal.Cast<byte, uint>(_paletteBuf.AsSpan()[(y * _paletteStride)..]);

        for (var x = 0; x < Global.PaletteLength; x++)
        {
            line[x] = colorsArgb8[x];
        }
    }

    public static void SetColorIndexed(Palette paletteIndex, int colorIndex, int sysColor)
    {
        uint colorArgb8 = 0;
        if (colorIndex != 0)
        {
            colorArgb8 = _activeSystemPalette[sysColor];
        }
        SetColor(paletteIndex, colorIndex, colorArgb8);
        GetPalette(paletteIndex, colorIndex) = (byte)sysColor;
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ImmutableArray<byte> sysColors)
    {
        SetPaletteIndexed(paletteIndex, sysColors.AsSpan());
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ReadOnlySpan<byte> sysColors)
    {
        ReadOnlySpan<uint> colorsArgb8 =
        [
            0,
            _activeSystemPalette[sysColors[1]],
            _activeSystemPalette[sysColors[2]],
            _activeSystemPalette[sysColors[3]],
        ];

        SetPalette(paletteIndex, colorsArgb8);
        var dest = GetPalette(paletteIndex);
        sysColors[..Global.PaletteLength].CopyTo(dest);
    }

    public static void SetPilePalette()
    {
        ReadOnlySpan<byte> palette = [0, 0x27, 0x06, 0x16];
        SetPaletteIndexed(Palette.SeaPal, palette);
    }

    public static ref byte GetPalette(Palette paletteIndex, int colorIndex) => ref _palettes[(int)paletteIndex * Global.PaletteLength + colorIndex];
    public static Span<byte> GetPalette(Palette paletteIndex) => MemoryMarshal.CreateSpan(ref _palettes[(int)paletteIndex * Global.PaletteLength], Global.PaletteLength);
    public static ReadOnlySpan<SKColor> GetPaletteColors(Palette palette)
    {
        var paletteY = (int)palette * _paletteBmpWidth;
        return MemoryMarshal.Cast<byte, SKColor>(_paletteBuf.AsSpan())[paletteY..(paletteY + 4)];
    }

    public static void SwitchSystemPalette(uint[] newSystemPalette)
    {
        if (newSystemPalette == _activeSystemPalette) return;

        _activeSystemPalette = newSystemPalette;

        for (var i = 0; i < Global.PaletteCount; i++)
        {
            var sysColors = GetPalette((Palette)i);
            ReadOnlySpan<uint> colorsArgb8 =
            [
                0,
                _activeSystemPalette[sysColors[1]],
                _activeSystemPalette[sysColors[2]],
                _activeSystemPalette[sysColors[3]],
            ];
            SetPalette((Palette)i, colorsArgb8);
        }
    }

    public static void EnableGrayscale()
    {
        SwitchSystemPalette(_grayscalePalette);
    }

    public static void DisableGrayscale()
    {
        SwitchSystemPalette(_systemPalette);
    }

    public static void UpdatePalettes()
    {
        // I'm leaving all calls into UpdatePalettes() in the code for now.
        // It's IRL something the Graphics handlers should be handling, ie, if there's a recomputation step that
        // has to be done. We'd likely implement this by setting a flag inside GraphicPalettes that is then detected
        // by the actual graphics code when it runs. Clears the flag and does the recompute then.
        // But then, just make the Set* functions above set a flag?
    }
}