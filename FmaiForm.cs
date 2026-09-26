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

    private readonly Label statusLabel;

    public FmaiForm() {
        ClientSize = new Size(480, 320);
        SuspendLayout();
        statusLabel = new Label { AutoSize = true, Text = "Hello World!" };
        Controls.Add(statusLabel);
        ResumeLayout(performLayout: false);
        PerformLayout();

        Load += (_, _) => IsLoaded = true;
        Activated += (_, _) => IsActive = true;
        Deactivate += (_, _) => IsActive = false;
        FormClosed += (_, _) => IsLoaded = false;
    }

    private void RefreshFormControls() {
        if (ApiContainer == null) {
            return;
        }

        var bytes = ApiContainer.Memory.ReadByteRange(0x00D580L, 2);
        var byteArray = bytes.ToArray();
        var health = BitConverter.ToUInt16(byteArray, 0);

        statusLabel.Text = "Current Health: " + health;
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