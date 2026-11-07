using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace DIPlaybox.Preview;

public sealed class ModelDocument
{
    internal DI3MeshReader.Model Model;
    internal NativeVertexData Native;
    internal List<PreviewRig.Joint> Rig=new();
    internal Dictionary<int,BitmapSource> Images=new();
    public List<string> Diagnostics {get;}=new();
    public int VertexCount=>Model.positions.Length;
    public int TriangleCount=>Model.triangles.Sum(t=>t.Length/3);
    public int TexturedParts=>Images.Count;
    public int RigCount=>Rig.Count;
}

public static class PreviewApi
{
    public static TextureDecoder.Decoded PrepareTexture(byte[] bytes,byte[]? stream)
    {
        var result=TextureDecoder.Decode(bytes);
        if(stream==null)return result;
        try{var high=TextureDecoder.Decode(stream);if((long)high.Image.PixelWidth*high.Image.PixelHeight>(long)result.Image.PixelWidth*result.Image.PixelHeight)return high with{Format=high.Format+" · higher-resolution TSTREAM"};}
        catch(Exception e) when(e is InvalidDataException or NotSupportedException or IOException or ArgumentException or OverflowException)
        {return result with{Format=result.Format+" · base retained; stream: "+e.Message};}
        return result;
    }
    public static string[] BufferNames(byte[] oct)=>DI3MeshReader.Parse(oct).All("FileName")
        .Select(n=>n.value as string).Where(n=>n!=null&&(n.EndsWith(".vbuf",StringComparison.OrdinalIgnoreCase)||n.EndsWith(".ibuf",StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static ModelDocument LoadModel(string path,Func<string,byte[]?> resolve)
    {
        var d=new ModelDocument{Model=DI3MeshReader.Read(path)};
        if(d.VertexCount>500000||d.TriangleCount>1000000)throw new NotSupportedException("Interactive preview limit: 500,000 vertices / 1,000,000 triangles.");
        try{d.Rig=PreviewRig.Read(path);d.Native=NativeVertexData.Read(d.Model,d.Rig);d.Diagnostics.Add(d.Native.Status);}
        catch(Exception e) when(e is InvalidDataException or IOException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or OverflowException)
        {d.Diagnostics.Add("Source attributes/rig unavailable; computed normals used: "+e.Message);}
        try
        {
            string mtb=Path.ChangeExtension(path,".mtb");
            if(!File.Exists(mtb)){d.Diagnostics.Add("No companion MTB: plain preview colours.");return d;}
            if(new FileInfo(mtb).Length>64*1024*1024)throw new InvalidDataException("MTB exceeds 64 MiB preview limit.");
            var bundle=MaterialBundleReader.Read(File.ReadAllBytes(mtb));d.Diagnostics.Add(bundle.Status);
            var cache=new Dictionary<string,BitmapSource>();long pixels=0;
            for(int i=0;i<d.Model.slots.Count;i++)
            {
                string id=bundle.ColorTexture(d.Model.slots[i].reference);
                if(id==null){d.Diagnostics.Add($"Part {i}: unresolved colour sampler.");continue;}
                if(!cache.TryGetValue(id,out var image))
                {
                    byte[] bytes=resolve(id+".tbody")??resolve(id+".dds");
                    if(bytes==null){d.Diagnostics.Add($"Part {i}: missing texture {id}.");continue;}
                    try
                    {
                        image=TextureDecoder.Decode(bytes).Image;
                        try
                        {
                            var stream=resolve(id+".tstream");
                            if(stream!=null){var high=TextureDecoder.Decode(stream).Image;if((long)high.PixelWidth*high.PixelHeight>(long)image.PixelWidth*image.PixelHeight)image=high;}
                        }
                        catch(Exception ex) when(ex is InvalidDataException or NotSupportedException or IOException or ArgumentException or OverflowException)
                        {d.Diagnostics.Add(id+": base retained; stream unavailable: "+ex.Message);}
                        pixels+=(long)image.PixelWidth*image.PixelHeight;
                        if(pixels>32*1024*1024)throw new NotSupportedException("Model texture budget exceeded (128 MiB decoded).");
                        cache[id]=image;
                    }
                    catch(Exception e) when(e is InvalidDataException or NotSupportedException or IOException or ArgumentException or OverflowException)
                    {d.Diagnostics.Add($"Part {i}: {id}: {e.Message}");continue;}
                }
                d.Images[i]=image;d.Diagnostics.Add($"Part {i}: {id}, {image.PixelWidth} × {image.PixelHeight}");
            }
        }
        catch(Exception e) when(e is InvalidDataException or IOException or ArgumentException or OverflowException)
        {d.Diagnostics.Add("MTB unavailable; plain colours retained: "+e.Message);}
        return d;
    }
    static void Show(Window window,long owner)
    {if(owner!=0)new WindowInteropHelper(window).Owner=(nint)owner;window.ShowDialog();}
    public static void ShowTexture(string name,TextureDecoder.Decoded texture,long owner)
    {
        var window=new Window{Title="DI Playbox — "+name,Width=1000,Height=760,Background=Brushes.DimGray};
        var root=new DockPanel();window.Content=root;
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(5)};DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        var info=new TextBlock{Text=$"{texture.Image.PixelWidth} × {texture.Image.PixelHeight} | {texture.Format}",Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5)};
        var image=new Image{Source=texture.Image,Stretch=Stretch.Uniform};
        var scroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=image};
        var fit=new Button{Content="Fit",Margin=new Thickness(4)};fit.Click+=(_,_)=>{image.Width=double.NaN;image.Height=double.NaN;scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;};
        var actual=new Button{Content="1:1",Margin=new Thickness(4)};actual.Click+=(_,_)=>{scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;image.Width=texture.Image.PixelWidth;image.Height=texture.Image.PixelHeight;};
        var save=new Button{Content="Export PNG copy",Margin=new Thickness(4)};
        save.Click+=(_,_)=>
        {
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter="PNG image|*.png",FileName=Path.GetFileNameWithoutExtension(name)+"_preview.png"};
            if(dialog.ShowDialog(window)!=true)return;
            try{var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(texture.Image));using var output=new FileStream(dialog.FileName,FileMode.CreateNew);encoder.Save(output);}
            catch(Exception e){MessageBox.Show(window,e.Message,"Export failed");}
        };
        bar.Children.Add(fit);bar.Children.Add(actual);bar.Children.Add(save);bar.Children.Add(info);root.Children.Add(scroll);
        scroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
        Show(window,owner);
    }
    public static void ShowModel(string name,ModelDocument data,long owner)=>Show(new MeshWindow(name,data),owner);
    public static void SaveModelSnapshot(ModelDocument data,string path)
    {
        var window=new MeshWindow("Preview validation",data);
        try
        {
            var visual=(FrameworkElement)window.Content;
            visual.Measure(new Size(1150,800));visual.Arrange(new Rect(0,0,1150,800));visual.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1150,800,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=new FileStream(path,FileMode.CreateNew);encoder.Save(file);
        }
        finally{window.Close();}
    }
}

