using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// N6-6 soft limit: GetImageCount + the edge-triggered RecommendedImageLimitExceeded warning.
// CheckImageLimit (internal) is normally driven by the TextChanged flush; tests call it directly.
public class ImageLimitTests
{
    private static FlowDocument DocWithImages(int blockImages, int inlineImages = 0, int tableCellImages = 0)
    {
        var doc = new FlowDocument();
        for (int i = 0; i < blockImages; i++)
            doc.Blocks.Add(new ImageBlock());
        if (inlineImages > 0)
        {
            var p = new Paragraph();
            for (int i = 0; i < inlineImages; i++)
                p.Inlines.Add(new InlineImage());
            doc.Blocks.Add(p);
        }
        if (tableCellImages > 0)
        {
            var t = new TableBlock(1, 1);
            for (int i = 0; i < tableCellImages; i++)
                t.Cells[0][0].Para.Inlines.Add(new InlineImage());
            doc.Blocks.Add(t);
        }
        return doc;
    }

    [AvaloniaFact]
    public void GetImageCount_CountsBlockInlineAndTableCellImages()
    {
        var ed = new RichEditor();
        ed.Document = DocWithImages(blockImages: 2, inlineImages: 3, tableCellImages: 1);
        Assert.Equal(6, ed.GetImageCount());
    }

    [AvaloniaFact]
    public void ExceedingLimit_RaisesEventOnce()
    {
        var ed = new RichEditor { MaxRecommendedImages = 2 };
        ed.Document = DocWithImages(3);
        int fired = 0;
        ed.RecommendedImageLimitExceeded += (_, _) => fired++;

        ed.CheckImageLimit();
        ed.CheckImageLimit(); // still over — must not re-fire
        Assert.Equal(1, fired);
    }

    [AvaloniaFact]
    public void DroppingBelowLimit_RearmsTheWarning()
    {
        var ed = new RichEditor { MaxRecommendedImages = 2 };
        ed.Document = DocWithImages(3);
        int fired = 0;
        ed.RecommendedImageLimitExceeded += (_, _) => fired++;

        ed.CheckImageLimit();
        // NormalizeBlocks interleaves paragraphs between the images, so remove an image explicitly.
        ed.Document!.Blocks.Remove(ed.Document.Blocks.First(b => b is ImageBlock)); // back to 2 (== limit)
        ed.CheckImageLimit();
        ed.Document.Blocks.Add(new ImageBlock()); // over again
        ed.CheckImageLimit();
        Assert.Equal(2, fired);
    }

    [AvaloniaFact]
    public void AtOrUnderLimit_DoesNotFire()
    {
        var ed = new RichEditor { MaxRecommendedImages = 3 };
        ed.Document = DocWithImages(3);
        int fired = 0;
        ed.RecommendedImageLimitExceeded += (_, _) => fired++;

        ed.CheckImageLimit();
        Assert.Equal(0, fired);
    }

    [AvaloniaFact]
    public void ZeroLimit_DisablesTheWarning()
    {
        var ed = new RichEditor { MaxRecommendedImages = 0 };
        ed.Document = DocWithImages(100);
        int fired = 0;
        ed.RecommendedImageLimitExceeded += (_, _) => fired++;

        ed.CheckImageLimit();
        Assert.Equal(0, fired);
    }
}
