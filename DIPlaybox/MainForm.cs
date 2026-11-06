using System.IO.Compression;
using DIPlaybox.Core;

namespace DIPlaybox;

public sealed class MainForm : Form
{
    private readonly ListView listing = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, GridLines = true };
    private readonly TextBox filter = new() { Width = 200, PlaceholderText = "Filter filenames..." };
    private readonly ToolStripStatusLabel status = new("Open a ZIP or Disney Infinity archive.");
    private ArchiveService? archive;

    public MainForm(string? startup)
    {
        Text = "DI Playbox — ZIP & Disney Infinity Archive Manager";
        Width = 1060; Height = 680; StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) Open(paths[0]); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(5), AutoSize = true, WrapContents = true };
        AddButton(bar, "Open", (_, _) => PickOpen());
        AddButton(bar, "Open Lua", (_, _) => PickLua());
        AddButton(bar, "View Lua", (_, _) => ViewSelectedLua());
        AddButton(bar, "New ZIP", (_, _) => NewZip());
        AddButton(bar, "Add Files", (_, _) => AddFiles());
        AddButton(bar, "Extract Selected", (_, _) => Extract(true));
        AddButton(bar, "Extract All", (_, _) => Extract(false));
        AddButton(bar, "Delete Selected", (_, _) => DeleteSelected());
        AddButton(bar, "Test", (_, _) => TestArchive());
        bar.Controls.Add(filter);
        filter.TextChanged += (_, _) => RefreshList();
        listing.Columns.Add("Name", 550);
        listing.Columns.Add("Size", 125, HorizontalAlignment.Right);
        listing.Columns.Add("Packed", 125, HorizontalAlignment.Right);
        listing.Columns.Add("Compression", 130);
        listing.DoubleClick += (_, _) => { var items = Selected(); if (items.Count == 1 && IsLuaName(items[0].Name)) ViewSelectedLua(); else Extract(true); };
        var bottom = new StatusStrip(); bottom.Items.Add(status);
        Controls.Add(listing); Controls.Add(bar); Controls.Add(bottom);
        FormClosed += (_, _) => archive?.Dispose();
        if (!string.IsNullOrWhiteSpace(startup)) Shown += (_, _) => Open(startup);
    }

    private static void AddButton(FlowLayoutPanel p, string title, EventHandler click)
    {
        var b = new Button { Text = title, AutoSize = true, Height = 29, Margin = new Padding(2) };
        b.Click += click; p.Controls.Add(b);
    }
    private void PickOpen()
    {
        using var d = new OpenFileDialog { Filter = "Archives and Lua (*.zip;*.lua;*.luac;*.dnax;*.sx)|*.zip;*.lua;*.luac;*.dnax;*.sx|All files (*.*)|*.*" };
        if (d.ShowDialog(this) == DialogResult.OK) Open(d.FileName);
    }
    private void Open(string path)
    {
        if (IsLuaName(path)) { OpenLooseLua(path); return; }
        try
        {
            archive?.Dispose(); archive = null;
            archive = new ArchiveService(path);
            filter.Clear(); RefreshList();
            Text = $"DI Playbox — {Path.GetFileName(path)}";
            status.Text = $"{archive.Kind} | {archive.Items.Count:N0} entries | {path}";
        }
        catch (Exception ex) { Error(ex); }
    }
    private static bool IsLuaName(string path) => new[] { ".lua", ".luac", ".dnax", ".sx" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private void PickLua()
    {
        using var dialog = new OpenFileDialog { Filter = "Lua / configuration (*.lua;*.luac;*.dnax;*.sx)|*.lua;*.luac;*.dnax;*.sx|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenLooseLua(dialog.FileName);
    }
    private void OpenLooseLua(string path)
    {
        try
        {
            if (new FileInfo(path).Length > LuaDecoder.MaximumBytes) throw new InvalidDataException("Lua viewer limit is 16 MiB per file.");
            new LuaViewerForm(Path.GetFileName(path), File.ReadAllBytes(path), path).Show(this);
        }
        catch (Exception error) { Error(error); }
    }
    private void ViewSelectedLua()
    {
        if (archive == null) { PickLua(); return; }
        var selected = Selected();
        if (selected.Count != 1) { MessageBox.Show(this, "Select one Lua/configuration entry to view."); return; }
        try
        {
            if (selected[0].Name.EndsWith('/') || selected[0].Size > LuaDecoder.MaximumBytes) throw new InvalidDataException("Select a file no larger than 16 MiB.");
            byte[] bytes = archive.Read(selected[0]);
            new LuaViewerForm(Path.GetFileName(archive.PathName) + " :: " + selected[0].Name, bytes, archive.PathName).Show(this);
        }
        catch (Exception error) { Error(error); }
    }

    private void RefreshList()
    {
        listing.BeginUpdate(); listing.Items.Clear();
        if (archive != null)
        {
            foreach (var item in archive.Items)
            {
                if (!item.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase)) continue;
                var row = new ListViewItem(item.Name) { Tag = item };
                row.SubItems.Add(item.Size.ToString("N0"));
                row.SubItems.Add(item.PackedSize.ToString("N0"));
                row.SubItems.Add(item.Method);
                listing.Items.Add(row);
            }
        }
        listing.EndUpdate();
    }
    private List<ArchiveItem> Selected() => listing.SelectedItems.Cast<ListViewItem>().Select(i => (ArchiveItem)i.Tag!).ToList();
    private void Extract(bool selectedOnly)
    {
        if (archive == null) return;
        var items = selectedOnly ? Selected() : archive.Items.ToList();
        if (items.Count == 0) { MessageBox.Show(this, "No entries selected."); return; }
        using var d = new FolderBrowserDialog { Description = "Choose extraction destination" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            archive.Extract(items, d.SelectedPath);
            status.Text = $"Extracted {items.Count:N0} entries to {d.SelectedPath}";
            MessageBox.Show(this, "Extraction complete.", "DI Playbox");
        }
        catch (Exception ex) { Error(ex); }
        finally { UseWaitCursor = false; }
    }
    private void TestArchive()
    {
        if (archive == null) return;
        try
        {
            UseWaitCursor = true;
            foreach (var item in archive.Items.Where(x => !x.Name.EndsWith('/'))) archive.Read(item);
            MessageBox.Show(this, $"Read and validated {archive.Items.Count} entries.", "Archive test");
        }
        catch (Exception ex) { Error(ex); }
        finally { UseWaitCursor = false; }
    }
    private void NewZip()
    {
        using var d = new SaveFileDialog { Filter = "ZIP archive (*.zip)|*.zip", DefaultExt = "zip" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try { using (var z = ZipFile.Open(d.FileName, ZipArchiveMode.Create)) { } Open(d.FileName); }
        catch (Exception ex) { Error(ex); }
    }
    private void AddFiles()
    {
        if (!RequireWritableZip()) return;
        using var d = new OpenFileDialog { Multiselect = true, Title = "Add files to ZIP" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        string path = archive!.PathName;
        archive.Dispose(); archive = null;
        try
        {
            using (var z = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                foreach (var file in d.FileNames)
                {
                    string name = Path.GetFileName(file);
                    z.GetEntry(name)?.Delete();
                    z.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                }
            }
        }
        catch (Exception ex) { Error(ex); }
        finally { Open(path); }
    }
    private void DeleteSelected()
    {
        if (!RequireWritableZip()) return;
        var selected = Selected();
        if (selected.Count == 0) return;
        if (MessageBox.Show(this, $"Delete {selected.Count} entries?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        string path = archive!.PathName;
        archive.Dispose(); archive = null;
        try
        {
            using (var z = ZipFile.Open(path, ZipArchiveMode.Update))
                foreach (var item in selected) z.GetEntry(item.Name)?.Delete();
        }
        catch (Exception ex) { Error(ex); }
        finally { Open(path); }
    }
    private bool RequireWritableZip()
    {
        if (archive == null) { MessageBox.Show(this, "Open or create an archive first."); return false; }
        if (archive.Kind == ArchiveKind.StandardZip) return true;
        MessageBox.Show(this, "Disney Infinity archives are read-only to avoid corrupting game assets. Extract or create a standard ZIP instead.");
        return false;
    }
    private void Error(Exception ex)
    {
        status.Text = ex.Message;
        MessageBox.Show(this, ex.Message, "DI Playbox error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
