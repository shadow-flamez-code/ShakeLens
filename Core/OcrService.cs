// ============================================================================
//  ShakeLens - "copy text" through the Windows OCR engine
//
//  The WinRT projections are not available to a plain .NET app without the
//  Windows SDK package, so the small helper script below drives
//  Windows.Media.Ocr through Windows PowerShell, which can talk to WinRT
//  out of the box.  Everything still runs locally.
// ============================================================================

using System.Diagnostics;
using System.IO;
using System.Text;

namespace ShakeLens.Core;

internal static class OcrService
{
    private const string Script = """
$ErrorActionPreference = 'Stop'
param([string]$Path)

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
$null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Media.Ocr, ContentType=WindowsRuntime]

$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]

function Await($op, $type) {
    $m = $asTaskGeneric.MakeGenericMethod($type)
    $t = $m.Invoke($null, @($op))
    $t.Wait(-1) | Out-Null
    $t.Result
}

$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Path)) ([Windows.Storage.StorageFile])
$stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
$decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
$bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if (-not $engine) { Write-Output 'NO_ENGINE'; exit 2 }
$result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
Write-Output $result.Text
""";

    public static bool Recognize(byte[] png, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;

        try
        {
            Directory.CreateDirectory(GoogleLens.TempFolder);
            string scriptPath = Path.Combine(GoogleLens.TempFolder, "ocr.ps1");
            if (!File.Exists(scriptPath))
                File.WriteAllText(scriptPath, Script.Replace("param([string]$Path)", "param([string]$Path)"), Encoding.UTF8);

            string imagePath = Path.Combine(GoogleLens.TempFolder, $"ocr_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(imagePath, png);

            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Bypass");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(scriptPath);
            info.ArgumentList.Add("-Path");
            info.ArgumentList.Add(imagePath);

            using var process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(20000);

            try { File.Delete(imagePath); } catch { /* ignore */ }

            if (output.Contains("NO_ENGINE"))
            {
                error = "Windows OCR is not available for your display language. "
                      + "Add the language in Settings > Time & language and try again.";
                return false;
            }

            text = output.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "No text was found in the selection.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
