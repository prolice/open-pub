"""Convertit un .pub en format ouvert OpenPub (document.json + assets/) sans Publisher.

S'appuie sur libmspub (outil pub2raw) pour la lecture du format, puis transforme
sa sortie texte en modele OpenPub (unites : points).

Usage : python3 -I pub2json.py fichier.pub dossier_sortie [--pub2raw CHEMIN]
"""
import base64
import hashlib
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_ROOT = os.path.join(HERE, '..', 'work', 'ref', 'debs', 'root')
# Calibrations mesurees sur les apercus Publisher de la newsletter (voir PLAN.md) :
# - interligne simple de Cavolini (police cloud d'Office) = valeur "typo" de la police (1,07 x taille),
#   au lieu de winAscent+winDescent (1,286) ;
# - les paragraphes Cavolini sans reglage explicite heritent d'un style (105 %, 4,8 pt apres)
#   que libmspub n'exporte pas.
CALIBRATED_LINE_FACTORS = {'Cavolini': 1.07}
SHAPE_TEXT_MARGIN = 11.5
# 1re ligne de base a 0,738 em (ascendante typo) sous le haut de la zone, l'interligne en plus allant
# sous la ligne : avec l'interligne 105 %, cela donne une part "au-dessus de la base" de 0,64.
CALIBRATED_ASCENTS = {'Cavolini': 0.64}
INHERITED_PARA_STYLE = {'Cavolini': {'lineHeight': '105.0000%', 'spaceAfter': 4.8}}
MIME_EXT = {'image/jpeg': 'jpg', 'image/png': 'png', 'image/gif': 'gif', 'image/bmp': 'bmp',
            'image/tiff': 'tif', 'image/x-wmf': 'wmf', 'image/x-emf': 'emf', 'image/wmf': 'wmf', 'image/emf': 'emf'}


# ---------------------------------------------------------------- lecture pub2raw

def run_pub2raw(pub, pub2raw=None):
    if pub2raw is None:
        pub2raw = os.path.join(DEFAULT_ROOT, 'usr', 'bin', 'pub2raw')
        env = dict(os.environ, LD_LIBRARY_PATH=os.path.join(DEFAULT_ROOT, 'usr', 'lib', 'x86_64-linux-gnu'))
    else:
        env = None
    out = subprocess.run([pub2raw, pub], capture_output=True, env=env, check=True)
    return out.stdout.decode('utf-8', 'replace')


def split_top(s, sep=', '):
    """Decoupe s sur sep, hors parentheses."""
    parts, depth, cur, i = [], 0, [], 0
    while i < len(s):
        c = s[i]
        if c == '(':
            depth += 1
        elif c == ')':
            depth -= 1
        if depth == 0 and s.startswith(sep, i):
            parts.append(''.join(cur))
            cur = []
            i += len(sep)
            continue
        cur.append(c)
        i += 1
    if cur:
        parts.append(''.join(cur))
    return parts


def parse_props(s):
    s = s.strip()
    if s.startswith('(') and s.endswith(')'):
        s = s[1:-1]
    props = {}
    for part in split_top(s):
        if ': ' not in part:
            continue
        k, v = part.split(': ', 1)
        v = v.strip()
        props[k.strip()] = [parse_props(x) for x in split_top(v[1:-1])] if v.startswith('((') else v
    return props


def parse_line(line):
    body = line.strip()
    m = re.match(r'([A-Za-z]+)\s*(\((.*)\))?$', body, re.S)
    if not m:
        return body, None
    return m.group(1), m.group(3)


# ---------------------------------------------------------------- utilitaires

def inch(v):
    """'1.2345in' -> points"""
    if v is None:
        return None
    m = re.match(r'(-?[\d.]+)(in|pt|cm|mm)?', str(v))
    if not m:
        return None
    n = float(m.group(1))
    return round(n * {'in': 72, 'pt': 1, 'cm': 72 / 2.54, 'mm': 72 / 25.4, None: 72}[m.group(2)], 3)


