using System.Diagnostics;
using System.Text.Json;

namespace PopFs.Gui;

internal sealed class MainForm : Form
{
    private static readonly string[] Archives = ["DataPC_LOD.forge", "DataPC.forge", "DataPC_interface.forge"];
    private readonly TextBox game = new() { Dock = DockStyle.Fill };
    private readonly TextBox project = new() { Dock = DockStyle.Fill };
    private readonly TextBox lzo = new() { Dock = DockStyle.Fill };
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly CheckBox uppercase = new() { Text = "Tự viết hoa text khi nhập (cho bộ font hiện tại)", Checked = true, AutoSize = true };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9) };
    private readonly Button export = new() { Text = "1  Xuất text + font", AutoSize = true, Padding = new Padding(12, 8, 12, 8) };
    private readonly Button open = new() { Text = "2  Mở thư mục để sửa", AutoSize = true, Padding = new Padding(12, 8, 12, 8) };
    private readonly Button import = new() { Text = "3  Nhập và cài vào game", AutoSize = true, Padding = new Padding(12, 8, 12, 8) };
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PopFsGui", "settings.json");

    public MainForm()
    {
        Text = "Prince of Persia Forgotten Sands — Việt hóa";
        ClientSize = new Size(900, 660);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var paths = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        foreach (var _ in Enumerable.Range(0, 4)) paths.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        AddPath(paths, 0, "Thư mục game", game, "Chọn...", () => PickFolder(game));
        AddPath(paths, 1, "Thư mục dự án", project, "Chọn...", () => PickFolder(project));
        AddPath(paths, 2, "LZO DLL", lzo, "Chọn...", PickLzo);
        paths.Controls.Add(new Label { Text = "Ngôn ngữ", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 3);
        language.Items.AddRange(["English", "French", "Spanish", "German", "Italian", "Dutch"]);
        language.SelectedItem = "English";
        paths.Controls.Add(language, 1, 3);
        root.Controls.Add(paths, 0, 0);

        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text =
            "Xuất ra TXT, PNG/DDS và TSV. Sửa TXT trong texts; sửa atlas trong fonts/atlases; " +
            "sửa glyph/UV trong fonts/metrics. Với mỗi atlas, chỉ sửa PNG hoặc DDS. " +
            "Nút nhập sẽ đóng gói và cài archive vào game sau khi sao lưu.", AutoSize = false }, 0, 1);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false };
        options.Controls.Add(uppercase);
        root.Controls.Add(options, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false };
        buttons.Controls.AddRange([export, open, import]);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(log, 0, 4);

        export.Click += async (_, _) => await RunSafely(ExportAsync);
        import.Click += async (_, _) => await RunSafely(ImportAsync);
        open.Click += (_, _) =>
        {
            if (Directory.Exists(project.Text))
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { project.Text } });
            else MessageBox.Show(this, "Hãy xuất dự án trước.", "Chưa có thư mục");
        };
        LoadSettings();
    }

    private static void AddPath(TableLayoutPanel grid, int row, string label, TextBox input,
        string buttonText, Action browse)
    {
        grid.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        grid.Controls.Add(input, 1, row);
        var button = new Button { Text = buttonText, Dock = DockStyle.Fill };
        button.Click += (_, _) => browse();
        grid.Controls.Add(button, 2, row);
    }

    private void PickFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { ShowNewFolderButton = true };
        if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void PickLzo()
    {
        using var dialog = new OpenFileDialog { Filter = "LZO library|lzo2_w64.dll|DLL files|*.dll" };
        if (File.Exists(lzo.Text)) dialog.FileName = lzo.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) lzo.Text = dialog.FileName;
    }

    private void LoadSettings()
    {
        var steam = @"C:\Program Files (x86)\Steam\steamapps\common\Prince of Persia Forgotten Sands";
        game.Text = Directory.Exists(steam) ? steam : "";
        project.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PoP Forgotten Sands Translation");
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "lzo2_w64.dll"),
                     Path.Combine(Environment.CurrentDirectory, "lzo2_w64.dll"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         "Downloads", "POP-FORGOTTEN-SAND", "_research", "delutto-data", "lzo2_w64.dll") })
            if (File.Exists(path)) { lzo.Text = path; break; }
        if (!File.Exists(settingsPath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(settingsPath));
            if (saved is null) return;
            game.Text = saved.Game;
            project.Text = saved.Project;
            if (File.Exists(saved.Lzo)) lzo.Text = saved.Lzo;
            language.SelectedItem = saved.Language;
            if (language.SelectedIndex < 0) language.SelectedItem = "English";
        }
        catch (Exception ex) { Log($"Không đọc được thiết lập cũ: {ex.Message}"); }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new SavedSettings(
            game.Text, project.Text, lzo.Text, language.Text)));
    }

    private async Task RunSafely(Func<Task> action)
    {
        export.Enabled = import.Enabled = false;
        try { await action(); }
        catch (Exception ex)
        {
            Log("LỖI: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Không thể hoàn tất", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally { export.Enabled = import.Enabled = true; }
    }

    private async Task ExportAsync()
    {
        var gamePath = Path.GetFullPath(game.Text.Trim());
        foreach (var archive in Archives)
            if (!File.Exists(Path.Combine(gamePath, archive)))
                throw new FileNotFoundException($"Thiếu archive game: {archive}");
        if (Process.GetProcessesByName("Prince of Persia").Length != 0)
            throw new InvalidOperationException("Hãy thoát game trước khi xuất.");
        var baseProject = Path.GetFullPath(project.Text.Trim());
        var target = File.Exists(Path.Combine(baseProject, "project.json")) ?
            baseProject + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") : baseProject;
        Directory.CreateDirectory(target);
        var source = Path.Combine(target, "source");
        Directory.CreateDirectory(source);
        Log("Đang sao chép archive game...");
        await Task.Run(() =>
        {
            foreach (var archive in Archives)
                File.Copy(Path.Combine(gamePath, archive), Path.Combine(source, archive), true);
        });
        Log("Đang xuất TXT...");
        await RunCli("dump", source, Path.Combine(target, "texts"), language.Text);
        Log("Đang xuất atlas và metrics...");
        await RunCli("dump-fonts", source, Path.Combine(target, "fonts"));
        var info = new ProjectInfo { GameDirectory = gamePath, Language = language.Text };
        foreach (var archive in Archives) info.ExpectedHashes[archive] =
            ProjectInfo.Hash(Path.Combine(gamePath, archive));
        info.Save(target);
        project.Text = target;
        SaveSettings();
        Log($"Xuất xong: {target}");
    }

    private async Task ImportAsync()
    {
        var root = Path.GetFullPath(project.Text.Trim());
        var info = ProjectInfo.Load(root);
        var gamePath = Path.GetFullPath(game.Text.Trim());
        if (!gamePath.Equals(Path.GetFullPath(info.GameDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Thư mục game khác với dự án đã xuất.");
        if (Process.GetProcessesByName("Prince of Persia").Length != 0)
            throw new InvalidOperationException("Hãy thoát game trước khi nhập.");
        if (!File.Exists(lzo.Text)) throw new FileNotFoundException("Hãy chọn lzo2_w64.dll.");
        CheckLiveHashes(info, gamePath);
        var source = Path.Combine(root, "source");
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var build = Path.Combine(root, "build", stamp);
        var textPackages = Path.Combine(build, "text-packages");
        var fontPackages = Path.Combine(build, "font-packages");
        var patched = Path.Combine(build, "patched");
        Directory.CreateDirectory(textPackages);
        Directory.CreateDirectory(fontPackages);
        Directory.CreateDirectory(patched);
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in new[] { "Common", "Menu" })
        {
            var name = $"Localization{kind}Package_{info.Language}";
            var txt = Path.Combine(root, "texts", name + ".txt");
            if (!File.Exists(txt)) throw new FileNotFoundException($"Thiếu TXT: {name}", txt);
            var data = Path.Combine(textPackages, $"DataPC_LOD.forge__{name}.data");
            var arguments = new List<string> { "import-text", Path.Combine(source, "DataPC_LOD.forge"),
                name, txt, data, lzo.Text };
            if (!uppercase.Checked) arguments.Add("--keep-case");
            Log($"Đang nhập TXT: {name}...");
            await RunCli([.. arguments]);
        }
        var lod = Path.Combine(patched, "DataPC_LOD.forge");
        Log("Đang dựng DataPC_LOD.forge...");
        await RunCli("rebuild-forge", Path.Combine(source, "DataPC_LOD.forge"), textPackages, lod);
        outputs.Add("DataPC_LOD.forge", lod);

        Log("Đang kiểm tra PNG/DDS/TSV đã sửa...");
        await RunCli("import-fonts", source, Path.Combine(root, "fonts"), fontPackages, lzo.Text);
        foreach (var archive in Archives.Skip(1))
        {
            if (!Directory.GetFiles(fontPackages, archive + "__*.data").Any()) continue;
            var result = Path.Combine(patched, archive);
            Log($"Đang dựng {archive}...");
            await RunCli("rebuild-forge", Path.Combine(source, archive), fontPackages, result);
            outputs.Add(archive, result);
        }

        CheckLiveHashes(info, gamePath);
        var backup = Path.Combine(root, "backups", stamp);
        Directory.CreateDirectory(backup);
        foreach (var archive in outputs.Keys)
            File.Copy(Path.Combine(gamePath, archive), Path.Combine(backup, archive));
        Log($"Đã sao lưu archive hiện tại tại: {backup}");
        var installed = new List<string>();
        try
        {
            foreach (var (archive, result) in outputs)
            {
                var live = Path.Combine(gamePath, archive);
                var staged = live + ".popfs-gui.tmp";
                File.Copy(result, staged, true);
                File.Move(staged, live, true);
                installed.Add(archive);
                Log($"Đã cài: {archive}");
            }
            foreach (var archive in outputs.Keys)
                info.ExpectedHashes[archive] = ProjectInfo.Hash(Path.Combine(gamePath, archive));
            info.Save(root);
        }
        catch
        {
            foreach (var archive in installed)
                File.Copy(Path.Combine(backup, archive), Path.Combine(gamePath, archive), true);
            foreach (var archive in outputs.Keys)
            {
                var staged = Path.Combine(gamePath, archive) + ".popfs-gui.tmp";
                if (File.Exists(staged)) File.Delete(staged);
            }
            throw;
        }
        SaveSettings();
        Log($"Hoàn tất. Bản build: {patched}");
        MessageBox.Show(this, "Đã nhập và cài vào game. Có thể mở game để kiểm tra.",
            "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void CheckLiveHashes(ProjectInfo info, string gamePath)
    {
        foreach (var archive in Archives)
        {
            var live = Path.Combine(gamePath, archive);
            if (!info.ExpectedHashes.TryGetValue(archive, out var expected) ||
                !ProjectInfo.Hash(live).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Archive {archive} đã đổi sau lần xuất/cài trước. " +
                    "Hãy xuất dự án mới từ game hiện tại.");
        }
    }

    private async Task RunCli(params string[] arguments)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "PopFs.Localization.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Thiếu PopFs.Localization.dll bên cạnh GUI.", dll);
        using var process = new Process { StartInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        } };
        process.StartInfo.ArgumentList.Add(dll);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var outText = await output;
        var errText = await errors;
        foreach (var line in outText.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Log(line.TrimEnd());
        foreach (var line in errText.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Log(line.TrimEnd());
        if (process.ExitCode != 0) throw new InvalidOperationException(errText.Trim().Length == 0 ?
            $"Lệnh {arguments[0]} thất bại (mã {process.ExitCode})." : errText.Trim());
    }

    private void Log(string message)
    {
        log.AppendText(message + Environment.NewLine);
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    private sealed record SavedSettings(string Game, string Project, string Lzo, string Language);
}
