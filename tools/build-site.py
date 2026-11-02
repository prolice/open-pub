"""Construit le portail web d'OpenPub (page de telechargement + aide en ligne) dans un dossier.

Utilise localement (tools/publish-update.sh, sous WSL) et par les pipelines GitHub Actions (Windows).

  python tools/build-site.py --out work/site --exe dist/OpenPub/OpenPub.exe --msi dist/OpenPub-1.6.0.msi \\
         (--manifest latest.json | --sign-key cle.pem --version 1.6.0 --notes "...")

- --manifest : reutilise un latest.json deja signe (pipeline du portail) ; version et notes en sont lues.
- --sign-key : signe un nouveau latest.json avec la cle privee (pipeline de l'application, publication locale).
L'executable sert aux captures d'ecran (document de demonstration neutre) et a la verification des liens d'aide.
"""
import argparse
import html
import json
import os
import platform
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEMPLATE = os.path.join(ROOT, "tools", "site-template")
WSL = "microsoft" in platform.release().lower()

# captures de l'aide : nom du fichier -> scene de OpenPub.exe --screenshot
DOC_SCENES = {
    "accueil": "TabHome", "onglet-accueil": "TabHome", "onglet-insertion": "TabInsert", "onglet-dessin": "TabDraw",
    "dessin-crayon": "draw-tool", "onglet-forme": "shape", "onglet-image": "picture",
    "texte-edition": "text-edit", "editeur-image": "image-editor",
}

SWA_CONFIG = {
    "mimeTypes": {".msi": "application/x-msi", ".json": "application/json", ".woff2": "font/woff2"},
    "routes": [
        {"route": "/latest.json", "headers": {"Cache-Control": "no-cache, no-store, must-revalidate"}},
        {"route": "/*.msi", "headers": {"Cache-Control": "public, max-age=31536000, immutable"}},
        {"route": "/fonts/*", "headers": {"Cache-Control": "public, max-age=31536000, immutable"}},
        {"route": "/index.html", "headers": {"Cache-Control": "no-cache"}},
    ],
}


def native(path):
    """Chemin passe a l'executable Windows (sous WSL : C:\\...)."""
    path = os.path.abspath(path)
    if WSL:
        return subprocess.run(["wslpath", "-w", path], capture_output=True, text=True, check=True).stdout.strip()
    return path


def run_exe(exe, *args):
    out = subprocess.run([exe, *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if out.returncode != 0:
        sys.exit(f"echec de {os.path.basename(exe)} {args[0]} :\n{out.stdout}\n{out.stderr}")
    return out.stdout.replace("\r", "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--exe", required=True, help="OpenPub.exe autonome (captures, liens d'aide, signature)")
    ap.add_argument("--msi", required=True)
    ap.add_argument("--manifest", help="latest.json deja signe a reutiliser")
    ap.add_argument("--sign-key", help="cle privee PEM pour signer un nouveau latest.json")
    ap.add_argument("--version")
    ap.add_argument("--notes", default="")
    a = ap.parse_args()
    if not (a.manifest or a.sign_key):
        sys.exit("--manifest ou --sign-key est requis")

    out = os.path.abspath(a.out)
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)
    for item in ("assets", "fonts"):
        shutil.copytree(os.path.join(TEMPLATE, item), os.path.join(out, item))
    shutil.copy(os.path.join(TEMPLATE, "styles.css"), out)
    msi_name = os.path.basename(a.msi)
    shutil.copy(a.msi, os.path.join(out, msi_name))
    exe = os.path.abspath(a.exe)

    # --- manifeste de mise a jour
    manifest_path = os.path.join(out, "latest.json")
    if a.manifest:
        shutil.copy(a.manifest, manifest_path)
    else:
        if not a.version:
            sys.exit("--version est requis avec --sign-key")
        print(run_exe(exe, "--sign-update", native(os.path.join(out, msi_name)), a.version, a.notes,
                      native(a.sign_key), native(manifest_path)).strip())
    manifest = json.load(open(manifest_path, encoding="utf-8-sig"))
    version, notes = manifest["version"], manifest.get("notes") or ""
    if manifest.get("url") != msi_name:
        sys.exit(f"latest.json pointe vers {manifest.get('url')}, l'installeur fourni est {msi_name}")
    check = run_exe(exe, "--verify-update", native(manifest_path))
    if "INVALIDE" in check or "NON CONFORME" in check:
        sys.exit("latest.json invalide : " + check)
    print(check.strip())

    # --- page de telechargement
    run_exe(exe, "--screenshot", native(os.path.join(TEMPLATE, "demo", "document.json")),
            native(os.path.join(out, "assets", "apercu-openpub.png")), "1402", "948", "TabDraw")
    size = f"{os.path.getsize(a.msi) / 1048576:.1f}".replace(".", ",") + " Mo"
    notes_block = f'<p class="notes"><strong>Nouveautés :</strong> {html.escape(notes)}</p>' if notes.strip() else ""
    page = open(os.path.join(TEMPLATE, "index.html"), encoding="utf-8").read()
    for k, v in {"{{VERSION}}": html.escape(version), "{{MSI}}": html.escape(msi_name), "{{SIZE}}": size,
                 "{{NOTES_BLOCK}}": notes_block}.items():
        page = page.replace(k, v)
    if "{{" in page:
        sys.exit("champ non rempli dans index.html")
    open(os.path.join(out, "index.html"), "w", encoding="utf-8").write(page)
    print(f"page : version {version}, {size}")

    # --- aide en ligne
    subprocess.run([sys.executable, os.path.join(ROOT, "tools", "build-docs.py"), out, version], check=True)
    demo = native(os.path.join(TEMPLATE, "demo", "document.json"))
    for name, scene in DOC_SCENES.items():
        png = os.path.join(out, "aide", "img", f"{name}.png")
        run_exe(exe, "--screenshot", demo, native(png), "1402", "900", scene)
        if not os.path.exists(png):
            sys.exit(f"capture manquante : {name}")
    anchors_file = os.path.join(out, "aide", "ancres.txt")
    anchors = set(open(anchors_file, encoding="utf-8").read().split())
    topics = set(run_exe(exe, "--help-topics").split())
    missing = sorted(topics - anchors)
    if missing:
        sys.exit("liens d'aide de l'application sans section correspondante :\n  " + "\n  ".join(missing))
    os.remove(anchors_file)
    print(f"aide : {len([f for f in os.listdir(os.path.join(out, 'aide')) if f.endswith('.html')])} pages, "
          f"{len(DOC_SCENES)} captures, {len(topics)} liens contextuels verifies")

    json.dump(SWA_CONFIG, open(os.path.join(out, "staticwebapp.config.json"), "w"), indent=2)
    print(f"portail pret : {out}")


if __name__ == "__main__":
    main()