def bbox(points):
    xs = [inch(p['svg:x']) for p in points]
    ys = [inch(p['svg:y']) for p in points]
    return min(xs), min(ys), max(xs) - min(xs), max(ys) - min(ys)


def style_to_fill_line(st):
    fill = None
    if st.get('draw:fill') == 'solid':
        fill = {'color': st.get('draw:fill-color'), 'opacity': float(st.get('draw:opacity', '1').rstrip('%')) / (100 if st.get('draw:opacity', '').endswith('%') else 1)}
    elif st.get('draw:fill') == 'gradient':
        fill = {'gradient': True, 'color': st.get('draw:fill-color')}
    line = None
    if st.get('draw:stroke') not in (None, 'none'):
        line = {'color': st.get('svg:stroke-color', '#000000'), 'weight': inch(st.get('svg:stroke-width', '0.0139in')),
                'dash': st.get('draw:stroke') == 'dash'}
    return fill, line


# ---------------------------------------------------------------- conversion

def convert(pub, out_dir, pub2raw=None):
    raw = run_pub2raw(pub, pub2raw)
    assets_dir = os.path.join(out_dir, 'assets')
    os.makedirs(assets_dir, exist_ok=True)

    doc = {'format': 'openpub/1', 'source': os.path.basename(pub), 'unit': 'pt', 'reader': 'libmspub',
           'meta': {}, 'pageWidth': None, 'pageHeight': None, 'pages': []}
    page = None
    style = {}
    text = None          # zone de texte en cours
    para = None
    span = None
    z = 0
    images = {}
    stats = {'text': 0, 'image': 0, 'shape': 0, 'chars': 0}

    layers = []          # pile des groupes ouverts (startLayer)
    layer_count = 0

    def add(shape):
        nonlocal z
        z += 1
        shape['z'] = z
        if layers:
            shape['group'] = layers[-1]
        page['shapes'].append(shape)

    # split('\n') et non splitlines() : le texte contient des \r (fins de paragraphe Quill)
    for line in raw.split('\n'):
        name, args = parse_line(line)
        if name == 'insertText':
            if span is not None:
                t = line.strip()[len('insertText ('):-1].replace('\r', '')
                span['text'] += t
                stats['chars'] += len(t)
            continue
        if name == 'insertSpace':
            if span is not None:
                span['text'] += ' '
            continue
        if name == 'insertTab':
            if span is not None:
                span['text'] += '\t'
            continue
        if name == 'insertLineBreak':
            if span is not None:
                span['text'] += '\n'
            continue

        p = parse_props(args) if args else {}
        if name == 'setDocumentMetaData':
            doc['meta'] = p
        elif name == 'startPage':
            w, h = inch(p.get('svg:width')), inch(p.get('svg:height'))
            doc['pageWidth'] = doc['pageWidth'] or w
            doc['pageHeight'] = doc['pageHeight'] or h
            page = {'index': len(doc['pages']) + 1, 'width': w, 'height': h, 'shapes': []}
            doc['pages'].append(page)
            z = 0
        elif name == 'startLayer':
            layer_count += 1
            layers.append(f'g{layer_count}')
        elif name == 'endLayer':
            if layers:
                layers.pop()
        elif name == 'setStyle':
            style = p
        elif name in ('drawPolygon', 'drawRectangle', 'drawEllipse', 'drawPath', 'drawPolyline'):
            if name == 'drawPath':
                d = p.get('svg:d', [])
                pts = [q for q in d if 'svg:x' in q]
                if not pts:
                    continue
                # les arcs (A) debordent de leurs extremites : on elargit la boite de rx/ry
                ext = list(pts)
                for q in d:
                    if q.get('librevenge:path-action') == 'A':
                        rx, ry = q.get('svg:rx', '0in'), q.get('svg:ry', '0in')
                        for dx, dy in ((rx, '0in'), ('-' + rx, '0in'), ('0in', ry), ('0in', '-' + ry)):
                            ext.append({'svg:x': f"{inch(q['svg:x']) / 72 + inch(dx) / 72}in",
                                        'svg:y': f"{inch(q['svg:y']) / 72 + inch(dy) / 72}in"})
                x, y, w, h = bbox(ext)
                path = []
                for q in d:
                    act = q.get('librevenge:path-action')
                    seg = {'a': act}
                    for k in ('svg:x', 'svg:y', 'svg:x1', 'svg:y1', 'svg:x2', 'svg:y2', 'svg:rx', 'svg:ry'):
                        if k in q:
                            seg[k.split(':')[1]] = inch(q[k])
                    if act == 'A':
                        seg['rot'] = float(str(q.get('librevenge:rotate', '0')).rstrip('in').rstrip('deg') or 0)
                        seg['large'] = int(q.get('librevenge:large-arc', 0) in ('1', 'true'))
                        seg['sweep'] = int(q.get('librevenge:sweep', 0) in ('1', 'true'))
                    path.append(seg)
            elif name in ('drawPolygon', 'drawPolyline'):
                pts = p.get('svg:points', [])
                if not pts:
                    continue
                x, y, w, h = bbox(pts)
                path = [{'a': 'M' if i == 0 else 'L', 'x': inch(q['svg:x']), 'y': inch(q['svg:y'])} for i, q in enumerate(pts)]
                if name == 'drawPolygon' and len(pts) > 2:   # un "polygone" de 2 points est un filet
                    path.append({'a': 'Z'})
            else:
                x, y = inch(p.get('svg:x')), inch(p.get('svg:y'))
                w, h = inch(p.get('svg:width')), inch(p.get('svg:height'))
                path = None
            if style.get('draw:fill') == 'bitmap' and style.get('draw:fill-image'):
                data = base64.b64decode(style['draw:fill-image'])
                key = hashlib.sha1(data).hexdigest()[:12]
                if key not in images:
                    ext = MIME_EXT.get(style.get('librevenge:mime-type', ''), 'bin')
                    fname = f'img-{len(images) + 1:02d}-{key}.{ext}'
                    open(os.path.join(assets_dir, fname), 'wb').write(data)
                    images[key] = 'assets/' + fname
                # Un fond d'image couvrant toute la page = arriere-plan
                is_bg = page and abs(w - page['width']) < 1 and abs(h - page['height']) < 1
                _, line_ = style_to_fill_line(style)
                add({'kind': 'image', 'x': x, 'y': y, 'w': w, 'h': h, 'src': images[key],
                     'mime': style.get('librevenge:mime-type'), 'background': bool(is_bg), 'line': line_,
                     'tile': style.get('style:repeat') == 'repeat', 'clip': path if path and len(path) > 6 else None})
                stats['image'] += 1
            else:
                fill, line_ = style_to_fill_line(style)
                if not fill and not line_:
                    continue
                axis = path and len(path) == 6 and len({round(q['x'], 1) for q in path[:4]}) == 2 \
                    and len({round(q['y'], 1) for q in path[:4]}) == 2
                kind = 'rect' if axis and name == 'drawPolygon' else ('ellipse' if name == 'drawEllipse' else 'path')
                shape = {'kind': kind, 'x': x, 'y': y, 'w': w, 'h': h, 'fill': fill, 'line': line_}
                if kind == 'path':
                    shape['path'] = path
                add(shape)
                stats['shape'] += 1
        elif name == 'startTextObject':
            text = {'kind': 'text', 'x': inch(p.get('svg:x')), 'y': inch(p.get('svg:y')),
                    'w': inch(p.get('svg:width')), 'h': inch(p.get('svg:height')),
                    'rotation': float(p['librevenge:rotate']) if 'librevenge:rotate' in p else 0,
                    'padding': [inch(p.get('fo:padding-top', 0)), inch(p.get('fo:padding-right', 0)),
                                inch(p.get('fo:padding-bottom', 0)), inch(p.get('fo:padding-left', 0))],
                    'columns': int(p.get('fo:column-count', 1)), 'columnGap': inch(p.get('fo:column-gap', 0)),
                    'vAlign': p.get('draw:textarea-vertical-align', 'top'), 'paragraphs': []}
            # libmspub dessine deja fond et contour des zones (polygone emis juste avant) :
            # la zone elle-meme n'en porte pas, sinon elle herite du style de la forme precedente.
            text['fill'], text['line'] = None, None
        elif name == 'endTextObject':
            if text is not None:
                add(text)
                stats['text'] += 1
            text = None
        elif name == 'openParagraph' and text is not None:
            para = {'align': p.get('fo:text-align', 'left'), 'lineHeight': p.get('fo:line-height'),
                    'spaceBefore': inch(p.get('fo:margin-top')), 'spaceAfter': inch(p.get('fo:margin-bottom')),
                    'indentLeft': inch(p.get('fo:margin-left')), 'indentRight': inch(p.get('fo:margin-right')),
                    'firstIndent': inch(p.get('fo:text-indent')), 'runs': []}
        elif name == 'closeParagraph' and text is not None and para is not None:
            text['paragraphs'].append(para)
            para = None
        elif name == 'openSpan' and para is not None:
            span = {'text': '', 'font': {
                'family': p.get('style:font-name'), 'size': inch(p.get('fo:font-size')),
                'bold': p.get('fo:font-weight') == 'bold', 'italic': p.get('fo:font-style') == 'italic',
                'underline': p.get('style:text-underline-type', 'none') != 'none', 'color': p.get('fo:color'),
                'lang': p.get('fo:language')}}
        elif name == 'closeSpan' and para is not None and span is not None:
            para['runs'].append(span)
            span = None

    # Interligne simple de Publisher pour les polices cloud d'Office : mesure sur les apercus
    # Publisher de la newsletter (Cavolini 12 pt -> 13,44 pt par ligne, au lieu de 15,43 pt
    # avec la valeur standard winAscent+winDescent). Les autres polices suivent la regle standard.
    fams = {r['font']['family'] for p_ in doc['pages'] for sh in p_['shapes'] if sh['kind'] == 'text'
            for pa in sh['paragraphs'] for r in pa['runs'] if r.get('font')}
    calib = {k: v for k, v in CALIBRATED_LINE_FACTORS.items() if k in fams}
    if calib:
        doc['fontLineFactors'] = calib
        doc['fontAscents'] = {k: v for k, v in CALIBRATED_ASCENTS.items() if k in fams}
    for p_ in doc['pages']:
        for sh in p_['shapes']:
            if sh['kind'] != 'text':
                continue
            for pa in sh['paragraphs']:
                fs = {(r.get('font') or {}).get('family') for r in pa['runs']}
                sizes = [(r.get('font') or {}).get('size') or 0 for r in pa['runs']]
                if len(fs) == 1 and (fam := next(iter(fs))) in INHERITED_PARA_STYLE and max(sizes or [0]) <= 14 \
                        and pa.get('lineHeight') is None and pa.get('spaceAfter') is None and pa.get('spaceBefore') is None:
                    pa.update(INHERITED_PARA_STYLE[fam])
    # Texte loge dans un rectangle a coins arrondis (zone de meme cadre qu'une forme de son groupe) :
    # Publisher y applique une marge interieure que libmspub n'exporte pas (mesuree : ~11,5 pt).
    for p_ in doc['pages']:
        for sh in p_['shapes']:
            if sh['kind'] != 'text' or not sh.get('group'):
                continue
            for o in p_['shapes']:
                # rectangle a coins arrondis (trace avec des courbes) : seul cas mesure
                if o is not sh and o.get('group') == sh['group'] and o['kind'] == 'path' \
                        and any(seg.get('a') == 'C' for seg in o.get('path') or []) \
                        and all(abs(o[k] - sh[k]) < 1 for k in ('x', 'y', 'w', 'h')):
                    sh['padding'] = [SHAPE_TEXT_MARGIN] * 4
                    break
    stats['rotated'] = infer_text_rotation(doc)
    stats['chains'] = detect_chains(doc)
    stats['chains'] += link_split_stories(doc)
    with open(os.path.join(out_dir, 'document.json'), 'w', encoding='utf-8') as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    return doc, stats


