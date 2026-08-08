# Reads each merge variant with Word and reports the geometry Word actually built.
# READ-ONLY: no Documents.Add, no SaveAs -- that path hangs headless.
$ErrorActionPreference = "Continue"
$dir = "$PSScriptRoot\merge-probe"
$word = New-Object -ComObject Word.Application
$word.Visible = $false; $word.DisplayAlerts = 0
try {
    foreach ($f in (Get-ChildItem $dir -Filter *.rtf | Sort-Object Name)) {
        Write-Host ("`n=== {0} ===" -f $f.Name) -ForegroundColor Cyan
        try {
            $d = $word.Documents.Open($f.FullName, $false, $true)
            if ($d.Tables.Count -eq 0) { Write-Host "  NO TABLE" -ForegroundColor Red; $d.Close(); continue }
            $t = $d.Tables.Item(1)
            $cells = @()
            foreach ($c in $t.Range.Cells) {
                $cells += ("r{0}c{1}='{2}' w={3}" -f $c.RowIndex, $c.ColumnIndex,
                           ($c.Range.Text -replace "`r|`a", ""), [Math]::Round($c.Width))
            }
            Write-Host ("  cells={0}   (3 declared columns; a merge of col1+col2 should give 2)" -f $cells.Count)
            $cells | ForEach-Object { Write-Host ("    " + $_) }
            $d.Close()
        } catch {
            Write-Host ("  ERROR: " + $_.Exception.Message) -ForegroundColor Red
        }
    }
}
finally { $word.Quit(); [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null }
