# OpenPub — notes de reprise

*État au 2 novembre 2026. Détails techniques : `CLAUDE.md` ; historique des décisions : `PLAN.md`.*

## Où en est-on

| Élément | État |
|---|---|
| Application | Version **1.5.1** en ligne (ruban, édition sur la page, dessin, retouche d'images, aide contextuelle F1, mises à jour automatiques signées) |
| Portail | https://calm-pebble-0080d6003.5.azurestaticapps.net : page de téléchargement + aide en ligne (`/aide/`), Azure Static Web Apps, offre gratuite |
| Dépôt | https://github.com/prolice/open-pub (public, branche `main`) |
| Pipeline « Publier une version de l'app » | En place, **validée en mode essai** sur GitHub |
| Pipeline « Publier le portail » | En place, **pas encore testée** : attend une première release GitHub |
| Secrets GitHub | `UPDATE_SIGNING_KEY` et `AZURE_STATIC_WEB_APPS_API_TOKEN` enregistrés |

## À faire ensuite

1. **Créer la release `v1.5.1`** (point de départ de la pipeline du portail). Ses fichiers sont ceux produits et signés par l'essai de la pipeline, dans `work/ci-art/openpub-1.5.1` :

   ```bash
   A=work/ci-art/openpub-1.5.1
   /mnt/c/Users/prolice/AppData/Local/Programs/gh/bin/gh.exe release create v1.5.1 --repo prolice/open-pub --target main \
     --title "OpenPub 1.5.1" --notes "Menus déroulants redessinés, infobulles modernisées, boutons Fichier et onglets en rectangle arrondi." \
     "$(wslpath -w $A/work/site/OpenPub-1.5.1.msi)" "$(wslpath -w $A/dist/OpenPub/OpenPub.exe)" "$(wslpath -w $A/work/site/latest.json)"
   ```

   Si `work/ci-art` a disparu, retélécharger l'artefact de l'essai : `gh run download 41366937980 --repo prolice/open-pub --dir work/ci-art` (artefacts conservés 14 jours), ou relancer un essai.

2. **Tester la pipeline du portail en mode essai** : *Actions ▸ Publier le portail ▸ Run workflow* (« Essai » coché), ou
   `gh workflow run publish-portal.yml --repo prolice/open-pub -f dry_run=true`.

3. Idées en attente :
   - restyler le cadre des listes déroulantes (police, taille…), qui garde l'apparence Windows ;
   - choisir une **licence** pour le dépôt public (aucune pour l'instant, donc « tous droits réservés ») ;
   - vérifier en réel le circuit de mise à jour automatique (bandeau → installation → relance) sur un PC équipé d'une version antérieure.

## Publier une nouvelle version

**Avec la pipeline (recommandé)** — le texte de l'étiquette devient les nouveautés affichées dans l'application et sur le portail :

```bash
git tag -a v1.6.0 -m "Description des nouveautés"
git push origin v1.6.0
```

Ou sur GitHub : *Actions ▸ Publier une version de l'app ▸ Run workflow* (décocher « Essai »).

**Le portail seul** (page, aide, charte) : il se republie automatiquement quand on pousse sur `main` une modification de `tools/site-template/**`, `tools/build-docs.py`, `tools/build-site.py` ou `assets/openpub-design/**`.

**En local (secours)** : `tools/publish-update.sh X.Y.Z "Nouveautés"` prépare tout dans `work/site`, puis `tools/publish-update.sh X.Y.Z "" --deploy-only` envoie sur Azure.

## Infos utiles

- **Commits** : git de Windows (`work/g.ps1`, lancé avec `powershell.exe -File`) ; identité *Christophe Van Beneden <christophe@van-beneden.com>* ; GitHub CLI dans `%LOCALAPPDATA%\Programs\gh`, connecté au compte `prolice`.
- **SDK .NET** : `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (pas dans le PATH).
- **Azure** : Static Web App `openpub-updates`, groupe `rg-openpub-updates` (Europe de l'Ouest), offre Free ; Azure CLI de Windows via `tools/az.sh`.
- **Clé de signature des mises à jour** : `%APPDATA%\OpenPub\update-signing-key.pem` — **à sauvegarder hors du PC**, jamais dans le dépôt (aussi enregistrée comme secret GitHub).
- **Fichier de travail de la newsletter** : `newsletter.opub` (pages 2 à 4 supprimées volontairement). Ne jamais le versionner ni l'écraser ; la conversion d'origine est dans `converted/newsletter-natif` (hors dépôt).
- **Données personnelles** : la newsletter contient des noms et photos d'enfants — rien de tout cela sur GitHub ni sur le portail public (captures faites sur `tools/site-template/demo`).
- **Actions refusées en mode automatique** : création de release publique et envoi en production — à lancer soi-même (préfixe `!` dans Claude Code) ou en mode manuel.

## Vérifications rapides

```bash
D=/mnt/c/Users/prolice/AppData/Local/Microsoft/dotnet/dotnet.exe
$D test tests/OpenPub.Tests -nologo -v q                     # 37 tests unitaires
$D 'C:\dev\open-pub\src\OpenPub.App\bin\Debug\net10.0-windows\OpenPub.dll' --selftest \
   'C:\dev\open-pub\converted\newsletter-natif\document.json' 'C:\dev\open-pub\work\selftest'   # 49 vérifications (conversion locale requise)
curl -s https://calm-pebble-0080d6003.5.azurestaticapps.net/latest.json | grep version   # version en ligne
```
