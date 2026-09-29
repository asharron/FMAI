using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BizHawk.Emulation.Common;
using BizHawk.Client.Common;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.Remoting.Channels;

namespace FMAI;

[ExternalTool("Hex Viewer")]
[ExternalToolApplicability.SingleSystem(VSystemID.Raw.SNES)]
public class HexViewer : Form, IExternalToolForm {
    public bool IsActive { get; private set; }
    public bool IsLoaded { get; private set; }

    [RequiredApi] public ApiContainer? ApiContainer { get; set; }

    [RequiredApi] public IEmulationApi? EmulationApi { get; set; }
    public void UpdateValues(ToolFormUpdateType type) => RefreshFormControls();
    public void Restart() => RefreshFormControls();
    public bool AskSaveChanges() => true;

    private const int BytesPerRow = 16;
    private static readonly string[] HexStrings = Enumerable.Range(0, 256).Select(b => b.ToString("X2")).ToArray();
    private readonly HashSet<long> _modifiedAddresses = new HashSet<long>();
    private readonly Dictionary<long, string> _notes = new Dictionary<long, string>();
    private readonly Dictionary<long, string> _labels = new Dictionary<long, string>();
    private readonly DataTable _table;
    private readonly DataGridView _dataGridView;
    private readonly TextBox _noteTextBox;
    private readonly Label _selectedAddressLabel;
    private readonly Label _statusLabel;
    private readonly Button _saveNoteButton;
    private readonly Panel _rightPanel;
    private long _currentSelectedAddress = -1;
    private bool _isJumpDialogOpen;
    private bool _isLabelDialogOpen;

    public HexViewer() {
        KeyPreview = true;
        ClientSize = new Size(920, 380);
        MinimumSize = new Size(700, 260);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        SuspendLayout();

        _table = new DataTable();

        _table.Columns.Add("0");
        _table.Columns.Add("1");
        _table.Columns.Add("2");
        _table.Columns.Add("3");
        _table.Columns.Add("4");
        _table.Columns.Add("5");
        _table.Columns.Add("6");
        _table.Columns.Add("7");
        _table.Columns.Add("8");
        _table.Columns.Add("9");
        _table.Columns.Add("A");
        _table.Columns.Add("B");
        _table.Columns.Add("C");
        _table.Columns.Add("D");
        _table.Columns.Add("E");
        _table.Columns.Add("F");
        _table.Columns.Add("Label", typeof(string));

        _table.BeginLoadData();
        for (int i = 0; i < 8192; i++) {
            _table.Rows.Add("00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "");
        }
        _table.EndLoadData();

        Font monoFont = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font monoBoldFont = new Font("Consolas", 9.5f, FontStyle.Bold);

        _dataGridView = new DataGridView {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(30, 30, 30),
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = Color.FromArgb(50, 50, 50),
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            EnableHeadersVisualStyles = false,
            RowHeadersWidth = 70,
            RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 26,
            RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            Font = monoFont,
            DefaultCellStyle = new DataGridViewCellStyle {
                Font = monoFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                SelectionBackColor = Color.FromArgb(38, 79, 120),
                SelectionForeColor = Color.FromArgb(255, 255, 255),
                Padding = new Padding(2, 0, 2, 0),
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle {
                Font = monoFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(37, 37, 38),
                ForeColor = Color.FromArgb(220, 220, 220),
                SelectionBackColor = Color.FromArgb(38, 79, 120),
                SelectionForeColor = Color.FromArgb(255, 255, 255),
                Padding = new Padding(2, 0, 2, 0),
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Font = monoBoldFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(200, 200, 200),
                Padding = new Padding(0),
            },
            RowHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Font = monoBoldFont,
                Alignment = DataGridViewContentAlignment.MiddleRight,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(140, 170, 200),
                Padding = new Padding(0),
            },
        };

        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_dataGridView, true, null);

        _dataGridView.DataBindingComplete += (sender, e) => {
            _dataGridView.ClearSelection();
            _dataGridView.CurrentCell = null;

            foreach (DataGridViewColumn col in _dataGridView.Columns) {
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                col.HeaderCell.Style.Font = monoBoldFont;
                col.HeaderCell.Style.BackColor = Color.FromArgb(45, 45, 48);
                col.HeaderCell.Style.ForeColor = Color.FromArgb(200, 200, 200);

                if (col.Index < BytesPerRow) {
                    col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    col.HeaderCell.Style.Padding = new Padding(0);
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                } else {
                    col.HeaderText = "Label";
                    col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
                    col.HeaderCell.Style.Padding = new Padding(6, 0, 0, 0);
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                    col.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
                    col.DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
                    col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    col.MinimumWidth = 120;
                }
            }
        };

        _dataGridView.CellFormatting += (sender, e) => {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.ColumnIndex < BytesPerRow) {
                long address = (long)e.RowIndex * BytesPerRow + e.ColumnIndex;
                if (_modifiedAddresses.Contains(address)) {
                    e.CellStyle.BackColor = Color.FromArgb(80, 70, 20);
                    e.CellStyle.ForeColor = Color.FromArgb(255, 235, 140);
                }
            }
        };

