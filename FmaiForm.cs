using System;
using System.Collections.Generic;
using System.Linq;
using BizHawk.Emulation.Common;
using BizHawk.Client.Common;
using System.Windows.Forms;
using System.Drawing;

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

    private record RamValue(string Character, string Stat, long Address, int Length, bool isBigEndian = false) {
        public string Description => $"{Character} {Stat}";
    }

    private static readonly RamValue[] TrackedValues = [
        
        
        // Loyd
        //////////////////////////////////////////
        // Health
        new("Loyd", "Body Health",      0x00D580L, 1),
        new("Loyd", "Body Health 256 Multiplier",      0x00D581L, 1),
        new("Loyd", "Body Max Health",  0x00D582L, 1),
        new("Loyd", "Body Max Health 256 Multiplier",  0x00D583L, 1),
        new("Loyd", "Left Arm Health",     0x00D586L, 1),
        new("Loyd", "Left Arm Health 256 Multiplier",     0x00D587L, 1),
        new("Loyd", "Left Arm Max Health", 0x00D588L, 1),
        new("Loyd", "Left Arm Max Health 256 Multiplier", 0x00D589L, 1),
        new("Loyd", "Right Arm Health",     0x00D58CL, 1),
        new("Loyd", "Right Arm Health 256 Multiplier",     0x00D58DL, 1),
        new("Loyd", "Right Arm Max Health", 0x00D58EL, 1),
        new("Loyd", "Right Arm Max Health 256 Multiplier", 0x00D58FL, 1),
        new("Loyd", "Leg Health",     0x00D592L, 1),
        new("Loyd", "Leg Health 256 Multiplier",     0x00D593L, 1),
        new("Loyd", "Leg Max Health", 0x00D594L, 1),
        new("Loyd", "Leg Max Health 256 Multiplier", 0x00D595L, 1),
        
        // Exp
        new("Loyd", "Fight Stat Exp",      0x00D55DL, 1),
        new("Loyd", "Fight Stat Exp 256 Multiplier",      0x00D55EL, 1),
        new("Loyd", "Short Stat Exp",      0x00D55FL, 1),
        new("Loyd", "Short Stat Exp 256 Multiplier",      0x00D560L, 1),
        new("Loyd", "Long Stat Exp",      0x00D561L, 1),
        new("Loyd", "Long Stat Exp 256 Multiplier",      0x00D562L, 1),
        new("Loyd", "Agility Stat Exp",      0x00D563L, 1),
        new("Loyd", "Agility Stat Exp 256 Multiplier",      0x00D564L, 1),
        new("Loyd", "Skill #1", 0x00D565L, 1),
        new("Loyd", "Skill #2", 0x00D566L, 1),
        new("Loyd", "Skill #3", 0x00D567L, 1),
        new("Loyd", "Skill #4", 0x00D568L, 1),
        new("Loyd", "Skill #5", 0x00D569L, 1),
        
        // Turn
        new("Loyd", "Has Turn Completed", 0x00D574L, 1),
        new("Loyd", "X Position?", 0x00D548L, 1),
        new("Loyd", "Y Position?", 0x00D54AL, 1),
        new("Loyd", "Max Move",      0x00D56EL, 1),
       
        // Sakata
        ///////////////////////////////////////////////
        // Health
        new("Sakata", "Body Health", 0x00D60B, 1),
        new("Sakata", "Body Health 256 Multiplier", 0x00D60C, 1),
        new("Sakata", "Body Max Health", 0x00D60D, 1),
        new("Sakata", "Body Max Health 256 Multiplier", 0x00D60E, 1),
        new("Sakata", "Left Arm Health", 0x00D611, 1),
        new("Sakata", "Left Arm Health 256 Multiplier", 0x00D612, 1),
        new("Sakata", "Left Arm Max Health", 0x00D613, 1),
        new("Sakata", "Left Arm Max Health 256 Multiplier", 0x00D614, 1),
        new("Sakata", "Right Arm Health", 0x00D617, 1),
        new("Sakata", "Right Arm Health 256 Multiplier", 0x00D618, 1),
        new("Sakata", "Right Arm Max Health", 0x00D619, 1),
        new("Sakata", "Right Arm Max Health 256 Multiplier", 0x00D61A, 1),
        new("Sakata", "Leg Health", 0x00D61D, 1),
        new("Sakata", "Leg Health 256 Multiplier", 0x00D61E, 1),
        new("Sakata", "Leg Max Health", 0x00D61F, 1),
        new("Sakata", "Leg Max Health 256 Multiplier", 0x00D620, 1),
        
        // Exp
        new("Sakata", "Fight Stat Exp", 0x00D5E8, 1),
        new("Sakata", "Fight Stat Exp 256 Multiplier", 0x00D5E9, 1),
        new("Sakata", "Short Stat Exp", 0x00D5EA, 1),
        new("Sakata", "Short Stat Exp 256 Multiplier", 0x00D5EB, 1),
        new("Sakata", "Long Stat Exp", 0x00D5EC, 1),
        new("Sakata", "Long Stat Exp 256 Multiplier", 0x00D5ED, 1),
        new("Sakata", "Agility Stat Exp", 0x00D5EE, 1),
        new("Sakata", "Agility Stat Exp 256 Multiplier", 0x00D5EF, 1),
        new("Sakata", "Skill #1", 0x00D5F0, 1),
        new("Sakata", "Skill #2", 0x00D5F1, 1),
        new("Sakata", "Skill #3", 0x00D5F2, 1),
        new("Sakata", "Skill #4", 0x00D5F3, 1),
        new("Sakata", "Skill #5", 0x00D5F4, 1),
        
        // Turn
        new("Sakata", "Max Move", 0x00D5F9, 1),
        new("Sakata", "X Position?", 0x00D5D3L, 1),
        new("Sakata", "Y Position?", 0x00D5D5L, 1),
        new("Sakata", "Has Turn Completed", 0x00D5FFL, 1),
    ];

    private readonly List<(RamValue RamValue, Label Label)> rows =
        TrackedValues.Select(v => (v, new Label { AutoSize = true })).ToList();

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

                var incrementButton = new Button { Text = "Increment", AutoSize=true };
                var decrementButton = new Button{Text="Decrement", AutoSize=true};

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

    private ushort RamValueToInt(RamValue ramValue) {
        if (ApiContainer == null) {
            return 0;
        }

        var bytes = ApiContainer.Memory.ReadByteRange(ramValue.Address, ramValue.Length);
        var byteArray = bytes.ToArray();

        if (ramValue.isBigEndian) {
            Array.Reverse(byteArray);
        }

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