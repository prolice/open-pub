"""Genere la documentation en ligne d'OpenPub (dossier aide/ du site).

Usage : python3 -I tools/build-docs.py DOSSIER_SITE [VERSION]
Produit DOSSIER_SITE/aide/*.html, aide.css, recherche.js. Les captures sont attendues dans
DOSSIER_SITE/aide/img/<scene>.png (produites par OpenPub.exe --screenshot, voir publish-update.sh).

Les ancres (#...) sont utilisees par les liens contextuels de l'application (Help.cs) : ne pas les renommer.
"""
import html
import json
import os
import re
import sys

OUT = sys.argv[1] if len(sys.argv) > 1 else "work/site"
VERSION = sys.argv[2] if len(sys.argv) > 2 else ""


def k(*keys):
    """Touches clavier : k("Ctrl", "S") -> <kbd>Ctrl</kbd>+<kbd>S</kbd>"""
    return "+".join(f"<kbd>{html.escape(x)}</kbd>" for x in keys)


def tip(text):
    return f'<div class="callout tip"><strong>Astuce</strong><p>{text}</p></div>'


def warn(text):
    return f'<div class="callout warn"><strong>Attention</strong><p>{text}</p></div>'


def shot(scene, alt, caption=None):
    cap = f"<figcaption>{caption}</figcaption>" if caption else ""
    return f'<figure class="shot"><img src="img/{scene}.png" alt="{html.escape(alt)}" loading="lazy">{cap}</figure>'


def steps(*items):
    return "<ol class=\"steps\">" + "".join(f"<li>{i}</li>" for i in items) + "</ol>"


def table(rows, head=("Action", "Raccourci")):
    h = "".join(f"<th>{c}</th>" for c in head)
    b = "".join("<tr>" + "".join(f"<td>{c}</td>" for c in r) + "</tr>" for r in rows)
    return f'<div class="table-wrap"><table><thead><tr>{h}</tr></thead><tbody>{b}</tbody></table></div>'


