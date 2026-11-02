using System.IO;
using System.Windows;
using OpenPub.Core.Io;

namespace OpenPub.App;

/// <summary>
/// Point d'entree. Modes en ligne de commande (sans fenetre), utiles pour l'automatisation et les tests :
///   OpenPub.exe --import dossier_converti sortie.opub
///   OpenPub.exe --render fichier.opub|document.json dossier_png [dpi]
/// Sinon : ouvre l'editeur (avec le fichier passe en argument, le cas echeant).
/// </summary>
public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        if (args.Length > 0 && args[0].StartsWith("--")) AttachConsole(-1);   // sorties visibles en ligne de commande
        try
        {
            if (args.Length >= 3 && args[0] == "--import")
            {
                using var pkg = OpubPackage.ImportFolder(args[1], FontEmbedder.DefaultSearchDirs);
                pkg.Save(args[2]);
                Console.WriteLine($"OK -> {args[2]}");
                Shutdown(0);
                return;
            }
            if (args.Length >= 2 && args[0] == "--gen-update-key")
            {
                // cree la cle de signature des mises a jour (a garder secrete, hors du depot)
                if (File.Exists(args[1])) { Console.Error.WriteLine("La cle existe deja : " + args[1]); Shutdown(1); return; }
                var (priv, pub) = OpenPub.Core.Update.UpdateSigner.GenerateKey();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
                File.WriteAllText(args[1], priv);
                Console.WriteLine(pub);
                Shutdown(0);
                return;
            }
            if (args.Length >= 2 && args[0] == "--public-key")
            {
                Console.WriteLine(OpenPub.Core.Update.UpdateSigner.PublicKeyOf(File.ReadAllText(args[1])));
                Shutdown(0);
                return;
            }
            if (args.Length >= 6 && args[0] == "--sign-update")
            {
                // --sign-update installeur.msi version "notes" cle.pem latest.json
                var m = new OpenPub.Core.Update.UpdateManifest
                {
                    Version = args[2], Url = Path.GetFileName(args[1]), Sha256 = OpenPub.Core.Update.UpdateSigner.Sha256Of(args[1]),
                    Size = new FileInfo(args[1]).Length, Notes = args[3], Published = DateTime.UtcNow,
                };
                OpenPub.Core.Update.UpdateSigner.Sign(m, File.ReadAllText(args[4]));
                File.WriteAllText(args[5], m.ToJson());
                Console.WriteLine($"latest.json signe : {m.Version}, {m.Size / 1048576.0:0.0} Mo, sha256 {m.Sha256[..12]}...");
                Shutdown(0);
                return;
            }
            if (args.Length >= 2 && args[0] == "--verify-update")
            {
                // verifie hors ligne un latest.json (et l'installeur voisin) avec la cle publique embarquee
                var m = OpenPub.Core.Update.UpdateManifest.Parse(File.ReadAllText(args[1]));
                bool sig = OpenPub.Core.Update.UpdateSigner.Verify(m, Update.UpdateService.PublicKey ?? "");
                var msi = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, m.Url);
                bool sha = File.Exists(msi) && OpenPub.Core.Update.UpdateSigner.Sha256Of(msi) == m.Sha256;
                Console.WriteLine($"signature : {(sig ? "valide" : "INVALIDE")} ; installeur : {(sha ? "empreinte conforme" : "NON CONFORME")}");
                Shutdown(sig && sha ? 0 : 1);
                return;
            }
            if (args.Length >= 1 && args[0] == "--check-update")
            {
                // verifie la mise a jour en ligne (et la telecharge avec --download), sans rien installer
                // sur un fil secondaire : attendre une tache asynchrone sur le fil WPF provoquerait un interblocage
                var r = Task.Run(() => Update.UpdateService.CheckAsync(false)).GetAwaiter().GetResult();
                Console.WriteLine($"Version installee : {Update.UpdateService.CurrentVersion}; flux : {Update.UpdateService.FeedUrl}");
                Console.WriteLine(r.Message + (r.Manifest is { } mm ? $" (en ligne : {mm.Version}, signature valide)" : ""));
                if (r.Manifest is { } m2 && args.Contains("--download"))
                {
                    var f = Task.Run(() => Update.UpdateService.DownloadAsync(m2, null)).GetAwaiter().GetResult();
                    Console.WriteLine("Telecharge et verifie (SHA-256 conforme) : " + f);
                }
                Shutdown(r.Manifest is null ? 1 : 0);
                return;
            }
            if (args.Length >= 1 && args[0] == "--help-topics")
            {
                foreach (var t in Help.HelpService.AllTopics) Console.WriteLine(t);
                Shutdown(0);
                return;
            }
            if (args.Length >= 3 && args[0] == "--screenshot")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int sw = args.Length >= 5 ? int.Parse(args[3]) : 1402, sh = args.Length >= 5 ? int.Parse(args[4]) : 948;
                var code = SelfTest.Screenshot(args[1], args[2], sw, sh, args.Length >= 6 ? args[5] : null);
                Shutdown(code);
                return;
            }
            if (args.Length >= 3 && args[0] == "--selftest")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var code = SelfTest.Run(args[1], args[2]);
                Shutdown(code);
                return;
            }
            if (args.Length >= 2 && args[0] == "--dump")
            {
                // diagnostic : obstacles et lignes de la page 1 (ou de la page donnee)
                using var pkg = OpubPackage.Open(args[1], FontEmbedder.DefaultSearchDirs);
                using var session = new EditorSession(pkg);
                if (args.Contains("--noshrink")) session.Engine.ShrinkOnOverflow = false;
                var page = session.Doc.Pages[args.Length >= 3 && int.TryParse(args[2], out var pn) ? pn - 1 : 0];
                var obstacles = OpenPub.Layout.PageLayouter.Obstacles(page);
                foreach (var f in page.Shapes.Where(x => x.IsText && (x.Chain is not null || args.Contains("--all"))))
                {
                    var g = OpenPub.Layout.PageLayouter.Geometry(f, obstacles);
                    Console.WriteLine($"zone z={f.Z} chaine={f.Chain}#{f.ChainIndex} contenu {g.Width:0.0}x{g.Height:0.0}");
                    foreach (var e2 in g.Exclusions) Console.WriteLine($"   obstacle {e2.Top:0.0}-{e2.Bottom:0.0} x {e2.Left:0.0}-{e2.Right:0.0} {(e2.FullWidth ? "pleine largeur" : "")}");
                }
                var lay = session.Layout(page);
                foreach (var fl in lay.Chains.SelectMany(c => c.Frames).Where(fl => fl.Shape.Chain is not null || args.Contains("--all")))
                {
                    Console.WriteLine($"-- z={fl.Shape.Z}");
                    foreach (var l in fl.Lines)
                        Console.WriteLine($"   {l.Top - fl.Shape.Y,6:0.0} h={l.Height:0.00} p{l.ParagraphIndex} | {string.Join(" ", l.Pieces.Select(x => $"[{x.Text}@{x.X - fl.Shape.X:0.0}+{x.Width:0.0}]"))}");
                }
                Shutdown(0);
                return;
            }
            if (args.Length >= 3 && args[0] == "--render")
            {
                double dpi = args.Length >= 4 && double.TryParse(args[3], out var d) ? d : 150;
                using var pkg = OpubPackage.Open(args[1], FontEmbedder.DefaultSearchDirs);
                using var session = new EditorSession(pkg);
                Directory.CreateDirectory(args[2]);
                var files = Exporter.ExportPng(session, args[2], dpi);
                Console.WriteLine($"{files.Count} pages -> {args[2]}");
                foreach (var page in session.Doc.Pages)
                    foreach (var c in session.Layout(page).Chains.Where(c => c.Scale < 0.999 || c.Overflow))
                        Console.WriteLine($"  page {page.Index} : zone z={c.Frames[0].Shape.Z} ({c.Frames.Count} zone(s)) reduite a {c.Scale:P0}{(c.Overflow ? ", DEBORDE" : "")}");
                if (session.Fonts.Substituted.Count > 0)
                    Console.WriteLine("Polices remplacees : " + string.Join(", ", session.Fonts.Substituted));
                Shutdown(0);
                return;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Shutdown(1);
            return;
        }

        var win = new MainWindow { TrackRecent = true };
        MainWindow = win;
        win.Show();
        if (args.Length >= 1 && File.Exists(args[0])) win.OpenFile(args[0]);
    }
}
