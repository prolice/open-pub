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
#
# Le site ne contient que l'installeur courant (quota Azure Free : 250 Mo, controle par build-site.py, limite 230 Mo).
# La version precedente est un lien vers l'installeur de sa release GitHub, trouvee avec le gh Windows ou l'API
# publique de GitHub (sans reseau : page produite sans ce lien). Ce script ne cree pas de release GitHub :
# preferer la pipeline « Publier une version de l'app », sinon la version suivante ne verra pas celle-ci.
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
python3 -I -c "import re,sys;p='installer/OpenPub.Installer.wixproj';s=open(p).read();s,n=re.subn(r'(<ProductVersion Condition=[^>]*>)[0-9.]+(</ProductVersion>)',r'\g<1>'+sys.argv[1]+r'\g<2>',s);open(p,'w').write(s);sys.exit(n!=1)" "$VERSION"
grep -q "\">$VERSION</ProductVersion>" installer/OpenPub.Installer.wixproj || { echo "version de l'installeur non mise a jour"; exit 1; }

echo "== 2. Tests, executable autonome et installeur"
rm -f dist/OpenPub-*.msi
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(wslpath -w tools/build-installer.ps1)" | tr -d '\r' | grep -E "Installeur|chec|rreur" || true
MSI="dist/OpenPub-$VERSION.msi"
[ -f "$MSI" ] || { echo "Installeur introuvable : $MSI"; exit 1; }

echo "== 3. Portail : accueil, fonctionnalites, aide en ligne, latest.json signe (tools/build-site.py)"
python3 -I tools/build-site.py --out work/site --exe dist/OpenPub/OpenPub.exe --msi "$MSI" \
  --sign-key "$(wslpath -u "$KEY")" --version "$VERSION" --notes "$NOTES"
ls -la work/site
fi

if [ "$DEPLOY" = "--deploy" ]; then
  echo "== 4. Envoi sur Azure Static Web Apps ($SWA_NAME)"
  TOKEN=$(tools/az.sh staticwebapp secrets list -n "$SWA_NAME" -g "$SWA_RG" --query properties.apiKey -o tsv)
  npx -y @azure/static-web-apps-cli@2.0.2 deploy work/site --api-location tools/site-api --api-language node --api-version 22 --env production --deployment-token "$TOKEN" --no-use-keychain
  # domaine personnalise (l'adresse Azure *.azurestaticapps.net reste servie pour les versions <= 2.1.3)
  echo "Publie : https://open-pub.prolice.net/latest.json"
else
  echo "== Site pret dans work/site (relancer avec --deploy pour l'envoyer sur Azure)"
fi
