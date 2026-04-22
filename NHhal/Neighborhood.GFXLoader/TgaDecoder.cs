using System.Runtime.CompilerServices;

namespace Neighborhood.GFXLoader;

/// <summary>
/// Decodes .tga sprite files used in NFH1 and NFH2.
///
/// Supported formats (from file analysis):
///   * 16bpp, alpha_bits=4  -> ARGB4444  (99% of sprites, with transparency)
///   * 16bpp, alpha_bits=0  -> RGB565    (GUI elements, no transparency)
///   * 24bpp, alpha_bits=0  -> RGB24     (rare, single file found)
///
/// All files share:
///   * Standard TGA header (18 bytes) + optional ID field
///   * image_descriptor bit 5 set -> top-to-bottom row order (non-standard TGA)
///   * TGA v2 footer (54 bytes, "TRUEVISION-XFILE.\0") -- ignored
///   * No RLE compression (image_type = 2, uncompressed RGB)
///
/// Output: always ARGB32 (4 bytes per pixel, B G R A order in memory = GDI+ native).
/// </summary>
public static class TgaDecoder
{
    /// <summary>
    /// Decodes raw .tga bytes into a <see cref="DecodedSprite"/>.
    /// Throws <see cref="InvalidTgaException"/> on malformed data.
    /// </summary>
    public static DecodedSprite Decode(byte[] tgaData, string debugName = "")
    {
        if (tgaData.Length < 18)
            throw new InvalidTgaException(debugName, "File too small to contain TGA header.");

        // -- Parse header ------------------------------------------------------
        int  idLength    = tgaData[0];
        int  imageType   = tgaData[2];   // 2 = uncompressed RGB
        int  width       = ReadU16(tgaData, 12);
        int  height      = ReadU16(tgaData, 14);
        int  bitsPerPixel = tgaData[16];
        int  imageDesc   = tgaData[17];
        int  alphaBits   = imageDesc & 0x0F;
        bool topToBottom = (imageDesc & 0x20) != 0;

        if (imageType != 2)
            throw new InvalidTgaException(debugName,
                $"Unsupported TGA image type {imageType}. Only uncompressed RGB (type 2) is supported.");

        if (width == 0 || height == 0)
            throw new InvalidTgaException(debugName, "Invalid dimensions (0).");

        int pixelOffset  = 18 + idLength;
        int bytesPerPixel = bitsPerPixel / 8;
        int expectedBytes = width * height * bytesPerPixel;

        if (tgaData.Length < pixelOffset + expectedBytes)
            throw new InvalidTgaException(debugName,
                $"File truncated: expected {pixelOffset + expectedBytes} bytes, got {tgaData.Length}.");

        // -- Decode pixels -> ARGB32 --------------------------------------------
        var argb32 = new byte[width * height * 4];

        if (bitsPerPixel == 16 && alphaBits == 4)
            DecodeArgb4444(tgaData, pixelOffset, argb32, width, height, topToBottom);
        else if (bitsPerPixel == 16 && alphaBits == 0)
            DecodeRgb565(tgaData, pixelOffset, argb32, width, height, topToBottom);
        else if (bitsPerPixel == 24)
            DecodeRgb24(tgaData, pixelOffset, argb32, width, height, topToBottom);
        else
            throw new InvalidTgaException(debugName,
                $"Unsupported pixel format: {bitsPerPixel}bpp, alpha_bits={alphaBits}.");

        return new DecodedSprite(width, height, argb32, alphaBits > 0);
    }

    // --- Format decoders -----------------------------------------------------

    /// <summary>
    /// ARGB4444: each 16-bit word = AAAA RRRR GGGG BBBB (high nibble first).
    /// Expand each 4-bit channel to 8-bit: value * 17  (0xF->255, 0x8->136, 0x0->0).
    /// Output: GDI+ BGRA byte order.
    /// </summary>
    private static void DecodeArgb4444(
        byte[] src, int srcOffset,
        byte[] dst, int width, int height, bool topToBottom)
    {
        for (int y = 0; y < height; y++)
        {
            int srcRow = srcOffset + y * width * 2;
            int dstRow = GetDstRow(y, height, topToBottom) * width * 4;

            for (int x = 0; x < width; x++)
            {
                ushort word = ReadU16(src, srcRow + x * 2);

                byte a = (byte)(((word >> 12) & 0xF) * 17);
                byte r = (byte)(((word >>  8) & 0xF) * 17);
                byte g = (byte)(((word >>  4) & 0xF) * 17);
                byte b = (byte)(( word        & 0xF) * 17);

                int dstIdx = dstRow + x * 4;
                dst[dstIdx    ] = b;  // GDI+ BGRA
                dst[dstIdx + 1] = g;
                dst[dstIdx + 2] = r;
                dst[dstIdx + 3] = a;
            }
        }
    }

