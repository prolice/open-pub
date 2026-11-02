# Exporte un fichier .pub vers un format ouvert (JSON + images + PDF de reference)
# en pilotant Microsoft Publisher via COM.
# Usage : powershell -ExecutionPolicy Bypass -File export-pub.ps1 -Pub "C:\...\fichier.pub" -Out "C:\...\converted"
param(
  [Parameter(Mandatory=$true)][string]$Pub,
  [Parameter(Mandatory=$true)][string]$Out,
  [switch]$Pdf
)
$ErrorActionPreference = 'Stop'

$name = [IO.Path]::GetFileNameWithoutExtension($Pub)
$dir = Join-Path $Out $name
$assets = Join-Path $dir 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function Hex($rgb) {
  # COM renvoie BGR
  if ($rgb -eq $null -or $rgb -lt 0) { return $null }
  $r = $rgb -band 0xFF; $g = ($rgb -shr 8) -band 0xFF; $b = ($rgb -shr 16) -band 0xFF
  return ('#{0:x2}{1:x2}{2:x2}' -f $r, $g, $b)
}
function Safe($sb, $default = $null) { try { & $sb } catch { $default } }
function Tri($v) { if ($v -eq -1 -or $v -eq $true) { $true } elseif ($v -eq 0 -or $v -eq $false) { $false } else { $null } }

$script:imgCount = 0
function Save-Picture($obj, $prefix) {
  $script:imgCount++
  $file = "$prefix-$($script:imgCount).png"
  $path = Join-Path $assets $file
  try { $obj.SaveAsPicture($path); if (Test-Path $path) { return "assets/$file" } } catch {}
  return $null
}

function Font-Of($font) {
  [ordered]@{
    family    = Safe { $font.Name }
    size      = Safe { [double]$font.Size }
    bold      = Tri (Safe { $font.Bold })
    italic    = Tri (Safe { $font.Italic })
    underline = (Safe { $font.Underline } 0) -gt 0
    color     = Hex (Safe { $font.Color.RGB })
  }
}
# Une mise en forme est "homogene" si aucune propriete ne renvoie de valeur mixte/nulle
function Uniform($f) {
  if ([string]::IsNullOrEmpty($f.family)) { return $false }
  if ($f.size -eq $null -or $f.size -le 0 -or $f.size -ge 9999) { return $false }
  if ($f.bold -eq $null -or $f.italic -eq $null -or $f.color -eq $null) { return $false }
  return $true
}
$script:comCalls = 0
# Decoupe [start, start+len) en segments de mise en forme homogene (dichotomie)
function Split-Runs($range, $start, $len, [System.Collections.ArrayList]$acc) {
  if ($len -le 0) { return }
  $r = $range.Characters($start, $len)
  $script:comCalls++
  $f = Font-Of $r.Font
  if ($len -eq 1 -or (Uniform $f)) {
    [void]$acc.Add([ordered]@{ text = [string]$r.Text; font = $f })
    return
  }
  $half = [Math]::Floor($len / 2)
  Split-Runs $range $start $half $acc
  Split-Runs $range ($start + $half) ($len - $half) $acc
}
function Merge-Runs($runs) {
  $out = New-Object System.Collections.ArrayList
  foreach ($r in $runs) {
    $last = if ($out.Count) { $out[$out.Count - 1] } else { $null }
    if ($last -and (($last.font | ConvertTo-Json -Compress) -eq ($r.font | ConvertTo-Json -Compress))) { $last.text += $r.text }
    else { [void]$out.Add($r) }
  }
  return ,$out
}

