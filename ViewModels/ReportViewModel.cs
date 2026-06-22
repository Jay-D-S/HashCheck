using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashCheck.Core.HashFile;
using HashCheck.Core.Validation;

namespace HashCheck.ViewModels;

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
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.AppendLine("<title>HashCheck Report</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;margin:32px}table{border-collapse:collapse;width:100%}");
        sb.AppendLine("td,th{padding:6px 12px;border:1px solid #ccc;text-align:left}");
        sb.AppendLine(".pass{color:#2db84d}.fail{color:#e81123}.section{margin-top:24px}</style></head><body>");

        sb.AppendLine($"<h1>HashCheck Validation Report</h1>");
        sb.AppendLine($"<p>Media: <strong>{Report.MediaName}</strong></p>");
        sb.AppendLine($"<p>Timestamp: {Report.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}</p>");
        sb.AppendLine($"<p>Status: <strong class='{(Report.Passed ? "pass" : "fail")}'>{StatusText}</strong></p>");
        sb.AppendLine("<h2>Summary</h2><table>");
        sb.AppendLine($"<tr><th>Metric</th><th>Count</th></tr>");
        sb.AppendLine($"<tr><td>Files Found</td><td>{Report.TotalFilesFound}</td></tr>");
        sb.AppendLine($"<tr><td>Files in Hash Set</td><td>{Report.TotalFilesInHashSet}</td></tr>");
        sb.AppendLine($"<tr><td>Matching</td><td>{Report.TotalMatching}</td></tr>");
        sb.AppendLine($"<tr><td>Corrupted (bit-rot)</td><td>{Report.TotalCorrupted}</td></tr>");
        sb.AppendLine($"<tr><td>Modified (legitimate)</td><td>{Report.TotalModified}</td></tr>");
        sb.AppendLine($"<tr><td>Missing</td><td>{Report.TotalMissing}</td></tr>");
        sb.AppendLine($"<tr><td>Errors</td><td>{Report.TotalErrors}</td></tr>");
        sb.AppendLine($"<tr><td>New</td><td>{Report.TotalNew}</td></tr>");
        sb.AppendLine("</table>");

        AppendListSection(sb, "Missing Files", Report.MissingFiles);
        AppendNotMatchingSection(sb, Report.NotMatchingFiles);
        AppendListSection(sb, "New Files", Report.NewFiles);
        AppendErrorSection(sb, Report.ErrorFiles);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendListSection(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"<div class='section'><h2>{title} ({items.Count})</h2><table>");
        sb.AppendLine("<tr><th>Path</th></tr>");
        foreach (var item in items)
            sb.AppendLine($"<tr><td>{HtmlEncode(item)}</td></tr>");
        sb.AppendLine("</table></div>");
    }

    private static void AppendNotMatchingSection(StringBuilder sb, IReadOnlyList<NotMatchingFile> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"<div class='section'><h2>Not Matching ({items.Count})</h2><table>");
        sb.AppendLine("<tr><th>Path</th><th>Reason</th></tr>");
        foreach (var item in items)
            sb.AppendLine($"<tr><td>{HtmlEncode(item.RelativePath)}</td><td>{item.Reason}</td></tr>");
        sb.AppendLine("</table></div>");
    }

    private static void AppendErrorSection(StringBuilder sb, IReadOnlyList<ErrorFile> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"<div class='section'><h2>Errors ({items.Count})</h2><table>");
        sb.AppendLine("<tr><th>Path</th><th>Error</th></tr>");
        foreach (var item in items)
            sb.AppendLine($"<tr><td>{HtmlEncode(item.RelativePath)}</td><td>{HtmlEncode(item.ErrorMessage)}</td></tr>");
        sb.AppendLine("</table></div>");
    }

    public string BuildCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"HashCheck Report,{Report.MediaName},{Report.Timestamp.ToLocalTime():O},{StatusText}");
        sb.AppendLine("Type,Path,Detail");
        foreach (var f in Report.MissingFiles)
            sb.AppendLine($"Missing,{CsvEscape(f)},");
        foreach (var f in Report.NotMatchingFiles)
            sb.AppendLine($"{f.Reason},{CsvEscape(f.RelativePath)},");
        foreach (var f in Report.NewFiles)
            sb.AppendLine($"New,{CsvEscape(f)},");
        foreach (var f in Report.ErrorFiles)
            sb.AppendLine($"Error,{CsvEscape(f.RelativePath)},{CsvEscape(f.ErrorMessage)}");
        return sb.ToString();
    }

    private static string HtmlEncode(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string CsvEscape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
