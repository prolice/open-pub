<p align="center"><img src="assets/openpub-design/assets/openpub-256.png" width="128" alt="Logo OpenPub"></p>

# OpenPub

Éditeur de mise en page pour Windows (C# / .NET 10 / WPF), conçu pour remplacer Microsoft Publisher après son retrait : bulletins, affiches, cartes, invitations.

- **Téléchargement et aide en ligne** : https://open-pub.prolice.net (installeur MSI par utilisateur, sans droits administrateur ; mises à jour automatiques signées).
- **Fonctions** : écriture directement sur la page, zones de texte liées en colonnes, habillage des images, césure française, réduction automatique du texte ; formes, crayon à main levée, forme libre, flèches ; rotation ; magnétisme et repères ; retouche d'images (recadrage, gris, sépia, filigrane, luminosité, contraste, miroir, ombre, reflet) ; impression et PDF ; aide contextuelle (F1).

## Structure

| Dossier | Contenu |
|---|---|
| `src/OpenPub.Core` | Modèle de document (unités en points), format `.opub` (ZIP : `document.json` + images + polices), annuler/rétablir, édition de texte, manifeste de mise à jour signé |
| `src/OpenPub.Layout` | Moteur de mise en page du texte : chaînes de zones, habillage, césure, justification, réduction en cas de débordement |
| `src/OpenPub.App` | Application WPF : ruban, édition sur la page, dessin, éditeur d'images, aide contextuelle, mises à jour |
| `tests/OpenPub.Tests` | Tests xUnit (sur le document de démonstration `tools/site-template/demo`) |
| `tools/` | Conversion des fichiers `.pub` (via libmspub), publication (installeur, site, aide en ligne), génération de la documentation |
| `installer/` | Installeur MSI (WiX Toolset 5) |
| `assets/openpub-design` | Charte graphique (logos, maquette de la page de téléchargement) |

## Développement

```powershell
dotnet build OpenPub.sln
dotnet test tests/OpenPub.Tests
dotnet run --project src/OpenPub.App -- chemin\vers\document.opub
```

Publication d'une version (tests, exécutable autonome, MSI, page de téléchargement, aide en ligne) : `tools/publish-update.sh X.Y.Z "Nouveautés"` (voir `CLAUDE.md` et `PLAN.md`).

L'historique des décisions de conception, les calibrations de mise en page et le journal des versions sont dans [`PLAN.md`](PLAN.md).

## Composants tiers

- Polices **DM Sans** et **Plus Jakarta Sans** : SIL Open Font License 1.1 (`src/OpenPub.App/Fonts/OFL-*.txt`).
- Export PDF : [PDFsharp](https://github.com/empira/PDFsharp) 6 (licence MIT), paquet NuGet `PDFsharp`.
- Motifs de césure français (`src/OpenPub.Layout/Resources/hyph_fr.dic`) : dictionnaire de césure de LibreOffice (LGPL / MPL), distribué via pyphen.
- Conversion des fichiers Publisher : [libmspub](https://git.libreoffice.org/libmspub) (MPL 2.0), utilisé comme outil externe (`pub2raw`), non inclus dans ce dépôt.
