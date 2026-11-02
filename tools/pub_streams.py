"""Lecteur minimal Compound File Binary (OLE) pour inspecter un .pub sans Publisher.

Usage : python3 -I pub_streams.py fichier.pub            -> liste des flux
        python3 -I pub_streams.py fichier.pub --text     -> texte du flux Quill/QuillSub/CONTENTS
        python3 -I pub_streams.py fichier.pub --dump OUT -> extrait tous les flux dans OUT/
"""
import os
import struct
import sys

FREESECT, ENDOFCHAIN = 0xFFFFFFFF, 0xFFFFFFFE


class Cfb:
    def __init__(self, path):
        self.data = open(path, 'rb').read()
        h = self.data[:512]
        if h[:8] != bytes.fromhex('D0CF11E0A1B11AE1'):
            raise ValueError('pas un fichier OLE')
        self.ssz = 1 << struct.unpack_from('<H', h, 30)[0]
        self.mssz = 1 << struct.unpack_from('<H', h, 32)[0]
        n_fat, dir_start = struct.unpack_from('<II', h, 44)
        self.cutoff, mini_fat_start, _, difat_start, n_difat = struct.unpack_from('<IIIII', h, 56)
        difat = list(struct.unpack_from('<109I', h, 76))
        s = difat_start
        for _ in range(n_difat):
            sec = self.sector(s)
            vals = struct.unpack_from('<%dI' % (self.ssz // 4), sec)
            difat += vals[:-1]
            s = vals[-1]
        self.fat = []
        for s in difat[:n_fat]:
            self.fat += struct.unpack_from('<%dI' % (self.ssz // 4), self.sector(s))
        self.minifat = []
        if mini_fat_start < ENDOFCHAIN:
            raw = self.chain(mini_fat_start)
            self.minifat = list(struct.unpack_from('<%dI' % (len(raw) // 4), raw))
        raw = self.chain(dir_start)
        self.entries = []
        for i in range(len(raw) // 128):
            e = raw[i * 128:(i + 1) * 128]
            nlen = struct.unpack_from('<H', e, 64)[0]
            name = e[:max(0, nlen - 2)].decode('utf-16le', 'replace')
            typ = e[66]
            left, right, child = struct.unpack_from('<III', e, 68)
            start = struct.unpack_from('<I', e, 116)[0]
            size = struct.unpack_from('<Q', e, 120)[0] & 0xFFFFFFFF
            self.entries.append(dict(name=name, type=typ, left=left, right=right, child=child, start=start, size=size))
        self.ministream = self.chain(self.entries[0]['start']) if self.entries else b''

    def sector(self, i):
        o = 512 + i * self.ssz
        return self.data[o:o + self.ssz]

    def chain(self, start):
        out, s, seen = [], start, set()
        while s < ENDOFCHAIN and s not in seen:
            seen.add(s)
            out.append(self.sector(s))
            s = self.fat[s]
        return b''.join(out)

    def read(self, e):
        if e['size'] < self.cutoff and e['type'] == 2:
            out, s = [], e['start']
            while s < ENDOFCHAIN:
                o = s * self.mssz
                out.append(self.ministream[o:o + self.mssz])
                s = self.minifat[s]
            return b''.join(out)[:e['size']]
        return self.chain(e['start'])[:e['size']]

    def walk(self, idx=None, prefix=''):
        """Renvoie [(chemin, entree)] en parcourant l'arbre rouge-noir."""
        if idx is None:
            return self.walk(self.entries[0]['child'], '')
        res = []
        stack = [idx]
        while stack:
            i = stack.pop()
            if i >= len(self.entries) or i == FREESECT:
                continue
            e = self.entries[i]
            stack += [e['left'], e['right']]
            path = prefix + e['name']
            res.append((path, e))
            if e['type'] == 1:
                res += self.walk(e['child'], path + '/')
        return res


def main():
    cfb = Cfb(sys.argv[1])
    items = sorted(cfb.walk(), key=lambda x: x[0])
    if '--text' in sys.argv:
        for path, e in items:
            if path.endswith('QuillSub/CONTENTS'):
                raw = cfb.read(e)
                # Le texte Quill est en UTF-16LE ; 0x0D = fin de paragraphe
                txt = raw.decode('utf-16le', 'replace')
                sys.stdout.write(txt.replace('\r', '\n'))
        return
    if '--dump' in sys.argv:
        out = sys.argv[sys.argv.index('--dump') + 1]
        for path, e in items:
            if e['type'] == 2:
                p = os.path.join(out, path.replace('\x01', '_').replace('\x05', '_'))
                os.makedirs(os.path.dirname(p) or out, exist_ok=True)
                open(p, 'wb').write(cfb.read(e))
        print('flux extraits dans', out)
        return
    for path, e in items:
        kind = {1: 'dossier', 2: 'flux'}.get(e['type'], e['type'])
        print(f"{kind:8} {e['size']:>10}  {path!r}")


if __name__ == '__main__':
    main()
