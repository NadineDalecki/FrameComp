using System.Text.Json;
using System.Text.Json.Nodes;

namespace VideoFrameComparer;

internal sealed class ProjectSelectionForm : Form
{
    private static readonly Color DialogBackground = Color.FromArgb(28, 28, 28);
    private static readonly Color PanelBackground = Color.FromArgb(36, 36, 36);
    private static readonly Color PrimaryButtonBackground = Color.FromArgb(66, 133, 244);
    private static readonly Color PrimaryButtonForeground = Color.White;
    private static readonly Color SecondaryButtonBackground = Color.FromArgb(62, 62, 62);
    private static readonly Color SecondaryButtonForeground = Color.White;
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

    private static readonly string ProjectDirectoryPath = ResolveProjectDirectoryPath();
    private static readonly string LastProjectPathFile = Path.Combine(ProjectDirectoryPath, "last-project.txt");

    private readonly ListBox _projectListBox;
    private readonly Button _openSelectedButton;
    private readonly Button _deleteProjectButton;
    private readonly Button _newProjectButton;
    private readonly Button _renameProjectButton;
    private readonly Label _infoLabel;
    private readonly ToolTip _toolTip = new ToolTip();

    public ProjectSelectionForm()
    {
        Text = "Choose Project";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 360);
        BackColor = DialogBackground;

