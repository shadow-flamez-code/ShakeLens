// ============================================================================
//  ShakeLens - hands a picture to Google Lens
//
//  Google has no public upload API, but the web UI accepts a plain multipart
//  POST on lens.google.com/v3/upload and redirects to the visual matches.
//  A small local page does that POST from inside the browser, so the browser
//  session stays in charge and the result page loads normally.
// ============================================================================

using System.Diagnostics;
using System.IO;
using System.Text;

namespace ShakeLens.Core;

internal static class GoogleLens
{
    public static string TempFolder => Path.Combine(Path.GetTempPath(), "ShakeLens");

    public static bool Open(byte[] png, Settings settings, out string error)
    {
        error = string.Empty;
        if (png.Length == 0)
        {
            error = "There is no image data.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(TempFolder);

            string language = settings.LensLanguage;
            if (string.IsNullOrWhiteSpace(language) || language == "auto")
                language = SystemLanguage();

            string page = BuildPage(Convert.ToBase64String(png), language);
            string path = Path.Combine(TempFolder, $"lens_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.html");
            File.WriteAllText(path, page, Encoding.UTF8);

            OpenInBrowser(path, settings.Browser);
            PruneOldPages();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string BuildPage(string base64, string language)
    {
        var sb = new StringBuilder(base64.Length + 4096);
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.Append("<title>ShakeLens - Google Lens</title><style>");
        sb.Append("html,body{margin:0;height:100%;background:#141821;color:#eceff5;");
        sb.Append("font:15px/1.5 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif;");
        sb.Append("display:flex;flex-direction:column;align-items:center;justify-content:center;gap:20px}");
        sb.Append("img{max-width:min(72vw,640px);max-height:54vh;border-radius:14px;");
        sb.Append("box-shadow:0 24px 60px rgba(0,0,0,.55)}");
        sb.Append("button{background:#4c8dff;color:#fff;border:0;border-radius:999px;padding:13px 28px;");
        sb.Append("font-size:15px;cursor:pointer}button:hover{background:#3b7ae8}");
        sb.Append(".spin{width:26px;height:26px;border:3px solid #394153;border-top-color:#4c8dff;");
        sb.Append("border-radius:50%;animation:s 1s linear infinite}");
        sb.Append("@keyframes s{to{transform:rotate(360deg)}}");
        sb.Append("</style></head><body>");
        sb.Append("<img id=\"p\" alt=\"capture\">");
        sb.Append("<div id=\"s\" style=\"display:flex;align-items:center;gap:12px\">");
        sb.Append("<div class=\"spin\"></div><span>Uploading to Google Lens...</span></div>");
        sb.Append("<button id=\"b\" hidden>Search with Google Lens</button>");
        sb.Append($"<form id=\"f\" method=\"POST\" enctype=\"multipart/form-data\" action=\"https://lens.google.com/v3/upload?hl={language}&st={Environment.TickCount64}\">");
        sb.Append("<input type=\"file\" name=\"encoded_image\" id=\"i\" hidden></form>");
        sb.Append("<script>\nconst data=\"").Append(base64).Append("\";\n");
        sb.Append("document.getElementById('p').src='data:image/png;base64,'+data;\n");
        sb.Append("const button=document.getElementById('b');const status=document.getElementById('s');\n");
        sb.Append("function go(){\n  try{\n");
        sb.Append("    const bytes=Uint8Array.from(atob(data),c=>c.charCodeAt(0));\n");
        sb.Append("    const transfer=new DataTransfer();\n");
        sb.Append("    transfer.items.add(new File([bytes],'capture.png',{type:'image/png'}));\n");
        sb.Append("    const input=document.getElementById('i');\n");
        sb.Append("    input.files=transfer.files;\n");
        sb.Append("    status.style.display='none';\n");
        sb.Append("    document.getElementById('f').submit();\n");
        sb.Append("  }catch(e){status.style.display='none';button.hidden=false;}\n}\n");
        sb.Append("button.addEventListener('click',go);\n");
        sb.Append("window.addEventListener('load',go);\n");
        sb.Append("</script></body></html>");
        return sb.ToString();
    }

    private static string SystemLanguage()
    {
        try
        {
            string name = System.Globalization.CultureInfo.CurrentUICulture.Name;
            int dash = name.IndexOf('-');
            return dash > 0 ? name[..dash] : name;
        }
        catch { return "en"; }
    }

    public static void OpenInBrowser(string target, string browser)
    {
        string? executable = browser switch
        {
            "chrome" => FindFirst(
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "Google\\Chrome\\Application\\chrome.exe")),
            "edge" => FindFirst(
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
            "firefox" => FindFirst(
                @"C:\Program Files\Mozilla Firefox\firefox.exe",
                @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe"),
            _ => null
        };

        var info = new ProcessStartInfo
        {
            FileName = executable ?? target,
            UseShellExecute = true
        };
        if (executable is not null) info.Arguments = $"\"{target}\"";
        Process.Start(info);
    }

    private static string? FindFirst(params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void PruneOldPages()
    {
        try
        {
            var cutoff = DateTime.Now.AddHours(-1);
            foreach (string file in Directory.GetFiles(TempFolder, "lens_*.html"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch { /* ignore */ }
    }
}
