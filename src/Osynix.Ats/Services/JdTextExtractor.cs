using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
namespace Osynix.Ats.Services;
public static class JdTextExtractor
{
    public static string Extract(string fileName, byte[] content)
    {
        if (content.Length == 0 || content.Length > AiService.MaxFileSize)
            throw new InvalidOperationException("Choose a document of up to 10 MB.");
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".txt":
                using (var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, true))
                    return reader.ReadToEnd();
            case ".pdf":
                using (var pdf = PdfDocument.Open(content))
                {
                    if (pdf.NumberOfPages > 300) throw new InvalidOperationException("This document has too many pages to display as text. Open the original JD.");
                    return string.Join("\n\n", pdf.GetPages().Select(page=>ContentOrderTextExtractor.GetText(page)));
                }
            case ".docx":
                using (var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidOperationException("This Word document has no readable body.");
                    using var stream = entry.Open();
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 5_000_000 });
                    var document = XDocument.Load(reader);
                    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    return string.Join("\n", document.Descendants(w + "p").Select(p => string.Concat(p.Descendants()
                        .Where(x => x.Name == w + "t" || x.Name == w + "tab" || x.Name == w + "br")
                        .Select(x => x.Name == w + "t" ? x.Value : x.Name == w + "tab" ? "\t" : "\n"))));
                }
            default: throw new InvalidOperationException("Choose a PDF, DOCX, or TXT document.");
        }
    }
}