# ------------------------------------------------------------------ contenu
# (fichier, titre, description, icone, sections[(ancre, titre, html)])
PAGES = [
    ("index.html", "Bienvenue dans OpenPub", "Prendre en main OpenPub en quelques minutes.", "🏠", [
        ("premiers-pas", "Premiers pas", f"""
<p>OpenPub est un éditeur de mise en page pour Windows : bulletins, affiches, cartes, invitations. Il ouvre les
documents <strong>.opub</strong>, et reprend la mise en page des anciens fichiers Microsoft Publisher convertis.</p>
{steps("Lancez OpenPub depuis le menu Démarrer, ou double-cliquez sur un fichier <strong>.opub</strong>.",
       f"Ouvrez un document avec <strong>Fichier ▸ Ouvrir…</strong> ({k('Ctrl', 'O')}).",
       "Double-cliquez dans un texte pour l'écrire directement sur la page ; double-cliquez sur une image pour la retoucher.",
       f"Enregistrez avec {k('Ctrl', 'S')}. Tout se retrouve dans un seul fichier .opub : textes, images et polices.")}
{shot("accueil", "Fenêtre principale d'OpenPub", "La fenêtre d'OpenPub : ruban en haut, pages à gauche, propriétés à droite.")}
"""),
        ("aide-contextuelle", "Obtenir de l'aide", f"""
<p>Appuyez sur {k('F1')} à tout moment : OpenPub ouvre la page d'aide de ce que vous êtes en train de faire
(texte en cours d'écriture, image sélectionnée, outil de dessin, onglet du ruban…). Chaque onglet du ruban a aussi
son bouton <strong>?</strong>, et le panneau de droite propose un lien « En savoir plus ».</p>
"""),
        ("sommaire", "Sommaire", """
<div class="cards">
<a class="card" href="interface.html"><b>Interface et ruban</b><span>Fichier, onglets, panneaux, barre d'état</span></a>
<a class="card" href="texte.html"><b>Texte</b><span>Écrire, paragraphes, listes, zones liées, colonnes</span></a>
<a class="card" href="images.html"><b>Images</b><span>Insérer, recadrer, couleurs, ombre, reflet</span></a>
<a class="card" href="formes.html"><b>Formes et dessin</b><span>Formes, WordArt, effets, crayon, forme libre</span></a>
<a class="card" href="disposition.html"><b>Disposition</b><span>Plusieurs objets, grouper, aligner, magnétisme</span></a>
<a class="card" href="revision.html"><b>Révision</b><span>Orthographe, rechercher et remplacer, typographie</span></a>
<a class="card" href="tableaux.html"><b>Tableaux</b><span>Insérer, fusionner, styles, calendrier</span></a>
<a class="card" href="modeles.html"><b>Modèles et blocs</b><span>Nouveau document, vos modèles, blocs réutilisables</span></a>
<a class="card" href="envois.html"><b>Publipostage et web</b><span>Copies personnalisées, liens, export web</span></a>
<a class="card" href="pages.html"><b>Pages</b><span>Mise en page, pages maîtres, numéros, couleurs</span></a>
<a class="card" href="impression.html"><b>Impression et PDF</b><span>Imprimer, créer un PDF, exporter en images</span></a>
<a class="card" href="mises-a-jour.html"><b>Mises à jour</b><span>Automatiques et sécurisées</span></a>
<a class="card" href="raccourcis.html"><b>Raccourcis clavier</b><span>Tous les raccourcis</span></a>
<a class="card" href="faq.html"><b>Questions fréquentes</b><span>Polices, fichiers .pub, sauvegardes…</span></a>
</div>"""),
    ]),

    ("interface.html", "Interface et ruban", "Le ruban, les panneaux et la barre d'état.", "🧭", [
        ("ruban", "Le ruban", f"""
<p>Le ruban regroupe les commandes par onglets. Les onglets <strong>Format de la forme</strong> (pastille violette) et
<strong>Outils image</strong> (pastille turquoise) apparaissent automatiquement quand vous sélectionnez une forme, une
zone de texte ou une image.</p>
{shot("onglet-accueil", "Onglet Accueil du ruban")}"""),
        ("fichier", "Le menu Fichier", f"""
<p>Le bouton <strong>Fichier</strong> donne accès à : Ouvrir ({k('Ctrl', 'O')}), Enregistrer ({k('Ctrl', 'S')}),
Enregistrer sous ({k('Ctrl', 'Maj', 'S')}), Imprimer ({k('Ctrl', 'P')}), Exporter en PDF, Exporter en images,
Rechercher des mises à jour, l'aide en ligne et À propos (version, type d'installation, état des mises à jour).</p>
<p>Sous « Ouvrir… », la section <strong>Documents récents</strong> liste les derniers documents ouverts ou
enregistrés (nom et dossier) : un clic les rouvre, {k('Ctrl', 'Maj', '1')} à {k('Ctrl', 'Maj', '9')} aussi.
Un document déplacé ou supprimé disparaît de la liste ; « Effacer la liste » la vide.</p>
<p>Deux options du menu protègent votre travail : la <strong>copie de secours automatique</strong> (voir
<a href="#recuperation">Récupérer un document</a>) et <strong>Garder la version précédente (.bak)</strong>, qui conserve
à côté du fichier la version d'avant chaque enregistrement (« numero.opub.bak », à renommer en .opub pour l'ouvrir).</p>"""),
        ("accueil", "Onglet Accueil", """
<ul><li><strong>Historique</strong> : Annuler et Rétablir.</li>
<li><strong>Police</strong> : police, taille, gras, italique, souligné, couleur du texte.</li>
<li><strong>Police</strong> (suite) : barré, exposant, indice, majuscules, espacement des caractères, surlignage,
reproduire la mise en forme (pinceau).</li>
<li><strong>Paragraphe</strong> : puces, numérotation, retraits, interligne, alignements, lettrine et boîte
<strong>¶…</strong> (voir <a href="texte.html#paragraphe">Paragraphes</a>).</li>
<li><strong>Objets</strong> : dupliquer, premier plan, arrière-plan, supprimer, grouper, dissocier, aligner, avancer,
reculer, verrouiller (voir <a href="disposition.html#plusieurs">Plusieurs objets</a>).</li></ul>
<p>Quand une zone de texte est simplement sélectionnée, la police et le paragraphe s'appliquent à tout son texte ;
pendant l'écriture sur la page, ils s'appliquent à la sélection (voir <a href="texte.html#mise-en-forme">Mise en forme</a>).</p>"""),
        ("insertion", "Onglet Insertion", f"""
<p>Pages (ajouter, dupliquer), zone de texte à tracer, image depuis un fichier, galerie de formes, ligne.</p>
{shot("onglet-insertion", "Onglet Insertion du ruban")}"""),
        ("revision", "Onglet Révision", """
<p>Orthographe de tout le document, soulignement des fautes, typographie française automatique, rechercher et
remplacer, statistiques. Voir <a href="revision.html">Révision et orthographe</a>.</p>"""),
        ("affichage", "Onglet Affichage", """
<p>Zoom (−, +, 100 %, ajuster à la largeur), affichage des <strong>repères</strong> et <strong>magnétisme</strong>
(voir <a href="disposition.html#magnetisme">Magnétisme et repères</a>).</p>"""),
        ("panneaux", "Panneaux et barre d'état", f"""
<ul><li><strong>Pages</strong> (à gauche) : miniatures, la page active est encadrée en violet. Voir <a href="pages.html">Pages</a>.</li>
<li><strong>Propriétés</strong> (à droite) : position et taille en centimètres, informations sur le texte ou l'image, lien d'aide.</li>
<li><strong>Barre d'état</strong> : page active (« Page 1 sur 4 ») et curseur de zoom. {k('Ctrl')} + molette zoome aussi.</li></ul>
<p>Un <strong>clic droit</strong> sur la page ouvre un menu adapté : couper, copier, coller, grouper, verrouiller…
sur un objet ; suggestions d'orthographe pendant l'écriture.</p>"""),
        ("recuperation", "Récupérer un document après un incident", """
<p>Toutes les 5 minutes, si le document a des modifications non enregistrées, OpenPub en range une
<strong>copie de secours</strong> à part (jamais à la place de votre fichier). Si OpenPub ou l'ordinateur s'arrête
brutalement, le démarrage suivant propose de <strong>récupérer</strong> cette copie : le document retrouve ses
dernières modifications, il ne reste qu'à l'enregistrer. La copie disparaît dès que le document est enregistré ou
fermé normalement.</p>
<p>Si le fichier a été modifié par un autre programme pendant que vous travailliez (synchronisation OneDrive, autre
PC), OpenPub le signale et demande confirmation avant de le remplacer.</p>"""),
    ]),

    ("texte.html", "Texte", "Créer des zones de texte, écrire et mettre en forme.", "✍️", [
        ("creer", "Créer une zone de texte", steps(
            "Onglet <strong>Insertion</strong> ▸ <strong>Zone de texte</strong>.",
            "Tracez le cadre sur la page avec la souris (un simple clic crée un cadre de taille standard).",
            "Écrivez directement : la zone est prête à recevoir le texte.")),
        ("ecrire", "Écrire directement sur la page", f"""
<p><strong>Double-cliquez</strong> dans un texte pour y placer le curseur, comme dans Publisher. Le texte s'écoule
automatiquement dans les zones liées et contourne les images. Appuyez sur {k('Échap')} pour terminer.</p>
{shot("texte-edition", "Écriture d'un texte sur la page, avec une sélection", "Écriture sur la page : le texte sélectionné apparaît en surbrillance.")}
<ul><li>Sélection : glisser avec la souris, double-clic pour un mot, triple-clic pour un paragraphe, {k('Maj')} + flèches.</li>
<li>{k('Entrée')} crée un paragraphe, {k('Maj', 'Entrée')} un saut de ligne dans le paragraphe.</li>
<li>{k('Ctrl', 'C')}, {k('Ctrl', 'X')}, {k('Ctrl', 'V')} : copier, couper, coller (le texte collé prend la mise en forme du curseur).</li>
<li>{k('Ctrl', 'Z')} annule ; une frappe continue s'annule en une fois.</li></ul>"""),
        ("mise-en-forme", "Mise en forme", f"""
<p>Pendant l'écriture, l'onglet <strong>Accueil</strong> applique police, taille, couleur et alignement au texte
sélectionné (ou au mot sous le curseur). {k('Ctrl', 'B')} gras, {k('Ctrl', 'I')} italique, {k('Ctrl', 'U')} souligné.</p>
<p>Le groupe <strong>Police</strong> propose aussi le barré, l'exposant (x²), l'indice (x₂), <strong>Aa ▾</strong>
(tout en majuscules, petites majuscules), l'<strong>espacement des caractères</strong> (très serré à très large) et le
<strong>surlignage</strong>. Le <strong>pinceau</strong> reproduit la mise en forme : placez le curseur dans le texte
modèle, cliquez le pinceau, puis sélectionnez le texte à mettre en forme ({k('Échap')} pour annuler).</p>
{tip("Sélectionnez simplement la zone (un clic, sans écrire) pour changer la police ou l'alignement de <em>tout</em> son texte en une fois. Avec plusieurs zones sélectionnées, la mise en forme s'applique à toutes.")}"""),
        ("styles", "Styles de texte", """
<p>La liste <strong>Styles</strong> de l'onglet Accueil applique un style au paragraphe (Normal, Titre 1, Titre 2,
Chapeau, Citation, Légende) : police, taille, couleur et espacements d'un coup. <strong>Styles ▾</strong> applique un
style de caractère à la sélection (« Mise en valeur »), crée un <strong>nouveau style d'après le texte</strong>
courant, <strong>modifie</strong> un style (tous les textes qui l'utilisent suivent) ou le supprime (le texte garde
sa mise en forme).</p>"""),
        ("paragraphe", "Paragraphes : interligne, espacements, retraits", f"""
<p>Le bouton <strong>↕≡ ▾</strong> règle l'interligne (1 à 3) et ajoute un espace avant ou après le paragraphe ;
<strong>⇤</strong> et <strong>⇥</strong> diminuent ou augmentent le retrait gauche. La boîte <strong>¶…</strong>
regroupe tous les réglages : alignement, retraits gauche, droit et de première ligne (ou retrait suspendu),
espacement avant et après en points, interligne multiple, lettrine et tabulations.</p>
<p>La <strong>lettrine</strong> (bouton <strong>A≡ ▾</strong>) agrandit la première lettre du paragraphe sur 2 à
5 lignes.</p>"""),
        ("listes", "Puces et numérotation", f"""
<p><strong>•≡ ▾</strong> choisit une puce (•, ◦, ▪, –, ✓, ➢, ★), <strong>1≡ ▾</strong> une numérotation (1. 2. 3.,
a), A., i., I.…). La puce se place dans la marge, le texte reste aligné. En début d'élément, {k('Tab')} passe au
niveau inférieur et {k('Maj', 'Tab')} remonte d'un niveau ; {k('Entrée')} sur un élément vide termine la liste.
« Recommencer la numérotation à 1 » repart de 1 à partir du paragraphe choisi.</p>"""),
        ("tabulations", "Tabulations", """
<p>La touche Tab avance jusqu'au taquet suivant, tous les 1,27 cm par défaut. Dans la boîte <strong>¶…</strong>,
ajoutez des taquets à une position précise : <strong>gauche</strong>, <strong>centré</strong>,
<strong>droite</strong> (pour aligner des prix ou des heures) ou <strong>décimal</strong> (les virgules
s'alignent), avec des <strong>points de suite</strong> si besoin (« Menu ........ 12,50 »).</p>"""),
        ("zones-liees", "Zones liées (colonnes)", """
<p>Un article peut s'étaler sur plusieurs zones reliées (par exemple trois colonnes). Le texte passe d'une zone à la
suivante quand la précédente est pleine. Quand une zone liée est sélectionnée, les autres zones de la chaîne sont
entourées de pointillés, et le panneau de droite indique « zone 1 sur 3 (texte lié) ».</p>
<p>Pour lier deux zones : sélectionnez la zone où le texte commence, <strong>Format de la forme ▸ Créer un lien</strong>,
puis cliquez sur une <strong>zone vide</strong> de la même page (ou cliquez directement sur l'indicateur
<strong>A…</strong> d'une zone qui déborde). <strong>Rompre le lien</strong> arrête le texte à la zone sélectionnée ;
<strong>Zone précédente</strong> et <strong>Zone suivante</strong> parcourent la chaîne.</p>"""),
        ("habillage", "Habillage et réduction automatique", """
<p>Le texte contourne les images, les légendes et les encadrés posés dessus. Si le texte ne tient pas dans ses zones,
un petit indicateur rouge <strong>A…</strong> apparaît en bas de la zone. La <strong>réduction automatique</strong>
est optionnelle : clic droit sur la zone ▸ <strong>Réduire le texte pour qu'il tienne dans la zone</strong> (ou
<strong>Ajustement ▾</strong>, voir ci-dessous). OpenPub diminue alors la taille du texte pour qu'il tienne ; le
panneau de droite affiche « texte réduit à 92 % pour tenir ». Elle est inactive sur les nouvelles zones ; les zones
des documents existants gardent leur réglage.</p>
<p>Pour chaque objet, <strong>Format ▸ Habillage ▾</strong> choisit comment le texte le contourne :
<strong>automatique</strong> (comportement de Publisher), <strong>aucun</strong> (le texte passe dessous),
<strong>carré</strong>, <strong>serré</strong> (le texte suit le contour d'un disque ou d'une forme libre) ou
<strong>haut et bas</strong> (jamais de texte à côté), ainsi que la <strong>distance</strong> entre l'objet et le
texte.</p>"""),
        ("zone-options", "Colonnes, marges et ajustement d'une zone", """
<p>Le groupe <strong>Format de la forme ▸ Zone de texte</strong> règle la zone sélectionnée :
<strong>Colonnes ▾</strong> (1 à 4 colonnes dans la même zone, espacement réglable), <strong>Marges ▾</strong>
(marges intérieures), position du texte <strong>en haut</strong>, <strong>au milieu</strong> ou
<strong>en bas</strong>, et <strong>Ajustement ▾</strong> : ne rien faire (par défaut pour les nouvelles
zones), réduire le texte en cas de débordement, ou <strong>agrandir la zone</strong> pour contenir tout le texte.</p>"""),
        ("cadre", "Cadre arrondi ou en forme", """
<p>Sélectionnez la zone, puis onglet <strong>Format de la forme</strong> ▸ <strong>Forme</strong> ▸ par exemple
<em>Rectangle à coins arrondis</em>. Le curseur <strong>Arrondi</strong> règle le rayon ; <strong>Remplissage</strong>,
<strong>Contour</strong>, <strong>Épaisseur</strong> et <strong>Ombre</strong> habillent le cadre.</p>"""),
        ("editeur", "Éditeur de texte", """
<p>Pour les longs textes, le panneau de droite propose <strong>Éditeur de texte…</strong> : une fenêtre de traitement de
texte avec police, taille, gras, italique, souligné, couleur et alignement. <strong>Appliquer</strong> reporte le
texte dans la zone.</p>"""),
    ]),

    ("images.html", "Images", "Insérer, remplacer et retoucher les images.", "🖼️", [
        ("inserer", "Insérer ou remplacer une image", f"""
<p><strong>Insertion</strong> ▸ <strong>Image</strong> choisit une photo (JPG, PNG, GIF, BMP, TIFF). Pour changer une
photo en gardant sa place : sélectionnez-la puis <strong>Outils image</strong> ▸ <strong>Remplacer…</strong>.</p>
{tip(f"{k('Maj')} en tirant un coin conserve les proportions de la photo.")}"""),
        ("outils", "Outils image (ruban)", f"""
<p>Sélectionnez une image : l'onglet <strong>Outils image</strong> s'ouvre. Il propose en un clic les couleurs
(origine, niveaux de gris, noir et blanc, sépia, filigrane), le miroir, l'ombre, le reflet, la rotation et la
réinitialisation. Le groupe <strong>Cadre</strong> ajoute les effets (lueur, bords adoucis, ombre intérieure), la
bordure décorative, la légende, la permutation de deux images, <strong>Vider le cadre</strong> et la compression.</p>
{shot("onglet-image", "Onglet Outils image")}"""),
        ("editeur", "Éditeur d'images", f"""
<p><strong>Double-cliquez</strong> sur une image (ou <strong>Modifier l'image…</strong>) : l'éditeur affiche un aperçu
en direct. <strong>Appliquer</strong> valide, <strong>Annuler</strong> abandonne, <strong>Tout réinitialiser</strong>
revient à la photo d'origine.</p>
{shot("editeur-image", "Éditeur d'images d'OpenPub")}"""),
        ("recadrer", "Recadrer", """
<p>Cliquez sur <strong>Recadrer</strong>, puis tracez sur l'aperçu le cadre à conserver ; ajustez finement avec les
curseurs Gauche / Haut / Droite / Bas. Sur la page, le cadre de l'image rétrécit et le contenu reste en place.</p>"""),
        ("couleurs", "Couleurs et réglages", """
<ul><li><strong>Couleur</strong> : origine, gris, noir et blanc, sépia, <strong>filigrane</strong> (image très claire, idéale en fond).</li>
<li><strong>Réglages</strong> : luminosité, contraste, transparence.</li>
<li><strong>Miroir</strong> horizontal ou vertical, rotation par quarts de tour.</li></ul>"""),
        ("forme", "Forme de la photo", """
<p>Découpez la photo selon une forme : coins arrondis (rayon réglable), ellipse, étoile, cœur… Les photos importées
de Publisher peuvent garder leur <em>détourage d'origine</em>.</p>"""),
        ("ombre-reflet", "Ombre et reflet", """
<p><strong>Ombre portée</strong> : distance, flou, opacité et direction ; la direction reste la même quand l'objet
tourne. <strong>Reflet</strong> : copie en miroir sous l'image, en dégradé (taille, opacité, écart).</p>"""),
        ("espaces-reserves", "Espaces réservés et permutation", f"""
<p><strong>Insertion ▸ Espace réservé</strong> pose un cadre d'image vide (gris, avec une icône) : préparez la mise
en page, puis cliquez sur <strong>le bouton au centre du cadre</strong> (ou double-cliquez) pour choisir la photo.
Elle remplit le cadre sans être déformée (recadrage automatique, ajustable avec <strong>Recadrer</strong>). Un cadre
vide n'apparaît ni à l'impression ni à l'export. <strong>Vider le cadre</strong> retire la photo et garde le cadre.</p>
<p><strong>Permuter</strong> : sélectionnez deux images ({k('Ctrl')} + clic) puis <strong>Outils image ▸ Permuter</strong> :
les photos échangent leur place, chaque cadre garde sa position, sa taille et sa forme.</p>"""),
        ("legende", "Légende d'image", """
<p>Sélectionnez une image puis <strong>Légende</strong> (Outils image) : une zone de texte en italique est posée sous
l'image et <strong>groupée</strong> avec elle ; ils se déplacent ensemble et le texte de la page contourne les deux.
Double-cliquez sur la légende pour l'écrire. Sans image sélectionnée, <strong>Insertion ▸ Image + légende</strong>
crée un espace réservé et sa légende.</p>"""),
        ("compresser", "Compresser les images", f"""
<p>Les photos d'un appareil ou d'un téléphone sont souvent bien plus grandes que nécessaire.
<strong>Outils image ▸ Compresser…</strong> réduit à <strong>300 ppp</strong> (la qualité d'impression) chaque image
plus grande que sa plus grande taille sur la page, recadrage compris : le fichier .opub s'allège sans changement
visible. Les anciennes images quittent le fichier à l'enregistrement suivant ({k('Ctrl', 'Z')} annule tant que le
document n'est pas enregistré).</p>"""),
    ]),

    ("formes.html", "Formes et dessin", "Formes, WordArt, effets, crayon, forme libre, lignes et flèches.", "✏️", [
        ("formes", "Dessiner une forme", f"""
{steps("Onglet <strong>Dessin</strong> (ou Insertion ▸ Formes) : choisissez une forme dans la galerie.",
       f"Tracez-la sur la page. {k('Maj')} donne un carré, un cercle ou une forme régulière.",
       "La forme est sélectionnée : réglez-la dans l'onglet <strong>Format de la forme</strong>.")}
<p>Une cinquantaine de formes, rangées en quatre catégories dans <strong>Insertion ▸ Formes ▾</strong> (et
<strong>Plus ▾</strong> dans la galerie de l'onglet Dessin) :</p>
<ul><li><strong>Formes de base</strong> : rectangle (coins arrondis), ellipse, triangles, losange, parallélogramme,
trapèze, pentagone, hexagone, octogone, croix, cadre, anneau, cœur, éclair, soleil, lune, nuage, visage souriant.</li>
<li><strong>Flèches pleines</strong> : droite, gauche, haut, bas, doubles, quadruple, chevron, pentagone, flèche à
encoche, flèche courbe, flèche circulaire.</li>
<li><strong>Étoiles et bannières</strong> : étoiles de 4 à 32 branches, explosion, ruban, bannière ondulée.</li>
<li><strong>Bulles et légendes</strong> : bulle de dialogue, légendes rectangulaire, arrondie, ovale, bulle de
pensée, légende avec trait.</li></ul>
{shot("menu-formes", "Menu Formes : les catégories")}
{shot("onglet-dessin", "Onglet Dessin du ruban")}"""),
        ("reglages", "Poignées jaunes de réglage", f"""
<p>Une forme sélectionnée montre, en plus des poignées blanches, un ou deux <strong>losanges jaunes</strong> : tirez-les
pour régler la forme sans la redimensionner — épaisseur et pointe d'une flèche, profondeur des branches d'une étoile,
<strong>pointe d'une bulle</strong> (elle peut sortir du cadre pour désigner un personnage), arrondi des coins,
épaisseur d'un cadre ou d'un anneau, croissant de lune, sourire… Les réglages suivent le redimensionnement de la
forme ; {k('Ctrl', 'Z')} les annule.</p>"""),
        ("dessin-libre", "Dessin libre", f"""
<ul><li><strong>Crayon</strong> : dessinez à main levée, le trait est lissé automatiquement. L'outil reste actif pour
enchaîner les traits ; {k('Échap')} pour terminer.</li>
<li><strong>Forme libre</strong> : un clic par sommet ; double-clic ou {k('Entrée')} pour terminer ; clic sur le
premier point pour fermer (la forme se remplit) ; {k('Maj')} pour des angles de 45° ; {k('Retour arrière')} retire le
dernier point.</li>
<li><strong>Ligne</strong> et <strong>Flèche</strong> : tracez du point de départ au point d'arrivée ({k('Maj')} : 45°).</li>
<li><strong>Trait</strong> : couleur et épaisseur des prochains dessins.</li></ul>
{shot("dessin-crayon", "Outil Crayon actif dans l'onglet Dessin")}"""),
        ("format", "Format de la forme", """
<ul><li><strong>Remplissage</strong> et <strong>Contour</strong> : couleur (ou « Aucune »).</li>
<li><strong>Épaisseur</strong> et <strong>Tirets</strong> du contour.</li>
<li><strong>Flèches</strong> : pointe au début et/ou à la fin d'une ligne ou d'un tracé.</li>
<li><strong>Forme</strong> : change la forme d'un objet ou le cadre d'une zone de texte ; <strong>Arrondi</strong> des coins.</li>
<li><strong>Ombre</strong>, <strong>Rotation</strong>, <strong>Organiser</strong> (premier plan, arrière-plan, supprimer).</li>
<li>Groupe <strong>Styles</strong> : <strong>Remplissage ▾</strong>, <strong>Effets ▾</strong>, <strong>Bordure ▾</strong>,
<strong>Modifier les points</strong> et, pour un WordArt, <strong>Texte WordArt…</strong> et
<strong>Déformation ▾</strong> (voir ci-dessous).</li></ul>"""
         + "{SHOT_FORMAT}"),
        ("remplissages", "Dégradés, motifs, images et transparence", """
<p><strong>Format de la forme ▸ Remplissage ▾</strong> propose :</p>
<ul><li><strong>Couleur unie</strong> ;</li>
<li><strong>Dégradé</strong> : des dégradés prêts à l'emploi (clair vers foncé à partir de la couleur de la forme,
radial, coucher de soleil, océan, arc-en-ciel) ou un <em>dégradé personnalisé</em> : linéaire (direction en degrés)
ou radial, avec ses <strong>arrêts</strong> (couleur, position, opacité ; ajouter, supprimer) ;</li>
<li><strong>Motif</strong> : pois, lignes, diagonales, quadrillage, treillis, damier, zigzag, briques, vagues, dans
deux couleurs au choix ;</li>
<li><strong>Image</strong> : une photo remplit la forme (sans déformation) ou se répète en mosaïque ;</li>
<li><strong>Transparence</strong> du remplissage et <strong>du contour</strong>, de 0 à 90 %.</li></ul>
<p><strong>Options de remplissage…</strong> réunit tous ces réglages avec un aperçu. Un remplissage s'applique aussi
au cadre d'une zone de texte et au texte d'un WordArt.</p>"""),
        ("effets", "Lueur, bords adoucis, ombre intérieure", """
<p><strong>Effets ▾</strong> (onglet Format de la forme, ou Outils image pour une photo) ajoute une
<strong>lueur</strong> colorée autour de l'objet, des <strong>bords adoucis</strong> (le contour se fond vers la
transparence) ou une <strong>ombre intérieure</strong> (l'objet semble creusé). <strong>Options des effets…</strong>
règle couleur, taille, transparence, distance et direction. Ces effets se combinent avec l'ombre portée.</p>"""),
        ("points", "Modifier les points", f"""
<p>Sélectionnez une forme ou un tracé puis <strong>Format de la forme ▸ Modifier les points</strong> : les sommets
apparaissent en petits carrés.</p>
<ul><li><strong>Glissez</strong> un sommet pour le déplacer ; cliquez dessus pour voir ses <strong>poignées de
courbe</strong> (ronds) et glissez-les pour arrondir le tracé.</li>
<li>{k('Ctrl')} + clic (ou double-clic) <strong>sur le contour</strong> ajoute un sommet ; {k('Ctrl')} + clic
<strong>sur un sommet</strong> (ou {k('Suppr')}) le supprime.</li>
<li><strong>Double-clic sur un sommet</strong> : passe d'un <strong>angle vif</strong> à un <strong>point lisse</strong>
(et inversement).</li>
<li>{k('Échap')}, {k('Entrée')} ou un clic hors de la forme terminent la modification.</li></ul>
<p>Une forme automatique (étoile, flèche…) devient alors un tracé libre : elle perd ses poignées jaunes
({k('Ctrl', 'Z')} pour revenir en arrière).</p>"""),
        ("wordart", "WordArt (texte décoratif)", f"""
{steps("<strong>Insertion ▸ WordArt ▾</strong> : choisissez un style (dégradé, contour, arc-en-ciel, ombre, lueur…).",
       "Tapez le texte, choisissez la police, le gras ou l'italique et la <strong>déformation</strong> : texte droit, arc vers le haut, arc vers le bas, cercle ou vague.",
       "Le texte remplit son cadre : redimensionnez-le pour l'étirer ; double-clic pour modifier le texte.")}
<p><strong>Texte sur un tracé</strong> : dessinez une ligne au <strong>Crayon</strong> (ou avec la forme libre),
sélectionnez-la puis <strong>Dessin ▸ Texte sur le tracé</strong> ; les lettres suivent le tracé, qui devient un
WordArt (le redimensionner étire le tracé avec le texte).</p>
<p>Le WordArt se met en forme comme une forme : <strong>Remplissage ▾</strong> (dégradé, motif, image),
<strong>Contour</strong>, <strong>Ombre</strong>, <strong>Effets ▾</strong>, rotation. <strong>Déformation ▾</strong>
change la courbe du texte. Il ne contient qu'une ligne : pour un titre sur plusieurs lignes, utilisez plusieurs
WordArt ou une zone de texte.</p>"""),
        ("bordures", "Bordures décoratives", """
<p><strong>Bordure ▾</strong> entoure une forme, une zone de texte ou une image d'un <strong>motif répété</strong> :
cœurs, étoiles, pois, losanges, triangles, fleurs, soleils, lunes, nuages, chevrons, damier, éclairs. Réglez la
<strong>taille des motifs</strong>, leur <strong>couleur</strong> et une <strong>couleur alternée</strong>. Les motifs
sont dessinés à l'intérieur du cadre : laissez une marge intérieure suffisante dans une zone de texte.</p>"""),
        ("couleurs", "Choisir une couleur", """
<p>Le sélecteur propose les couleurs du thème et leurs nuances, des couleurs standard, les couleurs déjà utilisées
dans le document et la saisie d'un code (par exemple <code>#087aff</code>). « Aucune » retire le remplissage ou le
contour.</p>"""),
    ]),

    ("disposition.html", "Disposition des objets", "Sélectionner, déplacer, tourner, aligner.", "📐", [
        ("selection", "Sélectionner, déplacer, redimensionner", f"""
<ul><li>Cliquez sur un objet pour le sélectionner ; {k('Échap')} désélectionne.</li>
<li>Glissez pour déplacer ({k('Maj')} : horizontalement ou verticalement) ; flèches du clavier : 1 pt
({k('Maj')} : 10 pt).</li>
<li>Tirez les poignées blanches pour redimensionner ({k('Maj')} sur un coin : proportions conservées).</li></ul>"""),
        ("position", "Position et taille précises", """
<p>Le panneau de droite affiche X, Y, Largeur et Hauteur en <strong>centimètres</strong> ; tapez une valeur puis
<kbd>Entrée</kbd>.</p>"""),
        ("rotation", "Rotation", f"""
<p>Tirez la <strong>poignée verte</strong> au-dessus de l'objet ({k('Maj')} : par pas de 15° ; l'objet s'aimante
sur 0°, 90° et 180°), ou utilisez <strong>Format de la forme</strong> ▸ <strong>Rotation</strong> (±90°, angle en
degrés). Le texte d'une zone tournée reste modifiable.</p>"""),
        ("magnetisme", "Magnétisme et repères", f"""
<p>En déplaçant un objet, OpenPub l'aligne sur les bords et le centre de la page, les <strong>marges</strong>
(pointillés bleus) et les bords des autres objets ; des lignes magenta montrent l'alignement. Maintenez {k('Alt')}
pour placer librement. Onglet <strong>Affichage</strong> : activer ou masquer <strong>Repères</strong> et
<strong>Magnétisme</strong>.</p>"""),
        ("regles", "Règles et repères de règle", f"""
<p>Les <strong>règles</strong> graduées en centimètres (onglet <strong>Affichage ▸ Règles</strong>) suivent la page
active et surlignent la position de l'objet sélectionné. Glissez depuis la règle du haut (ou de gauche) vers la page
pour créer un <strong>repère</strong> horizontal (ou vertical) en vert : le magnétisme s'y accroche. Glissez un
repère pour le déplacer ({k('Maj')} s'il passe sur un objet), hors de la page pour le supprimer.
<strong>Affichage ▸ Double page</strong> montre les pages côte à côte, comme une planche imprimée.</p>"""),
        ("plusieurs", "Plusieurs objets à la fois", f"""
<ul><li>{k('Ctrl')} ou {k('Maj')} + clic ajoute ou retire un objet de la sélection ; glisser dans un espace vide
trace un <strong>rectangle de sélection</strong> (les objets entièrement dedans sont choisis) ; {k('Ctrl', 'A')}
sélectionne tous les objets de la page.</li>
<li>Les objets sélectionnés se déplacent, se redimensionnent (poignées de la sélection), se suppriment et se mettent
en forme ensemble.</li>
<li>{k('Ctrl', 'C')}, {k('Ctrl', 'X')}, {k('Ctrl', 'V')} copient, coupent et collent les objets, y compris vers une
autre page ou un autre document OpenPub ; une image ou un texte copiés dans une autre application se collent aussi.</li>
<li><strong>Aligner ▾</strong> aligne les objets à gauche, au centre, à droite, en haut, au milieu ou en bas, et les
<strong>répartit</strong> régulièrement (3 objets au moins). Un seul objet s'aligne sur les marges de la page.</li></ul>"""),
        ("grouper", "Grouper, ordonner, verrouiller", f"""
<p><strong>Grouper</strong> ({k('Ctrl', 'G')}) réunit les objets sélectionnés : un clic sélectionne alors tout le
groupe, un double-clic un de ses objets. <strong>Dissocier</strong> ({k('Ctrl', 'Maj', 'G')}) les sépare.
<strong>Avancer</strong> et <strong>Reculer</strong> déplacent l'objet d'un plan (au-dessus ou au-dessous de l'objet
qu'il chevauche) ; <strong>Premier plan</strong> et <strong>Arrière-plan</strong> le mettent tout devant ou tout
derrière. <strong>Verrouiller</strong> empêche de déplacer ou redimensionner un bandeau par erreur (cadenas gris).</p>"""),
        ("plans", "Premier plan, dupliquer, supprimer", f"""
<p>Onglet <strong>Accueil</strong> ▸ <strong>Objets</strong> : premier plan, arrière-plan, dupliquer, supprimer
({k('Suppr')}). Tout s'annule avec {k('Ctrl', 'Z')}.</p>"""),
    ]),

    ("tableaux.html", "Tableaux", "Insérer, remplir et mettre en forme un tableau.", "▦", [
        ("inserer", "Insérer un tableau", f"""
<p><strong>Insertion ▸ Tableau ▾</strong> propose des tailles courantes, un tableau personnalisé (lignes, colonnes,
style) et un <strong>calendrier</strong> du mois. Le curseur se place dans la première cellule : écrivez, puis
{k('Tab')} passe à la cellule suivante ({k('Maj', 'Tab')} : précédente) ; {k('Tab')} dans la dernière cellule ajoute
une ligne. Les lignes s'agrandissent pour contenir leur texte. Un tableau copié dans Excel ou Word se
<strong>colle</strong> directement ({k('Ctrl', 'V')}).</p>"""),
        ("modifier", "Lignes, colonnes, fusion", """
<p>Double-cliquez dans une cellule : l'onglet <strong>Outils de tableau</strong> s'ouvre. <strong>Insérer /
supprimer ▾</strong> ajoute des lignes ou colonnes autour de la cellule, les supprime, égalise largeurs ou hauteurs.
<strong>Fusionner ▾</strong> réunit une cellule avec sa voisine de droite ou du dessous (ou toute la ligne) ;
« Fractionner » la sépare. Tirez une bordure intérieure du tableau sélectionné pour régler une largeur de colonne ou
une hauteur de ligne ; les poignées redimensionnent tout le tableau.</p>"""),
        ("mise-en-forme", "Mise en forme", """
<p><strong>Styles ▾</strong> applique un style prédéfini (en-tête coloré, lignes alternées…), <strong>Bordures
▾</strong> choisit les bordures (toutes, extérieures, intérieures, lignes horizontales, aucune), leur couleur et
épaisseur, <strong>Remplissage</strong> colore la cellule en cours (ou toutes les cellules), Haut / Milieu / Bas
place le texte dans les cellules.</p>"""),
    ]),

    ("modeles.html", "Modèles et blocs", "Partir d'un modèle, réutiliser vos éléments.", "🧩", [
        ("nouveau", "Nouveau document et modèles", f"""
<p><strong>Fichier ▸ Nouveau…</strong> ({k('Ctrl', 'N')}) propose un document vierge (format, marges, nombre de
pages), les modèles fournis — lettre d'information, affiche, carte postale, invitation — et vos propres modèles.
<strong>Fichier ▸ Enregistrer comme modèle…</strong> range le document actuel parmi vos modèles : un nouveau
document créé d'après lui ne modifie jamais le modèle.</p>"""),
        ("blocs", "Blocs de construction", """
<p><strong>Insertion ▸ Blocs ▾</strong> insère au centre de la page un élément prêt à l'emploi : encadré, bandeau de
titre, citation, coupon-réponse, pastille « Nouveau », bloc contact. « Enregistrer la sélection comme bloc… »
ajoute vos propres éléments (images comprises) à ce menu, pour tous vos documents.</p>"""),
    ]),

    ("envois.html", "Publipostage, liens et web", "Personnaliser des copies, ajouter des liens, publier sur le web.", "✉️", [
        ("publipostage", "Publipostage", """
<p><strong>Insertion ▸ Publipostage ▾</strong> : choisissez une liste (fichier CSV enregistré par Excel, ou liste
copiée dans Excel puis « Coller la liste ») dont la première ligne donne les noms des colonnes. Pendant l'écriture,
« Insérer un champ » place par exemple «Prénom» dans le texte. « Aperçu » montre le document pour chaque
destinataire ; <strong>Fusionner</strong> crée un nouveau document avec une copie par ligne de la liste, prête à
imprimer ou à exporter.</p>"""),
        ("liens", "Liens hypertextes", f"""
<p>Sélectionnez un texte (ou un objet) puis <strong>Insertion ▸ Lien ▾ ▸ Ajouter un lien…</strong> : adresse web ou
adresse e-mail. Le texte lié est souligné en bleu ; {k('Ctrl')} + clic ouvre le lien. Les liens restent actifs dans
l'export web.</p>"""),
        ("web", "Exporter pour le web ou un e-mail", """
<p><strong>Fichier ▸ Exporter pour le web ou un e-mail…</strong> crée une page web autonome (un seul fichier .html,
images comprises) : à joindre à un courriel, à ouvrir dans un navigateur ou à déposer sur un site. Les liens y sont
cliquables et le texte des pages y figure aussi, pour les lecteurs d'écran et les moteurs de recherche.</p>"""),
        ("accessibilite", "Texte de remplacement", """
<p><strong>Outils image ▸ Texte de remplacement</strong> décrit une image en quelques mots ; ce texte est lu par les
lecteurs d'écran et repris dans l'export web.</p>"""),
    ]),

    ("revision.html", "Révision et orthographe", "Orthographe, rechercher et remplacer, typographie.", "🔎", [
        ("orthographe", "Orthographe", f"""
<p>Les mots inconnus du dictionnaire sont <strong>soulignés en rouge</strong> pendant l'écriture (à l'écran seulement,
jamais à l'impression). Un <strong>clic droit</strong> sur le mot propose des corrections, <strong>Ignorer tout</strong>
ou <strong>Ajouter au dictionnaire</strong>. <strong>Révision ▸ Orthographe</strong> ({k('F7')}) parcourt tout le
document, faute par faute.</p>
{shot("onglet-revision", "Onglet Révision du ruban")}
<p>OpenPub utilise le dictionnaire français de <strong>Windows</strong> (rien à installer) ; les mots ajoutés le sont
dans votre dictionnaire personnel Windows. La case <strong>Souligner les fautes</strong> désactive le soulignement.</p>"""),
        ("rechercher", "Rechercher et remplacer", f"""
<p>{k('Ctrl', 'F')} recherche dans tous les textes du document, {k('Ctrl', 'H')} remplace. Sans « Respecter la casse
et les accents », « ete » trouve aussi « Été ». « Mot entier » ignore les mots qui contiennent le texte cherché.
<strong>Remplacer tout</strong> s'annule en une seule fois avec {k('Ctrl', 'Z')}.</p>"""),
        ("typographie", "Typographie française automatique", """
<p>Pendant la frappe, OpenPub transforme les guillemets droits en « guillemets français », l'apostrophe droite en
apostrophe typographique (’) et met une espace insécable avant : ; ! ? (le signe ne se retrouve jamais seul en début
de ligne). La case <strong>Typographie française</strong> de l'onglet Révision désactive ces corrections.</p>"""),
        ("symboles", "Symboles, date et statistiques", """
<p><strong>Insertion ▸ Symbole ▾</strong> insère un caractère spécial (espace insécable, tirets, €, °, œ, É, ✓…) et
<strong>Date ▾</strong> la date du jour, à la position du curseur. <strong>Révision ▸ Statistiques</strong> compte
pages, zones, mots et caractères du document et de la zone sélectionnée.</p>"""),
    ]),

    ("pages.html", "Pages", "Ajouter, dupliquer, réordonner et supprimer des pages.", "📄", [
        ("gerer", "Gérer les pages", """
<p>Les boutons au-dessus des miniatures (panneau de gauche) agissent sur la <strong>page active</strong> :
<strong>＋</strong> ajoute une page vierge après elle, <strong>⧉</strong> la duplique, <strong>↑ ↓</strong> la
déplacent, <strong>🗑</strong> la supprime. Cliquez sur une miniature pour aller à la page.</p>"""),
        ("numero-suivant", "Préparer le numéro suivant", steps(
            "Ouvrez le numéro précédent puis <strong>Fichier ▸ Enregistrer sous…</strong> avec un nouveau nom.",
            "Remplacez les photos (double-clic ▸ Remplacer…) et réécrivez les articles directement sur la page.",
            "Dupliquez une page modèle si vous avez besoin de plus de pages.",
            "Imprimez ou créez le PDF (voir <a href=\"impression.html\">Impression et PDF</a>).")),
        ("mise-en-page", "Mise en page : format, marges, grille", """
<p><strong>Création de page ▸ Mise en page…</strong> règle le <strong>format</strong> (A4, A5, A3, carte postale,
Lettre US… ou dimensions libres), l'<strong>orientation</strong>, les <strong>marges</strong> et une
<strong>grille de disposition</strong> (colonnes et lignes en pointillés bleus, sur lesquelles le magnétisme
s'accroche). « Adapter les objets à la nouvelle taille » met les objets à l'échelle et les centre.</p>"""),
        ("maitres", "Pages maîtres", """
<p>Une <strong>page maître</strong> porte les objets communs à plusieurs pages : bandeau, logo, numéro de page,
fond. <strong>Création de page ▸ Pages maîtres</strong> passe en mode de modification des pages maîtres (bandeau violet
en haut) : ce que vous y placez s'affiche sous toutes les pages qui l'utilisent, sans pouvoir y être déplacé par
erreur. <strong>Nouvelle page maître</strong> en ajoute une autre (B, C…) ; <strong>Appliquer ▾</strong> choisit la
page maître de la page active ou de toutes les pages (par exemple A pour les pages impaires, B pour les paires),
ou aucune page maître pour une page.</p>"""),
        ("numeros", "Numéros de page, date automatique", """
<p><strong>Création de page ▸ Numéros de page ▾</strong> : « Ajouter les numéros en bas au centre » place en un clic
le numéro sur la page maître, donc sur toutes les pages. Pendant l'écriture, le même menu insère le
<strong>numéro de page</strong>, le <strong>nombre de pages</strong>, « Page X sur Y » ou la <strong>date du
jour</strong> : ces champs se mettent à jour seuls. « Format de la numérotation » choisit le style (1, i, I, a, A),
le premier numéro et un redémarrage de la numérotation sur une page.</p>"""),
        ("arriere-plan", "Arrière-plan", """
<p><strong>Création de page ▸ Arrière-plan ▾</strong> : couleur de la page active ou de toutes les pages, image de
fond couvrant la page, ou retrait de l'arrière-plan. Une couleur posée sur la page maître sert à toutes ses
pages.</p>"""),
        ("jeux", "Jeux de couleurs et de polices", """
<p><strong>Création de page ▸ Couleurs ▾</strong> change d'un coup les couleurs du document : chaque couleur du jeu
précédent (couleur principale et quatre accents) est remplacée partout par celle du nouveau jeu. « Personnaliser »
choisit vos cinq couleurs, qui apparaissent aussi en tête du sélecteur de couleur. <strong>Polices ▾</strong>
remplace la police des titres et celle du texte, styles compris.</p>"""),
    ]),

    ("impression.html", "Impression et PDF", "Imprimer, créer un PDF, préparer un fichier pour l'imprimeur, exporter en images.", "🖨️", [
        ("imprimer", "Imprimer", f"""
<p><strong>Fichier ▸ Imprimer…</strong> ({k('Ctrl', 'P')}) ouvre la boîte <strong>Imprimer</strong> d'OpenPub :</p>
<ul>
<li><strong>Pages</strong> : toutes, la page active, ou une liste comme <code>1-3; 5</code> (une plage ouverte
<code>4-</code> va jusqu'à la dernière page) ;</li>
<li><strong>Exemplaires</strong> : le document entier est imprimé, puis le suivant (exemplaires assemblés) ;</li>
<li><strong>Disposition</strong> : une page par feuille, recto verso manuel, ou livret (voir plus bas).</li>
</ul>
<p>Cliquez sur <strong>Imprimer…</strong> : la boîte de Windows s'ouvre pour choisir l'imprimante et ses réglages,
puis l'impression part.</p>"""),
        ("apercu", "Aperçu avant impression", """
<p>La partie droite de la boîte <strong>Imprimer</strong> montre les feuilles <strong>telles qu'elles sortiront</strong>,
dans l'ordre d'impression, avec une légende (« Feuille 1, recto — pages 8 et 1 », « Verso 2 — page 4 »…). L'aperçu
suit vos réglages : changez les pages, les exemplaires ou la disposition, il se met à jour.</p>"""),
        ("livret", "Imprimer un livret", f"""
<p>La disposition <strong>Livret</strong> imprime <strong>deux pages par face</strong> d'une feuille en paysage,
dans l'<strong>ordre de pliage</strong> : une fois les feuilles imprimées, empilées et pliées en deux, les pages se
suivent. Pour 8 pages : feuille 1 = 8-1 au recto et 2-7 au verso, feuille 2 = 6-3 et 4-5.</p>
<ul>
<li>Le nombre de pages est complété par des <strong>pages blanches</strong> jusqu'à un multiple de 4 (une feuille
pliée porte 4 pages).</li>
<li>Les pages sont réduites pour tenir à deux sur la feuille de l'imprimante : des pages A4 sur une feuille A4
donnent un livret A5 ; sur une feuille A3, le livret garde le format A4.</li>
<li><strong>Livret (imprimante recto verso)</strong> : l'imprimante imprime les deux faces, retournement sur le
<strong>bord court</strong>.</li>
<li><strong>Livret, recto verso manuel</strong> : pour une imprimante qui n'imprime qu'une face (voir ci-dessous).</li>
</ul>
{tip("Pour la newsletter de 4 pages : une seule feuille A3 pliée donne un livret A4 (4-1 au recto, 2-3 au verso).")}"""),
        ("recto-verso", "Recto verso manuel", f"""
<p>Si l'imprimante n'imprime qu'une face, choisissez <strong>Recto verso manuel</strong> (ou <strong>Livret, recto
verso manuel</strong>) :</p>
""" + steps(
            "OpenPub imprime d'abord tous les <strong>rectos</strong> (pages 1, 3, 5…).",
            "Un message vous demande de <strong>retourner la pile</strong> et de la remettre dans le bac.",
            "Cliquez sur <strong>OK</strong> : les <strong>versos</strong> s'impriment (pages 2, 4, 6…). Une page blanche "
            "est ajoutée au besoin pour que chaque feuille reçoive son verso.") + f"""
<p>Si les versos sortent dans le mauvais ordre (imprimantes qui empilent les feuilles face dessus), cochez
<strong>Versos dans l'ordre inverse</strong>. Faites un essai sur deux feuilles la première fois.</p>"""),
        ("pdf", "Exporter en PDF", f"""
<p><strong>Fichier ▸ Exporter en PDF…</strong> crée directement un fichier PDF, sans passer par une imprimante :</p>
<ul>
<li>le <strong>texte reste du texte</strong> (net à tous les zooms, sélectionnable, recherchable), avec les vraies
polices <strong>incorporées</strong> : le PDF s'affiche de la même façon sur tous les ordinateurs ;</li>
<li>les <strong>photos d'origine</strong> sont reprises telles quelles, sans perte de qualité ;</li>
<li>ombres floues et reflets sont convertis en images haute définition (300 points par pouce).</li>
</ul>
<p>Choisissez les pages (toutes, page active ou liste), puis l'emplacement du fichier. La case <strong>Ouvrir le PDF
une fois créé</strong> l'affiche dans votre lecteur de PDF.</p>
{tip("C'est le meilleur format pour envoyer la newsletter par courriel ou la publier sur un site.")}"""),
        ("imprimeur", "PDF pour l'imprimeur", f"""
<p>Pour une impression professionnelle, la boîte <strong>Exporter en PDF</strong> propose, sous <strong>Pour
l'imprimeur</strong> :</p>
<ul>
<li><strong>Fond perdu</strong> (3 mm par défaut) : la page du PDF est agrandie de chaque côté ; les fonds et les
photos qui <strong>touchent le bord</strong> de la page sont prolongés dans cette marge, qui sera coupée. Ainsi,
aucun liseré blanc n'apparaît si la coupe est un peu décalée. Les photos gardent leurs proportions (la partie
recadrée est d'abord dévoilée, sinon la photo est très légèrement agrandie). Votre document n'est pas modifié.</li>
<li><strong>Traits de coupe</strong> : de petits traits dans les coins indiquent où couper ; ils sont placés en
dehors du fond perdu.</li>
<li><strong>PDF/X-4</strong> : la norme des imprimeurs. Le profil d'impression CMJN est incorporé (FOGRA39 par
défaut, le plus courant en Europe ; <strong>Autre profil…</strong> pour celui fourni par l'imprimeur), les couleurs
sont étiquetées sRGB, les dimensions de coupe (TrimBox, BleedBox) sont indiquées sur chaque page.</li>
</ul>
{warn("OpenPub ne convertit pas lui-même les couleurs en CMJN : c'est l'imprimeur qui le fait à partir du profil incorporé. "
       "Demandez-lui un contrôle du fichier (« BAT ») avant une grosse impression.")}"""),
        ("png", "Exporter en images", """
<p><strong>Fichier ▸ Exporter en images (PNG, JPEG)…</strong> enregistre des pages en images :</p>
<ul>
<li><strong>Format</strong> : PNG (sans perte, idéal pour le texte) ou JPEG (fichiers plus légers, idéal pour les
photos et les réseaux sociaux) ;</li>
<li><strong>Résolution</strong> : 96 ppp pour l'écran, 150 à 200 ppp pour un courriel ou un site, 300 ppp pour
l'impression ;</li>
<li><strong>Pages</strong> : la page active (vous choisissez le nom du fichier), toutes les pages ou une liste (vous
choisissez un dossier ; les fichiers s'appellent <code>page-1.png</code>, <code>page-2.png</code>…).</li>
</ul>"""),
    ]),

    ("mises-a-jour.html", "Mises à jour", "Recevoir les nouvelles versions automatiquement.", "🔄", [
        ("automatiques", "Mises à jour automatiques", """
<p>Au démarrage, OpenPub vérifie discrètement s'il existe une nouvelle version. Si c'est le cas, un bandeau propose
<strong>Mettre à jour maintenant</strong>, <strong>Plus tard</strong> ou <strong>Ignorer cette version</strong>.
La mise à jour propose d'enregistrer votre document, s'installe sans droits administrateur, puis OpenPub se rouvre
sur votre document.</p>"""),
        ("manuel", "Vérifier ou désactiver", """
<p><strong>Fichier ▸ Rechercher des mises à jour…</strong> vérifie tout de suite. La case <strong>Rechercher
automatiquement au démarrage</strong> du menu Fichier active ou désactive la vérification.</p>"""),
        ("securite", "Sécurité", """
<p>Chaque version publiée est <strong>signée numériquement</strong> ; OpenPub vérifie la signature et l'empreinte du
fichier avant de l'installer, et refuse toute version modifiée.</p>"""),
    ]),

    ("raccourcis.html", "Raccourcis clavier", "Tous les raccourcis d'OpenPub.", "⌨️", [
        ("general", "Général", table([
            ("Nouveau / Ouvrir / Enregistrer / Enregistrer sous", f"{k('Ctrl', 'N')} · {k('Ctrl', 'O')} · {k('Ctrl', 'S')} · {k('Ctrl', 'Maj', 'S')}"),
            ("Rouvrir un document récent (n° du menu Fichier)", f"{k('Ctrl', 'Maj', '1')} … {k('Ctrl', 'Maj', '9')}"),
            ("Imprimer (aperçu, livret, recto verso)", k('Ctrl', 'P')),
            ("Annuler / Rétablir", f"{k('Ctrl', 'Z')} · {k('Ctrl', 'Y')}"),
            ("Aide sur ce que vous faites", k('F1')),
            ("Zoom", f"{k('Ctrl')} + molette"),
        ])),
        ("objets", "Objets", table([
            ("Supprimer l'objet sélectionné", k('Suppr')),
            ("Désélectionner, terminer, quitter un outil", k('Échap')),
            ("Déplacer de 1 pt / 10 pt", f"Flèches · {k('Maj')} + flèches"),
            ("Déplacer horizontalement ou verticalement", f"{k('Maj')} + glisser"),
            ("Redimensionner en gardant les proportions", f"{k('Maj')} + coin"),
            ("Tourner par pas de 15°", f"{k('Maj')} + poignée verte"),
            ("Placer sans magnétisme", f"{k('Alt')} + glisser"),
            ("Ajouter à la sélection", f"{k('Ctrl')} ou {k('Maj')} + clic"),
            ("Tous les objets de la page", k('Ctrl', 'A')),
            ("Copier / couper / coller des objets", f"{k('Ctrl', 'C')} · {k('Ctrl', 'X')} · {k('Ctrl', 'V')}"),
            ("Grouper / dissocier", f"{k('Ctrl', 'G')} · {k('Ctrl', 'Maj', 'G')}"),
        ])),
        ("revision", "Révision", table([
            ("Vérifier l'orthographe", k('F7')),
            ("Rechercher / remplacer", f"{k('Ctrl', 'F')} · {k('Ctrl', 'H')}"),
        ])),
        ("ecriture", "Écriture sur la page", table([
            ("Écrire dans un texte / terminer", f"Double-clic · {k('Échap')}"),
            ("Gras / italique / souligné", f"{k('Ctrl', 'B')} · {k('Ctrl', 'I')} · {k('Ctrl', 'U')}"),
            ("Tout sélectionner", k('Ctrl', 'A')),
            ("Copier / couper / coller", f"{k('Ctrl', 'C')} · {k('Ctrl', 'X')} · {k('Ctrl', 'V')}"),
            ("Nouveau paragraphe / saut de ligne", f"{k('Entrée')} · {k('Maj', 'Entrée')}"),
            ("Mot précédent / suivant", f"{k('Ctrl', '←')} · {k('Ctrl', '→')}"),
            ("Début / fin de ligne", f"{k('Origine')} · {k('Fin')}"),
            ("Début / fin du texte", f"{k('Ctrl', 'Origine')} · {k('Ctrl', 'Fin')}"),
            ("Effacer le mot précédent", k('Ctrl', 'Retour arrière')),
            ("Sélectionner un mot / un paragraphe", "Double-clic · triple-clic"),
            ("Liste : niveau inférieur / supérieur (en début d'élément)", f"{k('Tab')} · {k('Maj', 'Tab')}"),
            ("Tableau : cellule suivante / précédente", f"{k('Tab')} · {k('Maj', 'Tab')}"),
            ("Ouvrir un lien", f"{k('Ctrl')} + clic"),
            ("Barré, exposant, majuscules…", "Onglet Accueil ▸ Police"),
        ])),
        ("dessin", "Dessin", table([
            ("Carré, cercle, ligne à 45°", f"{k('Maj')} pendant le tracé"),
            ("Forme libre : terminer", f"Double-clic · {k('Entrée')}"),
            ("Forme libre : fermer", "Clic sur le premier point"),
            ("Forme libre : retirer le dernier point", k('Retour arrière')),
        ])),
    ]),

    ("faq.html", "Questions fréquentes", "Polices, fichiers Publisher, sauvegardes.", "💬", [
        ("polices", "« Polices manquantes » s'affiche en bas à droite", """
<p>Le document utilise une police absente de ce PC ; OpenPub la remplace par Arial. Installez la police (clic droit
sur le fichier .ttf ▸ Installer) puis rouvrez le document. Les polices non installées mais disponibles lors de
l'import sont <strong>embarquées</strong> dans le fichier .opub.</p>"""),
        ("pub", "Puis-je ouvrir un fichier .pub ?", """
<p>Les fichiers Publisher doivent d'abord être convertis au format .opub (conversion réalisée une fois pour toutes) ;
OpenPub enregistre ensuite dans son propre format, ouvert et documenté.</p>"""),
        ("texte-reduit", "Mon texte est devenu plus petit", """
<p>Le texte ne tenait plus dans ses zones : OpenPub l'a réduit automatiquement (voir
<a href="texte.html#habillage">Habillage et réduction automatique</a>). Agrandissez les zones, raccourcissez le texte
ou retirez des lignes vides pour retrouver la taille d'origine.</p>"""),
        ("sauvegarde", "Comment sauvegarder mes documents ?", """
<p>Un fichier .opub contient tout (textes, images, polices) : copiez-le simplement sur une clé USB ou un espace en
ligne. Gardez une copie de chaque numéro publié. OpenPub garde aussi la version précédente (« .bak ») et une copie de
secours pendant le travail (voir <a href="interface.html#recuperation">Récupérer un document</a>).</p>"""),
        ("annuler", "J'ai fait une erreur", f"""<p>{k('Ctrl', 'Z')} annule (plusieurs fois si besoin), {k('Ctrl', 'Y')} rétablit.</p>"""),
    ]),
]

