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

    [RequiredApi] 
    public ApiContainer? ApiContainer { get; set; }
    
    [RequiredApi] 
    public IEmulationApi? EmulationApi { get; set; }

    private readonly Label jevLabel = new Label{AutoSize = true};

    private readonly Jev jev = new Jev();


    private readonly List<(Common.RamValue RamValue, Label Label)> rows =
        Common.TrackedValues.Select(v => (v, new Label { AutoSize = true })).ToList();

    public FmaiForm() {
        ClientSize = new Size(480, 320);
        SuspendLayout();

        var root = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10),
            AutoScroll = true,
        };
        
        CreateFormControls(root);
        Controls.Add(root);

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
        jev.MakeJevRequest().ContinueWith(task => {
            jevLabel.Text = task.Result;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        
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
                    AutoSize=true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                };

                var incrementButton = new Button { Text = "+", AutoSize=true };
                var decrementButton = new Button{Text="-", AutoSize=true};

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
    }

    public void UpdateValues(ToolFormUpdateType type) => RefreshFormControls();
    public void Restart() => RefreshFormControls();
    public bool AskSaveChanges() => true;
}