        _infoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(16, 14, 16, 0),
            ForeColor = Color.Gainsboro,
            Text = "Choose an existing comparison project or create a new one. Projects are stored in the Projects folder."
        };

        _projectListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(16),
            BackColor = PanelBackground,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _projectListBox.DoubleClick += (_, _) => OpenSelectedProject();
        _projectListBox.SelectedIndexChanged += (_, _) => UpdateButtons();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(16, 8, 16, 8),
            FlowDirection = FlowDirection.LeftToRight
        };

        _openSelectedButton = new Button
        {
            Text = "Open Selected",
            AutoSize = true
        };
        StyleButton(_openSelectedButton, isPrimary: true);
        _openSelectedButton.Click += (_, _) => OpenSelectedProject();

        _deleteProjectButton = new Button
        {
            Text = "Delete",
            AutoSize = true
        };
        StyleButton(_deleteProjectButton, isPrimary: false);
        StyleIconButton(_deleteProjectButton, CreateTrashIcon());
        _deleteProjectButton.AccessibleName = "Delete Project";
        _deleteProjectButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(168, 54, 54);
        _deleteProjectButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(138, 40, 40);
        _deleteProjectButton.Click += (_, _) => DeleteSelectedProject();

        _newProjectButton = new Button
        {
            Text = "New Project",
            AutoSize = true
        };
        StyleButton(_newProjectButton, isPrimary: false);
        StyleIconButton(_newProjectButton, CreatePlusIcon());
        _newProjectButton.AccessibleName = "New Project";
        _newProjectButton.Click += (_, _) => CreateProject();

        _renameProjectButton = new Button
        {
            Text = "Rename",
            AutoSize = true
        };
        StyleButton(_renameProjectButton, isPrimary: false);
        StyleIconButton(_renameProjectButton, CreatePencilIcon());
        _renameProjectButton.AccessibleName = "Rename Project";
        _renameProjectButton.Click += (_, _) => RenameSelectedProject();

        buttonPanel.Controls.Add(_openSelectedButton);
        buttonPanel.Controls.Add(_newProjectButton);
        buttonPanel.Controls.Add(_renameProjectButton);
        buttonPanel.Controls.Add(_deleteProjectButton);

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 8)
        };
        listHost.Controls.Add(_projectListBox);

        Controls.Add(listHost);
        Controls.Add(buttonPanel);
        Controls.Add(_infoLabel);

        _toolTip.SetToolTip(_newProjectButton, "New project");
        _toolTip.SetToolTip(_renameProjectButton, "Rename selected project");
        _toolTip.SetToolTip(_deleteProjectButton, "Delete selected project");

        RefreshProjects();
    }

    public string? SelectedProjectPath { get; private set; }

    private void RefreshProjects()
    {
        Directory.CreateDirectory(ProjectDirectoryPath);
        _projectListBox.Items.Clear();

        string[] projectFiles = Directory.GetFiles(ProjectDirectoryPath, "*.json");
        foreach (string projectPath in projectFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            _projectListBox.Items.Add(new ProjectListItem(projectPath));
        }

        _infoLabel.Text = "Choose an existing comparison project or create a new one. Projects are stored in the Projects folder.";

        string? lastProjectPath = TryReadLastProjectPath();
        if (lastProjectPath is not null)
        {
            for (int i = 0; i < _projectListBox.Items.Count; i++)
            {
                if (_projectListBox.Items[i] is ProjectListItem item &&
                    string.Equals(item.FullPath, lastProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    _projectListBox.SelectedIndex = i;
                    break;
                }
            }
        }

        if (_projectListBox.SelectedIndex < 0 && _projectListBox.Items.Count > 0)
        {
            _projectListBox.SelectedIndex = 0;
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _openSelectedButton.Enabled = _projectListBox.SelectedItem is ProjectListItem;
        _renameProjectButton.Enabled = _projectListBox.SelectedItem is ProjectListItem;
        _deleteProjectButton.Enabled = _projectListBox.SelectedItem is ProjectListItem;
    }

    private void OpenSelectedProject()
    {
        if (_projectListBox.SelectedItem is not ProjectListItem item)
        {
            return;
        }

        CompleteSelection(item.FullPath);
    }

    private void DeleteSelectedProject()
    {
        if (_projectListBox.SelectedItem is not ProjectListItem item)
        {
            return;
        }

        string projectName = Path.GetFileNameWithoutExtension(item.FullPath);
        DialogResult confirm = MessageBox.Show(
            this,
            $"Delete project '{projectName}'?\n\nThis will permanently remove the project file:\n{item.FullPath}\n\nThis cannot be undone.",
            "Delete Project",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            if (File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
            }

            string? lastProjectPath = TryReadLastProjectPath();
            if (lastProjectPath is not null &&
                string.Equals(lastProjectPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(LastProjectPathFile, string.Empty);
            }

            RefreshProjects();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not delete the project.\n\n{ex.Message}",
                "Delete Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void CreateProject()
    {
        using var prompt = new ProjectNamePromptForm();
        if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(prompt.ProjectName))
        {
            return;
        }

        string fileName = SanitizeFileName(prompt.ProjectName.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            MessageBox.Show(this, "Please choose a project name with at least one letter or number.", "Invalid project name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string projectPath = GetUniqueProjectPath(fileName);
        File.WriteAllText(projectPath, "{\n  \"ProjectName\": \"" + EscapeJson(prompt.ProjectName.Trim()) + "\"\n}");
        CompleteSelection(projectPath);
    }

    private void RenameSelectedProject()
    {
        if (_projectListBox.SelectedItem is not ProjectListItem item)
        {
            return;
        }

        string currentDisplayName = Path.GetFileNameWithoutExtension(item.FullPath);
        using var prompt = new ProjectNamePromptForm(title: "Rename Project", initialName: currentDisplayName, submitLabel: "Rename");
        if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(prompt.ProjectName))
        {
            return;
        }

        string requestedName = prompt.ProjectName.Trim();
        string fileName = SanitizeFileName(requestedName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            MessageBox.Show(this, "Please choose a project name with at least one letter or number.", "Invalid project name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string currentPath = item.FullPath;
        string currentBaseName = Path.GetFileNameWithoutExtension(currentPath);
        string targetPath = string.Equals(fileName, currentBaseName, StringComparison.OrdinalIgnoreCase)
            ? currentPath
            : GetUniqueProjectPath(fileName);

        try
        {
            if (!string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(currentPath, targetPath);
            }

            TryUpdateProjectNameInFile(targetPath, requestedName);

            string? lastProjectPath = TryReadLastProjectPath();
            if (lastProjectPath is not null &&
                string.Equals(lastProjectPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(LastProjectPathFile, targetPath);
            }

            RefreshProjects();
            SelectProjectByPath(targetPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not rename the project.\n\n{ex.Message}",
                "Rename Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SelectProjectByPath(string fullPath)
    {
        for (int i = 0; i < _projectListBox.Items.Count; i++)
        {
            if (_projectListBox.Items[i] is ProjectListItem item &&
                string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                _projectListBox.SelectedIndex = i;
                break;
            }
        }
    }

    private static void TryUpdateProjectNameInFile(string projectPath, string projectName)
    {
        try
        {
            if (!File.Exists(projectPath))
            {
                return;
            }

            string json = File.ReadAllText(projectPath);
            JsonNode? root = JsonNode.Parse(json);
            if (root is not JsonObject obj)
            {
                return;
            }

            obj["ProjectName"] = projectName;
            File.WriteAllText(projectPath, obj.ToJsonString(PrettyJsonOptions));
        }
        catch
        {
            // Best-effort metadata update only.
        }
    }

    private void CompleteSelection(string projectPath)
    {
        SelectedProjectPath = projectPath;
        Directory.CreateDirectory(ProjectDirectoryPath);
        File.WriteAllText(LastProjectPathFile, projectPath);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string? TryReadLastProjectPath()
    {
        if (!File.Exists(LastProjectPathFile))
        {
            return null;
        }

        string path = File.ReadAllText(LastProjectPathFile).Trim();
        return File.Exists(path) ? path : null;
    }

    private static string GetUniqueProjectPath(string baseName)
    {
        string candidate = Path.Combine(ProjectDirectoryPath, baseName + ".json");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        int suffix = 2;
        while (true)
        {
            candidate = Path.Combine(ProjectDirectoryPath, $"{baseName}-{suffix}.json");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static string SanitizeFileName(string projectName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = projectName
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray();
        return string.Join("-", new string(chars)
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ResolveProjectDirectoryPath()
    {
        string localProjectsDir = Path.Combine(AppContext.BaseDirectory, "Projects");
        string? fallbackExistingProjects = null;
        var startDirectories = new List<string>();

        // Prefer the repository root Projects folder when running from dist\FrameComp.
        try
        {
            string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
            string trimmedBaseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string baseName = new DirectoryInfo(trimmedBaseDir).Name;
            string? parentName = Directory.GetParent(trimmedBaseDir)?.Name;
            if (string.Equals(parentName, "dist", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(baseName, "FrameComp", StringComparison.OrdinalIgnoreCase))
            {
                string rootProjectsDir = Path.GetFullPath(Path.Combine(trimmedBaseDir, "..", "..", "Projects"));
                if (Directory.Exists(rootProjectsDir))
                {
                    return rootProjectsDir;
                }
            }
        }
        catch
        {
        }

        static void AddUniqueDirectory(List<string> destinations, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            if (destinations.Any(existing => string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            destinations.Add(fullPath);
        }

        AddUniqueDirectory(startDirectories, AppContext.BaseDirectory);
        AddUniqueDirectory(startDirectories, Environment.CurrentDirectory);
        AddUniqueDirectory(startDirectories, Application.StartupPath);

        foreach (string startDirectory in startDirectories)
        {
            string? current = startDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                string candidate = Path.Combine(current, "Projects");
                if (Directory.Exists(candidate))
                {
                    fallbackExistingProjects ??= candidate;
                    try
                    {
                        if (Directory.EnumerateFiles(candidate, "*.json").Any())
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent;
            }
        }

        return fallbackExistingProjects ?? localProjectsDir;
    }

    private static void StyleButton(Button button, bool isPrimary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        button.BackColor = isPrimary ? PrimaryButtonBackground : SecondaryButtonBackground;
        button.ForeColor = isPrimary ? PrimaryButtonForeground : SecondaryButtonForeground;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        button.Padding = new Padding(10, 4, 10, 4);
        button.Margin = new Padding(8, 0, 0, 0);
        button.MinimumSize = new Size(104, 32);
    }

    private static void StyleIconButton(Button button, Bitmap icon)
    {
        button.Text = string.Empty;
        button.AutoSize = false;
        button.Size = new Size(40, 32);
        button.MinimumSize = new Size(40, 32);
        button.Image = icon;
        button.ImageAlign = ContentAlignment.MiddleCenter;
    }

    private static Bitmap CreatePlusIcon()
    {
        var bmp = new Bitmap(16, 16);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.White, 1.8f);
        g.DrawLine(pen, 8, 3, 8, 13);
        g.DrawLine(pen, 3, 8, 13, 8);
        return bmp;
    }

    private static Bitmap CreateTrashIcon()
    {
        var bmp = new Bitmap(16, 16);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.White, 1.5f);
        g.DrawRectangle(pen, 4, 5, 8, 8);
        g.DrawLine(pen, 3, 5, 13, 5);
        g.DrawLine(pen, 6, 3, 10, 3);
        g.DrawLine(pen, 7, 7, 7, 11);
        g.DrawLine(pen, 9, 7, 9, 11);
        return bmp;
    }

    private static Bitmap CreatePencilIcon()
    {
        var bmp = new Bitmap(16, 16);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.White, 1.5f);
        g.DrawLine(pen, 4, 12, 12, 4);
        g.DrawLine(pen, 11, 3, 13, 5);
        g.DrawLine(pen, 3, 13, 6, 12);
        g.DrawLine(pen, 3, 13, 4, 10);
        return bmp;
    }

    private sealed record ProjectListItem(string FullPath)
    {
        public override string ToString() => Path.GetFileNameWithoutExtension(FullPath);
    }

    private sealed class ProjectNamePromptForm : Form
    {
        private readonly TextBox _nameTextBox;

        public ProjectNamePromptForm(string title = "New Project", string initialName = "", string submitLabel = "Create")
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 140);
            BackColor = DialogBackground;

            var label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 38,
                Padding = new Padding(16, 14, 16, 0),
                ForeColor = Color.White,
                Text = "Project name"
            };

            _nameTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                Margin = new Padding(16),
                BorderStyle = BorderStyle.FixedSingle
            };

            var textHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(16, 0, 16, 0)
            };
            textHost.Controls.Add(_nameTextBox);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(16, 8, 16, 8),
                FlowDirection = FlowDirection.RightToLeft
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            StyleButton(cancelButton, isPrimary: false);

            var createButton = new Button
            {
                Text = submitLabel,
                AutoSize = true
            };
            StyleButton(createButton, isPrimary: true);
            createButton.Click += (_, _) =>
            {
                ProjectName = _nameTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(ProjectName))
                {
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };

            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(createButton);

            Controls.Add(buttonPanel);
            Controls.Add(textHost);
            Controls.Add(label);

            AcceptButton = createButton;
            CancelButton = cancelButton;
            _nameTextBox.Text = initialName;
            _nameTextBox.SelectAll();
        }

        public string? ProjectName { get; private set; }
    }
}
