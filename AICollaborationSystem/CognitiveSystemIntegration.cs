using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;


namespace AnthropicApp.AICollaborationSystem
{
    /// <summary>
    /// Handles the integration between the AgentDatabase and AIManager components,
    /// providing a bridge to create runtime agents from database records.
    /// </summary>
    public class CognitiveSystemIntegration
    {
        protected readonly AIManager _aiManager;
        private AgentDatabase _agentDb;
        private AgentPromptImporter _promptImporter;
        private PerformanceDatabase _performanceDb;

        // Track request start times for accurate processing time calculation
        private readonly ConcurrentDictionary<string, DateTime> _requestStartTimes =
            new ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// Initializes a new instance of the CognitiveSystemIntegration class.
        /// </summary>
        /// <param name="aiManager">The AIManager instance for runtime agent management.</param>
        /// <param name="agentDb">The AgentDatabase instance for persistent storage.</param>
        /// <param name="performanceDb">The PerformanceDatabase instance for tracking agent performance.</param>
        public CognitiveSystemIntegration(AIManager aiManager, AgentDatabase agentDb, PerformanceDatabase performanceDb)
        {
            _aiManager = aiManager ?? throw new ArgumentNullException(nameof(aiManager));
            _agentDb = agentDb ?? throw new ArgumentNullException(nameof(agentDb));
            _performanceDb = performanceDb ?? throw new ArgumentNullException(nameof(performanceDb));
            _promptImporter = new AgentPromptImporter();

            // Subscribe to AIManager events to track performance
            SubscribeToEvents();
        }

        /// <summary>
        /// Initializes the cognitive system by importing agent prompts and creating runtime agents.
        /// </summary>
        /// <param name="useSubset">If true, creates only a subset of agents for initial testing.</param>
        /// <returns>A dictionary of created agent names and their database IDs.</returns>
        //public async Task<Dictionary<string, int>> InitializeCognitiveSystemAsync(bool useSubset = false)
        //{
        //    Debug.WriteLine("Initializing cognitive system...");

        //    // Import agent prompts into the database
        //    var agentIds = await _promptImporter.ImportCognitiveAgentPromptsAsync(_agentDb);

        //    // Create runtime agents based on database records
        //    if (useSubset)
        //    {
        //        // For initial testing, create only a subset of agents
        //        await CreateAgentSubsetAsync(agentIds);
        //    }
        //    else
        //    {
        //        // Create all agents
        //        await CreateAllAgentsAsync(agentIds);
        //    }

        //    Debug.WriteLine("Cognitive system initialization complete.");
        //    return agentIds;
        //}

        /// <summary>
        /// Creates a subset of agents for initial testing.
        /// </summary>
        /// <param name="agentIds">Dictionary of agent names and their database IDs.</param>
        private async Task CreateAgentSubsetAsync(Dictionary<string, int> agentIds)
        {
            // Create a small initial testing set (Chief, Evaluator, Innovator)
            string[] initialAgents = { "Chief", "Evaluator", "Innovator" };

            Debug.WriteLine("Creating subset of agents for initial testing...");

            foreach (string agentName in initialAgents)
            {
                if (agentIds.TryGetValue(agentName, out int agentId))
                {
                    await CreateAgentFromDatabaseAsync(agentId);
                }
            }

            Debug.WriteLine($"Created {initialAgents.Length} agents for testing.");
        }

        /// <summary>
        /// Creates all agents from the database.
        /// </summary>
        /// <param name="agentIds">Dictionary of agent names and their database IDs.</param>
        private async Task CreateAllAgentsAsync(Dictionary<string, int> agentIds)
        {
            Debug.WriteLine("Creating all cognitive agents...");

            foreach (var agentPair in agentIds)
            {
                await CreateAgentFromDatabaseAsync(agentPair.Value);
            }

            Debug.WriteLine($"Created {agentIds.Count} cognitive agents.");
        }

