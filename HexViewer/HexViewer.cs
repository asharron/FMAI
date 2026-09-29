using System;
using System.Collections.Generic;
using System.Data;
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

    public HexViewer() {
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
}