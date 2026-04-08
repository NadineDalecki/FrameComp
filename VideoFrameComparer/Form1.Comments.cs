using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;

namespace VideoFrameComparer;

public partial class Form1
{
    private void SharedCommentsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSharedCommentsList)
        {
            return;
        }

        if (_sharedCommentsListBox?.SelectedItem is SharedComment comment)
        {
            SelectSharedComment(comment.Id, moveTimelineToComment: true);
        }
    }

    private void SharedCommentsListBox_MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || _sharedCommentsListBox is null || _sharedCommentsListBox.Items[e.Index] is not SharedComment comment)
        {
            e.ItemHeight = 24;
            return;
        }

        int contentWidth = Math.Max(80, _sharedCommentsListBox.ClientSize.Width - 10 - CommentDeleteGlyphSize - (CommentDeleteGlyphPadding * 2));
        string text = $"{FrameToSharedTimestamp(comment.FrameIndex)}  {comment.Text}";
        DrawingSize size = TextRenderer.MeasureText(
            text,
            _sharedCommentsListBox.Font,
            new DrawingSize(contentWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.LeftAndRightPadding);
        e.ItemHeight = Math.Max(24, size.Height + 8);
    }

    private void SharedCommentsListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _sharedCommentsListBox is null || _sharedCommentsListBox.Items[e.Index] is not SharedComment comment)
        {
            return;
        }

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color backColor = selected ? Color.FromArgb(42, 72, 104) : _sharedCommentsListBox.BackColor;
        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        string text = $"{FrameToSharedTimestamp(comment.FrameIndex)}  {comment.Text}";
        Rectangle deleteBounds = GetCommentDeleteBounds(e.Bounds);
        Rectangle textBounds = new Rectangle(
            e.Bounds.X + 4,
            e.Bounds.Y + 4,
            Math.Max(10, deleteBounds.Left - e.Bounds.X - 8),
            Math.Max(10, e.Bounds.Height - 8));
        Color textColor = selected ? Color.FromArgb(235, 245, 255) : _sharedCommentsListBox.ForeColor;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            _sharedCommentsListBox.Font,
            textBounds,
            textColor,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.LeftAndRightPadding);

        bool deleteHover = _hoverDeleteCommentId == comment.Id;
        Color deleteColor = deleteHover
            ? Color.FromArgb(255, 132, 132)
            : selected ? Color.FromArgb(245, 205, 205) : Color.FromArgb(198, 150, 150);
        TextRenderer.DrawText(
            e.Graphics,
            "x",
            _sharedCommentsListBox.Font,
            deleteBounds,
            deleteColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        e.DrawFocusRectangle();
    }

    private void SharedCommentsListBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_sharedCommentsListBox is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        int index = _sharedCommentsListBox.IndexFromPoint(e.Location);
        if (index < 0 || index >= _sharedCommentsListBox.Items.Count || _sharedCommentsListBox.Items[index] is not SharedComment comment)
        {
            return;
        }

        Rectangle itemBounds = _sharedCommentsListBox.GetItemRectangle(index);
        Rectangle deleteBounds = GetCommentDeleteBounds(itemBounds);
        if (!deleteBounds.Contains(e.Location))
        {
            return;
        }

        RemoveSharedComment(comment.Id);
    }

    private void SharedCommentsListBox_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_sharedCommentsListBox is null)
        {
            return;
        }

        int? nextHoverId = null;
        int index = _sharedCommentsListBox.IndexFromPoint(e.Location);
        if (index >= 0 && index < _sharedCommentsListBox.Items.Count && _sharedCommentsListBox.Items[index] is SharedComment comment)
        {
            Rectangle itemBounds = _sharedCommentsListBox.GetItemRectangle(index);
            if (GetCommentDeleteBounds(itemBounds).Contains(e.Location))
            {
                nextHoverId = comment.Id;
            }
        }

        if (_hoverDeleteCommentId != nextHoverId)
        {
            _hoverDeleteCommentId = nextHoverId;
            _sharedCommentsListBox.Invalidate();
            _sharedCommentsListBox.Cursor = nextHoverId.HasValue ? Cursors.Hand : Cursors.Default;
        }
    }

    private void SharedCommentsListBox_MouseLeave(object? sender, EventArgs e)
    {
        if (_sharedCommentsListBox is null || !_hoverDeleteCommentId.HasValue)
        {
            return;
        }

        _hoverDeleteCommentId = null;
        _sharedCommentsListBox.Cursor = Cursors.Default;
        _sharedCommentsListBox.Invalidate();
    }

    private bool AddSharedCommentAtCurrentTimeline()
    {
        if (!PromptForComment(out string commentText))
        {
            return false;
        }

        var comment = new SharedComment(_nextSharedCommentId++, masterTimeline.Value, commentText.Trim(), DateTime.Now);
        _sharedComments.Add(comment);
        _sharedComments.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        _isCommentsSidebarCollapsed = false;
        UpdateLayoutMode();
        SelectSharedComment(comment.Id, moveTimelineToComment: false);
        UpdateSharedCommentsUi();
        SaveSession();
        return true;
    }

    private static bool PromptForComment(out string commentText)
    {
        commentText = string.Empty;
        using var promptForm = new Form
        {
            Text = "Add Comment",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Width = 540,
            Height = 190
        };

        var messageLabel = new Label
        {
            Text = "Comment text",
            Left = 12,
            Top = 10,
            Width = 500
        };
        var inputBox = new TextBox
        {
            Left = 12,
            Top = 34,
            Width = 500,
            Height = 64,
            Multiline = true
        };
        var okButton = new Button
        {
            Text = "Add",
            Left = 352,
            Top = 110,
            Width = 75,
            DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 437,
            Top = 110,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        promptForm.Controls.Add(messageLabel);
        promptForm.Controls.Add(inputBox);
        promptForm.Controls.Add(okButton);
        promptForm.Controls.Add(cancelButton);
        promptForm.AcceptButton = okButton;
        promptForm.CancelButton = cancelButton;

        if (promptForm.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        commentText = inputBox.Text.Trim();
        return !string.IsNullOrWhiteSpace(commentText);
    }

    private void UpdateSharedCommentsUi()
    {
        if (_sharedCommentsListBox is null)
        {
            return;
        }

        _isUpdatingSharedCommentsList = true;
        _sharedCommentsListBox.BeginUpdate();
        try
        {
            _sharedCommentsListBox.Items.Clear();
            foreach (SharedComment comment in _sharedComments)
            {
                _sharedCommentsListBox.Items.Add(comment);
            }

            if (_selectedSharedCommentId is int selectedId)
            {
                for (int i = 0; i < _sharedCommentsListBox.Items.Count; i++)
                {
                    if (_sharedCommentsListBox.Items[i] is SharedComment comment && comment.Id == selectedId)
                    {
                        _sharedCommentsListBox.SelectedIndex = i;
                        _sharedCommentsListBox.TopIndex = Math.Max(0, i - 2);
                        break;
                    }
                }
            }
        }
        finally
        {
            _sharedCommentsListBox.EndUpdate();
            _isUpdatingSharedCommentsList = false;
        }
        _sharedCommentMarkerPanel?.Invalidate();
        UpdatePlaybackStatus();
    }

    private void SelectSharedComment(int commentId, bool moveTimelineToComment)
    {
        SharedComment? comment = _sharedComments.FirstOrDefault(c => c.Id == commentId);
        if (comment is null)
        {
            return;
        }

        _selectedSharedCommentId = comment.Id;
        if (moveTimelineToComment)
        {
            StopPlayback();
            SetGlobalTimelineFrame(comment.FrameIndex);
        }

        UpdateSharedCommentsUi();
    }

    private void SharedCommentMarkerPanel_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_sharedCommentMarkerPanel is null || _sharedComments.Count == 0 || _sharedCommentMarkerPanel.ClientSize.Width <= 0)
        {
            return;
        }

        int width = _sharedCommentMarkerPanel.ClientSize.Width - 1;
        SharedComment? nearest = null;
        int bestDistance = int.MaxValue;
        foreach (SharedComment comment in _sharedComments)
        {
            int markerX = SharedCommentFrameToX(comment.FrameIndex, width);
            int distance = Math.Abs(markerX - e.X);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = comment;
            }
        }

        if (nearest is not null && bestDistance <= SharedCommentSelectionThresholdPixels)
        {
            SelectSharedComment(nearest.Id, moveTimelineToComment: true);
        }
    }

    private void DrawSharedCommentMarkers(Graphics graphics)
    {
        if (_sharedCommentMarkerPanel is null)
        {
            return;
        }

        graphics.Clear(_sharedCommentMarkerPanel.BackColor);
        if (_sharedCommentMarkerPanel.ClientSize.Width <= 0)
        {
            return;
        }

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var markerPen = new Pen(Color.FromArgb(200, 200, 200), 2f);
        using var selectedPen = new Pen(Color.FromArgb(255, 184, 84), 3f);
        using var markerBrush = new SolidBrush(Color.FromArgb(200, 200, 200));
        using var selectedBrush = new SolidBrush(Color.FromArgb(255, 184, 84));
        int width = _sharedCommentMarkerPanel.ClientSize.Width - 1;
        int height = _sharedCommentMarkerPanel.ClientSize.Height - 1;
        int tipY = Math.Max(2, height - 1);
        int baseY = 1;

        foreach (SharedComment comment in _sharedComments)
        {
            int x = SharedCommentFrameToX(comment.FrameIndex, width);
            bool isSelected = _selectedSharedCommentId == comment.Id;
            graphics.DrawLine(isSelected ? selectedPen : markerPen, x, baseY, x, tipY - 2);
            DrawingPoint[] shape =
            [
                new DrawingPoint(Math.Max(0, x - 6), baseY),
                new DrawingPoint(Math.Min(width, x + 6), baseY),
                new DrawingPoint(x, tipY)
            ];
            graphics.FillPolygon(isSelected ? selectedBrush : markerBrush, shape);
        }
    }

    private int SharedCommentFrameToX(int frameIndex, int width)
    {
        _ = width;
        return FrameToTimelineX(frameIndex);
    }

    private static string FrameToSharedTimestamp(int frameIndex)
    {
        int fps = 30;
        int totalSeconds = Math.Max(0, frameIndex / fps);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }
}

