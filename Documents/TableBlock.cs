using System.Collections.Generic;

namespace AvaloniaRichTextBoxPort.Documents;

public class TableBlock : Block
{
    public int Rows { get; set; } = 2;
    public int Columns { get; set; } = 2;
    public List<double> ColumnWidths { get; set; } = new();
    public List<List<Paragraph>> Cells { get; set; } = new();

    public TableBlock()
    {
        InitializeCells(Rows, Columns);
    }

    public TableBlock(int rows, int cols)
    {
        Rows = rows;
        Columns = cols;
        InitializeCells(Rows, Columns);
    }

    private void InitializeCells(int rows, int cols)
    {
        for (int c = 0; c < cols; c++)
        {
            ColumnWidths.Add(100); // Default width
        }
        for (int r = 0; r < rows; r++)
        {
            var row = new List<Paragraph>();
            for (int c = 0; c < cols; c++)
            {
                row.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
            }
            Cells.Add(row);
        }
    }

    public override TextElement Clone()
    {
        var tb = new TableBlock(Rows, Columns);
        tb.Cells.Clear();
        tb.ColumnWidths.Clear();
        foreach(var w in ColumnWidths) tb.ColumnWidths.Add(w);

        for (int r = 0; r < Rows; r++)
        {
            var row = new List<Paragraph>();
            for (int c = 0; c < Columns; c++)
            {
                var pClone = Cells[r][c].Clone() as Paragraph;
                if (pClone != null)
                {
                    pClone.Parent = tb;
                    row.Add(pClone);
                }
            }
            tb.Cells.Add(row);
        }
        return tb;
    }
}
