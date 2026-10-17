using Microsoft.Extensions.Options;
using PDFtoImage;
using ReceiptReader.Application.Abstractions;
using SkiaSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ReceiptReader.Infrastructure.Extraction;

/// <summary>
/// EXIF auto-rotate (phone photos), cap the long edge (vision models resize anyway; smaller = faster
/// and fewer image tokens), mild contrast boost for faded thermal paper, re-encode as JPEG.
/// PDFs are rendered page by page (up to <see cref="ExtractionOptions.MaxPdfPages"/>) with PDFium.
/// </summary>
public sealed class ImageSharpPreprocessor(IOptions<ExtractionOptions> options) : IImagePreprocessor
{
    public async Task<IReadOnlyList<byte[]>> PrepareAsync(Stream source, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        if (!IsPdf(bytes))
        {
            using var image = Image.Load(bytes);
            return [await EncodeAsync(image, contrast: true, ct)];
        }

        var opt = options.Value;
        var pageCount = Math.Min(Conversion.GetPageCount(bytes), opt.MaxPdfPages);
        var pages = new List<byte[]>(pageCount);
        for (var i = 0; i < pageCount; i++)
        {
            using var png = new MemoryStream();
            Conversion.SavePng(png, bytes, i, options: new RenderOptions(Dpi: opt.PdfDpi, WithAnnotations: true, BackgroundColor: SKColors.White));
            png.Position = 0;
            using var page = await Image.LoadAsync(png, ct);
            // Digital PDFs are already crisp; the contrast boost only helps photos of thermal paper.
            pages.Add(await EncodeAsync(page, contrast: false, ct));
        }
        return pages;
    }

    private static bool IsPdf(ReadOnlySpan<byte> bytes) => bytes.StartsWith("%PDF-"u8);

    private async Task<byte[]> EncodeAsync(Image image, bool contrast, CancellationToken ct)
    {
        var opt = options.Value;
        image.Mutate(x =>
        {
            x.AutoOrient();
            var longEdge = Math.Max(image.Width, image.Height);
            if (longEdge > opt.MaxImageEdge)
                x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(opt.MaxImageEdge, opt.MaxImageEdge) });
            if (contrast && opt.ContrastBoost > 0)
                x.Contrast(1f + opt.ContrastBoost);
        });

        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 90 }, ct);
        return ms.ToArray();
    }
}
