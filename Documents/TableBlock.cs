using System.Collections.Generic;

namespace AvaloniaRichTextBoxPort.Documents;

public class TableBlock : Block
{
    public int Rows { get; set; } = 2;
    public int Columns { get; set; } = 2;
    public List<double> ColumnWidths { get; set; } = new();
    // User-specified minimum row heights (parallel to rows). Empty/0 means "auto" (content-driven);
    // the renderer uses Max(content height, this) so a manually dragged row can't shrink below its text.
    public List<double> RowHeights { get; set; } = new();
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

    private Paragraph NewCell() => new Paragraph { Inlines = { new Run { Text = "" } }, Parent = this };

    // Structural edits used by the context menu. Each keeps Rows/Columns, Cells, ColumnWidths and
    // (sparse) RowHeights consistent. RowHeights may be shorter than Rows (trailing = auto), so we
    // only shift entries that actually exist.
    public void InsertRow(int at)
    {
        at = System.Math.Clamp(at, 0, Rows);
        var row = new List<Paragraph>();
        for (int c = 0; c < Columns; c++) row.Add(NewCell());
        Cells.Insert(at, row);
        if (at < RowHeights.Count) RowHeights.Insert(at, 0);
        Rows++;
    }

    public void DeleteRow(int at)
    {
        if (Rows <= 1 || at < 0 || at >= Rows) return;
        Cells.RemoveAt(at);
        if (at < RowHeights.Count) RowHeights.RemoveAt(at);
        Rows--;
    }

    public void InsertColumn(int at)
    {
        at = System.Math.Clamp(at, 0, Columns);
        for (int r = 0; r < Rows; r++) Cells[r].Insert(at, NewCell());
        ColumnWidths.Insert(System.Math.Clamp(at, 0, ColumnWidths.Count), 100);
        Columns++;
    }

    public void DeleteColumn(int at)
    {
        if (Columns <= 1 || at < 0 || at >= Columns) return;
        for (int r = 0; r < Rows; r++) Cells[r].RemoveAt(at);
        if (at < ColumnWidths.Count) ColumnWidths.RemoveAt(at);
        Columns--;
    }

    public override TextElement Clone()
    {
        var tb = new TableBlock(Rows, Columns);
        tb.Cells.Clear();
        tb.ColumnWidths.Clear();
        foreach(var w in ColumnWidths) tb.ColumnWidths.Add(w);
        foreach(var h in RowHeights) tb.RowHeights.Add(h);

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
