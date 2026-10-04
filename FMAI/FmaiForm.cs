using System;
using System.Collections.Generic;
using System.Linq;
using BizHawk.Emulation.Common;
using BizHawk.Client.Common;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Label = System.Windows.Forms.Label;

namespace FMAI;

[ExternalTool("FMAI")]
[ExternalToolApplicability.RomList(VSystemID.Raw.SNES, "A1C2BA17E44B8E69B6118F1E12DCEFC32EB93387")]
public class FmaiForm : Form, IExternalToolForm {
    public bool IsActive { get; private set; }
    public bool IsLoaded { get; private set; }

    [RequiredApi] public ApiContainer? ApiContainer { get; set; }

    [RequiredApi] public IEmulationApi? EmulationApi { get; set; }

    private readonly System.Windows.Forms.Label jevLabel = new System.Windows.Forms.Label { AutoSize = true };

    private readonly Jev jev = new Jev();

    private readonly DataGridView movementBitmaskGridView = new DataGridView();
    private readonly DataGridView movementCostGridView = new DataGridView();
    private readonly DataGridView movementXYGrid = new DataGridView();

    private readonly List<(Common.RamValue RamValue, System.Windows.Forms.Label Label)> rows =
        Common.TrackedValues.Select(v => (v, new Label { AutoSize = true })).ToList();

    private readonly TabControl tabControl = new TabControl { Dock = DockStyle.Fill };
    private int frameCount = 0;

    private readonly TabPage tab3 = new TabPage("Test Movement");
    private Common.TileGridCoordinate tileToMoveTo = new Common.TileGridCoordinate(0, 0);
    private bool isMovingToTile = false;

