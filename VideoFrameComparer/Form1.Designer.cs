namespace VideoFrameComparer;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        topBarPanel = new Panel();
        restoreVideoSourceButton = new Button();
        useWindowSourceButton = new Button();
        alignmentModeCheckBox = new CheckBox();
        helpLabel = new Label();
        speedComboBox = new ComboBox();
        speedLabel = new Label();
        layoutComboBox = new ComboBox();
        layoutLabel = new Label();
        videosTableLayout = new TableLayoutPanel();
        bottomTransportPanel = new Panel();
        transportLayout = new TableLayoutPanel();
        playbackStatusLabel = new Label();
        masterTimeline = new TrackBar();
        playPauseButton = new Button();
        leftHostPanel = new Panel();
        leftTrackLayout = new TableLayoutPanel();
        leftFooterPanel = new Panel();
        leftMarkerPanel = new Panel();
        leftTimeline = new TrackBar();
        leftInfoLabel = new Label();
        leftImagePanel = new Panel();
        leftPictureBox = new PictureBox();
        leftTitleLabel = new Label();
        rightHostPanel = new Panel();
        rightTrackLayout = new TableLayoutPanel();
        rightFooterPanel = new Panel();
        rightMarkerPanel = new Panel();
        rightTimeline = new TrackBar();
        rightInfoLabel = new Label();
        rightImagePanel = new Panel();
        rightPictureBox = new PictureBox();
        rightTitleLabel = new Label();
        topBarPanel.SuspendLayout();
        videosTableLayout.SuspendLayout();
        bottomTransportPanel.SuspendLayout();
        transportLayout.SuspendLayout();
        leftHostPanel.SuspendLayout();
        leftTrackLayout.SuspendLayout();
        leftFooterPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)leftTimeline).BeginInit();
        leftImagePanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)leftPictureBox).BeginInit();
        rightHostPanel.SuspendLayout();
        rightTrackLayout.SuspendLayout();
        rightFooterPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)rightTimeline).BeginInit();
        rightImagePanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)rightPictureBox).BeginInit();
        ((System.ComponentModel.ISupportInitialize)masterTimeline).BeginInit();
        SuspendLayout();
        // 
        // topBarPanel
        // 
        topBarPanel.BackColor = Color.FromArgb(32, 32, 32);
        topBarPanel.Controls.Add(restoreVideoSourceButton);
        topBarPanel.Controls.Add(useWindowSourceButton);
        topBarPanel.Controls.Add(alignmentModeCheckBox);
        topBarPanel.Controls.Add(helpLabel);
        topBarPanel.Controls.Add(layoutComboBox);
        topBarPanel.Controls.Add(layoutLabel);
        topBarPanel.Dock = DockStyle.Top;
        topBarPanel.Location = new Point(0, 0);
        topBarPanel.Name = "topBarPanel";
        topBarPanel.Size = new Size(1424, 64);
        topBarPanel.TabIndex = 0;
        // 
        // 
        // restoreVideoSourceButton
        // 
        restoreVideoSourceButton.Location = new Point(300, 20);
        restoreVideoSourceButton.Name = "restoreVideoSourceButton";
        restoreVideoSourceButton.Size = new Size(110, 24);
        restoreVideoSourceButton.TabIndex = 8;
        restoreVideoSourceButton.Text = "Back To Video";
        restoreVideoSourceButton.UseVisualStyleBackColor = true;
        restoreVideoSourceButton.Click += restoreVideoSourceButton_Click;
        // 
        // useWindowSourceButton
        // 
        useWindowSourceButton.Location = new Point(154, 20);
        useWindowSourceButton.Name = "useWindowSourceButton";
        useWindowSourceButton.Size = new Size(140, 24);
        useWindowSourceButton.TabIndex = 7;
        useWindowSourceButton.Text = "Use App Window";
        useWindowSourceButton.UseVisualStyleBackColor = true;
        useWindowSourceButton.Click += useWindowSourceButton_Click;
        // 
        // alignmentModeCheckBox
        // 
        alignmentModeCheckBox.AutoSize = true;
        alignmentModeCheckBox.ForeColor = Color.Gainsboro;
        alignmentModeCheckBox.Location = new Point(694, 23);
        alignmentModeCheckBox.Name = "alignmentModeCheckBox";
        alignmentModeCheckBox.Size = new Size(111, 19);
        alignmentModeCheckBox.TabIndex = 10;
        alignmentModeCheckBox.Text = "Alignment mode";
        alignmentModeCheckBox.UseVisualStyleBackColor = true;
        alignmentModeCheckBox.CheckedChanged += alignmentModeCheckBox_CheckedChanged;
        // 
        // helpLabel
        // 
        helpLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        helpLabel.ForeColor = Color.Gainsboro;
          helpLabel.Location = new Point(810, 12);
        helpLabel.Name = "helpLabel";
          helpLabel.Size = new Size(598, 40);
        helpLabel.TabIndex = 6;
          helpLabel.AutoEllipsis = true;
          helpLabel.Text = "Click video to set active | Arrow-keys to move frame-by-frame | Shift + arrow-keys for 20 frames | Ctrl + Arrow-keys controls shared timeline | Space plays/pauses | M adds a marker | C adds a timeline comment";
        helpLabel.TextAlign = ContentAlignment.MiddleRight;
        // 
        // layoutComboBox
        // 
        layoutComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        layoutComboBox.FormattingEnabled = true;
        layoutComboBox.Items.AddRange(new object[] { "Side by side", "Stacked" });
        layoutComboBox.Location = new Point(24, 21);
        layoutComboBox.Name = "layoutComboBox";
        layoutComboBox.Size = new Size(109, 23);
        layoutComboBox.TabIndex = 3;
        layoutComboBox.SelectedIndexChanged += layoutComboBox_SelectedIndexChanged;
        // 
        // layoutLabel
        // 
        layoutLabel.ForeColor = Color.White;
        layoutLabel.Location = new Point(24, 2);
        layoutLabel.Name = "layoutLabel";
        layoutLabel.Size = new Size(52, 20);
        layoutLabel.TabIndex = 2;
        layoutLabel.Text = "Layout";
        // 
        // videosTableLayout
        // 
        videosTableLayout.BackColor = Color.FromArgb(24, 24, 24);
        videosTableLayout.ColumnCount = 2;
        videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        videosTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        videosTableLayout.Controls.Add(leftHostPanel, 0, 0);
        videosTableLayout.Controls.Add(rightHostPanel, 1, 0);
        videosTableLayout.Padding = new Padding(1);
        videosTableLayout.Dock = DockStyle.Fill;
        videosTableLayout.Location = new Point(0, 64);
        videosTableLayout.Name = "videosTableLayout";
        videosTableLayout.RowCount = 1;
        videosTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        videosTableLayout.Size = new Size(1424, 726);
        videosTableLayout.TabIndex = 1;
        // 
        // bottomTransportPanel
        // 
        bottomTransportPanel.BackColor = Color.FromArgb(28, 28, 28);
        bottomTransportPanel.Controls.Add(transportLayout);
        bottomTransportPanel.Dock = DockStyle.Bottom;
        bottomTransportPanel.Location = new Point(0, 790);
        bottomTransportPanel.Name = "bottomTransportPanel";
        bottomTransportPanel.Padding = new Padding(16, 8, 16, 10);
        bottomTransportPanel.Size = new Size(1424, 71);
        bottomTransportPanel.TabIndex = 2;
        // 
        // transportLayout
        // 
        transportLayout.ColumnCount = 4;
        transportLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
        transportLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        transportLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        transportLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        transportLayout.Controls.Add(playPauseButton, 0, 0);
        transportLayout.Controls.Add(playbackStatusLabel, 1, 0);
        transportLayout.Controls.Add(speedLabel, 2, 0);
        transportLayout.Controls.Add(speedComboBox, 3, 0);
        transportLayout.Controls.Add(masterTimeline, 1, 1);
        transportLayout.Dock = DockStyle.Fill;
        transportLayout.Location = new Point(16, 8);
        transportLayout.Name = "transportLayout";
        transportLayout.RowCount = 2;
        transportLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        transportLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        transportLayout.Size = new Size(1392, 53);
        transportLayout.TabIndex = 0;
        // 
        // playbackStatusLabel
        // 
        playbackStatusLabel.Dock = DockStyle.Fill;
        playbackStatusLabel.ForeColor = Color.Gainsboro;
        playbackStatusLabel.Location = new Point(135, 0);
        playbackStatusLabel.Margin = new Padding(3, 0, 0, 0);
        playbackStatusLabel.Name = "playbackStatusLabel";
        playbackStatusLabel.Padding = new Padding(12, 0, 0, 0);
        playbackStatusLabel.Size = new Size(1257, 22);
        playbackStatusLabel.TabIndex = 2;
        playbackStatusLabel.Text = "Master timeline";
        playbackStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // speedLabel
        // 
        speedLabel.Dock = DockStyle.Fill;
        speedLabel.ForeColor = Color.White;
        speedLabel.Location = new Point(1263, 0);
        speedLabel.Name = "speedLabel";
        speedLabel.Size = new Size(52, 22);
        speedLabel.TabIndex = 3;
        speedLabel.Text = "Speed";
        speedLabel.TextAlign = ContentAlignment.MiddleRight;
        // 
        // speedComboBox
        // 
        speedComboBox.Dock = DockStyle.Fill;
        speedComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        speedComboBox.FormattingEnabled = true;
        speedComboBox.Items.AddRange(new object[] { "25%", "50%", "75%", "100%", "125%", "150%", "200%" });
        speedComboBox.Location = new Point(1321, 0);
        speedComboBox.Margin = new Padding(6, 0, 0, 0);
        speedComboBox.Name = "speedComboBox";
        speedComboBox.Size = new Size(71, 23);
        speedComboBox.TabIndex = 4;
        speedComboBox.SelectedIndexChanged += speedComboBox_SelectedIndexChanged;
        // 
        // masterTimeline
        // 
        masterTimeline.Dock = DockStyle.Fill;
        masterTimeline.Enabled = false;
        masterTimeline.Location = new Point(135, 25);
        masterTimeline.Margin = new Padding(3, 3, 0, 0);
        masterTimeline.Name = "masterTimeline";
        masterTimeline.Size = new Size(1177, 28);
        masterTimeline.TabIndex = 1;
        masterTimeline.TickStyle = TickStyle.None;
        masterTimeline.Scroll += masterTimeline_Scroll;
        // 
        // playPauseButton
        // 
        transportLayout.SetRowSpan(playPauseButton, 2);
        playPauseButton.Dock = DockStyle.Fill;
        playPauseButton.Location = new Point(0, 0);
        playPauseButton.Margin = new Padding(0);
        playPauseButton.Name = "playPauseButton";
        playPauseButton.Size = new Size(132, 53);
        playPauseButton.TabIndex = 0;
        playPauseButton.Text = "▶";
        playPauseButton.UseVisualStyleBackColor = true;
        playPauseButton.Click += playPauseButton_Click;
        // 
        // leftHostPanel
        // 
        leftHostPanel.BackColor = Color.FromArgb(45, 45, 45);
        leftHostPanel.Controls.Add(leftTrackLayout);
        leftHostPanel.Dock = DockStyle.Fill;
        leftHostPanel.Location = new Point(1, 1);
        leftHostPanel.Margin = new Padding(0, 0, 1, 0);
        leftHostPanel.Name = "leftHostPanel";
        leftHostPanel.Padding = new Padding(0);
        leftHostPanel.Size = new Size(711, 724);
        leftHostPanel.TabIndex = 0;
        // 
        // leftTrackLayout
        // 
        leftTrackLayout.ColumnCount = 1;
        leftTrackLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        leftTrackLayout.Controls.Add(leftTitleLabel, 0, 0);
        leftTrackLayout.Controls.Add(leftImagePanel, 0, 1);
        leftTrackLayout.Controls.Add(leftFooterPanel, 0, 2);
        leftTrackLayout.Dock = DockStyle.Fill;
        leftTrackLayout.Location = new Point(0, 0);
        leftTrackLayout.Margin = new Padding(0);
        leftTrackLayout.Name = "leftTrackLayout";
        leftTrackLayout.RowCount = 3;
        leftTrackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        leftTrackLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        leftTrackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        leftTrackLayout.Size = new Size(711, 724);
        leftTrackLayout.TabIndex = 6;
        // 
        // leftFooterPanel
        // 
        leftFooterPanel.Controls.Add(leftMarkerPanel);
        leftFooterPanel.Controls.Add(leftTimeline);
        leftFooterPanel.BackColor = Color.FromArgb(45, 45, 45);
        leftFooterPanel.Dock = DockStyle.Fill;
        leftFooterPanel.Location = new Point(0, 648);
        leftFooterPanel.Margin = new Padding(0);
        leftFooterPanel.Name = "leftFooterPanel";
        leftFooterPanel.Size = new Size(711, 76);
        leftFooterPanel.TabIndex = 5;
        // 
        // leftTimeline
        // 
        leftTimeline.AutoSize = false;
        leftTimeline.Dock = DockStyle.Top;
        leftTimeline.Enabled = false;
        leftTimeline.Location = new Point(0, 0);
        leftTimeline.Name = "leftTimeline";
        leftTimeline.Size = new Size(711, 52);
        leftTimeline.TabIndex = 3;
        leftTimeline.TickStyle = TickStyle.None;
        // 
        // leftMarkerPanel
        // 
        leftMarkerPanel.BackColor = Color.FromArgb(52, 52, 52);
        leftMarkerPanel.BorderStyle = BorderStyle.FixedSingle;
        leftMarkerPanel.Cursor = Cursors.Hand;
        leftMarkerPanel.Dock = DockStyle.Bottom;
        leftMarkerPanel.Location = new Point(0, 52);
        leftMarkerPanel.Name = "leftMarkerPanel";
        leftMarkerPanel.Size = new Size(711, 24);
        leftMarkerPanel.TabIndex = 4;
        // 
        // leftInfoLabel
        // 
        leftInfoLabel.Dock = DockStyle.Bottom;
        leftInfoLabel.ForeColor = Color.Gainsboro;
        leftInfoLabel.Location = new Point(0, 724);
        leftInfoLabel.Name = "leftInfoLabel";
        leftInfoLabel.Padding = new Padding(6, 6, 0, 0);
        leftInfoLabel.Size = new Size(711, 0);
        leftInfoLabel.TabIndex = 2;
        leftInfoLabel.Text = "No video loaded";
        // 
        // leftImagePanel
        // 
        leftImagePanel.AutoScroll = true;
        leftImagePanel.BackColor = Color.Black;
        leftImagePanel.Controls.Add(leftPictureBox);
        leftImagePanel.Dock = DockStyle.Fill;
        leftImagePanel.Location = new Point(0, 28);
        leftImagePanel.Margin = new Padding(0);
        leftImagePanel.Name = "leftImagePanel";
        leftImagePanel.Padding = new Padding(0);
        leftImagePanel.Size = new Size(711, 620);
        leftImagePanel.TabIndex = 1;
        // 
        // leftPictureBox
        // 
        leftPictureBox.Location = new Point(0, 0);
        leftPictureBox.Name = "leftPictureBox";
        leftPictureBox.Size = new Size(320, 180);
        leftPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        leftPictureBox.TabIndex = 0;
        leftPictureBox.TabStop = false;
        // 
        // leftTitleLabel
        // 
        leftTitleLabel.Dock = DockStyle.Fill;
        leftTitleLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        leftTitleLabel.ForeColor = Color.White;
        leftTitleLabel.Location = new Point(0, 0);
        leftTitleLabel.Margin = new Padding(0);
        leftTitleLabel.Name = "leftTitleLabel";
        leftTitleLabel.Padding = new Padding(6, 0, 0, 0);
        leftTitleLabel.Size = new Size(711, 28);
        leftTitleLabel.TabIndex = 0;
        leftTitleLabel.Text = "Video A";
        leftTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // rightHostPanel
        // 
        rightHostPanel.BackColor = Color.FromArgb(45, 45, 45);
        rightHostPanel.Controls.Add(rightTrackLayout);
        rightHostPanel.Dock = DockStyle.Fill;
        rightHostPanel.Location = new Point(713, 1);
        rightHostPanel.Margin = new Padding(0);
        rightHostPanel.Name = "rightHostPanel";
        rightHostPanel.Padding = new Padding(0);
        rightHostPanel.Size = new Size(710, 724);
        rightHostPanel.TabIndex = 1;
        // 
        // rightTrackLayout
        // 
        rightTrackLayout.ColumnCount = 1;
        rightTrackLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rightTrackLayout.Controls.Add(rightTitleLabel, 0, 0);
        rightTrackLayout.Controls.Add(rightImagePanel, 0, 1);
        rightTrackLayout.Controls.Add(rightFooterPanel, 0, 2);
        rightTrackLayout.Dock = DockStyle.Fill;
        rightTrackLayout.Location = new Point(0, 0);
        rightTrackLayout.Margin = new Padding(0);
        rightTrackLayout.Name = "rightTrackLayout";
        rightTrackLayout.RowCount = 3;
        rightTrackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        rightTrackLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rightTrackLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        rightTrackLayout.Size = new Size(710, 724);
        rightTrackLayout.TabIndex = 6;
        // 
        // rightFooterPanel
        // 
        rightFooterPanel.Controls.Add(rightMarkerPanel);
        rightFooterPanel.Controls.Add(rightTimeline);
        rightFooterPanel.BackColor = Color.FromArgb(45, 45, 45);
        rightFooterPanel.Dock = DockStyle.Fill;
        rightFooterPanel.Location = new Point(0, 648);
        rightFooterPanel.Margin = new Padding(0);
        rightFooterPanel.Name = "rightFooterPanel";
        rightFooterPanel.Size = new Size(710, 76);
        rightFooterPanel.TabIndex = 5;
        // 
        // rightTimeline
        // 
        rightTimeline.AutoSize = false;
        rightTimeline.Dock = DockStyle.Top;
        rightTimeline.Enabled = false;
        rightTimeline.Location = new Point(0, 0);
        rightTimeline.Name = "rightTimeline";
        rightTimeline.Size = new Size(710, 52);
        rightTimeline.TabIndex = 3;
        rightTimeline.TickStyle = TickStyle.None;
        // 
        // rightMarkerPanel
        // 
        rightMarkerPanel.BackColor = Color.FromArgb(52, 52, 52);
        rightMarkerPanel.BorderStyle = BorderStyle.FixedSingle;
        rightMarkerPanel.Cursor = Cursors.Hand;
        rightMarkerPanel.Dock = DockStyle.Bottom;
        rightMarkerPanel.Location = new Point(0, 52);
        rightMarkerPanel.Name = "rightMarkerPanel";
        rightMarkerPanel.Size = new Size(710, 24);
        rightMarkerPanel.TabIndex = 4;
        // 
        // rightInfoLabel
        // 
        rightInfoLabel.Dock = DockStyle.Bottom;
        rightInfoLabel.ForeColor = Color.Gainsboro;
        rightInfoLabel.Location = new Point(0, 724);
        rightInfoLabel.Name = "rightInfoLabel";
        rightInfoLabel.Padding = new Padding(6, 6, 0, 0);
        rightInfoLabel.Size = new Size(710, 0);
        rightInfoLabel.TabIndex = 2;
        rightInfoLabel.Text = "No video loaded";
        // 
        // rightImagePanel
        // 
        rightImagePanel.AutoScroll = true;
        rightImagePanel.BackColor = Color.Black;
        rightImagePanel.Controls.Add(rightPictureBox);
        rightImagePanel.Dock = DockStyle.Fill;
        rightImagePanel.Location = new Point(0, 28);
        rightImagePanel.Margin = new Padding(0);
        rightImagePanel.Name = "rightImagePanel";
        rightImagePanel.Padding = new Padding(0);
        rightImagePanel.Size = new Size(710, 620);
        rightImagePanel.TabIndex = 1;
        // 
        // rightPictureBox
        // 
        rightPictureBox.Location = new Point(0, 0);
        rightPictureBox.Name = "rightPictureBox";
        rightPictureBox.Size = new Size(320, 180);
        rightPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        rightPictureBox.TabIndex = 0;
        rightPictureBox.TabStop = false;
        // 
        // rightTitleLabel
        // 
        rightTitleLabel.Dock = DockStyle.Fill;
        rightTitleLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        rightTitleLabel.ForeColor = Color.White;
        rightTitleLabel.Location = new Point(0, 0);
        rightTitleLabel.Margin = new Padding(0);
        rightTitleLabel.Name = "rightTitleLabel";
        rightTitleLabel.Padding = new Padding(6, 0, 0, 0);
        rightTitleLabel.Size = new Size(710, 28);
        rightTitleLabel.TabIndex = 0;
        rightTitleLabel.Text = "Video B";
        rightTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(24, 24, 24);
        ClientSize = new Size(1424, 861);
        Controls.Add(videosTableLayout);
        Controls.Add(bottomTransportPanel);
        Controls.Add(topBarPanel);
        KeyPreview = true;
        MinimumSize = new Size(900, 620);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Video Frame Comparer";
        topBarPanel.ResumeLayout(false);
        videosTableLayout.ResumeLayout(false);
        bottomTransportPanel.ResumeLayout(false);
        transportLayout.ResumeLayout(false);
        leftHostPanel.ResumeLayout(false);
        leftTrackLayout.ResumeLayout(false);
        leftFooterPanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)leftTimeline).EndInit();
        leftImagePanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)leftPictureBox).EndInit();
        rightHostPanel.ResumeLayout(false);
        rightTrackLayout.ResumeLayout(false);
        rightFooterPanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)rightTimeline).EndInit();
        rightImagePanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)rightPictureBox).EndInit();
        ((System.ComponentModel.ISupportInitialize)masterTimeline).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private Panel topBarPanel;
    private Button restoreVideoSourceButton;
    private Button useWindowSourceButton;
    private CheckBox alignmentModeCheckBox;
    private Label helpLabel;
    private ComboBox speedComboBox;
    private Label speedLabel;
    private ComboBox layoutComboBox;
    private Label layoutLabel;
    private TableLayoutPanel videosTableLayout;
    private Panel bottomTransportPanel;
    private TableLayoutPanel transportLayout;
    private Label playbackStatusLabel;
    private TrackBar masterTimeline;
    private Button playPauseButton;
    private Panel leftHostPanel;
    private TableLayoutPanel leftTrackLayout;
    private Panel leftFooterPanel;
    private Panel leftMarkerPanel;
    private TrackBar leftTimeline;
    private Label leftInfoLabel;
    private Panel leftImagePanel;
    private PictureBox leftPictureBox;
    private Label leftTitleLabel;
    private Panel rightHostPanel;
    private TableLayoutPanel rightTrackLayout;
    private Panel rightFooterPanel;
    private Panel rightMarkerPanel;
    private TrackBar rightTimeline;
    private Label rightInfoLabel;
    private Panel rightImagePanel;
    private PictureBox rightPictureBox;
    private Label rightTitleLabel;
}