    /// <summary>
    /// RGB565: RRRRR GGGGGG BBBBB -- no alpha channel, fully opaque.
    /// R: 5 bits -> expand to 8: (r5 * 255 + 15) / 31
    /// G: 6 bits -> expand to 8: (g6 * 255 + 31) / 63
    /// B: 5 bits -> same as R.
    /// Output: GDI+ BGRA byte order, A=255.
    /// </summary>
    private static void DecodeRgb565(
        byte[] src, int srcOffset,
        byte[] dst, int width, int height, bool topToBottom)
    {
        for (int y = 0; y < height; y++)
        {
            int srcRow = srcOffset + y * width * 2;
            int dstRow = GetDstRow(y, height, topToBottom) * width * 4;

            for (int x = 0; x < width; x++)
            {
                ushort word = ReadU16(src, srcRow + x * 2);

                byte r = Expand5To8((word >> 11) & 0x1F);
                byte g = Expand6To8((word >>  5) & 0x3F);
                byte b = Expand5To8( word        & 0x1F);

                int dstIdx = dstRow + x * 4;
                dst[dstIdx    ] = b;
                dst[dstIdx + 1] = g;
                dst[dstIdx + 2] = r;
                dst[dstIdx + 3] = 255; // fully opaque
            }
        }
    }

    /// <summary>
    /// RGB24: 3 bytes per pixel in B G R order (standard TGA RGB storage).
    /// Output: GDI+ BGRA byte order, A=255.
    /// </summary>
    private static void DecodeRgb24(
        byte[] src, int srcOffset,
        byte[] dst, int width, int height, bool topToBottom)
    {
        for (int y = 0; y < height; y++)
        {
            int srcRow = srcOffset + y * width * 3;
            int dstRow = GetDstRow(y, height, topToBottom) * width * 4;

            for (int x = 0; x < width; x++)
            {
                int srcIdx = srcRow + x * 3;
                int dstIdx = dstRow + x * 4;

                dst[dstIdx    ] = src[srcIdx    ]; // B
                dst[dstIdx + 1] = src[srcIdx + 1]; // G
                dst[dstIdx + 2] = src[srcIdx + 2]; // R
                dst[dstIdx + 3] = 255;
            }
        }
    }

    // --- Helpers -------------------------------------------------------------

    /// <summary>
    /// Maps source row index to destination row.
    /// TGA default is bottom-to-top; NFH files use top-to-bottom (bit 5 set).
    /// If top-to-bottom: destination row = source row (no flip needed).
    /// If bottom-to-top: destination row = (height - 1 - y).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDstRow(int y, int height, bool topToBottom) =>
        topToBottom ? y : height - 1 - y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    /// <summary>Expands 5-bit value to 8-bit: preserves full range 0->0, 31->255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Expand5To8(int v) => (byte)((v * 255 + 15) / 31);

    /// <summary>Expands 6-bit value to 8-bit: preserves full range 0->0, 63->255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Expand6To8(int v) => (byte)((v * 255 + 31) / 63);
}

/// <summary>
/// Decoded sprite: raw ARGB32 pixel data + dimensions.
/// Ready to be wrapped in a GDI+ Bitmap or passed to any renderer.
/// </summary>
public sealed class DecodedSprite
{
    public int    Width      { get; }
    public int    Height     { get; }

    /// <summary>
    /// Raw pixel data in GDI+ native format: BGRA, 4 bytes per pixel,
    /// row-major top-to-bottom, stride = Width * 4 (no padding).
    /// </summary>
    public byte[] Argb32Data { get; }

    /// <summary>True if this sprite has meaningful alpha channel (ARGB4444 source).</summary>
    public bool   HasAlpha   { get; }

    public DecodedSprite(int width, int height, byte[] argb32Data, bool hasAlpha)
    {
        Width      = width;
        Height     = height;
        Argb32Data = argb32Data;
        HasAlpha   = hasAlpha;
    }
}

/// <summary>Thrown when a .tga file cannot be decoded.</summary>
public sealed class InvalidTgaException : Exception
{
    public string SpriteName { get; }

    public InvalidTgaException(string spriteName, string reason)
        : base($"[TGA] Cannot decode '{spriteName}': {reason}")
    {
        SpriteName = spriteName;
    }
}
