using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AnthropicApp.AICollaborationSystem
{




    /// <summary>
    /// Manages multiple AI agents in a thread-safe manner for Windows Forms applications.
    /// </summary>
    public class AIManager : IDisposable
    {
        // Thread-safe dictionary for storing AI agents with their cancellation tokens
        private readonly ConcurrentDictionary<string, (AIAgent Agent, CancellationTokenSource CancellationSource)> _agents =
            new ConcurrentDictionary<string, (AIAgent, CancellationTokenSource)>();

        private bool _disposed = false;

        // Events that the Forms application can subscribe to
        public event EventHandler<AgentAddedEventArgs> AgentAdded;

        public event EventHandler<AgentRemovedEventArgs> AgentRemoved;

        public event EventHandler<AgentErrorEventArgs> AgentError;
        public event EventHandler<AgentCompletedEventArgs> AgentCompleted;
        public event EventHandler<AgentStatusEventArgs> AgentStatus;
        public event EventHandler<AgentRequestEventArgs> AgentRequest;
        public event EventHandler<AgentResponseEventArgs> AgentResponse;

       public const string ANTHROPIC_APIKEY = ""; // TODO: Move to configuration



        // Add the response collection
        private readonly ResponseCollection _responseCollection = new ResponseCollection();

        // Public property to access the response collection
        public ResponseCollection Responses => _responseCollection;

        // Add a new event for when all agents have responded
        public event EventHandler<AllResponsesCompletedEventArgs> AllResponsesCompleted;

        // Tracking for pending responses
        private readonly ConcurrentDictionary<string, HashSet<string>> _pendingResponses =
            new ConcurrentDictionary<string, HashSet<string>>();


        private AgentDatabase _agentDb;

        public AIManager()
        {
            _agentDb = new AgentDatabase("cognitive_agents.db");
           // _agentDb = new AgentDatabase("agent_database.db");
            _agentDb.Initialize();
            // Initialize the AI manager
            Debug.WriteLine("AIManager initialized");
        }


        /// <summary>
        /// Creates a new AI agent with the specified name.
        /// </summary>
        public AIAgent CreateAgent(string name, string prompt)
        {
            ThrowIfDisposed();

            // Create agent and cancellation token source
            var agent = new AIAgent(name, prompt, ANTHROPIC_APIKEY);
            var cts = new CancellationTokenSource();

            // Add to dictionary
            if (!_agents.TryAdd(name, (agent, cts)))
            {
                cts.Dispose();
                throw new InvalidOperationException($"Agent with name '{name}' already exists.");
            }

            Debug.WriteLine($"[AIManager.CreateAgent] Added '{name}'. Current count: {_agents.Count}");


            // Setup event handlers
            SetupEventHandlers(agent);

            // Raise AgentAdded event
            OnAgentAdded(new AgentAddedEventArgs(name));

           // Debug.WriteLine($"Agent '{name}' created with specialized prompt");

            return agent;
        }



 

         
        /// <summary>
        /// Gets an existing AI agent by name.
        /// </summary>
        public AIAgent? GetAgent(string name)
        {
            ThrowIfDisposed();
            return _agents.TryGetValue(name, out var agentData) ? agentData.Agent : null;
        }

        /// <summary>
        /// Checks if an agent exists.
        /// </summary>
        public bool AgentExists(string name)
        {
            return _agents.ContainsKey(name);
        }

        /// <summary>
        /// Gets all AI agent names.
        /// </summary>
        public ICollection<string> GetAgentNames()
        {
            ThrowIfDisposed();
            return _agents.Keys;
        }

        /// <summary>
        /// Removes an AI agent by name.
        /// </summary>
        public bool RemoveAgent(string name)
        {
            ThrowIfDisposed();

            if (_agents.TryRemove(name, out var agentData))
            {
                // Cancel any ongoing operations
                agentData.CancellationSource.Cancel();
                agentData.CancellationSource.Dispose();

                // Raise AgentRemoved event
                OnAgentRemoved(new AgentRemovedEventArgs(name));

               Debug.WriteLine($"Agent '{name}' removed");

                return true;
            }

            return false;
        }
        /// <summary>
        /// Sends a request to a specific AI agent.
        /// </summary>
        public async Task RequestAsync(string agentName, string requestData)
        {

             

            ThrowIfDisposed();

            if (_agents.TryGetValue(agentName, out var agentData))
            {
                // Track this pending response
                _pendingResponses.AddOrUpdate(
                    requestData,
                    _ => new HashSet<string> { agentName },
                    (_, set) =>
                    {
                        lock (set)
                        {
                            set.Add(agentName);
                            return set;
                        }
                    }
                );

                // Cancel any previous operation
                agentData.CancellationSource.Cancel();
                agentData.CancellationSource.Dispose();

                // Create a new cancellation token source
                var cts = new CancellationTokenSource();

                // Update the cancellation token source
                _agents[agentName] = (agentData.Agent, cts);

                // Send the request (streaming enabled by default)
                await agentData.Agent.RequestAsync(requestData, cts.Token, true);
            }
            else
            {
                throw new KeyNotFoundException($"Agent with name '{agentName}' not found.");
            }
        }

        /// <summary>
        /// Sends a request to all AI agents.
        /// </summary>
        public async Task RequestAllAsync(string requestData)
        {
            Debug.WriteLine("Requesting all agents...");

            ThrowIfDisposed();

            var tasks = new List<Task>();


            Debug.WriteLine($"[AIManager.RequestAllAsync] Attempting request. Current agent count: {_agents.Count}");



            var agentNames = _agents.Keys.ToList();

            // Track all agents that we're sending requests to
            _pendingResponses[requestData] = new HashSet<string>(agentNames);

            foreach (var agentName in agentNames)
            {
                tasks.Add(RequestAsync(agentName, requestData));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Cancels all ongoing requests.
        /// </summary>
        public void CancelAllRequests()
        {
            ThrowIfDisposed();

            foreach (var agentData in _agents.Values)
            {
                agentData.CancellationSource.Cancel();
            }

           Debug.WriteLine("All requests cancelled");
        }

        /// <summary>
        /// Sets up event handlers for an AI agent.
        /// </summary>


 
        private void SetupEventHandlers(AIAgent agent)
        {
            agent.Error += (sender, args) =>
            {
             //   Debug.WriteLine($"[{agent.Name}] Error: {args.Exception.MessageAnthropic}");
                OnAgentError(new AgentErrorEventArgs(agent.Name, args.Exception));
            };

            agent.Completed += (sender, args) =>
            {
              //  Debug.WriteLine($"[{agent.Name}] Completed: {(args.Success ? "Success" : "Failed")}");
                OnAgentCompleted(new AgentCompletedEventArgs(agent.Name, args.Success));
            };

            agent.Status += (sender, args) =>
            {
              //  Debug.WriteLine($"[{agent.Name}] Status: {args.Status} ({args.Progress}%)");
                OnAgentStatus(new AgentStatusEventArgs(agent.Name, args.Status));
            };

            agent.RequestEvent += (sender, args) =>
            {
            //    Debug.WriteLine($"[{agent.Name}] Request: {args.RequestData}");
                OnAgentRequest(new AgentRequestEventArgs(agent.Name, args.RequestData));
            };

            agent.Response += (sender, args) =>
            {
               // Debug.WriteLine($"[{agent.Name}] Response for request '{args.RequestData}': {args.ResponseData}");

 
                // Add the response to our collection
                _responseCollection.AddResponse(args.RequestData, agent.Name, args.ResponseData);

                // Check if all agents have responded to this request
                if (_pendingResponses.TryGetValue(args.RequestData, out var pendingSet))
                {
                    lock (pendingSet)
                    {
                        pendingSet.Remove(agent.Name);

                        // If all agents have responded, raise the AllResponsesCompleted event
                        if (pendingSet.Count == 0)
                        {
                            _pendingResponses.TryRemove(args.RequestData, out _);
                            OnAllResponsesCompleted(new AllResponsesCompletedEventArgs(args.RequestData));
                        }
                    }
                }

                // Notify subscribers of this response
                OnAgentResponse(new AgentResponseEventArgs(agent.Name, args.RequestData, args.ResponseData));
            };
        }




        // Helper method to make sure agent exists in database
        private async Task<int> EnsureAgentExistsAsync(string agentName)
        {
            // Simple implementation - expand as needed
            var agents = await _agentDb.GetAllAgentsAsync();
            var existingAgent = agents.FirstOrDefault(a => a.Name == agentName);

            if (existingAgent != null)
                return existingAgent.AgentId;

            // Create new agent if not found
            string purpose = "Cognitive agent"; // You'll need better descriptions
            string prompt = "Default prompt"; // You'll need to get the actual prompt

            return await _agentDb.AddAgentAsync(agentName, purpose, prompt);
        }






        public async Task<AgentResponseEventArgs?> RequestAsyncAndWait(string agentName, string requestData, TimeSpan timeout)
        {
            if (!_agents.TryGetValue(agentName, out var agentData))
            {
                throw new KeyNotFoundException($"Agent with name '{agentName}' not found.");
            }

            var tcs = new TaskCompletionSource<AgentResponseEventArgs?>();
            string invocationId = $"wait_{agentName}_{Guid.NewGuid()}"; // Unique ID

            EventHandler<AgentResponseEventArgs>? handler = null;
            handler = (sender, args) =>
            {
                // Basic check - assumes next response from this agent is the one we want
                if (args.AgentName.Equals(agentName, StringComparison.OrdinalIgnoreCase) && !tcs.Task.IsCompleted)
                {
                    Debug.WriteLine($"[RequestAsyncAndWait] Response received for {agentName} ({invocationId})");
                    if (handler != null) this.AgentResponse -= handler; // Unsubscribe
                    tcs.TrySetResult(args);
                }
            };

            this.AgentResponse += handler;

            try
            {
                // Send the request fire-and-forget style
                // The actual response is captured by the event handler
                _ = agentData.Agent.RequestAsync(requestData, CancellationToken.None, true); // Use CancellationToken.None if timeout is handled by Task.Delay

                Debug.WriteLine($"[RequestAsyncAndWait] Request sent to {agentName} ({invocationId}). Waiting...");

                // Wait for the response or timeout
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));

                if (completedTask == tcs.Task)
                {
                    return await tcs.Task; // Return the received args
                }
                else
                {
                    Debug.WriteLine($"[RequestAsyncAndWait] Timeout waiting for {agentName} ({invocationId}).");
                    tcs.TrySetCanceled(); // Cancel the TCS
                    return null; // Indicate timeout
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RequestAsyncAndWait] Error during request/wait for {agentName}: {ex.Message}");
                tcs.TrySetException(ex);
                return null; // Indicate error
            }
            finally
            {
                if (handler != null) this.AgentResponse -= handler; // Ensure unsubscribe
            }
        }






        // Event invokers
        protected virtual void OnAgentAdded(AgentAddedEventArgs args) => AgentAdded?.Invoke(this, args);
        protected virtual void OnAgentRemoved(AgentRemovedEventArgs args) => AgentRemoved?.Invoke(this, args);
        protected virtual void OnAgentError(AgentErrorEventArgs args) => AgentError?.Invoke(this, args);
        protected virtual void OnAgentCompleted(AgentCompletedEventArgs args) => AgentCompleted?.Invoke(this, args);
        protected virtual void OnAgentStatus(AgentStatusEventArgs args) => AgentStatus?.Invoke(this, args);
        protected virtual void OnAgentRequest(AgentRequestEventArgs args) => AgentRequest?.Invoke(this, args);
        protected virtual void OnAgentResponse(AgentResponseEventArgs args) => AgentResponse?.Invoke(this, args);

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AIManager));
            }
        }

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
                    foreach (var agentData in _agents.Values)
                    {
                        agentData.CancellationSource.Cancel();
                        agentData.CancellationSource.Dispose();
                    }

                    _agents.Clear();

                    Debug.WriteLine("AIManager disposed");
                }

                _disposed = true;
            }
        }
 


        protected virtual void OnAllResponsesCompleted(AllResponsesCompletedEventArgs args) =>
        AllResponsesCompleted?.Invoke(this, args);
   }
}
