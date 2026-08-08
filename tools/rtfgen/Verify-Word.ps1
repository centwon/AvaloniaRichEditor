# Verifies the generated RTF by MEASURING it inside Word's own object model.
#
# The point: Word parses the file with Word's parser, and then we ask Word questions about the result
# ("does this table have borders", "how many cells does this row have", "what is the header's text").
# That is the same discipline the HTML half used -- measure, do not eyeball -- applied to the consumer
# whose disagreement with this writer is what rounds 4-9 kept discovering.
#
# READ-ONLY ON PURPOSE. Documents.Add + SaveAs2 (asking Word to WRITE a reference file) hangs headless
# and leaves invisible WINWORD processes behind. Open, ask, close.

param([string]$OutDir = "$PSScriptRoot\bin\Debug\net10.0\out")

$ErrorActionPreference = "Stop"
$pass = 0; $fail = 0

function Check($id, $desc, $ok, $detail) {
    if ($ok) { $script:pass++; Write-Host ("  PASS  #{0,-3} {1}" -f $id, $desc) -ForegroundColor Green }
    else     { $script:fail++; Write-Host ("  FAIL  #{0,-3} {1}" -f $id, $desc) -ForegroundColor Red }
    if ($detail) { Write-Host ("          -> {0}" -f $detail) -ForegroundColor DarkGray }
}
function Clean($s) { return ($s -replace "`r|`a", "") }

# A paragraph's bottom border, or 0 when Word will not answer for that paragraph.
function BottomBorder($p) { try { return $p.Borders.Item(-3).LineStyle } catch { return 0 } }

# Cells per row, via Range.Cells. Rows.Item(n).Cells throws outright on a vertically merged table
# ("cannot access individual rows because the cells are vertically merged"), which is why this walks
# the whole range and groups by RowIndex instead.
function CellsPerRow($t) {
    $byRow = @{}
    foreach ($c in $t.Range.Cells) {
        if (-not $byRow.ContainsKey($c.RowIndex)) { $byRow[$c.RowIndex] = @() }
        $byRow[$c.RowIndex] += (Clean $c.Range.Text)
    }
    return $byRow
}

# Finds the top-level table whose text contains a marker, so the checks do not depend on table order.
function TableWith($d, $needle) {
    foreach ($t in $d.Tables) { if ((Clean $t.Range.Text).Contains($needle)) { return $t } }
    return $null
}

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

function Open-Doc($name) {
    $p = Join-Path $OutDir $name
    if (-not (Test-Path $p)) { throw "missing $p" }
    return $word.Documents.Open($p, $false, $true)   # ConfirmConversions=false, ReadOnly=true
}