function Paragraphs-Of($tr) {
  $paras = New-Object System.Collections.ArrayList
  $full = [string](Safe { $tr.Text } '')
  if ($full.Length -eq 0) { return ,$paras }
  # Texte de reference : on decoupe nous-memes sur les fins de paragraphe
  $pos = 1
  $chunks = $full -split "`r"
  for ($i = 0; $i -lt $chunks.Count; $i++) {
    $txt = $chunks[$i]
    $plen = $txt.Length
    if ($i -eq $chunks.Count - 1 -and $plen -eq 0) { break }
    $pr = Safe { $tr.Characters($pos, [Math]::Max(1, $plen)) }
    $pf = Safe { $pr.ParagraphFormat }
    $align = switch (Safe { $pf.Alignment }) { 1 {'center'} 2 {'right'} 3 {'left'} 4 {'justify'} default {'left'} }
    $acc = New-Object System.Collections.ArrayList
    if ($plen -gt 0) {
      try { Split-Runs $tr $pos $plen $acc } catch { [void]$acc.Add([ordered]@{ text = $txt; font = $null }) }
    }
    $runs = Merge-Runs $acc
    # Le texte COM peut differer legerement (caracteres speciaux) : on garde le texte brut aussi
    [void]$paras.Add([ordered]@{
      text         = ($txt -replace "`v", "`n")
      align        = $align
      spaceBefore  = Safe { [double]$pf.SpaceBefore }
      spaceAfter   = Safe { [double]$pf.SpaceAfter }
      lineSpacing  = Safe { [double]$pf.LineSpacing }
      lineRule     = Safe { [int]$pf.LineSpacingRule }
      firstIndent  = Safe { [double]$pf.FirstLineIndent }
      leftIndent   = Safe { [double]$pf.LeftIndent }
      rightIndent  = Safe { [double]$pf.RightIndent }
      runs         = $runs
    })
    $pos += $plen + 1
  }
  return ,$paras
}

# Types Publisher : 1 forme auto, 6 groupe, 9 ligne, 11 image liee, 12 table, 13 image, 15 WordArt, 17 zone de texte
function Shape-Of($s, $prefix) {
  $type = Safe { [int]$s.Type } 0
  $o = [ordered]@{
    name = Safe { $s.Name }; type = $type
    x = [double]$s.Left; y = [double]$s.Top; w = [double]$s.Width; h = [double]$s.Height
    rotation = Safe { [double]$s.Rotation } 0
    z = Safe { [int]$s.ZOrderPosition } 0
    fill = $null; line = $null
    wrap = Safe { switch ([int]$s.TextWrap.Type) { 0 {'none'} 1 {'square'} 2 {'tight'} 3 {'through'} 4 {'topBottom'} default {$null} } }
  }
  if (Tri (Safe { $s.Fill.Visible })) { $o.fill = [ordered]@{ color = Hex (Safe { $s.Fill.ForeColor.RGB }); transparency = Safe { [double]$s.Fill.Transparency } 0 } }
  if (Tri (Safe { $s.Line.Visible })) { $o.line = [ordered]@{ color = Hex (Safe { $s.Line.ForeColor.RGB }); weight = Safe { [double]$s.Line.Weight } 1 } }

  if ($type -eq 6) {
    $o.kind = 'group'; $o.children = @()
    foreach ($c in $s.GroupItems) { $o.children += (Shape-Of $c $prefix) }
    return $o
  }

  $hasText = (Tri (Safe { $s.HasTextFrame })) -and (Safe { $s.TextFrame.HasText } $false)
  if ($hasText -and $type -ne 12 -and $type -ne 15) {
    $tf = $s.TextFrame
    $o.kind = 'text'
    $o.margins = @((Safe { $tf.MarginTop } 0), (Safe { $tf.MarginRight } 0), (Safe { $tf.MarginBottom } 0), (Safe { $tf.MarginLeft } 0))
    $o.vAlign = switch (Safe { $tf.VerticalTextAlignment }) { 2 {'middle'} 3 {'bottom'} default {'top'} }
    $o.columns = Safe { [int]$tf.Columns } 1
    $o.autoFit = Safe { [int]$tf.AutoFitText } 0
    if (Tri (Safe { $tf.HasNextLink })) { $o.nextFrame = Safe { $tf.NextLinkedTextFrame.Parent.Name } }
    if (Tri (Safe { $tf.HasPreviousLink })) { $o.prevFrame = Safe { $tf.PreviousLinkedTextFrame.Parent.Name } }
    $o.paragraphs = Paragraphs-Of $tf.TextRange
    return $o
  }
  if ($type -eq 9) { $o.kind = 'line'; return $o }
  if ($type -eq 1 -and -not (Tri (Safe { $s.HasTextFrame -and $s.TextFrame.HasText }))) {
    $ast = Safe { [int]$s.AutoShapeType } 1
    if ($ast -eq 1 -or $ast -eq 9) { $o.kind = if ($ast -eq 9) { 'ellipse' } else { 'rect' }; return $o }
  }
  # Tout le reste (images, tableaux, WordArt, formes complexes) -> image
  $o.kind = 'image'
  $o.src = Save-Picture $s $prefix
  return $o
}