def story_text(shape):
    return '\n'.join(''.join(r['text'] for r in pa['runs']) for pa in shape['paragraphs'])


def detect_chains(doc):
    """libmspub recopie parfois toute l'histoire dans chaque zone d'une chaine
    (zones liees non encore mises en page). On regroupe les zones d'une meme page
    au contenu identique, alignees en haut et de meme hauteur : le texte reste dans
    la premiere (ordre gauche->droite), les suivantes deviennent des suites ('flowFrom')."""
    n = 0
    for page in doc['pages']:
        texts = [sh for sh in page['shapes'] if sh['kind'] == 'text' and story_text(sh).strip()]
        groups = []
        for sh in texts:
            for g in groups:
                ref = g[0]
                if (story_text(ref) == story_text(sh) and abs(ref['y'] - sh['y']) < 2
                        and abs(ref['h'] - sh['h']) < 2):
                    g.append(sh)
                    break
            else:
                groups.append([sh])
        for g in groups:
            if len(g) < 2:
                continue
            n += 1
            g.sort(key=lambda sh: sh['x'])
            chain_id = f"chain-{page['index']}-{n}"
            for i, sh in enumerate(g):
                sh['chain'] = chain_id
                sh['chainIndex'] = i
                if i:
                    sh['paragraphs'] = []
                    sh['flowFrom'] = g[i - 1]['z']
    return n


