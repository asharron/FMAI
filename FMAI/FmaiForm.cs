using System;
using System.Collections.Generic;
using System.Linq;
using BizHawk.Emulation.Common;
using BizHawk.Client.Common;
using System.Windows.Forms;
using System.Drawing;
using System.Threading.Tasks;

namespace FMAI;

[ExternalTool("FMAI")]
[ExternalToolApplicability.RomList(VSystemID.Raw.SNES, "A1C2BA17E44B8E69B6118F1E12DCEFC32EB93387")]
public class FmaiForm : Form, IExternalToolForm {
    public bool IsActive { get; private set; }
    public bool IsLoaded { get; private set; }

    [RequiredApi] public ApiContainer? ApiContainer { get; set; }

    [RequiredApi] public IEmulationApi? EmulationApi { get; set; }

    private readonly Label jevLabel = new Label { AutoSize = true };

    private readonly Jev jev = new Jev();

    private readonly DataGridView movementBitmaskGridView = new DataGridView();
    private readonly DataGridView movementCostGridView = new DataGridView();

    private readonly List<(Common.RamValue RamValue, Label Label)> rows =
        Common.TrackedValues.Select(v => (v, new Label { AutoSize = true })).ToList();

    private readonly TabControl tabControl = new TabControl { Dock = DockStyle.Fill };

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
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };

        gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        gridLayout.Controls.Add(movementBitmaskGridView, 0, 0);
        gridLayout.Controls.Add(movementCostGridView, 1, 0);

        scrollPanel.Controls.Add(gridLayout);
        tab2.Controls.Add(scrollPanel);

        tabControl.TabPages.Add(tab1);
        tabControl.TabPages.Add(tab2);
        Controls.Add(tabControl);
        ResumeLayout(performLayout: false);
        PerformLayout();

        Load += (_, _) => IsLoaded = true;
        Activated += (_, _) => IsActive = true;
        Deactivate += (_, _) => IsActive = true;
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

        movementBitmaskGridView.AutoSize = true;
        movementBitmaskGridView.Dock = DockStyle.Fill;
        movementBitmaskGridView.ScrollBars = ScrollBars.Both;

        movementBitmaskGridView.ColumnCount = 16;

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
        }

        movementBitmaskGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        movementBitmaskGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;


        movementCostGridView.SuspendLayout();

        movementCostGridView.AutoSize = true;
        movementCostGridView.Dock = DockStyle.Fill;
        movementCostGridView.ScrollBars = ScrollBars.Both;

        movementCostGridView.ColumnCount = 16;

        movementCostGridView.Rows.Clear();

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
        }

        movementCostGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        movementCostGridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        movementBitmaskGridView.ResumeLayout();
        movementCostGridView.ResumeLayout();
    }

    private void RefreshMovementGridViewer() {
        if (ApiContainer == null) {
            return;
        }

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
            }
            else {
                movementBitmaskGridView.Rows[row].Cells[col].Style.BackColor = Color.FromKnownColor(KnownColor.White);
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

    private void RefreshFormControls() {
        foreach (var (value, label) in rows) {
            label.Text = value.Description + ": " + RamValueToInt(value);
        }

        RefreshMovementGridViewer();
    }

    public void UpdateValues(ToolFormUpdateType type) => RefreshFormControls();
    public void Restart() => RefreshFormControls();
    public bool AskSaveChanges() => true;
}