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

        var dataGridView = new DataGridView {
            Dock = DockStyle.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding {
                    All = 0,
                    Top = 0,
                    Bottom = 0,
                    Left = 0,
                    Right = 0,
                }
            },
        };

        dataGridView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        dataGridView.DataBindingComplete += (sender, e) => {
            dataGridView.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView.ColumnHeadersDefaultCellStyle.Padding = new Padding {
                All = 0,
                Top = 0,
                Bottom = 0,
                Left = 0,
                Right = 0,
            };

            foreach (DataGridViewColumn col in dataGridView.Columns) {
                col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                col.HeaderCell.Style.Padding = new Padding {
                    All = 0,
                    Top = 0,
                    Bottom = 0,
                    Left = 0,
                    Right = 0,
                };
            }
        };

        dataGridView.CellPainting += (sender, e) => {
            if (e.ColumnIndex < 0 || e.RowIndex != -1) {
                return;
            }
            
            DataGridView? dgv = sender as DataGridView;
            if (dgv == null) return;
            SortOrder sort = dgv.Columns[e.ColumnIndex].HeaderCell.SortGlyphDirection;

            if (e.RowIndex == -1 && sort == SortOrder.None) {
                string headerText = dgv.Columns[e.ColumnIndex].HeaderText;
                Font headerFont = e.CellStyle.Font;
                Brush headerBrush = new SolidBrush(e.CellStyle.ForeColor);
                DataGridViewContentAlignment headerAlignment = e.CellStyle.Alignment;

                e.Paint(e.ClipBounds, (DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground));

                SizeF stringSize =
                    TextRenderer.MeasureText(e.Graphics, headerText, e.CellStyle.Font, e.CellBounds.Size);
                Rectangle p = e.CellBounds;
                switch (headerAlignment) {
                    case DataGridViewContentAlignment.TopCenter:
                        p.Offset(
                            e.CellBounds.Width / 2 - (int)(stringSize.Width / 2),
                            0
                        );
                        break;
                    case DataGridViewContentAlignment.TopRight:
                        p.Offset(
                            e.CellBounds.Width - (int)stringSize.Width,
                            0
                        );
                        break;
                    case DataGridViewContentAlignment.MiddleLeft:
                        p.Offset(
                            0,
                            e.CellBounds.Height / 2 - (int)(stringSize.Height / 2)
                        );
                        break;
                    case DataGridViewContentAlignment.MiddleCenter:
                        p.Offset(
                            e.CellBounds.Width / 2 - (int)(stringSize.Width / 2),
                            e.CellBounds.Height / 2 - (int)(stringSize.Height / 2)
                        );
                        break;
                    case DataGridViewContentAlignment.MiddleRight:
                        p.Offset(
                            e.CellBounds.Width - (int)stringSize.Width,
                            e.CellBounds.Height / 2 - (int)(stringSize.Height / 2)
                        );
                        break;
                    case DataGridViewContentAlignment.BottomLeft:
                        p.Offset(
                            0,
                            e.CellBounds.Height - (int)stringSize.Height
                        );
                        break;
                    case DataGridViewContentAlignment.BottomCenter:
                        p.Offset(
                            e.CellBounds.Width / 2 - (int)(stringSize.Width / 2),
                            e.CellBounds.Height - (int)stringSize.Height
                        );
                        break;
                    case DataGridViewContentAlignment.BottomRight:
                        p.Offset(
                            e.CellBounds.Width - (int)stringSize.Width,
                            e.CellBounds.Height - (int)stringSize.Height
                        );
                        break;
                    default:
                        p.Offset(
                            0,
                            0
                        );
                        break;
                }

                e.Graphics.DrawString(headerText, headerFont, headerBrush, new PointF(p.X, p.Y));
                e.Handled = true;
            }
        };

        dataGridView.RowPostPaint += (sender, e) => {
            var grid = sender as DataGridView;
            if (grid == null) return;

            // Convert zero-based index to 1-based display number
            string rowNumber = (e.RowIndex + 1).ToString();

            // Format the text alignment inside the header
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right;

            // Calculate bounds for drawing text
            Rectangle headerBounds = new Rectangle(
                e.RowBounds.Left, 
                e.RowBounds.Top, 
                grid.RowHeadersWidth, 
                e.RowBounds.Height
            );

            // Draw the row header text
            TextRenderer.DrawText(
                e.Graphics, 
                rowNumber, 
                grid.RowHeadersDefaultCellStyle.Font, 
                headerBounds, 
                grid.RowHeadersDefaultCellStyle.ForeColor, 
                flags
            );
        };

        dataGridView.DataSource = _table;

        Controls.Add(dataGridView);

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

        _table.BeginLoadData();
        while (_table.Rows.Count < requiredRows) {
            _table.Rows.Add("00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00", "00");
        }
        while (_table.Rows.Count > requiredRows) {
            _table.Rows.RemoveAt(_table.Rows.Count - 1);
        }
        _table.EndLoadData();
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

        var bytes = ApiContainer.Memory.ReadByteRange(0, totalBytes);
        if (bytes == null) {
            return;
        }

        int count = bytes.Count;
        int cols = _table.Columns.Count;
        int rows = _table.Rows.Count;

        for (int row = 0; row < rows; row++) {
            DataRow dataRow = _table.Rows[row];
            int rowOffset = row * cols;
            for (int col = 0; col < cols; col++) {
                int index = rowOffset + col;
                if (index < count) {
                    string hexValue = HexStrings[bytes[index]];
                    if (!ReferenceEquals(dataRow[col], hexValue) && !Equals(dataRow[col], hexValue)) {
                        dataRow[col] = hexValue;
                    }
                }
            }
        }
    }
}