def infer_text_rotation(doc):
    """libmspub donne la zone de texte d'une forme inclinee non tournee (bonnes dimensions, bon centre)
    mais sans son angle. On le retrouve sur le quadrilatere du meme groupe : meme centre, memes cotes."""
    import math
    n = 0
    for page in doc['pages']:
        for sh in page['shapes']:
            if sh['kind'] != 'text' or not sh.get('group') or sh.get('rotation'):
                continue
            for o in page['shapes']:
                if o.get('group') != sh['group'] or o['kind'] != 'path' or not o.get('path'):
                    continue
                pts = [(q['x'], q['y']) for q in o['path'] if q['a'] in ('M', 'L')]
                if len(pts) == 5 and abs(pts[0][0] - pts[4][0]) < 0.5 and abs(pts[0][1] - pts[4][1]) < 0.5:
                    pts = pts[:4]
                if len(pts) != 4:
                    continue
                w = math.dist(pts[0], pts[1]); h = math.dist(pts[1], pts[2])
                cx = sum(p[0] for p in pts) / 4; cy = sum(p[1] for p in pts) / 4
                ang = math.degrees(math.atan2(pts[1][1] - pts[0][1], pts[1][0] - pts[0][0]))
                if abs(ang) < 0.3 or abs(ang) > 45:
                    continue
                if abs(cx - (sh['x'] + sh['w'] / 2)) < 3 and abs(cy - (sh['y'] + sh['h'] / 2)) < 3 \
                        and abs(w - sh['w']) < 0.08 * w and abs(h - sh['h']) < 0.15 * h:
                    sh['rotation'] = round(ang, 2)
                    n += 1
                    break
    return n