        /// <summary>
        /// Creates a runtime agent in the AIManager based on a database record.
        /// </summary>
        /// <param name="agentId">The database ID of the agent to create.</param>
        /// <returns>The created AIAgent instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the agent or its prompt version cannot be found.</exception>
        public async Task<AIAgent> CreateAgentFromDatabaseAsync(int agentId)
        {
            // Get agent details from database
            var agentInfo = await _agentDb.GetAgentAsync(agentId);
            if (agentInfo == null)
            {
                var message = $"Agent with ID {agentId} not found in database.";
                Debug.WriteLine($"[ERROR] {message}");
                throw new InvalidOperationException(message);
            }

            // Get current version of agent's prompt
            var versionInfo = await _agentDb.GetCurrentAgentVersionAsync(agentId);
            if (versionInfo == null)
            {
                var message = $"No active prompt version found for agent '{agentInfo.Name}' (ID: {agentId}). " +
                              "Ensure the agent has at least one prompt version marked as active.";
                Debug.WriteLine($"[ERROR] {message}");
                throw new InvalidOperationException(message);
            }

            // Create agent in AIManager if it doesn't already exist
            if (!_aiManager.AgentExists(agentInfo.Name))
            {
                Debug.WriteLine($"Creating agent {agentInfo.Name} with prompt version {versionInfo.VersionNumber}...");
                return _aiManager.CreateAgent(agentInfo.Name, versionInfo.Prompt);
            }
            else
            {
                Debug.WriteLine($"Agent {agentInfo.Name} already exists in AIManager.");
                return _aiManager.GetAgent(agentInfo.Name);
            }
        }

