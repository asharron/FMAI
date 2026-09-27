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
        new("Loyd", "Body Health",      0x00D580L, 2),
        new("Loyd", "Body Max Health",  0x00D582L, 2),
        new("Loyd", "Left Arm Health",     0x00D586L, 2),
        new("Loyd", "Left Arm Max Health", 0x00D588L, 2),
        new("Loyd", "Right Arm Health",     0x00D58CL, 2),
        new("Loyd", "Right Arm Max Health", 0x00D58EL, 2),
        new("Loyd", "Leg Health",     0x00D592L, 2),
        new("Loyd", "Leg Max Health", 0x00D594L, 2),
        new("Loyd", "Max Move",      0x00D56EL, 2),
        new("Loyd", "Fight Stat Exp",      0x00D55DL, 1),
        new("Loyd", "Fight Stat Exp Multiplier",      0x00D55EL, 1),
        new("Loyd", "Short Stat Exp",      0x00D55FL, 1),
        new("Loyd", "Short Stat Exp Multiplier",      0x00D560L, 1),
        new("Loyd", "Long Stat Exp",      0x00D561L, 1),
        new("Loyd", "Long Stat Exp Multiplier",      0x00D562L, 1),
        new("Loyd", "Agility Stat Exp",      0x00D563L, 1),
        new("Loyd", "Agility Stat Exp Multiplier",      0x00D564L, 1),
        new("Loyd", "Skill #1", 0x00D565L, 1),
        new("Loyd", "Skill #2", 0x00D566L, 1),
        new("Loyd", "Skill #3", 0x00D567L, 1),
        new("Loyd", "Skill #4", 0x00D568L, 1),
        new("Loyd", "Skill #5", 0x00D569L, 1),
        
       
        // This may not be big endian and instead int + multiplier
        new("Sakata", "Body Health", 0x00D60A, 2, isBigEndian:true),
        new("Sakata", "Body Max Health", 0x00D60C, 2, isBigEndian:true),
        new("Sakata", "Left Arm Health", 0x00D610, 2, isBigEndian:true),
        new("Sakata", "Left Arm Max Health", 0x00D612, 2, isBigEndian:true),
        new("Sakata", "Right Arm Health", 0x00D616, 2, isBigEndian:true),
        new("Sakata", "Right Arm Max Health", 0x00D618, 2, isBigEndian:true),
        new("Sakata", "Leg Health", 0x00D61C, 2, isBigEndian:true),
        new("Sakata", "Leg Max Health", 0x00D61E, 2, isBigEndian:true),
        new("Sakata", "Max Move", 0x00D5F8, 2, isBigEndian:true),
        new("Sakata", "Fight Stat Exp", 0x00D5E8, 2),
        new("Sakata", "Short Stat Exp", 0x00D5EA, 2),
        new("Sakata", "Long Stat Exp", 0x00D5EC, 2),
        new("Sakata", "Agility Stat Exp", 0x00D5EE, 2),
        // Need to correct this since the exp is broken up by int + multiplier
        new("Sakata", "Skill #1", 0x00D5F0, 1),
        new("Sakata", "Skill #2", 0x00D5F1, 1),
        new("Sakata", "Skill #3", 0x00D5F2, 1),
        new("Sakata", "Skill #4", 0x00D5F3, 1),
        new("Sakata", "Skill #5", 0x00D5F4, 1),
    ];

    private readonly List<(RamValue Value, Label Label)> rows =
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

        foreach (var group in rows.GroupBy(r => r.Value.Character)) {
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
                inner.Controls.Add(row.Label);
            }

            box.Controls.Add(inner);
            root.Controls.Add(box);
        }

        Controls.Add(root);

        ResumeLayout(performLayout: false);
        PerformLayout();

        Load += (_, _) => IsLoaded = true;
        Activated += (_, _) => IsActive = true;
        Deactivate += (_, _) => IsActive = false;
        FormClosed += (_, _) => IsLoaded = false;

        Shown += (_, _) => { ApiContainer?.SaveState.LoadSlot(1); };
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