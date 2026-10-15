using System.Buffers.Binary;
namespace Osynix.Ats.Domain;
public record CareersPalette(string Key,string Name,string Primary,string Deep,string Tint);
public record CareersCover(int Id,string Name){public string Path=>$"/images/careers/cover-{Id:00}.jpg";}
public static class CareersBranding
{
    public static readonly CareersPalette[] Palettes=[new("Osynix","Osynix Blue","#1958ff","#443d99","#eef2ff"),new("Ocean","Ocean Teal","#087b8e","#155e75","#eaf8fa"),new("Emerald","Emerald Green","#147d54","#14543c","#edf8f1"),new("Rose","Rose Plum","#a82e69","#642357","#fcf0f6"),new("Amber","Warm Amber","#a4510c","#713c18","#fff7eb"),new("Slate","Midnight Slate","#40536f","#1c2940","#eef2f7")];
    public static readonly CareersCover[] Covers=[new(1,"Modern Workspace"),new(2,"Bright Office"),new(3,"Creative Studio"),new(4,"City Skyline"),new(5,"Focused Workspace"),new(6,"Open Office"),new(7,"Collaboration"),new(8,"Connected World"),new(9,"Business Planning"),new(10,"Teamwork")];
    public static CareersPalette Palette(string key)=>Palettes.FirstOrDefault(p=>p.Key==key)??Palettes[0];
    public static string StockPath(int id)=>Covers.FirstOrDefault(c=>c.Id==id)?.Path??Covers[0].Path;
    public static string ValidateCover(byte[] bytes)
    {
        if(bytes.Length>5*1024*1024)throw new InvalidOperationException("Cover Photo Must Be At Most 5 MB.");
        int width=0,height=0;string type="";
        if(bytes.Length>=33&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})&&bytes.AsSpan(12,4).SequenceEqual("IHDR"u8)){
            width=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));height=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));type="image/png";
        }else if(bytes.Length>4&&bytes[0]==255&&bytes[1]==216&&bytes[^2]==255&&bytes[^1]==217){
            var i=2;while(i+3<bytes.Length){if(bytes[i++]!=255)break;while(i<bytes.Length&&bytes[i]==255)i++;if(i>=bytes.Length)break;var marker=bytes[i++];if(marker is 216 or 217||marker is >=208 and <=215)continue;if(i+2>bytes.Length)break;var length=(bytes[i]<<8)|bytes[i+1];if(length<2||i+length>bytes.Length)break;if(marker is 192 or 193 or 194){if(length<8)break;height=(bytes[i+3]<<8)|bytes[i+4];width=(bytes[i+5]<<8)|bytes[i+6];type="image/jpeg";break;}i+=length;}
        }
        if(type.Length==0||width<1||height<1||width>12000||height>12000||(long)width*height>40000000)throw new InvalidOperationException("Choose A Valid PNG Or JPEG Cover Photo (Up To 40 Megapixels).");
        return type;
    }
}
