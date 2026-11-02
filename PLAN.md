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

## 6 duodecies. Portail : deux versions téléchargeables, page « Fonctionnalités »

- **Quota** : Azure Static Web Apps Free limite le site à 250 Mo. Le site n'héberge que l'installeur **courant** (≈ 56 Mo), à côté de `latest.json` (inchangé : signé, `url` relative = ce MSI, donc mises à jour automatiques intactes). L'exécutable autonome n'est jamais déposé sur Azure (seulement dans la release GitHub).
- **Version précédente** : lien vers l'installeur de **sa release GitHub** (`releases/download/vX.Y.Z/OpenPub-X.Y.Z.msi`), sans aucun octet sur Azure. `build-site.py` la trouve seul : release publiée (ni brouillon ni pré-version) de plus haut numéro **strictement inférieur** à la version courante, ayant son MSI. Lecture par `gh api` (`GH_TOKEN` dans les deux pipelines ; gh Windows sous WSL) puis repli sur l'API publique ; sans réponse, la page sort sans ce lien (avertissement). `--previous X.Y.Z|none` force le choix. La carte rappelle qu'il faut désinstaller avant de revenir en arrière (le MSI refuse les retours de version).
- **Budget mesuré** (1.5.2) : installeur 56,3 Mo + aide 2,0 Mo + images et polices 0,7 Mo = **59,1 Mo**. `build-site.py` échoue au-delà de `--max-mb` (230 Mo) ou si un autre `.msi`/`.exe` se trouve sur le site.
- **Pages** : navigation commune Accueil · Fonctionnalités · Aide (aussi dans l'aide, `build-docs.py`).
  - `index.html` : téléchargement (version et date, taille), nouveautés (notes de `latest.json`, découpées sur « ; »), configuration requise, carte discrète « Version précédente », raccourcis vers les domaines.
  - `fonctionnalites.html` : 8 domaines (texte, objets, images, dessin, écriture, sûreté, impression, mises à jour), icônes au dégradé de la charte, vraies captures du document de démonstration (celles de l'aide réutilisées ; `file-menu` et `about` dans `assets/captures/`).
- **Contenu** : `tools/site-template/fonctionnalites.json`. Un élément porte `depuis` (première version qui le livre) : il n'est affiché que si la version publiée est au moins celle-là (badge « Nouveau » pour la version courante). Les lots 1 à 5 de la section 8 y sont décrits avec `"depuis": "2.0.0"` : ils apparaîtront seuls avec la 2.0.0 ; retirer ou repousser un élément non livré. `--apercu-a-venir` les montre pour relecture (jamais pour publier).
- Cache (`staticwebapp.config.json`) : `latest.json` jamais en cache, MSI et polices immuables, `/`, `index.html` et `fonctionnalites.html` revalidés (ils citent le MSI courant).

## 6 terdecies. Version 2.0.0 : lots 1 à 5 du plan d'alignement (2026-11-02)

La 2.0.0 livre d'un coup les lots 0 à 5 de la section 8 (tout l'usage mensuel de la newsletter). Les lots 6 à 12
deviennent les versions 2.1 et suivantes.

- **Lot 0** : `MainWindow` découpé en classes partielles par domaine (`MainWindow.Objects.cs`, `.Paragraph.cs`,
  `.TextFrame.cs`, `.Review.cs`, `.Recovery.cs`) ; sélection multiple dans `EditorSession` (`Selection`, `SelectMany`,
  `ToggleSelect` ; `Selected` reste l'objet principal) ; commandes `GroupCommand` / `UngroupCommand` avec `CanExecute`.
  Coût de l'annulation mesuré sur la newsletter : **2,9 ms par action** (instantané de 91 Ko), rien à optimiser.
- **Lot 1, objets** (`Core/Editing/ShapeOps.cs`, `PageView`) : Ctrl/Maj + clic, rectangle de sélection, Ctrl+A ;
  déplacement et mise à l'échelle de l'ensemble (magnétisme sur la boîte commune) ; grouper (Ctrl+G) / dissocier
  (Ctrl+Maj+G), un clic sélectionne le groupe, un double-clic un membre ; aligner et répartir (sur la sélection ou les
  marges) ; avancer / reculer d'un plan ; verrouiller la position (`Shape.Locked`) ; presse-papiers `OpenPub.Shapes.v1`
  (objets, histoires des chaînes incomplètes, images en base64) entre pages et documents, collage d'images et de texte
  externes ; menu contextuel de la page.
  - Non fait : rotation d'une sélection multiple d'un bloc (chaque objet tourne sur lui-même).
- **Lot 2, paragraphes et caractères** (`TextLayoutEngine`) : `Paragraph.List` (puces, numérotation 1/a/A/i/I,
  niveaux, Tab / Maj+Tab, Entrée sur un élément vide), `Paragraph.Tabs` (gauche, centre, droite, décimal, points de
  suite), `Paragraph.DropCap` ; `FontSpec.Strike/Script/Caps/Highlight/Tracking`. Les morceaux posés sont découpés
  par apparence (casse, exposant) sans changer les positions source ; `LaidLine.Marks` porte ce qui est dessiné hors du
  texte éditable (puce, points de suite, lettrine) ; `LaidPiece.Dy/Tracking/Advance()` pour le rendu et le curseur.
  Boîte `ParagraphWindow`, pinceau de reproduction de la mise en forme.
- **Lot 3, zones de texte** (`PageLayouter`) : créer / rompre un lien (même page), depuis l'indicateur A… aussi ;
  `Shape.Columns` enfin utilisé (sous-zones enchaînées) ; `Shape.AutoFit` (réduire, agrandir la zone — calculé dans
  `EditorSession.Layout` —, aucun) ; `Shape.Wrap` (automatique, aucun, carré, serré par bandes de 4 pt sur une ellipse
  ou un tracé, haut et bas) et `WrapDistance`.
  - Non fait : liens entre pages (la mise en page reste page par page).
- **Lot 4, révision** : correcteur de Windows par COM (`Editing/SpellService.cs`, spellcheck.h ; aucun dictionnaire
  embarqué), soulignement à l'écran seulement, suggestions au clic droit, F7 (`SpellingWindow`) ; rechercher /
  remplacer (`Core/Editing/TextSearch.cs`, `FindReplaceWindow`) ; typographie française à la frappe
  (`FrenchTypography`) ; symboles, date, statistiques ; onglet Révision ; diagnostic `OpenPub.exe --spell "texte"`.
- **Lot 5, sûreté** : `Core/Io/RecoveryStore.cs` (copies dans `%LOCALAPPDATA%\OpenPub\Recovery`, toutes les
  5 minutes, proposées au démarrage, supprimées à l'enregistrement ou à la fermeture normale) ; `OpubPackage.SaveCopy`,
  `Retarget`, `Save(keepBackup)` (« .bak ») ; alerte si le fichier a changé sur le disque depuis l'ouverture.
- **Vérifications** : 68 tests unitaires (dont 31 nouveaux : objets, listes, tabulations, lettrine, styles,
  colonnes, habillage, recherche, typographie, récupération) et **115 vérifications** de bout en bout. L'auto-test se
  termine désormais franchement en cas d'exception (une fenêtre restée ouverte bloquait le processus).
- **Aide en ligne** : nouvelle page `revision.html` et sections `texte.html#paragraphe|listes|tabulations|zone-options`,
  `disposition.html#plusieurs|grouper`, `interface.html#recuperation` ; raccourcis mis à jour ; capture de l'onglet
  Révision (`onglet-revision`).
- **Portail** : la page Fonctionnalités affiche les éléments `"depuis": "2.0.0"` dès la publication (section 6 duodecies).

## 6 quattuordecies. Version 2.1.0 (en préparation) : lots 6, 7, 8, 11 et 12

Lots 9 (formes, effets, WordArt) et 10 (PDF, impression) : réalisés en parallèle, voir les sections 9 et 10.

- **Lot 6, création de page** (`MainWindow.PageDesign.cs`, `MainWindow.Rulers.cs`, `PageSetupWindow`, `Controls/RulerBar`).
  - Pages maîtres : `PubDocument.Masters` ; une page utilise `Page.Master` (sinon la première) sauf si `IgnoreMaster`.
    Mode de modification : les vues affichent les maîtres (`PageList`) ; rendu sous la page, non sélectionnable.
  - Champs `Run.Field` (« page », « pages », « date », « merge:… ») : segment « # » non fusionnable, valeur calculée
    à la mise en page par page (`EditorSession.FieldsFor`, `LayoutMaster(master, page)` mis en cache) ; le champ ne
    compte que pour un caractère source (curseur juste).
  - Numérotation (`PageNumbering` : style, premier numéro, `Page.NumberStart`), arrière-plan (`Page.BackColor` ou image
    `Shape.Background`), boîte Mise en page (format, orientation, marges, grille `LayoutGrid`, adaptation des objets),
    Fichier ▸ Nouveau, double page (grille à deux colonnes), règles (cm, page active, sélection surlignée), repères de
    règle `PubDocument.Guides` (glisser depuis la règle, déplacer, supprimer hors de la page), magnétisme sur repères
    et grille.
  - Format : `PubFormat.Required` écrit « openpub/2 » dès qu'une nouveauté serait perdue par une version 1 ;
    une version plus récente que `SupportedFormat` s'ouvre mais ne s'enregistre pas.
    - Limite connue : la 2.0.0 ne connaît pas ce garde-fou ; elle perdrait les nouveautés en enregistrant un
      document 2.1 (d'où l'intérêt des mises à jour automatiques).
- **Lot 7, tableaux** (`Core/Editing/TableOps.cs`, `MainWindow.Tables.cs`) : `ShapeKind.Table` + `TableData`
  (colonnes, hauteurs minimales, cellules avec fusion `RowSpan/ColSpan/Hidden`, bordures par côté).
  - Chaque cellule visible est mise en page comme une **zone de texte fantôme** stable (`PageLayouter.CellFrame`,
    `CellOf`) : l'éditeur sur la page, l'orthographe, la recherche et le rendu du texte fonctionnent sans code dédié.
  - Les lignes s'agrandissent avec le texte (`TableData.ActualRows`, hauteur de l'objet recalculée).
  - Tab / Maj+Tab, ligne ajoutée au bout, colonnes et lignes à la souris, collage TSV, styles, calendrier.
- **Lot 8, styles et thèmes** (`Core/Editing/StyleOps.cs`, `MainWindow.Styles.cs`, `StyleWindow`).
  - Styles de paragraphe et de caractère (`Paragraph.Style`, `Run.Style`) : appliquer recopie les valeurs ;
    modifier un style les réapplique partout (une mise en forme locale des mêmes propriétés est alors remplacée,
    choix assumé pour rester simple).
  - Jeux de couleurs : les cinq couleurs du jeu précédent sont remplacées partout par celles du nouveau jeu
    (pas de référence symbolique dans le rendu). Jeux de polices : titres / texte, styles compris.
- **Lot 11, modèles et blocs** (`Core/Editing/Templates.cs`, `MainWindow.Templates.cs`) : 4 modèles construits en
  code, modèles de l'utilisateur (`%APPDATA%\OpenPub\Modèles\*.opubt`, un document créé d'après eux est « sans
  titre »), 6 blocs fournis et blocs de l'utilisateur (`Blocs\*.json` = presse-papiers OpenPub, images comprises),
  calendrier du mois ou du mois prochain.
- **Lot 12, envois** (`Core/Editing/MergeOps.cs`, `MainWindow.Merge.cs`) : publipostage (CSV ou liste collée depuis
  Excel copiée dans le document, champs «Colonne», aperçu, document fusionné), liens hypertextes (`Run.Link`,
  `Shape.Link`, Ctrl + clic), texte de remplacement (`Shape.AltText`), page web autonome (pages en PNG incluses,
  `<map>` des liens, texte des pages pour l'accessibilité).
  - Après la fusion des lots 9 et 10 : texte le long d'un tracé libre (`WordArtWarps.Path`, Dessin ▸ Texte sur le
    tracé) et liens actifs dans le PDF (`MainWindow.LinkAreas`, sauf PDF/X-4) sont faits. Reste : PDF balisé
    (textes de remplacement, ordre de lecture).
- **Vérifications** : 88 tests unitaires, 160 vérifications de bout en bout (les échecs intermittents de copier/coller
  venaient d'autres processus utilisant le presse-papiers Windows en même temps).

## 7. Prochaines étapes proposées
1. ✅ ~~Extraction via Publisher~~ : impossible depuis la perte de licence ; acquis partiels conservés.
2. ✅ Lecteur natif prototype sur la newsletter : texte → zones, polices, images d'origine, aperçu fidèle.
3. ✅ ~~Archive .pub~~ : un seul fichier, déjà converti.
4. ✅ Solution .NET et éditeur WPF (phases 1–2).
5. ✅ Pages, édition sur la page, repères et magnétisme, installeur.
6. ✅ Mises à jour automatiques, texte incliné, aide en ligne, pipelines de publication.
7. Suite : **plan d'alignement sur Publisher**, section 8.

---

## 8. Plan d'alignement sur Publisher (hors format .pub)

Établi le 2026-11-02 à partir de l'inventaire des fonctions de Publisher (Microsoft 365) comparé au code d'OpenPub 1.5.2. **Le format `.pub` est exclu** : ni lecture intégrée, ni écriture.

### 8.1 Principes

- **Ordre** : d'abord ce qui sert chaque mois pour la newsletter (objets, paragraphes, zones liées, orthographe), ensuite la structure du document (pages maîtres, tableaux), enfin les sorties professionnelles et les fonctions rares.
- **Une version par lot**, publiée par la pipeline habituelle (étiquette `vX.Y.Z`) ; chaque lot reste utilisable seul.
- **Chaque lot comprend** : tests unitaires (moteur, noyau), vérifications ajoutées à `--selftest`, sections d'aide dans `tools/build-docs.py` (avec constantes `HelpService` et captures du document de démonstration), section de ce journal.
- **Format `.opub`** : les ajouts restent facultatifs dans `document.json` (un ancien fichier s'ouvre toujours). Dès qu'un lot ajoute une donnée qu'une version plus ancienne perdrait à l'enregistrement (pages maîtres, tableaux, styles), on passe à `"format": "openpub/2"` et l'application refuse d'**enregistrer** un fichier d'un format plus récent que le sien, en invitant à mettre à jour.
- **Estimations** en jours de développement pour une personne, tests et aide compris.

### 8.2 Lot 0 — Préalables techniques (avec la 1.6)

| Tâche | Détail | Jours |
|---|---|---|
| Découper `MainWindow.xaml.cs` (1 300 lignes) | Classes partielles par onglet du ruban (`MainWindow.Home.cs`, `.Insert.cs`, `.Draw.cs`, `.Shape.cs`, `.Picture.cs`, `.File.cs`) | 1 |
| Sélection multiple dans le modèle d'édition | `EditorSession.Selection` (liste) à la place de `Selected` ; `Selected` reste comme raccourci du premier objet ; propriétés affichées « mixtes » | 2 |
| Commandes du ruban | Passer les boutons à des `RoutedCommand` avec `CanExecute`, pour activer/désactiver proprement selon la sélection | 1 |
| Coût de l'annulation | Mesurer la photo complète du document par action (`UndoStack`, limite 200) sur un document de 12 pages ; compresser ou ne garder que les pages modifiées si nécessaire | 1 |

### 8.3 Lot 1 — Objets (version 1.6)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Sélection multiple | Ctrl/Maj + clic, rectangle de sélection sur la page vide, Ctrl+A hors édition de texte ; déplacement, flèches, suppression et rotation de l'ensemble | 3 |
| Copier / couper / coller des objets | Presse-papiers Windows au format privé `OpenPub.Shapes` (JSON des formes + images en base64) ; collage sur la page active, décalé si même page ; entre deux documents OpenPub ; collage d'une **image** ou d'un **texte** copié depuis une autre application | 3 |
| Grouper / dissocier | Champ `Group` déjà présent : poignées et rotation sur le cadre du groupe, double-clic pour entrer dans le groupe, Ctrl+Maj+G | 3 |
| Aligner et répartir | Gauche/centre/droite/haut/milieu/bas, répartir horizontalement/verticalement, par rapport à la sélection ou à la page | 1,5 |
| Ordre d'empilement fin | « Avancer d'un plan » / « Reculer d'un plan » en plus de premier plan / arrière-plan | 0,5 |
| Verrouiller la position | Option par objet (pratique pour les bandeaux), conservée dans le fichier | 0,5 |

### 8.4 Lot 2 — Paragraphes et caractères (version 1.7)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Interligne, espacement avant/après | Données déjà lues par le moteur (`LineHeight`, `SpaceBefore/After`) : groupe « Paragraphe » du ruban (liste 1 / 1,15 / 1,5 / 2 + valeur libre) et boîte « Paragraphe… » | 2 |
| Retraits (gauche, droite, première ligne) | Mêmes données (`IndentLeft/Right`, `FirstIndent`) ; boutons Augmenter/Diminuer le retrait ; boîte « Paragraphe… » | 1,5 |
| Puces et numérotation | Modèle : `Paragraph.List` (type, symbole, niveau) ; moteur : marqueur dessiné dans le retrait négatif, numéros recalculés le long de l'histoire ; Tab / Maj+Tab change le niveau | 4 |
| Tabulations | Taquets par paragraphe (gauche, centre, droite, décimal, points de suite) dans `TextLayoutEngine` ; aujourd'hui le caractère Tab est saisi mais non aligné | 3 |
| Barré, exposant, indice, petites majuscules, tout en majuscules | Nouveaux champs de `Font` ; rendu et mesure (exposant/indice : taille 58 %, décalage de ligne de base) | 2 |
| Surlignage du texte | Couleur de fond par segment, dessinée sous la ligne | 1 |
| Interlettrage et crénage | `Font.Tracking` (espacement en 1/1000 em) ; crénage via les tables de la police (`GlyphTypeface`) | 2 |
| Lettrine | Paragraphe avec `DropCap` (lignes, police) : obstacle rectangulaire en début de paragraphe dans le moteur | 2 |
| Reproduire la mise en forme | Pinceau : copie la police et le paragraphe de la sélection, applique au clic suivant | 1 |

### 8.5 Lot 3 — Zones de texte (version 1.8)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Lier / délier des zones | Bouton « Créer un lien » puis clic sur une zone vide (curseur cruche, comme Publisher) ; « Rompre le lien » ; flèches « zone suivante / précédente » sur les zones liées ; le texte reste à la tête de chaîne (`Chain`, `ChainIndex`) | 4 |
| Indicateur de débordement | Pastille « A… » sur la dernière zone quand le texte ne tient pas et que la réduction est désactivée ; clic pour lier une nouvelle zone | 1 |
| Ajustement automatique réglable | Par zone : « Ne pas ajuster » / « Réduire le texte en cas de débordement » (actuel, par défaut) / « Ajuster la zone au texte » | 2 |
| Colonnes dans une zone | `Columns` et `ColumnGap` existent dans le modèle mais sont ignorés : le moteur découpe la zone en sous-zones virtuelles enchaînées (il sait déjà faire couler le texte de zone en zone) | 2,5 |
| Marges intérieures, alignement vertical | `Padding` et `VAlign` existent : à exposer dans « Format de la zone de texte » | 1 |
| Habillage réglable | Par obstacle : aucun / carré / serré / haut et bas, distances réglables ; « serré » suit le contour de la forme (géométrie) ou de l'image (pixels non transparents), calculé ligne par ligne dans `PageLayouter` | 4 |

### 8.6 Lot 4 — Outils d'écriture (version 1.9)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Correcteur orthographique | API de Windows (`ISpellChecker`, français fourni par Windows, aucun dictionnaire à embarquer) ; vérification en arrière-plan par paragraphe ; soulignement ondulé rouge dans `PageRenderer` ; clic droit : suggestions, Ignorer, Ajouter au dictionnaire ; langue par segment (`Font.Lang` existe) | 5 |
| Vérification de tout le document | Fenêtre « Orthographe » qui parcourt toutes les histoires (F7) | 1,5 |
| Rechercher / remplacer | Ctrl+F / Ctrl+H : recherche dans toutes les histoires, respect de la casse, mot entier ; sélection du résultat sur la page ; remplacement annulable en une étape | 3 |
| Symboles et caractères spéciaux | Insertion : espace insécable, tirets, guillemets « », symboles récents ; remplacement automatique à la frappe (guillemets français, espace insécable avant ; : ! ?) | 2 |
| Date et heure, numéro de page | Champs dynamiques dans le texte (le numéro de page dépend du lot 6) | 1 |
| Statistiques | Mots, caractères, paragraphes de la zone ou du document | 0,5 |

### 8.7 Lot 5 — Sûreté des données (version 1.10)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Enregistrement automatique | Toutes les 5 minutes, si modifié : copie de secours dans `%LOCALAPPDATA%\OpenPub\Recovery` (jamais à la place du fichier de l'utilisateur) | 1,5 |
| Récupération après plantage | Au démarrage, proposition de rouvrir la copie de secours plus récente que le fichier ; suppression à la fermeture normale | 1 |
| Enregistrement sûr | Écriture dans un fichier temporaire puis remplacement atomique ; conservation de la version précédente en `.bak` | 1 |
| Fichier modifié ailleurs | Avertissement si le `.opub` a changé sur le disque depuis l'ouverture (OneDrive, autre PC) | 0,5 |

### 8.8 Lot 6 — Document et pages (version 2.0, format `openpub/2`)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Fichier ▸ Nouveau | Document vierge (A4, A5, Lettre, personnalisé ; portrait/paysage) | 1 |
| Taille, orientation et marges de page | Onglet « Création de page » : modifier la taille (avec option de mise à l'échelle des objets), l'orientation, les marges | 2 |
| Pages maîtres | Modèle : `PubDocument.Masters` (pages d'objets communs), `Page.Master` ; rendu sous la page, objets non sélectionnables sur les pages normales ; édition des pages maîtres dans un mode dédié ; maître différent pour pages paires/impaires ; « ignorer le maître » sur une page | 6 |
| Numéros de page, en-têtes et pieds de page | Champ « numéro de page » (et nombre de pages), généralement posé sur la page maître | 1,5 |
| Arrière-plan de page | Couleur, dégradé ou image par page ou par maître (`Background` existe pour les images importées) | 1,5 |
| Vue en double page | Pages paires et impaires côte à côte, comme une planche imprimée | 2 |
| Règles et repères de règle | Règles graduées (cm) en haut et à gauche ; repères tirés depuis les règles ; magnétisme sur ces repères (`Snapper`) | 3 |
| Guides de disposition | Grille de colonnes et de lignes de la page, lignes de base | 2 |
| Sections et numérotation personnalisée | Début de numérotation, format (1, i, A) | 1 |

### 8.9 Lot 7 — Tableaux (version 2.1)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Insérer un tableau | Nouveau type `ShapeKind.Table` : lignes, colonnes (largeurs/hauteurs), cellules avec `Paragraphs` ; chaque cellule mise en page par `TextLayoutEngine` (une zone par cellule) | 5 |
| Édition | Écriture dans les cellules (Tab : cellule suivante), sélection de cellules, insertion/suppression de lignes et colonnes, redimensionnement à la souris | 5 |
| Mise en forme | Bordures par côté, remplissage des cellules, fusion/fractionnement, alignement vertical, styles de tableau prédéfinis | 3 |
| Coller un tableau | Depuis Excel/Word (format HTML ou texte tabulé du presse-papiers) | 1,5 |

### 8.10 Lot 8 — Styles et thèmes (version 2.2)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Styles de paragraphe et de caractère | `PubDocument.Styles` ; `Paragraph.Style` + mises en forme locales par-dessus ; galerie dans l'onglet Accueil ; « Nouveau style à partir de la sélection » ; modifier un style met à jour tout le document | 5 |
| Jeu de couleurs du document | Palette nommée (principale, accent 1 à 4…) ; les objets peuvent y faire référence ; changer le jeu recolore le document ; visible dans le sélecteur de couleur | 3 |
| Jeu de polices | Police des titres et police du corps, utilisées par les styles | 1,5 |

### 8.11 Lot 9 — Formes, effets et texte décoratif (version 2.3)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Bibliothèque de formes étendue | De 11 à environ 50 formes : bulles et légendes à pointe déplaçable, bannières, rubans, flèches (double, courbe, bloc), étoiles (5 à 32 branches), parallélogramme, trapèze, octogone, nuage, éclair, soleil, lune, croix, cadre ; poignées jaunes de réglage (comme Office) | 5 |
| Remplissages | Dégradé (linéaire/radial, éditeur d'arrêts), motif, texture ou image en remplissage ; transparence du remplissage et du contour | 3 |
| Modifier les points | Édition des sommets et des courbes de Bézier d'un tracé (`Path`) : déplacer, ajouter, supprimer, lisser/angle | 4 |
| Effets d'images et de formes | Lueur, bords adoucis, ombre intérieure | 2 |
| WordArt | Texte décoratif : remplissage, contour, ombre ; déformations en arc, vague, cercle (glyphes placés le long d'un tracé) | 5 |
| Bordures décoratives (BorderArt) | Motifs répétés le long du cadre d'une forme ; petite bibliothèque libre de droits | 2 |
| Espaces réservés et permutation d'images | Cadre « image » vide avec bouton d'insertion ; échanger deux images en gardant les cadres | 1,5 |
| Légende d'image | Groupe image + zone de texte prépositionnée | 0,5 |
| Compression des images | Réduire la résolution des images au besoin d'impression (300 ppp) pour alléger le `.opub` | 1 |

### 8.12 Lot 10 — Sorties professionnelles (version 2.4)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Abstraction du rendu | `PageRenderer` dessine aujourd'hui dans un `DrawingContext` WPF ; interface de dessin commune (WPF / PDF) pour garantir un PDF identique à l'écran | 4 |
| Export PDF direct | Bibliothèque **PDFsharp 6** (licence MIT) : texte vectoriel avec polices incorporées en sous-ensemble, images d'origine, transparences, ombres en image ; Fichier ▸ Exporter en PDF | 6 |
| PDF pour imprimeur | PDF/X-4, fond perdu (3 mm), traits de coupe ; conversion CMJN des couleurs simple (profil ISO Coated v2 / FOGRA39) | 4 |
| Impression avancée | Choix des pages, nombre d'exemplaires, aperçu avant impression ; **livret** (imposition 2 pages par face, ordre de pliage) ; recto verso manuel guidé | 4 |
| Export d'images | JPEG en plus du PNG, choix de la résolution, page active seulement | 1 |

### 8.13 Lot 11 — Modèles et éléments réutilisables (version 2.5)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Modèles | « Enregistrer comme modèle » (`.opubt`) ; Fichier ▸ Nouveau propose les modèles de l'utilisateur et quelques modèles fournis (newsletter, affiche, carte, invitation) construits sur le document de démonstration | 3 |
| Blocs de construction | Bibliothèque d'éléments enregistrés (encadré, bandeau, calendrier, coupon) à insérer en un clic ; repose sur le format de presse-papiers du lot 1 | 2 |
| Calendriers | Générateur de calendrier mensuel (tableau du lot 7) | 1,5 |

### 8.14 Lot 12 — Fonctions rares (à confirmer avant de les engager)

| Fonction Publisher | Mise en œuvre | Jours |
|---|---|---|
| Publipostage | Source CSV ou Excel, champs dans le texte, génération d'un PDF ou d'une impression par ligne | 5 |
| Liens hypertextes | Sur du texte ou un objet ; actifs dans le PDF exporté | 1,5 |
| Envoi par e-mail / export HTML | Page exportée en image ou HTML pour un courriel | 2 |
| Texte sur tracé libre | Variante du WordArt le long d'un tracé quelconque | 2 |
| Accessibilité | Texte de remplacement des images, ordre de lecture dans le PDF balisé | 2 |

### 8.15 Synthèse

| Lot | Version | Contenu | Jours |
|---|---|---|---|
| 0 | 1.6 | Préalables techniques | 5 |
| 1 | 1.6 | Objets : sélection multiple, copier/coller, grouper, aligner | 11,5 |
| 2 | 1.7 | Paragraphes et caractères | 18,5 |
| 3 | 1.8 | Zones de texte : liens, colonnes, habillage, ajustement | 14,5 |
| 4 | 1.9 | Orthographe, rechercher/remplacer, symboles | 13 |
| 5 | 1.10 | Enregistrement automatique, récupération | 4 |
| 6 | 2.0 | Document et pages : nouveau, taille, pages maîtres, règles | 20 |
| 7 | 2.1 | Tableaux | 14,5 |
| 8 | 2.2 | Styles, jeux de couleurs et de polices | 9,5 |
| 9 | 2.3 | Formes, effets, WordArt, BorderArt | 24 |
| 10 | 2.4 | PDF direct, PDF imprimeur, livret | 19 |
| 11 | 2.5 | Modèles, blocs de construction | 6,5 |
| 12 | — | Fonctions rares (à confirmer) | 12,5 |
| | | **Total** | **≈ 173 jours** (≈ 160 sans le lot 12) |

**Jalons** : après les lots 0 à 5 (≈ 67 jours), OpenPub couvre tout l'usage mensuel de la newsletter sans manque gênant ; la version 2.0 apporte la structure de document de Publisher ; les lots 9 à 11 complètent la parité visuelle et les sorties d'imprimerie.

**Risques** : habillage serré et WordArt (géométrie du texte), tableaux (nouveau type d'objet dans tout l'éditeur), PDF identique à l'écran (polices et effets) ; pour chacun, un prototype de 1 à 2 jours avant d'engager le lot.

---

## 9. Lot 10 livré : PDF direct et impression

Réalisé le 2026-11-02 sur la base de la 2.0.0 (les nouveautés sont annoncées `"depuis": "2.1.0"` sur le portail ;
numéros de version inchangés).

### Fait

- **Export PDF direct** (Fichier ▸ Exporter en PDF…, `Output/PdfExporter.cs`) avec **PDFsharp 6.2.4** (MIT, paquet
  `PDFsharp`, version « Core » sans GDI ni WPF, cible `net10.0` incluse). Pas d'abstraction du rendu : chaque page est
  dessinée par `PageRenderer.DrawPage` (`editorMarks: false`, **inchangé**) dans un `DrawingGroup`, puis
  `Output/DrawingToPdf.cs` parcourt l'arbre :
  - `GeometryDrawing` → tracés PDFsharp (arcs convertis en Bézier, transformation de la géométrie appliquée aux
    points, règle de remplissage, tirets, extrémités, jointures, transparence par `ExtGState`) ;
  - `GlyphRunDrawing` → texte réel : segments de glyphes placés à la position exacte du `GlyphRun` (un nouveau segment
    dès que l'avance WPF — crénage, interlettrage — diffère de l'avance de la police) ; les **indices de glyphes de WPF**
    sont imposés à PDFsharp par son événement `RenderTextEvent` (mêmes formes qu'à l'écran) ; texte Unicode
    sélectionnable et recherchable ; polices incorporées en **sous-ensemble** ;
  - `ImageDrawing` → **fichier d'origine** recopié (JPEG sans recompression, PNG sans perte) ; images retouchées,
    recadrées ou issues d'un WMF → PNG sans perte ; une image utilisée plusieurs fois n'est incorporée qu'une fois ;
    transparence d'image appliquée dans les pixels (pas de rééchantillonnage) ;
  - `DrawingGroup` → `Transform`, `ClipGeometry`, `Opacity` ; `OpacityMask` (reflet), effets, pinceaux non unis
    → **rasterisés localement à 300 ppp**. Les ombres floues sont déjà des images dans le rendu (`ImageCache.Shadow`).
  - Polices (`Output/PdfFonts.cs`, `IFontResolver`) : chaque `GlyphTypeface` rencontré (police système ou du dossier
    `fonts/` du paquet, comme Cavolini) est relu depuis le vrai fichier ; **collections .ttc** découpées en .ttf
    autonome (`Core/Output/SfntFont.cs`, PDFsharp ne les gère pas) ; italique simulé reproduit par PDFsharp ;
    **gras simulé, contours CFF, licence « restricted »** (OS/2 fsType) → texte en contours vectoriels.
- **PDF pour l'imprimeur** (options de la boîte d'export) :
  - **fond perdu** (3 mm par défaut) : feuille agrandie, découpe de la page élargie au fond perdu, et
    `Core/Output/PrintLayout.cs` `BleedExtender` **prolonge** temporairement les rectangles pleins et les images qui
    touchent un bord (tolérance 1 pt ; pas les formes non rectangulaires, objets tournés, zones de texte, images
    détourées ou à reflet). Les images gardent leurs proportions : on dévoile la partie recadrée, sinon agrandissement
    minimal autour du bord opposé. La mise en page du texte est calculée avant (inchangée) ; le document est restauré
    exactement (vérifié par l'auto-test) ;
  - **traits de coupe** (8 traits de 5 mm, à 3 mm du format fini, hors fond perdu) ; `TrimBox` et `BleedBox` ;
  - **PDF/X-4** : PDF 1.6, `GTS_PDFXVersion`, `Trapped`, intention de sortie `GTS_PDFX` avec le **profil CMJN
    incorporé** (cherché dans `C:\Windows\System32\spool\drivers\color`, FOGRA39 en premier ; « Autre profil… »),
    couleurs du rendu étiquetées sRGB (`DefaultRGB` = profil sRGB de Windows sur chaque page), métadonnées XMP
    (`pdfxid:GTS_PDFXVersion`, dates et producteur cohérents avec Info). PDFsharp réécrit à l'enregistrement ses
    propres XMP et un groupe de transparence `DeviceRGB` : `Core/Output/PdfIncrementalUpdate.cs` les remplace par une
    **mise à jour incrémentale** standard (catalogue, pages, nouvelle table de références chaînée par `/Prev`).
- **Impression avancée** (`Output/PrintService.cs`, `Output/PrintWindow.cs`) : choix des pages (`1-3; 5`, `4-`),
  exemplaires assemblés (le document est répété), **aperçu avant impression** (vignettes des feuilles dans l'ordre,
  légendées « Feuille 1, recto — pages 8 et 1 »), **livret** (`Booklet.Sides` : 8-1, 2-7, 6-3, 4-5 ; complété à un
  multiple de 4 ; feuille paysage du papier choisi, pages réduites et centrées ; recto verso bord court demandé au
  pilote), **recto verso manuel guidé** (rectos, message « retournez la pile », versos complétés par une face blanche,
  option « versos dans l'ordre inverse ») et livret en recto verso manuel.
- **Export d'images** (`Output/ImageExportWindow.cs`, `Exporter.ExportImages`) : PNG ou JPEG (qualité 92), 24 à
  1200 ppp, page active (nom de fichier au choix), toutes ou liste.
- **Ligne de commande** : `--pdf <fichier> <sortie.pdf> [--pages 1-3] [--bleed [mm]] [--marks] [--pdfx [profil.icc]]
  [--dpi n]` (affiche le résumé du PDF relu : pages, feuille, format fini, polices incorporées, images, PDF/X) et
  `--images <fichier> <dossier> [--jpeg] [--dpi n] [--pages …]`.
- **Interface** : menu Fichier « Imprimer… » (Ctrl+P), « Exporter en PDF… », « Exporter en images (PNG, JPEG)… »
  (gestionnaires dans `MainWindow.Output.cs`) ; F1 dans chaque boîte ouvre la section d'aide.
- **Aide** : `impression.html` réécrite (`#imprimer`, `#apercu`, `#livret`, `#recto-verso`, `#pdf`, `#imprimeur`,
  `#png`), constantes `HelpService.PrintPreview/Booklet/ManualDuplex/PdfForPrinter/ImageExport` (47 sujets, tous
  trouvés dans l'aide générée) ; raccourcis et menu Fichier mis à jour ; 6 éléments « Impression et PDF » sur le portail.

### Mesuré

- Fidélité : pages rendues depuis le PDF (MuPDF) comparées au rendu PNG d'OpenPub à 72 ppp : écart moyen 0,5 à
  2/255 sur la démo, 4,7 à 6,9/255 sur la newsletter (lissage différent du texte et des images rééchantillonnées ;
  positions des lignes et des mots identiques au point près à 200 ppp).
- Newsletter (4 pages, 10 polices, 16 images) : 2,6 s en ligne de commande (démarrage des polices compris), 5,7 Mo
  (images d'origine : 5,8 Mo) ; avec fond perdu, traits de coupe et PDF/X-4 : 2,9 s, 6,6 Mo. Dans l'auto-test
  (5 pages) : 1,3 s. Démo : 0,5 s, 96 Ko.
- Tests : **78 tests unitaires** (10 nouveaux : livret, recto verso, choix des pages, fond perdu et traits de coupe,
  extension des rectangles et des images, collections .ttc, licence de police, mise à jour incrémentale) et
  **134 vérifications** de bout en bout (19 nouvelles : PDF relu par PDFsharp, nombre et taille des pages, polices
  incorporées, texte non converti en contours, images, fond perdu, format fini, document intact, PDF/X-4, reflet
  rasterisé, livret, recto verso, exemplaires, aperçu, menu, JPEG, PNG). Captures `impression-livret.png` et
  `export-pdf.png` dans le dossier de l'auto-test.

### Non fait, limites

- **Conformité PDF/X-4 non validée** par un outil de contrôle (pas d'Acrobat Preflight ni de callas sur la machine) :
  la structure suit la norme, mais un contrôle par l'imprimeur reste conseillé.
- **Conversion CMJN non faite** : `PdfDocumentOptions.ColorMode = Cmyk` de PDFsharp est une formule naïve, sans profil,
  qui ne touche pas les images ; la conversion est laissée au RIP de l'imprimeur (profil de sortie incorporé). Une
  vraie conversion demanderait le moteur de couleurs de Windows (WCS) pour les couleurs et `ColorConvertedBitmap`
  pour les images, plus la gestion de la transparence en CMJN.
- Opacité de groupe appliquée enfant par enfant (exact pour le rendu actuel, où un groupe opaque ne contient qu'une
  image) ; un futur groupe semi-transparent de plusieurs objets qui se chevauchent serait légèrement différent.
- Ligatures (« fi ») : dessinées en lettres séparées à la position exacte de la ligature (le texte extrait reste
  juste) ; gras simulé en contours (pas de texte sélectionnable pour ces mots).
- Pas d'aperçu à l'échelle de l'imprimante choisie avant la boîte Windows : l'aperçu du livret utilise le papier de
  l'imprimante par défaut (A4 sinon) ; l'impression réelle prend le papier choisi.
- Pas encore de PDF en livret (imposition) ni de PDF balisé (accessibilité, lot 12).

---

## 10. Lot 9 livré : formes, effets et texte décoratif

Réalisé sur la branche du lot 9 (version inchangée : à publier avec la prochaine version, `"depuis": "2.1.0"` dans
`fonctionnalites.json`). Tous les nouveaux champs de `document.json` sont facultatifs ; une version plus ancienne les
ignore et garde `Fill.Color`, toujours renseignée (première couleur d'un dégradé, fond d'un motif).

- **Conception** : la géométrie est passée dans le noyau, sans WPF. `Core/Geometry/ShapeLibrary.cs` décrit chaque
  forme automatique (contour en segments M/L/C/Q/Z calculé depuis le cadre, catégorie, réglages par défaut, poignées) ;
  `Rendering/ShapeGeometries.cs` ne fait plus que convertir en géométrie WPF. Les mêmes contours servent au rendu, à
  l'habillage serré (`PageLayouter.Bands` utilise `ShapeLibrary.Polygons`, courbes aplaties, plusieurs sous-tracés pour
  un cadre ou un anneau) et à la modification des points. Les nouveaux champs sont dans `Core/Model/DecorModel.cs`
  (classes `Shape`, `Fill`, `LineStyle`, `ShapeKind` rendues `partial` dans `Document.cs`, seul changement de ce
  fichier).
- **50 formes** en 4 catégories (formes de base, flèches pleines, étoiles et bannières, bulles et légendes), dont les
  étoiles de 4 à 32 branches, nuage, éclair, soleil, lune, croix, cadre, anneau, visage, flèches doubles, quadruple,
  courbe, circulaire, à encoche, ruban, bannière ondulée, légendes rectangulaire / arrondie / ovale / pensée / trait.
  Les 10 identifiants existants sont gardés (la bulle par défaut est identique à l'ancienne). Menu Insertion ▸ Formes en
  grilles par catégorie ; galerie de l'onglet Dessin réduite à 14 formes + « ⋯ » ; liste « Forme » avec intertitres.
- **Poignées jaunes** : `Shape.Adjust` (liste de nombres en parts du cadre, donc qui suivent le redimensionnement ;
  null = valeurs par défaut). Chaque `ShapeDef` déclare ses `AdjustHandle` (position et effet d'un glissement, dans le
  repère non tourné de l'objet) ; `PageView.Shapes.cs` les dessine (losanges jaunes) et les fait glisser. Flèches
  (épaisseur + pointe, une poignée 2D), étoiles (profondeur), bulles (pointe libre, même hors du cadre), coins arrondis
  (`CornerRadius`), cadre, anneau, lune, soleil, sourire, parallélogramme, trapèze, hexagone, octogone, chevron, ruban…
  Changer de forme remet `Adjust` à null.
- **Remplissages** (`Rendering/DecorRenderer.cs`) : `Fill.Stops` / `GradientType` / `Angle` (dégradé linéaire ou
  radial ; l'ancien drapeau d'import `Fill.Gradient` est inchangé), `Fill.Pattern` / `PatternColor` (12 motifs en
  carreaux de 8 pt dessinés en code), `Fill.Image` / `ImageTile` (couvre la forme ou mosaïque à 150 ppp),
  `LineStyle.Opacity`. Menu Remplissage ▾ (dégradés prêts à l'emploi calculés depuis la couleur de la forme, motifs,
  image, transparences) et `FillWindow` (éditeur d'arrêts : couleur, position, opacité ; aperçu). Les images de
  remplissage sont gardées à l'enregistrement (`OpubPackage.RemoveUnusedAssets`) et suivent le copier-coller entre
  documents.
- **Effets** : `Shape.Effects` (`EffectsFx` : lueur couleur/taille/opacité, bords adoucis, ombre intérieure `ShadowFx`)
  pour les formes, cadres de zones de texte, images et WordArt ; rendus par silhouettes floutées mises en cache
  (`ImageCache.Shadow`) : lueur = silhouette élargie, bords adoucis = masque d'opacité de la silhouette rétrécie, ombre
  intérieure = extérieur flouté, décalé et découpé par la silhouette. Menu Effets ▾ (préréglages) et `EffectsWindow`.
- **Modifier les points** (`Core/Geometry/PathEditor.cs`, mode dans `PageView.Shapes.cs`) : la forme est convertie en
  tracé (annulable), puis normalisée (Q → C, sommet de fermeture explicite). Glisser un sommet (ses poignées suivent)
  ou une poignée de courbe (la poignée opposée reste alignée sur un point lisse) ; Ctrl + clic ou double-clic sur le
  contour ajoute un sommet (découpe de Casteljau, la courbe ne change pas) ; Ctrl + clic ou Suppr supprime (au moins
  2 sommets, 3 si fermé) ; double-clic sur un sommet : lisse / angle vif. Le cadre est recalculé à la fin du geste, avec
  compensation pour un objet tourné (rien ne bouge à l'écran). Les arcs SVG importés (A) gardent leurs extrémités
  modifiables et sont aplatis exactement pour les boîtes englobantes.
- **WordArt** : nouveau type d'objet `"wordart"` (`Shape.WordArt` : texte d'une ligne, police, gras, italique,
  déformation). Choix retenu plutôt qu'une option de zone de texte : aucun changement du moteur de texte, de l'édition
  sur la page ni des chaînes ; le texte est dessiné comme une forme (contours des glyphes), donc remplissage (dégradé,
  motif, image), contour, ombre, effets et rotation s'appliquent sans code spécial. Le placement est un calcul pur
  (`Core/Geometry/TextOnPath.cs`) : texte droit qui remplit le cadre (étiré), vague, arc vers le haut / le bas
  (demi-ellipse, taille réduite jusqu'à ce que le texte tienne), cercle (texte centré en haut) ; glyphes posés le long
  d'une polyligne par abscisse curviligne (`Polyline`), réutilisable pour le « texte sur tracé » du lot 12. Insertion ▸
  WordArt ▾ (6 styles), double-clic ou « Texte WordArt… » (`WordArtWindow`, aperçu), « Déformation ▾ ». Un nouveau
  WordArt prend les proportions naturelles de son texte.
- **Bordures décoratives** : `Shape.BorderArt` (motif, taille, couleur, couleur alternée) ; 12 motifs dessinés en code
  à partir des formes de la bibliothèque (`BorderArtLayout` : un motif par coin, répartition régulière sur les bords,
  motifs tournés vers l'extérieur, à l'intérieur du cadre). Pour formes, zones de texte et images.
- **Images** : espace réservé = image sans `Src` (`Shape.IsPlaceholder`), dessiné à l'écran seulement (cadre gris
  pointillé, bouton central ; rien à l'impression ni à l'export) ; clic sur le bouton ou double-clic → choix de l'image,
  qui remplit le cadre (`ImageFit.CoverCrop`). « Permuter » échange les images de deux cadres (recadrage recalculé pour
  chacun), « Vider le cadre », « Légende » (zone de texte groupée sous l'image ; sans image, Insertion ▸ Image +
  légende crée l'espace réservé et sa légende ; le texte de la page contourne le groupe comme avant).
- **Compression** (`Core/Imaging/ImageTools.cs` pour le calcul, `MainWindow.Shapes.cs` pour les pixels) : résolution utile
  de chaque image = plus grande taille affichée (recadrage et remplissage compris) à 300 ppp ; une image plus grande de
  15 % est réduite (JPEG qualité 88, PNG si transparence), sous un nouveau nom ; les anciennes quittent le paquet à
  l'enregistrement (annulable jusque-là). Les mosaïques ne sont pas touchées.
- **Fichiers partagés touchés** (blocs marqués « lot 9 ») : `Document.cs` (mot `partial`), `PageRenderer.cs`
  (appels à `DecorRenderer` : objets spéciaux, bordure, corps avec effets, pinceaux, `PenOf`, réglages des formes),
  `PageView.cs` (classe `partial`, 6 appels d'une ligne vers `PageView.Shapes.cs`), `MainWindow.xaml` (groupe « Styles et
  effets » qui reprend le bouton Ombre, groupe « Cadre » des outils image, WordArt / espace réservé / légende dans
  Insertion), `MainWindow.xaml.cs` (galeries, abonnements, aide contextuelle, espace réservé dans Remplacer / Modifier),
  `MainWindow.Objects.cs` (images de remplissage au presse-papiers), `PageLayouter.cs` (polygones d'habillage),
  `OpubPackage.cs` (images de remplissage gardées), `SelfTest.cs` (classe `partial`, un appel).
- **Vérifications** : 95 tests unitaires (27 nouveaux : bibliothèque, poignées, habillage d'une étoile, points, arcs SVG,
  WordArt, bordures, recadrage, compression, format de fichier, paquet) ; **151 vérifications** de bout en bout (36
  nouvelles, sur une page vierge ajoutée au document : aucune capture ne montre la newsletter), captures
  `selftest-9-*.png`.
- **Aide** : `formes.html#reglages|remplissages|effets|points|wordart|bordures`, `images.html#espaces-reserves|legende|compresser`
  (constantes `HelpService`, vérifiées contre `ancres.txt`) ; nouvelle capture `menu-formes` (scène `shapes-menu`).
- **Non fait** :
  - « Texture » en remplissage (Publisher propose des photos de bois, papier…) : pas de bibliothèque libre de droits
    embarquée ; une image en mosaïque en tient lieu.
  - WordArt sur plusieurs lignes et déformations « enveloppe » (glyphes déformés, pas seulement tournés) : une ligne
    par WordArt ; les déformations Office qui étirent les lettres demanderaient de transformer chaque contour point
    par point.
  - Poignées jaunes en sélection multiple ; clic sur la pointe d'une légende hors du cadre (la sélection se fait sur
    le cadre) ; flèches du clavier pour déplacer un sommet.
  - Effets et WordArt ne sont pas encore pris en compte par un export PDF vectoriel (lot 10) : le rendu actuel est
    celui de WPF (écran, impression, PNG).
  - Le site d'aide est régénéré par `publish-portal.yml` à chaque modification de `build-docs.py` : les nouvelles
    sections seront en ligne avant la version qui les apporte (captures de la version publiée) ; à pousser avec la
    version, ou à accepter.
