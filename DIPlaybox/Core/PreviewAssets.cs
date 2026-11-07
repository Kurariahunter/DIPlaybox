using DIPlaybox.Preview;

namespace DIPlaybox.Core;

public sealed class ModelStage : IDisposable
{
    private readonly string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DIPlayboxPreview_"+Guid.NewGuid().ToString("N"));
    public string ModelPath {get;}
    public ModelStage(ArchiveService archive,ArchiveItem model)
    {
        string name=System.IO.Path.GetFileName(model.Name);
        if(name.Length==0||name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Invalid model name.");
        ModelPath=System.IO.Path.Combine(root,name);
        try
        {
            if(model.Size>64*1024*1024)throw new InvalidDataException("OCT limit: 64 MiB.");
            byte[] oct=archive.Read(model);var buffers=PreviewApi.BufferNames(oct);
            if(buffers.Length>128)throw new InvalidDataException("Too many companion buffers.");
            string folder=model.Name[..(model.Name.LastIndexOf('/')+1)];
            var companions=new List<ArchiveItem>();long total=oct.Length;
            foreach(string buffer in buffers)
            {
                if(buffer!=System.IO.Path.GetFileName(buffer)||buffer.Contains("..")||buffer.Contains('/')||buffer.Contains('\\'))throw new InvalidDataException("Unsafe companion filename.");
                var matches=archive.Items.Where(i=>i.Name.Equals(folder+buffer,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(matches.Length!=1)throw new InvalidDataException("Missing or ambiguous companion buffer: "+folder+buffer);
                total+=matches[0].Size;if(total>256L*1024*1024)throw new InvalidDataException("Staged model exceeds 256 MiB.");
                companions.Add(matches[0]);
            }
            var mtbs=archive.Items.Where(i=>i.Name.Equals(folder+System.IO.Path.ChangeExtension(name,".mtb"),StringComparison.OrdinalIgnoreCase)).ToArray();
            if(mtbs.Length==1){if(mtbs[0].Size>64*1024*1024)throw new InvalidDataException("MTB exceeds preview limit.");companions.Add(mtbs[0]);}
            Directory.CreateDirectory(root);File.WriteAllBytes(ModelPath,oct);
            foreach(var item in companions)File.WriteAllBytes(System.IO.Path.Combine(root,System.IO.Path.GetFileName(item.Name)),archive.Read(item));
        }
        catch{Dispose();throw;}
    }
    public void Dispose()
    {
        if(!Directory.Exists(root))return;
        // Only this instance's GUID directory is removed, never a caller-supplied path.
        if(System.IO.Path.GetDirectoryName(root)!=System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetTempPath())||!System.IO.Path.GetFileName(root).StartsWith("DIPlayboxPreview_",StringComparison.Ordinal))return;
        try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}

public static class PreviewAssets
{
    static readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DIPlaybox","preview-assets.txt");
    public static string Root {get;private set;}=Load();
    static string Load(){try{return File.Exists(settings)?File.ReadAllText(settings):"";}catch(IOException){return "";}}
    public static void SetRoot(string root){Root=Path.GetFullPath(root);Directory.CreateDirectory(Path.GetDirectoryName(settings)!);File.WriteAllText(settings,Root);}
    public static byte[]? Resolve(string filename,string? modelPath,ArchiveService? current,string? memberFolder)
    {
        string id=Path.GetFileNameWithoutExtension(filename);
        if(filename!=Path.GetFileName(filename)||id.Length!=16||!id.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid texture identifier.");
        string ext=Path.GetExtension(filename).ToLowerInvariant();
        if(ext is not (".tbody" or ".tstream" or ".dds"))throw new InvalidDataException("Invalid texture extension.");
        if(current!=null)
        {
            string scoped=(memberFolder??"")+"textures/"+filename;
            var matches=current.Items.Where(i=>i.Name.Equals(scoped,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length==0)matches=current.Items.Where(i=>i.Name.Equals("textures/"+filename,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length==0)matches=current.Items.Where(i=>Path.GetFileName(i.Name).Equals(filename,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length==1)return Read(current,matches[0]);
            if(matches.Length>1)throw new InvalidDataException("Ambiguous in-archive texture: "+filename);
        }
        var roots=new List<string>();if(modelPath!=null)roots.Add(Path.GetDirectoryName(modelPath)!);
        if(!string.IsNullOrWhiteSpace(Root)){roots.Add(Root);roots.Add(Path.Combine(Root,"assets"));}
        foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach(string path in new[]{Path.Combine(root,filename),Path.Combine(root,"textures",filename)})
                if(File.Exists(path)){if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("Texture input limit: 64 MiB.");return File.ReadAllBytes(path);}
            string shard=Path.Combine(root,"textures",id[..2]+".zip");
            if(!File.Exists(shard))continue;
            using var archive=new ArchiveService(shard);
            var entries=archive.Items.Where(i=>Path.GetFileName(i.Name).Equals(filename,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(entries.Length==1)return Read(archive,entries[0]);
            if(entries.Length>1)throw new InvalidDataException("Ambiguous texture in "+shard);
        }
        return null;
    }
    static byte[] Read(ArchiveService archive,ArchiveItem item)
    {if(item.Size>64*1024*1024)throw new InvalidDataException("Texture input limit: 64 MiB.");return archive.Read(item);}
}
