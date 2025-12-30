namespace AnthropicMinimal
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        
        private void Form1_Load(object sender, EventArgs e)
        {
            var api_key = "";
            txtApiKey.Text = api_key;
        }

        private async void btnSend_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtApiKey.Text))
            {
                MessageBox.Show("Please enter your API key.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtInput.Text))
            {
                MessageBox.Show("Please enter a message.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnSend.Enabled = false;
            btnStream.Enabled = false;
            txtOutput.Clear();
            txtOutput.Text = "Sending request...\n\n";

            try
            {
                using var client = new AnthropicClient(txtApiKey.Text);

                var request = new MessageRequest
                {
                    Model = Models.Claude45Haiku,
                    MaxTokens = 1024,
                    Messages = new List<Message>
                    {
                        Message.CreateUserMessage(txtInput.Text)
                    }
                };

                var response = await client.SendMessageAsync(request);

                if (response != null)
                {
                    txtOutput.Text = $"Response (ID: {response.Id}):\n\n{response.GetText()}\n\n";
                    txtOutput.AppendText($"Tokens - Input: {response.Usage.InputTokens}, Output: {response.Usage.OutputTokens}");
                }
            }
            catch (Exception ex)
            {
                txtOutput.Text = $"Error: {ex.Message}";
            }
            finally
            {
                btnSend.Enabled = true;
                btnStream.Enabled = true;
            }
        }

        private async void btnStream_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtApiKey.Text))
            {
                MessageBox.Show("Please enter your API key.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtInput.Text))
            {
                MessageBox.Show("Please enter a message.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnSend.Enabled = false;
            btnStream.Enabled = false;
            txtOutput.Clear();
            txtOutput.Text = "Streaming response:\n\n";

            try
            {
                using var client = new AnthropicClient(txtApiKey.Text);

                // Subscribe to streaming events
                client.MessageStart += (s, evt) =>
                {
                    this.Invoke(() =>
                    {
                        txtOutput.AppendText($"[Message Started - ID: {evt.Message.Id}]\n\n");
                    });
                };

                client.StreamingTextReceived += (s, text) =>
                {
                    this.Invoke(() =>
                    {
                        txtOutput.AppendText(text);
                    });
                };

                client.MessageDelta += (s, evt) =>
                {
                    this.Invoke(() =>
                    {
                        if (!string.IsNullOrEmpty(evt.Delta.StopReason))
                        {
                            txtOutput.AppendText($"\n\n[Stop Reason: {evt.Delta.StopReason}]");
                        }
                        txtOutput.AppendText($"\n[Output Tokens: {evt.Usage.OutputTokens}]");
                    });
                };

                client.MessageStop += (s, evt) =>
                {
                    this.Invoke(() =>
                    {
                        txtOutput.AppendText("\n\n[Message Complete]");
                    });
                };

                client.Error += (s, evt) =>
                {
                    this.Invoke(() =>
                    {
                        txtOutput.AppendText($"\n\nError: {evt.Error.Message}");
                    });
                };

                var request = new MessageRequest
                {
                    Model = Models.Claude45Haiku,
                    MaxTokens = 1024,
                    Messages = new List<Message>
                    {
                        Message.CreateUserMessage(txtInput.Text)
                    }
                };

                await client.SendMessageStreamAsync(request);
            }
            catch (Exception ex)
            {
                txtOutput.AppendText($"\n\nError: {ex.Message}");
            }
            finally
            {
                btnSend.Enabled = true;
                btnStream.Enabled = true;
            }
        }

    }
}