        _dataGridView.CellPainting += (sender, e) => {
            if (e.ColumnIndex < 0 || e.RowIndex != -1) {
                return;
            }

            DataGridView? dgv = sender as DataGridView;
            if (dgv == null) return;
            SortOrder sort = dgv.Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection;

            if (e.RowIndex == -1 && sort == SortOrder.None) {
                string headerText = dgv.Columns[e.ColumnIndex].HeaderText;
                Font headerFont = e.CellStyle.Font ?? monoBoldFont;

                e.Paint(e.ClipBounds, (DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground));

                TextFormatFlags flags = e.ColumnIndex < BytesPerRow
                    ? (TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter)
                    : (TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                Rectangle textBounds = e.ColumnIndex < BytesPerRow
                    ? e.CellBounds
                    : new Rectangle(e.CellBounds.Left + 6, e.CellBounds.Top, e.CellBounds.Width - 6, e.CellBounds.Height);

                TextRenderer.DrawText(
                    e.Graphics,
                    headerText,
                    headerFont,
                    textBounds,
                    e.CellStyle.ForeColor,
                    flags
                );
                e.Handled = true;
            }
        };

        _dataGridView.RowPostPaint += (sender, e) => {
            var grid = sender as DataGridView;
            if (grid == null) return;

            long address = (long)e.RowIndex * BytesPerRow;
            string addressText = address.ToString("X6");

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right;

            Rectangle headerBounds = new Rectangle(
                e.RowBounds.Left, 
                e.RowBounds.Top, 
                grid.RowHeadersWidth - 6, 
                e.RowBounds.Height
            );

            Font font = grid.RowHeadersDefaultCellStyle.Font ?? monoBoldFont;
            Color foreColor = grid.RowHeadersDefaultCellStyle.ForeColor;

            TextRenderer.DrawText(
                e.Graphics, 
                addressText, 
                font, 
                headerBounds, 
                foreColor, 
                flags
            );
        };

        _dataGridView.Scroll += (sender, e) => {
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll && e.NewValue >= 0 && e.NewValue < _dataGridView.RowCount) {
                _dataGridView.FirstDisplayedScrollingRowIndex = e.NewValue;
            }
            RefreshFormControls();
        };

        _dataGridView.MouseWheel += (_, _) => RefreshFormControls();

        _dataGridView.SelectionChanged += (sender, e) => UpdateSelectedCellNote();
        _dataGridView.CurrentCellChanged += (sender, e) => UpdateSelectedCellNote();

