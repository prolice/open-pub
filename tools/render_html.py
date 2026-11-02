"""Genere un apercu HTML autonome d'un document OpenPub (document.json).

Usage : python3 -I render_html.py dossier_converti   -> dossier/preview.html
Le texte des chaines de zones (chain/flowFrom) s'ecoule d'une zone a la suivante.
"""
import json
import os
import re
import subprocess
import sys

PAGE_JS = r"""
const doc = DOC;
const PX = 96 / 72; // points -> pixels CSS
const root = document.getElementById('pages');
const imgSrc = s => s && /\.(wmf|emf)$/i.test(s) ? s.replace(/\.(wmf|emf)$/i, '.png') : s;

function el(tag, css, parent) { const e = document.createElement(tag); if (css) Object.assign(e.style, css); if (parent) parent.appendChild(e); return e; }
function box(s) { return { left: s.x * PX + 'px', top: s.y * PX + 'px', width: s.w * PX + 'px', height: s.h * PX + 'px', position: 'absolute' }; }
function lineCss(l) { return l ? `${Math.max(l.weight || 1, 0.5) * PX}px ${l.dash ? 'dashed' : 'solid'} ${l.color}` : 'none'; }

function svgPath(s) {
  return s.path.map(p => {
    const X = v => ((v - s.x) * PX).toFixed(2), Y = v => ((v - s.y) * PX).toFixed(2);
    switch (p.a) {
      case 'M': case 'L': return `${p.a}${X(p.x)},${Y(p.y)}`;
      case 'C': return `C${X(p.x1)},${Y(p.y1)} ${X(p.x2)},${Y(p.y2)} ${X(p.x)},${Y(p.y)}`;
      case 'Q': return `Q${X(p.x1)},${Y(p.y1)} ${X(p.x)},${Y(p.y)}`;
      case 'A': return `A${(p.rx * PX).toFixed(2)},${(p.ry * PX).toFixed(2)} ${p.rot || 0} ${p.large || 0} ${p.sweep || 0} ${X(p.x)},${Y(p.y)}`;
      case 'Z': return 'Z';
      default: return p.x !== undefined ? `L${X(p.x)},${Y(p.y)}` : '';
    }
  }).join(' ');
}

const METRICS_BY_FONT = __METRICS__;
let SCALE = 1; // reduction "texte reduit en cas de debordement" de la zone en cours
function lineHeightPx(p) {
  const runs = p.runs.length ? p.runs : [{ font: {} }];
  let h = 0;
  for (const r of runs) {
    const f = r.font || {}, size = f.size || 10;
    h = Math.max(h, size * (METRICS_BY_FONT[f.family] || 1.17));
  }
  const factor = p.lineHeight && p.lineHeight.endsWith('%') ? parseFloat(p.lineHeight) / 100 : 1;
  return (h * factor * SCALE * PX).toFixed(2);
}
function paraEl(p) {
  const e = document.createElement('p');
  e.className = 'tp';
  Object.assign(e.style, {
    margin: 0, textAlign: p.align === 'end' ? 'right' : (p.align || 'left'),
    lineHeight: lineHeightPx(p) + 'px',
    paddingTop: (p.spaceBefore || 0) * PX + 'px', paddingBottom: (p.spaceAfter || 0) * PX + 'px',
    paddingLeft: (p.indentLeft || 0) * PX + 'px', paddingRight: (p.indentRight || 0) * PX + 'px',
    textIndent: (p.firstIndent || 0) * PX + 'px', whiteSpace: 'pre-wrap', minHeight: '1em'
  });
  return e;
}
function runEl(r, text) {
  const e = document.createElement('span');
  const f = r.font || {};
  Object.assign(e.style, {
    fontFamily: `"${f.family || 'Arial'}", Arial, sans-serif`, fontSize: (f.size || 10) * SCALE * PX + 'px',
    fontWeight: f.bold ? 'bold' : 'normal', fontStyle: f.italic ? 'italic' : 'normal',
    textDecoration: f.underline ? 'underline' : 'none', color: f.color || '#000'
  });
  e.textContent = text;
  return e;
}

function textFrame(s, parent) {
  const outer = el('div', Object.assign(box(s), {
    background: s.fill && s.fill.color ? s.fill.color : 'transparent', border: lineCss(s.line), boxSizing: 'border-box',
    transform: s.rotation ? `rotate(${-s.rotation}deg)` : '', overflow: 'hidden'
  }), parent);
  const pad = s.padding || [0, 0, 0, 0];
  const inner = el('div', {
    position: 'absolute', inset: `${pad[0] * PX}px ${pad[1] * PX}px ${pad[2] * PX}px ${pad[3] * PX}px`,
    display: 'flex', flexDirection: 'column', overflow: 'hidden',
    justifyContent: { middle: 'center', bottom: 'flex-end' }[s.vAlign] || 'flex-start'
  }, outer);
  const flow = el('div', { hyphens: 'auto' }, inner);
  flow.lang = 'fr';
  addWrap(s, flow, pad);
  outer.dataset.z = s.z;
  return { s, outer, inner, flow };
}

// Habillage rectangulaire (comme Publisher par defaut pour les images) : pour chaque image
// au-dessus de la zone qui la chevauche, une cale flottante de largeur nulle pousse un
// flotteur de la taille de l'image a la bonne hauteur ; le texte le contourne.
const GAP = 4; // distance texte-image en points
let pageImages = [];
function overlapRatio(a, b) {
  const w = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x), h = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
  return w > 0 && h > 0 ? (w * h) / Math.max(1e-6, a.w * a.h) : 0;
}
function addWrap(s, flow, pad) {
  const cx = s.x + pad[3], cy = s.y + pad[0], cw = s.w - pad[1] - pad[3];
  const hits = pageImages.filter(im => im.z > s.z && im !== s && !(s.chain && im.chain === s.chain) &&
    im.x < cx + cw && im.x + im.w > cx && im.y < s.y + s.h && im.y + im.h > cy &&
    // l'objet doit etre majoritairement pose dans la zone (un objet qui l'effleure ne la repousse pas)
    overlapRatio(im, s) >= 0.5)
    .sort((a, b) => a.y - b.y);
  let used = 0;
  for (const im of hits) {
    // l'obstacle est borne au bas de la zone : s'il depasse, la zone paraitrait pleine d'emblee
    const ch = s.h - pad[0] - pad[2];
    const top = Math.max(0, im.y - GAP - cy), bottom = Math.min(ch, im.y + im.h + GAP - cy);
    if (bottom <= used) continue;
    const left = Math.max(0, im.x - GAP - cx), right = Math.min(cw, im.x + im.w + GAP - cx);
    // l'image touche-t-elle plutot le bord gauche ou droit de la colonne ?
    const side = left < cw - right ? 'left' : 'right';
    let width = side === 'left' ? right : cw - left;
    // Publisher ne fait pas couler le texte dans une bande trop etroite a cote de l'objet
    if (cw - width < Math.max(36, cw * 0.35)) width = cw;
    // la cale doit commencer sous l'obstacle precedent (clear), sinon elle remonte a cote de lui
    el('div', { float: side, clear: 'both', width: '0', height: Math.max(0, top - used) * PX + 'px' }, flow);
    el('div', { float: side, clear: side, width: width * PX + 'px', height: (bottom - Math.max(top, used)) * PX + 'px' }, flow);
    used = bottom;
  }
}

// Remplit les zones d'une chaine mot par mot : quand une zone deborde, on passe a la suivante.
function flowChain(frames, paragraphs) {
  const tokens = [];
  paragraphs.forEach((p, pi) => {
    if (!p.runs.length || p.runs.every(r => !r.text)) tokens.push({ pi, empty: true, run: p.runs[0] || {} });
    p.runs.forEach(r => (r.text.match(/\S+\s*|\s+/g) || []).forEach(w => tokens.push({ pi, run: r, text: w })));
  });
  let f = 0, curP = null, curPi = -1;
  const over = fr => fr.flow.scrollHeight > fr.inner.clientHeight + 0.5;
  for (const t of tokens) {
    for (;;) {
      const fr = frames[f];
      if (t.pi !== curPi || !curP || curP.parentNode !== fr.flow) { curP = paraEl(paragraphs[t.pi]); fr.flow.appendChild(curP); curPi = t.pi; }
      const node = t.empty ? runEl(t.run, '​') : runEl(t.run, t.text);
      curP.appendChild(node);
      if (!over(fr) || f === frames.length - 1) break;
      curP.removeChild(node);
      if (!curP.childNodes.length) fr.flow.removeChild(curP);
      f++; curP = null;
    }
  }
  const last = frames[frames.length - 1];
  return over(last);
}

// Comme Publisher ("reduire le texte en cas de debordement") : si le texte deborde de la
// zone (ou de la derniere zone de la chaine), on le reduit par dichotomie jusqu'a ce qu'il tienne.
function layoutFit(frames, paragraphs) {
  const clear = () => frames.forEach(fr => fr.flow.querySelectorAll(':scope > .tp').forEach(n => n.remove()));
  SCALE = 1;
  if (!flowChain(frames, paragraphs)) return 1;
  let lo = 0.5, hi = 1;
  for (let i = 0; i < 7; i++) {
    SCALE = (lo + hi) / 2;
    clear();
    if (flowChain(frames, paragraphs)) hi = SCALE; else lo = SCALE;
  }
  SCALE = lo; clear(); flowChain(frames, paragraphs);
  frames.forEach(fr => fr.outer.dataset.scale = lo.toFixed(3));
  const r = lo; SCALE = 1; return r;
}

// Les polices @font-face se chargent de facon asynchrone : il faut les attendre avant de
// mesurer, sinon la mise en page est calculee avec la police de secours.
const families = new Set();
doc.pages.forEach(p => p.shapes.forEach(s => (s.paragraphs || []).forEach(pa => pa.runs.forEach(r => r.font && r.font.family && families.add(r.font.family)))));
const loads = [];
families.forEach(f => ['normal', 'bold'].forEach(w => ['normal', 'italic'].forEach(st => loads.push(document.fonts.load(`${st} ${w} 16px "${f}"`).catch(() => null)))));
Promise.all(loads).then(() => document.fonts.ready).then(layoutAll);

function layoutAll() {
for (const page of doc.pages) {
  const label = el('div', { font: '12px system-ui', color: '#667', margin: '18px 0 6px' }, root);
  label.textContent = `Page ${page.index}`;
  const pg = el('div', { position: 'relative', width: page.width * PX + 'px', height: page.height * PX + 'px', background: '#fff', boxShadow: '0 4px 18px #0003', overflow: 'hidden' }, root);
  pg.className = 'page';
  const chains = {};
  pageImages = page.shapes.filter(s => (s.kind === 'image' && !s.background)
    // une zone de texte ne fait obstacle que si elle est groupee avec une image (legende)
    || (s.kind === 'text' && s.group && page.shapes.some(o => o.kind === 'image' && o.group === s.group))
    || ((s.kind === 'rect' || s.kind === 'path') && s.fill && s.fill.color && s.w < page.width * 0.9));
  for (const s of page.shapes) {
    if (s.kind === 'image') {
      const img = el('img', Object.assign(box(s), { objectFit: 'fill', border: lineCss(s.line), boxSizing: 'border-box' }), pg);
      img.src = imgSrc(s.src);
      if (s.background) img.style.objectFit = 'cover';
    } else if (s.kind === 'rect' || s.kind === 'ellipse' || s.kind === 'path') {
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      // largeur/hauteur minimale : un filet horizontal a une hauteur nulle et ne serait pas dessine
      Object.assign(svg.style, box(s), { overflow: 'visible', minWidth: '1px', minHeight: '1px' });
      const w = s.w * PX, h = s.h * PX;
      let shape;
      if (s.kind === 'ellipse') { shape = document.createElementNS(svg.namespaceURI, 'ellipse'); shape.setAttribute('cx', w / 2); shape.setAttribute('cy', h / 2); shape.setAttribute('rx', w / 2); shape.setAttribute('ry', h / 2); }
      else { shape = document.createElementNS(svg.namespaceURI, 'path'); shape.setAttribute('d', s.path ? svgPath(s) : `M0,0 H${w} V${h} H0 Z`); }
      shape.setAttribute('fill', s.fill && s.fill.color ? s.fill.color : 'none');
      if (s.fill && s.fill.opacity !== undefined) shape.setAttribute('fill-opacity', s.fill.opacity);
      shape.setAttribute('stroke', s.line ? s.line.color : 'none');
      if (s.line) shape.setAttribute('stroke-width', Math.max(s.line.weight || 1, 0.5) * PX);
      svg.appendChild(shape); pg.appendChild(svg);
    } else if (s.kind === 'text') {
      const fr = textFrame(s, pg);
      if (s.chain) (chains[s.chain] = chains[s.chain] || []).push(fr);
      else layoutFit([fr], s.paragraphs);
    }
  }
  for (const id in chains) {
    const frames = chains[id].sort((a, b) => a.s.chainIndex - b.s.chainIndex);
    layoutFit(frames, frames[0].s.paragraphs);
  }
}
document.body.dataset.done = '1';
}
// journal des reductions appliquees (lisible dans la console)
document.querySelectorAll('[data-scale]').forEach(e => console.log('reduction', e.dataset.z, e.dataset.scale));
"""

