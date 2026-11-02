#!/usr/bin/env bash
# Publie une nouvelle version d'OpenPub sur le site de mises a jour (Azure Static Web Apps, offre gratuite).
#
#   tools/publish-update.sh 1.2.0 "Nouveautes de la version"            -> construit, signe, prepare work/site
#   tools/publish-update.sh 1.2.0 "Nouveautes de la version" --deploy   -> ... puis envoie sur Azure
#   tools/publish-update.sh 1.2.0 "" --deploy-only                      -> envoie le site deja prepare, sans reconstruire
#
# Prerequis : SDK .NET (%LOCALAPPDATA%\Microsoft\dotnet), Azure CLI Windows connecte (az login),
# Node.js (npx) pour l'outil officiel de deploiement @azure/static-web-apps-cli,
# cle privee de signature dans %APPDATA%\OpenPub\update-signing-key.pem (ne jamais la partager).
set -euo pipefail
VERSION="${1:?usage: publish-update.sh VERSION \"NOTES\" [--deploy]}"
NOTES="${2:-}"
DEPLOY="${3:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
SWA_NAME=openpub-updates
SWA_RG=rg-openpub-updates
KEY='C:\Users\prolice\AppData\Roaming\OpenPub\update-signing-key.pem'
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version attendue : X.Y.Z"; exit 1; }

if [ "$DEPLOY" = "--deploy-only" ]; then
  # envoie le site deja prepare (work/site) sans reconstruire, apres controle de sa version
  grep -q "\"version\": \"$VERSION\"" work/site/latest.json || { echo "work/site ne contient pas la version $VERSION"; exit 1; }
  [ -f "work/site/OpenPub-$VERSION.msi" ] || { echo "Installeur absent de work/site"; exit 1; }
  DEPLOY=--deploy
else
echo "== 1. Numero de version $VERSION"
sed -i -E "s#<Version>[0-9.]+</Version>#<Version>$VERSION</Version>#" src/OpenPub.App/OpenPub.App.csproj
sed -i -E "s/Version=\"[0-9]+\.[0-9]+\.[0-9]+\.0\"/Version=\"$VERSION.0\"/" installer/Package.wxs
sed -i -E "s#<OutputName>OpenPub-[0-9.]+</OutputName>#<OutputName>OpenPub-$VERSION</OutputName>#" installer/OpenPub.Installer.wixproj

echo "== 2. Tests, executable autonome et installeur"
rm -f dist/OpenPub-*.msi
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w tools/build-installer.ps1)" | tr -d '\r' | grep -E "Installeur|chec|rreur" || true
MSI="dist/OpenPub-$VERSION.msi"
[ -f "$MSI" ] || { echo "Installeur introuvable : $MSI"; exit 1; }

echo "== 3. Site : page de telechargement (charte OpenPub), installeur, latest.json signe"
rm -rf work/site && mkdir -p work/site
# modele de la page (tools/site-template), sans le document de demonstration
cp -r tools/site-template/assets tools/site-template/fonts tools/site-template/styles.css work/site/
cp "$MSI" work/site/
dist/OpenPub/OpenPub.exe --sign-update "$(wslpath -w work/site/OpenPub-$VERSION.msi)" "$VERSION" "$NOTES" "$KEY" "$(wslpath -w work/site/latest.json)" | tr -d '\r'
# vraie capture de l'application (document de demonstration neutre, aucune donnee personnelle)
dist/OpenPub/OpenPub.exe --screenshot "$(wslpath -w tools/site-template/demo/document.json)" "$(wslpath -w work/site/assets/apercu-openpub.png)" 1402 948 TabDraw | tr -d '\r'
[ -f work/site/assets/apercu-openpub.png ] || { echo "Capture d'ecran manquante"; exit 1; }
python3 - "$VERSION" "$NOTES" "OpenPub-$VERSION.msi" "$MSI" <<'PY'
import html, os, sys
version, notes, msi_name, msi_path = sys.argv[1:5]
size = f"{os.path.getsize(msi_path) / 1048576:.1f}".replace(".", ",") + " Mo"
notes_block = f'<p class="notes"><strong>Nouveautés :</strong> {html.escape(notes)}</p>' if notes.strip() else ""
page = open("tools/site-template/index.html", encoding="utf-8").read()
for k, v in {"{{VERSION}}": html.escape(version), "{{MSI}}": html.escape(msi_name), "{{SIZE}}": size, "{{NOTES_BLOCK}}": notes_block}.items():
    page = page.replace(k, v)
assert "{{" not in page, "champ non rempli"
open("work/site/index.html", "w", encoding="utf-8").write(page)
print(f"page : version {version}, {size}")
PY
# documentation en ligne (aide/) : pages, captures de chaque situation, verification des liens de l'application
python3 -I tools/build-docs.py work/site "$VERSION"
DEMO="$(wslpath -w tools/site-template/demo/document.json)"
for spec in accueil:TabHome onglet-accueil:TabHome onglet-insertion:TabInsert onglet-dessin:TabDraw dessin-crayon:draw-tool \
            onglet-forme:shape onglet-image:picture texte-edition:text-edit editeur-image:image-editor; do
  dist/OpenPub/OpenPub.exe --screenshot "$DEMO" "$(wslpath -w work/site/aide/img/${spec%%:*}.png)" 1402 900 "${spec#*:}" > /dev/null
  [ -f "work/site/aide/img/${spec%%:*}.png" ] || { echo "Capture manquante : ${spec%%:*}"; exit 1; }
done
MISSING=$(comm -23 <(dist/OpenPub/OpenPub.exe --help-topics | tr -d '\r' | sort) <(sort work/site/aide/ancres.txt))
[ -z "$MISSING" ] || { echo "Liens d'aide sans section correspondante :"; echo "$MISSING"; exit 1; }
rm -f work/site/aide/ancres.txt
echo "aide : $(ls work/site/aide/*.html | wc -l) pages, captures et liens contextuels verifies"
cat > work/site/staticwebapp.config.json <<'JSON'
{
  "mimeTypes": { ".msi": "application/x-msi", ".json": "application/json", ".woff2": "font/woff2" },
  "routes": [
    { "route": "/latest.json", "headers": { "Cache-Control": "no-cache, no-store, must-revalidate" } },
    { "route": "/*.msi", "headers": { "Cache-Control": "public, max-age=31536000, immutable" } },
    { "route": "/fonts/*", "headers": { "Cache-Control": "public, max-age=31536000, immutable" } },
    { "route": "/index.html", "headers": { "Cache-Control": "no-cache" } }
  ]
}
JSON
ls -la work/site
fi

if [ "$DEPLOY" = "--deploy" ]; then
  echo "== 4. Envoi sur Azure Static Web Apps ($SWA_NAME)"
  TOKEN=$(tools/az.sh staticwebapp secrets list -n "$SWA_NAME" -g "$SWA_RG" --query properties.apiKey -o tsv)
  npx -y @azure/static-web-apps-cli@2.0.2 deploy work/site --env production --deployment-token "$TOKEN" --no-use-keychain
  HOST=$(tools/az.sh staticwebapp show -n "$SWA_NAME" -g "$SWA_RG" --query defaultHostname -o tsv)
  echo "Publie : https://$HOST/latest.json"
else
  echo "== Site pret dans work/site (relancer avec --deploy pour l'envoyer sur Azure)"
fi
