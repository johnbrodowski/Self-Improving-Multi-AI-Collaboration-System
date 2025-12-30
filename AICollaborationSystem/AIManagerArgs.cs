using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnthropicApp.AICollaborationSystem
{


    // New event args class for when all agents have responded
    public class AllResponsesCompletedEventArgs : EventArgs
    {
        public string RequestData { get; }
        public AllResponsesCompletedEventArgs(string requestData) => RequestData = requestData;
    }

    //// Manager event args classes
    //public class AgentEventArgs : EventArgs
    //{
    //    public string AgentName { get; }
    //    public AgentEventArgs(string agentName) => AgentName = agentName;
    //}

    // Manager event args classes
    public class AgentEventArgs : EventArgs
    {
        public string AgentName { get; }
        public AgentEventArgs(string agentName) => AgentName = agentName;
    }

    public class AgentAddedEventArgs : EventArgs
    {
        public string AgentName { get; }
        public AgentAddedEventArgs(string agentName) => AgentName = agentName;
    }

    // Manager event args classes
    public class AgentRemovedEventArgs : EventArgs
    {
        public string AgentName { get; }
        public AgentRemovedEventArgs(string agentName) => AgentName = agentName;
    }

    public class AgentErrorEventArgs : AgentEventArgs
    {
        public Exception Exception { get; }
        public AgentErrorEventArgs(string agentName, Exception exception) : base(agentName) => Exception = exception;
    }

    public class AgentCompletedEventArgs : AgentEventArgs
    {
        public bool Success { get; }
        public AgentCompletedEventArgs(string agentName, bool success) : base(agentName) => Success = success;
    }

    public class AgentStatusEventArgs : AgentEventArgs
    {
        public string Status { get; }

        public AgentStatusEventArgs(string agentName, string status) : base(agentName)
        {
            Status = status;

        }
    }

    public class AgentRequestEventArgs : AgentEventArgs
    {
        public string RequestData { get; }
        public AgentRequestEventArgs(string agentName, string requestData) : base(agentName) => RequestData = requestData;
    }

    public class AgentResponseEventArgs : AgentEventArgs
    {
        public string RequestData { get; }
        public string ResponseData { get; }

        public AgentResponseEventArgs(string agentName, string requestData, string responseData)
            : base(agentName)
        {
            RequestData = requestData;
            ResponseData = responseData;
        }
    }


    /// <summary>
    /// Specifies how an agent should handle its context history for a given request.
    /// </summary>
    public enum HistoryMode
    {
        /// <summary>
        /// Agent uses its own persistent history and appends the new interaction.
        /// Can optionally load session history for additional context. (Default behavior)
        /// </summary>
        Conversational,
        CONVERSATIONAL = Conversational, // Alias for backward compatibility
        /// <summary>
        /// Agent temporarily ignores its own history and is provided with a snippet
        /// of the orchestrating session's history for a one-shot task. The interaction
        /// is NOT saved to its persistent history.
        /// </summary>
        SessionAware,
        SESSION_AWARE = SessionAware, // Alias for backward compatibility
        /// <summary>
        /// Agent is completely stateless for the task, receiving only its system prompt
        /// and the current directive. No history is used or saved.
        /// </summary>
        Stateless,
        STATELESS = Stateless // Alias for backward compatibility
    }


    /// <summary>
    /// Holds the parsed information from a Chief's activation directive.
    /// </summary>
    public class ActivationInfo
    {
        public string ModuleName { get; set; } = string.Empty;
        public string Focus { get; set; } = string.Empty;
        public HistoryMode HistoryMode { get; set; } = HistoryMode.Conversational; // Default to conversational
        public int SessionHistoryCount { get; set; } = 0; // Default to 0

        /// <summary>
        /// Execution phase for ordering parallel activations. Lower phases execute first.
        /// Agents in the same phase run in parallel. Default is 1 (first phase).
        /// </summary>
        public int ExecutionPhase { get; set; } = 1;

        /// <summary>
        /// Optional list of module names this activation depends on.
        /// The system will wait for these modules to complete before starting this one.
        /// </summary>
        public List<string> DependsOn { get; set; } = new List<string>();
    }

    /// <summary>
    /// Holds the parsed information from a Chief's [ACTIVATE_TEAM] directive.
    /// </summary>
    public class TeamActivationInfo
    {
        public string TeamName { get; set; } = string.Empty;
        public string TeamFocus { get; set; } = string.Empty;
        public HistoryMode HistoryMode { get; set; } = HistoryMode.Conversational; // Default
        public int SessionHistoryCount { get; set; } = 0; // Default
        //public string Focus { get; set; } = string.Empty;
    }



}