$logFile = Join-Path $dir 'export.log'
function Log($m) { $l = "$(Get-Date -Format HH:mm:ss) $m"; Write-Host $l; Add-Content -Path $logFile -Value $l -Encoding UTF8 }
Log "Debut : $Pub"
# Se connecter de preference a un Publisher deja ouvert (visible) : ses eventuelles
# boites de dialogue restent visibles au lieu de bloquer une instance cachee.
$ownInstance = $false
try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject('Publisher.Application'); Log 'Connecte au Publisher ouvert' }
catch {
  Log 'Aucun Publisher ouvert : lancement'
  $app = New-Object -ComObject Publisher.Application
  $ownInstance = $true
}
Safe { $app.ActiveWindow.Visible = $true } | Out-Null
Safe { $app.DisplayAlerts = 0 } | Out-Null
$doc = $null
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
  Log 'Ouverture du fichier...'
  $doc = $app.Open($Pub, $true, $false)
  Log 'Fichier ouvert'
  $ps = $doc.PageSetup
  $result = [ordered]@{
    format = 'open-pub/1'; source = [IO.Path]::GetFileName($Pub); unit = 'pt'
    pageWidth = [double]$ps.PageWidth; pageHeight = [double]$ps.PageHeight
    masters = @(); pages = @()
  }
  $mi = 0
  foreach ($m in $doc.MasterPages) {
    $mi++; $shapes = @()
    foreach ($s in $m.Shapes) { $shapes += (Shape-Of $s "m$mi") }
    $result.masters += [ordered]@{ id = "m$mi"; shapes = $shapes }
  }
  $pi = 0
  foreach ($p in $doc.Pages) {
    $pi++
    Log "Page $pi..."
    $shapes = @()
    foreach ($s in $p.Shapes) { $shapes += (Shape-Of $s "p$pi") }
    $preview = $null
    try { $pp = Join-Path $assets "page-$pi-preview.png"; $p.SaveAsPicture($pp); $preview = "assets/page-$pi-preview.png" } catch {}
    $master = Safe { $p.Master.Name }
    $result.pages += [ordered]@{ index = $pi; master = $master; preview = $preview; shapes = $shapes }
  }
  $json = $result | ConvertTo-Json -Depth 40
  [IO.File]::WriteAllText((Join-Path $dir 'document.json'), $json, (New-Object Text.UTF8Encoding($false)))
  Log "JSON ecrit ($pi pages, $script:imgCount images, $script:comCalls appels texte, $([int]$sw.Elapsed.TotalSeconds) s)"
  if ($Pdf) { try { $doc.ExportAsFixedFormat(2, (Join-Path $dir "$name.pdf")) ; Write-Host 'PDF ecrit' } catch { Write-Host "PDF : $_" } }
  Write-Host "OK -> $dir"
} finally {
  # Le document lu est marque "modifie" : sans ceci, Close() ouvre une boite "Enregistrer ?" invisible et bloque
  if ($doc) { Safe { $doc.Saved = $true } | Out-Null; try { $doc.Close() } catch {} }
  if ($ownInstance) { try { $app.Quit() } catch {} }
  [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app)
}