    public FmaiForm() {
        ClientSize = new Size(480, 320);
        SuspendLayout();

        TabPage tab1 = new TabPage("Ram Value Editor");

        var root = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        CreateMovementGridViewerTable();
        CreateFormControls(root);
        tab1.Controls.Add(root);

        TabPage tab2 = new TabPage("Movement Grid Viewer");

        var scrollPanel = new Panel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };
        var gridLayout = new TableLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
        };

        gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));

        gridLayout.Controls.Add(movementBitmaskGridView, 0, 0);
        gridLayout.Controls.Add(movementCostGridView, 1, 0);
        gridLayout.Controls.Add(movementXYGrid, 2, 0);

        scrollPanel.Controls.Add(gridLayout);
        tab2.Controls.Add(scrollPanel);

        tabControl.TabPages.Add(tab1);
        tabControl.TabPages.Add(tab2);
        tabControl.TabPages.Add(tab3);
        Controls.Add(tabControl);
        ResumeLayout(performLayout: false);
        PerformLayout();

        Load += (_, _) => IsLoaded = true;
        Activated += (_, _) => IsActive = true;
        Deactivate += (_, _) => IsActive = false;
        FormClosed += (_, _) => IsLoaded = false;

        Shown += (_, _) => { ApiContainer?.SaveState.LoadSlot(1); };
    }

    private void CreateFormControls(Control rootControl) {
        Controls.Add(jevLabel);
        // jev.MakeJevRequest().ContinueWith(task => {
        //     jevLabel.Text = task.Result;
        // }, TaskScheduler.FromCurrentSynchronizationContext());

        foreach (var group in rows.GroupBy(r => r.RamValue.Character)) {
            var box = new GroupBox {
                Text = group.Key,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
            };

            var inner = new FlowLayoutPanel {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };

            foreach (var row in group) {
                var rowControl = new FlowLayoutPanel {
                    FlowDirection = FlowDirection.LeftToRight,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                };

                var incrementButton = new Button { Text = "+", AutoSize = true };
                var decrementButton = new Button { Text = "-", AutoSize = true };

                incrementButton.Click += (_, _) => {
                    if (ApiContainer == null) {
                        return;
                    }

                    var currentValue = RamValueToInt(row.RamValue);
                    currentValue++;

                    var byteValue = BitConverter.GetBytes(currentValue);
                    ApiContainer.Memory.WriteByteRange(row.RamValue.Address, byteValue);
                };

                decrementButton.Click += (_, _) => {
                    if (ApiContainer == null) {
                        return;
                    }

                    var currentValue = RamValueToInt(row.RamValue);
                    currentValue--;

                    var byteValue = BitConverter.GetBytes(currentValue);
                    ApiContainer.Memory.WriteByteRange(row.RamValue.Address, byteValue);
                };

                rowControl.Controls.Add(row.Label);
                rowControl.Controls.Add(incrementButton);
                rowControl.Controls.Add(decrementButton);

                inner.Controls.Add(rowControl);
            }

            box.Controls.Add(inner);
            rootControl.Controls.Add(box);
        }
    }

    private void CreateMovementGridViewerTable() {
        movementBitmaskGridView.SuspendLayout();
        movementCostGridView.SuspendLayout();
        movementXYGrid.SuspendLayout();

        movementBitmaskGridView.AutoSize = true;
        movementBitmaskGridView.Dock = DockStyle.Fill;
        movementBitmaskGridView.ScrollBars = ScrollBars.Both;

        movementBitmaskGridView.ColumnCount = 16;

        for (var i = 1; i <= 16; i++) {
            movementBitmaskGridView.Columns[i - 1].HeaderText = i.ToString();
            movementBitmaskGridView.Columns[i - 1].MinimumWidth = 20;
        }

        movementBitmaskGridView.Rows.Clear();

        for (var i = 0; i < (736 / 16); i++) {
            movementBitmaskGridView.Rows.Add(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0
            );
            movementBitmaskGridView.Rows[i].HeaderCell.Value = (i + 1).ToString();
        }

        movementBitmaskGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        movementBitmaskGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;


        movementCostGridView.AutoSize = true;
        movementCostGridView.Dock = DockStyle.Fill;
        movementCostGridView.ScrollBars = ScrollBars.Both;

        movementCostGridView.ColumnCount = 16;

        movementCostGridView.Rows.Clear();

        for (var i = 1; i <= 16; i++) {
            movementCostGridView.Columns[i - 1].HeaderText = i.ToString();
            movementBitmaskGridView.Columns[i - 1].MinimumWidth = 20;
        }

        for (var i = 0; i < (736 / 16); i++) {
            movementCostGridView.Rows.Add(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0
            );
            movementCostGridView.Rows[i].HeaderCell.Value = (i + 1).ToString();
        }

        movementCostGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        movementCostGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;


        movementXYGrid.AutoSize = true;
        movementXYGrid.Dock = DockStyle.Fill;
        movementXYGrid.ScrollBars = ScrollBars.Both;

        movementXYGrid.ColumnCount = 16;

        movementXYGrid.Rows.Clear();

        for (var i = 1; i <= 16; i++) {
            movementXYGrid.Columns[i - 1].HeaderText = i.ToString();
            movementBitmaskGridView.Columns[i - 1].MinimumWidth = 20;
        }

        var col = 1;
        for (var i = 0; i < (736 / 16); i++) {
            var x = 1;

            var tile1 = BitmaskToTileGridCoordinate(x++, col);
            var tile2 = BitmaskToTileGridCoordinate(x++, col);
            var tile3 = BitmaskToTileGridCoordinate(x++, col);
            var tile4 = BitmaskToTileGridCoordinate(x++, col);
            var tile5 = BitmaskToTileGridCoordinate(x++, col);
            var tile6 = BitmaskToTileGridCoordinate(x++, col);
            var tile7 = BitmaskToTileGridCoordinate(x++, col);
            var tile8 = BitmaskToTileGridCoordinate(x++, col);
            var tile9 = BitmaskToTileGridCoordinate(x++, col);
            var tile10 = BitmaskToTileGridCoordinate(x++, col);
            var tile11 = BitmaskToTileGridCoordinate(x++, col);
            var tile12 = BitmaskToTileGridCoordinate(x++, col);
            var tile13 = BitmaskToTileGridCoordinate(x++, col);
            var tile14 = BitmaskToTileGridCoordinate(x++, col);
            var tile15 = BitmaskToTileGridCoordinate(x++, col);
            var tile16 = BitmaskToTileGridCoordinate(x++, col);
            
            movementXYGrid.Rows.Add(
                $"x:{tile1.x}, y: {tile1.y}",
                $"x:{tile2.x}, y: {tile2.y}",
                $"x:{tile3.x}, y: {tile3.y}",
                $"x:{tile4.x}, y: {tile4.y}",
                $"x:{tile5.x}, y: {tile5.y}",
                $"x:{tile6.x}, y: {tile6.y}",
                $"x:{tile7.x}, y: {tile7.y}",
                $"x:{tile8.x}, y: {tile8.y}",
                $"x:{tile9.x}, y: {tile9.y}",
                $"x:{tile10.x}, y: {tile10.y}",
                $"x:{tile11.x}, y: {tile11.y}",
                $"x:{tile12.x}, y: {tile12.y}",
                $"x:{tile13.x}, y: {tile13.y}",
                $"x:{tile14.x}, y: {tile14.y}",
                $"x:{tile15.x}, y: {tile15.y}",
                $"x:{tile16.x}, y: {tile16.y}"
            );
            movementXYGrid.Rows[i].HeaderCell.Value = (i + 1).ToString();

            col++;
        }

        movementXYGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        movementXYGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        movementBitmaskGridView.ResumeLayout();
        movementCostGridView.ResumeLayout();
        movementXYGrid.ResumeLayout();
    }

    private void RefreshMovementGridViewer() {
        if (ApiContainer == null) {
            return;
        }

        frameCount++;
        if (frameCount % 10 != 0) {
            return;
        }

        frameCount = 0;

        var bitmaskBytes =
            ApiContainer.Memory.ReadByteRange(Common.MovementBitmaskGrid.Address, Common.MovementBitmaskGrid.Length);
        for (var i = 0; i < bitmaskBytes.Count; i++) {
            var row = i / 16;
            var col = i % 16;

            if (row >= movementBitmaskGridView.Rows.Count) {
                break;
            }

            movementBitmaskGridView.Rows[row].Cells[col].Value = bitmaskBytes[i];

            if (bitmaskBytes[i] == 1) {
                movementBitmaskGridView.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.Blue);
                movementXYGrid.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.Wheat);
            }
            else if (bitmaskBytes[i] == 145) {
                movementBitmaskGridView.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.Green);
                movementXYGrid.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.Green);
            }
            else {
                movementBitmaskGridView.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.White);
                movementXYGrid.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.White);
            }
        }

        var costBytes =
            ApiContainer.Memory.ReadByteRange(Common.MovementCostGrid.Address, Common.MovementCostGrid.Length);
        for (var i = 0; i < costBytes.Count; i++) {
            var row = i / 16;
            var col = i % 16;

            if (row >= movementCostGridView.Rows.Count) {
                break;
            }

            movementCostGridView.Rows[row].Cells[col].Value = costBytes[i];

            if (costBytes[i] > 0 && costBytes[i] < 255) {
                movementCostGridView.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.Blue);
            }
            else {
                movementCostGridView.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.White);
            }
        }
    }

    private ushort RamValueToInt(Common.RamValue ramValue) {
        if (ApiContainer == null) {
            return 0;
        }

        var bytes = ApiContainer.Memory.ReadByteRange(ramValue.Address, ramValue.Length);
        var byteArray = bytes.ToArray();

        return ramValue.Length switch {
            1 => byteArray[0],
            2 => BitConverter.ToUInt16(byteArray, 0),
            _ => throw new NotSupportedException(
                $"Ram value length not supported or implemented yet: {ramValue.Length}")
        };
    }

    private Common.TileGridCoordinate
        BitmaskToTileGridCoordinate(int row, int col, int baseLength = 17, int width = 30) {
        var offset = row * 16 + col - baseLength;
        return new Common.TileGridCoordinate(offset % width, offset / width);
    }

    private void RefreshTabMovementButtons() {
        if (ApiContainer is null) {
            return;
        }

        frameCount++;
        if (frameCount % 30 != 0) {
            return;
        }

        frameCount = 0;

        tab3.Controls.Clear();

        var container = new FlowLayoutPanel {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var bitmaskBytes =
            ApiContainer.Memory.ReadByteRange(Common.MovementBitmaskGrid.Address, Common.MovementBitmaskGrid.Length);

        for (var i = 0; i < bitmaskBytes.Count; i++) {
            var row = i / 16;
            var col = i % 16;

            if (bitmaskBytes[i] != 1) {
                continue;
            }

            var xyCoordinate = BitmaskToTileGridCoordinate(row+1, col+1);
            var button = new Button { AutoSize = true, Text = $"x: {xyCoordinate.x} y: {xyCoordinate.y}" };

            button.Click += (sender, args) => TriggerMoveToTile(xyCoordinate);

            container.Controls.Add(button);
        }
        
        tab3.Controls.Add(container);
    }

    private void TriggerMoveToTile(Common.TileGridCoordinate xyCoordinate) {
        tileToMoveTo = xyCoordinate;
        isMovingToTile = true;
    }

    private void MoveToTile() {
        if (ApiContainer == null) {
            return;
        }

        var selectedTileXRamValue =
            Common.TrackedValues.FirstOrDefault(value => value.Stat == "Selected Tile X");
        var selectedTileYRamValue =
            Common.TrackedValues.FirstOrDefault(value => value.Stat == "Selected Tile Y");

        if (selectedTileYRamValue is null || selectedTileXRamValue is null) {
            return;
        }

        var xPosition = ApiContainer.Memory.ReadByte(selectedTileXRamValue.Address);
        var yPosition = ApiContainer.Memory.ReadByte(selectedTileYRamValue.Address);

        if (xPosition < tileToMoveTo.x) {
            ApiContainer.Joypad.Set("Right", true, 1);
        } else if (xPosition > tileToMoveTo.x) {
            ApiContainer.Joypad.Set("Left", true, 1);
        }
                
        if (yPosition < tileToMoveTo.y) {
            ApiContainer.Joypad.Set("Down", true, 1);
        } else if (yPosition > tileToMoveTo.y) {
            ApiContainer.Joypad.Set("Up", true, 1);
        }

        if (xPosition == tileToMoveTo.x && yPosition == tileToMoveTo.y) {
            isMovingToTile = false;
        }
    }

    private void RefreshFormControls() {
        SuspendLayout();
        foreach (var (value, label) in rows) {
            label.Text = value.Description + ": " + RamValueToInt(value);
        }

        RefreshMovementGridViewer();
        RefreshTabMovementButtons();
        MoveToTile();
        ResumeLayout();
    }

    public void UpdateValues(ToolFormUpdateType type) => RefreshFormControls();
    public void Restart() => RefreshFormControls();
    public bool AskSaveChanges() => true;
}