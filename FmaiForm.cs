using System;
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

    [RequiredApi] public ApiContainer? ApiContainer { get; set; }

    [RequiredApi] public IEmulationApi? EmulationApi { get; set; }

    private record RamValue(long Address, int Length, String Description);

    private static readonly RamValue LoydBodyHealth = new RamValue(0x00D580L, 2, "Loyd Body Health");
    private static readonly RamValue LoydBodyMaxHealth = new RamValue(0x00D582L, 2, "Loyd Body Max Health");
    private static readonly RamValue LoydLeftArmHealth = new RamValue(0x00D586L, 2, "Loyd Left Arm health");
    private static readonly RamValue LoydLeftArmMaxHealth = new RamValue(0x00D588L, 2, "Loyd Left Arm Max Health");
    private static readonly RamValue LoydRightArmHealth = new RamValue(0x00D58CL, 2, "Loyd Right Arm Health");
    private static readonly RamValue LoydRightArmMaxHealth = new RamValue(0x00D58EL, 2, "Loyd Right Arm Max Health");
    private static readonly RamValue LoydLegHealth = new RamValue(0x00D592L, 2, "Loyd Leg Health");
    private static readonly RamValue LoydLegMaxHealth = new RamValue(0x00D594L, 2, "Loyd Leg Max Health");
    private static readonly RamValue LoydMaxMove = new RamValue(0x00D56EL, 2, "Loyd Max Move");

    private record RamLabel(RamValue RamValue, Label Label);

    private readonly RamLabel loydBodyHealthLabel = new RamLabel(LoydBodyHealth, new Label{AutoSize = true});
    private readonly RamLabel loydBodyMaxHealthLabel = new RamLabel(LoydBodyMaxHealth,new Label{AutoSize = true});
    private readonly RamLabel loydLeftArmHealthLabel = new RamLabel(LoydLeftArmHealth, new Label{AutoSize = true});
    private readonly RamLabel loydLeftArmMaxHealthLabel = new RamLabel(LoydLeftArmMaxHealth, new Label{AutoSize = true});
    private readonly RamLabel loydRightArmHealthLabel = new RamLabel(LoydRightArmHealth, new Label{AutoSize = true});
    private readonly RamLabel loydRightArmMaxHealthLabel = new RamLabel(LoydRightArmMaxHealth, new Label{AutoSize = true});
    private readonly RamLabel loydLegHealthLabel = new RamLabel(LoydLegHealth, new Label{AutoSize = true});
    private readonly RamLabel loydLegMaxHealthLabel = new RamLabel(LoydLegMaxHealth, new Label{AutoSize = true});
    private readonly RamLabel loydMaxMoveLabel = new RamLabel(LoydMaxMove, new Label{AutoSize = true});

    private RamLabel[] loydLabels;

    public FmaiForm() {
        ClientSize = new Size(480, 320);
        SuspendLayout();

        loydLabels = [
            loydBodyHealthLabel,
            loydBodyMaxHealthLabel,
            loydLeftArmHealthLabel,
            loydLeftArmMaxHealthLabel,
            loydRightArmHealthLabel,
            loydRightArmMaxHealthLabel,
            loydLegHealthLabel,
            loydLegMaxHealthLabel,
            loydMaxMoveLabel
        ];

        var layout = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        foreach (var ramLabel in loydLabels) {
            layout.Controls.Add(ramLabel.Label);
        }

        Controls.Add(layout);

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
        return BitConverter.ToUInt16(byteArray, 0);
    }

    private void RefreshFormControls() {
        foreach (var ramLabel in loydLabels) {
            var text = RamValueToInt(ramLabel.RamValue);
            ramLabel.Label.Text = ramLabel.RamValue.Description + ": " + text;
        }
    }


    public void UpdateValues(ToolFormUpdateType type) {
        RefreshFormControls();
    }

    public void Restart() {
        RefreshFormControls();
    }

    public bool AskSaveChanges() {
        return true;
    }
}