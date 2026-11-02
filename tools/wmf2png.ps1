# Convertit les images WMF/EMF d'un dossier en PNG (rendu GDI+ de Windows)
param([Parameter(Mandatory=$true)][string]$Dir, [int]$Dpi = 300)
Add-Type -AssemblyName System.Drawing
Get-ChildItem $Dir -Include *.wmf,*.emf -Recurse | ForEach-Object {
  $mf = New-Object System.Drawing.Imaging.Metafile $_.FullName
  $h = $mf.GetMetafileHeader()
  $w = [int]($mf.Width / $h.DpiX * $Dpi); $ht = [int]($mf.Height / $h.DpiY * $Dpi)
  $k = [Math]::Min(1.0, 2000.0 / [Math]::Max($w, $ht)); $w = [int]($w * $k); $ht = [int]($ht * $k)
  $bmp = New-Object System.Drawing.Bitmap $w, $ht
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
  $g.DrawImage($mf, 0, 0, $w, $ht)
  $out = [IO.Path]::ChangeExtension($_.FullName, '.png')
  $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose(); $mf.Dispose()
  "$($_.Name) -> $([IO.Path]::GetFileName($out)) ($w x $ht)"
}
