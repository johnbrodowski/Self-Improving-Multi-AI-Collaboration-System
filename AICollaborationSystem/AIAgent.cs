using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnthropicApp.AICollaborationSystem
{
    /// <summary>
    /// Represents an AI agent that processes requests asynchronously through Anthropic API
    /// and reports status through events.
    /// </summary>
    public class AIAgent : IDisposable
    {
        #region Private Fields

        private StringBuilder _responseBuilder = new StringBuilder();
        private readonly AnthropicClient _anthropicClient;
        private List<Message> _messageHistory;
        private string _agentPrompt;
        private string _currentRequestData;
        private bool _disposed = false;

        #endregion Private Fields

        #region Properties

        /// <summary>
        /// Gets the name of the agent.
        /// </summary>
        public string Name { get; }

        #endregion Properties

        #region Events

        /// <summary>
        /// Occurs when an error happens during processing.
        /// </summary>
        public event EventHandler<ErrorEventArgs> Error;

        /// <summary>
        /// Occurs when a request has completed processing.
        /// </summary>
        public event EventHandler<CompletedAgentEventArgs> Completed;

        /// <summary>
        /// Occurs when the status of a request changes.
        /// </summary>
        public event EventHandler<StatusEventArgs> Status;

        /// <summary>
        /// Occurs when a new request is being processed.
        /// </summary>
        public event EventHandler<RequestEventArgs> RequestEvent;

        /// <summary>
        /// Occurs when a response is generated.
        /// </summary>
        public event EventHandler<ResponseEventArgs> Response;

        #endregion Events

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="AIAgent"/> class.
        /// </summary>
        /// <param name="name">The name of the agent.</param>
        /// <param name="prompt">The specialized prompt for this agent.</param>
        /// <param name="anthropicApiKey">The API key for Anthropic.</param>
        public AIAgent(string name, string prompt, string anthropicApiKey)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _agentPrompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

            // Initialize message history
            _messageHistory = new List<Message>();

            // Initialize Anthropic client
            _anthropicClient = new AnthropicClient(anthropicApiKey);

            // Subscribe to streaming events
            _anthropicClient.MessageStop += OnMessageStop;
            _anthropicClient.Error += OnAnthropicError;
        }

        #endregion Constructor

        #region Public Methods

        /// <summary>
        /// Processes a request asynchronously and generates a response.
        /// </summary>
        /// <param name="requestData">The request data to process.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <param name="useStreaming">Whether to use streaming mode (default: true).</param>
        public async Task RequestAsync(string requestData, CancellationToken cancellationToken, bool useStreaming = true)
        {
            ThrowIfDisposed();
            _responseBuilder.Clear();

            try
            {
                // Store the current request
                _currentRequestData = requestData;

                // Raise request event
                OnRequest(new RequestEventArgs(requestData));

                // Update status
                OnStatus(new StatusEventArgs("Starting", 0));

                await ProcessWithAnthropic(requestData, useStreaming, cancellationToken);

                // Completed event will be raised in OnMessageStop event handler
            }
            catch (OperationCanceledException)
            {
                OnStatus(new StatusEventArgs("Cancelled", 0));
                OnCompleted(new CompletedAgentEventArgs(false));
            }
            catch (Exception ex)
            {
                OnError(new ErrorEventArgs(ex));
                OnCompleted(new CompletedAgentEventArgs(false));
            }
        }

        #endregion Public Methods

        #region Private Methods - Core Processing

        private async Task ProcessWithAnthropic(string requestData, bool useStreaming, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(requestData))
            {
                OnError(new ErrorEventArgs(new ArgumentNullException(nameof(requestData), "Request data cannot be null or empty")));
                OnResponse(new ResponseEventArgs(_currentRequestData, "Error: Request data cannot be null or empty"));
                return;
            }

            try
            {
                OnStatus(new StatusEventArgs("Processing with Anthropic", 25));

                // Add user message to history
                _messageHistory.Add(Message.CreateUserMessage(requestData));

                // Create request
                var request = new MessageRequest
                {
                    Model = Models.Claude45Haiku,
                    MaxTokens = 8000,
                    Temperature = 0.4,
                    System = _agentPrompt,
                    Messages = new List<Message>(_messageHistory)
                };

                if (useStreaming)
                {
                    // Subscribe to streaming text
                    _anthropicClient.StreamingTextReceived += OnStreamingTextReceived;

                    try
                    {
                        await _anthropicClient.SendMessageStreamAsync(request);
                    }
                    finally
                    {
                        _anthropicClient.StreamingTextReceived -= OnStreamingTextReceived;
                    }
                }
                else
                {
                    // Non-streaming mode
                    var response = await _anthropicClient.SendMessageAsync(request);
                    if (response != null)
                    {
                        string responseText = response.GetText();
                        _responseBuilder.Append(responseText);

                        // Add to message history
                        _messageHistory.Add(Message.CreateAssistantMessage(responseText));

                        // Raise response event
                        OnResponse(new ResponseEventArgs(_currentRequestData, responseText));
                        OnCompleted(new CompletedAgentEventArgs(true));
                    }
                }

                OnStatus(new StatusEventArgs("Processing complete", 100));
            }
            catch (Exception ex)
            {
                OnError(new ErrorEventArgs(ex));
                throw;
            }
        }

        private void OnStreamingTextReceived(object? sender, string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _responseBuilder.Append(text);
            }
        }

        private void OnMessageStop(object? sender, MessageStopEvent e)
        {
            // Get the complete response
            string completeResponse = _responseBuilder.ToString();

            if (!string.IsNullOrEmpty(_currentRequestData) && !string.IsNullOrEmpty(completeResponse))
            {
                try
                {
                    // Add to message history
                    _messageHistory.Add(Message.CreateAssistantMessage(completeResponse));

                    // Raise response event
                    OnResponse(new ResponseEventArgs(_currentRequestData, completeResponse));

                    // Raise completed event
                    OnCompleted(new CompletedAgentEventArgs(true));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error raising response event: {ex.Message}");
                    OnError(new ErrorEventArgs(ex));
                }
            }
        }

        private void OnAnthropicError(object? sender, ErrorEvent e)
        {
            var exception = new Exception($"Anthropic API Error: {e.Error.Type} - {e.Error.Message}");
            OnError(new ErrorEventArgs(exception));
            OnCompleted(new CompletedAgentEventArgs(false));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AIAgent));
            }
        }

        #endregion Private Methods - Core Processing

        #region Event Handlers

        protected virtual void OnError(ErrorEventArgs args) => Error?.Invoke(this, args);
        protected virtual void OnCompleted(CompletedAgentEventArgs args) => Completed?.Invoke(this, args);
        protected virtual void OnStatus(StatusEventArgs args) => Status?.Invoke(this, args);
        protected virtual void OnRequest(RequestEventArgs args) => RequestEvent?.Invoke(this, args);
        protected virtual void OnResponse(ResponseEventArgs args) => Response?.Invoke(this, args);

        #endregion Event Handlers

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from events
                    if (_anthropicClient != null)
                    {
                        _anthropicClient.MessageStop -= OnMessageStop;
                        _anthropicClient.Error -= OnAnthropicError;
                        _anthropicClient.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        #endregion IDisposable
    }

    #region Event Arguments

    /// <summary>
    /// Provides data for the <see cref="AIAgent.Error"/> event.
    /// </summary>
    public class ErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the exception that occurred.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorEventArgs"/> class.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        public ErrorEventArgs(Exception exception) => Exception = exception;
    }

    /// <summary>
    /// Provides data for the <see cref="AIAgent.Completed"/> event.
    /// </summary>
    public class CompletedAgentEventArgs : EventArgs
    {
        /// <summary>
        /// Gets a value indicating whether the operation completed successfully.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompletedAgentEventArgs"/> class.
        /// </summary>
        /// <param name="success">A value indicating whether the operation completed successfully.</param>
        public CompletedAgentEventArgs(bool success) => Success = success;
    }

    /// <summary>
    /// Provides data for the <see cref="AIAgent.Status"/> event.
    /// </summary>
    public class StatusEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the status message.
        /// </summary>
        public string Status { get; }

        /// <summary>
        /// Gets the progress percentage (0-100).
        /// </summary>
        public int Progress { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StatusEventArgs"/> class.
        /// </summary>
        /// <param name="status">The status message.</param>
        /// <param name="progress">The progress percentage (0-100).</param>
        public StatusEventArgs(string status, int progress)
        {
            Status = status;
            Progress = progress;
        }
    }

    /// <summary>
    /// Provides data for the <see cref="AIAgent.RequestEvent"/> event.
    /// </summary>
    public class RequestEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the request data.
        /// </summary>
        public string RequestData { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RequestEventArgs"/> class.
        /// </summary>
        /// <param name="requestData">The request data.</param>
        public RequestEventArgs(string requestData) => RequestData = requestData;
    }

    /// <summary>
    /// Provides data for the <see cref="AIAgent.Response"/> event.
    /// </summary>
    public class ResponseEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the request data that triggered this response.
        /// </summary>
        public string RequestData { get; }

        /// <summary>
        /// Gets the response data.
        /// </summary>
        public string ResponseData { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResponseEventArgs"/> class.
        /// </summary>
        /// <param name="requestData">The request data that triggered this response.</param>
        /// <param name="responseData">The response data.</param>
        public ResponseEventArgs(string requestData, string responseData)
        {
            RequestData = requestData;
            ResponseData = responseData;
        }
    }

    #endregion Event Arguments
}
