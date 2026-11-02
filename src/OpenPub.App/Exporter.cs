using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenPub.App.Controls;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

/// <summary>Sorties : images PNG par page et impression (dont "Microsoft Print to PDF").</summary>
public static class Exporter
{
    static PageView MakeView(EditorSession session, Page page)
    {
        var view = new PageView(session, page) { Interactive = false, Zoom = 1 };
        var size = new Size(page.Width * PageView.DipPerPoint, page.Height * PageView.DipPerPoint);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();
        return view;
    }

    public static List<string> ExportPng(EditorSession session, string dir, double dpi = 150)
    {
        var files = new List<string>();
        foreach (var page in session.Doc.Pages)
        {
            var view = MakeView(session, page);
            int w = (int)Math.Ceiling(page.Width * PageView.DipPerPoint * dpi / 96);
            int h = (int)Math.Ceiling(page.Height * PageView.DipPerPoint * dpi / 96);
            var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(view);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            var path = Path.Combine(dir, $"page-{page.Index}.png");
            using (var fs = File.Create(path)) enc.Save(fs);
            files.Add(path);
        }
        return files;
    }

    public static void Print(EditorSession session, string title)
    {
        var dlg = new PrintDialog { UserPageRangeEnabled = false };
        if (session.Doc.Pages.FirstOrDefault() is { } first)
        {
            dlg.PrintTicket.PageMediaSize = new PageMediaSize(first.Width * PageView.DipPerPoint, first.Height * PageView.DipPerPoint);
            dlg.PrintTicket.PageOrientation = first.Width > first.Height ? PageOrientation.Landscape : PageOrientation.Portrait;
        }
        if (dlg.ShowDialog() != true) return;
        var doc = new FixedDocument();
        foreach (var page in session.Doc.Pages)
        {
            var size = new Size(page.Width * PageView.DipPerPoint, page.Height * PageView.DipPerPoint);
            var fp = new FixedPage { Width = size.Width, Height = size.Height, Background = Brushes.White };
            fp.Children.Add(MakeView(session, page));
            var pc = new PageContent();
            ((System.Windows.Markup.IAddChild)pc).AddChild(fp);
            doc.Pages.Add(pc);
        }
        dlg.PrintDocument(doc.DocumentPaginator, title);
    }
}
