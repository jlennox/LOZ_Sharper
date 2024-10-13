/*
   Copyright 2016 Aldo J. Nunez

   Licensed under the Apache License, Version 2.0.
   See the LICENSE text file for details.
*/

// This file has been modified by Joseph Lennox 2024

global using z1.Common;
global using z1.Common.Data;
global using z1.Common.IO;

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
#pragma warning disable CA1416

namespace ExtractLoz;

internal class SpriteFrame
{
    public byte X;
    public byte Y;
    public byte Flags;
}

internal class SpriteAnim
{
    public byte Width;
    public byte Height;
    public SpriteFrame[] Frames;
}

public partial class LozExtractor
{
    private delegate void Extractor(Options options);

    private static byte[] RomMd5UProg0 =
    {
        0xD9, 0xA1, 0x63, 0x1D, 0x5C, 0x32, 0xD3, 0x55,
        0x94, 0xB9, 0x48, 0x48, 0x62, 0xA2, 0x6C, 0xBA
    };

    private const int PrimarySquareTable = 0x1697C + 16;
    private const int SecondarySquareTable = 0x169B4 + 16;
    private const int SecretSquareTable = 0x16976 + 16;
    private const int UnderworldSquareTable = 0x16718 + 16;
    private const int OWTileCHR = 0xC93B + 16;
    private const int UWTileCHR = 0xC11B + 16;
    private const int Misc1CHR = 0x877F + 16;
    private const int Misc2CHR = 0x8E7F + 16;

    private const int TileSize = 16;

    private static byte[] tileBuf = new byte[TileSize];

    public static Dictionary<string, byte[]> Extract(string[] args)
    {
        var options = Options.Parse(args);

        if (options.Error != null)
        {
            Console.Error.WriteLine(options.Error);
            return null;
        }

        if (options.Function == null)
        {
            Console.WriteLine("Nothing to work on.");
            return null;
        }

        CheckSupportedRom(options);

        Dictionary<string, Extractor> extractorMap = new Dictionary<string, Extractor> {
            { "text", ExtractTextBundle },
            { "overworldtiles", ExtractOverworldBundle },
            { "underworldtiles", ExtractUnderworldBundle },
            { "sprites", ExtractSpriteBundle },
            { "sound", ExtractSound }
        };

        foreach (var pair in extractorMap)
        {
            Console.WriteLine("Extracting {0} ...", pair.Key);
            pair.Value(options);
        }

        ExtractTiledMaps(options);

        return options.Files;
    }

    public enum RomCheckResult
    {
        Valid,
        FileNotFound,
        NotNesRom,
        NotCorrectVersion,
    }

    public const string CorrectFilename = "Legend of Zelda, The (U) (PRG0) [!].nes";

    public static RomCheckResult CheckRomFile(string file)
    {
        if (!File.Exists(file))
        {
            return RomCheckResult.FileNotFound;
        }

        byte[] romImage = File.ReadAllBytes(file);
        byte[] hash;

        if (romImage.Length < 0x20010 ||
            romImage[0] != 'N' || romImage[1] != 'E' || romImage[2] != 'S' || romImage[3] != 0x1A)
        {
            return RomCheckResult.NotNesRom;
        }

        using (var hashAlgo = MD5.Create())
        {
            hash = hashAlgo.ComputeHash(romImage, 0x10, romImage.Length - 0x10);
        }

        if (!AreHashesEqual(hash, RomMd5UProg0))
        {
            return RomCheckResult.NotCorrectVersion;
        }

        return RomCheckResult.Valid;

        bool AreHashesEqual(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }

    private static void CheckSupportedRom(Options options)
    {
        var result = CheckRomFile(options.RomPath);
        switch (result)
        {
            case RomCheckResult.NotCorrectVersion: throw new Exception("ROM is not supported. Pass the (U) (PRG0) version.");
            case RomCheckResult.NotNesRom: throw new Exception("Input file is not an NES ROM.");
            case RomCheckResult.FileNotFound: throw new FileNotFoundException("ROM file not found");
        }
    }

    private static void ExtractOverworldBundle(Options options)
    {
        ExtractOverworldTiles(options);
        ExtractOverworldTileAttrs(options);
        ExtractOverworldMap(options);
        ExtractOverworldMapAttrs(options);
        ExtractOverworldMapSparseAttrs(options);
        ExtractOverworldInfo(options);
        ExtractOverworldInfoEx(options);
        ExtractObjLists(options);
    }

    private static void ExtractUnderworldBundle(Options options)
    {
        ExtractUnderworldTiles(options);
        ExtractUnderworldTileAttrs(options);
        ExtractUnderworldMap(options);
        ExtractUnderworldMapAttrs(options);
        ExtractUnderworldInfo(options);
        ExtractUnderworldCellarMap(options);
        ExtractUnderworldCellarTiles(options);
        ExtractUnderworldCellarTileAttrs(options);
    }

    private static void ExtractSpriteBundle(Options options)
    {
        ExtractSystemPalette(options);
        ExtractSprites(options);
        ExtractOWSpriteVRAM(options);
    }

    private static void ExtractTextBundle(Options options)
    {
        ExtractFont(options);
        ExtractText(options);
        ExtractCredits(options);
    }

    private static void ExtractSound(Options options)
    {
        ExtractSounds(options, "Songs");
        ExtractSounds(options, "Effects");
    }

    private class SoundItem
    {
        public short Track;
        public short Begin;
        public short End;
        public byte Slot;
        public byte Priority;
        public byte Flags;
        public string Filename;

        public static SoundItem ConvertFields(string[] fields)
        {
            SoundItem item = new SoundItem();
            item.Track = short.Parse(fields[0]);
            item.Begin = short.Parse(fields[1]);
            item.End = short.Parse(fields[2]);
            item.Slot = byte.Parse(fields[3]);
            item.Priority = byte.Parse(fields[4]);
            item.Flags = byte.Parse(fields[5]);
            item.Filename = fields[6];
            return item;
        }

        public SongInformation ToSongInformation()
        {
            return new SongInformation
            {
                Track = Track,
                Start = Begin,
                End = End,
                Slot = Slot,
                Priority = Priority,
                Flags = (SoundFlags)Flags,
                Filename = Filename,
            };
        }
    }

    private class NsfItem
    {
        public int Offset;
        public int Length;

        public static NsfItem ConvertFields(string[] fields)
        {
            NsfItem item = new NsfItem();
            item.Offset = int.Parse(fields[0], System.Globalization.NumberStyles.HexNumber);
            item.Length = int.Parse(fields[1], System.Globalization.NumberStyles.HexNumber);
            return item;
        }
    }

    private static void ExtractSounds(Options options, string tableFileBase)
    {
        byte[] nsfImage = BuildMemoryNsf(options);

        var songs = new List<SongInformation>();
        using var inStream = GetResourceStream("ExtractLoz.Data." + tableFileBase + ".csv");
        var items = DatafileReader.ReadTable(inStream, SoundItem.ConvertFields);
        foreach (var item in items)
        {
            ExtractSoundFile(options, item, nsfImage);
            songs.Add(item.ToSongInformation());
        }

        options.AddJson(tableFileBase + ".json", songs.ToArray());
    }

    private static void ExtractSoundFile(Options options, SoundItem item, byte[] nsfImage)
    {
        const int SampleRate = 44100;
        const double SampleRateMs = SampleRate / 1000.0;
        const double MillisecondsAFrame = 1000.0 / 60.0;

        string outPath = "Audio/" + item.Filename;
        using (var tempFile = options.AddTempFile(outPath))
        using (var emu = new ExtractNsf.NsfEmu())
        using (var waveWriter = new ExtractNsf.WaveWriter(SampleRate, tempFile.TempFilename))
        {
            emu.SampleRate = SampleRate;
            emu.LoadMem(nsfImage, nsfImage.Length);
            emu.StartTrack(item.Track);

            waveWriter.EnableStereo();

            short[] buffer = new short[1024];
            int limit = (int)(item.End * MillisecondsAFrame);
            while (emu.Tell < limit)
            {
                int count = buffer.Length;
                int samplesRem = (int)(SampleRateMs * (limit - emu.Tell));
                if (samplesRem < count)
                {
                    count = (int)((samplesRem + 1) & 0xFFFFFFFE);
                }
                emu.Play(count, buffer);
                waveWriter.Write(buffer, count, 1);
            }
        }
    }

    private static byte[] BuildMemoryNsf(Options options)
    {
        List<NsfItem> nsfItems;
        int totalRomSectionSize = 0;

        using (var specStream = GetResourceStream("ExtractLoz.Data.NsfSpec.csv"))
        {
            nsfItems = DatafileReader.ReadTable(specStream, NsfItem.ConvertFields);
        }

        foreach (var item in nsfItems)
        {
            totalRomSectionSize += item.Length;
        }

        var romImage = File.ReadAllBytes(options.RomPath);

        using (var headerStream = GetResourceStream("ExtractLoz.Data.NsfHeader.bin"))
        using (var footerStream = GetResourceStream("ExtractLoz.Data.NsfFooter.bin"))
        {
            int nsfSize = totalRomSectionSize + (int)headerStream.Length + (int)footerStream.Length;
            var nsfImage = new byte[nsfSize];
            int offset = 0;

            headerStream.Read(nsfImage, 0, (int)headerStream.Length);
            offset += (int)headerStream.Length;

            foreach (var item in nsfItems)
            {
                Array.Copy(romImage, item.Offset, nsfImage, offset, item.Length);
                offset += item.Length;
            }

            footerStream.Read(nsfImage, offset, (int)footerStream.Length);

            return nsfImage;
        }
    }

    private static void ExtractSystemPalette(Options options)
    {
        options.AddJson("Palette.json", DefaultSystemPalette.Colors);
    }

    private static void ExtractFont(Options options)
    {
        using (var reader = options.GetBinaryReader())
        {
            Bitmap bmp = new Bitmap(16 * 8, 16 * 8);
            Color[] colors = GetPaletteStandInColors();
            int x = 0;
            int y = 0;

            reader.BaseStream.Position = Misc1CHR;

            for (int i = 0; i < 0x70; i++)
            {
                DrawTile(reader, bmp, colors, x, y);

                x += 8;
                if (x >= bmp.Width)
                {
                    x = 0;
                    y += 8;
                }
            }

            SeekBgTile(reader, UWTileCHR, 0xE2);
            x = 0;

            for (int i = 0; i < 16; i++)
            {
                DrawTile(reader, bmp, colors, x, 0x68);
                x += 8;
            }

            int t = 0xE5;
            x = 0x28;
            y = 0x70;

            for (int i = 0; i < 27; i++)
            {
                SeekBgTile(reader, OWTileCHR, t);
                DrawTile(reader, bmp, colors, x, y);

                t++;
                x += 8;
                if (x >= bmp.Width)
                {
                    x = 0;
                    y += 8;
                }
            }

            SeekUWSpriteTile(reader, UW127SpriteCHR, 0x3E);
            DrawTile(reader, bmp, colors, 0, 0x70);

            options.AddFile("font.png", bmp, ImageFormat.Png);
        }
    }

    private static readonly List<string> _gameStrings = new();
    private static readonly List<string> _caveStrings = new();

    private static string[] ExtractText(Options options)
    {
        const int TextPtrs = 0x4000 + 16;

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = TextPtrs;
            ushort[] listPtrs = new ushort[38];

            for (int i = 0; i < listPtrs.Length; i++)
            {
                listPtrs[i] = reader.ReadUInt16();
            }

            var heap = reader.ReadBytes(0x556);

            // 0..9     : '0'..'9'
            // $A..$23  : 'A'..'Z'
            // $24..$27 : ' ', justifying-space, ?, ?
            // $28..$2F : ',', '!', '\'', '&', '.', '"', '?', '-'
            // bits 6,7 : end of string
            // bit 7    : go to second line
            // bit 6    : go to third line

            using (var writer = new BinaryWriter(options.AddStream("text.tab")))
            {
                writer.Write((ushort)listPtrs.Length);

                for (int i = 0; i < listPtrs.Length; i++)
                {
                    ushort ptr = (ushort)(listPtrs[i] - listPtrs[0]);
                    writer.Write(ptr);

                    var str = heap[ptr..];
                    for (var findEnd = 0; findEnd < str.Length; ++findEnd)
                    {
                        var chr = str[findEnd];
                        if ((chr & 0xC0) == 0xC0)
                        {
                            // str[findEnd] = (byte)(chr & 0x3F);
                            str = str[..(findEnd + 1)];
                            _gameStrings.Add(GameString.FromBytes(str));
                            break;
                        }
                    }
                }

                writer.Write(heap);

                Utility.PadStream(writer.BaseStream);
            }
        }

        options.AddJson("text.json", _gameStrings);

        return _gameStrings.ToArray();
    }

    private static void ExtractCredits(Options options)
    {
        const int TextPtrs = 0xAC2E + 16;

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = TextPtrs;
            byte[] listPtrsLo = reader.ReadBytes(0x17);
            byte[] listPtrsHi = reader.ReadBytes(0x17);
            ushort[] listPtrs = new ushort[0x17];

            for (int i = 0; i < listPtrs.Length; i++)
            {
                listPtrs[i] = (ushort)(listPtrsLo[i] | (listPtrsHi[i] << 8));
            }

            var heap = reader.ReadBytes(0x19E);

            // Each entry is defined as follows:
            //  byte 0  : length
            //  byte 1  : first column (in chars)
            //  byte 2..: text


            var text = new List<string>();

            using (var writer = new BinaryWriter(options.AddStream("credits.tab")))
            {
                writer.Write((ushort)listPtrs.Length);

                for (int i = 0; i < listPtrs.Length; i++)
                {
                    ushort ptr = (ushort)(listPtrs[i] - listPtrs[0]);
                    writer.Write(ptr);

                    var strBin = heap[ptr..];
                    var str = GameString.FromBytes(strBin);
                }

                writer.Write(heap);

                Utility.PadStream(writer.BaseStream);
            }
        }

