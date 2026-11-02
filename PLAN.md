# OpenPub — plan d'implémentation

Éditeur de mise en page de bureau (Windows), écrit en C# / .NET, qui remplace Microsoft Publisher et ouvre les fichiers `.pub` existants.

*Rédigé le 2026-11-01, à partir du prototype `mini-publisher.html` et d'un essai d'extraction sur `newsletter  septembre 26.pub`. Mis à jour le même soir : Publisher n'est plus utilisable, le lecteur natif devient la voie principale.*

---

## 1. Point de départ

### Le prototype existant (`mini-publisher.html`)
Il sert de **spécification fonctionnelle** (le code JavaScript n'est pas repris tel quel) :

| Déjà présent dans le prototype | Repris dans OpenPub |
|---|---|
| Blocs texte / image / forme, déplacement et redimensionnement | ✔ (cœur de l'éditeur) |
| Panneau de propriétés (police, taille, couleur, fond, bordure, alignement, opacité, ajustement de l'image) | ✔ |
| Annuler / Rétablir, dupliquer, supprimer, ordre d'empilement | ✔ |
| Formats de page + orientation, modèles | ✔ (étendu) |
| Enregistrer / ouvrir un projet `.json`, export HTML, impression | ✔ → format `.opub`, export PDF/PNG, impression native |

Ce qui **manque** pour remplacer Publisher : plusieurs pages, pages maîtres, zones de texte chaînées sur plusieurs colonnes, habillage du texte autour des images, césure, repères et magnétisme, tableaux, export PDF fidèle et **import `.pub`**.

### Essai d'extraction du `.pub` (via Publisher, script `tools/export-pub.ps1`)
Testé sur la newsletter (A4, 4 pages, 1 page maître) :

- ✅ **Géométrie exacte** : position, taille, rotation, ordre d'empilement, remplissage et contour de chaque bloc (95 objets, groupes compris).
- ✅ **Images** : les 17 images et éléments complexes ont été exportés en PNG, plus un aperçu de référence pour chaque page.
- ✅ **Structure du texte** : marges, alignement vertical, colonnes, paragraphes (alignement, espacements, retraits).
- ❌ **Texte lui-même vide** : la boucle mot par mot (`Words()`) échoue en COM. Il faut lire `TextRange.Text`, puis découper les mises en forme avec `Characters(start, len)`.
- ❌ **Blocage à la fin** : l'export PDF (`ExportAsFixedFormat`) ou la fermeture se bloque, probablement sur une boîte de dialogue cachée. Il faut un délai maximal et `DisplayAlerts = false`.
- ⚠️ **Mise en page à reproduire** : articles qui s'écoulent sur 3 zones chaînées, texte qui habille les images, césure française, citations en encadré, bandeau d'en-tête sur la page maître.

### Mise à jour du 2026-11-01 au soir : Publisher n'est plus utilisable
Peu après ce premier essai, Publisher est passé en **mode fonctionnalités réduites** (« Produit sans licence »). Il ouvre encore les fichiers en lecture seule, mais n'expose plus aucune page, ni à l'écran ni par COM (`Pages.Count = 0`). **Le pont Publisher (niveau A) ne fonctionne donc plus.**

Ce qui reste acquis de l'essai de 18:57 sert désormais de **vérité terrain** pour la newsletter : géométrie, images, aperçus des pages et nombre de paragraphes par zone.

Premier essai de lecture native (`tools/pub_streams.py`, sans Publisher) : le conteneur OLE se lit sans difficulté, et le **texte complet** (9 376 caractères, 121 paragraphes) sort du bloc `TEXT` du flux `Quill/QuillSub/CONTENTS`. L'index des blocs Quill (`TEXT`, `STSH`, `FDPC`, `FDPP`, `SYID`, `PLC `…) est lisible.

---

## 2. Choix techniques

| Sujet | Choix | Raison |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | .NET 8 n'est plus supporté après le 10/11/2026 |
| Interface | **WPF** + **MVVM** (CommunityToolkit.Mvvm) | Application Windows uniquement, rendu vectoriel et typographique mature, aucune dépendance externe |
| Ruban / docking | **Fluent.Ribbon** (MIT) + AvalonDock (Dirkster, MS-PL) | Interface proche de Publisher, donc moins de réapprentissage pour les utilisateurs |
| Moteur de texte | **Moteur maison** basé sur `GlyphRun` / `FormattedText` (WPF) | Ni WPF ni aucune bibliothèque ne gère les chaînes de zones de texte et l'habillage autour de formes : c'est le cœur à écrire |
| Césure | **NHyphenator** avec les motifs TeX `hyph-fr` / `hyph-en` | Reproduire la césure du `.pub` |
| Lecture OLE (`.pub`) | **OpenMcdf** (MPL 2.0) | Lecture du conteneur Compound File Binary |
| Pont Publisher | COM en liaison tardive (`dynamic`), aucune PIA à installer | Fonctionne avec n'importe quelle version de Publisher installée |
| Export PDF | **PDFsharp 6** (MIT) | Dessin à positions absolues, polices incorporées |
| Images | **SkiaSharp** (redimensionnement, recadrage, formats WMF/EMF via conversion) | |
| Installeur | **WiX Toolset v5 → MSI** (+ association des extensions `.opub` / `.pub`) | Déployable par GPO/Intune en entreprise |
| Mises à jour | Velopack (optionnel, phase 6) | |
| Tests | xUnit + **tests de rendu visuel** (comparaison avec les aperçus PNG produits par Publisher) | Mesurer objectivement la fidélité |

---

## 3. Architecture

```
OpenPub.sln
├─ src/
│  ├─ OpenPub.Core          Modèle de document, commandes, annuler/rétablir, sérialisation .opub
│  ├─ OpenPub.Layout        Moteur de texte : chaînage, colonnes, habillage, césure, débordement
│  ├─ OpenPub.Rendering     Rendu abstrait (IRenderTarget) → WPF (écran), PDFsharp (PDF), Skia (PNG)
│  ├─ OpenPub.Import.Pub    Lecteur .pub natif (OpenMcdf + parseur des flux Contents/Quill)
│  ├─ OpenPub.Import.Publisher  Pont COM (secours, uniquement avec un Publisher sous licence)
│  ├─ OpenPub.Export        PDF, PNG/JPG par page, HTML
│  ├─ OpenPub.App           WPF : ruban, canevas, panneaux pages/calques/propriétés
│  └─ OpenPub.Cli           `openpub convert *.pub --out dossier` (conversion en masse)
├─ tests/
│  ├─ OpenPub.Core.Tests
│  ├─ OpenPub.Layout.Tests
│  └─ OpenPub.Fidelity.Tests   corpus .pub + PNG de référence → score de différence par page
└─ installer/OpenPub.Installer (WiX)
```

### Modèle de document (`OpenPub.Core`)
Unités internes en **points** (comme Publisher, ce qui évite toute conversion à l'import).

```
Document { PageSize, Pages[], MasterPages[], Stories[], Styles[], ColorScheme, Fonts[], Assets[] }
Page     { MasterId, Shapes[] }
Shape    { Id, Bounds, Rotation, Z, Fill, Line, Wrap(None|Square|Tight|TopBottom), Locked }
  ├─ TextFrame { StoryId, Margins, Columns, Gutter, VAlign, NextFrameId, AutoFit }
  ├─ Picture   { AssetId, Crop, Fit }
  ├─ AutoShape { Geometry (rect, ellipse, path…) }
  ├─ Line, Table { Rows, Cols, Cells → Story }
  └─ Group     { Children[] }
Story    { Paragraphs[] { Style, Align, Spacing, Indents, Runs[] { Text, CharStyle } } }
```

Point clé : le **texte appartient à une Story, pas à une zone**. Les zones chaînées (NextFrameId) ne sont que des fenêtres dans lesquelles la Story s'écoule. C'est le modèle de Publisher, et il est indispensable pour les newsletters.

### Format natif `.opub`
Archive ZIP : `document.json` (versionné, `"format": "openpub/1"`) + `assets/` (images originales) + `fonts/` (optionnel) + `preview.png`. Le format est lisible et versionnable, et l'export produit par le script PowerShell actuel en est très proche.

---

## 4. Prise en charge des `.pub`

**Voie principale : le lecteur natif** (ex-niveau B), puisque Publisher n'est plus utilisable.

| | **Lecteur natif (principal)** | **Pont Publisher (secours)** | **LibreOffice (référence)** |
|---|---|---|---|
| Prérequis | Aucun | Un Publisher **encore sous licence** | LibreOffice installé |
| Rôle | Import dans OpenPub | Uniquement si un poste licencié est retrouvé | Conversion de secours en PDF/ODG + seconde référence de comparaison |
| État | Conteneur OLE et texte lus | Script corrigé, inutilisable ici | À installer |

**Écriture `.pub` : non prévue.** Le format est fermé et non documenté. OpenPub enregistre en `.opub` et exporte en PDF.

### Documentation disponible
Il n'existe aucune spécification officielle du `.pub`. On s'appuie sur :
1. **libmspub** (Document Liberation Project, utilisé par LibreOffice et Scribus, MPL 2.0) : la référence, écrite par rétro-ingénierie. Elle couvre Publisher 2000 et plus récents : structure `Contents`, chaînes de texte, polices, styles, couleurs. Le code source tient lieu de documentation.
2. **[MS-ODRAW]** (spécification officielle Microsoft, révision 12.3, mai 2025) : format OfficeArt (« Escher ») des formes et images. Il concerne les flux `Escher/EscherStm` (formes) et `Escher/EscherDelayStm` (images d'origine en pleine qualité, 5,6 Mo dans la newsletter).
3. **Apache POI HPBF** (Java) : lecteur partiel du flux Quill, utile pour recouper.
4. Notes de Steve Parker (2007) : intérêt historique.

### Lecteur natif
**Prototype opérationnel (2026-11-01)** : `tools/preview.sh fichier.pub dossier` enchaîne
1. `pub2raw` (outil de libmspub 0.1.4, paquets Ubuntu décompressés localement dans `work/ref/debs`) pour lire le `.pub` ;
2. `tools/pub2json.py` → `document.json` + `assets/` (images **d'origine** : JPEG, PNG, WMF) ;
3. `tools/wmf2png.ps1` (WMF → PNG via GDI+) ;
4. `tools/render_html.py` → aperçu HTML, puis capture PNG par page (Edge headless).

Résultat sur la newsletter : 4 pages, 55 zones de texte, 9 255 caractères, 17 images, 63 formes, 7 chaînes de zones, **mise en page très proche des aperçus Publisher**. Ce que le prototype a dû reconstituer, et que le moteur C# devra faire aussi :
- **Chaînes de zones** : libmspub recopie parfois toute l'histoire dans chaque zone d'une chaîne. On regroupe les zones de même contenu, même page, même hauteur et même taille.
- **Habillage rectangulaire** autour des images, des légendes groupées et des encadrés pleins ; pas de texte dans une bande trop étroite ; obstacle seulement s'il est posé à plus de 50 % dans la zone.
- **Interligne** = facteur du paragraphe × (winAscent + winDescent) / unitsPerEm de la police.
- **Réduction en cas de débordement** (réglage Publisher non exposé par libmspub) : réduction par dichotomie jusqu'à ce que le texte tienne (environ −13 % sur la page 1).
- **Césure** française (motifs LibreOffice via pyphen ; NHyphenator en C#).
- **Polices cloud d'Office** (ex. Cavolini) : à chercher dans `%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts`, sinon substitution signalée.

Limites connues : rotation du texte des zones inclinées et réglages d'ajustement automatique non fournis par libmspub ; image d'arrière-plan de page à valider.

**Choix pour le C#** : porter la logique de libmspub en C# (aucune dépendance native à installer), et garder `pub2raw` comme **oracle de test**. Le lecteur C# doit produire le même `document.json` que le prototype.

Détails techniques :
- Conteneur OLE via OpenMcdf (C#). Prototype Python : `tools/pub_streams.py`.
- Flux `Quill/QuillSub/CONTENTS` : bloc `TEXT` (UTF-16LE), découpage en histoires (`PLC `/`TCD`), mise en forme des caractères (`FDPC`) et des paragraphes (`FDPP`), feuilles de style (`STSH`).
- Flux `Contents` : pages, blocs, positions, liens entre zones de texte et histoires, polices, couleurs.
- Flux `Escher/*` selon [MS-ODRAW] : formes, contours, remplissages, images d'origine.
- Code C# adapté de libmspub, sous MPL 2.0 pour les fichiers concernés (licence copyleft limitée au fichier, compatible avec une application propriétaire).
- Validation : comparaison avec l'export Publisher de 18:57 et avec la conversion LibreOffice.

### Pont Publisher (secours uniquement)
`tools/export-pub.ps1`, à n'utiliser que sur un poste où Publisher est encore sous licence. Corrections déjà faites ou identifiées :
1. Texte : `TextRange.Text`, puis segments de mise en forme homogènes via `Characters()`, avec recherche dichotomique des changements de police pour rester rapide.
2. Chaînage : `TextFrame.HasNextLink` / `NextLinkedTextFrame` pour reconstituer les Stories.
3. Habillage : `Shape.TextWrap` (type, distances).
4. Styles et jeu de couleurs : `Document.TextStyles`, `ColorScheme`.
5. Tableaux : cellules → Stories, au lieu d'une image.
6. Images : récupérer l'**original** (`PictureFormat` / lien) plutôt qu'une capture d'écran quand c'est possible.
7. Robustesse : `DisplayAlerts = false`, délai maximal par fichier, processus Publisher dédié tué en cas de blocage, journal CSV des réussites et échecs.
8. Lancer Publisher **visiblement** et s'y connecter (`GetActiveObject`) : une instance cachée se fige sur des boîtes de dialogue invisibles et laisse des processus orphelins.

---

## 5. Phases

| # | Phase | Contenu | Livrable | Durée estimée* |
|---|---|---|---|---|
| **0** | **Sauvetage** (immédiat) | Lecteur natif prototype (Python) : texte → zones, polices et styles, images d'origine ; conversion de secours de l'archive via LibreOffice | Newsletter reconstituée en JSON sans Publisher ; archive lisible en PDF | 1 sem |
| **1** | Socle | Solution .NET, modèle Core, sérialisation `.opub`, commandes + annuler/rétablir, CI | Bibliothèque testée | 1–2 sem |
| **2** | Éditeur de base (parité avec le prototype) | Fenêtre WPF, ruban, canevas avec zoom, sélection, poignées, rotation, propriétés, calques, plusieurs pages, panneau de miniatures, formats de page | Application qui remplace `mini-publisher.html` | 3–4 sem |
| **3** | Moteur de texte | Story/zones chaînées, colonnes, habillage autour des formes, césure FR, débordement (indicateur), styles de paragraphe et de caractère, édition au clavier dans le canevas | Mise en page d'une newsletter éditable | 5–7 sem (**phase critique**) |
| **4** | Lecteur `.pub` natif en C# (1re version) | Port en C# du prototype : OLE (OpenMcdf), Quill (texte, styles), `Contents` (pages, zones, liens), Escher (formes, images) ; menu *Fichier ▸ Ouvrir* `.pub` ; CLI de conversion en masse | Ouverture directe des `.pub` | 4–6 sem (**phase critique**) |
| **5** | Fonctions Publisher | Pages maîtres, repères et magnétisme, tableaux, formes automatiques, jeu de couleurs, modèles (newsletter, flyer, carte…), recadrage d'image, publipostage simple (CSV) | | 4–6 sem |
| **6** | Sorties + installation | Export PDF (polices incorporées, fond perdu, traits de coupe), PNG, impression native avec imposition livret, installeur MSI, associations de fichiers, mises à jour | v1.0 installable | 2–3 sem |
| **7** | Lecteur `.pub` natif : fidélité | WordArt, tableaux, effets, anciennes versions (Publisher 98–2003), tests de fidélité sur tout le corpus | Couverture de toute l'archive | 4–6 sem (en parallèle des phases 5–6) |

\* Pour un développeur à temps plein. Les phases 3 et 7 concentrent le risque.

### Critères de réussite
- La newsletter de test ouverte dans OpenPub diffère de **moins de 2 %** (en pixels) des aperçus Publisher de l'essai de 18:57, sur chaque page.
- Le texte qui s'écoule entre zones chaînées reste correct après une modification.
- Le PDF exporté est accepté tel quel par un imprimeur (PDF/X-1a ou X-4).

---

## 6. Risques

| Risque | Impact | Parade |
|---|---|---|
| ~~Publisher cesse de démarrer~~ **survenu le 2026-11-01** | Plus d'import par Publisher | Lecteur natif comme voie principale ; LibreOffice comme filet de sécurité |
| Polices absentes sur le poste (ex. Comic Sans, polices Publisher) | Texte qui ne passe plus aux mêmes endroits | Inventaire des polices à l'import + substitution signalée à l'utilisateur |
| Moteur de texte (césure, habillage) plus long que prévu | Retard | Livrer d'abord colonnes et chaînage, ajouter l'habillage ensuite ; tests visuels systématiques |
| Images de l'essai Publisher capturées à résolution d'écran | Impression floue | Extraire les originaux du flux `Escher/EscherDelayStm` ([MS-ODRAW]) |
| Couverture de libmspub incomplète pour certaines versions de `.pub` | Fichiers mal lus en natif | Conversion LibreOffice comme secours ; corpus de test couvrant toutes les versions présentes dans l'archive |

---

## 6 bis. État au 2026-11-01 (soir) : un seul fichier à maintenir

**Décision : seul `newsletter  septembre 26.pub` doit être maintenu.** La conversion est faite une fois pour toutes (`newsletter.opub`). Le lecteur `.pub` en C# (phases 4 et 7) **n'est plus nécessaire** ; l'application ouvre et enregistre son propre format.

**Fait (phases 1 et 2, début de la 3) :**
- Solution `OpenPub.sln` (.NET 10, SDK installé dans `%LOCALAPPDATA%\Microsoft\dotnet`) : `OpenPub.Core` (modèle, `.opub`, annuler/rétablir, embarquement des polices), `OpenPub.Layout` (moteur de texte), `OpenPub.App` (WPF), `OpenPub.Tests` (19 tests, tous verts).
- Moteur de texte : chaînes de zones, habillage, césure française (motifs LibreOffice), justification, réduction en cas de débordement, lignes vides logées à côté des images, tolérance de fin de ligne. Il est **calé sur les aperçus Publisher** de la newsletter : la page 1 tient à 100 %, et les coupures de ligne et les positions correspondent à environ 0,5 pt près.
- Éditeur : sélection, déplacement, redimensionnement, flèches, Annuler/Rétablir, position/taille en cm, premier/arrière-plan, dupliquer, supprimer, ajouter une zone de texte ou une image, remplacer une image, éditeur de texte enrichi (police, taille, gras, italique, souligné, couleur, alignement), impression (dont « Microsoft Print to PDF »), export PNG.
- Ligne de commande : `--import dossier sortie.opub`, `--render fichier dossier [dpi]`, `--dump fichier [page] [--noshrink] [--all]` (diagnostic de mise en page).
- `newsletter.opub` (5,9 Mo) embarque la police Cavolini : il s'ouvre aussi sur un PC sans Office.

**Calibrations propres à ce document** (mesurées sur les aperçus Publisher, dans `tools/pub2json.py`) : interligne simple de Cavolini = 1,07 × taille et ligne de base à 0,64 ; style hérité des paragraphes Cavolini (interligne 105 %, 4,8 pt après), non exporté par libmspub ; marge intérieure de 11,5 pt pour le texte logé dans un rectangle arrondi.

**Écarts connus :** le texte du bandeau incliné n'est pas incliné (rotation non fournie par libmspub) ; les reflets sous les photos sont absents ; l'application se lance pour l'instant via `dotnet OpenPub.App.dll` (la version installable autonome relève de la phase 6).

## 6 ter. Étapes 1 à 4 réalisées (2026-11-01, nuit)

1. **Pages** : ajouter (vierge), dupliquer (base du numéro suivant), supprimer, monter/descendre ; page active mise en évidence ; tout est annulable.
2. **Édition sur la page** (`StoryEditor` dans le noyau, `InPlaceTextEditor` dans l'application) : double-clic pour écrire directement dans le texte. Curseur, sélection à la souris (double-clic : mot, triple-clic : paragraphe) et au clavier (Maj, Ctrl + flèches, Origine/Fin, Ctrl+A), saisie, Entrée, Maj+Entrée, Retour arrière/Suppr (Ctrl : mot), copier/couper/coller, Ctrl+B/I/U. La frappe continue forme une seule étape d'annulation, et le texte coule en direct dans les zones liées. La barre « Mise en forme » (police, taille, G/I/S, couleur, alignement) s'applique à la sélection, ou à tout le texte si la zone est seulement sélectionnée.
3. **Repères et magnétisme** : repères de marge (0,5 pouce par défaut, réglables dans le document) ; alignement automatique sur les bords et le centre de la page, les marges, les bords et centres des autres objets, avec repères magenta pendant le glissement. Alt désactive le magnétisme ; Maj sur un coin conserve les proportions. Interrupteurs « Magnétisme » et « Repères » dans la barre d'outils.
4. **Version installable** : `tools/build-installer.ps1` lance les tests, publie `dist/OpenPub/OpenPub.exe` (autonome, runtime .NET inclus, 62 Mo), puis construit `dist/OpenPub-1.0.0.msi` (WiX 5, installation par utilisateur sans droits administrateur dans `%LOCALAPPDATA%\Programs\OpenPub`, raccourci dans le menu Démarrer, association des fichiers `.opub`, désinstallation depuis les Paramètres).

Vérifications : 29 tests unitaires et un test de bout en bout de l'interface (`OpenPub.exe --selftest fichier.opub dossier`), 25 vérifications sur la vraie fenêtre : édition, sélection, gras, paragraphes, coulée dans les colonnes, annulation, pages, magnétisme.

## 6 quater. Version 1.1 : dessin, rotation, retouche d'images, ruban (2026-11-01, nuit)

- **Ruban à onglets** : menu Fichier (Ouvrir, Enregistrer, Enregistrer sous, Imprimer/PDF, Exporter PNG, Quitter), onglets Accueil (historique, police, paragraphe, objets), Insertion (pages, zone de texte, image, formes, ligne), Affichage (zoom, repères, magnétisme). Deux onglets **contextuels**, comme dans Office : « Format de la forme » et « Outils image ».
- **Formes** à tracer à la souris : rectangle, rectangle arrondi, ellipse, triangle, losange, hexagone, étoile, flèche, bulle, cœur, ligne. Maj : carré, cercle, angle de 45°.
- **Zones de texte avec cadre** : n'importe quelle forme comme cadre (bords arrondis, rayon réglable), remplissage, contour (couleur, épaisseur, tirets), ombre.
- **Rotation** de tous les objets : poignée verte (Maj : pas de 15° ; magnétisme sur 0/90/180°), angle en degrés, ±90°. Le texte reste éditable dans une zone tournée.
- **Retouche d'images** (éditeur avec aperçu en direct + boutons rapides du ruban) : recadrage à la souris, gris, noir et blanc, sépia, filigrane, luminosité, contraste, transparence, miroir horizontal/vertical, forme de découpe, ombre portée (distance, flou, opacité, direction), reflet (taille, opacité, écart), réinitialisation.
- **Sélecteur de couleur** : palette de thème avec nuances, couleurs standard, couleurs du document, code hexadécimal.
- **Import** : les zones de texte inclinées de la newsletter (bandeau de la page 1, encadré de la page 4) reprennent leur angle, retrouvé sur la forme de leur groupe.
- Vérifications : 30 tests unitaires et 39 vérifications de bout en bout sur la vraie fenêtre ; installeur `dist/OpenPub-1.1.0.msi`.

## 6 quinquies. Mises à jour automatiques (version 1.2, coût Azure : 0 €)

- **Hébergement** : Azure Static Web Apps, **offre Free** (gratuite en permanence) : `openpub-updates` dans le groupe `rg-openpub-updates` (Europe de l'Ouest). Adresse : https://calm-pebble-0080d6003.5.azurestaticapps.net (contient `latest.json`, l'installeur MSI et une page de téléchargement).
- **Dans l'application** : vérification discrète 3 s après le démarrage (désactivable dans Fichier). Un bandeau propose « Mettre à jour maintenant / Plus tard / Ignorer cette version ». OpenPub télécharge alors le MSI, vérifie sa **signature RSA-3072** et son **empreinte SHA-256**, propose d'enregistrer le document, installe en mode passif (par utilisateur, sans droits administrateur) puis se relance sur le document ouvert. Menu Fichier : « Rechercher des mises à jour… ».
- **Sécurité** : `latest.json` est signé avec une clé privée qui reste sur le poste de publication (`%APPDATA%\OpenPub\update-signing-key.pem`, **à sauvegarder et ne jamais partager**). L'application embarque la clé publique. Un `latest.json` modifié (version, adresse ou empreinte) est refusé : vérifié par 7 tests unitaires et hors ligne avec `OpenPub.exe --verify-update`.
- **Publier une version** : `tools/publish-update.sh X.Y.Z "Nouveautés" --deploy` enchaîne le numéro de version, les tests, l'exécutable autonome, le MSI, la signature, l'envoi sur Azure (outil officiel `@azure/static-web-apps-cli` via npx, jeton lu par `az`). Sans `--deploy` : préparation locale dans `work/site`.
- **Limites** : la première installation de la 1.2 se fait à la main (les versions précédentes n'avaient pas de mise à jour automatique). Perdre la clé privée oblige à réinstaller une version portant une nouvelle clé. L'offre Free limite le site à environ 250 Mo ; le script n'y place que la dernière version.

## 6 sexies. Version 1.3 : dessin libre

- **Onglet « Dessin »** du ruban :
  - **Outils** : Sélection, Crayon (main levée, trait simplifié par Douglas-Peucker puis lissé en courbes de Bézier ; l'outil reste actif), Forme libre (un clic par sommet, double-clic ou Entrée pour terminer, clic sur le premier point pour fermer et remplir, Maj : 45°, Retour arrière : retirer un point), Ligne, Flèche ;
  - **galerie des formes** directement visible ;
  - **couleur et épaisseur du trait**.
- **Flèches** : pointes au début et/ou à la fin de toute ligne ou tracé ouvert (Format de la forme ▸ Flèches) ; extrémités et angles arrondis.
- **Corrections** : interblocage de `--check-update` en ligne de commande (requêtes réseau sans retour sur le fil de l'interface) ; l'ombre portée garde une direction fixe sur la page quand l'objet tourne.
- Tests de bout en bout lancés depuis la conversion (`converted/newsletter-natif/document.json`), jamais sur le fichier de travail : 44 vérifications.

## 6 septies. Version 1.4 : nouvelle charte graphique (assets/openpub-design)

- **Application** :
  - nouveau logo et nouvelle icône (16 à 256 px) ;
  - polices **DM Sans** (interface) et **Plus Jakarta Sans** (titres), intégrées à l'application en graisses fixes, produites depuis les polices variables avec fontTools (licence OFL, fichiers dans `src/OpenPub.App/Fonts`) ;
  - palette encre `#111c43`, bleu `#087aff`, violet `#9935f4`, turquoise `#06c7c0`, accent en dégradé bleu→violet ;
  - onglets en pilule, onglets contextuels marqués d'une pastille, icônes du ruban en dégradé, bouton Fichier et bouton de mise à jour en pilule ;
  - boutons, cases et champs arrondis dans toute l'application (fenêtres secondaires comprises) ;
  - fond de travail en dégradé clair, ombre douce sous les pages, miniatures avec cadre violet et pastille numérotée ;
  - barre d'état avec « Page n sur N » et curseur de zoom.
- **Page de téléchargement** : modèle `tools/site-template` tiré de la maquette.
  - Elle est générée à chaque publication : version, taille, nouveautés, et **vraie capture** de l'application produite par `OpenPub.exe --screenshot` sur un **document de démonstration neutre** (`tools/site-template/demo`).
  - La newsletter n'apparaît pas sur la page publique : elle contient des noms et des photos d'enfants.
  - Les polices sont hébergées sur le site en WOFF2 (aucun appel à Google Fonts).

## 6 octies. Version 1.5 : aide en ligne et liens contextuels

- **Documentation** sur le site Azure, dossier `/aide/` (gratuit) : 11 pages et 49 sections.
  - Pages : premiers pas, interface et ruban, texte, images, formes et dessin, disposition, pages, impression et PDF, mises à jour, raccourcis clavier, questions fréquentes.
  - Charte OpenPub, recherche intégrée (sans serveur), sommaire latéral, vraies captures de chaque situation (document de démonstration).
  - Générée à chaque publication par `tools/build-docs.py` ; le contenu se modifie dans ce fichier.
- **Liens contextuels** (`src/OpenPub.App/Help/HelpService.cs`) :
  - **F1** ouvre la section correspondant à ce que fait l'utilisateur : écriture, zone liée, image, forme, outil de dessin, onglet ;
  - groupe **Aide** en fin de chaque onglet du ruban, bouton **?** dans l'accès rapide et le panneau Pages ;
  - lien « En savoir plus » adaptatif dans le panneau de droite et dans le bandeau de mise à jour ;
  - menu Fichier : Aide en ligne, Raccourcis clavier, guide « préparer le numéro suivant » ;
  - F1 et bouton Aide dans l'éditeur d'images, l'éditeur de texte et le sélecteur de couleur.
- **Contrôle des liens** : le script de publication refuse de publier si un lien de l'application (`OpenPub.exe --help-topics`) ne correspond à aucune section de l'aide.

## 6 nonies. Version 1.5.1 : menus et boutons

- **Menus déroulants** (Fichier, galerie Formes, menus contextuels) : panneau blanc aux coins arrondis, ombre douce, lignes arrondies surlignées au survol, icônes en dégradé, coche en dégradé pour les options, raccourcis en gris, séparateurs fins.
- **Infobulles** sombres et arrondies ; éléments des listes déroulantes arrondis et surlignés aux couleurs de la charte.
- Bouton **Fichier**, bouton **Mettre à jour maintenant** et **onglets** du ruban : rectangle aux coins arrondis, couleurs inchangées.

## 6 decies. Pipelines de publication (GitHub Actions)

- **« Publier une version de l'app »** (`.github/workflows/release-app.yml`).
  - Déclenchement : étiquette `vX.Y.Z` (`git tag -a v1.6.0 -m "Nouveautés" && git push origin v1.6.0`) ou lancement manuel avec un mode « essai ».
  - Construction sous Windows : tests, exécutable autonome, MSI (version passée par `-p:ProductVersion`), `latest.json` signé (secret `UPDATE_SIGNING_KEY`), portail.
  - Publication sous Linux : release GitHub (MSI, exécutable, `latest.json`) et envoi sur Azure (secret `AZURE_STATIC_WEB_APPS_API_TOKEN`).
- **« Publier le portail »** (`.github/workflows/publish-portal.yml`).
  - Déclenchement : modification de la page, de l'aide ou de la charte sur `main`, ou lancement manuel.
  - Régénère la page et l'aide à partir des fichiers de la **dernière release** (installeur, exécutable pour les captures, `latest.json` déjà signé), sans reconstruire l'application, puis envoie sur Azure.
- Logique commune : `tools/build-site.py` (également utilisée par `tools/publish-update.sh`, qui reste disponible en local). Un verrou commun empêche deux envois simultanés vers le portail.

## 6 undecies. Version 1.5.2 : documents récents et fenêtre « À propos »

- **Documents récents** dans le menu Fichier, sous « Ouvrir… » : les 8 derniers documents ouverts ou enregistrés (numéro, nom, dossier ; chemin complet en infobulle ; document ouvert en gras).
  - Clic ou **Ctrl+Maj+1…9** pour rouvrir ; document disparu retiré de la liste ; « Effacer la liste ».
  - Stockés dans `%APPDATA%\OpenPub\settings.json` (`AppSettings.cs`, partagé avec les réglages de mise à jour).
  - Jamais alimentés par les tests et captures (`MainWindow.TrackRecent`, fichiers temporaires ignorés) ; la capture `file-menu` montre des noms fictifs.
- **Fenêtre « À propos »** (`AboutWindow.cs`) aux couleurs de la charte, à la place de la boîte de message : bandeau en dégradé avec logo et pastille de version, tableau d'informations (version, installation, mises à jour, environnement, site), crédits, boutons Aide en ligne / Mises à jour / Fermer. Scène de capture `about`.

## 7. Prochaines étapes proposées
1. ✅ ~~Extraction via Publisher~~ : impossible depuis la perte de licence ; acquis partiels conservés.
2. ✅ Lecteur natif prototype sur la newsletter : texte → zones, polices, images d'origine, aperçu fidèle.
3. ✅ ~~Archive .pub~~ : un seul fichier, déjà converti.
4. ✅ Solution .NET et éditeur WPF (phases 1–2).
5. ✅ Pages, édition sur la page, repères et magnétisme, installeur.
6. Suite possible : export PDF direct (sans passer par l'imprimante), lier ou délier des zones de texte à la main, texte incliné, pages maîtres, correcteur orthographique sur la page, mises à jour automatiques.
