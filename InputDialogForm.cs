using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace AnthropicApp
{
    public class InputDialogForm : Form
    {
        private TextBox txtInput;
        private Button btnOk;
        private Button btnCancel;
        private Label lblPrompt;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string UserInput { get; private set; }

        public InputDialogForm(string title, string prompt)
        {
            InitializeComponent();
            this.Text = title;
            lblPrompt.Text = prompt;
            lblPrompt.MaximumSize = new System.Drawing.Size(370, 0); // Allow label to wrap
            lblPrompt.AutoSize = true;

            // Recalculate layout after setting prompt text
            // Position textbox below the label with some padding
            int textBoxTop = lblPrompt.Bottom + 10;
            txtInput.Location = new Point(12, textBoxTop);
            txtInput.Height = 100; // Fixed height for input

            // Position buttons below textbox
            int buttonsTop = txtInput.Bottom + 10;
            btnOk.Location = new Point(this.ClientSize.Width - btnOk.Width - btnCancel.Width - 20, buttonsTop);
            btnCancel.Location = new Point(this.ClientSize.Width - btnCancel.Width - 12, buttonsTop);

            // Resize form to fit all controls
            this.ClientSize = new System.Drawing.Size(400, buttonsTop + btnOk.Height + 15);
        }

        private void InitializeComponent()
        {
            lblPrompt = new Label();
            txtInput = new TextBox();
            btnOk = new Button();
            btnCancel = new Button();
            SuspendLayout();
            // 
            // lblPrompt
            // 
            lblPrompt.Location = new Point(12, 9);
            lblPrompt.Name = "lblPrompt";
            lblPrompt.Size = new Size(376, 144);
            lblPrompt.TabIndex = 0;
            // 
            // txtInput
            // 
            txtInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtInput.Location = new Point(12, 156);
            txtInput.Multiline = true;
            txtInput.Name = "txtInput";
            txtInput.ScrollBars = ScrollBars.Vertical;
            txtInput.Size = new Size(376, 153);
            txtInput.TabIndex = 1;
            // 
            // btnOk
            // 
            btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnOk.Location = new Point(235, 315);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(75, 23);
            btnOk.TabIndex = 2;
            btnOk.Text = "OK";
            btnOk.Click += btnOk_Click;
            // 
            // btnCancel
            // 
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(316, 315);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(75, 23);
            btnCancel.TabIndex = 3;
            btnCancel.Text = "Cancel";
            // 
            // InputDialogForm
            // 
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            ClientSize = new Size(400, 350);
            Controls.Add(lblPrompt);
            Controls.Add(txtInput);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "InputDialogForm";
            StartPosition = FormStartPosition.CenterParent;
            Load += InputDialogForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            UserInput = txtInput.Text;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // Static method helper
        public static string ShowInputDialog(IWin32Window owner, string prompt, string title)
        {
            using (var dialog = new InputDialogForm(title, prompt))
            {
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                {
                    return dialog.UserInput;
                }
                return null; // Or string.Empty depending on how you want to handle cancel
            }
        }

        private void InputDialogForm_Load(object sender, EventArgs e)
        {

        }
    }
}