# ------------------------------------------------------------------ gabarit

CSS = """
.docs{display:grid;grid-template-columns:260px minmax(0,1fr);gap:40px;padding:36px 0 70px}
.side{position:sticky;top:20px;align-self:start}
.search{width:100%;padding:11px 14px;border:1px solid #d8dfee;border-radius:14px;font:inherit;background:#fff}
.search:focus{outline:3px solid #06c7c066;border-color:#087aff}
.results{list-style:none;margin:8px 0 0;padding:0;max-height:340px;overflow:auto}
.results a{display:block;padding:8px 10px;border-radius:10px;font-size:14px}.results a:hover{background:#eff2ff}
.results small{display:block;color:#52617e;font-size:12px}
.toc{list-style:none;margin:18px 0 0;padding:0}.toc a{display:flex;gap:8px;padding:9px 12px;border-radius:12px;font-weight:600;font-size:14px;color:#2b3a5c}
.toc a:hover{background:#eff2ff}.toc a.active{background:#e9eaff;color:#4e32cf}
.toc .sub{list-style:none;padding:2px 0 6px 34px;margin:0}.toc .sub a{font-weight:500;padding:5px 10px;font-size:13px}
article.doc{min-width:0}
.doc h1{font:800 clamp(30px,3vw,42px)/1.15 'Plus Jakarta Sans',sans-serif;letter-spacing:-1.5px;margin:6px 0 8px}
.doc .intro{font-size:18px;color:#394b72;margin-bottom:26px}
.doc h2{font:800 24px/1.3 'Plus Jakarta Sans',sans-serif;letter-spacing:-.6px;margin:42px 0 12px;scroll-margin-top:20px}
.doc h2 a.anchor{opacity:0;margin-left:8px;color:#9935f4;font-size:18px}.doc h2:hover a.anchor{opacity:1}
.doc p,.doc li{font-size:16px;line-height:1.7;color:#24324f}.doc ul,.doc ol{padding-left:22px}
.doc a{color:#4e32cf;font-weight:600}.doc a:hover{text-decoration:underline}
kbd{display:inline-block;min-width:22px;padding:2px 7px;border:1px solid #c9d2e8;border-bottom-width:3px;border-radius:7px;background:#fff;font:600 13px/1.4 'DM Sans',sans-serif;color:#111c43;text-align:center}
code{background:#eef2fb;padding:2px 6px;border-radius:6px;font-size:14px}
.steps{counter-reset:s;list-style:none;padding:0}.steps li{counter-increment:s;position:relative;padding:4px 0 10px 46px}
.steps li:before{content:counter(s);position:absolute;left:0;top:4px;width:30px;height:30px;border-radius:50%;display:grid;place-items:center;color:#fff;font-weight:800;font-size:14px;background:linear-gradient(105deg,#027efb,#3551dc 55%,#aa36ef)}
.callout{border-radius:18px;padding:16px 20px;margin:20px 0;border:1px solid}.callout p{margin:4px 0 0}
.callout.tip{background:linear-gradient(125deg,#edffff,#f1f8ff);border-color:#bfeeee}.callout.tip strong{color:#05807c}
.callout.warn{background:#fff7ed;border-color:#ffd8a8}.callout.warn strong{color:#b45309}
figure.shot{margin:24px 0;background:rgba(255,255,255,.75);padding:10px;border:1px solid #fff;border-radius:22px;box-shadow:0 18px 50px rgba(52,88,171,.16)}
figure.shot img{width:100%;height:auto;border-radius:14px}figure.shot figcaption{text-align:center;color:#52617e;font-size:13px;padding:10px 6px 2px}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:14px}
.card{display:block;padding:18px 18px;border-radius:20px;background:linear-gradient(125deg,#f8f5ff,#eef4ff);border:1px solid #ffffffb3;box-shadow:0 8px 22px rgba(52,78,143,.08);transition:transform .15s}
.card:hover{transform:translateY(-2px);text-decoration:none!important}.card b{display:block;font:800 17px 'Plus Jakarta Sans',sans-serif;color:#111c43;margin-bottom:4px}.card span{color:#52617e;font-size:14px;font-weight:500}
.table-wrap{overflow-x:auto}table{border-collapse:collapse;width:100%;background:#fff;border-radius:16px;overflow:hidden;box-shadow:0 6px 18px rgba(52,78,143,.07)}
th,td{text-align:left;padding:11px 14px;border-bottom:1px solid #eef1f8;font-size:15px}th{background:#f3f6ff;font-weight:700;color:#2b3a5c}
.pager{display:flex;justify-content:space-between;gap:12px;margin-top:50px;padding-top:22px;border-top:1px solid #e2e8f4}
.pager a{padding:12px 16px;border-radius:14px;background:#fff;border:1px solid #e2e8f4;font-weight:700}
.doc-hero{background:linear-gradient(115deg,#fcfdff 0%,#eef7ff 58%,#f4edff 100%);border-bottom:1px solid rgba(42,77,139,.10)}
.doc-hero .container{padding:26px 0}.crumbs{color:#52617e;font-size:14px}.crumbs a{color:#4e32cf;font-weight:600}
@media(max-width:900px){.docs{grid-template-columns:1fr;gap:20px}.side{position:static}}
"""

