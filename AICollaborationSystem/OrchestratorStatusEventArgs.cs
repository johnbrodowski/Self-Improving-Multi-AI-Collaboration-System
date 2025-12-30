using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AnthropicApp
{


 
 

    public class OrchestratorStatusEventArgs : EventArgs
    {
        public string Message { get; }
        public WorkflowStepStatus Status { get; }
        public int CurrentStepId { get; }
        public DateTime Timestamp { get; }

        public OrchestratorStatusEventArgs(string message, WorkflowStepStatus status, int currentStepId)
        {
            Message = message;
            Status = status;
            CurrentStepId = currentStepId;
            Timestamp = DateTime.UtcNow;
        }
    }

    // ToolExecutionRequestEventArgs removed

    public class UserInputRequestEventArgs : EventArgs
    {
        public string Prompt { get; } // The question for the user
        public DateTime Timestamp { get; }

        public UserInputRequestEventArgs(string prompt)
        {
            Prompt = prompt;
            Timestamp = DateTime.UtcNow;
        }
    }

 


    public enum WorkflowStepStatus
    {
        Info,
        Started,
        InProgress,
        Waiting, // Waiting for external input (user)
        Paused, // General paused state (e.g., for user input)
        Completed,
        Warning,
        Error,
        Failed,
        Cancelled
    }
 
 
}
