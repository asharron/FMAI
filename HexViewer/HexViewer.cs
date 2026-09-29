using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
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

    private static readonly string[] HexStrings = Enumerable.Range(0, 256).Select(b => b.ToString("X2")).ToArray();
    private readonly DataTable _table;
    private readonly DataGridView _dataGridView;
    private bool _isJumpDialogOpen;

    public HexViewer() {
        KeyPreview = true;
        ClientSize = new Size(480, 320);
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

        _table.BeginLoadData();
        for (int i = 0; i < 8192; i++) {
            _table.Rows.Add("00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00");
        }
        _table.EndLoadData();

        Font monoFont = new Font("Consolas", 9.5f, FontStyle.Regular);
        Font monoBoldFont = new Font("Consolas", 9.5f, FontStyle.Bold);

        _dataGridView = new DataGridView {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = Color.FromArgb(226, 230, 236),
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
                BackColor = Color.White,
                ForeColor = Color.FromArgb(20, 20, 20),
                SelectionBackColor = Color.FromArgb(198, 226, 255),
                SelectionForeColor = Color.FromArgb(0, 0, 0),
                Padding = new Padding(2, 0, 2, 0),
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle {
                Font = monoFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(246, 248, 250),
                ForeColor = Color.FromArgb(20, 20, 20),
                SelectionBackColor = Color.FromArgb(198, 226, 255),
                SelectionForeColor = Color.FromArgb(0, 0, 0),
                Padding = new Padding(2, 0, 2, 0),
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Font = monoBoldFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(238, 241, 245),
                ForeColor = Color.FromArgb(50, 55, 65),
                Padding = new Padding(0),
            },
            RowHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Font = monoBoldFont,
                Alignment = DataGridViewContentAlignment.MiddleRight,
                BackColor = Color.FromArgb(238, 241, 245),
                ForeColor = Color.FromArgb(70, 75, 85),
                Padding = new Padding(0),
            },
        };

        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_dataGridView, true, null);

        _dataGridView.DataBindingComplete += (sender, e) => {
            _dataGridView.ClearSelection();
            _dataGridView.CurrentCell = null;

            foreach (DataGridViewColumn col in _dataGridView.Columns) {
                col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                col.HeaderCell.Style.Font = monoBoldFont;
                col.HeaderCell.Style.BackColor = Color.FromArgb(238, 241, 245);
                col.HeaderCell.Style.ForeColor = Color.FromArgb(50, 55, 65);
                col.HeaderCell.Style.Padding = new Padding(0);
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

                TextRenderer.DrawText(
                    e.Graphics,
                    headerText,
                    headerFont,
                    e.CellBounds,
                    e.CellStyle.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
                e.Handled = true;
            }
        };

        _dataGridView.RowPostPaint += (sender, e) => {
            var grid = sender as DataGridView;
            if (grid == null) return;

            long address = (long)e.RowIndex * _table.Columns.Count;
            string addressText = address.ToString("X6");

            // Format the text alignment inside the header
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right;

            // Calculate bounds for drawing text with right margin
            Rectangle headerBounds = new Rectangle(
                e.RowBounds.Left, 
                e.RowBounds.Top, 
                grid.RowHeadersWidth - 6, 
                e.RowBounds.Height
            );

            Font font = grid.RowHeadersDefaultCellStyle.Font ?? monoBoldFont;
            Color foreColor = grid.RowHeadersDefaultCellStyle.ForeColor;

            // Draw the row header text
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

        _dataGridView.KeyDown += (sender, e) => {
            if (e.Control && e.KeyCode == Keys.J) {
                OpenJumpToAddressDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
            } else if (HandleIncrementDecrementKey(e.KeyData)) {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        _dataGridView.PreviewKeyDown += (sender, e) => {
            if (e.Control && e.KeyCode == Keys.J) {
                e.IsInputKey = true;
            } else if (!e.Control && !e.Alt && (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)) {
                e.IsInputKey = true;
            }
        };

        KeyDown += (sender, e) => {
            if (e.Control && e.KeyCode == Keys.J) {
                OpenJumpToAddressDialog();
                e.Handled = true;
                e.SuppressKeyPress = true;
            } else if (HandleIncrementDecrementKey(e.KeyData)) {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        ContextMenuStrip contextMenu = new ContextMenuStrip();
        ToolStripMenuItem jumpMenuItem = new ToolStripMenuItem("Jump to Address...", null, (sender, e) => OpenJumpToAddressDialog()) {
            ShortcutKeys = Keys.Control | Keys.J,
            ShowShortcutKeys = true
        };
        contextMenu.Items.Add(jumpMenuItem);
        _dataGridView.ContextMenuStrip = contextMenu;
        ContextMenuStrip = contextMenu;

        _dataGridView.DataSource = _table;

        Controls.Add(_dataGridView);

        ResumeLayout(performLayout: false);
        PerformLayout();

        Load += (_, _) => IsLoaded = true;
        Activated += (_, _) => IsActive = true;
        Deactivate += (_, _) => IsActive = false;
        FormClosed += (_, _) => IsLoaded = false;

        Shown += (_, _) => {
            ApiContainer?.SaveState.LoadSlot(1);
            RefreshFormControls();
        };
    }


    private void EnsureRows(int totalBytes) {
        int requiredRows = (totalBytes + _table.Columns.Count - 1) / _table.Columns.Count;
        if (_table.Rows.Count == requiredRows) return;

        int savedFirstRow = _dataGridView.FirstDisplayedScrollingRowIndex;

        _table.BeginLoadData();
        while (_table.Rows.Count < requiredRows) {
            _table.Rows.Add("00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00");
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

        int cols = _table.Columns.Count;
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
        if (keyData == (Keys.Control | Keys.J)) {
            OpenJumpToAddressDialog();
            return true;
        }
        if (HandleIncrementDecrementKey(keyData)) {
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool ProcessDialogKey(Keys keyData) {
        if (keyData == (Keys.Control | Keys.J)) {
            OpenJumpToAddressDialog();
            return true;
        }
        if (HandleIncrementDecrementKey(keyData)) {
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessKeyPreview(ref Message m) {
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
        }
        return base.ProcessKeyPreview(ref m);
    }

    private bool HandleIncrementDecrementKey(Keys keyData) {
        if (_isJumpDialogOpen) return false;

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
                ModifyCell(cell.RowIndex, cell.ColumnIndex, delta);
            }
        } else if (_dataGridView.CurrentCell != null) {
            ModifyCell(_dataGridView.CurrentCell.RowIndex, _dataGridView.CurrentCell.ColumnIndex, delta);
        }
    }

    private void ModifyCell(int row, int col, int delta) {
        if (row < 0 || col < 0 || row >= _table.Rows.Count || col >= _table.Columns.Count) return;

        long address = (long)row * _table.Columns.Count + col;
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

        _table.Rows[row][col] = HexStrings[newVal];
    }

    public void OpenJumpToAddressDialog() {
        if (_isJumpDialogOpen) return;
        _isJumpDialogOpen = true;
        try {
            long currentAddress = 0;
            if (_dataGridView.CurrentCell != null) {
                currentAddress = (long)_dataGridView.CurrentCell.RowIndex * _table.Columns.Count + _dataGridView.CurrentCell.ColumnIndex;
            } else if (_dataGridView.FirstDisplayedScrollingRowIndex >= 0) {
                currentAddress = (long)_dataGridView.FirstDisplayedScrollingRowIndex * _table.Columns.Count;
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

        int cols = _table.Columns.Count;
        if (cols <= 0 || _table.Rows.Count == 0) return;

        int targetRow = (int)(address / cols);
        int targetCol = (int)(address % cols);

        if (targetRow < 0) targetRow = 0;
        if (targetRow >= _dataGridView.RowCount) targetRow = _dataGridView.RowCount - 1;
        if (targetCol < 0) targetCol = 0;
        if (targetCol >= _dataGridView.ColumnCount) targetCol = _dataGridView.ColumnCount - 1;

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
            return (long)_table.Rows.Count * _table.Columns.Count;
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

            Label label = new Label {
                Text = "Enter Address (Hex):",
                Location = new Point(14, 12),
                AutoSize = true
            };

            _addressTextBox = new TextBox {
                Location = new Point(16, 34),
                Size = new Size(248, 23),
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                Text = currentAddress >= 0 ? currentAddress.ToString("X") : "0"
            };

            Button okButton = new Button {
                Text = "Jump",
                Location = new Point(108, 72),
                Size = new Size(75, 26),
                UseVisualStyleBackColor = true
            };

            Button cancelButton = new Button {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(189, 72),
                Size = new Size(75, 26),
                UseVisualStyleBackColor = true
            };

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
}