SEARCH_JS = """
(function(){var box=document.getElementById('q'),list=document.getElementById('results');if(!box)return;
function norm(s){return s.toLowerCase().normalize('NFD').replace(/[\\u0300-\\u036f]/g,'')}
box.addEventListener('input',function(){var q=norm(box.value.trim());list.innerHTML='';if(q.length<2)return;
var words=q.split(/\\s+/),hits=INDEX.filter(function(e){var t=norm(e.t+' '+e.x);return words.every(function(w){return t.indexOf(w)>=0})}).slice(0,12);
if(!hits.length){list.innerHTML='<li><a>Aucun résultat</a></li>';return}
hits.forEach(function(h){var li=document.createElement('li'),a=document.createElement('a');a.href=h.u;a.innerHTML=h.t+'<small>'+h.p+'</small>';li.appendChild(a);list.appendChild(li)})});
box.addEventListener('keydown',function(e){if(e.key==='Enter'){var a=list.querySelector('a[href]');if(a)location.href=a.href}});})();
"""

def layout(page_idx):
    fname, title, desc, icon, sections = PAGES[page_idx]
    toc = []
    for i, (f, t, _, ic, secs) in enumerate(PAGES):
        active = ' class="active"' if i == page_idx else ''
        sub = ""
        if i == page_idx:
            sub = '<ul class="sub">' + "".join(f'<li><a href="#{a}">{st}</a></li>' for a, st, _ in secs) + "</ul>"
        toc.append(f'<li><a href="{f}"{active}><span>{ic}</span>{t}</a>{sub}</li>')
    body = "".join(
        f'<h2 id="{a}">{st}<a class="anchor" href="#{a}" aria-label="Lien vers cette section">#</a></h2>{h}'
        for a, st, h in sections)
    prev_link = f'<a href="{PAGES[page_idx-1][0]}">← {PAGES[page_idx-1][1]}</a>' if page_idx > 0 else "<span></span>"
    next_link = f'<a href="{PAGES[page_idx+1][0]}">{PAGES[page_idx+1][1]} →</a>' if page_idx < len(PAGES) - 1 else "<span></span>"
    ver = f" · OpenPub {html.escape(VERSION)}" if VERSION else ""
    return f"""<!doctype html>
<html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{title} — Aide OpenPub</title><meta name="description" content="{html.escape(desc)}">
<link rel="icon" href="../assets/openpub.ico" sizes="any"><link rel="stylesheet" href="../styles.css"><link rel="stylesheet" href="aide.css"></head><body>
<header class="site-header"><div class="container nav"><a class="brand" href="../index.html" aria-label="OpenPub, accueil"><img src="../assets/openpub-128.png" alt="">OpenPub</a>
<nav class="nav-links" aria-label="Navigation principale"><a href="../index.html">Accueil</a><a href="../fonctionnalites.html">Fonctionnalités</a><a class="active" href="index.html" aria-current="page">Aide</a></nav></div></header>
<div class="doc-hero"><div class="container"><div class="crumbs"><a href="index.html">Aide</a>{' · ' + html.escape(title) if page_idx else ''}{ver}</div></div></div>
<main class="container docs"><aside class="side"><input id="q" class="search" type="search" placeholder="Rechercher dans l'aide…" aria-label="Rechercher dans l'aide" autocomplete="off">
<ul id="results" class="results"></ul><ul class="toc">{''.join(toc)}</ul></aside>
<article class="doc"><h1>{title}</h1><p class="intro">{html.escape(desc)}</p>{body}<nav class="pager">{prev_link}{next_link}</nav></article></main>
<footer class="footer"><div class="container footer-inner"><span>© OpenPub · Aide en ligne</span><span><a href="../index.html#telecharger">Télécharger OpenPub</a> · <a href="../fonctionnalites.html">Fonctionnalités</a></span></div></footer>
<script src="recherche.js"></script></body></html>
"""