        /// <summary>
        /// Attempts to create a runtime agent, returning null on failure instead of throwing.
        /// </summary>
        /// <param name="agentId">The database ID of the agent to create.</param>
        /// <returns>The created AIAgent instance, or null if creation failed.</returns>
        public async Task<AIAgent?> TryCreateAgentFromDatabaseAsync(int agentId)
        {
            try
            {
                return await CreateAgentFromDatabaseAsync(agentId);
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"[WARN] Failed to create agent {agentId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Subscribes to AIManager events to track agent performance.
        /// </summary>
        private void SubscribeToEvents()
        {
            // Track when requests start for processing time calculation
            _aiManager.AgentRequest += (sender, args) =>
            {
                _requestStartTimes.TryAdd(args.RequestData, DateTime.UtcNow);
            };

            // When all agents have responded to a request, analyze and record performance
            _aiManager.AllResponsesCompleted += async (sender, args) =>
            {
                await ProcessCompletedInteractionAsync(args.RequestData);
                // Clean up the request start time after processing
                _requestStartTimes.TryRemove(args.RequestData, out _);
            };
        }

        /// <summary>
        /// Processes completed interactions to record performance metrics.
        /// </summary>
        /// <param name="requestData">The request that was processed.</param>
        private async Task ProcessCompletedInteractionAsync(string requestData)
        {
            // Determine question/task type
            string taskType = DetermineTaskType(requestData);

            // Get all responses for this request
            var responses = _aiManager.Responses.GetResponsesForRequest(requestData);

            Debug.WriteLine($"Processing completed interaction for task: {taskType}");

            foreach (var response in responses)
            {
                // For this implementation, we'll consider all responses "correct"
                // In a real system, you would implement validation logic here
                bool isCorrect = true;

                // Calculate actual processing time using request start time and response timestamp
                float processingTime = 1.0f; // Default fallback
                if (_requestStartTimes.TryGetValue(requestData, out DateTime startTime))
                {
                    processingTime = (float)(response.Timestamp - startTime).TotalSeconds;
                }

                // Ensure agent exists in database
                int agentId = await EnsureAgentExistsAsync(response.AgentName);

                // Record the interaction in AgentDatabase
                await _agentDb.RecordInteractionAsync(
                    agentId,
                    taskType,
                    requestData,
                    response.ResponseData,
                    isCorrect,
                    processingTime
                );

                // Also record in PerformanceDatabase for quick statistics
                _performanceDb.RecordPerformance(
                    response.AgentName,
                    taskType,
                    isCorrect,
                    requestData,
                    response.ResponseData
                );

                Debug.WriteLine($"Recorded performance for agent {response.AgentName} on task {taskType}");
            }
        }

        /// <summary>
        /// Ensures an agent exists in the database, creating it if necessary.
        /// </summary>
        /// <param name="agentName">The name of the agent.</param>
        /// <returns>The database ID of the agent.</returns>
        private async Task<int> EnsureAgentExistsAsync(string agentName)
        {
            // Get all agents from database
            var agents = await _agentDb.GetAllAgentsAsync();

            // Find agent by name
            var existingAgent = agents.FirstOrDefault(a =>
                a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

            if (existingAgent != null)
            {
                return existingAgent.AgentId;
            }

            // If agent doesn't exist in database, create a basic entry
            AIAgent runtimeAgent = _aiManager.GetAgent(agentName);
            string prompt = "Default prompt"; // Ideally get the actual prompt
            string purpose = "AI Cognitive Agent";

            int newAgentId = await _agentDb.AddAgentAsync(
                agentName,
                purpose,
                prompt
            );

            Debug.WriteLine($"Created new agent in database: {agentName} (ID: {newAgentId})");
            return newAgentId;
        }

        /// <summary>
        /// Determines the type of task from the request data.
        /// </summary>
        /// <param name="requestData">The request being processed.</param>
        /// <returns>A string identifying the task type for database storage.</returns>
        private string DetermineTaskType(string requestData)
        {
            var taskType = TaskTypeClassifier.Classify(requestData);
            return TaskTypeClassifier.ToStorageString(taskType);
        }

        /// <summary>
        /// Runs a test scenario with the cognitive system.
        /// </summary>
        /// <param name="testDescription">Description of the test scenario.</param>
        public async Task RunTestScenarioAsync(string testDescription)
        {
            Debug.WriteLine($"Running test scenario: {testDescription}");

            // Send the request to all agents
            await _aiManager.RequestAllAsync(testDescription);

            // Processing will happen in the AllResponsesCompleted event
        }

        /// <summary>
        /// Generates a performance report for all agents.
        /// </summary>
        /// <returns>A formatted string containing the performance report.</returns>
        public string GeneratePerformanceReport()
        {
            var summaries = _performanceDb.GetAgentPerformanceSummary();

            var report = new System.Text.StringBuilder();
            report.AppendLine("========== COGNITIVE SYSTEM PERFORMANCE REPORT ==========");
            report.AppendLine($"Generated: {DateTime.Now}");
            report.AppendLine();

            // Group by task type
            var byTaskType = summaries.GroupBy(s => s.QuestionType);

            foreach (var group in byTaskType)
            {
                report.AppendLine($"=== {group.Key.ToUpper()} TASKS ===");

                foreach (var summary in group.OrderByDescending(s => s.SuccessRate))
                {
                    report.AppendLine(
                        $"{summary.AgentName}: " +
                        $"{summary.CorrectAnswers}/{summary.TotalAttempts} = " +
                        $"{summary.SuccessRate:P2}"
                    );
                }

                report.AppendLine();
            }

            // Overall performance by agent
            var byAgent = summaries.GroupBy(s => s.AgentName);

            report.AppendLine("=== OVERALL AGENT PERFORMANCE ===");
            foreach (var agent in byAgent)
            {
                int totalCorrect = agent.Sum(s => s.CorrectAnswers);
                int totalAttempts = agent.Sum(s => s.TotalAttempts);
                double overallRate = totalAttempts > 0 ? (double)totalCorrect / totalAttempts : 0;

                report.AppendLine(
                    $"{agent.Key}: " +
                    $"{totalCorrect}/{totalAttempts} = " +
                    $"{overallRate:P2}"
                );
            }

            Debug.WriteLine("Performance report generated.");
            return report.ToString();
        }
    }
}