        const int LineBitmap = 0xAC22 + 16;

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = LineBitmap;
            byte[] bytes = reader.ReadBytes(12);
            options.AddFile("creditsLinesBmp.dat", bytes);
        }
    }

    private static void ExtractUnderworldCellarTiles(Options options)
    {
        using (var reader = options.GetBinaryReader())
        {
            Bitmap bmp = new Bitmap(16 * 16, 4 * 16);

            ExtractUnderworldCellarTilesDebug(reader, bmp);

            options.AddFile("underworldCellarTilesDebug.png", bmp, ImageFormat.Png);

            ExtractUnderworldCellarMobs(options, reader);
        }
    }

    private const int UWCellarPrimarySquareTable = 0x1697C + 16;
    private const int UWCellarSecondarySquareTable = 0x169B4 + 16;

    private static (byte[] Primary, byte[] Secondary) ExtractUnderworldCellarMobs(Options options, BinaryReader reader)
    {
        reader.BaseStream.Position = UWCellarPrimarySquareTable;
        var primaries = reader.ReadBytes(56);

        for (int i = 0; i < 16; i++)
        {
            primaries[i] = 0xFF;
        }

        // We don't need to translate secrets into primaries.

        // WriteListFile(options, "uwCellarPrimaryMobs.list", primaries);

        reader.BaseStream.Position = UWCellarSecondarySquareTable;
        var secondaries = reader.ReadBytes(16 * 4);       // 16 squares, 4 8x8 tiles each

        // WriteListFile(options, "uwCellarSecondaryMobs.list", secondaries);

        return (primaries, secondaries);
    }

    private static void ExtractUnderworldCellarTilesDebug(BinaryReader reader, Bitmap bmp)
    {
        reader.BaseStream.Position = UWCellarPrimarySquareTable;
        var primaries = reader.ReadBytes(56);

        reader.BaseStream.Position = UWCellarSecondarySquareTable;
        var secondaries = reader.ReadBytes(16 * 4);       // 16 squares, 4 8x8 tiles each

        // Underworld cellar rooms are layed out like Overworld rooms, but there are no secret tiles.

        int x = 0;
        int y = 0;

        Color[] colors = GetPaletteStandInColors();

        byte[] tileIndexes = new byte[4];

        for (int i = 0; i < 56; i++)
        {
            if (i < 16)
            {
                var primary = i * 4;

                for (int j = 0; j < 4; j++)
                    tileIndexes[j] = secondaries[primary + j];

                DrawBgSquare(reader, bmp, colors, x, y, UWTileCHR, tileIndexes);
            }
            else
            {
                var primary = primaries[i];

                // We don't need to translate secrets into primaries.

                for (int j = 0; j < 4; j++)
                    tileIndexes[j] = (byte)(primary + j);

                DrawBgSquare(reader, bmp, colors, x, y, UWTileCHR, tileIndexes);
            }

            if (i % 16 == 15)
            {
                x = 0;
                y += 16;
            }
            else
                x += 16;
        }
    }

    private static void ExtractUnderworldTiles(Options options)
    {
        using (var reader = options.GetBinaryReader())
        {
            Bitmap bmp = new Bitmap(16 * 16, 4 * 16);

            ExtractUnderworldTilesDebug(reader, bmp);

            options.AddFile("underworldTilesDebug.png", bmp, ImageFormat.Png);
            bmp.Dispose();

            bmp = new Bitmap(16 * 8, 16 * 8);

            ExtractUnderworldTiles(reader, bmp);

            options.AddFile("underworldTiles.png", bmp, ImageFormat.Png);
            bmp.Dispose();

            bmp = new Bitmap(16 * 16, 11 * 16);

            ExtractUnderworldWalls(reader, bmp);

            options.AddFile("underworldWalls.png", bmp, ImageFormat.Png);
            bmp.Dispose();

            bmp = new Bitmap(16 * 16, 16 * 16);

            ExtractUnderworldDoors(reader, bmp);

            options.AddFile("underworldDoors.png", bmp, ImageFormat.Png);
            bmp.Dispose();

            ExtractUnderworldMobs(options, reader);
            ExtractUnderworldTileBehaviors(options, reader);
        }
    }

    private static ListResource<byte> ExtractUnderworldMobs(Options options, BinaryReader reader)
    {
        reader.BaseStream.Position = UnderworldSquareTable;
        var primaries = reader.ReadBytes(8);

        return GetListFile<byte>(primaries); // "uwPrimaryMobs.list"
    }

    private static void ExtractUnderworldTilesDebug(BinaryReader reader, Bitmap bmp)
    {
        reader.BaseStream.Position = UnderworldSquareTable;
        var primaries = reader.ReadBytes(8);

        int x = 0;
        int y = 0;

        Color[] colors = GetPaletteStandInColors();
        byte[] tileIndexes = new byte[4];

        for (int i = 0; i < 8; i++)
        {
            var primary = primaries[i];

            if (primary < 0x70 || primary > 0xF2)
            {
                for (int j = 0; j < 4; j++)
                    tileIndexes[j] = primary;

                DrawBgSquare(reader, bmp, colors, x, y, UWTileCHR, tileIndexes);
            }
            else
            {
                for (int j = 0; j < 4; j++)
                    tileIndexes[j] = (byte)(primary + j);

                DrawBgSquare(reader, bmp, colors, x, y, UWTileCHR, tileIndexes);
            }

            if (i % 16 == 15)
            {
                x = 0;
                y += 16;
            }
            else
                x += 16;
        }
    }

    private static void ExtractUnderworldTiles(BinaryReader reader, Bitmap bmp)
    {
        Color[] colors = GetPaletteStandInColors();

        for (int t = 0; t < 256; t++)
        {
            SeekBgTile(reader, UWTileCHR, t);
            DrawTile(reader, bmp, colors, t % 16 * 8, t / 16 * 8);
        }
    }

    private const int Walls = 0x15fa0 + 16;
    private const int WallTileCount = 78;

    private static byte[,] ExtractUnderworldWalls(BinaryReader reader, Bitmap bmp)
    {
        reader.BaseStream.Position = Walls;
        var wallTiles = reader.ReadBytes(WallTileCount);

        var colors = GetPaletteStandInColors();
        int row = 0;
        int col = 0;
        byte[,] map = new byte[22, 32];

        for (int i = 0; i < 78; i++)
        {
            if (wallTiles[i] != 0)
            {
                byte tile = wallTiles[i];

                map[row + 1, col + 1] = tile;

                tile = wallTiles[i];
                if (tile != 0xf5 && tile != 0xde)
                    tile++;
                map[10 - row + 10, col + 1] = tile;

                tile = wallTiles[i];
                if (tile == 0xde || tile == 0xdc)
                    tile++;
                else if (tile != 0xf5 && tile != 0xe0)
                    tile += 2;
                map[10 - row + 10, 12 - col + 16 + 2] = tile;

                tile = wallTiles[i];
                if (tile == 0xde || tile == 0xe0)
                    tile++;
                else if (tile != 0xf5 && tile != 0xdc)
                    tile += 3;
                map[row + 1, 12 - col + 16 + 2] = tile;
            }

            row++;
            if (row == 10 || wallTiles[i] == 0)
            {
                col++;
                row = 0;
            }
        }

        for (int i = 0; i < 32; i++)
        {
            map[0, i] = 0xF6;
            map[21, i] = 0xF6;
        }

        for (int i = 0; i < 20; i++)
        {
            map[i + 1, 0] = 0xF6;
            map[i + 1, 31] = 0xF6;
        }

        for (int r = 0; r < 22; r++)
        {
            for (int c = 0; c < 32; c++)
            {
                byte tile = map[r, c];
                if (tile != 0)
                {
                    SeekBgTile(reader, UWTileCHR, tile);
                    DrawTile(reader, bmp, colors, c * 8, r * 8);
                }
            }
        }

        using (var g = Graphics.FromImage(bmp))
        {
            g.FillRectangle(Brushes.Black, 224, 72, 32, 32);
            g.FillRectangle(Brushes.Black, 0, 72, 32, 32);
            g.FillRectangle(Brushes.Black, 112, 144, 32, 32);
            g.FillRectangle(Brushes.Black, 112, 0, 32, 32);
        }

        return map;
    }

    private static void ExtractUnderworldDoors(BinaryReader reader, Bitmap bmp)
    {
        const int EastWall = 0x15FEE + 16;
        const int WestWall = 0x1602A + 16;
        const int SouthWall = 0x16066 + 16;
        const int NorthWall = 0x160A2 + 16;

        reader.BaseStream.Position = EastWall;
        byte[] wallE = reader.ReadBytes(60);

        reader.BaseStream.Position = WestWall;
        byte[] wallW = reader.ReadBytes(60);

        reader.BaseStream.Position = SouthWall;
        byte[] wallS = reader.ReadBytes(60);

        reader.BaseStream.Position = NorthWall;
        byte[] wallN = reader.ReadBytes(60);

        var colors = GetPaletteStandInColors();
        int baseY = 0;
        bool transparent = false;
        int row;
        int col;
        int k;

        for (int layer = 0; layer < 2; layer++, baseY += 128)
        {
            k = 0;

            for (int i = 0; i < 5; i++)
            {
                int startX = i * 32;
                row = 0;
                col = 0;
                for (int j = 0; j < 12; j++, k++)
                {
                    int tile = 0;

                    if (!transparent || row > 0)
                    {
                        tile = wallS[k];
                        SeekBgTile(reader, UWTileCHR, tile);
                        DrawTile(reader, bmp, colors,
                            startX + col * 8, row * 8 + baseY + 0);
                    }

                    if (!transparent || row < 2)
                    {
                        tile = wallN[k];
                        SeekBgTile(reader, UWTileCHR, tile);
                        DrawTile(reader, bmp, colors,
                            startX + col * 8, row * 8 + baseY + 40);
                    }

                    row++;
                    if (row == 3)
                    {
                        col++;
                        row -= 3;
                    }
                }
            }

            for (int i = 0; i < 2; i++)
            {
                DrawUWBorderLineH(reader, bmp, colors, 0, i * 8 + baseY + 24, 20);
            }

            k = 0;

            for (int i = 0; i < 5; i++)
            {
                int startX = i * 32;
                row = 0;
                col = 0;
                for (int j = 0; j < 12; j++, k++)
                {
                    int tile;

                    if (!transparent || col > 0)
                    {
                        tile = wallE[k];
                        SeekBgTile(reader, UWTileCHR, tile);
                        DrawTile(reader, bmp, colors,
                            startX + col * 8, row * 8 + baseY + 64);
                    }

                    if (!transparent || col < 2)
                    {
                        tile = wallW[k];
                        SeekBgTile(reader, UWTileCHR, tile);
                        DrawTile(reader, bmp, colors,
                            startX + col * 8 + 8, row * 8 + baseY + 96);
                    }

                    row++;
                    if (row % 2 == 0)
                    {
                        col++;
                        row -= 2;

                        if (col == 3)
                        {
                            col = 0;
                            row = 2;
                        }
                    }
                }
                DrawUWBorderLineV(reader, bmp, colors, startX + 24, baseY + 64, 4);
                DrawUWBorderLineV(reader, bmp, colors, startX + 0, baseY + 96, 4);
            }
            transparent = true;
        }
    }

    private static void DrawUWBorderLineH(
        BinaryReader reader, Bitmap bmp, Color[] colors, int x, int y, int length)
    {
        for (int j = 0; j < length; j++)
        {
            SeekBgTile(reader, UWTileCHR, 0xF6);
            DrawTile(reader, bmp, colors, x + j * 8, y);
        }
    }

    private static void DrawUWBorderLineV(
        BinaryReader reader, Bitmap bmp, Color[] colors, int x, int y, int length)
    {
        for (int j = 0; j < length; j++)
        {
            SeekBgTile(reader, UWTileCHR, 0xF6);
            DrawTile(reader, bmp, colors, x, y + j * 8);
        }
    }

    private static void ExtractOverworldTiles(Options options)
    {
        using (var reader = options.GetBinaryReader())
        {
            Bitmap bmp = new Bitmap(16 * 16, 4 * 16);

            ExtractOverworldTilesDebug(reader, bmp);

            options.AddFile("overworldTilesDebug.png", bmp, ImageFormat.Png);
            bmp.Dispose();

            bmp = new Bitmap(16 * 8, 16 * 8);
            ExtractOverworldTiles(reader, bmp);
            options.AddFile("overworldTiles.png", bmp, ImageFormat.Png);
            bmp.Dispose();

            ExtractOverworldMobs(options, reader);
            ExtractOverworldTileBehaviors(options, reader);
        }
    }

    private static (byte[] Primary, byte[] Secondary) ExtractOverworldMobs(Options options, BinaryReader reader)
    {
        reader.BaseStream.Position = PrimarySquareTable;
        var primaries = reader.ReadBytes(56);

        reader.BaseStream.Position = SecretSquareTable;
        var secrets = reader.ReadBytes(6);                // 1 byte refs to primary squares

        for (int i = 0; i < 16; i++)
        {
            primaries[i] = 0xFF;
        }

        for (int i = 16; i < 56; i++)
        {
            int primary = primaries[i];
            if (primary >= 0xE5 && primary <= 0xEA)
                primaries[i] = secrets[primary - 0xE5];
        }

        // WriteListFile(options, "owPrimaryMobs.list", primaries);

        reader.BaseStream.Position = SecondarySquareTable;
        var secondaries = reader.ReadBytes(16 * 4);       // 16 squares, 4 8x8 tiles each

        // WriteListFile(options, "owSecondaryMobs.list", secondaries);

        return (primaries, secondaries);
    }

    private static void ExtractOverworldTilesDebug(BinaryReader reader, Bitmap bmp)
    {
        reader.BaseStream.Position = PrimarySquareTable;
        var primaries = reader.ReadBytes(56);

        reader.BaseStream.Position = SecondarySquareTable;
        var secondaries = reader.ReadBytes(16 * 4);       // 16 squares, 4 8x8 tiles each

        reader.BaseStream.Position = SecretSquareTable;
        var secrets = reader.ReadBytes(6);                // 1 byte refs to primary squares

        int x = 0;
        int y = 0;

        // Even though the graphics system can make a texture smaller than 16x16,
        // the texture is really 16x16 underneath. Use this size to calculate the
        // colors.

        Color[] colors = GetPaletteStandInColors();

        byte[] tileIndexes = new byte[4];

        for (int i = 0; i < 56; i++)
        {
            GetOverworldMobTiles(i, primaries, secondaries, secrets, tileIndexes);

            DrawBgSquare(reader, bmp, colors, x, y, OWTileCHR, tileIndexes);

            if (i % 16 == 15)
            {
                x = 0;
                y += 16;
            }
            else
                x += 16;
        }
    }

    private static void GetOverworldMobTiles(
        int i, byte[] primaries, byte[] secondaries, byte[] secrets, byte[] tileIndexes)
    {
        if (i < 16)
        {
            var primary = i * 4;

            for (int j = 0; j < 4; j++)
                tileIndexes[j] = secondaries[primary + j];
        }
        else
        {
            var primary = primaries[i];

            if (primary >= 0xE5 && primary <= 0xEA)
            {
                primary = secrets[primary - 0xE5];
            }

            for (int j = 0; j < 4; j++)
                tileIndexes[j] = (byte)(primary + j);
        }
    }

    private static void ExtractOverworldTiles(BinaryReader reader, Bitmap bmp)
    {
        Color[] colors = GetPaletteStandInColors();

        for (int t = 0; t < 256; t++)
        {
            SeekBgTile(reader, OWTileCHR, t);
            DrawTile(reader, bmp, colors, t % 16 * 8, t / 16 * 8);
        }
    }

    private static bool IsWalkable(int t)
    {
        // DF D5 D2 CC AD AC 9C 91 8D
        // < 89

        switch (t)
        {
            case 0xDF:
            case 0xD5:
            case 0xD2:
            case 0xCC:
            case 0xAD:
            case 0xAC:
            case 0x9C:
            case 0x91:
            case 0x8D:
                return true;

            default:
                return t < 0x89;
        }
    }

    private static TileAction GetAction(int t)
    {
        switch (t)
        {
            case 0x0B: return TileAction.Raft;
            case 0x0C:
            case 0x0F:
                return TileAction.Cave;
            case 0x12: return TileAction.Stairs;
            case 0x14: return TileAction.Ghost;
            case 0x26: return TileAction.Push;
            case 0x27: return TileAction.Bomb;
            case 0x28: return TileAction.Burn;
            case 0x29: return TileAction.PushHeadstone;
            case 0x2A: return TileAction.Armos;
            case 0x2B: return TileAction.Armos;
            case 0x2C: return TileAction.Armos;
            default:
                if (t >= 0x5 && t <= 0x9
                    || t >= 0x15 && t <= 0x18)
                {
                    return TileAction.Ladder;
                }
                return TileAction.None;
        }
    }

    // ActionTriggers
    // - None
    // - Init
    // - Push
    // - Touch
    // - Cover

    private class TileAttribute
    {
    }

    private static byte[] ExtractOverworldTileAttrs(Options options)
    {
        int[] tileAttrs = new int[56];
        var tileActions = new TileAction[56];

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = PrimarySquareTable;
            var primaries = reader.ReadBytes(56);

            reader.BaseStream.Position = SecondarySquareTable;
            var secondaries = reader.ReadBytes(16 * 4);       // 16 squares, 4 8x8 tiles each

            reader.BaseStream.Position = SecretSquareTable;
            var secrets = reader.ReadBytes(6);                // 1 byte refs to primary squares

            for (int i = 0; i < 56; i++)
            {
                int attr = 0;
                int walkBit = 1;

                if (i < 16)
                {
                    var primary = i * 4;

                    for (int j = 0; j < 4; j++)
                    {
                        var t = secondaries[primary + j];
                        if (!IsWalkable(t))
                            attr |= walkBit;
                        walkBit <<= 1;
                    }
                }
                else
                {
                    var primary = primaries[i];

                    if (primary >= 0xE5 && primary <= 0xEA)
                    {
                        primary = secrets[primary - 0xE5];
                    }

                    for (int j = 0; j < 4; j++)
                    {
                        var t = (byte)(primary + j);
                        if (!IsWalkable(t))
                            attr |= walkBit;
                        walkBit <<= 1;
                    }
                }

                tileAttrs[i] = attr;
                tileActions[i] = GetAction(i);
            }
        }

        var attrs = new byte[56];

        // using (var writer = new BinaryWriter(options.AddStream("overworldTileAttrs.dat")))
        {
            for (int i = 0; i < 56; i++)
            {
                byte value = (byte)(tileAttrs[i] | ((int)tileActions[i] << 4));
                attrs[i] = value;
                // writer.Write(value);
            }

            // Utility.PadStream(writer.BaseStream);
        }

        return attrs;
    }

    private static ReadOnlySpan<byte> ExtractOverworldTileBehaviors(Options options, BinaryReader reader)
    {
        reader.BaseStream.Position = PrimarySquareTable;
        var primaries = reader.ReadBytes(56);

        reader.BaseStream.Position = SecondarySquareTable;
        var secondaries = reader.ReadBytes(16 * 4);       // 16 squares, 4 8x8 tiles each

        reader.BaseStream.Position = SecretSquareTable;
        var secrets = reader.ReadBytes(6);                // 1 byte refs to primary squares

        var tileToBehaviorMap = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            if (IsWalkable(i))
                tileToBehaviorMap[i] = (byte)TileBehavior.GenericWalkable;
            else
                tileToBehaviorMap[i] = (byte)TileBehavior.GenericSolid;
        }

        byte[] tileIndexes = new byte[4];

        for (int i = 0; i < 56; i++)
        {
            TileBehavior behavior = TileBehavior.None;
            var action = GetAction(i);
            GetOverworldMobTiles(i, primaries, secondaries, secrets, tileIndexes);

            if (action == TileAction.Ladder)
                behavior = TileBehavior.Water;
            else if (action == TileAction.Cave)
                behavior = TileBehavior.Cave;
            else if (action == TileAction.Stairs)
                behavior = TileBehavior.Stairs;
            else if (action == TileAction.Ghost)
                behavior = TileBehavior.Ghost0;
            else if (action == TileAction.Armos)
                behavior = TileBehavior.Armos0;
            else
                continue;

            bool different = false;

            for (int j = 1; j < 4; j++)
            {
                different = IsWalkable(tileIndexes[0]) ^ IsWalkable(tileIndexes[j]);
                if (different)
                    break;
            }

            for (int j = 0; j < 4; j++)
            {
                int t = tileIndexes[j];
                if (!different || !IsWalkable(t))
                {
                    tileToBehaviorMap[t] = (byte)behavior;
                }
            }
        }

        tileToBehaviorMap[0x74] = (byte)TileBehavior.SlowStairs;
        tileToBehaviorMap[0x75] = (byte)TileBehavior.SlowStairs;

        tileToBehaviorMap[0x84] = (byte)TileBehavior.Sand;
        tileToBehaviorMap[0x85] = (byte)TileBehavior.Sand;
        tileToBehaviorMap[0x86] = (byte)TileBehavior.Sand;
        tileToBehaviorMap[0x87] = (byte)TileBehavior.Sand;

        // For stairs, only treat the top left corner specially.
        tileToBehaviorMap[0x71] = (byte)TileBehavior.GenericWalkable;
        tileToBehaviorMap[0x72] = (byte)TileBehavior.GenericWalkable;
        tileToBehaviorMap[0x73] = (byte)TileBehavior.GenericWalkable;

        // options.AddFile("owTileBehaviors.dat", tileToBehaviorMap);
        return ListResource<byte>.LoadList(tileToBehaviorMap, World.TileTypes);
    }

    private static bool IsUnderworldWalkable(int t)
    {
        if (t < 0x78)
            return true;
        return false;
    }

    private static TileAction GetUnderworldAction(int t)
    {
        switch (t)
        {
            case 4: return TileAction.Stairs;
            case 6: return TileAction.Ladder;
            default: return TileAction.None;
        }
    }

    private static byte[] ExtractUnderworldTileAttrs(Options options)
    {
        int[] tileAttrs = new int[9];
        var tileActions = new TileAction[9];

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = UnderworldSquareTable;
            var primaries = reader.ReadBytes(8);

            for (int i = 0; i < 8; i++)
            {
                int attr = 0;
                int walkBit = 1;
                var primary = primaries[i];

                if (primary < 0x70 || primary > 0xF2)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        var t = primary;
                        if (!IsUnderworldWalkable(t))
                            attr |= walkBit;
                        walkBit <<= 1;
                    }
                }
                else
                {
                    for (int j = 0; j < 4; j++)
                    {
                        var t = (byte)(primary + j);
                        if (!IsUnderworldWalkable(t))
                            attr |= walkBit;
                        walkBit <<= 1;
                    }
                }

                tileAttrs[i] = attr;
                tileActions[i] = GetUnderworldAction(i);
            }
        }

        tileAttrs[8] = 0xF;
        tileActions[8] = TileAction.None;


        var fixedAttrs = new byte[tileAttrs.Length];

        // using (var writer = new BinaryWriter(options.AddStream("underworldTileAttrs.dat")))
        {
            for (int i = 0; i < tileAttrs.Length; i++)
            {
                byte value = (byte)(tileAttrs[i] | ((int)tileActions[i] << 4));
                fixedAttrs[i] = value;
                // writer.Write(value);
            }

            // Utility.PadStream(writer.BaseStream);
        }

        return fixedAttrs;
    }

    private static byte[] ExtractUnderworldCellarTileAttrs(Options options)
    {
        int[] tileAttrs = new int[56];
        var tileActions = new TileAction[56];

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = UWCellarPrimarySquareTable;
            var primaries = reader.ReadBytes(56);

            reader.BaseStream.Position = UWCellarSecondarySquareTable;
            var secondaries = reader.ReadBytes(16 * 4);       // 16 squares, 4 8x8 tiles each

            // Underworld cellar rooms are layed out like Overworld rooms, but there are no secrets.

            for (int i = 0; i < 56; i++)
            {
                int attr = 0;
                int walkBit = 1;

                if (i < 16)
                {
                    var primary = i * 4;

                    for (int j = 0; j < 4; j++)
                    {
                        var t = secondaries[primary + j];
                        if (!IsUnderworldWalkable(t))
                            attr |= walkBit;
                        walkBit <<= 1;
                    }
                }
                else
                {
                    var primary = primaries[i];

                    // We don't need to translate secrets into primaries.

                    for (int j = 0; j < 4; j++)
                    {
                        var t = (byte)(primary + j);
                        if (!IsUnderworldWalkable(t))
                            attr |= walkBit;
                        walkBit <<= 1;
                    }
                }

                tileAttrs[i] = attr;
                // leave tileAction as None
            }
        }

        var fixedAttr = new byte[56];
        // using (var writer = new BinaryWriter(options.AddStream("underworldCellarTileAttrs.dat")))
        {
            for (int i = 0; i < 56; i++)
            {
                byte value = (byte)(tileAttrs[i] | ((int)tileActions[i] << 4));
                fixedAttr[i] = value;
                // writer.Write(value);
            }

            // Utility.PadStream(writer.BaseStream);
        }

        return fixedAttr;
    }

    private static ReadOnlySpan<byte> ExtractUnderworldTileBehaviors(Options options, BinaryReader reader)
    {
        var tileToBehaviorMap = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            if (IsUnderworldWalkable(i))
                tileToBehaviorMap[i] = (byte)TileBehavior.GenericWalkable;
            else
                tileToBehaviorMap[i] = (byte)TileBehavior.GenericSolid;
        }

        reader.BaseStream.Position = Walls;
        var wallTiles = reader.ReadBytes(WallTileCount);

        for (int i = 0; i < WallTileCount; i++)
        {
            int tile = wallTiles[i];
            if (tile == 0)
                continue;

            tileToBehaviorMap[tile] = (byte)TileBehavior.Wall;

            tile = wallTiles[i];
            if (tile != 0xf5 && tile != 0xde)
                tile++;
            tileToBehaviorMap[tile] = (byte)TileBehavior.Wall;

            tile = wallTiles[i];
            if (tile == 0xde || tile == 0xdc)
                tile++;
            else if (tile != 0xf5 && tile != 0xe0)
                tile += 2;
            tileToBehaviorMap[tile] = (byte)TileBehavior.Wall;

            tile = wallTiles[i];
            if (tile == 0xde || tile == 0xe0)
                tile++;
            else if (tile != 0xf5 && tile != 0xdc)
                tile += 3;
            tileToBehaviorMap[tile] = (byte)TileBehavior.Wall;
        }

        // Door frames
        for (int i = 0x78; i < 0x8C; i++)
        {
            tileToBehaviorMap[i] = (byte)TileBehavior.Wall;
        }

        // $8C - $93 are bombed holes in the wall.
        // $98 - $A7 are key doors.
        // $A8 - $AF are shutters.

        tileToBehaviorMap[0xF6] = (byte)TileBehavior.Wall;

        tileToBehaviorMap[0xF4] = (byte)TileBehavior.Water;

        // For stairs, only treat the top left corner specially.
        tileToBehaviorMap[0x70] = (byte)TileBehavior.Stairs;

        // string outPath = "uwTileBehaviors.dat";
        // options.AddFile(outPath, tileToBehaviorMap);
        return ListResource<byte>.LoadList(tileToBehaviorMap, World.TileTypes);
    }

    private const int OWRoomCols = 0x15418 + 16;
    private const int OWColDir = 0x19D0F + 16;
    private const int OWColTables = 0x15BD8 + 16;

    private const int UWRoomCols = 0x160DE + 16;
    private const int UWColDir = 0x16704 + 16;
    private const int UWColTables = 0x162D6 + 16;

    private static MapLayout ExtractUnderworldMap(Options options)
    {
        byte[] roomCols = null;
        ushort[] colTablePtrs = null;
        byte[] colTables = null;

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = UWRoomCols;
            roomCols = reader.ReadBytes(64 * 12);

            reader.BaseStream.Position = UWColDir;
            colTablePtrs = new ushort[10];
            for (int i = 0; i < 10; i++)
            {
                colTablePtrs[i] = (ushort)(reader.ReadUInt16() - colTablePtrs[0]);
            }
            colTablePtrs[0] = 0;

            // There are only 9 columns in the last table
            reader.BaseStream.Position = UWColTables;
            colTables = reader.ReadBytes(222);
        }

        var filePath = "underworldRoomCols.dat";
        var underworldRoomCols = new MemoryStream();
        {
            byte[] padding = new byte[4];
            for (int i = 0; i < 64; i++)
            {
                underworldRoomCols.Write(roomCols, i * 12, 12);
                underworldRoomCols.Write(padding, 0, padding.Length);
            }
        }

        filePath = "underworldCols.tab";
        var underworldCols = new MemoryStream();
        using (var writer = new BinaryWriter(underworldCols))
        {
            writer.Write((ushort)colTablePtrs.Length);
            for (int i = 0; i < colTablePtrs.Length; i++)
            {
                ushort ptr = (ushort)(colTablePtrs[i] - colTablePtrs[0]);
                writer.Write(ptr);
            }

            underworldCols.Write(colTables);

            Utility.PadStream(writer.BaseStream);
        }

        const int uniqueRoomCount = 64;

        return new MapLayout
        {
            uniqueRoomCount = uniqueRoomCount,
            columnsInRoom = 12,
            rowsInRoom = 7,
            owLayoutFormat = false,
            roomCols = roomCols,
            colTablePtrs = colTablePtrs,
            colTables = colTables,
            Table = TableResource<byte>.Load(underworldCols.ToArray()),
            RoomCols = ListResource<RoomCols>.LoadList(underworldRoomCols.ToArray(), uniqueRoomCount).ToArray()
        };
    }

    private const int UWCellarRoomCols = 0x163B4 + 16;
    private const int UWCellarColTables = 0x163D4 + 16;

    private static MapLayout ExtractUnderworldCellarMap(Options options)
    {
        byte[] roomCols = null;
        ushort[] colTablePtrs = null;
        byte[] colTables = null;

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = UWCellarRoomCols;
            roomCols = reader.ReadBytes(2 * 16);

            colTablePtrs = new ushort[1];
            // There's only one column table, so it's at offset 0 in the output heap.

            // There are only 5 columns in the last table
            reader.BaseStream.Position = UWCellarColTables;
            colTables = reader.ReadBytes(34);
        }

        // var filePath = "underworldCellarRoomCols.dat";
        // options.AddFile(filePath, roomCols);

        // filePath = "underworldCellarCols.tab";
        var underworldCellarCols = new MemoryStream();
        using (var writer = new BinaryWriter(underworldCellarCols))
        {
            writer.Write((ushort)colTablePtrs.Length);
            for (int i = 0; i < colTablePtrs.Length; i++)
            {
                ushort ptr = (ushort)(colTablePtrs[i] - colTablePtrs[0]);
                writer.Write(ptr);
            }

            writer.Write(colTables);

            Utility.PadStream(writer.BaseStream);
        }

        const int uniqueRoomCount = 2;

        return new MapLayout
        {
            uniqueRoomCount = uniqueRoomCount,
            columnsInRoom = 12,
            rowsInRoom = 7,
            owLayoutFormat = false,
            roomCols = roomCols,
            colTablePtrs = colTablePtrs,
            colTables = colTables,
            Table = TableResource<byte>.Load(underworldCellarCols.ToArray()),
            RoomCols = ListResource<RoomCols>.LoadList(roomCols, uniqueRoomCount).ToArray()
        };
    }

    private static void AnalyzeUniqueLayouts(Options options)
    {
        var owLayout = ExtractOverworldMap(options);
        var uwLayout = ExtractUnderworldMap(options);

        Analyzer.AnalyzeUniqueLayouts(owLayout, "OW", options);
        Analyzer.AnalyzeUniqueLayouts(uwLayout, "UW", options);
    }

    // Outer:
    // bits 0-1: palette selector for outer tiles (border)
    // bit  2:   sea sound effect
    // bit  3:   zora
    // bits 4-7: tile column where Link comes out of cave or level

    private const int OWOuterRoomAttr = 0x18400 + 16;

    // Inner:
    // bits 0-1: palette selector for inner tiles
    // bits 2-7: cave index

    private const int OWInnerRoomAttr = 0x18480 + 16;

    // Overworld Monsters:
    // bits 0-5: low 6 bits of monster list ID
    // bits 6-7: index of enemy count value

    private const int OWMonsterListID = 0x18500 + 16;

    // Overworld Rooms (layout):
    // bits 0-6: unique room ID
    // bit  7:   high bit of monster list ID

    private const int OWMapLayout = 0x18580 + 16;

    // Caves:

    private const int OWCaves = 0x18600 + 16;

    // Other:
    // bits 0-2: tile row where Link comes out of cave or level, starting from row 1
    // bit  3:   enemies from edges
    // bit  4-5: index for position of stairs from rocks
    // bits 6-7: quest secret (0=same in both, 1=quest#1, 2=quest#2)

    private const int OWOtherRoomAttr = 0x18680 + 16;

    private const int OWMonsterCount = 0x19324 + 16;

    private class OWRoomAttrs
    {
        public byte[] monsterCounts;
        public byte[] outer;
        public byte[] inner;
        public byte[] monsterListIDs;
        public byte[] worldLayout;
        public byte[] other;
    }

    private static OWRoomAttrs ReadOverworldRoomAttrs(BinaryReader reader)
    {
        var attrs = new OWRoomAttrs();

        reader.BaseStream.Position = OWMonsterCount;
        attrs.monsterCounts = reader.ReadBytes(4);

        reader.BaseStream.Position = OWOuterRoomAttr;
        attrs.outer = reader.ReadBytes(128);

        reader.BaseStream.Position = OWInnerRoomAttr;
        attrs.inner = reader.ReadBytes(128);

        reader.BaseStream.Position = OWMonsterListID;
        attrs.monsterListIDs = reader.ReadBytes(128);

        reader.BaseStream.Position = OWMapLayout;
        attrs.worldLayout = reader.ReadBytes(128);

        reader.BaseStream.Position = OWOtherRoomAttr;
        attrs.other = reader.ReadBytes(128);

        return attrs;
    }

    private static RoomAttr WriteConvertedOWRoomAttrs(BinaryWriter writer, int index, OWRoomAttrs roomAttrs)
    {
        // Output format:

        // unique room ID           7
        //
        // index of outer palette   2
        // index of inner palette   2
        // enemy count              4
        //
        // monster list ID low      6
        // monster list ID high     1
        //
        // exit column              4
        // exit row (-1)            3
        //
        // cave index               6
        // quest secrets            2
        //
        // index shortcut pos       2
        // zora                     1
        // edge enemies             1
        // sea sound                1

        int outerPalette = roomAttrs.outer[index] & 0x03;
        int seaSound = (roomAttrs.outer[index] & 0x04) >> 2;
        int zora = (roomAttrs.outer[index] & 0x08) >> 3;
        int exitColumn = (roomAttrs.outer[index] & 0xF0) >> 4;

        int innerPalette = roomAttrs.inner[index] & 0x03;
        int cave = (roomAttrs.inner[index] & 0xFC) >> 2;

        int monsterListIDLo6 = roomAttrs.monsterListIDs[index] & 0x3F;
        int monsterCountIndex = (roomAttrs.monsterListIDs[index] & 0xC0) >> 6;

        int uniqueRoomID = roomAttrs.worldLayout[index] & 0x7F;
        int monsterListIDHi1 = (roomAttrs.worldLayout[index] & 0x80) >> 7;

        int exitRow = roomAttrs.other[index] & 0x07;
        int edgeMonsters = (roomAttrs.other[index] & 0x08) >> 3;
        int shortcutStairsIndex = (roomAttrs.other[index] & 0x30) >> 4;
        int questSecrets = (roomAttrs.other[index] & 0xC0) >> 6;

        byte b;

        b = (byte)uniqueRoomID;
        var roomId = b;
        writer.Write(b);

        int monsterCount = roomAttrs.monsterCounts[monsterCountIndex] & 0x0F;

        b = (byte)outerPalette;
        b |= (byte)(innerPalette << 2);
        b |= (byte)(monsterCount << 4);
        var palettes = b;
        writer.Write(b);

        b = (byte)monsterListIDLo6;
        b |= (byte)(monsterListIDHi1 << 6);
        var monsterlist = b;
        writer.Write(b);

        b = (byte)exitColumn;
        b |= (byte)(exitRow << 4);
        var ah = b;
        writer.Write(b);

        b = (byte)cave;
        b |= (byte)(questSecrets << 6);
        var bee = b;
        writer.Write(b);

        b = (byte)shortcutStairsIndex;
        b |= (byte)(zora << 2);
        b |= (byte)(edgeMonsters << 3);
        b |= (byte)(seaSound << 4);
        var see = b;
        writer.Write(b);

        writer.Write((byte)0);

        return new RoomAttr
        {
            UniqueRoomId = roomId,
            PalettesAndMonsterCount = palettes,
            MonsterListId = monsterlist,
            A = ah,
            B = bee,
            C = see,
            D = 0
        };
    }

    private static RoomAttr[] ExtractOverworldMapAttrs(Options options)
    {
        OWRoomAttrs roomAttrs = null;

        using (var reader = options.GetBinaryReader())
        {
            roomAttrs = ReadOverworldRoomAttrs(reader);
        }

        var attrs = new List<RoomAttr>();

        // var filePath = "overworldRoomAttr.dat";
        // using (var writer = options.AddBinaryWriter(filePath))
        using (var writer = new BinaryWriter(new MemoryStream()))
        {
            for (int i = 0; i < 128; i++)
            {
                attrs.Add(WriteConvertedOWRoomAttrs(writer, i, roomAttrs));
            }

            // Utility.PadStream(writer.BaseStream);
        }

        return attrs.ToArray();
    }

    // Outer and S/N:
    // bits 0-1: palette selector for outer tiles (border)
    // bit  2-4: S door
    // bit  5-7: N door

    private const int UWOuterRoomAttr = 0x18700 + 16;

    // Inner and E/W:
    // bits 0-1: palette selector for inner tiles
    // bits 2-4: E door
    // bits 5-7: W door

    private const int UWInnerRoomAttr = 0x18780 + 16;

    // Overworld Monsters:
    // bits 0-5: low 6 bits of monster list ID
    // bits 6-7: index of enemy count value

    private const int UWMonsterListID = 0x18800 + 16;

    // Underworld Rooms (layout):
    // bits 0-5: unique room ID
    // bit  6:   push block in room
    // bit  7:   high bit of monster list ID

    private const int UWMapLayout = 0x18880 + 16;

    // Items:
    // bits 0-4: item
    // bit  5-6: sound effect
    // bit  7:   dark

    private const int UWItemRoomAttr = 0x18900 + 16;

    // Special:
    // bits 0-2: secret trigger and action
    // bit  3:   unused
    // bits 4-5: index of item position
    // bits 6-7: unused

    private const int UWSpecialRoomAttr = 0x18980 + 16;

    private const int UWMonsterCount = 0x19420 + 16;

    private class UWRoomAttrs
    {
        public byte[] monsterCounts;
        public byte[] outerSN;
        public byte[] innerEW;
        public byte[] monsterListIDs;
        public byte[] worldLayout;
        public byte[] items;
        public byte[] special;
    }

    private static UWRoomAttrs ReadUnderworldRoomAttrs(BinaryReader reader, int uwLevelGroup)
    {
        var attrs = new UWRoomAttrs();
        int groupOffset = 768 * uwLevelGroup;

        // The counts are the same for all UW levels.
        reader.BaseStream.Position = UWMonsterCount;
        attrs.monsterCounts = reader.ReadBytes(4);

        reader.BaseStream.Position = UWOuterRoomAttr + groupOffset;
        attrs.outerSN = reader.ReadBytes(128);

        reader.BaseStream.Position = UWInnerRoomAttr + groupOffset;
        attrs.innerEW = reader.ReadBytes(128);

        reader.BaseStream.Position = UWMonsterListID + groupOffset;
        attrs.monsterListIDs = reader.ReadBytes(128);

        reader.BaseStream.Position = UWMapLayout + groupOffset;
        attrs.worldLayout = reader.ReadBytes(128);

        reader.BaseStream.Position = UWItemRoomAttr + groupOffset;
        attrs.items = reader.ReadBytes(128);

        reader.BaseStream.Position = UWSpecialRoomAttr + groupOffset;
        attrs.special = reader.ReadBytes(128);

        return attrs;
    }

    private static void WriteConvertedUWRoomAttrs(BinaryWriter writer, int index, UWRoomAttrs roomAttrs)
    {
        // Output format:

        // unique room ID           6
        //
        // index of outer palette   2
        // index of inner palette   2
        // enemy count              4
        //
        // monster list ID low      6
        // monster list ID high     1
        //
        // S door                   3
        // N door                   3
        //
        // E door                   3
        // W door                   3
        //
        // item                     5
        // index item pos           2
        //
        // secret                   3
        // push block               1
        // dark                     1
        // sound                    2

        int roomLeft = roomAttrs.outerSN[index];
        int roomRight = roomAttrs.innerEW[index];

        int outerPalette = roomAttrs.outerSN[index] & 0x03;
        int south = (roomAttrs.outerSN[index] >> 2) & 0x07;
        int north = (roomAttrs.outerSN[index] >> 5) & 0x07;

        int innerPalette = roomAttrs.innerEW[index] & 0x03;
        int east = (roomAttrs.innerEW[index] >> 2) & 0x07;
        int west = (roomAttrs.innerEW[index] >> 5) & 0x07;

        int monsterListIDLo6 = roomAttrs.monsterListIDs[index] & 0x3F;
        int monsterCountIndex = (roomAttrs.monsterListIDs[index] >> 6) & 3;

        int uniqueRoomID = roomAttrs.worldLayout[index] & 0x3F;
        int pushBlock = (roomAttrs.worldLayout[index] >> 6) & 1;
        int monsterListIDHi1 = (roomAttrs.worldLayout[index] >> 7) & 1;

        int item = roomAttrs.items[index] & 0x1F;
        int sound = (roomAttrs.items[index] >> 5) & 3;
        int dark = (roomAttrs.items[index] >> 7) & 1;

        int secret = roomAttrs.special[index] & 0x07;
        int itemPosIndex = (roomAttrs.special[index] >> 4) & 3;

        byte b;

        b = (byte)uniqueRoomID;
        writer.Write(b);

        int monsterCount = roomAttrs.monsterCounts[monsterCountIndex] & 0x0F;

        b = (byte)outerPalette;
        b |= (byte)(innerPalette << 2);
        b |= (byte)(monsterCount << 4);
        writer.Write(b);

        b = (byte)monsterListIDLo6;
        b |= (byte)(monsterListIDHi1 << 6);
        writer.Write(b);

        if (uniqueRoomID == 0x3E || uniqueRoomID == 0x3F)
        {
            b = (byte)roomLeft;
        }
        else
        {
            b = (byte)south;
            b |= (byte)(north << 3);
        }
        writer.Write(b);

        if (uniqueRoomID == 0x3E || uniqueRoomID == 0x3F)
        {
            b = (byte)roomRight;
        }
        else
        {
            b = (byte)east;
            b |= (byte)(west << 3);
        }
        writer.Write(b);

        b = (byte)item;
        b |= (byte)(itemPosIndex << 5);
        writer.Write(b);

        b = (byte)secret;
        b |= (byte)(pushBlock << 3);
        b |= (byte)(dark << 4);
        b |= (byte)(sound << 5);
        writer.Write(b);
    }

    private readonly record struct RoomAttrLevelGroup(int LevelGroup, RoomAttr[] RoomAttributes);

    private static IEnumerable<RoomAttrLevelGroup> ExtractUnderworldMapAttrs(Options options)
    {
        for (int i = 0; i < 4; i++)
        {
            yield return ExtractUnderworldMapAttrs(options, i);
        }
    }

    private static RoomAttrLevelGroup ExtractUnderworldMapAttrs(Options options, int uwLevelGroup)
    {
        UWRoomAttrs roomAttrs = null;

        using (var reader = options.GetBinaryReader())
        {
            roomAttrs = ReadUnderworldRoomAttrs(reader, uwLevelGroup);
        }

        // var filename = string.Format("underworldRoomAttr{0}.dat", uwLevelGroup);
        // var filePath = filename;
        var underworldRoomAttr = new MemoryStream();
        using (var writer = new BinaryWriter(underworldRoomAttr))
        {
            for (int i = 0; i < 128; i++)
            {
                WriteConvertedUWRoomAttrs(writer, i, roomAttrs);
            }

            Utility.PadStream(writer.BaseStream);
        }

        return new RoomAttrLevelGroup(uwLevelGroup, ListResource<RoomAttr>.LoadList(underworldRoomAttr.ToArray(), 128).ToArray());
    }

    private const int OWArmosStairsRoomCount = 6;
    private const int OWArmosStairsRoomId = 0x10CB3 + 16;
    private const int OWArmosStairsCol = 0x10CBA + 16;
    private const int OWArmosStairsRow = 0x10CE5 + 16;

    private const int OWArmosItemRoomId = 0x10CB2 + 16;
    private const int OWArmosItemX = 0x10CB9 + 16;
    private const int OWArmosItemId = 0x10CF5 + 16;

    private const int OWItemRoomId = 0x1789A + 16;
    private const int OWItemId = 0x1788A + 16;
    private const int OWItemX = 0x1788E + 16;
    private const int OWItemY = 0x17890 + 16;   // Unconfirmed

    private const int OWShortcutCount = 4;

    private const int OWShortcutRoomId = 0x19334 + 16;
    //    1D 23 49 79
    // X  50 40 50 90
    // Y  70 90 70 90
    private const int OWShortcutPos = 0x19329 + 16;

    // Stairs pos from recorder: 69

    private static TableResource<byte> ExtractOverworldMapSparseAttrs(Options options)
    {
        const int AttrLines = 11;
        const int Alignment = 2;

        byte[] armosStairsRoomIds = null;
        byte[] armosStairsXs = null;
        byte armosSecretY;

        byte armosItemRoomId;
        byte armosItemX;
        byte armosItemId;

        // Dock: 3F 55
        byte[] dockRoomIds = new byte[] { 0x3F, 0x55 };

        // Heart Container: FF X=C0 Y=90 ItemId=1A
        byte itemRoomId;
        byte itemId;
        byte itemX;
        byte itemY;

        byte[] shortcutRoomIds;

        // Mazes: 1B 61
        // exits: 02 01
        const int OWMazePath = 0x6D97 + 16;
        const int OWMazePathLen = 4;

        byte[] mazeRoomIds = new byte[] { 0x61, 0x1B };
        byte[] mazeExitDirs = new byte[] { 0x01, 0x02 };
        byte[][] mazePaths = new byte[2][];

        // Secret sound for shifting map: 1F
        //                     direction: 08
        byte[] secretShiftRoomIds = new byte[] { 0x1F };
        byte[] secretShiftDirs = new byte[] { 0x08 };

        const int OWLadderRoomCount = 6;
        const int OWLadderRoomId = 0x1F20D + 16;

        byte[] ladderRoomIds = null;

        // Fairy pond: 39 43
        byte[] fairyRoomIds = new byte[] { 0x39, 0x43 };

        // L7 pond: 42
        const int OWRecorderRoomCount = 11;
        const int OWRecorderRoomId = 0x1EF66 + 16;

        byte[] recorderRoomIds = null;
        byte[] recorderStairsPositions = null;

        byte[] attrReplacementRoomIds = new byte[] { 0x0B, 0x0E, 0x0F, 0x22, 0x34, 0x3C, 0x74 };
        OWRoomAttrs roomAttrs = null;

        using (var reader = options.GetBinaryReader())
        {
            // Armos Stairs

            reader.BaseStream.Position = OWArmosStairsRoomId;
            armosStairsRoomIds = reader.ReadBytes(OWArmosStairsRoomCount);

            reader.BaseStream.Position = OWArmosStairsCol;
            armosStairsXs = reader.ReadBytes(OWArmosStairsRoomCount);

            reader.BaseStream.Position = OWArmosStairsRow;
            armosSecretY = reader.ReadByte();

            // Armos Item

            reader.BaseStream.Position = OWArmosItemRoomId;
            armosItemRoomId = reader.ReadByte();

            reader.BaseStream.Position = OWArmosItemX;
            armosItemX = reader.ReadByte();

            reader.BaseStream.Position = OWArmosItemId;
            armosItemId = reader.ReadByte();

            // Item (Heart Container)

            reader.BaseStream.Position = OWItemRoomId;
            itemRoomId = reader.ReadByte();

            reader.BaseStream.Position = OWItemX;
            itemX = reader.ReadByte();

            reader.BaseStream.Position = OWItemY;
            itemY = reader.ReadByte();

            reader.BaseStream.Position = OWItemId;
            itemId = reader.ReadByte();

            // Shortcuts

            reader.BaseStream.Position = OWShortcutRoomId;
            shortcutRoomIds = reader.ReadBytes(OWShortcutCount);

            // Mazes

            reader.BaseStream.Position = OWMazePath;
            for (int i = 0; i < mazePaths.Length; i++)
                mazePaths[i] = reader.ReadBytes(OWMazePathLen);

            // Ladder

            reader.BaseStream.Position = OWLadderRoomId;
            ladderRoomIds = reader.ReadBytes(OWLadderRoomCount);

            // Recorder

            reader.BaseStream.Position = OWRecorderRoomId;
            recorderRoomIds = reader.ReadBytes(OWRecorderRoomCount);
            recorderStairsPositions = new byte[recorderRoomIds.Length];
            for (int i = 0; i < recorderRoomIds.Length; i++)
                recorderStairsPositions[i] = 0x69;

            // Room attr replacements

            roomAttrs = ReadOverworldRoomAttrs(reader);
            roomAttrs.outer[0x3C] = 0x72;
            roomAttrs.outer[0x74] = 0x72;
            roomAttrs.inner[0x0E] = 0x7B;
            roomAttrs.inner[0x0F] = 0x83;
            roomAttrs.inner[0x22] = 0x84;
            roomAttrs.inner[0x34] = 0x0F - (1 << 2);
            roomAttrs.inner[0x3C] = 0x0B + (1 << 2);
            roomAttrs.inner[0x45] = 0x12;
            roomAttrs.inner[0x74] = 0x7A;
            roomAttrs.monsterListIDs[0x0B] = 0x2F;
            roomAttrs.worldLayout[0x0B] = 0x7B;
            roomAttrs.worldLayout[0x3C] = 0x7B;
            roomAttrs.worldLayout[0x74] = 0x5A;
            roomAttrs.other[0x3C] = 0x01;
            roomAttrs.other[0x74] = 0x00;
        }

        var filePath = "overworldRoomSparseAttr.tab";
        using (var writer = options.AddBinaryWriter(filePath))
        {
            var ptrs = new ushort[AttrLines];
            int i = 0;
            int bufBase = (1 + AttrLines) * 2;

            writer.BaseStream.Position = bufBase;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRoomXY(writer, armosStairsRoomIds, armosStairsXs, armosSecretY);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRoomItem(writer, armosItemRoomId, armosItemX, armosSecretY, armosItemId);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRooms(writer, dockRoomIds);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRoomItem(writer, itemRoomId, itemX, itemY, itemId);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRooms(writer, shortcutRoomIds);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRoomMaze(writer, mazeRoomIds, mazeExitDirs, mazePaths);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRoomByte(writer, secretShiftRoomIds, secretShiftDirs);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRooms(writer, ladderRoomIds);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRoomPos(writer, recorderRoomIds, recorderStairsPositions);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRooms(writer, fairyRoomIds);
            i++;

            ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
            WriteRoomAttrReplacements(writer, attrReplacementRoomIds, roomAttrs);
            i++;

            // Pad now, because after this we'll seek to the beginning of the file.
            Utility.PadStream(writer.BaseStream);

            writer.BaseStream.Position = 0;
            writer.Write((ushort)ptrs.Length);
            foreach (var ptr in ptrs)
            {
                writer.Write((ushort)(ptr - bufBase));
            }
        }

        return TableResource<byte>.Load(options.Files[filePath]);
    }

    private static void WriteRoomXY(BinaryWriter writer, byte[] roomIds, byte[] xs, byte y)
    {
        writer.Write((byte)roomIds.Length);
        writer.Write((byte)3);
        for (int i = 0; i < roomIds.Length; i++)
        {
            writer.Write(roomIds[i]);
            writer.Write(xs[i]);
            writer.Write(y);
        }
    }

    private static void WriteRoomItem(
        BinaryWriter writer, byte roomId, byte x, byte y, byte itemId)
    {
        writer.Write((byte)1);
        writer.Write((byte)4);
        writer.Write(roomId);
        writer.Write(x);
        writer.Write(y);
        writer.Write(itemId);
    }

    private static void WriteRooms(BinaryWriter writer, byte[] roomIds)
    {
        writer.Write((byte)roomIds.Length);
        writer.Write((byte)1);
        foreach (var id in roomIds)
            writer.Write(id);
    }

    private static void WriteRoomPos(
        BinaryWriter writer, byte[] roomIds, byte[] positions)
    {
        writer.Write((byte)roomIds.Length);
        writer.Write((byte)2);
        for (int i = 0; i < roomIds.Length; i++)
        {
            writer.Write(roomIds[i]);
            writer.Write(positions[i]);
        }
    }

    private static void WriteRoomMaze(
        BinaryWriter writer, byte[] roomIds, byte[] exitDirs, byte[][] paths)
    {
        writer.Write((byte)roomIds.Length);
        writer.Write((byte)6);
        for (int i = 0; i < roomIds.Length; i++)
        {
            writer.Write(roomIds[i]);
            writer.Write(exitDirs[i]);
            foreach (var dir in paths[i])
                writer.Write(dir);
        }
    }

    private static void WriteRoomByte(
        BinaryWriter writer, byte[] roomIds, byte[] bytes)
    {
        writer.Write((byte)roomIds.Length);
        writer.Write((byte)2);
        for (int i = 0; i < roomIds.Length; i++)
        {
            writer.Write(roomIds[i]);
            writer.Write(bytes[i]);
        }
    }

    private static void WriteRoomAttrReplacements(
        BinaryWriter writer, byte[] roomIds, OWRoomAttrs roomAttrs)
    {
        writer.Write((byte)roomIds.Length);
        writer.Write((byte)7);
        for (int i = 0; i < roomIds.Length; i++)
        {
            writer.Write(roomIds[i]);
            WriteConvertedOWRoomAttrs(writer, roomIds[i], roomAttrs);
        }
    }

    private const int OWInfoBlock = 0x19300 + 16;
    private const int InfoBlockPalettesOffset = 3;
    private const int InfoBlockStartY = 0x28;
    private const int InfoBlockStartRoomId = 0x2F;
    private const int InfoBlockTriforceRoomId = 0x30;
    private const int InfoBlockBossRoomId = 0x3E;
    private const int InfoBlockLevelNumber = 0x33;
    private const int InfoBlockDrawnMapOffset = 0x2D;
    private const int InfoBlockCellarRoomIdArray = 0x34;
    private const int InfoBlockShortcutPosArray = 0x29;
    private const int InfoBlockDrawnMap = 0x3F;
    private const int InfoBlockCellarPalette1 = 0x7C;
    private const int InfoBlockCellarPalette2 = 0x9C;
    private const int InfoBlockDarkPalette = 0xBC;
    private const int InfoBlockDeathPalette = 0xDC;

    private const int InfoBlockShortcutPosCount = 4;
    private const int InfoBlockCellarRoomIdCount = 10;
    private const int OWLastSpritePal = 0x1A281 + 16;

    private static void WritePalettes(
        BinaryWriter writer, byte[] paletteBytes, int paletteByteCount)
    {
        for (int i = 0; i < paletteByteCount; i++)
        {
            var colorIndex = paletteBytes[i];
            if (colorIndex >= DefaultSystemPalette.Colors.Length)
                colorIndex = 0;
            // ARGB 8888
            // Index 0 is opaque for BG palettes, and transparent for sprites.
            // The first 4 palettes are for BG; the second 4 are for sprites.
            writer.Write(colorIndex);
            // Alpha for BG palettes has to be 0 at color index 0, too, because sprites
            // can go behind the background.
        }
    }

    private static LevelInfoBlock ExtractOverworldInfo(Options options)
    {
        // var filePath = "overworldInfo.dat";

        var overworldInfo = new MemoryStream();
        using (var reader = options.GetBinaryReader())
        using (var writer = new BinaryWriter(overworldInfo))
        {
            const int PaletteByteCount = 8 * 4;

            reader.BaseStream.Position = OWInfoBlock + InfoBlockPalettesOffset;
            byte[] paletteBytes = reader.ReadBytes(PaletteByteCount);

            // Overwrite the last sprite palette, because the original doesn't seem to be used,
            // but the other one is. It's used by zoras and moblins, and for flashing.
            reader.BaseStream.Position = OWLastSpritePal;
            reader.Read(paletteBytes, 7 * 4, 4);

            for (int i = 0; i < PaletteByteCount; i++)
            {
                var colorIndex = paletteBytes[i];
                // ARGB 8888
                // Index 0 is opaque for BG palettes, and transparent for sprites.
                // The first 4 palettes are for BG; the second 4 are for sprites.
                writer.Write(colorIndex);
                // Alpha for BG palettes has to be 0 at color index 0, too, because sprites
                // can go behind the background.
            }

            reader.BaseStream.Position = OWInfoBlock + InfoBlockStartY;
            byte startY = reader.ReadByte();
            writer.Write(startY);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockStartRoomId;
            byte startRoomId = reader.ReadByte();
            writer.Write(startRoomId);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockTriforceRoomId;
            byte triforceRoomId = reader.ReadByte();
            writer.Write(triforceRoomId);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockBossRoomId;
            byte bossRoomId = reader.ReadByte();
            writer.Write(bossRoomId);

            byte song = 2;
            writer.Write(song);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockLevelNumber;
            byte levelNumber = reader.ReadByte();
            writer.Write(levelNumber);

            // The Overworld's effective level number is the same.
            writer.Write(levelNumber);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockDrawnMapOffset;
            byte mapOffset = reader.ReadByte();
            writer.Write(mapOffset);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockCellarRoomIdArray;
            byte[] cellarRoomIds = reader.ReadBytes(InfoBlockCellarRoomIdCount);
            writer.Write(cellarRoomIds);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockShortcutPosArray;
            byte[] shortcutPos = reader.ReadBytes(InfoBlockShortcutPosCount);
            writer.Write(shortcutPos);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockDrawnMap;
            var drawnMap = reader.ReadBytes(16);
            writer.Write(drawnMap);

            Utility.PadStream(writer.BaseStream);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockCellarPalette1;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            WritePalettes(writer, paletteBytes, PaletteByteCount);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockCellarPalette2;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            WritePalettes(writer, paletteBytes, PaletteByteCount);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockDarkPalette;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            WritePalettes(writer, paletteBytes, PaletteByteCount);

            reader.BaseStream.Position = OWInfoBlock + InfoBlockDeathPalette;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            writer.Write(paletteBytes);

            Utility.PadStream(writer.BaseStream);
        }

        // var dir = new LevelDirectory
        // {
        //     LevelInfoBlock = "overworldInfo.dat",
        //     RoomCols = "overworldRoomCols.dat",
        //     ColTables = "overworldCols.tab",
        //     TileAttrs = "overworldTileAttrs.dat",
        //     TilesImage = "overworldTiles.png",
        //     PlayerImage = "playerItem.png",
        //     PlayerSheet = "playerItemsSheet.tab",
        //     RoomAttrs = "overworldRoomAttr.dat",
        //     ObjLists = "objLists.tab",
        //     Extra1 = "overworldRoomSparseAttr.tab"
        // };
        // WriteLevelDir(options, 0, 0, dir);
        // WriteLevelDir(options, 1, 0, dir);

        return ListResource<LevelInfoBlock>.LoadSingle(overworldInfo.ToArray());
    }

    private static Dictionary<LevelGroupMap, LevelInfoBlock> ExtractUnderworldInfo(Options options)
    {
        var result = new Dictionary<LevelGroupMap, LevelInfoBlock>();
        for (int quest = 0; quest < 2; quest++)
        {
            for (int level = 1; level < 10; level++)
            {
                var info = ExtractUnderworldInfo(options, quest, level);
                result[new LevelGroupMap(quest, level)] = info;
            }
        }

        return result;
    }

    private const int InfoBlockSize = 0xFC;

    private static readonly string[] bossImageFilenames = new string[]
    {
        "",
        "uwBoss1257.png",
        "uwBoss1257.png",
        "uwBoss3468.png",
        "uwBoss3468.png",
        "uwBoss1257.png",
        "uwBoss3468.png",
        "uwBoss1257.png",
        "uwBoss3468.png",
        "uwBoss9.png",
    };

    private static readonly string[] bossSheetFilenames = new string[]
    {
        "",
        "uwBossSheet1257.tab",
        "uwBossSheet1257.tab",
        "uwBossSheet3468.tab",
        "uwBossSheet3468.tab",
        "uwBossSheet1257.tab",
        "uwBossSheet3468.tab",
        "uwBossSheet1257.tab",
        "uwBossSheet3468.tab",
        "uwBossSheet9.tab",
    };

    private const int InfoBlockDiffPtrs = 0x183A4 + 0x10;
    private const int FirstInfoBlockDiff = 0x1816F + 0x10;

    private const int InfoBlockDiffEffectiveLevelNumber = 0xA;
    private const int InfoBlockDiffStartRoomId = 6;
    private const int InfoBlockDiffTriforceRoomId = 7;
    private const int InfoBlockDiffBossRoomId = 0x15;
    private const int InfoBlockDiffDrawnMapOffset = 4;
    private const int InfoBlockDiffCellarRoomIdArray = 0xB;
    private const int InfoBlockDiffDrawnMap = 0x16;
    private const int InfoBlockDiffShortcutPosArray = 0;

    private static LevelInfoBlock ExtractUnderworldInfo(Options options, int quest, int level)
    {
        var filename = string.Format("levelInfo_{0}_{1}.dat", quest, level);
        var filePath = filename;
        int effectiveLevel = level;

        var stream = new MemoryStream();
        using (var reader = options.GetBinaryReader())
        using (var writer = new BinaryWriter(stream))
        {
            int quest2DiffAddr = 0;

            if (quest == 1)
            {
                reader.BaseStream.Position = InfoBlockDiffPtrs;
                ushort firstPtr = reader.ReadUInt16();
                reader.BaseStream.Position = InfoBlockDiffPtrs + (level - 1) * 2;
                ushort ptr = reader.ReadUInt16();
                quest2DiffAddr = ptr - firstPtr + FirstInfoBlockDiff;

                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffEffectiveLevelNumber;
                effectiveLevel = reader.ReadByte();

                reader.BaseStream.Position = InfoBlockDiffPtrs + (effectiveLevel - 1) * 2;
                ptr = reader.ReadUInt16();
                quest2DiffAddr = ptr - firstPtr + FirstInfoBlockDiff;
            }

            const int PaletteByteCount = 8 * 4;
            int blockOffset = InfoBlockSize * effectiveLevel;

            reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockPalettesOffset;
            byte[] paletteBytes = reader.ReadBytes(PaletteByteCount);

            for (int i = 0; i < PaletteByteCount; i++)
            {
                var colorIndex = paletteBytes[i];
                // ARGB 8888
                // Index 0 is opaque for BG palettes, and transparent for sprites.
                // The first 4 palettes are for BG; the second 4 are for sprites.
                writer.Write(colorIndex);
                // Alpha for BG palettes has to be 0 at color index 0, too, because sprites
                // can go behind the background.
            }

            reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockStartY;
            byte startY = reader.ReadByte();
            writer.Write(startY);

            if (quest == 0)
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockStartRoomId;
            else
                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffStartRoomId;
            byte startRoomId = reader.ReadByte();
            writer.Write(startRoomId);

            if (quest == 0)
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockTriforceRoomId;
            else
                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffTriforceRoomId;
            byte triforceRoomId = reader.ReadByte();
            writer.Write(triforceRoomId);

            if (quest == 0)
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockBossRoomId;
            else
                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffBossRoomId;
            byte bossRoomId = reader.ReadByte();
            writer.Write(bossRoomId);

            byte song = (byte)(level < 9 ? 3 : 7);
            writer.Write(song);

            if (quest == 0)
            {
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockLevelNumber;
                byte levelNumber = reader.ReadByte();
                writer.Write(levelNumber);
            }
            else
            {
                writer.Write((byte)level);
            }

            writer.Write((byte)effectiveLevel);

            if (quest == 0)
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockDrawnMapOffset;
            else
                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffDrawnMapOffset;
            byte mapOffset = reader.ReadByte();
            writer.Write(mapOffset);

            if (quest == 0)
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockCellarRoomIdArray;
            else
                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffCellarRoomIdArray;
            byte[] cellarRoomIds = reader.ReadBytes(InfoBlockCellarRoomIdCount);
            if (quest == 0 && level == 3)
                cellarRoomIds[0] = 0x0F;
            writer.Write(cellarRoomIds);

            if (quest == 0)
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockShortcutPosArray;
            else
                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffShortcutPosArray;
            byte[] shortcutPos = reader.ReadBytes(InfoBlockShortcutPosCount);
            writer.Write(shortcutPos);

            if (quest == 0)
                reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockDrawnMap;
            else
                reader.BaseStream.Position = quest2DiffAddr + InfoBlockDiffDrawnMap;
            var drawnMap = reader.ReadBytes(16);
            writer.Write(drawnMap);

            Utility.PadStream(writer.BaseStream);

            reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockCellarPalette1;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            WritePalettes(writer, paletteBytes, PaletteByteCount);

            reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockCellarPalette2;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            WritePalettes(writer, paletteBytes, PaletteByteCount);

            reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockDarkPalette;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            WritePalettes(writer, paletteBytes, PaletteByteCount);

            reader.BaseStream.Position = OWInfoBlock + blockOffset + InfoBlockDeathPalette;
            paletteBytes = reader.ReadBytes(PaletteByteCount);
            writer.Write(paletteBytes);

            Utility.PadStream(writer.BaseStream);
        }

        int levelGroup = 0;
        if (level >= 7)
            levelGroup++;
        if (quest == 1)
            levelGroup += 2;

        string roomAttrFilename = string.Format("underworldRoomAttr{0}.dat", levelGroup);
        string bossImageFilename = bossImageFilenames[effectiveLevel];
        string bossSheetFilename = bossSheetFilenames[effectiveLevel];

        // TODO: Some of these are for the OW, until we extract the matching UW parts.

        // var dir = new LevelDirectory
        // {
        //     LevelInfoBlock = filename,
        //     RoomCols = "underworldRoomCols.dat",
        //     ColTables = "underworldCols.tab",
        //     TileAttrs = "underworldTileAttrs.dat",
        //     TilesImage = "underworldTiles.png",
        //     PlayerImage = "playerItem.png",
        //     PlayerSheet = "playerItemsSheet.tab",
        //     RoomAttrs = roomAttrFilename,
        //     ObjLists = "objLists.tab",
        //     Extra1 = "overworldRoomSparseAttr.tab"
        // };
        // WriteLevelDir(options, quest, level, dir);
        return ListResource<LevelInfoBlock>.LoadSingle(stream.ToArray());
    }

    private delegate void ReadTranslateDelegate(BinaryReader reader, BinaryWriter writer);

    public sealed class ObjectAttr
    {
        public int ItemDropClass { get; set; }
        public bool CustomCollision { get; set; }
        public bool InvincibleToWeapons { get; set; }
        public bool HalfWidth { get; set; }
        public bool WorldCollision { get; set; }
        public bool Unknown10__ { get; set; }
        public bool Unknown80__ { get; set; }
    }

    [Flags]
    private enum ObjectAttrBin
    {
        None = 0,
        CustomCollision = 1,
        CustomDraw = 4,
        Unknown10__ = 0x10,
        InvincibleToWeapons = 0x20,
        HalfWidth = 0x40,
        Unknown80__ = 0x80,
        WorldCollision = 0x100,
    }

    private static int GetObjectMaxHP(HPAttr[] hpAttrs, int type)
    {
        var index = (int)type / 2;
        return (byte)hpAttrs[index].GetHP((int)type);
    }

    private static LevelInfoEx ExtractOverworldInfoEx(Options options)
    {
        var filePath = "overworldInfoEx.tab";

        using (var reader = options.GetBinaryReader())
        using (var writer = options.AddBinaryWriter(filePath))
        {
            const int Alignment = 4;
            const int DataLines = 8;

            var hp = ReadTranslateHitPoints(reader, writer);
            var damage = ReadTranslatePlayerDamage(reader, writer);

            var attrs = ReadTranslateObjAttrs(reader, writer);

            static bool ShouldSerailize(ObjectAttribute attr)
            {
                return attr.HitPoints != null || attr.Damage != null;
            }

            var objAttributes = Enumerable.Range(0, Math.Max(hp.Length, damage.Length))
                .Select(t => new
                {
                    ObjType = (ObjType)t,
                    Attr = new ObjectAttribute
                    {
                        HitPoints = GetObjectMaxHP(hp, t),
                        Damage = damage[t],
                        ItemDropClass = attrs[t].ItemDropClass,
                        HasCustomCollision = attrs[t].CustomCollision,
                        IsInvincibleToWeapons = attrs[t].InvincibleToWeapons,
                        IsHalfWidth = attrs[t].HalfWidth,
                        HasWorldCollision = attrs[t].WorldCollision,
                        Unknown10 = attrs[t].Unknown10__,
                        Unknown80 = attrs[t].Unknown80__,
                    }
                })
                .Where(t => ShouldSerailize(t.Attr))
                .ToDictionary(t => t.ObjType, t => t.Attr);

            var levelInfo = new LevelInfoEx
            {
                OWPondColors = ReadTranslateOWPondColors(reader, writer),
                CavePalette = ReadTranslateCavePalettes(reader, writer),
                CaveSpec = ReadTranslateCaves(reader, writer),
                ObjectAttribute = objAttributes,
                LevelPersonStringIds = ReadTranslateLevelPersonStringIds(reader, writer),
                SpawnSpot = ReadTranslateSpawnSpots(reader, writer),
            };

            var converters = new ReadTranslateDelegate[]
            {
                (r, w) => ReadTranslateOWPondColors(r, w),
                (r, w) => ReadTranslateSpawnSpots(r, w),
                (r, w) => ReadTranslateObjAttrs(r, w),
                (r, w) => ReadTranslateCavePalettes(r, w),
                (r, w) => ReadTranslateCaves(r, w),
                (r, w) => ReadTranslateLevelPersonStringIds(r, w),
                (r, w) => ReadTranslateHitPoints(r, w),
                (r, w) => ReadTranslatePlayerDamage(r, w),
            };

            var ptrs = new ushort[DataLines];
            int bufBase = (1 + DataLines) * 2;

            writer.BaseStream.Position = bufBase;

            for (int i = 0; i < converters.Length; i++)
            {
                ptrs[i] = (ushort)Utility.AlignStream(writer.BaseStream, Alignment);
                var converter = converters[i];
                converter(reader, writer);
            }

            // Pad now, because after this we'll seek to the beginning of the file.
            Utility.PadStream(writer.BaseStream);

            writer.BaseStream.Position = 0;
            writer.Write((ushort)ptrs.Length);
            foreach (var ptr in ptrs)
            {
                writer.Write((ushort)(ptr - bufBase));
            }

            options.AddJson("overworldInfoEx.json", levelInfo);

            return levelInfo;
        }
    }

    private static byte[] ReadTranslateOWPondColors(BinaryReader reader, BinaryWriter writer)
    {
        const int OWPondColorSeq = 0x1FEE8 + 16;

        reader.BaseStream.Position = OWPondColorSeq;
        var colorIndexes = reader.ReadBytes(12);

        writer.Write(colorIndexes.Length);
        writer.Write(colorIndexes);
        return colorIndexes;
    }

    private static PointXY[] ReadTranslateSpawnSpots(BinaryReader reader, BinaryWriter writer)
    {
        const int SpawnSpots = 0x1464E + 16;

        reader.BaseStream.Position = SpawnSpots;
        var spots = reader.ReadBytes(9 * 4);

        writer.Write(spots.Length);

        for (int i = 0; i < spots.Length; i++)
        {
            writer.Write(spots[i]);
        }

        return spots
            .Select(t => new PointXY(
                (t & 0x0F) << 4,
                (t & 0xF0) | 0xD)
            ).ToArray();
    }

    private static ObjectAttr[] ReadTranslateObjAttrs(BinaryReader reader, BinaryWriter writer)
    {
        const int ObjAttrs = 0x1FAEF + 0x10;

        byte[] fieldLengths = new byte[] { 0, 1 };
        byte[] byHandAttrs = LoadArray8("ObjAttrs", fieldLengths);
        ushort[] finalAttrs = new ushort[128];

        reader.BaseStream.Position = ObjAttrs;
        byte[] origAttrs = reader.ReadBytes(128);

        for (int i = 0; i < origAttrs.Length; i++)
        {
            finalAttrs[i] |= origAttrs[i];
        }

        for (int i = 0; i < byHandAttrs.Length; i++)
        {
            finalAttrs[i] |= (ushort)(byHandAttrs[i] << 8);
        }

        // Extract the item drop classes.

        for (int i = 1; i < finalAttrs.Length; i++)
        {
            finalAttrs[i] |= 4 << 9;
        }

        const int NoDropTypes = 0x1301B + 0x10;
        const int Class1Types = 0x13022 + 0x10;
        const int Class2Types = 0x13028 + 0x10;
        const int Class3Types = 0x13031 + 0x10;

        int[] lengths = new int[] { 7, 6, 9, 9 };
        int[] addrs = new int[]
        {
            NoDropTypes,
            Class1Types,
            Class2Types,
            Class3Types
        };

        for (int i = 0; i < addrs.Length; i++)
        {
            reader.BaseStream.Position = addrs[i];
            var types = reader.ReadBytes(lengths[i]);

            for (int j = 0; j < types.Length; j++)
            {
                int type = types[j];
                finalAttrs[type] = (ushort)(finalAttrs[type] & ~(7 << 9));
                finalAttrs[type] |= (ushort)(i << 9);
            }
        }

        for (int i = 0; i < finalAttrs.Length; i++)
        {
            writer.Write(finalAttrs[i]);
        }
        return finalAttrs.Select(CreateObjectAttr).ToArray();

        static ObjectAttr CreateObjectAttr(ushort s)
        {
            var typed = (ObjectAttrBin)s;
            var attr = new ObjectAttr
            {
                CustomCollision = typed.HasFlag(ObjectAttrBin.CustomCollision),
                InvincibleToWeapons = typed.HasFlag(ObjectAttrBin.InvincibleToWeapons),
                HalfWidth = typed.HasFlag(ObjectAttrBin.HalfWidth),
                WorldCollision = typed.HasFlag(ObjectAttrBin.WorldCollision),
                Unknown10__ = typed.HasFlag(ObjectAttrBin.Unknown10__),
                Unknown80__ = typed.HasFlag(ObjectAttrBin.Unknown80__),
                ItemDropClass = s >> 9 & 7
            };
            return attr;
        }
    }

    private static CavePaletteSet ReadTranslateCavePalettes(BinaryReader reader, BinaryWriter writer)
    {
        const int OWCavePalettes = 0x1A260 + 16;

        reader.BaseStream.Position = OWCavePalettes;
        var colorIndexes = reader.ReadBytes(8);

        writer.Write((int)2);
        writer.Write((int)2);
        WritePalettes(writer, colorIndexes, colorIndexes.Length);
        return new CavePaletteSet
        {
            PaletteA = colorIndexes.Take(4).ToArray(),
            PaletteB = colorIndexes.Skip(4).ToArray()
        };
    }

    internal enum StringId
    {
        DoorRepair = 5,
        AintEnough = 10,
        LostHillsHint = 11,
        LostWoodsHint = 12,
        Grumble = 18,
        MoreBombs = 25,
        MoneyOrLife = 27,
        EnterLevel9 = 34,
    }

    private static CaveSpec[] ReadTranslateCaves(BinaryReader reader, BinaryWriter writer)
    {
        const int OWCaveDwellers = 0x6E6F + 16;
        const int OWCaveStringIds = 0x45A2 + 16;
        const int OWCaveItems = 0x18600 + 16;
        const int OWCavePrices = 0x1863C + 16;

        var caves = new List<CaveSpec>();

        reader.BaseStream.Position = OWCaveDwellers;
        var types = reader.ReadBytes(20);

        reader.BaseStream.Position = OWCaveStringIds;
        var stringIds = reader.ReadBytes(20);

        reader.BaseStream.Position = OWCaveItems;
        var items = reader.ReadBytes(20 * 3);

        reader.BaseStream.Position = OWCavePrices;
        var prices = reader.ReadBytes(20 * 3);

        writer.Write((int)20);

        bool IsGambling(int i)
        {
            var type2 = (ObjType)((int)ObjType.Cave1 + i);
            return type2 == ObjType.Cave7;
        }

        PersonType GetPersonType(StringId stringId, int i)
        {
            // Commented out are the underworld ones. They're artificially constructed later.
            if (IsGambling(i)) return PersonType.Gambling;
            var type = (ObjType)((int)ObjType.Cave1 + i);
            switch (stringId)
            {
                case StringId.DoorRepair: return PersonType.DoorRepair;
                    // case StringId.MoneyOrLife: return PersonType.MoneyOrLife;
                    // case StringId.EnterLevel9: return PersonType.EnterLevel9;
                    // case StringId.MoreBombs: return PersonType.MoreBombs;
            }

            switch (type)
            {
                case ObjType.Cave5Shortcut: return PersonType.CaveShortcut;
                    // case ObjType.Grumble: return PersonType.Grumble;
            }

            return PersonType.Shop;
        }

        for (int i = 0; i < 20; i++)
        {
            byte origStringAttr = stringIds[i];
            byte stringAttr = (byte)((origStringAttr & 0xC0) | ((origStringAttr & 0x3F) / 2));

            writer.Write(types[i]);
            writer.Write(stringAttr);
            writer.Write(items, i * 3, 3);
            writer.Write(prices, i * 3, 3);

            var indexOf3 = i * 3;

            var itemAbin = items[indexOf3];
            var itemBbin = items[indexOf3 + 1];
            var itemCbin = items[indexOf3 + 2];

            var stringId = stringAttr & 0x3F;
            var pay = (stringAttr & 0x80) != 0; // JOE: Not sure I get what this one is...?
            var pickUp = (stringAttr & 0x40) != 0;
            var showNegative = (itemAbin & 0x80) != 0;
            var checkHearts = (itemAbin & 0x40) != 0;
            var special = (itemBbin & 0x80) != 0;
            var hint = (itemBbin & 0x40) != 0;
            var showPrices = (itemCbin & 0x80) != 0;
            var showItems = (itemCbin & 0x40) != 0;

            var caveshopitems = Enumerable.Range(0, 3)
                .Select(t => new CaveShopItem
                {
                    ItemId = (ItemId)(items[indexOf3 + t] & 0x3F),
                    ItemAmount = 1,
                    Cost = prices[indexOf3 + t],
                    Costing = ItemSlot.Rupees,
                }).ToArray();

            var personType = GetPersonType((StringId)stringAttr, i);

            var options = CaveSpecOptions.None;
            if (showNegative) options |= CaveSpecOptions.ShowNegative;
            if (showPrices) options |= CaveSpecOptions.ShowNumbers;
            if (showItems) options |= CaveSpecOptions.ShowItems;
            if (pay) options |= CaveSpecOptions.Pay;
            if (pickUp) options |= CaveSpecOptions.PickUp;
            if (pickUp && !showPrices) options |= CaveSpecOptions.Persisted;

            var caveId = (CaveId)((int)ObjType.Cave1 + i);
            var caveType = caveId == CaveId.Cave5Shortcut ? CaveType.Shortcut : CaveType.Items;

            var spec = new CaveSpec
            {
                DwellerType = (CaveDwellerType)types[i],
                CaveId = caveId,
                CaveType = caveType,
                Options = options,
                Text = _gameStrings[stringId],
                PersonType = personType,
                Items = caveshopitems,
            };

            switch (personType)
            {
                case PersonType.DoorRepair:
                    spec.Options |= CaveSpecOptions.EntranceCost;
                    spec.Options |= CaveSpecOptions.Persisted;
                    spec.EntranceCheckItem = ItemSlot.Rupees;
                    spec.EntranceCheckAmount = 20;
                    break;
            }

            switch (caveId)
            {
                case CaveId.Cave11MedicineShop:
                    spec.RequiredItem = new PersonItemRequirement
                    {
                        RequirementType = PersonItemRequirementType.Check,
                        Effect = PersonItemRequirementEffect.UpgradeItem,
                        Item = ItemSlot.Letter,
                        RequiredLevel = 1,
                        UpgradeLevel = 2,
                    };
                    break;
                case CaveId.Cave18:
                case CaveId.Cave19:
                case CaveId.Cave20:
                    spec.Items = [
                        new CaveShopItem {
                            ItemId = ItemId.Rupee,
                            ItemAmount = caveId switch {
                                CaveId.Cave18 => 10,
                                CaveId.Cave19 => 100,
                                CaveId.Cave20 => 30,
                                _ => throw new Exception(),
                            },
                            Cost = 0,
                            Options = CaveShopItemOptions.ShowNegative,
                        },
                    ];
                    break;
            }

            if (hint)
            {
                var hintStringId = spec.CaveId switch
                {
                    CaveId.Cave12LostHillsHint => StringId.LostHillsHint,
                    CaveId.Cave13LostWoodsHint => StringId.LostWoodsHint,
                    _ => StringId.AintEnough,
                };
                caveshopitems[0].Hint = _gameStrings[(int)StringId.AintEnough];
                caveshopitems[1].Hint = _gameStrings[(int)StringId.AintEnough];
                caveshopitems[2].Hint = _gameStrings[(int)hintStringId];
            }

            foreach (var item in caveshopitems)
            {
                if (item.ItemId == 0) continue;

                if (item.ItemId == ItemId.WhiteSword)
                {
                    item.Cost = 5;
                    item.Costing = ItemSlot.HeartContainers;
                    item.Options |= CaveShopItemOptions.CheckCost | CaveShopItemOptions.SetItem;
                }
                else if (item.ItemId == ItemId.MagicSword)
                {
                    item.Cost = 12;
                    item.Costing = ItemSlot.HeartContainers;
                    item.Options |= CaveShopItemOptions.CheckCost | CaveShopItemOptions.SetItem;
                }

                if (checkHearts) item.Options |= CaveShopItemOptions.CheckCost;
                if (spec.PersonType == PersonType.Gambling) item.Options |= CaveShopItemOptions.Gambling;
            }

            spec.Items = spec.Items.Where(t => t.ItemId < ItemId.MAX).ToArray();
            caves.Add(spec);
        }

        caves.Add(new CaveSpec
        {
            DwellerType = CaveDwellerType.Moblin,
            PersonType = PersonType.Grumble,
            Text = _gameStrings[(int)StringId.Grumble],
            Options = CaveSpecOptions.ControlsBlockingWall | CaveSpecOptions.ControlsShutterDoors | CaveSpecOptions.Persisted,
            RequiredItem = new PersonItemRequirement
            {
                RequirementType = PersonItemRequirementType.Consumes,
                Effect = PersonItemRequirementEffect.RemovePerson,
                Item = ItemSlot.Food,
                RequiredLevel = 1,
            }
        });

        caves.Add(new CaveSpec
        {
            DwellerType = CaveDwellerType.OldMan,
            PersonType = PersonType.MoneyOrLife,
            Text = _gameStrings[(int)StringId.MoneyOrLife],
            Options = CaveSpecOptions.ShowNumbers | CaveSpecOptions.ControlsBlockingWall
                | CaveSpecOptions.ControlsShutterDoors | CaveSpecOptions.ShowItems | CaveSpecOptions.Pay
                | CaveSpecOptions.Persisted,
            Items = [
                new CaveShopItem {
                    ItemId = ItemId.HeartContainer,
                    Costing = ItemSlot.HeartContainers,
                    Cost = 1,
                    Options = CaveShopItemOptions.ShowNegative,
                },
                new CaveShopItem {
                    ItemId = ItemId.Rupee,
                    Costing = ItemSlot.Rupees,
                    Cost = 50,
                    Options = CaveShopItemOptions.ShowNegative,
                }
            ],
        });

        caves.Add(new CaveSpec
        {
            DwellerType = CaveDwellerType.OldMan,
            PersonType = PersonType.MoreBombs,
            Text = _gameStrings[(int)StringId.MoreBombs],
            Options = CaveSpecOptions.ShowNumbers | CaveSpecOptions.PickUp | CaveSpecOptions.ShowItems,
            Items = [
                new CaveShopItem {
                    ItemId = ItemId.MaxBombs,
                    ItemAmount = 4,
                    Costing = ItemSlot.Rupees,
                    FillItem = ItemSlot.Bombs,
                    Cost = 100,
                    Options = CaveShopItemOptions.ShowNegative | CaveShopItemOptions.ShowCostingItem
                }
            ],
        });

        caves.Add(new CaveSpec
        {
            DwellerType = CaveDwellerType.OldMan,
            PersonType = PersonType.EnterLevel9,
            Text = _gameStrings[(int)StringId.EnterLevel9],
            Options = CaveSpecOptions.ControlsBlockingWall | CaveSpecOptions.ControlsShutterDoors
                | CaveSpecOptions.EntranceCheck | CaveSpecOptions.Persisted,
            EntranceCheckItem = ItemSlot.TriforcePieces,
            EntranceCheckAmount = 0xFF,
        });

        // These are the only items that are added to the existing total.
        var shouldAddItem = new HashSet<ItemId>
        {
            ItemId.FiveRupees,
            ItemId.Rupee,
            ItemId.Key,
            ItemId.HeartContainer,
            ItemId.RedPotion,
            ItemId.Heart,
        };

        foreach (var cave in caves.Where(t => t.Items != null))
        {
            foreach (var item in cave.Items.Where(t => !shouldAddItem.Contains(t.ItemId)))
            {
                item.Options |= CaveShopItemOptions.SetItem;
            }
        }

        return caves.ToArray();
    }

    private static int[][] ReadTranslateLevelPersonStringIds(BinaryReader reader, BinaryWriter writer)
    {
        byte[] stringIds = null;

        var tables = new List<int[]>();

        reader.BaseStream.Position = 0x4A1B + 0x10;
        stringIds = reader.ReadBytes(8);
        tables.Add(stringIds.Select(t => t / 2).ToArray());

        for (int i = 0; i < stringIds.Length; i++)
            stringIds[i] = (byte)(stringIds[i] / 2);
        writer.Write(stringIds);

        reader.BaseStream.Position = 0x4A61 + 0x10;
        stringIds = reader.ReadBytes(8);
        tables.Add(stringIds.Select(t => t / 2).ToArray());
        for (int i = 0; i < stringIds.Length; i++)
            stringIds[i] = (byte)(stringIds[i] / 2);
        writer.Write(stringIds);

        reader.BaseStream.Position = 0x4A80 + 0x10;
        stringIds = reader.ReadBytes(4);
        tables.Add(stringIds.Select(t => t / 2).ToArray());
        for (int i = 0; i < stringIds.Length; i++)
            stringIds[i] = (byte)(stringIds[i] / 2);
        writer.Write(stringIds);

        for (int i = 0; i < 4; i++)
            writer.Write((byte)0);

        tables.Add(Enumerable.Range(0, 4).ToArray());

        return tables.ToArray();
    }

    [DebuggerDisplay("{Data}")]
    private struct HPAttr
    {
        public byte Data;

        public readonly int GetHP(int type)
        {
            return (type & 1) switch
            {
                0 => Data & 0xF0,
                _ => Data << 4
            };
        }
    }

    private static HPAttr[] ReadTranslateHitPoints(BinaryReader reader, BinaryWriter writer)
    {
        const int EnemyHP = 0x1FB4E + 0x10;

        reader.BaseStream.Position = EnemyHP;
        var hpBytes = reader.ReadBytes(128);
        writer.Write(hpBytes);
        return hpBytes.Select(t => new HPAttr { Data = t }).ToArray();
    }

    private static int[] ReadTranslatePlayerDamage(BinaryReader reader, BinaryWriter writer)
    {
        const int PlayerDamage = 0x72BA + 0x10;

        reader.BaseStream.Position = PlayerDamage;
        var bytes = reader.ReadBytes(128);
        writer.Write(bytes);
        return bytes.Select(damageByte => ((damageByte & 0x0F) << 8) | (damageByte & 0xF0)).ToArray();
    }

    private static TableResource<byte> ExtractObjLists(Options options)
    {
        const int ObjListDir = 0x1473F + 16;
        const int ObjLists = 0x14676 + 16;

        byte[] lists = null;
        ushort[] listPtrs = null;

        using (var reader = options.GetBinaryReader())
        {
            reader.BaseStream.Position = ObjLists;
            lists = reader.ReadBytes(ObjListDir - ObjLists);

            reader.BaseStream.Position = ObjListDir;
            listPtrs = new ushort[30];
            for (int i = 0; i < listPtrs.Length; i++)
            {
                listPtrs[i] = reader.ReadUInt16();
            }
        }

        var filePath = "objLists.tab";
        using (var writer = options.AddBinaryWriter(filePath))
        {
            writer.Write((ushort)listPtrs.Length);
            for (int i = 0; i < listPtrs.Length; i++)
            {
                ushort ptr = (ushort)(listPtrs[i] - listPtrs[0]);
                writer.Write(ptr);
            }

            writer.Write(lists);

            Utility.PadStream(writer.BaseStream);
        }

        return TableResource<byte>.Load(options.Files[filePath]);
    }

    private static byte[] LoadArray8(string name, byte[] fieldLengths)
    {
        string resName = "ExtractLoz.Data." + name + ".csv";
        using (var stream = GetResourceStream(resName))
        {
            return DatafileReader.ReadHexArray8(stream, fieldLengths);
        }
    }

    private static Color[] GetPaletteStandInColors()
    {
        Color[] colors = new Color[]
        {
            Color.FromArgb( 0, 0, 0 ),
            Color.FromArgb( 16, 0x80, 0x00 ),
            Color.FromArgb( 32, 0x00, 0x80 ),
            Color.FromArgb( 48, 0x80, 0x80 ),
        };
        return colors;
    }

    private static Color[] GetPaletteContrastColors()
    {
        Color[] colors = new Color[]
        {
            Color.FromArgb( 0, 0, 0 ),
            Color.FromArgb( 64, 0, 0 ),
            Color.FromArgb( 128, 0, 0 ),
            Color.FromArgb( 192, 0, 0 ),
        };
        return colors;
    }

    private static void ExtractSprites(Options options)
    {
        using (var reader = options.GetBinaryReader())
        {
            Bitmap bmp = new Bitmap(8 * 16, 8 * 16);
            ExtractPlayerItemSprites(reader, bmp);
            options.AddFile("playerItem.png", bmp, ImageFormat.Png);
            WritePlayerItemSpecs(options);

            bmp = new Bitmap(8 * 16, 8 * 16);
            ExtractOverworldNpcSprites(reader, bmp);
            options.AddFile("owNpcs.png", bmp, ImageFormat.Png);
            WriteOverworldNpcSpecs(options);

            bmp = new Bitmap(8 * 16, 8 * 16);
            ExtractUnderworldNpcSprites(reader, bmp);
            options.AddFile("uwNpcs.png", bmp, ImageFormat.Png);
            WriteUnderworldNpcSpecs(options);

            bmp = new Bitmap(8 * 16, 8 * 16);
            ExtractUnderworldBossSpriteGroup(reader, bmp, "UWBossImage1257", UW1257BossCHR, 0);
            options.AddFile("uwBoss1257.png", bmp, ImageFormat.Png);
            WriteUnderworldBossSpecs(options, "uwBossSheet1257.tab", "UWBossSheet1257.csv");

            bmp = new Bitmap(8 * 16, 8 * 16);
            ExtractUnderworldBossSpriteGroup(reader, bmp, "UWBossImage3468", UW3468BossCHR, 0);
            options.AddFile("uwBoss3468.png", bmp, ImageFormat.Png);
            WriteUnderworldBossSpecs(options, "uwBossSheet3468.tab", "UWBossSheet3468.csv");

            bmp = new Bitmap(8 * 16, 8 * 16);
            ExtractUnderworldBossSpriteGroup(reader, bmp, "UWBossImage9", UW9BossCHR, 0);
            options.AddFile("uwBoss9.png", bmp, ImageFormat.Png);
            WriteUnderworldBossSpecs(options, "uwBossSheet9.tab", "UWBossSheet9.csv");
        }
    }

    private static ushort[,] LoadSpriteMap(string name)
    {
        string resName = "ExtractLoz.Data." + name + ".csv";
        using (var stream = GetResourceStream(resName))
        {
            return DatafileReader.ReadHexMap16(stream);
        }
    }

    private static void ExtractPlayerItemSprites(BinaryReader reader, Bitmap bmp)
    {
        ushort[,] map = LoadSpriteMap("PlayerItemsImage");
        Color[] colors = GetPaletteStandInColors();
        int y = 0;

        for (int r = 0; r < 8; r++)
        {
            int x = 0;

            for (int c = 0; c < 16; c++)
            {
                int code = map[r, c];
                int s = code & 0xFF;
                bool flipX = (code & 0x100) != 0;
                bool flipY = (code & 0x200) != 0;
                bool skip = (code & 0x8000) != 0;
                bool narrow = false;

                if ((code & 0xFF00) == 0x100 && s >= 0x62 && s < 0x6C)
                    narrow = true;

                if (narrow)
                    x--;
                if (!skip)
                    // We don't need monster sprites yet. So, the chrBase is useless right now.
                    DrawHalfSprite(reader, bmp, colors, x, y, OWMonsterCHR, s, false, flipX, flipY);
                if (narrow)
                    x++;
                x += 8;
            }

            y += 16;
        }

        // Draw the heart from the background tiles.
        DrawHalfSprite(reader, bmp, colors, 11 * 8, 4 * 16, Misc2CHR, 0x8E);

        // Draw the full triforce.
        DrawUWBossHalfSprite(reader, bmp, colors, 0x70, 0x50, UW9BossCHR, 0xF2);
        DrawUWBossHalfSprite(reader, bmp, colors, 0x78, 0x50, UW9BossCHR, 0xF4);
    }

    private static void WritePlayerItemSpecs(Options options)
    {
        var outPath = "playerItemsSheet.tab";
        using (var inStream = GetResourceStream("ExtractLoz.Data.PlayerItemsSheet.csv"))
        using (var outStream = options.AddStream(outPath))
        {
            DatafileReader.ConvertSpriteAnimTable(inStream, outStream);
        }
    }

    private static void ExtractOverworldNpcSprites(BinaryReader reader, Bitmap bmp)
    {
        ushort[,] map = LoadSpriteMap("OWNpcsImage");
        Color[] colors = GetPaletteStandInColors();
        int y = 0;

        for (int r = 0; r < 8; r++)
        {
            int x = 0;

            for (int c = 0; c < 16; c++)
            {
                int code = map[r, c];
                int s = code & 0xFF;
                bool flipX = (code & 0x100) != 0;
                bool flipY = (code & 0x200) != 0;
                bool skip = (code & 0x8000) != 0;

                if (!skip)
                    // We don't need monster sprites yet. So, the chrBase is useless right now.
                    DrawHalfSprite(reader, bmp, colors, x, y, OWMonsterCHR, s, false, flipX, flipY);
                x += 8;
            }

            y += 16;
        }
    }

    private static void ExtractUnderworldNpcSprites(BinaryReader reader, Bitmap bmp)
    {
        ExtractUnderworldNpcSpriteGroup(reader, bmp, "UWNpcsImageCommon", CommonUWSprites, 0);
        ExtractUnderworldNpcSpriteGroup(reader, bmp, "UWNpcsImage127", UW127SpriteCHR, 0x10);
        ExtractUnderworldNpcSpriteGroup(reader, bmp, "UWNpcsImage358", UW358SpriteCHR, 0x30);
        ExtractUnderworldNpcSpriteGroup(reader, bmp, "UWNpcsImage469", UW469SpriteCHR, 0x50);

        // Draw the moldorm ball from the player tiles.
        Color[] colors = GetPaletteStandInColors();
        DrawHalfSprite(reader, bmp, colors, 12 * 8, 0, 0, 0x44);
    }

    private static void ExtractUnderworldNpcSpriteGroup(
        BinaryReader reader, Bitmap bmp, string mapResName, int chrBase, int baseY)
    {
        ushort[,] map = LoadSpriteMap(mapResName);
        Color[] colors = GetPaletteStandInColors();
        int y = baseY;

        for (int r = 0; r < 8; r++)
        {
            int x = 0;

            for (int c = 0; c < 16; c++)
            {
                int code = map[r, c];
                int s = code & 0xFF;
                bool flipX = (code & 0x100) != 0;
                bool flipY = (code & 0x200) != 0;
                bool skip = (code & 0x8000) != 0;

                if (!skip)
                    DrawUWHalfSprite(reader, bmp, colors, x, y, chrBase, s, false, flipX, flipY);
                x += 8;
            }

            y += 16;
        }
    }

    private static void ExtractUnderworldBossSpriteGroup(
        BinaryReader reader, Bitmap bmp, string mapResName, int chrBase, int baseY)
    {
        ushort[,] map = LoadSpriteMap(mapResName);
        Color[] colors = GetPaletteStandInColors();
        int y = baseY;

        for (int r = 0; r < 8; r++)
        {
            int x = 0;

            for (int c = 0; c < 16; c++)
            {
                int code = map[r, c];
                int s = code & 0xFF;
                bool flipX = (code & 0x100) != 0;
                bool flipY = (code & 0x200) != 0;
                bool skip = (code & 0x8000) != 0;

                if (!skip)
                    DrawUWBossHalfSprite(reader, bmp, colors, x, y, chrBase, s, false, flipX, flipY);
                x += 8;
            }

            y += 16;
        }
    }

    private static void WriteOverworldNpcSpecs(Options options)
    {
        var outPath = "owNpcsSheet.tab";
        using (var inStream = GetResourceStream("ExtractLoz.Data.OWNpcsSheet.csv"))
        using (var outStream = options.AddStream(outPath))
        {
            DatafileReader.ConvertSpriteAnimTable(inStream, outStream);
        }
    }

    private static void WriteUnderworldNpcSpecs(Options options)
    {
        var outPath = "uwNpcsSheet.tab";
        using (var inStream = GetResourceStream("ExtractLoz.Data.UWNpcsSheet.csv"))
        using (var outStream = options.AddStream(outPath))
        {
            DatafileReader.ConvertSpriteAnimTable(inStream, outStream);
        }
    }

    private static void WriteUnderworldBossSpecs(Options options, string fileName, string resName)
    {
        var outPath = fileName;
        using (var inStream = GetResourceStream("ExtractLoz.Data." + resName))
        using (var outStream = options.AddStream(outPath))
        {
            DatafileReader.ConvertSpriteAnimTable(inStream, outStream);
        }
    }

    private static void ExtractOWSpriteVRAM(Options options)
    {
        using (var reader = options.GetBinaryReader())
        {
            Bitmap bmp = new Bitmap(8 * 16, 8 * 16);
            Color[] colors = GetPaletteContrastColors();
            int y = 0;
            int i = 0;

            for (int r = 0; r < 16; r++)
            {
                int x = 0;

                for (int c = 0; c < 16; c++)
                {
                    DrawSpriteTile(reader, bmp, colors, x, y, OWMonsterCHR, i);
                    i++;
                    x += 8;
                }
                y += 8;
            }

            options.AddFile("owSpriteVRAM.png", bmp, ImageFormat.Png);
        }
    }

    private static void DrawTile(BinaryReader reader, Bitmap bmp, Color[] colors, int x, int y,
        bool transparent = false, bool flipX = false, bool flipY = false)
    {
        // Read a whole tile's pixel data
        reader.Read(tileBuf, 0, tileBuf.Length);

        for (int v = 0; v < 8; v++)
        {
            for (int u = 0; u < 8; u++)
            {
                int lo = (tileBuf[v] >> (7 - u)) & 1;
                int hi = (tileBuf[v + 8] >> (7 - u)) & 1;
                int pixel = lo | (hi << 1);
                Color color = colors[pixel];

                int xOffset = flipX ? 7 - u : u;
                int yOffset = flipY ? 7 - v : v;

                if (pixel != 0 || !transparent)
                    bmp.SetPixel(x + xOffset, y + yOffset, color);
            }
        }
    }

    private static readonly int[] TileXOffset = new int[] { 0, 0, 8, 8 };
    private static readonly int[] TileYOffset = new int[] { 0, 8, 0, 8 };

    private static void DrawBgSquare(BinaryReader reader, Bitmap bmp, Color[] colors, int x, int y,
        int chrBase, byte[] indexes,
        bool transparent = false)
    {
        for (int i = 0; i < 4; i++)
        {
            int t = indexes[i];

            if (t < 0x70)
            {
                reader.BaseStream.Position = Misc1CHR + t * TileSize;
            }
            else if (t >= 0xF2)
            {
                reader.BaseStream.Position = Misc2CHR + (t - 0xF2) * TileSize;
            }
            else
            {
                reader.BaseStream.Position = chrBase + (t - 0x70) * TileSize;
            }

            DrawTile(reader, bmp, colors, x + TileXOffset[i], y + TileYOffset[i], transparent);
        }
    }

    private static void SeekBgTile(BinaryReader reader, int chrBase, int t)
    {
        if (t < 0x70)
        {
            reader.BaseStream.Position = Misc1CHR + t * TileSize;
        }
        else if (t >= 0xF2)
        {
            reader.BaseStream.Position = Misc2CHR + (t - 0xF2) * TileSize;
        }
        else
        {
            reader.BaseStream.Position = chrBase + (t - 0x70) * TileSize;
        }
    }

    private static void SeekUWSpriteTile(BinaryReader reader, int chrBase, int t)
    {
        if (t < 0x70)
        {
            reader.BaseStream.Position = LinkCHR + t * TileSize;
        }
        else if (t < 0x8E)
        {
            reader.BaseStream.Position = Common2CHR + (t - 0x70) * TileSize;
        }
        else if (t < 0x9E)
        {
            reader.BaseStream.Position = CommonUWSprites + (t - 0x8E) * TileSize;
        }
        else
        {
            reader.BaseStream.Position = chrBase + (t - 0x9E) * TileSize;
        }
    }

    private static void SeekUWBossTile(BinaryReader reader, int chrBase, int t)
    {
        if (t < 0x70)
        {
            reader.BaseStream.Position = LinkCHR + t * TileSize;
        }
        else if (t < 0x8E)
        {
            reader.BaseStream.Position = Common2CHR + (t - 0x70) * TileSize;
        }
        else if (t < 0x9E)
        {
            reader.BaseStream.Position = CommonUWSprites + (t - 0x8E) * TileSize;
        }
        else if (t < 0xC0)
        {
            throw new InvalidOperationException();
        }
        else
        {
            reader.BaseStream.Position = chrBase + (t - 0xC0) * TileSize;
        }
    }

    private const int LinkCHR = 0x807F + 16;
    private const int Common2CHR = 0x4DB4 + 16;
    private const int CommonUWSprites = 0xDCBB + 16;
    private const int OWMonsterCHR = 0xD15B + 16;
    private const int UW358SpriteCHR = 0xD87B + 16;
    private const int UW469SpriteCHR = 0xDA9B + 16;
    private const int UW127SpriteCHR = 0xDDBB + 16;
    private const int UW1257BossCHR = 0xDFDB + 16;
    private const int UW3468BossCHR = 0xE3DB + 16;
    private const int UW9BossCHR = 0xE7DB + 16;

    private static void DrawHalfSprite(BinaryReader reader, Bitmap bmp, Color[] colors,
        int x, int y,
        int chrBase, int index,
        bool transparent = false, bool flipX = false, bool flipY = false)
    {
        if (flipY)
        {
            DrawSpriteTile(reader, bmp, colors, x, y + 8, chrBase, index, transparent, flipX, flipY);
            DrawSpriteTile(reader, bmp, colors, x, y, chrBase, index + 1, transparent, flipX, flipY);
        }
        else
        {
            DrawSpriteTile(reader, bmp, colors, x, y, chrBase, index, transparent, flipX, flipY);
            DrawSpriteTile(reader, bmp, colors, x, y + 8, chrBase, index + 1, transparent, flipX, flipY);
        }
    }

    private static void DrawSpriteTile(BinaryReader reader, Bitmap bmp, Color[] colors,
        int x, int y,
        int chrBase, int index,
        bool transparent = false, bool flipX = false, bool flipY = false)
    {
        int t = index;

        if (t < 0x70)
        {
            reader.BaseStream.Position = LinkCHR + t * TileSize;
        }
        else if (t < 0x8E)
        {
            reader.BaseStream.Position = Common2CHR + (t - 0x70) * TileSize;
        }
        else
        {
            reader.BaseStream.Position = chrBase + (t - 0x8E) * TileSize;
        }

        DrawTile(reader, bmp, colors, x, y, transparent, flipX, flipY);
    }

    private static void DrawUWHalfSprite(BinaryReader reader, Bitmap bmp, Color[] colors,
        int x, int y,
        int chrBase, int index,
        bool transparent = false, bool flipX = false, bool flipY = false)
    {
        if (flipY)
        {
            DrawUWSpriteTile(reader, bmp, colors, x, y + 8, chrBase, index, transparent, flipX, flipY);
            DrawUWSpriteTile(reader, bmp, colors, x, y, chrBase, index + 1, transparent, flipX, flipY);
        }
        else
        {
            DrawUWSpriteTile(reader, bmp, colors, x, y, chrBase, index, transparent, flipX, flipY);
            DrawUWSpriteTile(reader, bmp, colors, x, y + 8, chrBase, index + 1, transparent, flipX, flipY);
        }
    }

    private static void DrawUWSpriteTile(BinaryReader reader, Bitmap bmp, Color[] colors,
        int x, int y,
        int chrBase, int index,
        bool transparent = false, bool flipX = false, bool flipY = false)
    {
        SeekUWSpriteTile(reader, chrBase, index);
        DrawTile(reader, bmp, colors, x, y, transparent, flipX, flipY);
    }

    private static void DrawUWBossHalfSprite(BinaryReader reader, Bitmap bmp, Color[] colors,
        int x, int y,
        int chrBase, int index,
        bool transparent = false, bool flipX = false, bool flipY = false)
    {
        if (flipY)
        {
            DrawUWBossTile(reader, bmp, colors, x, y + 8, chrBase, index, transparent, flipX, flipY);
            DrawUWBossTile(reader, bmp, colors, x, y, chrBase, index + 1, transparent, flipX, flipY);
        }
        else
        {
            DrawUWBossTile(reader, bmp, colors, x, y, chrBase, index, transparent, flipX, flipY);
            DrawUWBossTile(reader, bmp, colors, x, y + 8, chrBase, index + 1, transparent, flipX, flipY);
        }
    }

    private static void DrawUWBossTile(BinaryReader reader, Bitmap bmp, Color[] colors,
        int x, int y,
        int chrBase, int index,
        bool transparent = false, bool flipX = false, bool flipY = false)
    {
        SeekUWBossTile(reader, chrBase, index);
        DrawTile(reader, bmp, colors, x, y, transparent, flipX, flipY);
    }

    private static Stream GetResourceStream(string name)
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceStream(name);
    }

    private static void WriteListFile(Options options, string path, byte[] items)
    {
        using (var writer = new BinaryWriter(options.AddStream(path)))
        {
            writer.Write((ushort)items.Length);
            writer.Write(items);
        }
    }

    private static ListResource<T> GetListFile<T>(byte[] items)
        where T : struct
    {
        var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write((ushort)items.Length);
            writer.Write(items);
        }

        return ListResource<T>.Load(ms.ToArray());
    }
}
