namespace AnthropicMinimal
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            txtApiKey = new TextBox();
            txtInput = new TextBox();
            txtOutput = new RichTextBox();
            btnSend = new Button();
            btnStream = new Button();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            SuspendLayout();
            // 
            // txtApiKey
            // 
            txtApiKey.Location = new Point(12, 27);
            txtApiKey.Name = "txtApiKey";
            txtApiKey.PasswordChar = '*';
            txtApiKey.PlaceholderText = "Enter your Anthropic API key";
            txtApiKey.Size = new Size(776, 23);
            txtApiKey.TabIndex = 0;
            // 
            // txtInput
            // 
            txtInput.Location = new Point(12, 85);
            txtInput.Multiline = true;
            txtInput.Name = "txtInput";
            txtInput.PlaceholderText = "Enter your message here";
            txtInput.Size = new Size(776, 80);
            txtInput.TabIndex = 1;
            txtInput.Text = "Hi.";
            // 
            // txtOutput
            // 
            txtOutput.Location = new Point(12, 216);
            txtOutput.Name = "txtOutput";
            txtOutput.ReadOnly = true;
            txtOutput.Size = new Size(776, 280);
            txtOutput.TabIndex = 2;
            txtOutput.Text = "";
            // 
            // btnSend
            // 
            btnSend.Location = new Point(12, 171);
            btnSend.Name = "btnSend";
            btnSend.Size = new Size(150, 30);
            btnSend.TabIndex = 3;
            btnSend.Text = "Send (No Stream)";
            btnSend.UseVisualStyleBackColor = true;
            btnSend.Click += btnSend_Click;
            // 
            // btnStream
            // 
            btnStream.Location = new Point(168, 171);
            btnStream.Name = "btnStream";
            btnStream.Size = new Size(150, 30);
            btnStream.TabIndex = 4;
            btnStream.Text = "Send (Streaming)";
            btnStream.UseVisualStyleBackColor = true;
            btnStream.Click += btnStream_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(47, 15);
            label1.TabIndex = 5;
            label1.Text = "API Key";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(12, 67);
            label2.Name = "label2";
            label2.Size = new Size(35, 15);
            label2.TabIndex = 6;
            label2.Text = "Input";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(12, 198);
            label3.Name = "label3";
            label3.Size = new Size(45, 15);
            label3.TabIndex = 7;
            label3.Text = "Output";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 508);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(btnStream);
            Controls.Add(btnSend);
            Controls.Add(txtOutput);
            Controls.Add(txtInput);
            Controls.Add(txtApiKey);
            Name = "Form1";
            Text = "Anthropic API Client Demo";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox txtApiKey;
        private TextBox txtInput;
        private RichTextBox txtOutput;
        private Button btnSend;
        private Button btnStream;
        private Label label1;
        private Label label2;
        private Label label3;
    }
}
