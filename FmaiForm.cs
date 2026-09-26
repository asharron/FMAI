namespace FMAI;

using BizHawk.Client.Common;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;

[ExternalTool("FMAI")]
public class FmaiForm : Form, IExternalToolForm {
    public bool IsActive { get; }
    public bool IsLoaded { get; }
    public bool ContainsFocus { get; }
    
    [RequiredApi]
    public IEmulationApi EmulationApi { get; set; }

    public FmaiForm() {
        ClientSize = new Size(480, 320);
        SuspendLayout();
        Controls.Add(new Label{AutoSize = true, Text = "Hello World!"});
        ResumeLayout(performLayout:false);
        PerformLayout();
    }
    
    public void UpdateValues(ToolFormUpdateType type) {
        Debug.WriteLine("Update Form Values called");
    }

    public void Restart() {
        Debug.WriteLine("Restart Plugin called");
    }

    public bool AskSaveChanges() {
        Debug.WriteLine("Ask Save Changes called");
        return true;
    }
}