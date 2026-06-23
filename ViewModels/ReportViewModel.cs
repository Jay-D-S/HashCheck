using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashCheck.Core.HashFile;
using HashCheck.Core.Validation;

namespace HashCheck.ViewModels;

/// <summary>View model for the report page. Wraps a <see cref="ValidationReport"/> and provides HTML and CSV export methods.</summary>
public partial class ReportViewModel : ViewModelBase
{
    public ValidationReport Report { get; }
    public HashFileData? HashFile { get; }

    public string StatusText => Report.Passed ? "PASS" : "FAIL";
    public string StatusColor => Report.Passed ? "#2DB84D" : "#E81123";

    public ReportViewModel(ValidationReport report, HashFileData? hashFile = null)
    {
        Report = report;
        HashFile = hashFile;
    }

    public async Task ExportHtmlAsync(string outputPath)
    {
        var html = BuildHtml();
        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8);
    }

    public async Task ExportCsvAsync(string outputPath)
    {
        var csv = BuildCsv();
        await File.WriteAllTextAsync(outputPath, csv, Encoding.UTF8);
    }

    public string BuildHtml()
    {
        var r = Report;
        var sb = new StringBuilder();
        var passCss = r.Passed ? "pass-bg" : "fail-bg";

        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.Append($"<title>HashCheck Report — {HtmlEncode(r.MediaName)}</title>");
        sb.Append("<style>");
        sb.Append("body{font-family:Segoe UI,sans-serif;margin:32px;color:#1a1a1a;background:#f5f5f5}");
        sb.Append("h1{margin:0 0 6px;font-size:22px}");
        sb.Append(".meta{color:#555;font-size:13px;margin-bottom:20px;line-height:1.7}");
        sb.Append(".badge{display:inline-block;padding:3px 10px;border-radius:4px;font-weight:bold;font-size:13px;color:#fff;margin-left:10px;vertical-align:middle}");
        sb.Append(".pass-bg{background:#2db84d}.fail-bg{background:#e81123}");
        sb.Append(".cards{display:flex;flex-wrap:wrap;gap:12px;margin-bottom:24px}");
        sb.Append(".card{background:#fff;border:1px solid #ddd;border-radius:6px;padding:12px 18px;min-width:110px}");
        sb.Append(".card-label{font-size:11px;color:#666;text-transform:uppercase;letter-spacing:.5px;margin-bottom:2px}");
        sb.Append(".card-value{font-size:26px;font-weight:700}");
        sb.Append(".ok{color:#2db84d}.corrupted{color:#e81123}.modified{color:#d97706}.warn{color:#d97706}.info{color:#0066cc}.err{color:#e81123}");
        sb.Append("h2{font-size:14px;font-weight:600;margin:24px 0 8px;padding-bottom:4px;border-bottom:2px solid}");
        sb.Append("h2.corrupted{color:#e81123;border-color:#e81123}");
        sb.Append("h2.modified{color:#d97706;border-color:#d97706}");
        sb.Append("h2.missing{color:#d97706;border-color:#d97706}");
        sb.Append("h2.new{color:#0066cc;border-color:#0066cc}");
        sb.Append("h2.err{color:#e81123;border-color:#e81123}");
        sb.Append("table{border-collapse:collapse;width:100%;background:#fff;border-radius:6px;box-shadow:0 1px 3px rgba(0,0,0,.08);margin-bottom:8px}");
        sb.Append("th{background:#f0f0f0;padding:7px 14px;text-align:left;font-size:11px;text-transform:uppercase;letter-spacing:.5px;font-weight:600}");
        sb.Append("td{padding:6px 14px;border-top:1px solid #eee;font-size:12px;font-family:'Cascadia Code',Consolas,monospace}");
        sb.Append("tr:hover td{background:#fafafa}");
        sb.Append("</style></head><body>");

        // Title + status badge
        sb.Append($"<h1>{HtmlEncode(r.MediaName)} <span class='badge {passCss}'>{StatusText}</span></h1>");

        // Meta line
        var metaParts = new List<string>
        {
            HtmlEncode(r.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
        };
        if (!string.IsNullOrEmpty(r.VolumeSerial))
            metaParts.Insert(0, $"Volume: <code>{HtmlEncode(r.VolumeSerial)}</code>");
        if (!string.IsNullOrEmpty(r.ScanRoot))
            metaParts.Insert(metaParts.Count - 1, $"Scan root: <code>{HtmlEncode(r.ScanRoot)}</code>");
        sb.Append($"<div class='meta'>{string.Join(" &nbsp;&middot;&nbsp; ", metaParts)}</div>");

        // Summary cards — always show Files and Matching; only show problem categories when non-zero
        sb.Append("<div class='cards'>");
        sb.Append(Card("Files scanned", r.TotalFilesFound.ToString(), ""));
        sb.Append(Card("Matching", r.TotalMatching.ToString(), "ok"));
        if (r.TotalCorrupted > 0) sb.Append(Card("Corrupted", r.TotalCorrupted.ToString(), "corrupted"));
        if (r.TotalModified > 0)  sb.Append(Card("Modified",  r.TotalModified.ToString(),  "modified"));
        if (r.TotalMissing > 0)   sb.Append(Card("Missing",   r.TotalMissing.ToString(),   "warn"));
        if (r.TotalNew > 0)       sb.Append(Card("New files", r.TotalNew.ToString(),        "info"));
        if (r.TotalErrors > 0)    sb.Append(Card("Errors",    r.TotalErrors.ToString(),    "err"));
        sb.Append("</div>");

        // Detail sections — each only rendered when they have entries
        var corrupted = r.NotMatchingFiles.Where(f => f.Reason == NotMatchingReason.Corrupted).ToList();
        var modified  = r.NotMatchingFiles.Where(f => f.Reason == NotMatchingReason.Modified).ToList();

        if (corrupted.Count > 0)
        {
            sb.Append($"<h2 class='corrupted'>Corrupted — bit-rot suspected ({corrupted.Count})</h2>");
            sb.Append("<table><tr><th>Path</th></tr>");
            foreach (var f in corrupted) sb.Append($"<tr><td>{HtmlEncode(f.RelativePath)}</td></tr>");
            sb.Append("</table>");
        }

        if (modified.Count > 0)
        {
            sb.Append($"<h2 class='modified'>Modified — hash and metadata changed ({modified.Count})</h2>");
            sb.Append("<table><tr><th>Path</th></tr>");
            foreach (var f in modified) sb.Append($"<tr><td>{HtmlEncode(f.RelativePath)}</td></tr>");
            sb.Append("</table>");
        }

        if (r.MissingFiles.Count > 0)
        {
            sb.Append($"<h2 class='missing'>Missing — in hash set but not on media ({r.MissingFiles.Count})</h2>");
            sb.Append("<table><tr><th>Path</th></tr>");
            foreach (var f in r.MissingFiles) sb.Append($"<tr><td>{HtmlEncode(f)}</td></tr>");
            sb.Append("</table>");
        }

        if (r.NewFiles.Count > 0)
        {
            sb.Append($"<h2 class='new'>New Files — on media but not in hash set ({r.NewFiles.Count})</h2>");
            sb.Append("<table><tr><th>Path</th></tr>");
            foreach (var f in r.NewFiles) sb.Append($"<tr><td>{HtmlEncode(f)}</td></tr>");
            sb.Append("</table>");
        }

        if (r.ErrorFiles.Count > 0)
        {
            sb.Append($"<h2 class='err'>Read Errors ({r.ErrorFiles.Count})</h2>");
            sb.Append("<table><tr><th>Path</th><th>Error</th></tr>");
            foreach (var f in r.ErrorFiles)
                sb.Append($"<tr><td>{HtmlEncode(f.RelativePath)}</td><td>{HtmlEncode(f.ErrorMessage)}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Card(string label, string value, string valueCss) =>
        $"<div class='card'><div class='card-label'>{label}</div>" +
        $"<div class='card-value {valueCss}'>{value}</div></div>";

    public string BuildCsv()
    {
        var r = Report;
        var sb = new StringBuilder();

        // Metadata block
        sb.AppendLine("HashCheck Validation Report");
        sb.AppendLine($"Media,{CsvEscape(r.MediaName)}");
        if (!string.IsNullOrEmpty(r.VolumeSerial)) sb.AppendLine($"Volume,{CsvEscape(r.VolumeSerial)}");
        if (!string.IsNullOrEmpty(r.ScanRoot))     sb.AppendLine($"Scan Root,{CsvEscape(r.ScanRoot)}");
        sb.AppendLine($"Timestamp,{r.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Status,{StatusText}");
        sb.AppendLine($"Files Scanned,{r.TotalFilesFound}");
        sb.AppendLine($"Matching,{r.TotalMatching}");
        if (r.TotalCorrupted > 0) sb.AppendLine($"Corrupted,{r.TotalCorrupted}");
        if (r.TotalModified > 0)  sb.AppendLine($"Modified,{r.TotalModified}");
        if (r.TotalMissing > 0)   sb.AppendLine($"Missing,{r.TotalMissing}");
        if (r.TotalNew > 0)       sb.AppendLine($"New,{r.TotalNew}");
        if (r.TotalErrors > 0)    sb.AppendLine($"Errors,{r.TotalErrors}");
        sb.AppendLine();

        // Data rows
        sb.AppendLine("Category,Path,Detail");
        foreach (var f in r.MissingFiles)
            sb.AppendLine($"Missing,{CsvEscape(f)},");
        foreach (var f in r.NotMatchingFiles)
            sb.AppendLine($"{f.Reason},{CsvEscape(f.RelativePath)},");
        foreach (var f in r.NewFiles)
            sb.AppendLine($"New,{CsvEscape(f)},");
        foreach (var f in r.ErrorFiles)
            sb.AppendLine($"Error,{CsvEscape(f.RelativePath)},{CsvEscape(f.ErrorMessage)}");
        return sb.ToString();
    }

    private static string HtmlEncode(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string CsvEscape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