        _dataGridView.KeyDown += (sender, e) => {
            if (e.Control && e.KeyCode == Keys.J) {
                OpenJumpToAddressDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
            } else if (HandleIncrementDecrementKey(e.KeyData)) {
                e.Handled = true;
                e.SuppressKeyPress = true;
            } else if (HandleAddressLabelKey(e.KeyData)) {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _dataGridView.PreviewKeyDown += (sender, e) => {
            if (e.Control && e.KeyCode == Keys.J) {
                e.IsInputKey = true;
            } else if (!e.Control && !e.Alt && (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)) {
                e.IsInputKey = true;
            } else if (!e.Control && !e.Alt && e.KeyCode == Keys.L && GetSelectedFullRowIndex() != null) {
                e.IsInputKey = true;
            }
        };

        KeyDown += (sender, e) => {
            if (_noteTextBox != null && _noteTextBox.Focused) {
                if (e.Control && e.KeyCode == Keys.S) {
                    SaveCurrentNote();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                return;
            }
            if (e.Control && e.KeyCode == Keys.J) {
                OpenJumpToAddressDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
            } else if (HandleIncrementDecrementKey(e.KeyData)) {
                e.Handled = true;
                e.SuppressKeyPress = true;
            } else if (HandleAddressLabelKey(e.KeyData)) {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        ContextMenuStrip contextMenu = new ContextMenuStrip {
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.FromArgb(220, 220, 220),
            ShowImageMargin = false
        };
        ToolStripMenuItem jumpMenuItem = new ToolStripMenuItem("Jump to Address...", null, (sender, e) => OpenJumpToAddressDialog()) {
            ShortcutKeys = Keys.Control | Keys.J,
            ShowShortcutKeys = true,
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.FromArgb(220, 220, 220)
        };
        ToolStripMenuItem labelMenuItem = new ToolStripMenuItem("Address Label...", null, (sender, e) => {
            int? fullRow = GetSelectedFullRowIndex();
            if (fullRow.HasValue) {
                OpenAddressLabelDialog(fullRow.Value);
            } else if (_dataGridView.CurrentCell != null && _dataGridView.CurrentCell.RowIndex >= 0) {
                OpenAddressLabelDialog(_dataGridView.CurrentCell.RowIndex);
            }
        }) {
            ShortcutKeyDisplayString = "L",
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.FromArgb(220, 220, 220)
        };
        contextMenu.Items.Add(jumpMenuItem);
        contextMenu.Items.Add(labelMenuItem);
        _dataGridView.ContextMenuStrip = contextMenu;
        ContextMenuStrip = contextMenu;

        _dataGridView.DataSource = _table;

        // Side Panel Setup (Text Area & Save Button on the right)
        _rightPanel = new Panel {
            Dock = DockStyle.Right,
            Width = 240,
            BackColor = Color.FromArgb(37, 37, 38),
            Padding = new Padding(10, 8, 10, 10)
        };

        _selectedAddressLabel = new Label {
            Text = "Note for Address: $000000",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 220, 254),
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft
        };

        Panel bottomPanel = new Panel {
            Dock = DockStyle.Bottom,
            Height = 58,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };

        _saveNoteButton = new Button {
            Text = "Save Note",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            BackColor = Color.FromArgb(14, 99, 156),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Dock = DockStyle.Top,
            Height = 28,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        _saveNoteButton.FlatAppearance.BorderSize = 0;
        _saveNoteButton.Click += (sender, e) => SaveCurrentNote();

        _statusLabel = new Label {
            Text = "",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 200, 115),
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft
        };

        bottomPanel.Controls.Add(_saveNoteButton);
        bottomPanel.Controls.Add(_statusLabel);

        _noteTextBox = new TextBox {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle
        };

        _noteTextBox.KeyDown += (sender, e) => {
            if (e.Control && e.KeyCode == Keys.S) {
                SaveCurrentNote();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _rightPanel.Controls.Add(_noteTextBox);
        _rightPanel.Controls.Add(_selectedAddressLabel);
        _rightPanel.Controls.Add(bottomPanel);

        Controls.Add(_dataGridView);
        Controls.Add(_rightPanel);

        LoadNotesFromDisk();
        LoadLabelsFromDisk();

        ResumeLayout(performLayout: false);
        PerformLayout();

        Load += (_, _) => {
            IsLoaded = true;
            UpdateSelectedCellNote();
        };
        Activated += (_, _) => IsActive = true;
        Deactivate += (_, _) => IsActive = false;
        FormClosed += (_, _) => IsLoaded = false;

        Shown += (_, _) => {
            ApiContainer?.SaveState.LoadSlot(1);
            RefreshFormControls();
            UpdateSelectedCellNote();
        };
    }


    private void EnsureRows(int totalBytes) {
        int requiredRows = (totalBytes + BytesPerRow - 1) / BytesPerRow;
        if (_table.Rows.Count == requiredRows) return;

        int savedFirstRow = _dataGridView.FirstDisplayedScrollingRowIndex;

        _table.BeginLoadData();
        while (_table.Rows.Count < requiredRows) {
            int r = _table.Rows.Count;
            long addr = (long)r * BytesPerRow;
            string lbl = _labels.TryGetValue(addr, out string? val) ? (val ?? "") : "";
            _table.Rows.Add("00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", lbl);
        }
        while (_table.Rows.Count > requiredRows) {
            _table.Rows.RemoveAt(_table.Rows.Count - 1);
        }
        _table.EndLoadData();

        if (savedFirstRow >= 0 && savedFirstRow < _dataGridView.RowCount && _dataGridView.FirstDisplayedScrollingRowIndex != savedFirstRow) {
            _dataGridView.FirstDisplayedScrollingRowIndex = savedFirstRow;
        }
    }

    private void RefreshFormControls() {
        if (ApiContainer == null) {
            return;
        }

        ApiContainer.Memory.SetBigEndian(false);

        int totalBytes = (int)ApiContainer.Memory.GetCurrentMemoryDomainSize();
        if (totalBytes <= 0) {
            totalBytes = 0x20000;
        }

        EnsureRows(totalBytes);

        if (_table.Rows.Count == 0) {
            return;
        }

        int firstRow = _dataGridView.FirstDisplayedScrollingRowIndex;
        if (firstRow < 0) {
            firstRow = 0;
        }

        int visibleRowCount = _dataGridView.DisplayedRowCount(includePartialRow: true);
        if (visibleRowCount <= 0) {
            visibleRowCount = 50;
        }

        firstRow = Math.Min(firstRow, _table.Rows.Count - 1);
        int lastRow = Math.Min(_table.Rows.Count - 1, firstRow + visibleRowCount - 1);
        int rowsToRead = lastRow - firstRow + 1;

        int cols = BytesPerRow;
        long startAddress = (long)firstRow * cols;
        int bytesToRead = rowsToRead * cols;

        if (startAddress >= totalBytes) {
            return;
        }

        if (startAddress + bytesToRead > totalBytes) {
            bytesToRead = (int)(totalBytes - startAddress);
        }

        if (bytesToRead <= 0) {
            return;
        }

        var bytes = ApiContainer.Memory.ReadByteRange(startAddress, bytesToRead);
        if (bytes == null) {
            return;
        }

        int count = bytes.Count;
        _table.BeginLoadData();
        for (int r = 0; r < rowsToRead; r++) {
            int currentRow = firstRow + r;
            DataRow dataRow = _table.Rows[currentRow];
            int rowByteOffset = r * cols;

            for (int col = 0; col < cols; col++) {
                int byteIndex = rowByteOffset + col;
                if (byteIndex < count) {
                    string hexValue = HexStrings[bytes[byteIndex]];
                    if (!ReferenceEquals(dataRow[col], hexValue) && !Equals(dataRow[col], hexValue)) {
                        dataRow[col] = hexValue;
                    }
                }
            }
        }
        _table.EndLoadData();

        if (_dataGridView.RowCount > 0 && firstRow >= 0 && firstRow < _dataGridView.RowCount && _dataGridView.FirstDisplayedScrollingRowIndex != firstRow) {
            _dataGridView.FirstDisplayedScrollingRowIndex = firstRow;
        }
    }
    
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        if (_noteTextBox != null && _noteTextBox.Focused) {
            if (keyData == (Keys.Control | Keys.S)) {
                SaveCurrentNote();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        if (keyData == (Keys.Control | Keys.J)) {
            OpenJumpToAddressDialog();
            return true;
        }
        if (HandleIncrementDecrementKey(keyData)) {
            return true;
        }
        if (HandleAddressLabelKey(keyData)) {
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool ProcessDialogKey(Keys keyData) {
        if (_noteTextBox != null && _noteTextBox.Focused) {
            if (keyData == (Keys.Control | Keys.S)) {
                SaveCurrentNote();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
        if (keyData == (Keys.Control | Keys.J)) {
            OpenJumpToAddressDialog();
            return true;
        }
        if (HandleIncrementDecrementKey(keyData)) {
            return true;
        }
        if (HandleAddressLabelKey(keyData)) {
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessKeyPreview(ref Message m) {
        if (_noteTextBox != null && _noteTextBox.Focused) {
            return base.ProcessKeyPreview(ref m);
        }
        const int WM_KEYDOWN = 0x0100;
        if (m.Msg == WM_KEYDOWN) {
            Keys key = (Keys)(int)m.WParam | ModifierKeys;
            if (key == (Keys.Control | Keys.J)) {
                OpenJumpToAddressDialog();
                return true;
            }
            if (HandleIncrementDecrementKey(key)) {
                return true;
            }
            if (HandleAddressLabelKey(key)) {
                return true;
            }
        }
        return base.ProcessKeyPreview(ref m);
    }

    private void UpdateSelectedCellNote() {
        long address = GetCurrentSelectedAddress();
        if (address < 0) {
            _selectedAddressLabel.Text = "Note (No Selection):";
            _noteTextBox.Text = "";
            _statusLabel.Text = "";
            _currentSelectedAddress = -1;
            return;
        }

        if (address == _currentSelectedAddress) {
            return;
        }

        _currentSelectedAddress = address;
        _selectedAddressLabel.Text = $"Note for Address: ${address:X6}";
        if (_notes.TryGetValue(address, out string? note)) {
            _noteTextBox.Text = note ?? "";
        } else {
            _noteTextBox.Text = "";
        }
        _statusLabel.Text = "";
    }

    private long GetCurrentSelectedAddress() {
        if (_dataGridView.CurrentCell != null && _dataGridView.CurrentCell.RowIndex >= 0 && _dataGridView.CurrentCell.ColumnIndex >= 0) {
            int col = Math.Min(_dataGridView.CurrentCell.ColumnIndex, BytesPerRow - 1);
            return (long)_dataGridView.CurrentCell.RowIndex * BytesPerRow + col;
        }
        if (_dataGridView.SelectedCells.Count > 0) {
            var cell = _dataGridView.SelectedCells[0];
            if (cell.RowIndex >= 0 && cell.ColumnIndex >= 0) {
                int col = Math.Min(cell.ColumnIndex, BytesPerRow - 1);
                return (long)cell.RowIndex * BytesPerRow + col;
            }
        }
        if (_dataGridView.FirstDisplayedScrollingRowIndex >= 0) {
            return (long)_dataGridView.FirstDisplayedScrollingRowIndex * BytesPerRow;
        }
        return 0;
    }

    private int? GetSelectedFullRowIndex() {
        if (_dataGridView.SelectedRows.Count > 0) {
            return _dataGridView.SelectedRows[0].Index;
        }
        if (_dataGridView.SelectedCells.Count >= BytesPerRow) {
            int firstRow = _dataGridView.SelectedCells[0].RowIndex;
            for (int i = 1; i < _dataGridView.SelectedCells.Count; i++) {
                if (_dataGridView.SelectedCells[i].RowIndex != firstRow) {
                    return null;
                }
            }
            return firstRow;
        }
        return null;
    }

    private bool HandleAddressLabelKey(Keys keyData) {
        if (_isJumpDialogOpen || _isLabelDialogOpen) return false;
        if (_noteTextBox != null && _noteTextBox.Focused) return false;

        bool isCtrl = (keyData & Keys.Control) != 0;
        bool isAlt = (keyData & Keys.Alt) != 0;
        if (isCtrl || isAlt) return false;

        Keys keyCode = keyData & Keys.KeyCode;
        if (keyCode == Keys.L) {
            int? fullRow = GetSelectedFullRowIndex();
            if (fullRow.HasValue) {
                OpenAddressLabelDialog(fullRow.Value);
                return true;
            }
        }
        return false;
    }

    public void OpenAddressLabelDialog(int rowIndex) {
        if (_isLabelDialogOpen || rowIndex < 0 || rowIndex >= _table.Rows.Count) return;
        _isLabelDialogOpen = true;
        try {
            long address = (long)rowIndex * BytesPerRow;
            _labels.TryGetValue(address, out string? currentLabel);

            using var dialog = new AddressLabelDialog(address, currentLabel ?? "");
            if (dialog.ShowDialog(this) == DialogResult.OK) {
                string newLabel = dialog.AddressLabel;
                if (string.IsNullOrWhiteSpace(newLabel)) {
                    _labels.Remove(address);
                    _table.Rows[rowIndex]["Label"] = "";
                } else {
                    _labels[address] = newLabel;
                    _table.Rows[rowIndex]["Label"] = newLabel;
                }
                SaveLabelsToDisk();
                if (rowIndex < _dataGridView.RowCount) {
                    _dataGridView.InvalidateRow(rowIndex);
                }
            }
        } finally {
            _isLabelDialogOpen = false;
        }
    }

    public void SaveCurrentNote() {
        long address = _currentSelectedAddress >= 0 ? _currentSelectedAddress : GetCurrentSelectedAddress();
        if (address < 0) return;

        string text = _noteTextBox.Text;
        if (string.IsNullOrWhiteSpace(text)) {
            _notes.Remove(address);
        } else {
            _notes[address] = text;
        }

        SaveNotesToDisk();
        _statusLabel.Text = $"Saved note for ${address:X6}";
    }

    private static string GetNotesFilePath() {
        try {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir)) {
                return Path.Combine(baseDir, "hex_notes.json");
            }
        } catch { }
        return "hex_notes.json";
    }

    private void LoadNotesFromDisk() {
        try {
            string path = GetNotesFilePath();
            if (!File.Exists(path) && File.Exists("hex_notes.json")) {
                path = "hex_notes.json";
            }
            if (File.Exists(path)) {
                string content = File.ReadAllText(path);
                ParseNotesJson(content);
            }
        } catch { }
    }

    private void SaveNotesToDisk() {
        try {
            string path = GetNotesFilePath();
            string json = SerializeNotesJson();
            File.WriteAllText(path, json, Encoding.UTF8);
        } catch (Exception ex) {
            MessageBox.Show(this, $"Failed to save note to disk: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string SerializeNotesJson() {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        var sortedKeys = _notes.Keys.OrderBy(k => k).ToList();
        for (int i = 0; i < sortedKeys.Count; i++) {
            long address = sortedKeys[i];
            string note = _notes[address] ?? "";
            string escapedNote = note
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
            string trailing = (i == sortedKeys.Count - 1) ? "" : ",";
            sb.AppendLine($"  \"{address:X6}\": \"{escapedNote}\"{trailing}");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void ParseNotesJson(string json) {
        if (string.IsNullOrWhiteSpace(json)) return;
        _notes.Clear();

        var regex = new Regex("\"([^\"]+)\"\\s*:\\s*\"((?:\\\\\"|[^\"])*)\"");
        var matches = regex.Matches(json);
        foreach (Match match in matches) {
            if (match.Groups.Count >= 3) {
                string keyStr = match.Groups[1].Value;
                string valStr = match.Groups[2].Value;

                if (TryParseAddress(keyStr, out long address)) {
                    string unescaped = valStr
                        .Replace("\\n", "\n")
                        .Replace("\\r", "\r")
                        .Replace("\\t", "\t")
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");
                    _notes[address] = unescaped;
                }
            }
        }
    }

    private static string GetLabelsFilePath() {
        try {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir)) {
                return Path.Combine(baseDir, "hex_labels.json");
            }
        } catch { }
        return "hex_labels.json";
    }

    private void LoadLabelsFromDisk() {
        try {
            string path = GetLabelsFilePath();
            if (!File.Exists(path) && File.Exists("hex_labels.json")) {
                path = "hex_labels.json";
            }
            if (File.Exists(path)) {
                string content = File.ReadAllText(path);
                ParseLabelsJson(content);
            }
        } catch { }

        if (_table.Rows.Count > 0 && _labels.Count > 0) {
            for (int r = 0; r < _table.Rows.Count; r++) {
                long addr = (long)r * BytesPerRow;
                if (_labels.TryGetValue(addr, out string? lbl)) {
                    _table.Rows[r]["Label"] = lbl ?? "";
                }
            }
        }
    }

    private void SaveLabelsToDisk() {
        try {
            string path = GetLabelsFilePath();
            string json = SerializeLabelsJson();
            File.WriteAllText(path, json, Encoding.UTF8);
        } catch (Exception ex) {
            MessageBox.Show(this, $"Failed to save address label to disk: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string SerializeLabelsJson() {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        var sortedKeys = _labels.Keys.OrderBy(k => k).ToList();
        for (int i = 0; i < sortedKeys.Count; i++) {
            long address = sortedKeys[i];
            string label = _labels[address] ?? "";
            string escapedLabel = label
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
            string trailing = (i == sortedKeys.Count - 1) ? "" : ",";
            sb.AppendLine($"  \"{address:X6}\": \"{escapedLabel}\"{trailing}");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private void ParseLabelsJson(string json) {
        if (string.IsNullOrWhiteSpace(json)) return;
        _labels.Clear();

        var regex = new Regex("\"([^\"]+)\"\\s*:\\s*\"((?:\\\\\"|[^\"])*)\"");
        var matches = regex.Matches(json);
        foreach (Match match in matches) {
            if (match.Groups.Count >= 3) {
                string keyStr = match.Groups[1].Value;
                string valStr = match.Groups[2].Value;

                if (TryParseAddress(keyStr, out long address)) {
                    string unescaped = valStr
                        .Replace("\\n", "\n")
                        .Replace("\\r", "\r")
                        .Replace("\\t", "\t")
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");
                    _labels[address] = unescaped;
                }
            }
        }
    }

    private bool HandleIncrementDecrementKey(Keys keyData) {
        if (_isJumpDialogOpen || _isLabelDialogOpen) return false;

        bool isCtrl = (keyData & Keys.Control) != 0;
        bool isAlt = (keyData & Keys.Alt) != 0;
        if (isCtrl || isAlt) return false;

        Keys keyCode = keyData & Keys.KeyCode;

        if (keyCode == Keys.Add || keyCode == Keys.Oemplus) {
            ModifySelectedCell(1);
            return true;
        }
        if (keyCode == Keys.Subtract || keyCode == Keys.OemMinus) {
            ModifySelectedCell(-1);
            return true;
        }
        return false;
    }

    private void ModifySelectedCell(int delta) {
        var selectedCells = _dataGridView.SelectedCells;
        if (selectedCells.Count > 0) {
            foreach (DataGridViewCell cell in selectedCells) {
                if (cell.ColumnIndex < BytesPerRow) {
                    ModifyCell(cell.RowIndex, cell.ColumnIndex, delta);
                }
            }
        } else if (_dataGridView.CurrentCell != null && _dataGridView.CurrentCell.ColumnIndex < BytesPerRow) {
            ModifyCell(_dataGridView.CurrentCell.RowIndex, _dataGridView.CurrentCell.ColumnIndex, delta);
        }
    }

    private void ModifyCell(int row, int col, int delta) {
        if (row < 0 || col < 0 || col >= BytesPerRow || row >= _table.Rows.Count) return;

        long address = (long)row * BytesPerRow + col;
        long totalBytes = GetTotalMemorySize();
        if (address < 0 || address >= totalBytes) return;

        byte currentVal = 0;
        if (ApiContainer != null) {
            currentVal = (byte)ApiContainer.Memory.ReadByte(address);
        } else {
            string str = _table.Rows[row][col]?.ToString() ?? "00";
            byte.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out currentVal);
        }

        byte newVal = (byte)(currentVal + delta);

        if (ApiContainer != null) {
            ApiContainer.Memory.WriteByte(address, newVal);
        }

        _modifiedAddresses.Add(address);
        _table.Rows[row][col] = HexStrings[newVal];
        if (row < _dataGridView.RowCount && col < _dataGridView.ColumnCount) {
            _dataGridView.InvalidateCell(col, row);
        }
    }

    public void OpenJumpToAddressDialog() {
        if (_isJumpDialogOpen || _isLabelDialogOpen) return;
        _isJumpDialogOpen = true;
        try {
            long currentAddress = 0;
            if (_dataGridView.CurrentCell != null) {
                int col = Math.Min(_dataGridView.CurrentCell.ColumnIndex, BytesPerRow - 1);
                currentAddress = (long)_dataGridView.CurrentCell.RowIndex * BytesPerRow + col;
            } else if (_dataGridView.FirstDisplayedScrollingRowIndex >= 0) {
                currentAddress = (long)_dataGridView.FirstDisplayedScrollingRowIndex * BytesPerRow;
            }

            long totalBytes = GetTotalMemorySize();

            using var dialog = new JumpToAddressDialog(currentAddress, totalBytes);
            if (dialog.ShowDialog(this) == DialogResult.OK) {
                JumpToAddress(dialog.TargetAddress);
            }
        } finally {
            _isJumpDialogOpen = false;
        }
    }

    public void JumpToAddress(long address) {
        int totalBytes = (int)GetTotalMemorySize();
        EnsureRows(totalBytes);

        if (_table.Rows.Count == 0) return;

        int targetRow = (int)(address / BytesPerRow);
        int targetCol = (int)(address % BytesPerRow);

        if (targetRow < 0) targetRow = 0;
        if (targetRow >= _dataGridView.RowCount) targetRow = _dataGridView.RowCount - 1;
        if (targetCol < 0) targetCol = 0;
        if (targetCol >= BytesPerRow) targetCol = BytesPerRow - 1;

        if (_dataGridView.RowCount > 0 && targetRow >= 0 && targetRow < _dataGridView.RowCount) {
            _dataGridView.FirstDisplayedScrollingRowIndex = targetRow;
        }

        RefreshFormControls();

        if (targetRow < _dataGridView.RowCount && targetCol < _dataGridView.ColumnCount) {
            _dataGridView.ClearSelection();
            _dataGridView.CurrentCell = _dataGridView.Rows[targetRow].Cells[targetCol];
            _dataGridView.Rows[targetRow].Cells[targetCol].Selected = true;
            _dataGridView.Focus();
        }
    }

    private long GetTotalMemorySize() {
        if (ApiContainer != null) {
            long size = ApiContainer.Memory.GetCurrentMemoryDomainSize();
            if (size > 0) {
                return size;
            }
        }
        if (_table.Rows.Count > 0) {
            return (long)_table.Rows.Count * BytesPerRow;
        }
        return 0x20000;
    }

    public static bool TryParseAddress(string? input, out long address) {
        address = 0;
        if (string.IsNullOrWhiteSpace(input)) {
            return false;
        }

        string text = input!.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            text = text.Substring(2).Trim();
        } else if (text.StartsWith("$") || text.StartsWith("#")) {
            text = text.Substring(1).Trim();
        }

        if (text.EndsWith("h", StringComparison.OrdinalIgnoreCase)) {
            text = text.Substring(0, text.Length - 1).Trim();
        }

        text = text.Replace(":", "").Replace(" ", "");

        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address) && address >= 0;
    }

    private sealed class JumpToAddressDialog : Form {
        private readonly TextBox _addressTextBox;
        private readonly long _maxAddress;

        public long TargetAddress { get; private set; }

        public JumpToAddressDialog(long currentAddress, long maxAddress) {
            _maxAddress = maxAddress;

            Text = "Jump to Address";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(280, 115);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            BackColor = Color.FromArgb(37, 37, 38);
            ForeColor = Color.FromArgb(220, 220, 220);

            Label label = new Label {
                Text = "Enter Address (Hex):",
                Location = new Point(14, 12),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 220)
            };

            _addressTextBox = new TextBox {
                Location = new Point(16, 34),
                Size = new Size(248, 23),
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.FixedSingle,
                Text = currentAddress >= 0 ? currentAddress.ToString("X") : "0"
            };

            Button okButton = new Button {
                Text = "Jump",
                Location = new Point(108, 72),
                Size = new Size(75, 26),
                BackColor = Color.FromArgb(14, 99, 156),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            okButton.FlatAppearance.BorderSize = 0;

            Button cancelButton = new Button {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(189, 72),
                Size = new Size(75, 26),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.FromArgb(220, 220, 220),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            cancelButton.FlatAppearance.BorderSize = 0;

            okButton.Click += (sender, e) => {
                if (TryParseAddress(_addressTextBox.Text, out long parsedAddress)) {
                    if (parsedAddress < 0 || (_maxAddress > 0 && parsedAddress >= _maxAddress)) {
                        string maxStr = _maxAddress > 0 ? (_maxAddress - 1).ToString("X6") : "FFFFFF";
                        MessageBox.Show(
                            this,
                            $"Address is out of range.\nValid range: $000000 - ${maxStr}",
                            "Invalid Address",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                        _addressTextBox.Focus();
                        _addressTextBox.SelectAll();
                        return;
                    }
                    TargetAddress = parsedAddress;
                    DialogResult = DialogResult.OK;
                    Close();
                } else {
                    MessageBox.Show(
                        this,
                        "Please enter a valid hexadecimal address (e.g. 7E0000, $1234, 0x100).",
                        "Invalid Address",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    _addressTextBox.Focus();
                    _addressTextBox.SelectAll();
                }
            };

            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.Add(label);
            Controls.Add(_addressTextBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            Shown += (sender, e) => {
                _addressTextBox.Focus();
                _addressTextBox.SelectAll();
            };
        }
    }

    private sealed class AddressLabelDialog : Form {
        private readonly TextBox _labelTextBox;
        public string AddressLabel => _labelTextBox.Text.Trim();

        public AddressLabelDialog(long address, string currentLabel) {
            Text = "Address Label";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(320, 120);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            BackColor = Color.FromArgb(37, 37, 38);
            ForeColor = Color.FromArgb(220, 220, 220);

            Label label = new Label {
                Text = $"Enter Address Label for ${address:X6}:",
                Location = new Point(14, 12),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 220, 220)
            };

            _labelTextBox = new TextBox {
                Location = new Point(16, 36),
                Size = new Size(288, 23),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.FixedSingle,
                Text = currentLabel ?? ""
            };

            Button okButton = new Button {
                Text = "Save",
                DialogResult = DialogResult.OK,
                Location = new Point(148, 76),
                Size = new Size(75, 26),
                BackColor = Color.FromArgb(14, 99, 156),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            okButton.FlatAppearance.BorderSize = 0;

            Button cancelButton = new Button {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(229, 76),
                Size = new Size(75, 26),
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.FromArgb(220, 220, 220),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
            cancelButton.FlatAppearance.BorderSize = 0;

            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.Add(label);
            Controls.Add(_labelTextBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            Shown += (_, _) => {
                _labelTextBox.Focus();
                _labelTextBox.SelectAll();
            };
        }
    }
}