def main():
    out = os.path.join(OUT, "aide")
    os.makedirs(os.path.join(out, "img"), exist_ok=True)
    # capture de l'onglet Format de la forme si disponible
    for i, page in enumerate(PAGES):
        secs = [(a, t, h.replace("{SHOT_FORMAT}", shot("onglet-forme", "Onglet Format de la forme"))) for a, t, h in page[4]]
        PAGES[i] = page[:4] + (secs,)
    index = []
    for i, (fname, title, desc, icon, sections) in enumerate(PAGES):
        open(os.path.join(out, fname), "w", encoding="utf-8").write(layout(i))
        for a, st, h in sections:
            text = re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", h))).strip()
            index.append({"t": st, "p": title, "u": f"{fname}#{a}", "x": text[:600]})
    open(os.path.join(out, "aide.css"), "w", encoding="utf-8").write(CSS.strip() + "\n")
    open(os.path.join(out, "recherche.js"), "w", encoding="utf-8").write(
        "var INDEX=" + json.dumps(index, ensure_ascii=False) + ";\n" + SEARCH_JS.strip() + "\n")
    # verification : toutes les ancres utilisees par l'application existent
    anchors = {f"{f}#{a}" for f, _, _, _, s in PAGES for a, _, _ in s} | {f for f, *_ in PAGES}
    print(f"{len(PAGES)} pages, {len(index)} sections -> {out}")
    return anchors


if __name__ == "__main__":
    anchors = main()
    # liste des cibles valides (lue par le test des liens contextuels)
    open(os.path.join(OUT, "aide", "ancres.txt"), "w", encoding="utf-8").write("\n".join(sorted(anchors)) + "\n")