internal sealed class MeshWindow : Window
{
    readonly ModelDocument data;
    readonly Viewport3D viewport=new();
    readonly Model3DGroup scene=new();
    readonly PerspectiveCamera camera=new(){FieldOfView=40,NearPlaneDistance=.001,FarPlaneDistance=100000};
    double yaw=.6,pitch=.2,distance,extent;
    Point3D center;
    Point? last;
    bool textures=true,rig;
    public MeshWindow(string label,ModelDocument document)
    {
        data=document;Title="DI Playbox model — "+label;Width=1150;Height=800;Background=Brushes.Black;
        var root=new DockPanel();Content=root;
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(5)};DockPanel.SetDock(bar,Dock.Top);root.Children.Add(bar);
        var reset=new Button{Content="Reset view",Margin=new Thickness(4)};bar.Children.Add(reset);
        var textured=new CheckBox{Content="Textures",IsChecked=true,Foreground=Brushes.White,Margin=new Thickness(8)};
        var skeleton=new CheckBox{Content="Show skeleton",Foreground=Brushes.White,Margin=new Thickness(8),IsEnabled=data.Rig.Count>0};bar.Children.Add(textured);bar.Children.Add(skeleton);
        bar.Children.Add(new TextBlock{Text=$"{data.VertexCount:N0} vertices · {data.TriangleCount:N0} triangles · {data.TexturedParts}/{data.Model.slots.Count} textured parts | Drag: orbit · Wheel: zoom",Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center});
        var diagnostics=new TextBox{Text=string.Join(Environment.NewLine,data.Diagnostics),IsReadOnly=true,Height=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(diagnostics,Dock.Bottom);root.Children.Add(diagnostics);
        var border=new Border{Background=Brushes.Black,Child=viewport};root.Children.Add(border);
        var lo=data.Model.positions.Aggregate(System.Numerics.Vector3.Min);var hi=data.Model.positions.Aggregate(System.Numerics.Vector3.Max);
        center=new((lo.X+hi.X)/2,(lo.Y+hi.Y)/2,(lo.Z+hi.Z)/2);extent=Math.Max((hi-lo).Length(),.001);
        viewport.Camera=camera;viewport.Children.Add(new ModelVisual3D{Content=scene});
        viewport.SizeChanged+=(_,_)=>Reset();
        reset.Click+=(_,_)=>Reset();textured.Click+=(_,_)=>{textures=textured.IsChecked==true;Build();};skeleton.Click+=(_,_)=>{rig=skeleton.IsChecked==true;Build();};
        border.MouseLeftButtonDown+=(_,e)=>{last=e.GetPosition(border);border.CaptureMouse();};
        border.MouseLeftButtonUp+=(_,_)=>{last=null;border.ReleaseMouseCapture();};
        border.LostMouseCapture+=(_,_)=>last=null;
        border.MouseMove+=(_,e)=>{if(last is not Point previous)return;var now=e.GetPosition(border);yaw-=(now.X-previous.X)*.01;pitch=Math.Clamp(pitch+(now.Y-previous.Y)*.01,-1.45,1.45);last=now;UpdateCamera();};
        border.MouseWheel+=(_,e)=>{distance=Math.Clamp(distance*Math.Pow(.85,e.Delta/120.0),extent*.15,extent*20);UpdateCamera();};
        Build();Reset();
    }
    void Reset(){yaw=.6;pitch=.2;distance=extent*1.6*Math.Max(1,viewport.ActualWidth/Math.Max(1,viewport.ActualHeight));UpdateCamera();}
    void UpdateCamera()
    {var pos=new Point3D(center.X+distance*Math.Cos(pitch)*Math.Sin(yaw),center.Y+distance*Math.Sin(pitch),center.Z+distance*Math.Cos(pitch)*Math.Cos(yaw));camera.Position=pos;camera.LookDirection=center-pos;camera.UpDirection=new Vector3D(0,1,0);}
    void Build()
    {
        scene.Children.Clear();scene.Children.Add(new AmbientLight(Color.FromRgb(100,100,100)));scene.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-1,-2,-3)));
        var positions=new Point3DCollection(data.Model.positions.Select(p=>new Point3D(p.X,p.Y,p.Z)));
        // The mesh reader normalizes V for Unity; WPF uses the original image orientation.
        var uv=new PointCollection(data.Model.uvs.Select(p=>new Point(p.X,1-p.Y)));
        var normal=data.Native?.Normals?.Length==data.VertexCount?new Vector3DCollection(data.Native.Normals.Select(n=>new Vector3D(n.X,n.Y,n.Z))):null;
        for(int i=0;i<data.Model.triangles.Count;i++)
        {
            var mesh=new MeshGeometry3D{Positions=positions,TextureCoordinates=uv,TriangleIndices=new Int32Collection(data.Model.triangles[i])};
            if(normal!=null)mesh.Normals=normal;
            Brush brush=textures&&data.Images.TryGetValue(i,out var image)?new ImageBrush(image){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1),TileMode=TileMode.Tile}:new SolidColorBrush(Color.FromRgb((byte)(140+i*31%70),(byte)(155+i*19%60),(byte)(165+i*13%50)));
            var material=new DiffuseMaterial(brush);scene.Children.Add(new GeometryModel3D(mesh,material){BackMaterial=material});
        }
        if(rig)foreach(var joint in data.Rig)
        {
            if(joint.Parent<0||joint.Parent>=data.Rig.Count)continue;
            var a=joint.Position;var b=data.Rig[joint.Parent].Position;
            var start=new Point3D(a.X,a.Y,a.Z);var end=new Point3D(b.X,b.Y,b.Z);var direction=end-start;
            var cross=Vector3D.CrossProduct(direction,new Vector3D(0,0,1));if(cross.Length<1e-8)cross=Vector3D.CrossProduct(direction,new Vector3D(0,1,0));if(cross.Length<1e-8)continue;
            cross.Normalize();cross*=extent*.0015;
            var mesh=new MeshGeometry3D{Positions=new Point3DCollection{start-cross,start+cross,end+cross,end-cross},TriangleIndices=new Int32Collection{0,1,2,0,2,3}};
            var mat=new EmissiveMaterial(Brushes.Lime);scene.Children.Add(new GeometryModel3D(mesh,mat){BackMaterial=mat});
        }
    }
}
