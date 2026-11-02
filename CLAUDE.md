# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

OpenPub: a Windows desktop page-layout editor (C# / .NET 10 / WPF) that replaces Microsoft Publisher for one specific document, `newsletter  septembre 26.pub`. The UI, the online help and all code comments are in **French** — keep it that way. `PLAN.md` is the running design log (decisions, calibrations, release history); read it before large changes and append a section when shipping a version.

## Environment (WSL + Windows toolchain)

The repo lives on the Windows drive and is driven from WSL, but every .NET/WPF tool is the **Windows** one:

- .NET SDK is user-local: `D=/mnt/c/Users/prolice/AppData/Local/Microsoft/dotnet/dotnet.exe` (no `dotnet` on PATH, no machine-wide runtime). Run the debug app with `$D 'C:\dev\open-pub\src\OpenPub.App\bin\Debug\net10.0-windows\OpenPub.dll' <args>` — the apphost `.exe` in bin/Debug won't find the runtime. Only `dist/OpenPub/OpenPub.exe` (self-contained) runs standalone.
- Pass Windows paths (`C:\dev\open-pub\...`, or `$(wslpath -w ...)`) to Windows programs.
- `nuget.config` in the repo adds nuget.org (the user's global NuGet config has no sources).
- Azure CLI is the Windows one, wrapped by `tools/az.sh`.
- Python tools: run with `python3 -I`; `work/venv` holds pyphen + fontTools.

## Commands

```bash
D=/mnt/c/Users/prolice/AppData/Local/Microsoft/dotnet/dotnet.exe
$D build OpenPub.sln -v q -nologo                          # whole solution (installer project is NOT in the sln)
$D test tests/OpenPub.Tests -nologo -v q                    # xUnit, ~68 tests
$D test tests/OpenPub.Tests -nologo --filter Une_cesure_ne_duplique_pas_le_mot   # single test (method name)
DLL='C:\dev\open-pub\src\OpenPub.App\bin\Debug\net10.0-windows\OpenPub.dll'
$D $DLL --selftest 'C:\dev\open-pub\converted\newsletter-natif\document.json' 'C:\dev\open-pub\work\selftest'   # end-to-end UI test on the real window (~115 checks + PNG captures)
# ^ needs the local newsletter conversion (converted/ is git-ignored: personal data). Unit tests use tools/site-template/demo instead.
```

`App.xaml.cs` exposes headless CLI modes (used by tests, tooling and publishing):
`--import <converted dir> <out.opub>`, `--render <file> <png dir> [dpi]`, `--dump <file> [page] [--noshrink] [--all]` (layout diagnostics: exclusions + line positions), `--screenshot <file> <png> [w h] [scene]` (scenes: `TabHome|TabInsert|TabDraw|TabReview|TabView|shape|picture|text-edit|draw-tool|image-editor|file-menu|shapes-menu|about`), `--selftest`, `--spell "text" [lang]` (Windows spell checker diagnostic), `--help-topics`, `--check-update [--download]`, `--verify-update <latest.json>`, `--sign-update ...`, `--gen-update-key`, `--public-key`.

Release via **GitHub Actions** (preferred): `.github/workflows/release-app.yml` (tag `vX.Y.Z` or manual with dry-run: tests → self-contained exe → MSI with `-p:ProductVersion` → signed `latest.json` → portal → GitHub release with MSI/exe/latest.json → Azure deploy) and `.github/workflows/publish-portal.yml` (on changes to `tools/site-template/**`, `tools/build-docs.py`, `tools/build-site.py`, design assets: rebuilds page + help from the **latest GitHub release** assets without rebuilding the app, then deploys). Both build on `windows-latest` and deploy from `ubuntu-latest` (the SWA deploy action is Linux-only); shared concurrency group. Secrets: `UPDATE_SIGNING_KEY`, `AZURE_STATIC_WEB_APPS_API_TOKEN`. Portal generation is shared code: `tools/build-site.py`.

Portal (`build-site.py`, PLAN.md §6 duodecies): pages `index.html` (download), `fonctionnalites.html`, `aide/`. Azure Free quota is **250 MB for the whole site**: only the current MSI is hosted (next to `latest.json`, whose relative `url` must stay that MSI); the previous version is a link to its GitHub release asset, found automatically (highest published release below the current version, via `gh api` with `GH_TOKEN`, else the public API, else omitted). The build fails above `--max-mb` (230) or if any other `.msi`/`.exe` lands in the site. Feature list = `tools/site-template/fonctionnalites.json`; items with `"depuis": "X.Y.Z"` are hidden until that version is published (`--apercu-a-venir` to preview them, never to publish).

Local fallback: `tools/publish-update.sh X.Y.Z "notes"` bumps versions (csproj, `installer/Package.wxs`, wixproj), runs `tools/build-installer.ps1` (tests → self-contained single-file publish → WiX 5 per-user MSI in `dist/`), signs `latest.json`, generates the download site + online help in `work/site`. `--deploy` / `--deploy-only` uploads to Azure Static Web Apps via `npx @azure/static-web-apps-cli` — **production deploys are blocked in auto mode; prepare locally and ask the user to run `tools/publish-update.sh X.Y.Z "" --deploy-only` themselves.**

## Architecture

Three layers, each a project:

- **OpenPub.Core** — document model (`Model/Document.cs`: `PubDocument → Page → Shape`, units are **points**, origin top-left; JSON names match `document.json`), `.opub` package (`Io/OpubPackage.cs`: ZIP of `document.json` + `assets/` + `fonts/`, extracted to a temp work dir while open), snapshot-based undo (`Editing/UndoStack.cs` serializes the whole doc per action), text editing primitives (`Editing/StoryEditor.cs`, `TextPos(Para, Offset)`), signed update manifest (`Update/`).
- **OpenPub.Layout** — the custom text engine. `PageLayouter` groups text frames into chains and computes wrap exclusions from obstacles; `TextLayoutEngine` breaks paragraphs into lines across chained frames with French hyphenation (`Hyphenator`, Liang patterns embedded from `Resources/hyph_fr.dic`), justification, fit tolerance, and **shrink-on-overflow** (binary search on a scale factor). Measurement is abstracted behind `ITextMeasurer` so tests use a monospace fake; laid pieces carry source offsets (`Start/Length/Hyphen`) for caret mapping.
- **OpenPub.App** — WPF. `EditorSession` is the hub (package, undo, layout cache per page, selection — `Selection` list + primary `Selected` —, in-place text edit, draw tool, pen settings, link mode, spell checker); `Controls/PageView` renders a page (`Rendering/PageRenderer`) and handles mouse/keyboard (select/move/resize/rotate, snapping via `Controls/Snapper`, freehand/polygon drawing, in-place editing via `Editing/InPlaceTextEditor`). `MainWindow` is the ribbon UI (tabs are RadioButton panels; contextual "Format de la forme"/"Outils image" tabs). Theme/design system lives entirely in `App.xaml` (fonts DM Sans / Plus Jakarta Sans embedded from `Fonts/`, brushes, implicit control styles, ribbon styles `RBig/RSmall/RGroup/RibbonTab`).

`MainWindow` is split into partial classes by domain: `MainWindow.Objects.cs` (multi-selection ops, groups, align, clipboard `OpenPub.Shapes.v1`), `.Paragraph.cs` (lists, indents, character styles, format painter), `.TextFrame.cs` (links, columns, autofit, wrap), `.Review.cs` (spelling via `Editing/SpellService.cs` = Windows ISpellChecker COM, find/replace, symbols, page context menu), `.Recovery.cs` (autosave every 5 min to `%LOCALAPPDATA%\OpenPub\Recovery`, recovery prompt, external-change detection). Pure logic lives in Core (`ShapeOps`, `TextSearch`, `FrenchTypography`, `RecoveryStore`) so it is unit-tested.

Key model conventions that span layers:
- **The story belongs to the chain head**: in a chain (`Shape.Chain`/`ChainIndex`) only frame 0 has `Paragraphs`; followers are empty and get text from layout.
- `Shape.Rotation` rotates around the center; rendering pushes a transform, hit-testing/caret mapping go through `Controls/Geo.ToLocal`. Wrap uses `Shape.Bounds()` (rotated AABB); rotated text frames get no wrap.
- `Shape.Geometry` (auto-shape id, see `ShapeGeometry`) is regenerated from the frame rect; imported shapes keep absolute `Path` points instead (moved/scaled by `PageView.MoveShape`).
- Layout output: `LaidLine.Pieces` are editable text (source `Start/Length`, `Dy`, `Tracking`, use `Advance()` for caret x); `LaidLine.Marks` are drawn-only decorations (bullet/number, tab leaders, drop cap). Character segments (caps, super/subscript) never change source offsets.
- `PubDocument.FontLineFactors` / `FontAscents` are **per-document calibrations** (Publisher's line metrics for Office cloud fonts don't match winAscent+winDescent).

## .pub import pipeline (one-time conversion, already done)

Publisher itself is unlicensed on this machine (COM bridge `tools/export-pub.ps1` is dead). Conversion is `tools/preview.sh file.pub outdir`: libmspub's `pub2raw` (Ubuntu .debs extracted into `work/ref/debs/root`, no sudo) → `tools/pub2json.py` → `document.json`, then `OpenPub.dll --import`. `pub2json.py` contains hard-won fixes and calibrations measured against Publisher previews (`converted/newsletter  septembre 26/assets/page-N-preview.png`): `\r` inside pub2raw text (split on `\n`, never `splitlines`), libmspub duplicating a story into every chained frame, re-linking stories Publisher split across frames (with overlap removal), inherited paragraph style not exported, rounded-rect text inset, tilted text-frame rotation recovered from the group polygon.

## Don'ts

- The GitHub repo is **public**: `*.pub`, `*.opub`, `converted/`, `work/`, `dist/` are git-ignored on purpose — never commit newsletter content (or children's names in test strings).
- **Never open-and-save or overwrite `newsletter.opub`** — it is the user's working file (they intentionally deleted pages). Tests/screenshots must use `converted/newsletter-natif/document.json` (imported into a temp dir) or the neutral demo `tools/site-template/demo/document.json`.
- Never put screenshots of the newsletter on the public site/help (children's names and photos) — use the demo document.
- Don't block the WPF UI thread on async work in CLI modes (`.GetAwaiter().GetResult()` deadlocks) — wrap in `Task.Run`.
- Help links: constants in `Help/HelpService.cs` must match anchors generated by `tools/build-docs.py` (the publish script fails otherwise). Doc content is edited in `build-docs.py`.
- The update signing private key is `%APPDATA%\OpenPub\update-signing-key.pem` (outside the repo); the public key and feed/help URLs are `AssemblyMetadata` in `OpenPub.App.csproj`.
- Don't launch hidden Publisher COM instances (they hang on invisible dialogs and leave orphan `MSPUB.EXE`).
