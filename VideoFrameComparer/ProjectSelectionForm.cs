namespace VideoFrameComparer;

internal sealed class ProjectSelectionForm : Form
{
    private static readonly Color DialogBackground = Color.FromArgb(28, 28, 28);
    private static readonly Color PanelBackground = Color.FromArgb(36, 36, 36);
    private static readonly Color PrimaryButtonBackground = Color.FromArgb(66, 133, 244);
    private static readonly Color PrimaryButtonForeground = Color.White;
    private static readonly Color SecondaryButtonBackground = Color.FromArgb(62, 62, 62);
    private static readonly Color SecondaryButtonForeground = Color.White;

    private static readonly string ProjectDirectoryPath = Path.Combine(AppContext.BaseDirectory, "Projects");
    private static readonly string LastProjectPathFile = Path.Combine(ProjectDirectoryPath, "last-project.txt");

    private readonly ListBox _projectListBox;
    private readonly Button _openSelectedButton;
    private readonly Button _openLastButton;
    private readonly Button _newProjectButton;
    private readonly Label _infoLabel;

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
            Text = "Choose an existing comparison project or create a new one. Projects are stored in the Projects folder beside the app."
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
            FlowDirection = FlowDirection.RightToLeft
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        _openSelectedButton = new Button
        {
            Text = "Open Selected",
            AutoSize = true
        };
        StyleButton(_openSelectedButton, isPrimary: true);
        _openSelectedButton.Click += (_, _) => OpenSelectedProject();

        _openLastButton = new Button
        {
            Text = "Open Last",
            AutoSize = true
        };
        StyleButton(_openLastButton, isPrimary: false);
        _openLastButton.Click += (_, _) => OpenLastProject();

        _newProjectButton = new Button
        {
            Text = "New Project",
            AutoSize = true
        };
        StyleButton(_newProjectButton, isPrimary: false);
        _newProjectButton.Click += (_, _) => CreateProject();

        StyleButton(cancelButton, isPrimary: false);

        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(_openSelectedButton);
        buttonPanel.Controls.Add(_openLastButton);
        buttonPanel.Controls.Add(_newProjectButton);

        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 8)
        };
        listHost.Controls.Add(_projectListBox);

        Controls.Add(listHost);
        Controls.Add(buttonPanel);
        Controls.Add(_infoLabel);

        RefreshProjects();
    }

    public string? SelectedProjectPath { get; private set; }

    private void RefreshProjects()
    {
        Directory.CreateDirectory(ProjectDirectoryPath);
        _projectListBox.Items.Clear();

        foreach (string projectPath in Directory.GetFiles(ProjectDirectoryPath, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            _projectListBox.Items.Add(new ProjectListItem(projectPath));
        }

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
        _openLastButton.Enabled = TryReadLastProjectPath() is not null;
    }

    private void OpenSelectedProject()
    {
        if (_projectListBox.SelectedItem is not ProjectListItem item)
        {
            return;
        }

        CompleteSelection(item.FullPath);
    }

    private void OpenLastProject()
    {
        string? lastProjectPath = TryReadLastProjectPath();
        if (lastProjectPath is null || !File.Exists(lastProjectPath))
        {
            RefreshProjects();
            return;
        }

        CompleteSelection(lastProjectPath);
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

    private sealed record ProjectListItem(string FullPath)
    {
        public override string ToString() => Path.GetFileNameWithoutExtension(FullPath);
    }

    private sealed class ProjectNamePromptForm : Form
    {
        private readonly TextBox _nameTextBox;

        public ProjectNamePromptForm()
        {
            Text = "New Project";
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
                Text = "Create",
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
        }

        public string? ProjectName { get; private set; }
    }
}