HTML = """<!doctype html><html lang="fr"><head><meta charset="utf-8"><title>{title}</title>
<style>{fonts}
body{{margin:0;background:#d9dde5;display:flex;justify-content:center}}#pages{{padding:10px 30px 40px}}</style>
</head><body><div id="pages"></div><script>{js}</script></body></html>"""


def font_names(path):
    """(famille, sous-famille) depuis la table 'name' d'un TTF/OTF."""
    import struct
    data = open(path, 'rb').read()
    n = struct.unpack_from('>H', data, 4)[0]
    for i in range(n):
        tag, _, off, _ = struct.unpack_from('>4sIII', data, 12 + 16 * i)
        if tag != b'name':
            continue
        _, count, stor = struct.unpack_from('>HHH', data, off)
        names = {}
        for j in range(count):
            pid, eid, lid, nid, ln, o = struct.unpack_from('>6H', data, off + 6 + 12 * j)
            if nid in (1, 2) and pid == 3:
                txt = data[off + stor + o:off + stor + o + ln].decode('utf-16-be', 'replace')
                # l'anglais (0x409) d'abord : sinon "Kursiv", "Cursiva"... ne sont pas reconnus
                if lid == 0x409 or nid not in names:
                    names[nid] = txt
        return names.get(1), names.get(2, 'Regular')
    return None, None