def link_split_stories(doc):
    """Publisher a deja reparti certaines histoires entre des zones liees (ex. page 1 :
    "...a Viel-" | "salm. Une victoire..."), mais libmspub les rend comme textes separes.
    On relie A -> B (B a droite de A, meme haut, meme largeur, meme police) quand A
    s'interrompt en cours de phrase (tiret de cesure, minuscule, virgule) et que B commence
    par une minuscule ; les textes sont fusionnes en une seule histoire chainee."""
    def last_text(sh):
        for pa in reversed(sh['paragraphs']):
            t = ''.join(r['text'] for r in pa['runs']).rstrip()
            if t:
                return t
        return ''

    def first_text(sh):
        for pa in sh['paragraphs']:
            t = ''.join(r['text'] for r in pa['runs']).lstrip()
            if t:
                return t
        return ''

    def font(sh):
        return next(((r['font'] or {}).get('family') for pa in sh['paragraphs'] for r in pa['runs'] if r['text'].strip()), None)

    n = 0
    for page in doc['pages']:
        frames = [sh for sh in page['shapes'] if sh['kind'] == 'text' and 'chain' not in sh and first_text(sh)]
        succ = {}
        for a in frames:
            end = last_text(a)
            if not end or not (end.endswith('-') or end[-1].islower() or end[-1] == ','):
                continue
            cands = [b for b in frames if b is not a and b['x'] > a['x'] and abs(b['y'] - a['y']) < 4
                     and abs(b['w'] - a['w']) < 4 and font(b) == font(a) and first_text(b)[:1].islower()]
            if cands:
                succ[id(a)] = min(cands, key=lambda b: b['x'])
        targets = {id(b) for b in succ.values()}
        for a in frames:
            if id(a) not in succ or id(a) in targets:
                continue            # on part de la tete de chaque chaine
            chain = [a]
            while id(chain[-1]) in succ and len(chain) < 20:
                chain.append(succ[id(chain[-1])])
            n += 1
            chain_id = f"story-{page['index']}-{n}"
            head = chain[0]
            paras = [pa for pa in head['paragraphs']]
            for nxt in chain[1:]:
                # retire les paragraphes vides de fin, puis recolle le mot coupe ou la phrase
                while paras and not ''.join(r['text'] for r in paras[-1]['runs']).strip():
                    paras.pop()
                np_ = [pa for pa in nxt['paragraphs']]
                while np_ and not ''.join(r['text'] for r in np_[0]['runs']).strip():
                    np_.pop(0)
                if paras and np_:
                    lastp, firstp = paras[-1], np_.pop(0)
                    runs = lastp['runs']
                    while runs and not runs[-1]['text'].strip():
                        runs.pop()
                    if runs:
                        runs[-1]['text'] = runs[-1]['text'].rstrip()
                        # Mot coupe par Publisher entre deux zones : libmspub met parfois le mot
                        # entier dans la 1re zone ("malheureusement") ET sa fin dans la 2e
                        # ("heureusement defaite") : on retire ce chevauchement.
                        last_word = re.findall(r"[^\W\d_]+$", runs[-1]['text'])
                        first = firstp['runs'][0]['text'] if firstp['runs'] else ''
                        first_word = re.match(r"[^\W\d_]+", first)
                        if last_word and first_word and len(first_word.group(0)) < len(last_word[0]) \
                                and last_word[0].endswith(first_word.group(0)):
                            firstp['runs'][0]['text'] = first[first_word.end():].lstrip()
                            runs[-1]['text'] += ' '
                        elif runs[-1]['text'].endswith('-'):
                            runs[-1]['text'] = runs[-1]['text'][:-1]       # "Viel-" + "salm"
                        else:
                            runs[-1]['text'] += ' '
                    lastp['runs'] = runs + firstp['runs']
                paras += np_
            for i, sh in enumerate(chain):
                sh['chain'] = chain_id
                sh['chainIndex'] = i
                if i:
                    sh['paragraphs'] = []
                    sh['flowFrom'] = chain[i - 1]['z']
            head['paragraphs'] = paras
    return n


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    pub2raw = sys.argv[sys.argv.index('--pub2raw') + 1] if '--pub2raw' in sys.argv else None
    doc, stats = convert(sys.argv[1], sys.argv[2], pub2raw)
    print(f"{len(doc['pages'])} pages, {stats['text']} zones de texte ({stats['chars']} caracteres), "
          f"{stats['image']} images, {stats['shape']} formes, {stats['chains']} chaines de zones, "
          f"{stats['rotated']} zones inclinees -> {sys.argv[2]}")


if __name__ == '__main__':
    main()