try {
    # ---------------------------------------------------------------- 01-kitchen-sink
    Write-Host "`n01-kitchen-sink.rtf" -ForegroundColor Cyan
    $d = Open-Doc "01-kitchen-sink.rtf"
    $paras = @(); foreach ($p in $d.Paragraphs) { $paras += $p }
    $texts = $paras | ForEach-Object { Clean $_.Range.Text }

    $bracket = $texts | Where-Object { $_.Contains("사이에[") } | Select-Object -First 1
    $gap = ""; if ($bracket -match '\[([^\]]*)\]') { $gap = $Matches[1] }
    Check 1 "연속 공백 4칸 유지" ($gap.Length -eq 4) "대괄호 사이 $($gap.Length)칸"

    # RTF has no list element that HWP honours, so round 8 writes the marker as LITERAL TEXT plus a
    # hanging indent. listType=0 in Word is therefore correct BY DESIGN. What must hold is that the
    # marker is visible, the indent steps per level, and the hanging indent keeps the text off the
    # marker (without one the tab flew to the reader's next default stop).
    $lvl = @()
    foreach ($p in $paras) {
        $t = Clean $p.Range.Text
        if ($t -match '^\s*[•▪◦–]\s*\t?\s*(\d)단계') { $lvl += ,@([int]$Matches[1], $p.LeftIndent, $p.FirstLineIndent) }
    }
    Check 3 "글머리 마커가 Word에 보임(설계상 literal text)" ($lvl.Count -eq 3) "마커 붙은 항목 $($lvl.Count) 개"
    $stair = ($lvl.Count -eq 3) -and ($lvl[0][1] -lt $lvl[1][1]) -and ($lvl[1][1] -lt $lvl[2][1])
    Check 3 "단계마다 들여쓰기가 커짐" $stair ("leftIndent = " + (($lvl | ForEach-Object { $_[1] }) -join ", "))
    $hanging = ($lvl.Count -eq 3) -and (($lvl | Where-Object { $_[2] -ge 0 }).Count -eq 0)
    Check 3 "행잉 인덴트가 있어 마커와 본문이 겹치지 않음" $hanging ("firstLineIndent = " + (($lvl | ForEach-Object { $_[2] }) -join ", "))

    $iWi = [array]::IndexOf($texts, ($texts | Where-Object { $_ -eq "위 문단." } | Select-Object -First 1))
    $blankOk = ($iWi -ge 0) -and ($texts[$iWi + 1].Trim() -eq "") -and ($texts[$iWi + 2] -like "아래 문단*")
    Check 4 "저자가 넣은 빈 줄이 정확히 하나" $blankOk "위/아래 문단 사이: '$($texts[$iWi+1])'"

    $cen = $paras | Where-Object { $_.Range.Text -like "가운데 정렬*" } | Select-Object -First 1
    $rig = $paras | Where-Object { $_.Range.Text -like "오른쪽 정렬*" } | Select-Object -First 1
    $lef = $paras | Where-Object { $_.Range.Text -like "왼쪽 정렬*" } | Select-Object -First 1
    Check 5 "정렬 3종 (가운데=1 오른쪽=2 왼쪽=0)" `
        (($cen.Alignment -eq 1) -and ($rig.Alignment -eq 2) -and ($lef.Alignment -eq 0)) `
        "center=$($cen.Alignment) right=$($rig.Alignment) left=$($lef.Alignment)"

    Check 6 "이미지 2개(블록+인라인)" ($d.InlineShapes.Count -eq 2) "InlineShapes=$($d.InlineShapes.Count)"

    $picIdx = -1
    for ($i = 0; $i -lt $paras.Count; $i++) {
        if ($paras[$i].Range.InlineShapes.Count -gt 0 -and $paras[$i].Range.Text.Trim().Length -le 1) { $picIdx = $i; break }
    }
    $after = ""; $noGrow = $true
    if ($picIdx -ge 0 -and $picIdx + 1 -lt $paras.Count) { $after = $texts[$picIdx + 1]; $noGrow = $after.Trim() -ne "" }
    Check 6 "이미지 밑에 빈 문단이 생기지 않음" $noGrow "이미지 다음 문단: '$after'"

    $rule = @($paras | Where-Object { (Clean $_.Range.Text).Trim() -eq "" -and (BottomBorder $_) -ne 0 })
    Check 7 "구분선이 아래 테두리로 살아 있음" ($rule.Count -ge 1) "테두리 있는 빈 문단 $($rule.Count) 개"

    Check 8 "인라인 표가 표로 들어옴(RTF엔 인라인 표가 없어 문단 분할)" ($d.Tables.Count -ge 2) "Tables=$($d.Tables.Count)"
    $d.Saved = $true; $d.Close()

    # ---------------------------------------------------------------- 03-tables
    Write-Host "`n03-tables.rtf" -ForegroundColor Cyan
    $d = Open-Doc "03-tables.rtf"

    $t1 = TableWith $d "가로 병합 2칸"
    $borderOk = $true
    foreach ($s in @(-1, -2, -3, -4)) { if ($t1.Borders.Item($s).LineStyle -eq 0) { $borderOk = $false } }
    $inner = ($t1.Borders.Item(-5).LineStyle -ne 0) -and ($t1.Borders.Item(-6).LineStyle -ne 0)
    Check 10 "표 테두리(바깥 4면 + 안쪽 가로/세로)" ($borderOk -and $inner) "outer=$borderOk inner=$inner"

    $rows = CellsPerRow $t1
    $total = 0; foreach ($k in $rows.Keys) { $total += $rows[$k].Count }
    $r1 = $rows[1].Count
    Check 11 "가로 병합: 1행이 3칸(4칸이 아님)" ($r1 -eq 3) "1행 = $r1 칸 [$(($rows[1] | ForEach-Object { $_.Trim() }) -join ' | ')]"
    Check 11 "세로 병합: 전체 10칸(병합 없으면 12)" ($total -eq 10) "총 $total 칸"
    Check 11 "병합된 칸에 글자가 들어 있음(폭 0 셀에 갇히지 않음)" ($rows[1][0].Trim() -eq "가로 병합 2칸") "1행 1칸 = '$($rows[1][0].Trim())'"

    $shaded = 0
    foreach ($c in $t1.Range.Cells) { if ($c.Shading.BackgroundPatternColor -ne -16777216) { $shaded++ } }
    Check 12 "셀 배경이 정확히 1칸" ($shaded -eq 1) "배경 있는 셀 $shaded 개"

    $t2 = TableWith $d "글머리 항목"
    $a1 = $t2.Cell(1, 1).Range.Paragraphs.Item(1).Alignment
    $a2 = $t2.Cell(1, 2).Range.Paragraphs.Item(1).Alignment
    $b3 = Clean $t2.Cell(1, 3).Range.Text
    $h4 = $t2.Cell(1, 4).Range.Paragraphs.Item(1).Range.Bold
    Check 13 "셀 안 정렬 유지 (가운데=1 오른쪽=2)" (($a1 -eq 1) -and ($a2 -eq 2)) "center=$a1 right=$a2"
    Check 13 "셀 안 글머리 마커가 보임" ($b3 -match '[•▪◦–]') "3칸 = '$($b3.Trim())'"
    Check 13 "셀 안 제목이 굵게" ($h4 -ne 0) "bold=$h4"

    $t3 = TableWith $d "첫 번째 문단"
    $cellParas = $t3.Cell(1, 1).Range.Paragraphs.Count
    Check 14 "셀 안 두 문단이 두 문단으로" ($cellParas -eq 2) "문단 $cellParas 개"
    Check 15 "셀 안 이미지" ($t3.Cell(1, 2).Range.InlineShapes.Count -eq 1) "셀 안 그림 $($t3.Cell(1,2).Range.InlineShapes.Count) 개"

    $nested = 0; $hostT = $null
    foreach ($t in $d.Tables) { $nested += $t.Tables.Count; if ($t.Tables.Count -ge 1 -and $hostT -eq $null) { $hostT = $t } }
    Check 16 "중첩 표가 중첩으로 들어옴" ($nested -ge 1) "중첩 표 $nested 개"
    $hostCell = ""
    if ($hostT) { $hostCell = Clean $hostT.Cell(1, 1).Range.Text }
    Check 16 "중첩 표 앞/뒤 문단이 순서대로 남음" `
        (($hostCell -like "*앞 문단*") -and ($hostCell -like "*뒤 문단*")) `
        "부모 셀 = '$($hostCell.Substring(0, [Math]::Min(50, $hostCell.Length)).Trim())'"

    $lastTableEnd = $d.Tables.Item($d.Tables.Count).Range.End
    $picAfter = $true
    foreach ($sh in $d.InlineShapes) { if ($sh.Range.Start -lt $lastTableEnd -and $sh.Range.Tables.Count -eq 0) { $picAfter = $false } }
    Check 17 "표 뒤 이미지가 표 뒤에 있음" $picAfter "이미지 $($d.InlineShapes.Count) 개"
    $d.Saved = $true; $d.Close()

    # ---------------------------------------------------------------- 04-page-chrome
    Write-Host "`n04-page-chrome.rtf" -ForegroundColor Cyan
    $d = Open-Doc "04-page-chrome.rtf"
    $sec = $d.Sections.Item(1)
    $hdr = Clean $sec.Headers.Item(1).Range.Text
    $ftr = Clean $sec.Footers.Item(1).Range.Text
    Check 18 "머리글이 머리글 밴드에 있음" ($hdr -like "*머리글*") "header='$($hdr.Trim())'"
    $body1 = Clean $d.Paragraphs.Item(1).Range.Text
    Check 18 "머리글이 본문 첫 문단으로 새지 않음" ($body1 -notlike "*머리글 — AvaloniaRichEditor 검증*") "본문 1문단='$($body1.Trim())'"
    Check 19 "바닥글에 텍스트가 있음" ($ftr -like "*바닥글 텍스트*") "footer='$($ftr.Trim())'"
    Check 19 "바닥글 쪽번호가 필드로 있음" ($sec.Footers.Item(1).Range.Fields.Count -ge 1) "필드 $($sec.Footers.Item(1).Range.Fields.Count) 개"
    $pages = $d.ComputeStatistics(2)
    Check 20 "2페이지 이상" ($pages -ge 2) "$pages 페이지"
    $d.Saved = $true; $d.Close()
}
catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; Write-Host $_.ScriptStackTrace }
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}

Write-Host "`n=============================" -ForegroundColor Cyan
Write-Host ("PASS {0}   FAIL {1}" -f $pass, $fail) -ForegroundColor $(if ($fail -eq 0) { "Green" } else { "Red" })
