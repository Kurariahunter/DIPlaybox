using System.Text;
using DIPlaybox.Core;
using AssetWiiUdio;

namespace DIPlaybox;

public sealed class ResourceViewerForm : Form
{
    public TreeView ResourceTree { get; } = new() { Dock=DockStyle.Fill, HideSelection=false };
    public ResourceViewerForm(string name, byte[] data)
    {
        Text="DI Playbox — "+name; Width=1100; Height=740;
        var status=new TextBox {Dock=DockStyle.Top, Multiline=true, ReadOnly=true, Height=55};
        var detail=new TextBox {Dock=DockStyle.Bottom, Multiline=true, ReadOnly=true, Height=115, ScrollBars=ScrollBars.Both};
        var tabs=new TabControl {Dock=DockStyle.Fill};
        var treeTab=new TabPage("Structure"); treeTab.Controls.Add(ResourceTree); treeTab.Controls.Add(detail);
        tabs.TabPages.Add(treeTab);
        var hex=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Both,Font=new Font("Consolas",10)};
        var text=new StringBuilder();
        for(int i=0;i<Math.Min(data.Length,65536);i+=16) text.AppendLine($"{i:X8}  "+Convert.ToHexString(data.AsSpan(i,Math.Min(16,data.Length-i))));
        hex.Text="First 64 KiB maximum. Original resource is read-only.\r\n"+text;
        var hexTab=new TabPage("Hex");hexTab.Controls.Add(hex);tabs.TabPages.Add(hexTab);
        var bar=new FlowLayoutPanel {Dock=DockStyle.Top,Height=36};
        var search=new TextBox {Width=260,PlaceholderText="Find name, texture ID, alias…"};
        var find=new Button {Text="Find next",AutoSize=true};
        var collapse=new Button {Text="Collapse all",AutoSize=true};
        bar.Controls.Add(search);bar.Controls.Add(find);bar.Controls.Add(collapse);
        collapse.Click+=(_,_)=>ResourceTree.CollapseAll();
        find.Click+=(_,_)=>
        {
            if(string.IsNullOrWhiteSpace(search.Text))return;
            var nodes=Flatten(ResourceTree.Nodes).ToList();int start=nodes.IndexOf(ResourceTree.SelectedNode!)+1;
            for(int i=0;i<nodes.Count;i++) {var n=nodes[(start+i)%nodes.Count];if(n.Text.Contains(search.Text,StringComparison.OrdinalIgnoreCase)){ResourceTree.SelectedNode=n;n.EnsureVisible();break;}}
        };
        ResourceTree.AfterSelect+=(_,e)=>detail.Text=e.Node?.Tag as string ?? e.Node?.Text ?? "";
        Controls.Add(tabs);Controls.Add(bar);Controls.Add(status);
        ResourceTree.BeginUpdate();
        try
        {
            if(name.EndsWith(".mtb",StringComparison.OrdinalIgnoreCase))
            {
                var bundle=MaterialBundleReader.Read(data);
                status.Text=$"MTB | {(bundle.Big?"big":"little")}-endian | {bundle.Status}";
                var sections=ResourceTree.Nodes.Add("Sections");
                foreach(var s in bundle.Sections) sections.Nodes.Add($"{s.Tag} @ 0x{s.Offset:X} — {s.Size:N0} bytes, version {s.Version}, count {s.Count}");
                var textures=ResourceTree.Nodes.Add($"Texture references ({bundle.Textures.Count})");
                for(int i=0;i<bundle.Textures.Count;i++) textures.Nodes.Add($"[{i}] {bundle.Textures[i]}");
                var materials=ResourceTree.Nodes.Add($"Materials ({bundle.Materials.Count} decoded)");
                foreach(var m in bundle.Materials)
                {
                    var node=materials.Nodes.Add($"Material {m.Index} — {m.Shader}");
                    node.Nodes.Add($"Record @ 0x{m.Offset:X}, {m.Size} bytes");
                    node.Nodes.Add("UUID: "+m.Uuid);node.Nodes.Add("Hash bytes: "+m.Hash);
                    for(int i=0;i<m.TextureIndices.Length;i++)
                    {
                        int index=m.TextureIndices[i];string id=index<0?"unbound":bundle.Textures[index];
                        string sampler=m.SamplerNames is { } names&&i<names.Length?names[i]:$"slot {i}";
                        node.Nodes.Add($"{sampler} → [{index}] {id}"+(i==m.ColorSlot?" (reviewed colour slot)":""));
                    }
                }
                ResourceTree.Nodes.Add("Unknown shader layouts remain unresolved. Texture images are not embedded by this viewer.");
            }
            else
            {
                var doc=BentDocument.Read(data);status.Text=doc.Status;
                var stack=new List<TreeNode>();
                foreach(var r in doc.Records)
                {
                    var node=new TreeNode(r.Name+(r.Value.Length>0?" = "+Short(r.Value):"")) {Tag=$"Offset: 0x{r.Offset:X}\r\nDepth: {r.Depth}; type: 0x{r.Type:X}\r\n{r.Name}\r\n{r.Value}"};
                    while(stack.Count>=r.Depth)stack.RemoveAt(stack.Count-1);
                    if(stack.Count==0)ResourceTree.Nodes.Add(node);else stack[^1].Nodes.Add(node);
                    stack.Add(node);
                }
            }
        }
        catch(Exception ex) when(ex is InvalidDataException or IOException or ArgumentException or OverflowException)
        {status.Text="Inspection unavailable: "+ex.Message;ResourceTree.Nodes.Add("Unsupported or malformed resource; inspect Hex for original bytes.");}
        finally {ResourceTree.CollapseAll();ResourceTree.EndUpdate();}
    }
    private static string Short(string text)=>text.Length>240?text[..240]+"…":text;
    private static IEnumerable<TreeNode> Flatten(TreeNodeCollection nodes)
    {foreach(TreeNode node in nodes){yield return node;foreach(var child in Flatten(node.Nodes))yield return child;}}
}

