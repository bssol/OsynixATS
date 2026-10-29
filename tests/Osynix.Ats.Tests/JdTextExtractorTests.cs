using System.IO.Compression;
using System.Text;
using Osynix.Ats.Services;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;
namespace Osynix.Ats.Tests;
public class JdTextExtractorTests
{
    [Fact] public void TxtPreservesSourceWordingAndLineBreaks()
    {
        const string text="Senior Engineer\r\nRequirements:\r\n• C# & SQL\t5+ years\r\n";
        Assert.Equal(text,JdTextExtractor.Extract("role.txt",Encoding.UTF8.GetBytes(text)));
    }
    [Fact] public void DocxPreservesParagraphsAndTabs()
    {
        using var buffer=new MemoryStream();
        using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true)){
            using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Original JD</w:t></w:r></w:p><w:p><w:r><w:t>C#</w:t><w:tab/><w:t>SQL &amp; APIs</w:t></w:r></w:p></w:body></w:document>""");
        }
        Assert.Equal("Original JD\nC#\tSQL & APIs",JdTextExtractor.Extract("role.docx",buffer.ToArray()));
    }
    [Fact] public void PdfExtractsWordingAcrossPages()
    {
        var builder=new PdfDocumentBuilder();
        var font=builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(600,800).AddText("Original job description",12,new PdfPoint(40,700),font);
        builder.AddPage(600,800).AddText("Required experience: 5 years",12,new PdfPoint(40,700),font);
        var text=JdTextExtractor.Extract("role.pdf",builder.Build());
        Assert.Contains("Original job description",text);
        Assert.Contains("Required experience: 5 years",text);
        Assert.True(text.IndexOf("Original job description")<text.IndexOf("Required experience"));
    }
    [Fact] public void UnsupportedFormatIsRejected()=>Assert.Throws<InvalidOperationException>(()=>JdTextExtractor.Extract("file.exe",[1,2,3]));
}