def font_line_factor(path):
    """Hauteur de ligne 'simple' de la police / taille : (winAscent + winDescent) / unitsPerEm,
    comme les applications Windows (Publisher compte l'interligne en multiples de cette valeur)."""
    import struct
    data = open(path, 'rb').read()
    tables = {}
    for i in range(struct.unpack_from('>H', data, 4)[0]):
        tag, _, off, _ = struct.unpack_from('>4sIII', data, 12 + 16 * i)
        tables[tag] = off
    upm = struct.unpack_from('>H', data, tables[b'head'] + 18)[0]
    asc, desc = struct.unpack_from('>HH', data, tables[b'OS/2'] + 74)
    return round((asc + desc) / upm, 4)


def font_faces(doc, dirs):
    """@font-face pour les familles du document trouvees dans les dossiers donnes."""
    wanted = {r['font']['family'] for p in doc['pages'] for s in p['shapes'] if s['kind'] == 'text'
              for pa in s['paragraphs'] for r in pa['runs'] if r.get('font') and r['font'].get('family')}
    css, found, metrics = [], set(), {}
    for d in dirs:
        for base, _, files in os.walk(d):
            for f in files:
                if not f.lower().endswith(('.ttf', '.otf')):
                    continue
                path = os.path.join(base, f)
                try:
                    fam, sub = font_names(path)
                except Exception:
                    continue
                if fam in wanted:
                    sub = (sub or '').lower()
                    try:
                        metrics.setdefault(fam, font_line_factor(path))
                    except Exception:
                        pass
                    url = 'file:///' + subprocess.run(['wslpath', '-m', path], capture_output=True, text=True).stdout.strip() \
                        if path.startswith('/mnt/') else 'file://' + path
                    css.append(f"@font-face{{font-family:'{fam}';src:url('{url}');"
                               f"font-weight:{'bold' if 'bold' in sub else 'normal'};"
                               f"font-style:{'italic' if 'italic' in sub else 'normal'}}}")
                    found.add(fam)
    return '\n'.join(css), found, wanted - found, metrics


