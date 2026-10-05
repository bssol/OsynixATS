using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Osynix.Ats.Data;
using Osynix.Ats.Domain;

namespace Osynix.Ats.Services;

public interface IDocumentStore
{
    Task<StoredDocument> Store(string fileName,byte[] content,CancellationToken token=default);
    Task<StoredDocument?> Read(Guid id,CancellationToken token=default);
}

public class DatabaseDocumentStore(IDbContextFactory<AtsDbContext> factory) : IDocumentStore
{
    public async Task<StoredDocument> Store(string fileName,byte[] content,CancellationToken token=default)
    {
        if(content.Length==0||content.Length>AiService.MaxFileSize) throw new InvalidOperationException("Documents must be between 1 byte and 10 MB.");
        var hash=Convert.ToHexString(SHA256.HashData(content));await using var db=await factory.CreateDbContextAsync(token);
        var existing=await db.Documents.AsNoTracking().FirstOrDefaultAsync(x=>x.ContentHash==hash,token);if(existing is not null) return existing;
        var document=new StoredDocument{FileName=Path.GetFileName(fileName),Content=content,ContentHash=hash,ContentType=Path.GetExtension(fileName).ToLowerInvariant() switch {".pdf"=>"application/pdf",".docx"=>"application/vnd.openxmlformats-officedocument.wordprocessingml.document",_=>"text/plain"}};
        db.Documents.Add(document);await db.SaveChangesAsync(token);return document;
    }
    public async Task<StoredDocument?> Read(Guid id,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);return await db.Documents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token);
    }
}
