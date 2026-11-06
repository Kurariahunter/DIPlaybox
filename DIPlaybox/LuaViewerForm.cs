using System.Text;
using DIPlaybox.Core;

namespace DIPlaybox;

public sealed class LuaViewerForm : Form
{
    private readonly byte[] bytes;
    private readonly string? originalPath;
    private readonly string name;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly Label notice = new() { Dock = DockStyle.Top, Height = 72, Padding = new Padding(8), AutoEllipsis = true };
    private readonly TextBox search = new() { Width = 190, PlaceholderText = "Find text (Enter = next)" };
    private readonly Dictionary<TabPage, string> texts = new();
    private readonly Button external = new() { Text = "Decompile with unluac…", AutoSize = true };
    private bool busy;
    private const int PreviewLimit = 1_000_000;
    private static string Settings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIPlaybox", "unluac-path.txt");

    public LuaViewerForm(string name, byte[] bytes, string? originalPath = null)
    {
        this.bytes = bytes; this.name = name; this.originalPath = originalPath;
        Text = "DI Playbox — Lua Viewer — " + name; Width = 1150; Height = 800;
        StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4), AutoScroll = true };
        var copy = new Button { Text = "Copy view", AutoSize = true };
        copy.Click += (_, _) => { if (tabs.SelectedTab != null && texts.TryGetValue(tabs.SelectedTab, out string? text)) Clipboard.SetText(text); };
        var save = new Button { Text = "Export text copy…", AutoSize = true };
        save.Click += (_, _) => Export();
        var find = new Button { Text = "Find next", AutoSize = true }; find.Click += (_, _) => FindNext();
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
        external.Click += async (_, _) => await DecompileExternal();
        external.Enabled = bytes.Length >= 4 && bytes[0] == 27 && bytes[1] == 'L' && bytes[2] == 'u' && bytes[3] == 'a';
        bar.Controls.AddRange(new Control[] { copy, save, search, find, external });
        Controls.Add(tabs); Controls.Add(notice); Controls.Add(bar);
        tabs.SelectedIndexChanged += async (_, _) => await RenderSelected();
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.F) { search.Focus(); e.SuppressKeyPress = true; } };
        Shown += async (_, _) => await Analyze();
    }

    private async Task Analyze()
    {
        SetBusy(true); notice.Text = "Decoding without executing Lua…";
        try
        {
            var document = await Task.Run(() => LuaDecoder.Analyze(bytes));
            if (IsDisposed) return;
            notice.Text = document.Kind + " — " + document.Notice;
            AddTab("Lua", document.Source); AddTab("Bytecode", document.Instructions); AddTab("Constants", document.Constants);
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            notice.Text = "Decode unavailable: " + error.Message;
            AddTab("Decode status", "-- " + error.Message + "\n-- Original bytes remain unchanged. The Hex tab shows a bounded preview.");
        }
        finally
        {
            if (!IsDisposed) { AddTab("Hex (first 64 KiB)", Hex(bytes)); SetBusy(false); await RenderSelected(); }
        }
    }
    private void SetBusy(bool value) { busy = value; UseWaitCursor = value; external.Enabled = !value && bytes.Length >= 4 && bytes[0] == 27 && bytes[1] == 'L'; }
    private void AddTab(string title, string text)
    {
        var page = new TabPage(title);
        page.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, DetectUrls = false, BackColor = Color.FromArgb(27, 30, 37), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 10), BorderStyle = BorderStyle.None, HideSelection = false });
        texts[page] = text; tabs.TabPages.Add(page);
    }
    private async Task RenderSelected()
    {
        var page = tabs.SelectedTab;
        if (page == null || page.Tag is true || !texts.TryGetValue(page, out string? full)) return;
        page.Tag = true;
        string preview = full.Length > PreviewLimit ? full[..PreviewLimit] + "\n-- Preview truncated at 1,000,000 characters. Copy/Export preserves the complete decoded view.\n" : full;
        var box = (RichTextBox)page.Controls[0];
        try
        {
            string rtf = await Task.Run(() => LuaSyntax.ToRtf(preview));
            if (!IsDisposed && !box.IsDisposed) box.Rtf = rtf;
        }
        catch (Exception e) { if (!box.IsDisposed) box.Text = "Highlighting unavailable: " + e.Message + "\n" + preview; }
    }
    private void FindNext()
    {
        if (tabs.SelectedTab?.Controls[0] is not RichTextBox box || string.IsNullOrEmpty(search.Text)) return;
        int start = Math.Min(box.TextLength, box.SelectionStart + box.SelectionLength);
        int found = box.Find(search.Text, start, RichTextBoxFinds.None);
        if (found < 0) found = box.Find(search.Text, 0, RichTextBoxFinds.None);
        if (found >= 0) { box.Focus(); box.ScrollToCaret(); }
        else notice.Text = "Text not found in the displayed preview.";
    }
    private void Export()
    {
        if (tabs.SelectedTab == null || !texts.TryGetValue(tabs.SelectedTab, out string? text)) return;
        string basename = name.Split(new[] { " :: " }, StringSplitOptions.None).Last().Replace('\\', '/').Split('/').Last();
        using var dialog = new SaveFileDialog { Filter = "Lua/text copy (*.lua)|*.lua|Text file (*.txt)|*.txt", FileName = Path.GetFileNameWithoutExtension(basename) + "_view.lua", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (originalPath != null && Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a different filename to preserve the original source/archive.");
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false));
            notice.Text = "Exported a text copy. No bytecode recompilation or archive replacement was performed.";
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Export error"); }
    }
    private async Task DecompileExternal()
    {
        if (busy) return;
        using var dialog = new OpenFileDialog { Filter = "Trusted unluac JAR (*.jar)|*.jar", Title = "Select your unluac.jar (Java required)" };
        if (File.Exists(Settings)) { string saved = File.ReadAllText(Settings); if (File.Exists(saved)) dialog.FileName = saved; }
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true); notice.Text = "Running the selected unluac decompiler; Lua is not executed…";
        try
        {
            string source = await ExternalLuaDecompiler.RunAsync(bytes, dialog.FileName);
            if (IsDisposed) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Settings)!); File.WriteAllText(Settings, dialog.FileName);
            AddTab("unluac source", source); tabs.SelectedIndex = tabs.TabCount - 1;
            notice.Text = "External unluac output. Review before use; decompilation is not proof of a lossless round trip. Encrypted strings are not automatically decrypted.";
        }
        catch (Exception error) { if (!IsDisposed) notice.Text = "External decompiler failed: " + error.Message; }
        finally { if (!IsDisposed) SetBusy(false); }
    }
    private static string Hex(byte[] bytes)
    {
        var result = new StringBuilder(); int limit = Math.Min(bytes.Length, 65536);
        for (int i = 0; i < limit; i += 16)
        {
            result.Append(i.ToString("X8")).Append("  ");
            for (int j = i; j < Math.Min(i + 16, limit); j++) result.Append(bytes[j].ToString("X2")).Append(' ');
            result.AppendLine();
        }
        if (bytes.Length > limit) result.AppendLine("-- Hex preview truncated; original bytes are unchanged.");
        return result.ToString();
    }
}