def hyphenate(doc):
    try:
        import pyphen
    except ImportError:
        print('pyphen absent : pas de cesure')
        return
    dicts = {}
    for p in doc['pages']:
        for s in p['shapes']:
            if s['kind'] != 'text':
                continue
            for pa in s['paragraphs']:
                for r in pa['runs']:
                    lang = (r.get('font') or {}).get('lang') or 'fr'
                    code = {'fr': 'fr_FR', 'en': 'en_GB', 'nl': 'nl_NL', 'de': 'de_DE'}.get(lang, 'fr_FR')
                    h = dicts.setdefault(code, pyphen.Pyphen(lang=code, left=2, right=3))
                    r['text'] = re.sub(r"[^\W\d_]{6,}", lambda m: h.inserted(m.group(0), hyphen='\u00ad'), r['text'])


def main():
    d = sys.argv[1]
    dirs = sys.argv[sys.argv.index('--fonts') + 1].split(os.pathsep) if '--fonts' in sys.argv else []
    doc = json.load(open(os.path.join(d, 'document.json'), encoding='utf-8'))
    css, found, missing, metrics = font_faces(doc, dirs)
    hyphenate(doc)
    js = PAGE_JS.replace('DOC', json.dumps(doc, ensure_ascii=False).replace('</', '<\\/')).replace('__METRICS__', json.dumps(metrics))
    out = os.path.join(d, 'preview.html')
    open(out, 'w', encoding='utf-8').write(HTML.format(title=doc.get('source', 'apercu'), js=js, fonts=css))
    print(out)
    if found:
        print('polices chargees depuis les dossiers :', ', '.join(sorted(found)))
    print('polices du document non trouvees dans ces dossiers (systeme ou substitution) :', ', '.join(sorted(missing)) or '-')


if __name__ == '__main__':
    main()
