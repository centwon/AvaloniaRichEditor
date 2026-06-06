using System.Text.Json;
using System.Text.Json.Serialization;
using AvaloniaRichTextBoxPort.Documents;

namespace AvaloniaRichTextBoxPort.Formatters;

[JsonSerializable(typeof(FlowDocumentDto))]
internal partial class DocumentJsonContext : JsonSerializerContext { }

public static class DocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = DocumentJsonContext.Default
    };

    // A very simplistic DTO mapping for Phase 4 PoC
    public static string Serialize(FlowDocument document)
    {
        // In a real port, we'd use XamlServices or a custom RTF writer.
        // Here we use JSON to demonstrate document persistence.
        var dto = new FlowDocumentDto();
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph p)
            {
                var pDto = new ParagraphDto();
                foreach (var inline in p.Inlines)
                {
                    if (inline is Run r)
                    {
                        pDto.Runs.Add(new RunDto 
                        { 
                            Text = r.Text, 
                            IsBold = r.FontWeight == Avalonia.Media.FontWeight.Bold 
                        });
                    }
                }
                dto.Paragraphs.Add(pDto);
            }
        }
        return JsonSerializer.Serialize(dto, DocumentJsonContext.Default.FlowDocumentDto);
    }

    public static FlowDocument Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize(json, DocumentJsonContext.Default.FlowDocumentDto);
        var doc = new FlowDocument();
        if (dto != null)
        {
            foreach (var pDto in dto.Paragraphs)
            {
                var p = new Paragraph();
                foreach (var rDto in pDto.Runs)
                {
                    p.Inlines.Add(new Run 
                    { 
                        Text = rDto.Text, 
                        FontWeight = rDto.IsBold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal 
                    });
                }
                doc.Blocks.Add(p);
            }
        }
        return doc;
    }
}

public class FlowDocumentDto
{
    public System.Collections.Generic.List<ParagraphDto> Paragraphs { get; set; } = new();
}

public class ParagraphDto
{
    public System.Collections.Generic.List<RunDto> Runs { get; set; } = new();
}

public class RunDto
{
    public string? Text { get; set; }
    public bool IsBold { get; set; }
}
