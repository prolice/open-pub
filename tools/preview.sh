#!/usr/bin/env bash
# Convertit un .pub et produit un apercu PNG par page (Edge headless). Usage : tools/preview.sh fichier.pub dossier_sortie
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
PUB="$1"; OUT="$2"
F=/mnt/c/Users/prolice/AppData/Local/Microsoft
python3 -I "$HERE/pub2json.py" "$PUB" "$OUT"
WIN_ASSETS=$(wslpath -w "$OUT/assets")
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w "$HERE/wmf2png.ps1")" -Dir "$WIN_ASSETS" >/dev/null
PY="$HERE/../work/venv/bin/python"; [ -x "$PY" ] || PY=python3
"$PY" -I "$HERE/render_html.py" "$OUT" --fonts "$F/FontCache/4/CloudFonts:$F/Windows/Fonts:/mnt/c/Windows/Fonts"
N=$(python3 -I -c "import json,sys;print(len(json.load(open(sys.argv[1],encoding='utf-8'))['pages']))" "$OUT/document.json")
SHOT=$(wslpath -w "$OUT")\\preview-all.png
"/mnt/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" --headless=new --disable-gpu --hide-scrollbars \
  --allow-file-access-from-files --window-size=860,$((N*1163+70)) --virtual-time-budget=8000 \
  --screenshot="$SHOT" "file:///$(wslpath -m "$OUT/preview.html")" >/dev/null 2>&1
powershell.exe -NoProfile -Command "Add-Type -AssemblyName System.Drawing; \$s=[Drawing.Image]::FromFile('$SHOT'); for(\$i=0;\$i -lt $N;\$i++){ \$r=New-Object Drawing.Rectangle 0,(\$i*1163),860,1175; \$b=New-Object Drawing.Bitmap 860,1175; \$g=[Drawing.Graphics]::FromImage(\$b); \$g.DrawImage(\$s,(New-Object Drawing.Rectangle 0,0,860,1175),\$r,[Drawing.GraphicsUnit]::Pixel); \$b.Save('$(wslpath -w "$OUT")\\preview-p'+(\$i+1)+'.png'); \$g.Dispose(); \$b.Dispose() }; \$s.Dispose()"
echo "apercus : $OUT/preview-p*.png"
