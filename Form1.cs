using AnthropicApp;
using AnthropicApp.AICollaborationSystem;
using AnthropicApp.Threading;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CollaborationSystemDemo
{
    public partial class Form1 : Form
    {
        private int _chiefClarificationAttempts = 0;
        private const int MAX_CHIEF_CLARIFICATION_ATTEMPTS = 3; // Or a suitable small number
        private string _lastChiefErrorResponse = string.Empty; // To detect repetitive errors

        public Form1()
        {
            InitializeComponent();
        }

        private ProcessingState _currentProcessingState = ProcessingState.Idle;
        private string _currentGoal = string.Empty;
        private List<ProcessingRecord> _processingHistory = new List<ProcessingRecord>();

        // Stores outputs from specialist modules before sending to Chief for synthesis
        private Dictionary<string, string> _pendingSpecialistOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Stores which specialist modules the Chief requested input from
        private HashSet<string> _expectedSpecialistModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Phased execution tracking - allows sequential execution of phases with parallel agents within each phase
        private List<List<ActivationInfo>> _phasedActivations = new List<List<ActivationInfo>>();
        private int _currentExecutionPhase = 0;
        private Dictionary<string, string> _completedPhaseOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Stores the Chief's last complete output/directive
        private string _lastChiefDirective = string.Empty;

        public class ProcessingRecord
        {
            public DateTime Timestamp { get; set; }
            public string SourceModule { get; set; } // e.g., "User", "Chief", "Evaluator", "System:Tool", "System:User"
            public string OutputContent { get; set; }
        }

        private enum ProcessingState
        {
            Idle,
            AwaitingChiefInitiation, // Waiting for Chief's first response to the goal
            AwaitingSpecialistInput, // Waiting for responses from polled specialists
            AwaitingChiefSynthesis,  // Specialist inputs received, waiting for Chief synthesis/next step
            AwaitingUserInput,       // Waiting for input via InputDialogForm
            ProcessingComplete,
            Error
        }

        private AIManager _aiManager;
        private AgentDatabase _agentDb;
        private PerformanceDatabase _performanceDb;
        private CognitiveSystemIntegration _integration;

        public event EventHandler<UserInputRequestEventArgs>? UserInputRequired;

        private async void Form1_Load(object sender, EventArgs e)
        {
            InitializeCognitiveSystem();
            await TestCognitiveLoop_ChiefInitiation();
        }

        private void cancelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _aiManager.CancelAllRequests();
            Debug.WriteLine("All requests cancelled");
        }

        private async void forcePromptUpdateButton_Click(object sender, EventArgs e)
        {
            await ForceUpdateBasePromptsAsync(_agentDb);
            // Optional: Re-initialize runtime agents after updating DB prompts
            await InitializeCognitiveAgentsAsync();
            MessageBox.Show("Forced prompt update attempted. Check Debug Output.");
        }

        private async void removeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string agentName = "SentimentAnalyzer";

            await RemoveAgent(agentName);
        }

        private void displayResponsesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var requests = _aiManager.Responses.GetAllRequests();

            if (requests.Count == 0)
            {
                MessageBox.Show("No responses available.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // --- Simple approach: Show responses for the *last* request --- In a real app, provide
            // a way to select the request (e.g., ComboBox, ListBox)
            string requestData = requests.LastOrDefault(); // Get the most recent request
            if (string.IsNullOrEmpty(requestData)) return;

            var responses = _aiManager.Responses.GetResponsesForRequest(requestData);
            if (responses.Count == 0)
            {
                MessageBox.Show($"No responses found for the request: {TruncateResponse(requestData, 50)}", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // --- End Simple approach ---

            // Show responses in a new form or dialog
            ShowResponsesDialog(requestData, responses);
        }

        private void ShowResponsesDialog(string requestData, List<AgentResponse> responses)
        {
            // In a real app, create a more sophisticated form. For now, display in a simple dialog
            // or log to debug.
            StringBuilder responseText = new StringBuilder();
            responseText.AppendLine($"Responses for request: {TruncateResponse(requestData, 100)}");
            responseText.AppendLine("--------------------------------------------------");

            if (responses == null || responses.Count == 0)
            {
                responseText.AppendLine("No responses received for this request.");
            }
            else
            {
                foreach (var response in responses.OrderBy(r => r.AgentName))
                {
                    responseText.AppendLine($"Agent: {response.AgentName} (Votes: {response.Votes})");
                    responseText.AppendLine($"Timestamp: {response.Timestamp:yyyy-MM-dd HH:mm:ss}");
                    responseText.AppendLine("Response:");
                    responseText.AppendLine(response.ResponseData); // Show full response in dialog
                    responseText.AppendLine("---");
                }
                // Optionally highlight the winning response
                var winner = _aiManager?.Responses?.GetWinningResponse(requestData);
                if (winner != null)
                {
                    responseText.AppendLine($"\nWINNING RESPONSE (Based on votes): {winner.AgentName} ({winner.Votes} votes)");
                }
            }

            Debug.WriteLine(responseText.ToString()); // Log detailed view to Debug
            ShowReportInDialog("Agent Responses", responseText.ToString()); // Show in UI dialog
        }

        public async Task RemoveAgent(string theAgent)
        {
            var x = EnsureAgentExists(theAgent);

            if (x != 0)
            {
                var agent = await _agentDb.GetAgentAsync(x);
                await _agentDb.RemoveAgentCompletelyAsync(agent.AgentId);
                MessageBox.Show($"Removed:\nID: {agent.AgentId}\nNAME: {agent.Name}");
            }
            else
            {
                MessageBox.Show("Agent not found");
            }
        }

        public async Task<bool> RemoveAgent(int id)
        {
            var agent = await _agentDb.GetAgentAsync(id);

            try
            {
                if (id != 0 && agent.AgentId != 0 && !string.IsNullOrEmpty(agent.Name))
                {
                    await _agentDb.RemoveAgentCompletelyAsync(agent.AgentId);
                    return true;
                    // MessageBox.Show($"Removed:\nID: {agent.AgentId}\nNAME: {agent.Name}");
                }
                else
                {
                    // MessageBox.Show("Agent not found");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Handle specific exceptions if needed
                Debug.WriteLine($"Error removing agent: {ex.Message}");
                // Optionally log the error or show a message to the user
                return false;
            }
        }

        private int EnsureAgentExists(string agentName)
        {
            // Simple implementation - expand as needed
            var agents = _agentDb.GetAllAgentsAsync().Result;
            var existingAgent = agents.FirstOrDefault(a => a.Name == agentName);

            if (existingAgent == null || string.IsNullOrEmpty(existingAgent.Name))
                return 0;

            return existingAgent.AgentId;
        }

        private async void InitializeCognitiveSystem()
        {
            _aiManager = new AIManager();
            _agentDb = new AgentDatabase("cognitive_agents.db"); // Ensure correct path
            _performanceDb = new PerformanceDatabase("agent_performance.db"); // Ensure correct path
            _integration = new CognitiveSystemIntegration(_aiManager, _agentDb, _performanceDb);

            // Initialize databases first
            _agentDb.Initialize();
            _performanceDb.Initialize();

            // Initialize agents (imports prompts, creates runtime agents) Decide if you want full
            // init or subset init on startup
            await InitializeCognitiveSystemAsync(useSubset: false); // Example: Initialize full system

            // Subscribe to events
            SubscribeToEvents();
            Debug.WriteLine("Core cognitive components initialized.");
        }

        private void SubscribeToEvents()
        {
            if (_aiManager == null)
            {
                MessageBox.Show("Cannot subscribe to events: AIManager is null.");
                return;
            }

            // Subscribe
            _aiManager.AgentAdded += AgentAddedEvent;
            _aiManager.AgentRemoved += AgentRemovedEvent;
            _aiManager.AgentError += AgentErrorEvent;
            _aiManager.AgentCompleted += AgentCompletedEvent;
            _aiManager.AgentStatus += AgentStatusEvent;
            _aiManager.AgentRequest += AgentRequestEvent;
            _aiManager.AgentResponse += AgentResponseEvent; // Keep this
            _aiManager.AllResponsesCompleted += AllResponsesCompletedEvent;
            UserInputRequired += OrchestratorUserInputRequiredHandler;

            Debug.WriteLine("Subscribed to Events.");
        }

        private async Task InitializeCognitiveSystemAsync(bool useSubset = false) // Now accepts flag
        {
            try
            {
                Debug.WriteLine($"Starting cognitive system initialization (useSubset={useSubset})...");

                // Import/Update Agent Prompts in DB and Create Runtime Agents
                await InitializeCognitiveAgentsAsync(); // Handles DB import and AIManager creation

                // Ensure the Cognitive Team exists in the Database
                await CreateCognitiveTeamInDbAsync();

                Debug.WriteLine($"Cognitive system initialization complete. Runtime agents: {string.Join(", ", _aiManager.GetAgentNames())}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FATAL Error initializing cognitive system: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show(
                    $"Failed to initialize cognitive system: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        public async Task TestCognitiveLoop_ChiefInitiation()
        {
            string testName = "Cognitive Loop Initiation Test";
            Debug.WriteLine($"\n===== STARTING TEST: {testName} =====");

            // --- 1. Prerequisites Check ---
            if (_aiManager == null || _agentDb == null)
            {
                Debug.WriteLine($"[Test Error - {testName}] AIManager or AgentDatabase not initialized. Aborting.");
                MessageBox.Show("Core components not initialized.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!_aiManager.AgentExists("Chief"))
            {
                Debug.WriteLine($"[Test Error - {testName}] Chief agent not found in AIManager. Aborting.");
                MessageBox.Show("Chief agent not found. Cannot start test.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (_currentProcessingState != ProcessingState.Idle && _currentProcessingState != ProcessingState.Error && _currentProcessingState != ProcessingState.ProcessingComplete)
            {
                Debug.WriteLine($"[Test Warning - {testName}] A task might already be in progress ({_currentProcessingState}). Attempting to proceed, but results may be unpredictable.");
                // Optionally force reset or abort: MessageBox.Show("A task is already in progress.
                // Please wait or cancel.", testName + " Warning", MessageBoxButtons.OK,
                // MessageBoxIcon.Warning); return;
            }

            // --- 2. Define Goal & Reset State ---
            string testGoal = "Develop a simple Python function to calculate factorial.";
            Debug.WriteLine($"[Test - {testName}] Goal: {testGoal}");

            // Reset controller state (Mimics InitiateCognitiveTask_Click start)
            _currentGoal = testGoal;
            _processingHistory.Clear();
            _pendingSpecialistOutputs.Clear();
            _expectedSpecialistModules.Clear();
            _phasedActivations.Clear();
            _currentExecutionPhase = 0;
            _completedPhaseOutputs.Clear();
            _lastChiefDirective = string.Empty;
            _currentProcessingState = ProcessingState.Idle; // Start from Idle

            // Clear relevant UI if needed (e.g., log display) InvokeIfNeeded(() => {
            // lstProcessingLog.Items.Clear(); });

            // --- 3. Initiate Task CORRECTLY (Chief Only) ---
            _currentProcessingState = ProcessingState.AwaitingChiefInitiation; // Set state BEFORE sending request
            LogProcessingRecord("User (Test)", testGoal); // Log the goal
            SetUIBusy(true, $"Sending test goal to Chief...");
            Debug.WriteLine($"[Test - {testName}] Sending initial request ONLY to Chief.");

            // Use a TCS to wait for the controller to process the Chief's *first* response
            var firstStepProcessedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ProcessingState stateAfterChiefResponse = ProcessingState.Idle; // Variable to capture state change

            // Temporary handler to watch for state change after Chief response is processed
            EventHandler<AgentResponseEventArgs>? tempResponseHandler = null;
            tempResponseHandler = (sender, args) =>
            {
                // We only care about the state *after* the Chief's response has been handled by AgentResponseEvent
                if (args.AgentName.Equals("Chief", StringComparison.OrdinalIgnoreCase))
                {
                    // Give the main AgentResponseEvent handler a moment to finish processing This
                    // is a slight hack; a more robust way might involve another signal.
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        if (_currentProcessingState != ProcessingState.AwaitingChiefInitiation)
                        {
                            stateAfterChiefResponse = _currentProcessingState; // Capture the new state
                            Debug.WriteLine($"[Test Handler - {testName}] Detected state change after Chief response to: {stateAfterChiefResponse}. Signaling completion.");
                            firstStepProcessedTcs.TrySetResult(true); // Signal that the first step processing is done
                                                                      // Unsubscribe immediately
                                                                      // inside the continuation
                            if (tempResponseHandler != null)
                            {
                                _aiManager.AgentResponse -= tempResponseHandler;
                                Debug.WriteLine($"[Test Handler - {testName}] Unsubscribed temporary response handler.");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[Test Handler - {testName}] Chief responded but state is still {ProcessingState.AwaitingChiefInitiation}. Waiting...");
                        }
                    });
                }
            };

            _aiManager.AgentResponse += tempResponseHandler; // Subscribe temporary handler

            bool requestSent = false;
            Stopwatch testStopwatch = Stopwatch.StartNew();
            try
            {
                await _aiManager.RequestAsync("Chief", $"New Goal Received: \"{testGoal}\". Analyze this goal, determine initial modules needed, and outline first activation steps.");
                requestSent = true;
            }
            catch (Exception ex)
            {
                HandleProcessingError($"[Test - {testName}] Error sending initial request to Chief: {ex.Message}", showMessageBox: false);
                firstStepProcessedTcs.TrySetResult(false); // Signal failure
            }

            // --- 4. Wait for First Step Processing or Timeout ---
            bool firstStepSuccess = false;
            if (requestSent)
            {
                TimeSpan timeout = TimeSpan.FromMinutes(2); // Timeout for Chief's first response + parsing
                Debug.WriteLine($"[Test - {testName}] Waiting up to {timeout.TotalSeconds}s for controller to process Chief's first response...");
                var completedTask = await Task.WhenAny(firstStepProcessedTcs.Task, Task.Delay(timeout));

                if (completedTask == firstStepProcessedTcs.Task)
                {
                    firstStepSuccess = await firstStepProcessedTcs.Task; // Get the result (true if state changed)
                    Debug.WriteLine($"[Test - {testName}] First step processing signal received. Success: {firstStepSuccess}. State is now: {stateAfterChiefResponse}. Time: {testStopwatch.ElapsedMilliseconds}ms.");
                }
                else
                {
                    Debug.WriteLine($"[Test Error - {testName}] Timed out waiting for controller to process Chief's first response after {testStopwatch.ElapsedMilliseconds}ms.");
                    firstStepSuccess = false;
                    // Attempt to reset state if timed out
                    _currentProcessingState = ProcessingState.Error;
                    SetUIBusy(false, "Test timed out.");
                }
            }

            testStopwatch.Stop();

            // --- 5. Cleanup Temporary Handler --- Ensure unsubscribe even if timeout occurred or
            // TCS was already set
            if (tempResponseHandler != null)
            {
                _aiManager.AgentResponse -= tempResponseHandler;
                Debug.WriteLine($"[Test Cleanup - {testName}] Ensured temporary response handler unsubscribed.");
            }

            // --- 6. Verification ---
            Debug.WriteLine($"[Test Verification - {testName}]");
            bool passed = true;

            if (!firstStepSuccess)
            {
                Debug.WriteLine("  [FAIL] Controller did not process the Chief's initial response or timed out.");
                passed = false;
            }
            else
            {
                Debug.WriteLine($"  [PASS] Controller processed Chief's response. State transitioned to: {stateAfterChiefResponse}");
                // Further checks based on expected state:
                if (stateAfterChiefResponse == ProcessingState.AwaitingSpecialistInput)
                {
                    Debug.WriteLine($"  [INFO] Controller is now awaiting specialist input (Expected modules: {string.Join(", ", _expectedSpecialistModules)}). This indicates successful parsing and delegation.");
                }
                else if (stateAfterChiefResponse == ProcessingState.AwaitingChiefSynthesis)
                {
                    Debug.WriteLine($"  [INFO] Controller requested clarification or synthesis from Chief. State: {stateAfterChiefResponse}. This indicates the parser might have failed or the response was ambiguous.");
                    // This might be acceptable depending on the goal complexity and Chief's response
                }
                else if (stateAfterChiefResponse == ProcessingState.AwaitingUserInput)
                {
                    Debug.WriteLine($"  [INFO] Controller is awaiting user input. This indicates Chief requested clarification via ACTION: Ask User.");
                }
                else if (stateAfterChiefResponse == ProcessingState.ProcessingComplete)
                {
                    Debug.WriteLine($"  [INFO] Controller indicates processing completed immediately after Chief's first response (Goal might be simple).");
                }
                else
                {
                    Debug.WriteLine($"  [WARN] Controller ended in unexpected state: {stateAfterChiefResponse}");
                    // Consider if this state is a failure for this test
                }
            }

            // Check if any RequestAllAsync was logged (it shouldn't be for initiation) This
            // requires modifying LogProcessingRecord or having another logging mechanism bool
            // broadcastDetected = _processingHistory.Any(r => r.SourceModule.Equals("System
            // (Broadcast)", StringComparison.OrdinalIgnoreCase)); if(broadcastDetected) {
            // Debug.WriteLine(" [FAIL] A broadcast request (RequestAllAsync) seems to have been
            // used incorrectly."); passed = false; } else { Debug.WriteLine(" [PASS] No incorrect
            // broadcast detected during initiation."); }

            // --- 7. Conclusion ---
            Debug.WriteLine($"===== TEST {(passed ? "PASSED" : "FAILED")}: {testName} =====");
            MessageBox.Show(
                $"Test '{testName}' completed.\nResult: {(passed ? "PASSED" : "FAILED")}\n\nCheck Debug Output for detailed logs.",
                "Test Result",
                MessageBoxButtons.OK,
                passed ? MessageBoxIcon.Information : MessageBoxIcon.Warning
            );

            // Reset UI state if test didn't end in error already
            if (_currentProcessingState != ProcessingState.Error)
            {
                // If the test left the system waiting for specialists/user/synthesis, reset to Idle
                // for next manual operation. Or potentially allow it to continue running if
                // desired. For a test, resetting is usually safer.
                if (_currentProcessingState == ProcessingState.AwaitingSpecialistInput ||
                    _currentProcessingState == ProcessingState.AwaitingUserInput ||
                    _currentProcessingState == ProcessingState.AwaitingChiefSynthesis)
                {
                    Debug.WriteLine($"[Test - {testName}] Resetting state to Idle after test completion.");
                    _currentProcessingState = ProcessingState.Idle;
                    SetUIBusy(false, "Test complete. Ready.");
                }
                else
                {
                    SetUIBusy(false); // Ensure UI enabled if test finished cleanly or failed before timeout
                }
            }
        }

        // This method likely becomes redundant if InitializeCognitiveAgentsAsync handles creation.
        // Keep for reference or if direct creation is sometimes needed.
        private void createCognitiveSystemAgents()
        {
            var cognitiveAgents = new Dictionary<string, string>()
            {
                { "Chief", "You are the Chief agent. Coordinate others, make final decisions, synthesize responses." },
                { "Evaluator", "You are the Evaluator agent. Critically analyze ideas, identify flaws, provide constructive criticism." }
                // Add other agents if needed for direct creation...
            };

            foreach (var agentPair in cognitiveAgents)
            {
                string agentName = agentPair.Key;
                string agentPrompt = agentPair.Value;

                if (!_aiManager.AgentExists(agentName))
                {
                    // Note: This creates with the BASE prompt. If it's Chief/Evaluator,
                    // GetCompleteAgentPromptAsync should ideally be used later if re-initializing.
                    _aiManager.CreateAgent(agentName, agentPrompt);
                    Debug.WriteLine($"Directly created agent: {agentName}");
                }
            }
            Debug.WriteLine("Cognitive system agents created directly (if not already existing).");
        }

        /// <summary>
        /// Creates a new agent with simulated poor performance data for testing self-improvement.
        /// </summary>
        /// <param name="agentName">Name for the new agent (e.g., "FaultyPlanner").</param>
        /// <param name="purpose">Purpose Description for the agent.</param>
        /// <param name="initialPrompt">The base prompt for this agent.</param>
        /// <param name="weakTaskType">The TaskType string where performance should be poor.</param>
        /// <param name="totalAttempts">Total number of simulated interactions for the weak task.</param>
        /// <param name="correctAttempts">Number of 'correct' interactions out of the total attempts.</param>
        /// <returns>True if agent and performance data were added successfully, false otherwise.</returns>
        public async Task<bool> AddUnderperformingAgentAsync(
            string agentName, string purpose,
            string initialPrompt, string weakTaskType, int totalAttempts = 10, // Default to 10 attempts
            int correctAttempts = 2) // Default to 20% success rate
        {
            var agent = await _agentDb.GetAgentUsingNameAsync(agentName);

            if (agent.AgentId > 0)
            {
                await _agentDb.RemoveAgentCompletelyAsync(agent.AgentId); // Attempt cleanup
            }

            string testName = $"Add Underperforming Agent ({agentName})";
            Debug.WriteLine($"\n===== STARTING TEST: {testName} =====");

            // --- Validation ---
            if (_agentDb == null || _aiManager == null)
            {
                Debug.WriteLine($"[Test Error - {testName}] Database or AIManager not initialized.");
                MessageBox.Show("Database/AIManager not ready.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(purpose) || string.IsNullOrWhiteSpace(initialPrompt) || string.IsNullOrWhiteSpace(weakTaskType))
            {
                Debug.WriteLine($"[Test Error - {testName}] Agent details (Name, Purpose, Prompt, WeakTaskType) cannot be empty.");
                MessageBox.Show("Agent details cannot be empty.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (totalAttempts <= 0 || correctAttempts < 0 || correctAttempts > totalAttempts)
            {
                Debug.WriteLine($"[Test Error - {testName}] Invalid attempt numbers (Total: {totalAttempts}, Correct: {correctAttempts}).");
                MessageBox.Show("Invalid attempt numbers provided.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // --- Check if Agent Already Exists ---
            var existingAgents = await _agentDb.GetAllAgentsAsync();
            if (existingAgents.Any(a => a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.WriteLine($"[Test Warning - {testName}] Agent '{agentName}' already exists in the database. Skipping creation.");
                MessageBox.Show($"Agent '{agentName}' already exists.", testName + " Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false; // Don't overwrite existing agent in this test function
            }

            SetUIBusy(true, $"Creating underperforming agent '{agentName}'...");
            int newAgentId = 0;
            int newVersionId = 0;

            try
            {
                // --- 1. Add Agent and Initial Version ---
                newAgentId = await _agentDb.AddAgentAsync(
                    name: agentName,
                    purpose: purpose,
                    initialPrompt: initialPrompt,
                    comments: "Test agent with simulated poor performance.",
                    createdBy: "SystemTest"
                );

                if (newAgentId <= 0) throw new Exception("Failed to create agent core record.");
                Debug.WriteLine($"[Test - {testName}] Agent '{agentName}' created in DB with ID: {newAgentId}.");

                // Get the ID of the version just created (should be the only active one)
                var currentVersion = await _agentDb.GetCurrentAgentVersionAsync(newAgentId);
                if (currentVersion == null) throw new Exception("Failed to retrieve newly created agent version.");
                newVersionId = currentVersion.VersionId;
                Debug.WriteLine($"[Test - {testName}] Initial Version ID: {newVersionId}.");

                // --- 2. Add Basic Capabilities (Optional) ---
                await _agentDb.AddAgentCapabilityAsync(newAgentId, "Reasoning", "Basic reasoning capability.", 0.5f);
                await _agentDb.AddAgentCapabilityAsync(newAgentId, "Communication", "Basic communication capability.", 0.5f);
                Debug.WriteLine($"[Test - {testName}] Added basic capabilities.");

                // --- 3. Simulate Poor Performance Interactions ---
                Debug.WriteLine($"[Test - {testName}] Simulating {totalAttempts} interactions for TaskType '{weakTaskType}' with {correctAttempts} correct responses...");
                Random rnd = new Random();
                List<bool> correctnessDistribution = Enumerable.Repeat(true, correctAttempts)
                                                            .Concat(Enumerable.Repeat(false, totalAttempts - correctAttempts))
                                                            .OrderBy(x => rnd.Next()) // Shuffle true/false
                                                            .ToList();

                for (int i = 0; i < totalAttempts; i++)
                {
                    bool isCorrect = correctnessDistribution[i];
                    string request = $"Simulated Request {i + 1} for {weakTaskType}";
                    string response = isCorrect ? $"Simulated CORRECT Response {i + 1}" : $"Simulated INCORRECT Response {i + 1}";
                    float processingTime = (float)(rnd.NextDouble() * 2.0 + 0.5); // Simulate 0.5-2.5 seconds

                    // Use RecordInteractionAsync to correctly update all related tables
                    await _agentDb.RecordInteractionAsync(
                        agentId: newAgentId,
                        taskType: weakTaskType,
                        requestData: request,
                        responseData: response,
                        isCorrect: isCorrect,
                        processingTime: processingTime,
                        evaluationNotes: "Simulated performance data."
                    );
                    // Small delay to ensure timestamps differ slightly if needed
                    await Task.Delay(5);
                }
                Debug.WriteLine($"[Test - {testName}] Finished simulating interactions.");

                // --- 4. Ensure Runtime Agent Exists (Optional - depends if needed immediately) ---
                if (!_aiManager.AgentExists(agentName))
                {
                    _aiManager.CreateAgent(agentName, initialPrompt);
                    Debug.WriteLine($"[Test - {testName}] Created runtime agent '{agentName}'.");
                }

                // --- 5. Final Score Recalculation (Recommended) --- Although
                // RecordInteractionAsync updates scores incrementally, a final update ensures accuracy.
                bool scoreUpdateSuccess = await _agentDb.UpdateActiveVersionScoresForAgentAsync(newAgentId);
                Debug.WriteLine($"[Test - {testName}] Final score recalculation {(scoreUpdateSuccess ? "succeeded" : "failed")}.");

                Debug.WriteLine($"\n===== TEST SUCCEEDED: {testName} =====");
                MessageBox.Show($"Agent '{agentName}' created with simulated poor performance ({correctAttempts}/{totalAttempts} correct on '{weakTaskType}').",
                                "Test Agent Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Test Error - {testName}] Failed: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to create underperforming agent '{agentName}': {ex.Message}",
                                testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // Attempt cleanup if agent was partially created
                if (newAgentId > 0)
                {
                    await _agentDb.RemoveAgentCompletelyAsync(newAgentId); // Attempt cleanup
                }
                return false;
            }
            finally
            {
                SetUIBusy(false);
            }
        }

        private async Task InitializeCognitiveAgentsAsync()
        {
            if (_agentDb == null || _aiManager == null)
            {
                Debug.WriteLine("Error: Databases or AI Manager not initialized before calling InitializeCognitiveAgentsAsync.");
                return;
            }
            Debug.WriteLine("Importing/Updating agent prompts in DB and creating runtime agents...");
            try
            {
                var importer = new AgentPromptImporter();

                Dictionary<string, int> agentIds = await importer.ImportCognitiveAgentPromptsAsync(_agentDb);

                foreach (var agentPair in agentIds)
                {
                    string agentName = agentPair.Key;
                    int agentId = agentPair.Value;
                    string completePrompt = await _agentDb.GetCompleteAgentPromptAsync(agentId);

                    if (string.IsNullOrEmpty(completePrompt))
                    {
                        Debug.WriteLine($"Warning: Could not retrieve complete prompt for agent {agentName} (ID: {agentId}). Skipping runtime agent creation/update.");
                        continue;
                    }

                    if (!_aiManager.AgentExists(agentName))
                    {
                        _aiManager.CreateAgent(agentName, completePrompt);
                        Debug.WriteLine($"Created runtime agent '{agentName}'.");
                    }
                    else
                    {
                        // Optional: If AIManager had an UpdatePrompt method, call it here.
                        // _aiManager.UpdateAgentPrompt(agentName, completePrompt);
                        Debug.WriteLine($"Runtime agent '{agentName}' already exists.");
                    }
                }
                Debug.WriteLine("Cognitive agents initialized/updated from database.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during agent initialization: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error initializing agents: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task CreateCognitiveTeamInDbAsync()
        {
            if (_agentDb == null) return;
            Debug.WriteLine("Ensuring Cognitive Collaboration Team exists in database...");
            try
            {
                var agents = await _agentDb.GetAllAgentsAsync();
                var agentIds = agents.ToDictionary(a => a.Name, a => a.AgentId, StringComparer.OrdinalIgnoreCase);
                string teamName = "Cognitive Collaboration Team"; // Use the name from AgentPromptImporter

                string[] requiredAgents = { "Chief", "Sentinel", "Evaluator", "Navigator", "Innovator", "Strategist", "Coder" }; // Core team

                if (requiredAgents.Any(name => !agentIds.ContainsKey(name)))
                {
                    Debug.WriteLine($"Warning: Not all required agents ({string.Join(", ", requiredAgents)}) found in the database. Cannot ensure team.");
                    // Optionally attempt to create missing agents here?
                    return;
                }

                var teams = await _agentDb.GetAllTeamsAsync();

                var existingTeam = teams.FirstOrDefault(t => t.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase));

                int teamId;
                if (existingTeam == null)
                {
                    Debug.WriteLine($"Creating team '{teamName}'...");
                    teamId = await _agentDb.CreateTeamAsync(
                        teamName,
                        agentIds["Chief"],
                        "Primary team for collaborative problem solving using cognitive specialization."
                    );
                    Debug.WriteLine($"Created '{teamName}' with ID: {teamId}");
                }
                else
                {
                    teamId = existingTeam.TeamId;
                    Debug.WriteLine($"Team '{teamName}' (ID: {teamId}) already exists.");
                }

                // Ensure all members are in the team (handles adding if missing)
                foreach (var agentName in requiredAgents)
                {
                    // Simplified role mapping - refine if needed
                    string role = agentName switch
                    {
                        "Chief" => "Chief",
                        "Sentinel" => "Verification & Compliance",
                        "Evaluator" => "Analytical Assessor",
                        "Navigator" => "Process Guide",
                        "Innovator" => "Creative Specialist",
                        "Strategist" => "Long-Term Planner",
                        "Coder" => "Writing Code",
                        _ => "Member"
                    };
                    // AddAgentToTeamAsync handles duplicates gracefully (updates role if exists)
                    await _agentDb.AddAgentToTeamAsync(teamId, agentIds[agentName], role, $"Core member of {teamName}");
                }
                Debug.WriteLine($"Verified members for team '{teamName}'.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error ensuring cognitive team in DB: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new cognitive team in the database or updates members if the team already
        /// exists. The system's main "Chief" agent is always designated as the chief of any created
        /// team. Ensures all specified member agents are part of the team with their designated roles.
        /// </summary>
        /// <param name="teamName">
        /// The name of the team to create or update. Cannot be null or empty.
        /// </param>
        /// <param name="teamMemberAgentNamesAndRoles">
        /// A dictionary where Key is AgentName and Value is their Role within this team. If the
        /// system "Chief" is included here, its role will be used; otherwise, it's added as
        /// "Chief". Can be null for an empty team (besides Chief).
        /// </param>
        /// <param name="description">A Description for the team. Optional.</param>
        /// <returns>The TeamId if successful, or 0 if creation/update failed.</returns>
        public async Task<int> CreateOrUpdateCognitiveTeamAsync(
            string teamName,
            Dictionary<string, string>? teamMemberAgentNamesAndRoles, // AgentName -> Role
            string? description = null)
        {
            const string systemChiefAgentName = "Chief"; // Define the main Chief's name

            if (_agentDb == null)
            {
                Debug.WriteLine("[CreateOrUpdateCognitiveTeamAsync Error] AgentDatabase is not initialized.");
                return 0;
            }
            if (string.IsNullOrWhiteSpace(teamName))
            {
                Debug.WriteLine("[CreateOrUpdateCognitiveTeamAsync Error] Team name cannot be empty.");
                return 0;
            }

            teamMemberAgentNamesAndRoles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Ensure the system "Chief" is always part of the member list for role assignment and
            // will be designated as the team's chief.
            if (!teamMemberAgentNamesAndRoles.ContainsKey(systemChiefAgentName))
            {
                teamMemberAgentNamesAndRoles[systemChiefAgentName] = "Chief"; // Default role for the system Chief within any team
            }
            else if (string.IsNullOrWhiteSpace(teamMemberAgentNamesAndRoles[systemChiefAgentName])) // If present but role is empty
            {
                teamMemberAgentNamesAndRoles[systemChiefAgentName] = "Chief";
            }

            Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync] Ensuring team '{teamName}' with system Chief ('{systemChiefAgentName}') as its lead, and {teamMemberAgentNamesAndRoles.Count - 1} other specified members.");

            try
            {
                var allDbAgents = await _agentDb.GetAllAgentsAsync();
                var dbAgentNameToIdMap = allDbAgents.ToDictionary(a => a.Name, a => a.AgentId, StringComparer.OrdinalIgnoreCase);

                // Validate that all specified agents (including the system Chief) exist in the database
                if (!dbAgentNameToIdMap.ContainsKey(systemChiefAgentName))
                {
                    Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync Error] System Chief agent '{systemChiefAgentName}' not found in the database. Cannot create team '{teamName}'.");
                    MessageBox.Show($"System Chief agent '{systemChiefAgentName}' is missing. Team creation aborted.", "Missing Chief Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                }

                foreach (var agentName in teamMemberAgentNamesAndRoles.Keys)
                {
                    if (!dbAgentNameToIdMap.ContainsKey(agentName))
                    {
                        Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync Error] Prerequisite agent '{agentName}' for team '{teamName}' not found in the database. Team creation/update aborted.");
                        MessageBox.Show($"Agent '{agentName}' needed for team '{teamName}' does not exist in the database.", "Missing Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 0; // Abort
                    }
                }

                int systemChiefId = dbAgentNameToIdMap[systemChiefAgentName]; // ID of the system's Chief agent

                var teams = await _agentDb.GetAllTeamsAsync();
                var existingTeam = teams.FirstOrDefault(t => string.Equals(t.TeamName, teamName, StringComparison.OrdinalIgnoreCase));
                int teamId;

                if (existingTeam == null)
                {
                    Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync] Creating new team '{teamName}' with system Chief (ID: {systemChiefId}) as its lead.");
                    description ??= $"Team '{teamName}' focused on collaborative tasks, led by the system Chief.";
                    teamId = await _agentDb.CreateTeamAsync(teamName, systemChiefId, description); // systemChiefId is always the ChiefAgentId for the team
                    if (teamId <= 0)
                    {
                        Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync Error] Failed to create team '{teamName}' in database.");
                        return 0;
                    }
                    Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync] Created team '{teamName}' with DB ID: {teamId}.");
                }
                else
                {
                    teamId = existingTeam.TeamId;
                    Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync] Team '{teamName}' (ID: {teamId}) already exists. Verifying members.");
                    if (existingTeam.ChiefAgentId != systemChiefId)
                    {
                        // This case should ideally not happen if team creation is consistent. You
                        // might want to log a more severe warning or even attempt to correct it.
                        Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync CRITICAL WARNING] Existing team '{teamName}' has ChiefAgentId {existingTeam.ChiefAgentId}, but system Chief is {systemChiefId}. This indicates a potential inconsistency in team leadership setup.");
                        // Optionally: await _agentDb.UpdateTeamChiefAsync(teamId, systemChiefId);
                        // // If you add this DB method
                    }
                    // Optionally update Description if changed if (Description != null &&
                    // existingTeam.Description != Description) { /* ... update ... */ }
                }

                // Ensure all specified members (including the system Chief with its role for this
                // team) are in the team
                foreach (var agentRolePair in teamMemberAgentNamesAndRoles)
                {
                    string agentName = agentRolePair.Key;
                    string role = agentRolePair.Value;
                    int agentId = dbAgentNameToIdMap[agentName];

                    await _agentDb.AddAgentToTeamAsync(teamId, agentId, role, $"Member of '{teamName}' with role '{role}'.");
                }

                Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync] Verified members for team '{teamName}'. Team ID: {teamId}.");
                return teamId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CreateOrUpdateCognitiveTeamAsync Error] Exception creating/updating team '{teamName}': {ex.Message}\n{ex.StackTrace}");
                return 0;
            }
        }

        private async void OrchestratorUserInputRequiredHandler(object? sender, UserInputRequestEventArgs args)
        {
            string logMsg = $"[Controller Request]: User input needed. Prompt: {args.Prompt}";
            Debug.WriteLine(logMsg);
            // LogProcessingRecord("System", $"Requesting User Input: {args.Prompt}"); // Optional:
            // Log here or rely on caller

            string? userInput = null;

            // Explicitly ensure the dialog creation and ShowDialog happens on the UI thread
            await ThreadHelper.InvokeOnUIThreadAsync(this, async () =>
            // await InvokeOnUIAsync(async () => // Use the async UI helper
            {
                Debug.WriteLine("[UI Thread] Creating and showing InputDialogForm...");
                try
                {
                    using (var inputDialog = new InputDialogForm("Cognitive Process User Input Required", args.Prompt))
                    {
                        // Ensure 'this' (Form2) is the owner for modality
                        if (inputDialog.ShowDialog(this) == DialogResult.OK)
                        {
                            userInput = inputDialog.UserInput;
                            Debug.WriteLine("[UI Thread] InputDialogForm returned OK.");
                        }
                        else
                        {
                            userInput = "[User Cancelled Input]";
                            Debug.WriteLine("[UI Thread] InputDialogForm returned Cancel.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[UI Thread ERROR] Exception during ShowDialog: {ex.Message}\n{ex.StackTrace}");
                    userInput = "[Error Showing Dialog]"; // Indicate dialog error
                }
                await Task.CompletedTask; // Satisfy async lambda if needed by helper
            }); // End InvokeOnUIThreadAsync

            Debug.WriteLine($"[Handler] User input captured: {userInput ?? "NULL"}");

            // Process the response (or cancellation/error) This needs to run *after* the UI
            // interaction is complete
            await ProcessUserInputResponse(userInput);
        }

        private async Task ProcessUserInputResponse(string? userInput)
        {
            // Check if we are actually waiting for user input.
            if (_currentProcessingState != ProcessingState.AwaitingUserInput)
            {
                Debug.WriteLine($"[Controller Warning] Received user input '{userInput}' but not in AwaitingUserInput state ({_currentProcessingState}). Ignoring.");
                return;
            }

            LogProcessingRecord("User", userInput ?? "[User Cancelled Input]"); // Log the user's action.

            // Handle cases where user cancels or provides no input.
            if (userInput == null || userInput == "[User Cancelled Input]")
            {
                HandleProcessingError("User cancelled input request. Halting task.", showMessageBox: false); // Don't show a redundant message box.
                _chiefClarificationAttempts = 0; // Reset attempts as user cancelled, not a Chief failure.
                _lastChiefErrorResponse = string.Empty;
                return; // Exit processing.
            }

            Debug.WriteLine("[Controller] User input received. Requesting Chief processing.");
            // CRITICAL: Transition state to AwaitingChiefSynthesis *before* requesting from Chief.
            // This signals that the next expected response is from the Chief for synthesis/decision.
            _currentProcessingState = ProcessingState.AwaitingChiefSynthesis;
            SetUIBusy(true, "Processing user input..."); // Update UI to indicate activity.

            // Reset clarification attempt counters as we are proceeding with new input for the Chief.
            _chiefClarificationAttempts = 0;
            _lastChiefErrorResponse = string.Empty;

            // Get summarized context to provide to the Chief.
            string contextSummary = await GetSummarizedProcessingContextAsync();
            // Construct the prompt for the Chief, clearly indicating the user's input.
            string prompt = $"The user provided the requested input in response to the previous clarification request.\n\n" +
                            $"--- Processing Context Summary ---\n{contextSummary}\n\n" +
                            $"--- User Input Provided ---\n{userInput}\n\n" +
                            $"Based on this input, please continue the process and determine the next step, ensuring your response ends with a valid concluding tag block.";

            try
            {
                // Send the request to the Chief agent.
                await _aiManager.RequestAsync("Chief", prompt);
            }
            catch (Exception ex)
            {
                // Handle any errors that occur while sending the request to the Chief.
                HandleProcessingError($"Error sending user input to Chief: {ex.Message}", true);
            }
        }

        private async void AllResponsesCompletedEvent(object? sender, AllResponsesCompletedEventArgs args)
        {
            Debug.WriteLine($"[Controller] AllResponsesCompleted event received for request: {TruncateResponse(args.RequestData, 50)}. Current State: {_currentProcessingState}");

            // --- 1. Performance Logging --- This block logs performance for any request that
            // triggers AllResponsesCompleted. This is typically relevant for broader
            // `RequestAllAsync` calls, if used.
            await Task.Run(async () =>
            {
                string taskType = DetermineTaskType(args.RequestData);
                var responses = _aiManager.Responses.GetResponsesForRequest(args.RequestData);
                Debug.WriteLine($"[Perf Logging - AllResponsesCompleted] Processing {responses.Count} responses for task type '{taskType}'.");

                if (!responses.Any()) return;

                foreach (var response in responses)
                {
                    bool isCorrect = IsResponseCorrect(response.AgentName, args.RequestData, response.ResponseData);
                    try
                    {
                        int agentId = await EnsureAgentExistsInDatabaseAsync(response.AgentName);
                        if (agentId > 0)
                        {
                            // Record interaction in AgentDatabase
                            await _agentDb.RecordInteractionAsync(agentId, taskType, args.RequestData, response.ResponseData, isCorrect, 1.0f);
                            // Record performance in PerformanceDatabase
                            _performanceDb.RecordPerformance(response.AgentName, taskType, isCorrect, args.RequestData, response.ResponseData);
                        }
                        else { Debug.WriteLine($"[Perf Logging - AllResponsesCompleted] Warning: Agent '{response.AgentName}' not found in DB."); }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[Perf Logging - AllResponsesCompleted] Error logging performance for {response.AgentName}: {ex.Message}"); }
                }
            }).ContinueWith(t => { if (t.IsFaulted) { Debug.WriteLine($"[Perf Logging - AllResponsesCompleted] Error in background task: {t.Exception?.InnerException?.Message}"); } });
            // --- End Performance Logging ---

            // --- 2. Trigger Chief Synthesis ONLY if appropriate for this event's context --- This
            // event handler should NOT interfere with the primary specialist-activation loop
            // managed by AgentResponseEvent. It should only act if the system is in a state where
            // this event is the intended trigger for synthesis (e.g., after a broad RequestAllAsync
            // initiated when system was Idle or awaiting Chief's first move).

            if (_currentProcessingState == ProcessingState.AwaitingSpecialistInput)
            {
                // If the controller is already actively waiting for specific specialists (activated
                // by Chief's [ACTIVATE] tags), this event (which might be for a different, broader
                // request context) should not disrupt that. The main AgentResponseEvent loop is
                // responsible for collecting those specific specialist outputs.
                Debug.WriteLine($"[Controller Info - AllResponsesCompleted] Event fired while _currentProcessingState is AwaitingSpecialistInput. " +
                                $"The main AgentResponseEvent loop is expected to handle specialist collection and synthesis. No action taken by this event handler to prevent conflicts.");
                return; // IMPORTANT: Exit here to let the main AgentResponseEvent loop manage its specific specialist cycle.
            }

            // If the system was Idle or just starting (AwaitingChiefInitiation), and this event
            // signals all responses to an initial broad request are in, then it's appropriate for
            // Chief to synthesize.
            if (_currentProcessingState == ProcessingState.Idle || _currentProcessingState == ProcessingState.AwaitingChiefInitiation)
            {
                var allResponses = _aiManager.Responses.GetResponsesForRequest(args.RequestData);
                if (!allResponses.Any())
                {
                    Debug.WriteLine($"[Controller Warning - AllResponsesCompleted] No responses found in collection for request '{args.RequestData}'. Cannot proceed with synthesis via this event.");
                    // This is an unexpected state if AllResponsesCompleted fired. Request clarification.
                    await RequestChiefClarificationAsync($"AllResponsesCompleted event fired for request '{TruncateResponse(args.RequestData, 50)}', but no responses were found in the collection. Please assess the situation and advise on the next step.");
                    return;
                }

                Debug.WriteLine($"[Controller - AllResponsesCompleted] Transitioning state from {_currentProcessingState} to AwaitingChiefSynthesis.");
                _currentProcessingState = ProcessingState.AwaitingChiefSynthesis; // Now ready for Chief's synthesis
                SetUIBusy(true, "Sending initial responses to Chief for synthesis...");

                // Prepare specialist outputs, excluding Chief's own potential initial response if
                // it was part of a RequestAll.
                var specialistOutputs = allResponses
                                        .Where(r => !r.AgentName.Equals("Chief", StringComparison.OrdinalIgnoreCase))
                                        .ToDictionary(r => r.AgentName, r => r.ResponseData, StringComparer.OrdinalIgnoreCase);
                // Provide Chief's initial response (if any from a RequestAll) as additional context.
                var chiefInitialResponse = allResponses.FirstOrDefault(r => r.AgentName.Equals("Chief", StringComparison.OrdinalIgnoreCase));
                string additionalInfo = chiefInitialResponse != null ? $"Chief's initial analysis/request (from broad query):\n{TruncateResponse(chiefInitialResponse.ResponseData, 300)}" : null;

                // Reset clarification attempts before this new synthesis request.
                _chiefClarificationAttempts = 0;
                _lastChiefErrorResponse = string.Empty;
                await RequestChiefSynthesisAsync(specialistOutputs, additionalInfo); // Call synthesis
            }
            else
            {
                // If AllResponsesCompleted fires in any other state (e.g., AwaitingChiefSynthesis,
                // AwaitingUserInput, Error, ProcessingComplete), it's likely not the primary
                // trigger for the next action. The main AgentResponseEvent loop should be in control.
                if (_currentProcessingState != ProcessingState.ProcessingComplete)
                {
                    Debug.WriteLine($"[Controller Warning - AllResponsesCompleted] Event fired, but current state is {_currentProcessingState}. Synthesis not automatically requested by this event path. Main loop should handle if necessary.");
                }
                // Note: ProcessingComplete state silently ignores late-arriving responses (expected behavior)
            }
        }

        private async Task<int> EnsureAgentExistsInDatabaseAsync(string agentName)
        {
            if (_agentDb == null)
            {
                Debug.WriteLine("Error: AgentDatabase not initialized in EnsureAgentExistsInDatabaseAsync.");
                return 0;
            }

            try
            {
                var agents = await _agentDb.GetAllAgentsAsync();
                var existingAgent = agents.FirstOrDefault(a => a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

                if (existingAgent != null)
                {
                    return existingAgent.AgentId;
                }
                else
                {
                    // Agent not in DB, try to get info from AIManager to create it
                    string prompt = "Default prompt - Agent created dynamically";
                    string purpose = "AI Cognitive Agent";
                    AIAgent runtimeAgent = _aiManager?.GetAgent(agentName); // Safely get runtime agent

                    // If we have a runtime agent, maybe we can get its prompt? (Requires AIAgent
                    // exposing prompt) if (runtimeAgent != null &&
                    // !string.IsNullOrEmpty(runtimeAgent.BasePrompt)) { prompt =
                    // runtimeAgent.BasePrompt; }

                    Debug.WriteLine($"Agent '{agentName}' not found in DB. Creating new entry...");
                    int newAgentId = await _agentDb.AddAgentAsync(agentName, purpose, prompt);
                    if (newAgentId > 0)
                    {
                        Debug.WriteLine($"Successfully created agent '{agentName}' in DB with ID: {newAgentId}");
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to create agent '{agentName}' in DB.");
                    }
                    return newAgentId; // Returns ID or 0 if creation failed
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in EnsureAgentExistsInDatabaseAsync for '{agentName}': {ex.Message}");
                return 0; // Indicate failure
            }
        }

        /// <summary>
        /// Determines if an agent's response is considered "correct" or "successful" for the given
        /// request and task type.
        /// NOTE: This is a critical function for meaningful performance scoring and self-improvement.
        /// The current implementation is basic and needs significant enhancement.
        /// </summary>
        /// <param name="agentName">The name of the agent providing the response.</param>
        /// <param name="requestData">The original request sent to the agent.</param>
        /// <param name="response">The response received from the agent.</param>
        /// <returns>True if the response is considered correct/successful, false otherwise.</returns>
        private bool IsResponseCorrect(string agentName, string requestData, string response)
        {
            // Determine task type (using existing helper)
            string taskType = DetermineTaskType(requestData);
            bool determinedCorrectness = true; // Default assumption

            Debug.WriteLine($"[IsResponseCorrect] Evaluating response from {agentName} for TaskType '{taskType}'.");
            // Debug.WriteLine($"[IsResponseCorrect] Request: {TruncateResponse(requestData,
            // 100)}"); Debug.WriteLine($"[IsResponseCorrect] Response: {TruncateResponse(response, 100)}");

            // --- Option 1: Rule-Based Checks for Specific Task Types --- Add more specific checks
            // here based on task types you can reliably evaluate.
            if (taskType.Equals("Addition", StringComparison.OrdinalIgnoreCase))
            {
                // Example: Simple check if response contains a number A real check would parse the
                // request, calculate the expected answer, and see if the response contains that answer.
                var match = Regex.Match(response, @"\d+"); // Check if response contains any number
                determinedCorrectness = match.Success;
                Debug.WriteLine($"[IsResponseCorrect] TaskType 'Addition'. Rule-based check (contains number): {determinedCorrectness}");
                return determinedCorrectness; // Return early for specific rule match
            }
            // Add more 'else if (taskType == "...")' blocks for other rules...
            // Example: Check if generated code compiles (would require calling compile tool handler)
            // else if (taskType == "Generation" && agentName == "Coder") { /* Call compile check */ }

            // --- Option 2: Placeholder for Evaluator Agent Logic --- If no specific rule applies,
            // consider using the Evaluator agent for complex tasks.
            /*
            if (ShouldUseEvaluatorForTask(taskType)) // Define criteria for when to use Evaluator
            {
                Debug.WriteLine($"[IsResponseCorrect] Triggering Evaluator agent for assessment...");
                // This would require modifying the workflow:
                // 1. Store the current response temporarily.
                // 2. Change state to e.g., AwaitingEvaluation.
                // 3. Build prompt for Evaluator including request, response, and criteria.
                // 4. Call _aiManager.RequestAsync("Evaluator", evalPrompt);
                // 5. In AgentResponseEvent, handle Evaluator's response: parse its assessment
                // (e.g., score/boolean).
                // 6. Set determinedCorrectness based on Evaluator's output.
                // 7. Resume the original workflow (e.g., proceed to synthesis or next step). For
                // now, we just log and use the default.
                Debug.WriteLine("[IsResponseCorrect] Evaluator logic placeholder - defaulting correctness.");
                // determinedCorrectness = ParseEvaluatorResponse(evaluatorResponse); // Placeholder
            }
            */

            // --- Option 3: Placeholder for Human Feedback Integration --- If no automated method
            // applies, correctness might rely on user input.
            /*
            if (RequiresHumanFeedback(taskType))
            {
                // 1. Present the response to the user with Correct/Incorrect buttons.
                // 2. Store the user's feedback. determinedCorrectness =
                // GetStoredHumanFeedback(interactionId); // Need interaction ID
                Debug.WriteLine("[IsResponseCorrect] Human feedback logic placeholder - defaulting correctness.");
            }
            */

            // --- Default --- If no specific rule, evaluation, or feedback applies, use the
            // default. For now, the default is 'true' to allow scoring mechanics to function.
            // Change this default to 'false' if you prefer a stricter approach where correctness
            // must be explicitly proven.
            Debug.WriteLine($"[IsResponseCorrect] No specific rule/logic applied for TaskType '{taskType}'. Using default: {determinedCorrectness}");

            return determinedCorrectness;
        }

        // Helper stub - determines if human feedback is the primary method
        //private bool RequiresHumanFeedback(string taskType)
        //{
        //    // Example: Subjective tasks might require human feedback
        //    return taskType.Equals("Creative Writing", StringComparison.OrdinalIgnoreCase); // Example
        //}

        // Helper stub - determines if Evaluator agent should be used
        //private bool ShouldUseEvaluatorForTask(string taskType)
        //{
        //    // Example: Use Evaluator for complex tasks like Analysis, Design, Strategy
        //    return taskType.Equals("Analysis", StringComparison.OrdinalIgnoreCase) ||
        //           taskType.Equals("Design", StringComparison.OrdinalIgnoreCase) ||
        //           taskType.Equals("Structuring/Planning", StringComparison.OrdinalIgnoreCase);
        //}

        // Method to determine the type of task - generalized version
        private string DetermineTaskType(string requestData)
        {
            requestData = requestData.ToLowerInvariant(); // Normalize case

            // Prioritize verbs indicating cognitive function
            if (requestData.Contains("analyze") || requestData.Contains("evaluate") || requestData.Contains("assess") || requestData.Contains("critique")) return "Analysis";
            if (requestData.Contains("create") || requestData.Contains("generate") || requestData.Contains("write") || requestData.Contains("implement") || requestData.Contains("develop")) return "Generation";
            if (requestData.Contains("plan") || requestData.Contains("outline") || requestData.Contains("structure") || requestData.Contains("design") || requestData.Contains("architect")) return "Structuring/Planning"; // Combine design/planning for generality
            if (requestData.Contains("test") || requestData.Contains("verify") || requestData.Contains("validate") || requestData.Contains("check") || requestData.Contains("ensure compliance")) return "Verification";
            if (requestData.Contains("improve") || requestData.Contains("optimize") || requestData.Contains("refactor") || requestData.Contains("refine")) return "Refinement";
            if (requestData.Contains("summarize") || requestData.Contains("extract key points")) return "Summarization";
            if (requestData.Contains("decide") || requestData.Contains("choose") || requestData.Contains("select")) return "DecisionMaking";
            if (requestData.Contains("coordinate") || requestData.Contains("manage") || requestData.Contains("orchestrate")) return "Coordination"; // Useful for Chief

            // Fallback based on keywords
            if (requestData.Contains("problem") || requestData.Contains("issue") || requestData.Contains("challenge")) return "ProblemSolving";
            if (requestData.Contains("idea") || requestData.Contains("concept") || requestData.Contains("approach")) return "Conceptualization";

            // Default fallback
            return "GeneralTask";
        }

        private async void AgentResponseEvent(object? sender, AgentResponseEventArgs args)
        {
            // Capture the processing state at the beginning of the event handling.
            var stateAtStart = _currentProcessingState;
            Debug.WriteLine($"---> AgentResponseEvent START for {args.AgentName}. State upon entry: {stateAtStart}. Output: {TruncateResponse(args.ResponseData, 200)}");
            // Log every response received, regardless of agent.
            LogProcessingRecord(args.AgentName, args.ResponseData);

            // If the system is already in a terminal (Idle, Complete, Error) state, ignore further
            // responses. This prevents processing responses that arrive after a task has concluded
            // or failed.
            if (stateAtStart == ProcessingState.Idle ||
                stateAtStart == ProcessingState.ProcessingComplete ||
                stateAtStart == ProcessingState.Error)
            {
                Debug.WriteLine($"[Controller] Ignoring response from {args.AgentName} due to inactive/terminal state: {stateAtStart}");
                return;
            }

            try
            {
                // --- CHIEF AGENT RESPONSE PROCESSING ---
                if (args.AgentName.Equals("Chief", StringComparison.OrdinalIgnoreCase))
                {
                    // Store the latest full directive from the Chief.
                    _lastChiefDirective = args.ResponseData;
                    Debug.WriteLine($"[Controller] Handling Chief response (State: {stateAtStart}). Output length: {args.ResponseData?.Length ?? 0}");

                    // --- Repetitive Error Detection for Chief --- Check if the Chief's response
                    // indicates an error or inability to proceed.
                    bool isChiefErrorResponse = args.ResponseData.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                                                args.ResponseData.Contains("unable to proceed", StringComparison.OrdinalIgnoreCase) ||
                                                args.ResponseData.Contains("clarification needed", StringComparison.OrdinalIgnoreCase);

                    if (isChiefErrorResponse && args.ResponseData == _lastChiefErrorResponse)
                    {
                        // If Chief repeats the *same* error message, increment clarification attempts.
                        _chiefClarificationAttempts++;
                        Debug.WriteLine($"[Controller Warning] Chief repeated the same problematic response. Attempt {_chiefClarificationAttempts}/{MAX_CHIEF_CLARIFICATION_ATTEMPTS}.");
                        // If max attempts reached, halt the process.
                        if (_chiefClarificationAttempts >= MAX_CHIEF_CLARIFICATION_ATTEMPTS)
                        {
                            HandleProcessingError($"Chief failed to provide a valid directive after {MAX_CHIEF_CLARIFICATION_ATTEMPTS} clarification attempts. Last response: '{TruncateResponse(args.ResponseData, 150)}'. Halting.", true);
                            _chiefClarificationAttempts = 0; // Reset for any future independent tasks
                            _lastChiefErrorResponse = string.Empty;
                            return; // Exit event handling
                        }
                        // If not yet max attempts, will fall through to general clarification
                        // request later.
                    }
                    else if (isChiefErrorResponse)
                    {
                        // If it's a *new* error-like response, store it and reset attempt count for
                        // *this specific error*.
                        _lastChiefErrorResponse = args.ResponseData;
                        _chiefClarificationAttempts = 1; // First attempt for this new problematic response.
                        Debug.WriteLine($"[Controller Info] Chief response appears problematic. Storing for repetitive check. Attempt 1 for this specific response.");
                    }
                    else // Response is not an immediate error or is a new non-error response.
                    {
                        _lastChiefErrorResponse = string.Empty; // Clear tracker for last error.
                                                                // _chiefClarificationAttempts is
                                                                // reset when a *valid directive* is
                                                                // successfully processed below.
                    }

                    // Trim trailing whitespace from the response for cleaner tag checking.
                    string responseTrimmed = args.ResponseData.TrimEnd();

                    // --- Prioritized Parsing of Chief's Concluding Directive Tags ---

                    // 1. Check for [ACTION_HALT]
                    string haltStartTag = "[ACTION_HALT]";
                    string haltEndTag = "[/ACTION_HALT]";
                    if (responseTrimmed.EndsWith(haltEndTag, StringComparison.OrdinalIgnoreCase))
                    {
                        int tagStartIndex = responseTrimmed.LastIndexOf(haltStartTag, StringComparison.OrdinalIgnoreCase);
                        if (tagStartIndex != -1)
                        {
                            int reasonStartIndex = tagStartIndex + haltStartTag.Length;
                            int reasonEndIndex = responseTrimmed.LastIndexOf(haltEndTag, StringComparison.OrdinalIgnoreCase);
                            if (reasonEndIndex > reasonStartIndex)
                            {
                                string reason = responseTrimmed.Substring(reasonStartIndex, reasonEndIndex - reasonStartIndex).Trim();
                                Debug.WriteLine($"[Controller] Chief requested HALT via tag. Reason: {reason}");
                                HandleProcessingError($"Workflow halted by Chief: {reason}", showMessageBox: true);
                                _chiefClarificationAttempts = 0; _lastChiefErrorResponse = string.Empty; // Reset on valid action
                                return; // Exit event handling
                            }
                            else // Malformed HALT tag
                            {
                                Debug.WriteLine("[Controller Warning] Found HALT tags but couldn't extract reason. Halting.");
                                HandleProcessingError("Workflow halted by Chief (reason parsing failed).", showMessageBox: true);
                                _chiefClarificationAttempts = 0; _lastChiefErrorResponse = string.Empty; // Reset on valid action
                                return; // Exit event handling
                            }
                        }
                    }

                    // 2. Check for [ACTION_ASK_USER]
                    string askUserStartTag = "[ACTION_ASK_USER]";
                    string askUserEndTag = "[/ACTION_ASK_USER]";
                    if (responseTrimmed.EndsWith(askUserEndTag, StringComparison.OrdinalIgnoreCase))
                    {
                        int tagStartIndex = responseTrimmed.LastIndexOf(askUserStartTag, StringComparison.OrdinalIgnoreCase);
                        if (tagStartIndex != -1)
                        {
                            int questionStartIndex = tagStartIndex + askUserStartTag.Length;
                            int questionEndIndex = responseTrimmed.LastIndexOf(askUserEndTag, StringComparison.OrdinalIgnoreCase);
                            if (questionEndIndex > questionStartIndex)
                            {
                                string question = responseTrimmed.Substring(questionStartIndex, questionEndIndex - questionStartIndex).Trim();
                                Debug.WriteLine("[Controller] Chief requested user input via tag.");
                                _currentProcessingState = ProcessingState.AwaitingUserInput;
                                Debug.WriteLine($"[Controller] State changed to: {_currentProcessingState}");
                                SetUIBusy(false, "Waiting for user input...");
                                _chiefClarificationAttempts = 0; _lastChiefErrorResponse = string.Empty; // Reset on valid action
                                OnUserInputRequired(question); // Raise event for UI to show dialog
                                Debug.WriteLine("[Controller] Exiting AgentResponseEvent to await user input.");
                                return; // Exit event handling
                            }
                            else // Malformed ASK_USER tag
                            {
                                Debug.WriteLine("[Controller Warning] Found ASK_USER tags but couldn't extract question. Requesting clarification.");
                                // Fall through to general clarification logic at the end of Chief's
                                // response processing.
                            }
                        }
                    }

                    // 3. Check for [FINAL_...] tag (e.g., [FINAL_PLAN], [FINAL_CODE])
                    var finalTagPattern = @"\[(FINAL_[A-Z0-9_]+)\]([\s\S]*?)\[/\1\]\s*\z"; // \z ensures it's at the absolute end
                    var finalTagMatch = Regex.Match(responseTrimmed, finalTagPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    // Also check for common "Final..." labels at the start of the text if tag is
                    // missing (less robust)
                    bool startsWithFinalLabel = Regex.IsMatch(args.ResponseData.TrimStart(), @"^(Final\s+Plan:|Completed\s+Analysis:|Generated\s+Code:)", RegexOptions.IgnoreCase);

                    if (finalTagMatch.Success || startsWithFinalLabel)
                    {
                        string finalContent = args.ResponseData; // Use the full response for the final output
                        string finalType = finalTagMatch.Success ? finalTagMatch.Groups[1].Value : "Detected Label";
                        Debug.WriteLine($"[Controller] Chief signaled task completion via {finalType}.");
                        _currentProcessingState = ProcessingState.ProcessingComplete;
                        Debug.WriteLine($"[Controller] State changed to: {_currentProcessingState}");
                        SetUIBusy(false, "Cognitive task completed.");
                        Debug.WriteLine($"Final Output ({finalType}):\n{TruncateResponse(finalContent, 1000)}");
                        _chiefClarificationAttempts = 0; _lastChiefErrorResponse = string.Empty; // Reset on valid action
                        return; // Exit event handling, task is complete
                    }

                    // 4. Check for [REQUEST_AGENT_CREATION]
                    string creationStartTag = "[REQUEST_AGENT_CREATION]";
                    string creationEndTag = "[/REQUEST_AGENT_CREATION]";
                    if (responseTrimmed.EndsWith(creationEndTag, StringComparison.OrdinalIgnoreCase))
                    {
                        int tagStartIndex = responseTrimmed.LastIndexOf(creationStartTag, StringComparison.OrdinalIgnoreCase);
                        if (tagStartIndex != -1)
                        {
                            Debug.WriteLine("[Controller] Chief requested agent creation via tag.");
                            int contentStartIndex = tagStartIndex + creationStartTag.Length;
                            int contentEndIndex = responseTrimmed.LastIndexOf(creationEndTag, StringComparison.OrdinalIgnoreCase);
                            if (contentEndIndex > contentStartIndex)
                            {
                                string blockContent = responseTrimmed.Substring(contentStartIndex, contentEndIndex - contentStartIndex).Trim();
                                var parsedData = ParseAgentCreationBlock(blockContent);

                                // This is a valid directive path initiation, so reset clarification
                                // attempts. ProcessAgentCreationRequest will handle the subsequent
                                // interaction with Chief.
                                _chiefClarificationAttempts = 0;
                                _lastChiefErrorResponse = string.Empty;

                                if (parsedData.HasValue)
                                {
                                    await ProcessAgentCreationRequest(
                                        parsedData.Value.agentName!,
                                        parsedData.Value.agentPurpose!,
                                        parsedData.Value.capabilities,
                                        parsedData.Value.promptText!
                                    );
                                }
                                else // Parsing of the block content failed
                                {
                                    Debug.WriteLine("[Controller Warning] Failed to parse content of [REQUEST_AGENT_CREATION] tag. Requesting clarification.");
                                    _chiefClarificationAttempts++; // Increment because we are directly requesting clarification for this failure.
                                    await RequestChiefClarificationAsync("Your request to create an agent was received, but the format inside the tag block was invalid. Please provide NAME, PURPOSE, CAPABILITIES, and PROMPT within [PROMPT]...[/PROMPT] tags inside the main block.");
                                }
                                return; // Exit event handling
                            }
                            else // Malformed REQUEST_AGENT_CREATION tag block
                            {
                                Debug.WriteLine("[Controller Warning] Found AGENT_CREATION tags but couldn't extract content. Requesting clarification.");
                                // Fall through to general clarification logic.
                            }
                        }
                    }

                    // 5. Check for [ACTIVATION_DIRECTIVES] (Individual Specialists)
                    var individualActivations = ParseActivationDirectives(args.ResponseData);

                    if (individualActivations.Any())
                    {
                        // Group activations by execution phase and sort by phase number
                        _phasedActivations = individualActivations
                            .GroupBy(a => a.ExecutionPhase)
                            .OrderBy(g => g.Key)
                            .Select(g => g.ToList())
                            .ToList();

                        _currentExecutionPhase = 0;
                        _completedPhaseOutputs.Clear();
                        _pendingSpecialistOutputs.Clear();

                        // Set expected modules for the first phase only
                        var firstPhase = _phasedActivations[0];
                        _expectedSpecialistModules = new HashSet<string>(firstPhase.Select(a => a.ModuleName), StringComparer.OrdinalIgnoreCase);

                        _currentProcessingState = ProcessingState.AwaitingSpecialistInput;

                        string contextSummary = await GetSummarizedProcessingContextAsync();

                        int totalPhases = _phasedActivations.Count;
                        int phaseNumber = firstPhase.FirstOrDefault()?.ExecutionPhase ?? 1;
                        string phaseInfo = totalPhases > 1 ? $" (Phase {phaseNumber}/{totalPhases})" : "";
                        SetUIBusy(true, $"Activating modules{phaseInfo}: {string.Join(", ", _expectedSpecialistModules)}...");

                        Debug.WriteLine($"[Controller] Starting phased execution. Total phases: {totalPhases}");

                        // Execute first phase (no prior outputs yet)
                        int validModulesRequestedCount = await ExecutePhaseAsync(firstPhase, contextSummary, _completedPhaseOutputs);

                        _chiefClarificationAttempts = 0;
                        _lastChiefErrorResponse = string.Empty; // Reset on valid activation directive

                        if (validModulesRequestedCount == 0) // If all specified modules were invalid or none specified in a valid block
                        {
                            string missingModules = _expectedSpecialistModules.Any() ? string.Join(", ", _expectedSpecialistModules) : "none specified or all were invalid";
                            Debug.WriteLine($"[Controller Warning] Chief requested activations via tag, but no valid/existing modules were found for: {missingModules}. Requesting clarification.");

                            _expectedSpecialistModules.Clear(); // No valid modules to wait for
                            _phasedActivations.Clear();
                            _chiefClarificationAttempts++; // Increment for this clarification cycle

                            await RequestChiefClarificationAsync($"You requested module activations using `[ACTIVATE]` tags, but no valid modules were found for: '{missingModules}'. Please revise your plan or specify available modules.");
                        }
                        else // At least one valid module was requested
                        {
                            Debug.WriteLine($"[Controller] Phase {phaseNumber}: {validModulesRequestedCount} valid specialist module(s) activated. Waiting for responses.");
                            // The system will now wait for specialist responses.
                        }
                        return; // Exit event handling for this branch
                    }

                    // --- Fall-through: No Valid Concluding Tag Processed --- This means Chief's
                    // response did not end with any of the expected directive tag blocks.
                    Debug.WriteLine("[Controller Warning] Chief output received, but no valid concluding directive tag was fully processed. Requesting clarification.");

                    // Increment attempt count if it's not already incremented for a repetitive
                    // error, or if it's a new type of non-directive response.

                    if (!isChiefErrorResponse) _chiefClarificationAttempts++;

                    // If it *was* an error, _chiefClarificationAttempts was already handled at the
                    // start of Chief's logic.

                    if (_chiefClarificationAttempts >= MAX_CHIEF_CLARIFICATION_ATTEMPTS)
                    {
                        HandleProcessingError($"Chief failed to provide a valid directive after {MAX_CHIEF_CLARIFICATION_ATTEMPTS} clarification attempts. Last response: '{TruncateResponse(args.ResponseData, 150)}'. Halting.", true);
                        _chiefClarificationAttempts = 0; // Reset for any future independent tasks
                        _lastChiefErrorResponse = string.Empty;
                        return; // Exit
                    }

                    await RequestChiefClarificationAsync(args.ResponseData); // Pass the problematic output for context
                                                                             // _lastChiefErrorResponse
                                                                             // was set at the start
                                                                             // if current response
                                                                             // is error-like, or
                                                                             // cleared if not.
                }
                // --- SPECIALIST AGENT RESPONSE PROCESSING ---
                else // Response is from a Specialist module (not Chief)
                {
                    Debug.WriteLine($"[Controller] Handling Specialist response from {args.AgentName} (State: {stateAtStart}).");
                    if (stateAtStart == ProcessingState.AwaitingSpecialistInput)
                    {
                        if (_expectedSpecialistModules.Contains(args.AgentName))
                        {
                            // Add response to pending dictionary.
                            if (_pendingSpecialistOutputs.TryAdd(args.AgentName, args.ResponseData))
                            { Debug.WriteLine($"[Controller] Received expected output from {args.AgentName}."); }
                            else // Should not happen if _pendingSpecialistOutputs is cleared correctly.
                            { Debug.WriteLine($"[Controller Warning] Received duplicate output from {args.AgentName}. Overwriting previous."); _pendingSpecialistOutputs[args.AgentName] = args.ResponseData; }

                            // Check if all expected responses for current phase are in.
                            var receivedKeys = new HashSet<string>(_pendingSpecialistOutputs.Keys, StringComparer.OrdinalIgnoreCase);
                            if (receivedKeys.IsSupersetOf(_expectedSpecialistModules))
                            {
                                Debug.WriteLine($"[Controller] All expected specialist outputs for phase {_currentExecutionPhase + 1} received.");

                                // Check if task is already complete (Chief may have issued FINAL directive)
                                if (_currentProcessingState == ProcessingState.ProcessingComplete)
                                {
                                    Debug.WriteLine("[Controller] Task already complete. Ignoring late phase completion.");
                                    return;
                                }

                                // Store outputs from this phase for use by later phases
                                foreach (var kvp in _pendingSpecialistOutputs)
                                {
                                    _completedPhaseOutputs[kvp.Key] = kvp.Value;
                                }

                                // Check if there are more phases to execute
                                _currentExecutionPhase++;
                                if (_currentExecutionPhase < _phasedActivations.Count)
                                {
                                    // Start next phase
                                    var nextPhase = _phasedActivations[_currentExecutionPhase];
                                    _pendingSpecialistOutputs.Clear();
                                    _expectedSpecialistModules = new HashSet<string>(nextPhase.Select(a => a.ModuleName), StringComparer.OrdinalIgnoreCase);

                                    int totalPhases = _phasedActivations.Count;
                                    int phaseNumber = nextPhase.FirstOrDefault()?.ExecutionPhase ?? (_currentExecutionPhase + 1);
                                    Debug.WriteLine($"[Controller] Starting phase {phaseNumber} ({_currentExecutionPhase + 1}/{totalPhases})");
                                    SetUIBusy(true, $"Activating modules (Phase {phaseNumber}/{totalPhases}): {string.Join(", ", _expectedSpecialistModules)}...");

                                    string contextSummary = await GetSummarizedProcessingContextAsync();
                                    int validModulesCount = await ExecutePhaseAsync(nextPhase, contextSummary, _completedPhaseOutputs);

                                    if (validModulesCount == 0)
                                    {
                                        Debug.WriteLine($"[Controller Warning] No valid modules in phase {phaseNumber}. Proceeding to next phase or synthesis.");
                                        // Recursively check for more phases or go to synthesis
                                        // This will be handled by the next iteration when no responses are expected
                                    }
                                    return; // Exit event handling - wait for next phase responses
                                }
                                else
                                {
                                    // All phases complete - proceed to Chief synthesis
                                    Debug.WriteLine("[Controller] All phases complete. Requesting Chief synthesis.");
                                    _chiefClarificationAttempts = 0; // Reset before new Chief interaction
                                    _lastChiefErrorResponse = string.Empty;
                                    _phasedActivations.Clear();
                                    await RequestChiefSynthesisAsync(); // This will set state to AwaitingChiefSynthesis
                                    return; // Exit event handling
                                }
                            }
                            else // Still waiting for other specialists in current phase.
                            {
                                int remainingCount = _expectedSpecialistModules.Count - receivedKeys.Count;
                                var remainingModules = _expectedSpecialistModules.Except(receivedKeys, StringComparer.OrdinalIgnoreCase);
                                int totalPhases = _phasedActivations.Count;
                                string phaseInfo = totalPhases > 1 ? $" in phase {_currentExecutionPhase + 1}/{totalPhases}" : "";
                                Debug.WriteLine($"[Controller] Still waiting for {remainingCount} module(s){phaseInfo}: {string.Join(", ", remainingModules)}");
                                SetUIBusy(true, $"Waiting for {remainingCount} more module(s){phaseInfo}...");
                            }
                        }
                        else // Response from an unexpected specialist.
                        { Debug.WriteLine($"[Controller Warning] Received unexpected output from {args.AgentName} while in AwaitingSpecialistInput state (Expected: {string.Join(",", _expectedSpecialistModules)}). Ignoring."); }
                    }
                    else // Specialist response received but controller not in AwaitingSpecialistInput state.
                    { Debug.WriteLine($"[Controller Warning] Received specialist output from {args.AgentName} but not in AwaitingSpecialistInput state ({stateAtStart}). Ignoring."); }
                }
            }
            catch (Exception ex)
            {
                // Catch-all for any unhandled exceptions during response processing.
                HandleProcessingError($"Critical error processing module output from {args.AgentName}: {ex.Message}\n{ex.StackTrace}", true);
            }
            finally
            {
                // Reset UI busy state if the process has reached a terminal state or is waiting for
                // user input. Other states (AwaitingSpecialistInput, AwaitingChiefSynthesis) will
                // keep UI busy via their SetUIBusy calls.
                if (_currentProcessingState == ProcessingState.Error ||
                    _currentProcessingState == ProcessingState.ProcessingComplete ||
                    _currentProcessingState == ProcessingState.AwaitingUserInput)
                {
                    SetUIBusy(false);
                }
            }
            Debug.WriteLine($"---> AgentResponseEvent END for {args.AgentName}. Current state: {_currentProcessingState}");
        }

        protected virtual void OnUserInputRequired(string prompt) // Keep invoker
        {
            // Ensure UI updates happen on the UI thread
            InvokeIfNeeded(() =>
            {
                UserInputRequired?.Invoke(this, new UserInputRequestEventArgs(prompt));
            });
        }

        private async Task RequestChiefSynthesisAsync(string? additionalInfo = null)
        {
            // Create a copy to avoid modification issues if the collection changes while processing
            var outputsCopy = new Dictionary<string, string>(_pendingSpecialistOutputs, StringComparer.OrdinalIgnoreCase);
            // Call the primary implementation with the copied outputs
            await RequestChiefSynthesisAsync(outputsCopy, additionalInfo);
        }

        private async Task ProcessAgentCreationRequest(string agentName, string agentPurpose, List<string> capabilities, string promptText)
        {
            // Double-check parameters - belt and suspenders
            if (string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(agentPurpose) || string.IsNullOrWhiteSpace(promptText) || capabilities == null)
            {
                Debug.WriteLine("[Controller Error] ProcessAgentCreationRequest called with invalid parameters. Halting creation.");
                await RequestChiefClarificationAsync($"Internal Error: Invalid parameters received for agent creation request for '{agentName}'. Cannot proceed.");
                return;
            }

            Debug.WriteLine($"[Controller] Processing agent creation request for '{agentName}'. Requesting user confirmation...");

            // --- Confirmation Dialog ---
            string capabilitiesList = capabilities.Any() ? string.Join(", ", capabilities) : "None Specified";
            string confirmationMessage = $"Chief has requested the creation of a new agent:\n\n" +
                                         $"Name: '{agentName}'\n" +
                                         $"Purpose: {agentPurpose}\n" +
                                         $"Capabilities: {capabilitiesList}\n\n" +
                                         $"Prompt Preview:\n{TruncateResponse(promptText, 200)}\n\n" +
                                         $"Do you approve this agent creation?";

            DialogResult userConfirmation = DialogResult.None;
            // Ensure dialog runs on UI thread
            InvokeIfNeeded(() =>
            {
                // Make sure 'this' refers to the Form instance
                userConfirmation = MessageBox.Show(this, confirmationMessage, "Confirm New Agent Creation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            });

            string outcomeMessage; // MessageAnthropic to send back to Chief
            bool creationAttempted = false; // Flag if we tried DB operations

            if (userConfirmation == DialogResult.Yes)
            {
                Debug.WriteLine($"User confirmed creation for agent '{agentName}'. Proceeding with DB and AIManager operations...");
                SetUIBusy(true, $"Creating agent '{agentName}'..."); // Indicate busy during creation
                creationAttempted = true;
                int newAgentId = 0; // Initialize agent ID

                try
                {
                    // --- Step 1: Create Agent Core Record in Database --- This adds the agent to
                    // the Agents table and creates the first prompt version
                    newAgentId = await _agentDb.AddAgentAsync(
                        name: agentName,
                        purpose: agentPurpose,
                        initialPrompt: promptText, // The prompt generated by Chief
                        comments: "Dynamically created via Chief request.",
                        createdBy: "Chief/System"
                    );

                    if (newAgentId <= 0)
                    {
                        // Throw exception if DB call indicates failure
                        throw new Exception($"AgentDatabase.AddAgentAsync failed or returned invalid ID ({newAgentId}).");
                    }
                    Debug.WriteLine($"Agent '{agentName}' core record created in DB with ID: {newAgentId}.");

                    // --- Step 2: Add Capabilities to Database ---
                    int capabilitiesAdded = 0;
                    if (capabilities.Any())
                    {
                        Debug.WriteLine($"Attempting to add {capabilities.Count} capabilities for Agent ID {newAgentId}...");
                        foreach (string capabilityName in capabilities)
                        {
                            if (string.IsNullOrWhiteSpace(capabilityName))
                            {
                                Debug.WriteLine("[Tool Warning] Skipping empty capability string.");
                                continue; // Skip empty strings
                            }
                            try
                            {
                                // Call the DB method to add the capability Assuming
                                // AddAgentCapabilityAsync handles potential duplicates gracefully
                                // (e.g., INSERT OR IGNORE)
                                await _agentDb.AddAgentCapabilityAsync(newAgentId, capabilityName.Trim(), $"Capability for {agentName}");
                                capabilitiesAdded++;
                            }
                            catch (Exception capEx)
                            {
                                // Log error adding specific capability but continue with others
                                Debug.WriteLine($"[DB Warning] Failed to add capability '{capabilityName}' for agent {agentName} (ID: {newAgentId}): {capEx.Message}");
                                // Depending on requirements, you might want to collect these errors
                            }
                        }
                        Debug.WriteLine($"Successfully added {capabilitiesAdded}/{capabilities.Count} capabilities to DB for agent {agentName}.");
                    }
                    else
                    {
                        Debug.WriteLine($"No capabilities were specified for agent {agentName}.");
                    }

                    // --- Step 3: Create Runtime Agent Instance --- Remove existing runtime
                    // instance first, if any, to ensure fresh state
                    if (_aiManager.AgentExists(agentName))
                    {
                        Debug.WriteLine($"[AIManager Info] Runtime agent '{agentName}' already existed. Removing old instance.");
                        _aiManager.RemoveAgent(agentName);
                    }
                    // Create the new runtime instance with the specified prompt
                    _aiManager.CreateAgent(agentName, promptText);
                    Debug.WriteLine($"Runtime agent '{agentName}' created/recreated in AIManager.");

                    // --- Success Outcome ---
                    outcomeMessage = $"Agent '{agentName}' (ID: {newAgentId}) created successfully with {capabilitiesAdded} capabilities and is now available.";
                    LogProcessingRecord($"System:AgentCreation", outcomeMessage); // Log success
                                                                                  // SetUIBusy(false,
                                                                                  // $"Agent
                                                                                  // '{agentName}'
                                                                                  // created."); //
                                                                                  // Update UI
                                                                                  // status (will be
                                                                                  // overwritten by
                                                                                  // clarification request)
                }
                catch (Exception ex)
                {
                    // --- Failure Outcome ---
                    outcomeMessage = $"Failed to create agent '{agentName}'. Error: {ex.Message}";
                    Debug.WriteLine($"[Controller Error] {outcomeMessage}\n{ex.StackTrace}");
                    LogProcessingRecord($"System:AgentCreation", $"Error: {outcomeMessage}"); // Log error
                                                                                              // Show
                                                                                              // error
                                                                                              // to user
                    HandleProcessingError(outcomeMessage, showMessageBox: true);
                    // No need to call RequestChiefClarificationAsync here, HandleProcessingError
                    // sets state to Error
                    return; // Exit after handling error
                }
            }
            else // User clicked No
            {
                // --- Cancellation Outcome ---
                outcomeMessage = $"Agent creation cancelled by user for '{agentName}'.";
                Debug.WriteLine(outcomeMessage);
                LogProcessingRecord($"System:AgentCreation", outcomeMessage);
                // SetUIBusy(false, "Agent creation cancelled."); // Update UI status (will be overwritten)
            }

            // --- Step 4: Inform Chief about the Outcome (Success or Cancellation) --- Always loop
            // back to the Chief so it knows what happened and can plan the *next* cognitive step.
            // Even if creation failed inside the try block, HandleProcessingError should have been
            // called. Only request clarification if the process wasn't halted by an error.
            if (_currentProcessingState != ProcessingState.Error)
            {
                Debug.WriteLine($"[Controller] Informing Chief about agent creation outcome: {outcomeMessage}");
                await RequestChiefClarificationAsync($"Outcome of the request to create agent '{agentName}': {outcomeMessage}. Please determine the next cognitive step.");
            }
            else
            {
                Debug.WriteLine($"[Controller] Skipping clarification request to Chief because state is Error.");
            }
        }

        private string BuildPerspectivePrompt(string agentName, string chiefsFocusDirective, string currentGoal, string summarizedContext)
        {
            var prompt = new StringBuilder();

            // 1. Core Goal Info (Use passed parameter)
            prompt.AppendLine($"## Overall Goal: {TruncateResponse(currentGoal, 200)}");
            prompt.AppendLine("---");

            // 2. Summarized Context
            prompt.AppendLine("## Processing Context Summary");
            prompt.AppendLine(summarizedContext);
            prompt.AppendLine("---");

            // 3. Specific Task for this Agent based on Chief's Directive
            prompt.AppendLine($"## Your Task as {agentName.ToUpper()}");
            prompt.AppendLine("The Executive Function (Chief) requires your cognitive input based on the following focus and the context summary provided above.");
            prompt.AppendLine($"\n## Chief's Focus/Directive for You:");
            prompt.AppendLine(chiefsFocusDirective);
            prompt.AppendLine("\nExecute your primary cognitive function based *only* on this directive and the context summary.");
            prompt.AppendLine("Provide clear reasoning, analysis, plans, generated text, or code snippets as appropriate for your role.");
            prompt.AppendLine("If you absolutely require user input *to complete this specific directive*, state 'ACTION: Ask User' on a new line at the end, followed by your specific question.");
            prompt.AppendLine("\nYour Response:");

            return prompt.ToString();
        }

        /// <summary>
        /// Executes a single phase of specialist activations.
        /// All agents in the same phase run in parallel.
        /// </summary>
        /// <param name="phaseActivations">List of activation infos for this phase</param>
        /// <param name="contextSummary">Current context summary</param>
        /// <param name="priorPhaseOutputs">Outputs from earlier phases to include in context</param>
        /// <returns>Number of valid modules that were activated</returns>
        private async Task<int> ExecutePhaseAsync(List<ActivationInfo> phaseActivations, string contextSummary, Dictionary<string, string> priorPhaseOutputs)
        {
            int validModulesRequestedCount = 0;
            var historySnippet = new List<ProcessingRecord>();

            foreach (var activationInfo in phaseActivations)
            {
                if (activationInfo.SessionHistoryCount > 0)
                {
                    historySnippet = _processingHistory.TakeLast(activationInfo.SessionHistoryCount).ToList();
                }

                var activationInfoToString = new StringBuilder()
                    .AppendLine("[Activation Information]")
                    .AppendLine($"Module Name: {activationInfo.ModuleName}")
                    .AppendLine($"Focus: {activationInfo.Focus}")
                    .AppendLine($"Execution Phase: {activationInfo.ExecutionPhase}")
                    .AppendLine($"History Mode: {activationInfo.HistoryMode}");

                if (activationInfo.DependsOn.Any())
                {
                    activationInfoToString.AppendLine($"Depends On: {string.Join(", ", activationInfo.DependsOn)}");
                }

                if (activationInfo.SessionHistoryCount > 0)
                {
                    activationInfoToString.AppendLine($"Session History Count: {activationInfo.SessionHistoryCount}");
                    activationInfoToString.AppendLine($"Session History:\n{string.Join("\n",
                        historySnippet.Select(r => $"{r.Timestamp:HH:mm:ss} [{r.SourceModule}]: {TruncateResponse(r.OutputContent, 50)}"))}\n");
                }
                else
                {
                    activationInfoToString.AppendLine("Session History Count: None (0)\n");
                }

                Debug.WriteLine(activationInfoToString.ToString());

                if (_aiManager.AgentExists(activationInfo.ModuleName))
                {
                    // Build prompt with prior phase outputs included if available
                    string specialistPrompt = BuildPerspectivePrompt(activationInfo.ModuleName, activationInfo.Focus, _currentGoal, contextSummary);

                    // Append outputs from dependencies or prior phases
                    if (priorPhaseOutputs.Any())
                    {
                        var relevantOutputs = new StringBuilder();

                        // If specific dependencies are listed, only include those
                        if (activationInfo.DependsOn.Any())
                        {
                            foreach (var dep in activationInfo.DependsOn)
                            {
                                if (priorPhaseOutputs.TryGetValue(dep, out var depOutput))
                                {
                                    relevantOutputs.AppendLine($"\n[Output from {dep}]:");
                                    relevantOutputs.AppendLine(depOutput);
                                }
                            }
                        }
                        else
                        {
                            // Include all prior phase outputs
                            foreach (var kvp in priorPhaseOutputs)
                            {
                                relevantOutputs.AppendLine($"\n[Output from {kvp.Key}]:");
                                relevantOutputs.AppendLine(kvp.Value);
                            }
                        }

                        if (relevantOutputs.Length > 0)
                        {
                            specialistPrompt += "\n\n[Prior Module Outputs for Reference]:" + relevantOutputs.ToString();
                        }
                    }

                    Debug.WriteLine($"[Controller] Sending request to {activationInfo.ModuleName} (Phase {activationInfo.ExecutionPhase}) with focus: {TruncateResponse(activationInfo.Focus, 100)}");

                    await _aiManager.RequestAsync(activationInfo.ModuleName, specialistPrompt);
                    validModulesRequestedCount++;
                }
                else
                {
                    Debug.WriteLine($"[Controller Warning] Chief requested activation of non-existent module: '{activationInfo.ModuleName}'. Skipping.");
                    _expectedSpecialistModules.Remove(activationInfo.ModuleName);
                }
            }

            return validModulesRequestedCount;
        }

        private List<ActivationInfo> ParseActivationDirectives(string chiefOutput)
        {
            var activations = new List<ActivationInfo>();
            if (string.IsNullOrWhiteSpace(chiefOutput)) return activations;

            // This regex captures the module name, focus, and any optional parameters inside the
            // [ACTIVATE] tag.
            var activationPattern = new Regex(
                @"\[ACTIVATE\]\s*(?<module_name>[^:\[\]]+?)\s*:\s*(?<focus>[^\[\]]*)(?<params>(?:\[[^=\]]+=[^\]]+\])*)\s*\[/ACTIVATE\]",
                RegexOptions.IgnoreCase);

            var matches = activationPattern.Matches(chiefOutput);
            Debug.WriteLine($"[Parser Activation] Found {matches.Count} [ACTIVATE] tags.");

            foreach (Match match in matches)
            {
                var info = new ActivationInfo
                {
                    ModuleName = match.Groups["module_name"].Value.Trim(),
                    Focus = match.Groups["focus"].Value.Trim()
                };

                // Parse optional key-value parameters like [HISTORY_MODE=SESSION_AWARE]
                var paramsText = match.Groups["params"].Value;
                var paramPattern = new Regex(@"\[(?<key>[^=]+)=(?<value>[^\]]+)\]");
                var paramMatches = paramPattern.Matches(paramsText);

                foreach (Match paramMatch in paramMatches)
                {
                    string key = paramMatch.Groups["key"].Value.Trim().ToUpperInvariant();
                    string value = paramMatch.Groups["value"].Value.Trim();

                    switch (key)
                    {
                        case "HISTORY_MODE":
                            if (Enum.TryParse<HistoryMode>(value, true, out var mode))
                            {
                                info.HistoryMode = mode;
                            }
                            break;

                        case "SESSION_HISTORY_COUNT":
                            if (int.TryParse(value, out int count))
                            {
                                info.SessionHistoryCount = Math.Clamp(count, 0, 25); // Clamp to valid range
                            }
                            break;

                        case "PHASE":
                            if (int.TryParse(value, out int phase))
                            {
                                info.ExecutionPhase = Math.Clamp(phase, 1, 10); // Clamp to valid range 1-10
                            }
                            break;

                        case "DEPENDS_ON":
                            // Parse comma-separated list of module names
                            var dependencies = value.Split(',')
                                .Select(d => d.Trim())
                                .Where(d => !string.IsNullOrEmpty(d))
                                .ToList();
                            info.DependsOn = dependencies;
                            break;
                    }
                }

                if (IsValidModule(info.ModuleName))
                {
                    activations.Add(info);
                    Debug.WriteLine($"[Parser Activation] Parsed: Module='{info.ModuleName}', Phase={info.ExecutionPhase}, DependsOn=[{string.Join(",", info.DependsOn)}], Mode='{info.HistoryMode}', HistoryCount='{info.SessionHistoryCount}', Focus='{TruncateResponse(info.Focus, 50)}'");
                }
                else
                {
                    Debug.WriteLine($"[Parser Activation] Warning: Invalid or unknown module name found in tag: '{info.ModuleName}'");
                }
            }
            return activations;
        }

        private (string? agentName, string? agentPurpose, List<string> capabilities, string? promptText)? ParseAgentCreationBlock(string blockContent)
        {
            if (string.IsNullOrWhiteSpace(blockContent))
            {
                Debug.WriteLine("[Parser AgentCreateTag - HeaderParse] Input blockContent is null or empty.");
                return null;
            }
            Debug.WriteLine($"[Parser AgentCreateTag - HeaderParse] Parsing block content length: {blockContent.Length}");

            // Helper function to extract content between simple tags
            //string? ExtractContent(string content, string startTag, string endTag, bool trimResult = true)
            //{
            //    int startIndex = content.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            //    if (startIndex == -1) return null;

            // startIndex += startTag.Length; int endIndex = content.IndexOf(endTag, startIndex,
            // StringComparison.OrdinalIgnoreCase); if (endIndex == -1) return null;

            //    string extracted = content.Substring(startIndex, endIndex - startIndex);
            //    return trimResult ? extracted.Trim() : extracted; // Allow not trimming for prompt body
            //}

            string? agentName = ExtractContent(blockContent, "[NAME]", "[/NAME]");
            string? agentPurpose = ExtractContent(blockContent, "[PURPOSE]", "[/PURPOSE]");
            string? capabilitiesString = ExtractContent(blockContent, "[CAPABILITIES]", "[/CAPABILITIES]");
            string? fullPromptBlockContent = ExtractContent(blockContent, "[PROMPT]", "[/PROMPT]", trimResult: false); // Don't trim prompt block yet

            List<string> capabilities = new List<string>();
            if (!string.IsNullOrWhiteSpace(capabilitiesString))
            {
                capabilities = capabilitiesString.Split(',')
                                             .Select(s => s.Trim())
                                             .Where(s => !string.IsNullOrEmpty(s))
                                             .ToList();
            }

            string? finalPromptText = null;

            if (fullPromptBlockContent != null)
            {
                // Now extract [HEADER] from within the fullPromptBlockContent
                string? headerContent = ExtractContent(fullPromptBlockContent, "[HEADER]", "[/HEADER]");
                string? promptBody = "";

                if (headerContent != null)
                {
                    // Find the end of the [HEADER]...[/HEADER] block to get the rest
                    int headerEndTagIndex = fullPromptBlockContent.IndexOf("[/HEADER]", StringComparison.OrdinalIgnoreCase);
                    if (headerEndTagIndex != -1)
                    {
                        promptBody = fullPromptBlockContent.Substring(headerEndTagIndex + "[/HEADER]".Length).TrimStart('\r', '\n', ' ');
                    }
                    // Ensure the header itself is what we want, e.g., "# AgentName" or "# AgentName
                    // [PROMPT]" For now, we'll take what the Chief gives in [HEADER]. If you want
                    // to enforce "# AgentName [PROMPT]" format: string desiredHeaderFormat = $"#
                    // {agentName} [PROMPT]"; if (!headerContent.Equals(desiredHeaderFormat,
                    // StringComparison.OrdinalIgnoreCase) && agentName != null) { headerContent =
                    // desiredHeaderFormat; // Or log a warning Debug.WriteLine($"[Parser
                    // AgentCreateTag - HeaderParse] Adjusted header to: {headerContent}"); }
                    finalPromptText = $"{headerContent.Trim()}\n{promptBody}"; // Combine header and body
                    Debug.WriteLine($"[Parser AgentCreateTag - HeaderParse] Extracted HEADER: '{headerContent.Trim()}' and prompt body.");
                }
                else
                {
                    Debug.WriteLine("[Parser AgentCreateTag - HeaderParse] [HEADER] tag not found within [PROMPT] block. Using entire [PROMPT] content as prompt body.");
                    finalPromptText = fullPromptBlockContent.Trim(); // Use the whole block if no header tag
                                                                     // Optionally, prepend a
                                                                     // default header if [HEADER]
                                                                     // is missing but agentName exists
                    if (agentName != null && !string.IsNullOrWhiteSpace(finalPromptText) && !finalPromptText.TrimStart().StartsWith($"# {agentName}", StringComparison.OrdinalIgnoreCase))
                    {
                        finalPromptText = $"# {agentName} [PROMPT]\n{finalPromptText.TrimStart()}"; // Your desired default
                        Debug.WriteLine($"[Parser AgentCreateTag - HeaderParse] Prepended default header for '{agentName}' as [HEADER] was missing.");
                    }
                }
            }
            else
            {
                Debug.WriteLine("[Parser AgentCreateTag - HeaderParse] [PROMPT]...[/PROMPT] block not found or empty.");
            }

            // --- Validation ---
            bool nameOK = !string.IsNullOrWhiteSpace(agentName);
            bool purposeOK = !string.IsNullOrWhiteSpace(agentPurpose);
            bool capsOK = capabilities.Any(); // Or capabilities != null; if empty list is okay
            bool promptOK = finalPromptText != null; // Prompt text (after header processing) should exist

            if (nameOK && purposeOK && capsOK && promptOK)
            {
                Debug.WriteLine($"[Parser AgentCreateTag - HeaderParse] OVERALL SUCCESS: Name='{agentName}', Purpose='{agentPurpose}', Caps='{string.Join(",", capabilities)}', PromptLen='{finalPromptText?.Length ?? 0}'");
                return (agentName, agentPurpose, capabilities, finalPromptText);
            }
            else
            {
                Debug.WriteLine($"[Parser AgentCreateTag - HeaderParse] OVERALL FAILURE: Could not parse all required fields. Status: NameOK={nameOK}, PurposeOK={purposeOK}, CapsOK={capsOK}, PromptOK={promptOK}");
                if (!nameOK) Debug.WriteLine(" -> NAME missing or invalid");
                if (!purposeOK) Debug.WriteLine(" -> PURPOSE missing or invalid");
                if (!capsOK) Debug.WriteLine(" -> CAPABILITIES missing, invalid, or empty list (if required)");
                if (!promptOK) Debug.WriteLine(" -> PROMPT block or its header missing/invalid");
                return null;
            }
        }

        public string? ExtractContent(string content, string startTag, string endTag, bool trimResult = true)
        {
            // It finds the FIRST occurrence of the start tag
            int startIndex = content.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (startIndex == -1) return null;

            startIndex += startTag.Length;
            // It finds the FIRST occurrence of the end tag *after* the start tag
            int endIndex = content.IndexOf(endTag, startIndex, StringComparison.OrdinalIgnoreCase);
            if (endIndex == -1) return null;

            // It extracts everything between them, regardless of what comes after.
            string extracted = content.Substring(startIndex, endIndex - startIndex);
            return trimResult ? extracted.Trim() : extracted;
        }








        private void AgentAddedEvent(object? sender, AgentAddedEventArgs args) =>
            Debug.WriteLine($"> Agent Added: {args.AgentName}");

        private void AgentRemovedEvent(object? sender, AgentRemovedEventArgs args) =>
            Debug.WriteLine($"> Agent Removed: {args.AgentName}");

        private void AgentErrorEvent(object? sender, AgentErrorEventArgs args) =>
            Debug.WriteLine($"> Agent Error: {args.AgentName} - {args.Exception.Message}");

        private void AgentCompletedEvent(object? sender, AgentCompletedEventArgs args) =>
            Debug.WriteLine($"> Agent Completed: {args.AgentName} (Success: {args.Success})");

        private void AgentStatusEvent(object? sender, AgentStatusEventArgs args) =>
            Debug.WriteLine($"> Agent Status: {args.AgentName} - {args.Status}");

        private void AgentRequestEvent(object? sender, AgentRequestEventArgs args) =>
            Debug.WriteLine($"> Agent Request: {args.AgentName} processing '{TruncateResponse(args.RequestData, 50)}'");







        private Dictionary<string, string> ParseModuleActivationRequests(string chiefOutput) // Renamed back for consistency
        {
            var activations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Debug.WriteLine($"[Parser SimpleTag] Starting parse. Input length: {chiefOutput?.Length ?? 0}");

            if (string.IsNullOrWhiteSpace(chiefOutput))
            {
                Debug.WriteLine("[Parser SimpleTag] Input chiefOutput is null or empty.");
                return activations;
            }

            // Regex to find [ACTIVATE]ModuleName:Focus Text[/ACTIVATE] Allows spaces around colon,
            // captures ModuleName and Focus Text Handles potential multi-line focus text using
            // Singleline option
            var activationPattern = new Regex(
                @"\[ACTIVATE\]\s*(?<module_name>[^:]+?)\s*:\s*(?<focus_text>.*?)\s*\[/ACTIVATE\]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline); // Use Singleline for multi-line focus

            var matches = activationPattern.Matches(chiefOutput);

            if (matches.Count > 0)
            {
                Debug.WriteLine($"[Parser SimpleTag] Found {matches.Count} [ACTIVATE]...[/ACTIVATE] tags.");

                foreach (Match match in matches)
                {
                    // Trim whitespace from captured groups
                    string moduleNameRaw = match.Groups["module_name"].Value.Trim();
                    string focusText = match.Groups["focus_text"].Value.Trim();

                    // --- Direct Validation (No Mapping Needed) ---
                    if (IsValidModule(moduleNameRaw)) // Use helper to check against known agent names
                    {
                        // Use TryAdd to handle potential duplicate tags gracefully
                        if (activations.TryAdd(moduleNameRaw, focusText))
                        {
                            // Debug.WriteLine($"[Parser SimpleTag] Added
                            // activation: Module='{moduleNameRaw}',
                            // Focus='{TruncateResponse(focusText, 100)}'");
                            Debug.WriteLine($"[Parser SimpleTag] Added activation: Module=' ** REDACTED **', Focus=' ** REDACTED **'");
                        }
                        else
                        {
                            // Debug.WriteLine($"[Parser SimpleTag] Duplicate activation tag found
                            // for '{moduleNameRaw}'. Ignoring.");
                            Debug.WriteLine($"[Parser SimpleTag] Duplicate activation tag found for ' ** REDACTED **'. Ignoring.");
                        }
                    }
                    else
                    {
                        // Log invalid module names found within tags
                        Debug.WriteLine($"[Parser SimpleTag] Warning: Invalid or unknown module name found in tag: '{moduleNameRaw}'");
                        // Optionally: Add to a list of errors/warnings to potentially feed back to Chief?
                    }
                }
            }
            else
            {
                Debug.WriteLine("[Parser SimpleTag] No [ACTIVATE]...[/ACTIVATE] tags found.");
            }

            Debug.WriteLine($"[Parser SimpleTag] Finished parsing. Found {activations.Count} valid activations: {string.Join(", ", activations.Keys)}");
            return activations;
        }

        /// <summary>
        /// Parses the Chief's output for an [ACTIVATE_TEAM] directive using a robust regex pattern,
        /// similar to the individual activation parser.
        /// </summary>
        /// <param name="chiefOutput">The full response text from the Chief agent.</param>
        /// <returns>A TeamActivationInfo object if the tag is found and valid, otherwise null.</returns>
        private TeamActivationInfo? ParseTeamActivationRequest(string chiefOutput)
        {
            Debug.WriteLine($"[Parser TeamActivation] Starting parse. Input length: {chiefOutput?.Length ?? 0}");
            if (string.IsNullOrWhiteSpace(chiefOutput))
            {
                return null;
            }

            // This regex is designed to find the [ACTIVATE_TEAM] block at the end of the string. It
            // captures the team name, the focus, and the entire block of optional parameters. The
            // focus capture is non-greedy `(.*?)` and stops at the first sign of an optional
            // parameter `[` or the closing tag, which is asserted by the lookahead `(?=\s*\[|$)`.
            var teamActivationPattern = new Regex(
                @"\[ACTIVATE_TEAM\]\s*(?<team_name>[^:\[\]]+?)\s*:\s*(?<team_focus>.*?)(?<params>(?:\s*\[[^=\]]+=[^\]]+\])*)\s*\[/ACTIVATE_TEAM\]\s*\z",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            Match match = teamActivationPattern.Match(chiefOutput.Trim());

            if (!match.Success)
            {
                Debug.WriteLine("[Parser TeamActivation] No valid [ACTIVATE_TEAM]...[/ACTIVATE_TEAM] tag found at the end of the response.");
                return null;
            }

            // --- Extract main components from the successful match ---
            string teamName = match.Groups["team_name"].Value.Trim();
            string teamFocus = match.Groups["team_focus"].Value.Trim().TrimEnd('.');
            string paramsText = match.Groups["params"].Value;

            if (string.IsNullOrWhiteSpace(teamName) || string.IsNullOrWhiteSpace(teamFocus))
            {
                Debug.WriteLine($"[Parser TeamActivation] TeamName or TeamFocus was empty after parsing. Name: '{teamName}', Focus: '{teamFocus}'");
                return null;
            }

            var teamInfo = new TeamActivationInfo
            {
                TeamName = teamName,
                TeamFocus = teamFocus
            };

            // --- Parse the optional parameters block ---
            if (!string.IsNullOrEmpty(paramsText))
            {
                // This regex is specifically for parsing the [Key=Value] pairs. It's robust to
                // whitespace around the key, value, and '='.
                var paramPattern = new Regex(@"\[\s*(?<key>[^=]+?)\s*=\s*(?<value>[^\]]+?)\s*\]");
                var paramMatches = paramPattern.Matches(paramsText);

                foreach (Match paramMatch in paramMatches)
                {
                    string key = paramMatch.Groups["key"].Value.Trim().ToUpperInvariant();
                    string value = paramMatch.Groups["value"].Value.Trim();

                    switch (key)
                    {
                        case "HISTORY_MODE":
                            if (Enum.TryParse<HistoryMode>(value, true, out var mode))
                            {
                                teamInfo.HistoryMode = mode;
                            }
                            else
                            {
                                Debug.WriteLine($"[Param Parser] FAILED to parse '{value}' as a valid HistoryMode.");
                            }
                            break;

                        case "SESSION_HISTORY_COUNT":
                            if (int.TryParse(value, out int count))
                            {
                                teamInfo.SessionHistoryCount = Math.Clamp(count, 0, 25);
                            }
                            else
                            {
                                Debug.WriteLine($"[Param Parser] FAILED to parse '{value}' as an integer.");
                            }
                            break;
                    }
                }
            }

            Debug.WriteLine($"[Parser TeamActivation] Success: Team='{teamInfo.TeamName}', Mode='{teamInfo.HistoryMode}', Count='{teamInfo.SessionHistoryCount}', Focus='{TruncateResponse(teamInfo.TeamFocus, 100)}'");
            return teamInfo;
        }

        private async Task ProcessCognitiveAgents(string[]? agentNames = null)
        {
            // Remove agent from the "Cognitive Collaboration Team" before reinitialization

            // await _agentDb.RemoveTeamCompletelyAsync("Cognitive Collaboration Team");

            Debug.WriteLine($"A. Test remove team and team member.");

            var teams1 = await _agentDb.GetAllTeamsAsync();

            string teamToRemove = "Strategic Planning Group";

            // remove string teamToRemoveMemberFrom = "Code Development Team";
            string teamToRemoveMemberFrom = "Quick Task Force";
            string teamMemberToRemove = "Coder";

            // add string teamToAddMemberTo = "Quick Task Force";
            string teamToAddMemberTo = "Code Development Team";
            string teamMemberToAdd = "Coder";

            string teamMemberRole = "NONE!";

            var tmpTeam = await _agentDb.GetTeamAsync("Quick Task Force");

            foreach (var team1 in teams1)
            {
                Debug.WriteLine($"\n=================\n");

                if (teamToRemove == team1.TeamName)
                {
                    bool wasTeamRemoved = await _agentDb.RemoveTeamCompletelyAsync(team1.TeamId);
                    Debug.WriteLine($"Remove Team: {team1.TeamName}, ID: {team1.TeamId}, Member Count: {team1.MemberCount} {(wasTeamRemoved ? " Completed" : " Failed")}");
                }

                if (teamToAddMemberTo.Equals(team1.TeamName, StringComparison.OrdinalIgnoreCase))
                {
                    var agent = await _agentDb.GetAgentUsingNameAsync(teamMemberToAdd);

                    if (agent != null)
                    {
                        if (agent.Name.Equals("Coder", StringComparison.OrdinalIgnoreCase)) teamMemberRole = "To Generate code.";

                        bool wasAgentAdded = await _agentDb.AddAgentToTeamAsync(team1.TeamId, agent.AgentId, teamMemberRole);

                        Debug.WriteLine($"Added Agent: {agent.Name} to Team: {team1.TeamName} {(wasAgentAdded ? " Completed" : " Failed")}");
                    }
                    else
                    {
                        Debug.WriteLine($"Agent '{teamMemberToAdd}' not found. Cannot add to team.");
                    }
                }

                Debug.WriteLine($"Keep Team: {team1.TeamName}, ID: {team1.TeamId}, Member Count: {team1.MemberCount}");

                var teamMembers2 = await _agentDb.GetTeamMembersByTeamNameAsync(team1.TeamName);

                foreach (var member in teamMembers2)
                {
                    if (team1.TeamName == teamToRemoveMemberFrom && member.AgentName == teamMemberToRemove)
                    {
                        bool wasTeamMemberRemoved = await _agentDb.RemoveAgentFromTeamAsync(team1.TeamId, member.AgentId);

                        Debug.WriteLine($"Remove Team Member: {member.AgentName}{(wasTeamMemberRemoved ? " Completed" : " Failed")}");
                    }
                    else
                    {
                        Debug.WriteLine($"Keep Team Member: {member.AgentName}");
                    }
                }
            }

            Debug.WriteLine($"1.GetAllAgentsAsync");
            var agents = await _agentDb.GetAllAgentsAsync();
            Debug.WriteLine($"Agent Count: {agents.Count}");
            foreach (var agent in agents)
            {
                Debug.WriteLine($"Agent: {agent.Name}");
            }

            Debug.WriteLine($"\n=================\n");

            Debug.WriteLine($"2. Agent ID and Value");
            var agentIds = agents.ToDictionary(a => a.Name, a => a.AgentId, StringComparer.OrdinalIgnoreCase);
            Debug.WriteLine($"Agent Count: {agentIds.Count}");
            foreach (var agent in agentIds)
            {
                Debug.WriteLine($"Agent: {agent.Key}, ID: {agent.Value}");
            }

            Debug.WriteLine($"\n=================\n");

            Debug.WriteLine($"3. GetAllTeamsAsync");
            var teams = await _agentDb.GetAllTeamsAsync();
            foreach (var team in teams)
            {
                Debug.WriteLine($"Team: {team.TeamName}, ID: {team.TeamId}, Member Count: {team.MemberCount}");
                var teamMembers1 = await _agentDb.GetTeamMembersByTeamNameAsync(team.TeamName);
                foreach (var member in teamMembers1)
                {
                    Debug.WriteLine($"Team Member: {member.AgentName}");
                }
            }

            Debug.WriteLine($"\n=================\n");

            Debug.WriteLine($"4. Checking for existing team 'Cognitive Collaboration Team'");
            var existingTeam = teams.FirstOrDefault(t => t.TeamName.Equals("Cognitive Collaboration Team", StringComparison.OrdinalIgnoreCase));

            if (existingTeam != null)
            {
                Debug.WriteLine($"Found: 'Cognitive Collaboration Team' Member Count: {existingTeam.MemberCount}");
                foreach (var member in existingTeam.Members)
                {
                    Debug.WriteLine($"Existing Team Member: {member.AgentName}");
                }
            }
            else
            {
                Debug.WriteLine("No existing team found with the name 'Cognitive Collaboration Team'.");
            }

            Debug.WriteLine($"\n=================\n");

            Debug.WriteLine("5. List all Team Members Of: 'Cognitive Collaboration Team'");
            var teamMembers = await _agentDb.GetTeamMembersByTeamNameAsync("Cognitive Collaboration Team");

            if (teamMembers != null)
            {
                Debug.WriteLine($"Member Count: {teamMembers.Count()}");
                foreach (var member in teamMembers)
                {
                    Debug.WriteLine($"Team Member: {member.AgentName}");
                }
            }
            else
            {
                Debug.WriteLine("No existing team members found with the name 'Cognitive Collaboration Team'.");
            }

            if (agentNames == null)
            {
                agentNames = new[] { "Chief", "Sentinel", "Evaluator", "Navigator", "Innovator", "Strategist", "Coder" };
            }

            Debug.WriteLine($"7. ===================\ncognitive agent Count: {agentNames.Count()}\n====================");

            foreach (var agent in agentNames)
            {
                Debug.WriteLine($"8. Processing cognitive agent: {agent}");
            }
        }

        private async Task<string> GetSummarizedProcessingContextAsync(int maxHistoryItems = 5, int maxCharsPerRecord = 300, int maxTotalChars = 4000)
        {
            Debug.WriteLine($"---> GetSummarizedProcessingContextAsync START (MaxItems: {maxHistoryItems}, MaxCharsPerRecord: {maxCharsPerRecord}, MaxTotalChars: {maxTotalChars})");
            // ... (Implementation with detailed logging from previous example) ...
            return await Task.Run(() =>
            {
                var contextBuilder = new StringBuilder();

                // 1. Start with the Goal
                Debug.WriteLine($"[Context Summary] Adding Goal: '{_currentGoal}'");
                contextBuilder.AppendLine($"## Overall Goal: {_currentGoal}");
                contextBuilder.AppendLine("---");

                // 2. Add the Last Chief Directive (if any)
                if (!string.IsNullOrWhiteSpace(_lastChiefDirective))
                {
                    string truncatedDirective = TruncateResponse(_lastChiefDirective, 1000); // Allow more length
                    Debug.WriteLine($"[Context Summary] Adding Last Chief Directive (Truncated: {truncatedDirective.Length} chars).");
                    contextBuilder.AppendLine($"## Last Chief Directive/Synthesis:");
                    contextBuilder.AppendLine(truncatedDirective);
                    contextBuilder.AppendLine("---");
                }
                else
                {
                    Debug.WriteLine($"[Context Summary] No Last Chief Directive to add.");
                }

                // 3. Add Recent Processing History
                contextBuilder.AppendLine("## Recent Processing History (Newest Last):");
                int historyCount = _processingHistory.Count;
                int startIndex = Math.Max(0, historyCount - maxHistoryItems);
                int itemsAdded = 0;
                int estimatedLength = contextBuilder.Length; // Track current length

                Debug.WriteLine($"[Context Summary] Processing History: Total={historyCount}, StartIndex={startIndex}, MaxItems={maxHistoryItems}. Current Estimated Length: {estimatedLength}");

                if (historyCount > 0)
                {
                    for (int i = startIndex; i < historyCount; i++)
                    {
                        var record = _processingHistory[i];
                        // Use a helper to format history items consistently
                        string formattedRecord = FormatHistoryItemForSummary(record, maxCharsPerRecord);
                        int recordLength = formattedRecord.Length + Environment.NewLine.Length;

                        Debug.WriteLine($"[Context Summary] Considering History Item {i}: Source='{record.SourceModule}', Length={recordLength} (Content Truncated)");

                        // Check approximate length before adding
                        if (estimatedLength + recordLength > maxTotalChars && itemsAdded > 0)
                        {
                            Debug.WriteLine($"[Context Summary] Estimated length ({estimatedLength + recordLength}) exceeds MaxTotalChars ({maxTotalChars}). Truncating history here.");
                            contextBuilder.AppendLine("[... Earlier history truncated ...]");
                            break; // Stop adding if likely to exceed max total length
                        }

                        contextBuilder.AppendLine(formattedRecord);
                        estimatedLength += recordLength;
                        itemsAdded++;
                        Debug.WriteLine($"[Context Summary] Added History Item {i}. Items Added: {itemsAdded}. New Estimated Length: {estimatedLength}");
                    }
                }

                // Add final notes based on what was added
                if (itemsAdded == 0 && historyCount > 0)
                {
                    Debug.WriteLine($"[Context Summary] All history items skipped due to length constraints.");
                    contextBuilder.AppendLine("[... History truncated due to length ...]");
                }
                else if (historyCount == 0)
                {
                    Debug.WriteLine($"[Context Summary] No processing history exists.");
                    contextBuilder.AppendLine("(No processing history yet)");
                }
                else if (itemsAdded < (historyCount - startIndex))
                {
                    Debug.WriteLine($"[Context Summary] Added {itemsAdded} most recent history items (some truncated due to length).");
                }
                else
                {
                    Debug.WriteLine($"[Context Summary] Added all {itemsAdded} considered history items.");
                }

                contextBuilder.AppendLine("--- End History ---");

                string finalContext = contextBuilder.ToString();
                Debug.WriteLine($"---> GetSummarizedProcessingContextAsync END. Final Context Length: {finalContext.Length}");
                // Debug.WriteLine($"---> Final Context Preview:\n{TruncateResponse(finalContext,
                // 500)}\n--- End Preview ---"); //
                // Optional: Log preview

                return finalContext;
            });
        }

        private string FormatHistoryItemForSummary(ProcessingRecord record, int maxCharsPerRecord)
        {
            // Example: "[Chief @ 14:32:10]: Synthesized inputs. Plan is to proceed with code generation..."
            string truncatedContent = TruncateResponse(record.OutputContent, maxCharsPerRecord);
            return $"[{record.SourceModule} @ {record.Timestamp:HH:mm:ss}]: {truncatedContent}";
        }








        /// <summary>
        /// Tests the Chief's ability to synthesize input from multiple specialists and conclude
        /// with ANY valid directive tag ([ACTIVATE], [REQUEST_AGENT_CREATION], etc.). Allows the
        /// workflow to continue after the test invocation.
        /// </summary>
        public async Task TestChiefSynthesisAndEvaluationAsync()
        {
            string testName = "Chief Synthesis & Evaluation Test V4";
            Debug.WriteLine($"\n===== STARTING TEST: {testName} =====");
            bool testPassed = true; // Assume pass initially
            string? chiefResponseText = null; // To store the response

            // --- 1. Prerequisites Check --- ... (Keep checks) ...
            if (_aiManager == null || !_aiManager.AgentExists("Chief")) { /* ... */ return; }

            // --- 2. Simulate State & Specialist Outputs ---
            ProcessingState stateBefore = ProcessingState.AwaitingChiefSynthesis; // Simulate this state
            _currentProcessingState = stateBefore;
            _currentGoal = "Design DB schema for blog V4."; // Unique goal for test
            _processingHistory.Clear();
            _pendingSpecialistOutputs.Clear();
            _expectedSpecialistModules.Clear();
            _phasedActivations.Clear();
            _currentExecutionPhase = 0;
            _completedPhaseOutputs.Clear();
            LogProcessingRecord("System (Test Setup)", $"Simulating state ({stateBefore}) for {testName}. Goal: {_currentGoal}");

            var simulatedSpecialistOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Navigator", "Workflow: Define entities, attributes, relations..." },
        { "Strategist", "Considerations: Scalability, Extensibility..." }
    };
            Debug.WriteLine($"[Test - {testName}] Using Simulated Specialist Outputs...");

            // --- 3. Setup Response Waiting ---
            var chiefResponseTcs = new TaskCompletionSource<AgentResponseEventArgs?>(TaskCreationOptions.RunContinuationsAsynchronously);
            string chiefInvocationId = $"test_ChiefSynthV4_{Guid.NewGuid()}";

            EventHandler<AgentResponseEventArgs>? tempChiefResponseHandler = null;
            tempChiefResponseHandler = (sender, args) =>
            {
                if (args.AgentName.Equals("Chief", StringComparison.OrdinalIgnoreCase) && !chiefResponseTcs.Task.IsCompleted)
                {
                    _aiManager.AgentResponse -= tempChiefResponseHandler;
                    Debug.WriteLine($"[Test Handler - {testName}] Received Chief response for {chiefInvocationId}.");
                    chiefResponseTcs.TrySetResult(args);
                }
            };

            // --- 4. Invoke Chief Synthesis ---
            _aiManager.AgentResponse += tempChiefResponseHandler;
            SetUIBusy(true, "Requesting Chief synthesis for test..."); // Keep UI busy
            Debug.WriteLine($"[Test - {testName}] Calling RequestChiefSynthesisAsync for {chiefInvocationId}...");
            await RequestChiefSynthesisAsync(simulatedSpecialistOutputs, "Synthesize inputs and plan next step.");

            // --- 5. Wait for Chief Response or Timeout ---
            TimeSpan timeout = TimeSpan.FromMinutes(2);
            Debug.WriteLine($"[Test - {testName}] Waiting up to {timeout.TotalSeconds}s for Chief's synthesis response...");
            Task completedTask = await Task.WhenAny(chiefResponseTcs.Task, Task.Delay(timeout));
            AgentResponseEventArgs? chiefResponseArgs = null;

            if (completedTask == chiefResponseTcs.Task)
            {
                chiefResponseArgs = await chiefResponseTcs.Task;
                if (chiefResponseArgs != null) { chiefResponseText = chiefResponseArgs.ResponseData; Debug.WriteLine($"[Test - {testName}] Chief response received."); }
                else { Debug.WriteLine($"[Test Error - {testName}] Chief response TCS completed null."); testPassed = false; }
            }
            else { Debug.WriteLine($"[Test Error - {testName}] Timed out."); testPassed = false; chiefResponseTcs.TrySetCanceled(); }

            // --- 6. Cleanup Handler ---
            _aiManager.AgentResponse -= tempChiefResponseHandler;
            Debug.WriteLine($"[Test Cleanup - {testName}] Ensured handler unsubscribed.");

            // --- 7. Verification ---
            Debug.WriteLine($"[Test Verification - {testName}]");

            if (string.IsNullOrWhiteSpace(chiefResponseText))
            {
                Debug.WriteLine("  [FAIL] Chief response was null or empty.");
                testPassed = false;
            }
            else
            {
                // Check 1: Synthesis Keywords (Soft Check)
                var synthesisKeywords = new[] { "synthesiz", "integrat", "combin", "evaluat", "consider", "navigator", "strategist", "balancing", "incorporating" };
                bool synthesisDetected = synthesisKeywords.Any(kw => chiefResponseText.Contains(kw, StringComparison.OrdinalIgnoreCase));
                Debug.WriteLine(synthesisDetected
                    ? "  [PASS] Response likely contains evidence of synthesis/evaluation (keyword check)."
                    : "  [WARN] Response did not contain common synthesis/evaluation keywords. Manual inspection needed.");

                // Check 2: Presence of *ANY* Valid Concluding Tag This Regex checks if the string
                // ends with ANY of the known concluding tag blocks.
                var concludingTagRegex = new Regex(@"\[(?:ACTIVATION_DIRECTIVES|ACTION_ASK_USER|FINAL_[A-Z0-9_]+|ACTION_HALT|REQUEST_AGENT_CREATION)\].*?\[/(?:ACTIVATION_DIRECTIVES|ACTION_ASK_USER|FINAL_[A-Z0-9_]+|ACTION_HALT|REQUEST_AGENT_CREATION)\]\s*\z", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                // Simpler check just for the start tag at the end
                var startTagAtEndRegex = new Regex(@"\[(?:ACTIVATION_DIRECTIVES|ACTION_ASK_USER|FINAL_[A-Z0-9_]+|ACTION_HALT|REQUEST_AGENT_CREATION)\]", RegexOptions.IgnoreCase);

                bool hasValidConcludingTag = concludingTagRegex.IsMatch(chiefResponseText.TrimEnd());
                if (!hasValidConcludingTag)
                {
                    var lastTagIndex = chiefResponseText.LastIndexOf('[');
                    if (lastTagIndex > 0)
                    {
                        var endSegment = chiefResponseText.Substring(lastTagIndex);
                        hasValidConcludingTag = startTagAtEndRegex.IsMatch(endSegment);
                        if (hasValidConcludingTag) Debug.WriteLine("[Test Verification] Matched concluding tag using fallback check (start tag near end).");
                    }
                }

                if (hasValidConcludingTag)
                {
                    Debug.WriteLine("  [PASS] Response concludes with a valid directive tag.");
                }
                else
                {
                    Debug.WriteLine("  [FAIL] Response does NOT conclude with ANY valid directive tag.");
                    Debug.WriteLine($"Response End Preview: '{TruncateResponse(chiefResponseText.Length > 200 ? chiefResponseText.Substring(chiefResponseText.Length - 200) : chiefResponseText, 200)}'");
                    testPassed = false;
                }
            }

            // --- 8. Conclusion ---
            Debug.WriteLine($"===== TEST {(testPassed ? "PASSED" : "FAILED")}: {testName} =====");
            MessageBox.Show(
                $"Test '{testName}' completed.\nResult: {(testPassed ? "PASSED" : "FAILED")}\n\nCheck Debug Output for detailed logs. Workflow will continue if Chief provided a valid directive.", // Updated message
                "Test Result",
                MessageBoxButtons.OK,
                testPassed ? MessageBoxIcon.Information : MessageBoxIcon.Warning
            );

            // --- DO NOT RESET STATE --- Allow the workflow initiated/continued by the Chief's
            // response to proceed. The UI busy state will be handled by the normal workflow
            // completion/error paths. Debug.WriteLine($"[Test - {testName}] NOT resetting state.
            // Current state: {_currentProcessingState}"); SetUIBusy(false, "Test complete. Workflow
            // continues..."); // Don't do this here
        }

        public async Task TestMultiAgentInputScenarioAsync()
        {
            string testName = "Multi-Agent Input & Collection Test V5";
            Debug.WriteLine($"\n===== STARTING TEST: {testName} =====");
            bool testPassed = true;
            List<string> expectedAgentsInFirstRound_Test = new List<string>(); // Scoped to test
            ConcurrentDictionary<string, bool> responsesReceivedTracker_Test = new ConcurrentDictionary<string, bool>(); // Scoped to test
            int finalSpecialistResponsesCollectedByTest = 0; // Scoped to test

            // --- 1. Prerequisites Check ---
            if (_aiManager == null || !_aiManager.AgentExists("Chief"))
            {
                Debug.WriteLine($"[Test Error - {testName}] AIManager not initialized or Chief agent not found. Aborting.");
                MessageBox.Show("AIManager/Chief not ready.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (_currentProcessingState != ProcessingState.Idle && _currentProcessingState != ProcessingState.Error && _currentProcessingState != ProcessingState.ProcessingComplete)
            {
                Debug.WriteLine($"[Test Error - {testName}] System busy ({_currentProcessingState}). Aborting test.");
                MessageBox.Show("System is busy. Cannot start test.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // --- 2. Define Goal & Reset Controller State for this test run ---
            string testGoal = "Evaluate proposal V5: Refactor legacy auth module with new library (risks, benefits, steps, strategy).";
            _currentGoal = testGoal;
            _processingHistory.Clear();
            _pendingSpecialistOutputs.Clear(); // Controller's main pending list
            _expectedSpecialistModules.Clear(); // Controller's main expected list
            _phasedActivations.Clear();
            _currentExecutionPhase = 0;
            _completedPhaseOutputs.Clear();
            _lastChiefDirective = string.Empty;
            _chiefClarificationAttempts = 0;
            _lastChiefErrorResponse = string.Empty;
            _currentProcessingState = ProcessingState.Idle; // Ensure start from Idle for the controller

            // --- 3. Setup State/Response Monitoring for the TEST ---
            var synthesisReadyTcs_Test = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ProcessingState stateReachedBeforeSynthesis_Test = ProcessingState.Idle; // Test's view of prev state

            EventHandler<AgentResponseEventArgs>? tempMultiAgentHandler_Test = null;
            tempMultiAgentHandler_Test = (sender, args) =>
            {
                // This handler is ONLY for the TEST's internal logic and TCS. It observes the main
                // controller's state (_currentProcessingState).
                var controllerCurrentStateSnapshot = _currentProcessingState;

                // Capture Expected Agents (Run Once by test handler when controller enters AwaitingSpecialistInput)
                if (controllerCurrentStateSnapshot == ProcessingState.AwaitingSpecialistInput &&
                    expectedAgentsInFirstRound_Test.Count == 0 && // Test handler hasn't captured them yet
                    _expectedSpecialistModules.Any()) // Controller has set its expectations
                {
                    expectedAgentsInFirstRound_Test = _expectedSpecialistModules.ToList(); // Capture controller's expectation
                    responsesReceivedTracker_Test.Clear();
                    foreach (var agentName in expectedAgentsInFirstRound_Test)
                    {
                        responsesReceivedTracker_Test.TryAdd(agentName, false);
                    }
                    Debug.WriteLine($"[Test Handler - {testName}] Controller expects: {string.Join(", ", expectedAgentsInFirstRound_Test)}. Test tracker initialized.");
                }

                // Track Specialist Responses (for test verification purposes)
                if (controllerCurrentStateSnapshot == ProcessingState.AwaitingSpecialistInput &&
                    expectedAgentsInFirstRound_Test.Contains(args.AgentName) && // Is it one of the initially expected?
                    responsesReceivedTracker_Test.ContainsKey(args.AgentName))
                {
                    if (responsesReceivedTracker_Test.TryUpdate(args.AgentName, true, false))
                    {
                        finalSpecialistResponsesCollectedByTest = responsesReceivedTracker_Test.Count(kvp => kvp.Value);
                        Debug.WriteLine($"[Test Handler - {testName}] Logged specialist response from {args.AgentName} ({finalSpecialistResponsesCollectedByTest}/{expectedAgentsInFirstRound_Test.Count})");
                    }
                }

                // Detect Transition to Synthesis (based on CONTROLLER's state)
                if (controllerCurrentStateSnapshot == ProcessingState.AwaitingChiefSynthesis &&
                    stateReachedBeforeSynthesis_Test == ProcessingState.AwaitingSpecialistInput)
                {
                    Debug.WriteLine($"[Test Handler - {testName}] Controller reached AwaitingChiefSynthesis state after specialists responded. Test signaling completion.");
                    synthesisReadyTcs_Test.TrySetResult(true); // Signal that the controller reached synthesis state
                }

                // Update the test's view of the previous state
                stateReachedBeforeSynthesis_Test = controllerCurrentStateSnapshot;
            };

            _aiManager.AgentResponse += tempMultiAgentHandler_Test; // Subscribe test's temporary handler

            // --- 4. Initiate Task (Controller starts its process) ---
            Debug.WriteLine($"[Test - {testName}] Initiating task for controller. Goal: {testGoal}");
            // Set controller's initial state and send request to Chief
            _currentProcessingState = ProcessingState.AwaitingChiefInitiation;
            LogProcessingRecord("User (Test)", testGoal);
            SetUIBusy(true, $"Sending multi-perspective goal to Chief for {testName}...");

            bool requestSent = false;
            Stopwatch testStopwatch = Stopwatch.StartNew();
            try
            {
                string initialPrompt = $"New Goal Received: \"{testGoal}\". Analyze this complex goal. Determine the initial specialist cognitive modules whose input is needed concurrently to form a comprehensive initial assessment. Outline the activation steps using concluding `[ACTIVATE]ModuleName:Focus[/ACTIVATE]` tags.";
                await _aiManager.RequestAsync("Chief", initialPrompt); // This kicks off the CONTROLLER'S loop
                requestSent = true;
                Debug.WriteLine($"[Test - {testName}] Initial request sent to Chief by controller.");
            }
            catch (Exception ex)
            {
                HandleProcessingError($"[Test - {testName}] Error sending initial request: {ex.Message}", showMessageBox: false);
                synthesisReadyTcs_Test.TrySetResult(false); // Signal failure for the test
                testPassed = false;
            }

            // --- 5. Wait for TEST's Synthesis Readiness Signal or Timeout ---
            bool synthesisReachedByTestSignal = false;
            if (requestSent && testPassed)
            {
                TimeSpan timeout = TimeSpan.FromMinutes(5);
                Debug.WriteLine($"[Test - {testName}] Test waiting up to {timeout.TotalSeconds}s for controller to signal it has reached synthesis stage...");
                var completedTask = await Task.WhenAny(synthesisReadyTcs_Test.Task, Task.Delay(timeout));

                if (completedTask == synthesisReadyTcs_Test.Task)
                {
                    synthesisReachedByTestSignal = await synthesisReadyTcs_Test.Task;
                    if (synthesisReachedByTestSignal)
                    {
                        Debug.WriteLine($"[Test - {testName}] Controller signaled synthesis stage reached. Time: {testStopwatch.ElapsedMilliseconds}ms.");
                    }
                    else
                    {
                        Debug.WriteLine($"[Test Error - {testName}] Controller signaled synthesis stage with FALSE (unexpected by test).");
                        testPassed = false;
                    }
                }
                else
                {
                    Debug.WriteLine($"[Test Error - {testName}] Timed out waiting for controller to signal synthesis stage after {testStopwatch.ElapsedMilliseconds}ms.");
                    synthesisReachedByTestSignal = false;
                    testPassed = false;
                    // If test times out, it means controller got stuck or didn't reach synthesis as
                    // expected by test handler. Controller's state might be Error or still AwaitingSpecialistInput.
                    if (_currentProcessingState != ProcessingState.Error)
                    {
                        _currentProcessingState = ProcessingState.Error; // Mark controller state as error for test purposes
                        SetUIBusy(false, "Test timed out waiting for synthesis signal.");
                    }
                }
            }
            testStopwatch.Stop();

            // --- 6. Cleanup Test's Temporary Handler ---
            if (tempMultiAgentHandler_Test != null)
            {
                _aiManager.AgentResponse -= tempMultiAgentHandler_Test;
                Debug.WriteLine($"[Test Cleanup - {testName}] Ensured test's temporary response handler unsubscribed.");
            }

            // --- 7. Verification (Based on Test's Observations) ---
            Debug.WriteLine($"[Test Verification - {testName}]");

            if (!synthesisReachedByTestSignal)
            {
                Debug.WriteLine("  [FAIL] Test: Controller did not signal that it successfully reached the Chief Synthesis stage.");
                testPassed = false;
            }
            else
            {
                Debug.WriteLine("  [PASS] Test: Controller signaled that it reached the Chief Synthesis stage.");

                // How many agents did the *controller* expect in that first round (captured by test handler)?
                int expectedByControllerInFirstRound = expectedAgentsInFirstRound_Test.Count;
                if (expectedByControllerInFirstRound > 1)
                {
                    Debug.WriteLine($"  [PASS] Test: Controller (via Chief) initially requested input from multiple ({expectedByControllerInFirstRound}) specialists: {string.Join(", ", expectedAgentsInFirstRound_Test)}.");
                }
                else
                {
                    Debug.WriteLine($"  [FAIL] Test: Controller (via Chief) did not request input from multiple specialists as expected by this test (Controller expected: {expectedByControllerInFirstRound}).");
                    testPassed = false;
                }

                // Did the test's handler observe all those responses before signaling?
                if (finalSpecialistResponsesCollectedByTest == expectedByControllerInFirstRound && expectedByControllerInFirstRound > 0)
                {
                    Debug.WriteLine($"  [PASS] Test: Test handler observed responses from all {expectedByControllerInFirstRound} specialists it expected in the first round.");
                }
                else if (expectedByControllerInFirstRound > 0) // Only fail if agents were expected but count mismatches
                {
                    Debug.WriteLine($"  [FAIL] Test: Mismatch in responses observed by test handler. Expected by test handler: {expectedByControllerInFirstRound}, Observed by test handler: {finalSpecialistResponsesCollectedByTest}.");
                    testPassed = false;
                }
                else // Case where expectedAgentCountInController is 0
                {
                    Debug.WriteLine($"  [WARN] Test: No specialists were initially activated by the Chief, or test handler did not capture the expectation list.");
                    // For *this specific test*, this scenario might be a failure if
                    // multi-activation is key.
                    if (expectedByControllerInFirstRound == 0 && testGoal.Contains("Evaluate proposal V5")) testPassed = false; // V5 goal implies multi-agent
                }
            }

            // --- 8. Conclusion ---
            Debug.WriteLine($"===== TEST {(testPassed ? "PASSED" : "FAILED")}: {testName} =====");
            MessageBox.Show(
                $"Test '{testName}' completed.\nResult: {(testPassed ? "PASSED" : "FAILED")}\n\n" +
                $"Check Debug Output. Verify the main controller loop proceeded correctly.",
                "Test Result",
                MessageBoxButtons.OK,
                testPassed ? MessageBoxIcon.Information : MessageBoxIcon.Warning
            );

            // Let the main controller workflow continue from where the test left it (which should
            // be AwaitingChiefSynthesis if the test passed its main check). UI busy state is
            // managed by the main controller loop.
            if (_currentProcessingState != ProcessingState.Error && _currentProcessingState != ProcessingState.ProcessingComplete)
            {
                Debug.WriteLine($"[Test - {testName}] Test finished. Controller state is now: {_currentProcessingState}. Workflow will continue.");
            }
            else
            {
                SetUIBusy(false); // Ensure UI is reset if controller ended in Error/Complete during the test's observation window.
            }
        }

        /// <summary>
        /// Tests if the controller correctly parses the [REQUEST_AGENT_CREATION] tag from a
        /// simulated Chief response and initiates the agent creation process (which includes user confirmation).
        /// </summary>
        public async Task TestAgentCreationTagParsingAsync()
        {
            string testName = "Agent Creation Tag Parsing Test";
            Debug.WriteLine($"\n===== STARTING TEST: {testName} =====");
            bool testPassed = true; // Assume pass initially

            // --- 1. Prerequisites Check ---
            if (_aiManager == null || _agentDb == null) // Need these for the handler context
            {
                Debug.WriteLine($"[Test Error - {testName}] Core components not initialized. Aborting.");
                MessageBox.Show("Core components not initialized.", testName + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // Temporarily allow running even if busy for test setup if (_currentProcessingState !=
            // ProcessingState.Idle && ... ) { ... }

            // --- 2. Simulate State & Chief Response ---
            ProcessingState stateBefore = ProcessingState.AwaitingChiefSynthesis; // Plausible state
            _currentProcessingState = stateBefore;
            _currentGoal = "Test Goal: Create a Sentiment Analyzer Agent";
            _processingHistory.Clear();
            LogProcessingRecord("System (Test Setup)", $"Simulating state ({stateBefore}) before Agent Creation Request");

            // Define the details for the agent to be created
            string newAgentName = "TestAgent";
            string newAgentPurpose = "Analyzes text to determine sentiment (positive, negative, neutral).";
            string newAgentCapabilities = "Natural Language Processing, Sentiment Analysis, Text Classification"; // Comma-separated for parsing test
            string newAgentPrompt = @"# SentimentAnalyzer PROMPT
You are a specialized agent focused on analyzing text sentiment.
## CORE FUNCTION
- Receive text input.
- Classify sentiment as Positive, Negative, or Neutral.
- Provide a confidence score.
## INTERACTION PROTOCOL
- Output JSON: {'sentiment': '...', 'confidence': 0.xx}
- Do not activate other modules.";

            // Craft the simulated Chief response ending *correctly* with the tag block
            string simulatedChiefResponse = $@"[REQUEST_AGENT_CREATION]
[NAME]{newAgentName}[/NAME]
[CAPABILITIES]{newAgentCapabilities}[/CAPABILITIES]
[PURPOSE]{newAgentPurpose}[/PURPOSE]
[PROMPT]
[HEADER]# {newAgentName}[/HEADER]
{newAgentPrompt}
[/PROMPT]
[/REQUEST_AGENT_CREATION]"; // Tag block at the very end

            Debug.WriteLine($"[Test - {testName}] Using Simulated Chief Response:\n---\n{simulatedChiefResponse}\n---");

            // --- 3. Setup Confirmation Monitoring (Optional but Recommended) --- We can't easily
            // automate clicking the MessageBox, but we can monitor if the
            // ProcessAgentCreationRequest method gets called by checking logs or by setting a flag
            // within a temporarily modified ProcessAgentCreationRequest. For simplicity here, we
            // will rely on observing the MessageBox popping up and checking the subsequent state change.

            // --- 4. Directly Invoke AgentResponseEvent ---
            var simulatedEventArgs = new AgentResponseEventArgs("Chief", "Create Agent", simulatedChiefResponse);
            Debug.WriteLine($"[Test - {testName}] Manually invoking AgentResponseEvent for simulated Chief response...");

            // --- Execute the handler ---
            AgentResponseEvent(this, simulatedEventArgs);

            // --- 5. Verification (Check State & Observe Dialog) --- Give a brief moment for the
            // synchronous parts of AgentResponseEvent and potentially the start of
            // ProcessAgentCreationRequest (up to the MessageBox) to run.
            await Task.Delay(100);

            Debug.WriteLine($"[Test Verification - {testName}] Checking state after AgentResponseEvent invocation...");

            ProcessingState stateAfter = _currentProcessingState;

            // EXPECTATION: The ProcessAgentCreationRequest method should be called. That method
            // *itself* will show a MessageBox and then call RequestChiefClarificationAsync.
            // Therefore, the state *after* AgentResponseEvent should ideally be back to
            // AwaitingChiefSynthesis, because ProcessAgentCreationRequest concludes by requesting
            // clarification. The *real* verification is seeing the MessageBox pop up.

            Debug.WriteLine($"  [INFO] State immediately after AgentResponseEvent call returns: {stateAfter}");
            Debug.WriteLine($"  [INFO] Expected state after ProcessAgentCreationRequest runs fully: AwaitingChiefSynthesis");

            // --- Verification Steps ---
            // 1. Manual Check: Did the 'Confirm New Agent Creation' MessageBox appear?
            // 2. Log Check: Check the debug output for "[Controller] Processing agent creation
            // request for 'SentimentAnalyzer'." log message from ProcessAgentCreationRequest.
            // 3. State Check (Less Direct): Verify the state eventually becomes
            // AwaitingChiefSynthesis after the MessageBox is handled (manually click Yes/No).

            MessageBox.Show(
                $"Test '{testName}' invoked the handler.\n\n" +
                "VERIFICATION REQUIRED:\n" +
                "1. Did the 'Confirm New Agent Creation' dialog appear?\n" +
                "2. Check the Debug Output for '[Controller] Processing agent creation request...' log.\n\n" +
                "(Click Yes or No on the confirmation dialog to allow the process to continue)",
                "Manual Verification Needed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            // --- 6. Conclusion --- Test success depends on manual observation and log checking here.
            Debug.WriteLine($"===== TEST COMPLETED: {testName} =====");
            Debug.WriteLine("Please verify manually if the confirmation dialog appeared and check logs.");

            // Reset state after test (or let the subsequent clarification call handle it)
            // _currentProcessingState = ProcessingState.Idle; SetUIBusy(false, "Test complete. Ready.");
        }

        /// <summary>
        /// Runs an isolated test interaction with a specific cognitive module. Sends a prompt
        /// (optionally with context) and waits for/logs the response. Does NOT use the main
        /// controller state (_currentProcessingState, _processingHistory).
        /// </summary>
        /// <param name="testName">A descriptive name for this test run (for logging).</param>
        /// <param name="targetModuleName">
        /// The exact name of the module to test (e.g., "Chief", "Evaluator").
        /// </param>
        /// <param name="prompt">The specific input/question for the module.</param>
        /// <param name="simulatedContext">
        /// Optional string representing context to prepend to the prompt.
        /// </param>
        /// <param name="timeout">Optional timeout duration. Defaults to 2 minutes.</param>
        /// <returns>The response content string if successful, otherwise null.</returns>
        public async Task<string?> TestRunModuleInteractionAsync(string testName, string targetModuleName, string prompt, string? simulatedContext = null, TimeSpan? timeout = null)
        {
            // --- Test Setup ---
            Debug.WriteLine($"\n===== STARTING MODULE TEST: {testName} =====");
            Debug.WriteLine($"Target Module: {targetModuleName}");
            Debug.WriteLine($"Prompt: {prompt}");
            if (!string.IsNullOrEmpty(simulatedContext))
            {
                Debug.WriteLine($"Simulated Context:\n---\n{simulatedContext}\n---");
            }

            // Basic validation
            if (_aiManager == null)
            {
                Debug.WriteLine("[Test Error] AIManager is not initialized.");
                Debug.WriteLine($"===== TEST FAILED: {testName} =====");
                return null;
            }
            if (!_aiManager.AgentExists(targetModuleName)) // Still uses AgentExists internally
            {
                Debug.WriteLine($"[Test Error] Module '{targetModuleName}' not found in AIManager.");
                Debug.WriteLine($"===== TEST FAILED: {testName} =====");
                return null;
            }

            timeout ??= TimeSpan.FromMinutes(2); // Default timeout if null

            // --- Construct Full Prompt ---
            var fullPromptBuilder = new StringBuilder();
            if (!string.IsNullOrEmpty(simulatedContext))
            {
                fullPromptBuilder.AppendLine("--- SIMULATED CONTEXT ---");
                fullPromptBuilder.AppendLine(simulatedContext);
                fullPromptBuilder.AppendLine("--- END CONTEXT ---");
                fullPromptBuilder.AppendLine(); // Add separation
            }
            fullPromptBuilder.AppendLine("--- TEST PROMPT / INPUT ---");
            fullPromptBuilder.AppendLine(prompt);
            fullPromptBuilder.AppendLine("--- END PROMPT ---");
            string fullPrompt = fullPromptBuilder.ToString();
            Debug.WriteLine($"[Test] Full prompt length: {fullPrompt.Length}");

            // --- Setup Response Waiting ---
            var responseTcs = new TaskCompletionSource<AgentResponseEventArgs?>(TaskCreationOptions.RunContinuationsAsynchronously);
            string invocationId = $"test_{targetModuleName}_{Guid.NewGuid()}"; // Unique ID for logging/debugging

            // Define a local handler specifically for this test response
            EventHandler<AgentResponseEventArgs>? specificTestHandler = null;
            specificTestHandler = (sender, args) =>
            {
                // Check if the response is from the correct module
                // NOTE: This relies on the assumption that the *next* response from this module
                // belongs to this test request, as we don't have true invocation IDs passed back
                // yet. This might capture unrelated responses if the module is busy.
                if (args.AgentName.Equals(targetModuleName, StringComparison.OrdinalIgnoreCase) && !responseTcs.Task.IsCompleted)
                {
                    Debug.WriteLine($"[Test Handler - {testName}] Response received from {args.AgentName}.");
                    var handlerToRemove = specificTestHandler; // Capture for safe unsubscribe
                    if (handlerToRemove != null)
                    {
                        _aiManager.AgentResponse -= handlerToRemove; // Unsubscribe immediately
                        specificTestHandler = null; // Clear the reference
                        Debug.WriteLine($"[Test Handler - {testName}] Unsubscribed.");
                    }
                    responseTcs.TrySetResult(args); // Signal completion
                }
            };

            // --- Subscribe and Send Request ---
            _aiManager.AgentResponse += specificTestHandler;
            string? responseData = null;
            bool success = false;
            Stopwatch sw = Stopwatch.StartNew(); // Measure duration

            try
            {
                Debug.WriteLine($"[Test - {testName}] Sending request to {targetModuleName} ({invocationId})...");
                // Send the request using AIManager
                await _aiManager.RequestAsync(targetModuleName, fullPrompt);

                // --- Wait for Response or Timeout ---
                Debug.WriteLine($"[Test - {testName}] Waiting for response (Timeout: {timeout.Value.TotalSeconds}s)...");
                var timeoutTask = Task.Delay(timeout.Value);
                var completedTask = await Task.WhenAny(responseTcs.Task, timeoutTask);

                sw.Stop(); // Stop timer

                if (completedTask == responseTcs.Task)
                {
                    // Response received
                    var resultArgs = await responseTcs.Task; // Get result from TCS
                    if (resultArgs != null)
                    {
                        responseData = resultArgs.ResponseData;
                        success = true; // Assume success if we got a non-null response
                        Debug.WriteLine($"[Test - {testName}] Response received from {targetModuleName} in {sw.ElapsedMilliseconds}ms.");
                        Debug.WriteLine($"--- Response Content ({targetModuleName}) ---\n{responseData}\n---");
                    }
                    else
                    {
                        // Should ideally not happen if TrySetResult(args) was called with non-null args
                        Debug.WriteLine($"[Test Error - {testName}] Response TCS completed but yielded null arguments for {targetModuleName}.");
                        success = false;
                    }
                }
                else
                {
                    // Timeout occurred
                    Debug.WriteLine($"[Test Error - {testName}] Wait for response from {targetModuleName} timed out after {sw.ElapsedMilliseconds}ms.");
                    responseTcs.TrySetCanceled(); // Ensure TCS is cancelled
                    success = false;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                Debug.WriteLine($"[Test Error - {testName}] Exception during request/wait for {targetModuleName} after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                responseTcs.TrySetCanceled(); // Ensure TCS is cancelled on error
                success = false;
            }
            finally
            {
                // --- Cleanup --- Ensure the handler is unsubscribed, even if it was already
                // removed upon success
                if (specificTestHandler != null)
                {
                    _aiManager.AgentResponse -= specificTestHandler;
                    Debug.WriteLine($"[Test Cleanup - {testName}] Ensured handler unsubscribed.");
                }
            }

            Debug.WriteLine($"===== TEST {(success ? "COMPLETED" : "FAILED")}: {testName} =====");
            return responseData; // Return the response string or null
        }

        // Renamed from runTestButton_Click
        private async void testRunBasicScenario()
        {
            // runTestButton.Enabled = false; // Disable during test statusLabel.Text = "Running
            // test scenario...";

            try
            {
                string testPrompt = "Outline the key components of a basic REST API for user management.";
                await _integration.RunTestScenarioAsync(testPrompt);
                // statusLabel.Text = "Test scenario sent. Waiting for responses...";
                Debug.WriteLine("Test scenario sent. Waiting for responses...");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running test scenario: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Debug.WriteLine($"Error running test scenario: {ex.Message}");
                // statusLabel.Text = "Test scenario failed.";
            }
            // finally { runTestButton.Enabled = true; // Re-enable after test }
        }

        private async Task TestCreateCustomTeam()
        {
            // Example 1: Creating a "Code Development Team"
            string codeTeamName = "Code Development Team";
            // string codeTeamChief = "Coder"; // Let's say Coder is the lead for this team
            var codeTeamMembersAndRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Coder", "Lead Developer" },       // Coder is also a member, with a lead role
                { "Evaluator", "Code Reviewer" },
                { "Sentinel", "Security Analyst" }
            };
            string codeTeamDesc = "Team focused on generating, reviewing, and securing code.";

            int codeTeamId = await CreateOrUpdateCognitiveTeamAsync(codeTeamName, codeTeamMembersAndRoles, codeTeamDesc);
            if (codeTeamId > 0)
            {
                MessageBox.Show($"Team '{codeTeamName}' created/updated successfully with ID: {codeTeamId}.");
            }
            else
            {
                MessageBox.Show($"Failed to create/update team '{codeTeamName}'.");
            }

            // Example 2: Creating a "Strategic Planning Group"
            string planningTeamName = "Strategic Planning Group";
            // string planningTeamChief = "Strategist";
            var planningTeamMembersAndRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Strategist", "Lead Strategist" },
        { "Innovator", "Idea Generator" },
        { "Evaluator", "Feasibility Analyst" }
        // Chief (system Chief) is not explicitly added here unless you want them in every team.
    };
            string planningTeamDesc = "Team dedicated to long-term strategic planning and innovation.";

            int planningTeamId = await CreateOrUpdateCognitiveTeamAsync(planningTeamName, planningTeamMembersAndRoles, planningTeamDesc);
            if (planningTeamId > 0) { /* ... */ }

            // Example 3: Creating a simple team where Chief is also the team chief
            string simpleTeamName = "Quick Task Force";
            // string simpleTeamChief = "Chief"; // System's Chief agent leads this small task force
            var simpleTeamMembersAndRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Chief", "Task Lead" }, // Role for Chief within this team
        { "Navigator", "Process Support" }
    };
            int simpleTeamId = await CreateOrUpdateCognitiveTeamAsync(simpleTeamName, simpleTeamMembersAndRoles, "A small, agile task force.");
            if (simpleTeamId > 0) { /* ... */ }

            // Example 4: Default "Cognitive Collaboration Team" (similar to your original)
            // If you still want a general default team:
            //        string defaultTeamName = "Cognitive Collaboration Team";
            //    //    string defaultTeamChief = "Chief";
            //        var defaultTeamMembersAndRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            //{
            //    { "Chief", "Chief" },
            //    { "Sentinel", "Verification & Compliance" },
            //    { "Evaluator", "Analytical Assessor" },
            //    { "Navigator", "Process Guide" },
            //    { "Innovator", "Creative Specialist" },
            //    { "Strategist", "Long-Term Planner" },
            //    { "Coder", "Writing Code" }
            //};
            //        string defaultTeamDesc = "Primary team for collaborative problem solving using cognitive specialization.";
            //        await CreateOrUpdateCognitiveTeamAsync(defaultTeamName,  defaultTeamMembersAndRoles, defaultTeamDesc);
        }








        private async Task RequestChiefClarificationAsync(string previousChiefOutputOrReason)
        {
            Debug.WriteLine($"[Controller] Requesting clarification from Chief. Reason/Previous Output: {TruncateResponse(previousChiefOutputOrReason, 200)}");

            // Ensure we are in a state where Chief's response is expected
            _currentProcessingState = ProcessingState.AwaitingChiefSynthesis; // Or a specific "AwaitingChiefClarification" if you add one
            SetUIBusy(true, "Requesting clarification from Chief...");

            // It's good practice to clear these before a new Chief interaction cycle starts
            _expectedSpecialistModules.Clear();
            _pendingSpecialistOutputs.Clear();
            _phasedActivations.Clear();
            _currentExecutionPhase = 0;
            _completedPhaseOutputs.Clear();
            // _chiefClarificationAttempts is incremented *before* calling this if it's a loop

            string contextSummary = await GetSummarizedProcessingContextAsync();
            string prompt = BuildSynthesisPrompt(
                specialistOutputs: new Dictionary<string, string>(),
                currentGoal: _currentGoal,
                summarizedContext: contextSummary,
                additionalInfo: $"Clarification needed. Your previous output or the situation was unclear or did not follow the required directive format. Details of previous output:\n'{TruncateResponse(previousChiefOutputOrReason, 500)}'\nPlease provide a clear next cognitive step following the required concluding tag format (e.g., [ACTIVATION_DIRECTIVES]...[/ACTIVATION_DIRECTIVES])."
            );

            Debug.WriteLine($"--- PROMPT for Chief Clarification (Attempt: {_chiefClarificationAttempts}) ---");
            Debug.WriteLine(prompt);
            Debug.WriteLine("--- END PROMPT ---");

            try
            {
                await _aiManager.RequestAsync("Chief", prompt);
            }
            catch (Exception ex)
            {
                HandleProcessingError($"Error requesting clarification from Chief: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Requests the Chief agent to synthesize outputs from specialist modules. (Ensure this
        /// overload accepting Dictionary is used or adapt the other one)
        /// </summary>
        private async Task RequestChiefSynthesisAsync(Dictionary<string, string> specialistOutputs, string? additionalInfo = null)
        {
            // Check if there's anything to synthesize (caller might handle this)
            if ((specialistOutputs == null || !specialistOutputs.Any()) && string.IsNullOrEmpty(additionalInfo))
            {
                Debug.WriteLine("[Controller Warning] RequestChiefSynthesisAsync called with no outputs and no additional info. Requesting clarification instead.");
                // Use the existing clarification method
                await RequestChiefClarificationAsync("Synthesis requested, but no specialist outputs or additional info were provided from the previous step. Please clarify the next step based on the current context.");
                return;
            }

            // Set state BEFORE making the async call
            _currentProcessingState = ProcessingState.AwaitingChiefSynthesis;
            SetUIBusy(true, "Requesting Chief synthesis...");
            Debug.WriteLine($"[Controller] State changed to: {_currentProcessingState}");

            // Get context summary
            string contextSummary = await GetSummarizedProcessingContextAsync();

            // Build prompt using the helper, passing the provided dictionary This calls the
            // BuildSynthesisPrompt that should format with [AGENT] tags
            string synthesisPrompt = BuildSynthesisPrompt(
                specialistOutputs ?? new Dictionary<string, string>(), // Pass empty dict if null
                _currentGoal,
                contextSummary,
                additionalInfo
            );

            // Log the prompt being sent
            Debug.WriteLine($"--- PROMPT for Chief Synthesis ---");
            Debug.WriteLine(synthesisPrompt); // Log the full prompt for debugging
            Debug.WriteLine("--- END PROMPT ---");

            // Clear pending states for the *next* round *before* sending request This ensures that
            // if Chief responds quickly, we don't process old state
            _pendingSpecialistOutputs.Clear();
            _expectedSpecialistModules.Clear();
            _phasedActivations.Clear();
            _currentExecutionPhase = 0;
            _completedPhaseOutputs.Clear();

            try
            {
                // Send request to Chief
                await _aiManager.RequestAsync("Chief", synthesisPrompt);
                Debug.WriteLine("[Controller] Synthesis request sent to Chief.");
            }
            catch (Exception ex)
            {
                HandleProcessingError($"Error requesting synthesis from Chief: {ex.Message}");
                // HandleProcessingError already sets state to Error and updates UI
            }
            // No return needed here, AgentResponseEvent will handle Chief's response
        }

        public async Task ImprovePromptForAgentAsync(string agentName)
        {
            if (_agentDb == null || _aiManager == null) return;
            Debug.WriteLine($"Attempting to improve prompt for agent {agentName}...");

            try
            {
                // Get agent from database
                var agents = await _agentDb.GetAllAgentsAsync();
                var agent = agents.FirstOrDefault(a => a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

                if (agent == null)
                {
                    Debug.WriteLine($"Agent '{agentName}' not found in database.");
                    return;
                }

                // Get current version
                var currentVersion = await _agentDb.GetCurrentAgentVersionAsync(agent.AgentId);
                if (currentVersion == null)
                {
                    Debug.WriteLine($"No current/active version found for agent {agentName}.");
                    return;
                }
                Debug.WriteLine($"Current prompt version for {agentName}: {currentVersion.VersionNumber}");

                // Get performance data
                var performanceStats = await _agentDb.GetAgentPerformanceStatsAsync(agent.AgentId);
                if (!performanceStats.Any())
                {
                    Debug.WriteLine($"No performance stats found for {agentName}. Cannot determine weak areas.");
                    return;
                }

                // Identify weak task types (e.g., success rate below 70%)
                var weakAreas = performanceStats
                    .Where(p => p.TotalAttempts > 3 && p.SuccessRate < 0.70) // Require some attempts for significance
                    .Select(p => p.TaskType)
                    .Distinct()
                    .ToList();

                if (!weakAreas.Any())
                {
                    Debug.WriteLine($"No significant weak areas identified for {agentName} based on current performance data.");
                    return;
                }

                Debug.WriteLine($"Identified weak areas for {agentName}: {string.Join(", ", weakAreas)}");

                // Generate improved prompt
                string basePrompt = currentVersion.Prompt;
                string improvedPrompt = basePrompt; // Start with the current prompt
                StringBuilder improvementSummary = new StringBuilder("Added instructions for: ");
                bool improvementsAdded = false;

                // Add specific instructions for weak areas
                foreach (var area in weakAreas)
                {
                    string instruction = GetInstructionForTaskType(area); // Use generalized instructions
                    if (!string.IsNullOrEmpty(instruction) && !improvedPrompt.Contains(instruction)) // Avoid duplicate instructions
                    {
                        improvedPrompt += $"\n\n# Improvement Focus: {area}\n{instruction}";
                        improvementSummary.Append($"{area}, ");
                        improvementsAdded = true;
                    }
                }

                // Add a general encouragement for clarity if not already present
                string clarityInstruction = "Always strive for clarity and provide step-by-step reasoning where applicable.";
                if (!improvedPrompt.Contains(clarityInstruction))
                {
                    improvedPrompt += $"\n\n# General Guidance\n{clarityInstruction}";
                    improvementSummary.Append("General Clarity, ");
                    improvementsAdded = true;
                }

                if (!improvementsAdded)
                {
                    Debug.WriteLine($"No specific instructions generated for weak areas of {agentName}. Prompt not changed.");
                    return;
                }

                // Finalize summary
                string finalChangeSummary = improvementSummary.ToString().TrimEnd(',', ' ');

                // Add new version to database
                int newVersionNumber = await _agentDb.AddAgentVersionAsync(
                    agent.AgentId,
                    improvedPrompt,
                    "Performance-based refinement", // Reason
                    finalChangeSummary,            // Summary
                    "System-generated improvement based on task performance analysis.", // Comments
                    null, // Known Issues
                    "System", // Created By
                    currentVersion.PerformanceScore // Performance before change
                );

                if (newVersionNumber > currentVersion.VersionNumber)
                {
                    Debug.WriteLine($"Created improved prompt version {newVersionNumber} for {agentName}.");

                    // Update runtime agent with the new prompt
                    if (_aiManager.AgentExists(agentName))
                    {
                        // Ideally, AIManager would have an UpdatePrompt method. For now, we remove
                        // and re-create.
                        _aiManager.RemoveAgent(agentName);
                        _aiManager.CreateAgent(agentName, improvedPrompt); // Create with the NEW prompt
                        Debug.WriteLine($"Updated runtime agent {agentName} with improved prompt (Version {newVersionNumber}).");
                    }
                }
                else
                {
                    Debug.WriteLine($"Failed to create a new prompt version for {agentName}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error improving prompt for {agentName}: {ex.Message}");
            }
        }

        // Generalized instructions - adapt these as needed for non-software tasks too
        private string GetInstructionForTaskType(string taskType)
        {
            var instructions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Cognitive Functions
                ["Analysis"] = "When performing analysis: Clearly state your assumptions. Break down the subject into components. Evaluate based on evidence and provide balanced conclusions.",
                ["Generation"] = "When generating content (ideas, text, code): Aim for originality and relevance. Provide multiple options if appropriate. Ensure the output aligns with the core request.",
                ["Structuring/Planning"] = "When structuring or planning: Define clear goals and steps. Identify dependencies and potential obstacles. Ensure the structure is logical and easy to follow.",
                ["Verification"] = "When verifying or validating: Clearly define the criteria for success or compliance. Be thorough in checking against rules or requirements. Document findings clearly, noting any deviations.",
                ["Refinement"] = "When refining or improving: Identify specific areas for enhancement. Explain the rationale for changes. Ensure improvements don't negatively impact other aspects.",
                ["Summarization"] = "When summarizing: Identify and extract the most crucial points. Maintain the original meaning and context. Be concise and accurate.",
                ["DecisionMaking"] = "When making decisions: Clearly outline the options considered. State the criteria used for evaluation. Justify the final choice with supporting reasons.",
                ["Coordination"] = "When coordinating: Clearly define roles and responsibilities. Ensure effective communication flow. Identify and manage dependencies between tasks or agents.",
                // Problem Domains (Examples)
                ["ProblemSolving"] = "When solving problems: Clearly define the problem first. Explore potential root causes. Generate and evaluate multiple potential solutions before selecting one.",
                ["Conceptualization"] = "When conceptualizing: Explore the core idea from multiple angles. Define key attributes and relationships. Use analogies or examples for clarity.",
                // Software Specific (Keep if relevant, but ensure general ones exist too)
                ["Implementation"] = "For implementation tasks: Start with a clear structure before filling in details. Include comments and consider edge cases. Test your solution with example inputs.",
                ["Design"] = "For design tasks: Always consider maintainability, scalability, and user experience. Document key design decisions and trade-offs."
                // Add more task types and instructions as your system evolves
            };

            return instructions.TryGetValue(taskType, out string instruction) ? instruction : null;
        }

        /// <summary>
        /// Builds the prompt for the Chief agent to synthesize specialist outputs and decide the
        /// next step. Formats specialist input using tags.
        /// </summary>
        private string BuildSynthesisPrompt(Dictionary<string, string> specialistOutputs, string currentGoal, string summarizedContext, string? additionalInfo = null)
        {
            var prompt = new StringBuilder();

            // 1. Core Goal Info
            prompt.AppendLine($"## Overall Goal: {TruncateResponse(currentGoal, 200)}");
            prompt.AppendLine($"## Task: Synthesize Specialist Inputs & Plan Next Cognitive Step");
            prompt.AppendLine("---");

            // 2. Specialist Outputs Received (Using Tags)
            prompt.AppendLine("## Specialist Outputs Received");
            if (specialistOutputs != null && specialistOutputs.Any())
            {
                foreach (var kvp in specialistOutputs)
                {
                    // *** FORMAT USING TAGS ***
                    prompt.AppendLine($"[AGENT]{kvp.Key}[/AGENT]");
                    // Pass the full response, no truncation within the prompt itself
                    prompt.AppendLine($"[RESPONSE]{kvp.Value}[/RESPONSE]");
                    prompt.AppendLine(); // Add a newline for separation
                }
            }
            else
            {
                prompt.AppendLine("(No specialist outputs were provided for this synthesis round.)");
            }
            prompt.AppendLine("---");

            // 3. Summarized Context
            prompt.AppendLine("## Processing Context Summary");
            prompt.AppendLine(summarizedContext);
            prompt.AppendLine("---");

            // 4. Additional Info (Optional)
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                prompt.AppendLine("## Additional Information/Instructions");
                prompt.AppendLine(additionalInfo);
                prompt.AppendLine("---");
            }

            // 5. Chief's Task and Output Format Instructions (Emphasizing Synthesis & Tags)
            prompt.AppendLine("## Your Task as CHIEF");
            prompt.AppendLine("1. Analyze the specialist outputs provided above in `[AGENT]...[/AGENT][RESPONSE]...[/RESPONSE]` format.");
            prompt.AppendLine("2. **Evaluate the relevance, quality, and potential conflicts** within the specialist responses based on the 'Overall Goal' and 'Processing Context Summary'."); // Added Evaluation Instruction
            prompt.AppendLine("3. Synthesize the most valuable and relevant information into a cohesive understanding or plan."); // Synthesis Instruction
            prompt.AppendLine("4. Decide the next logical *cognitive* step required to progress towards the goal.");
            prompt.AppendLine("5. Your response MUST contain your reasoning (including synthesis and evaluation points) first, then conclude the *entire response* with **exactly one** of the following structured tag blocks. **No text should follow the chosen concluding tag block.**"); // Reiterate tag requirement
            prompt.AppendLine("   - Module Activation: Use `[ACTIVATION_DIRECTIVES]` block containing one or more `[ACTIVATE]ModuleName:Focus...[/ACTIVATE]` lines.");
            prompt.AppendLine("   - User Interaction: Use `[ACTION_ASK_USER]...[/ACTION_ASK_USER]` block containing the question.");
            prompt.AppendLine("   - Final Output: Use a descriptive tag like `[FINAL_PLAN]...[/FINAL_PLAN]`...");
            prompt.AppendLine("   - Halt: Use `[ACTION_HALT]...[/ACTION_HALT]` block containing the reason.");
            prompt.AppendLine("\nYour Synthesis, Evaluation, and Next Step Plan (followed by ONE concluding tag block):"); // Updated prompt title

            return prompt.ToString();
        }








        private void DisplayAllAgentPerformance()
        {
            if (_performanceDb == null) return;
            // Get performance summary for all agents
            var summaries = _performanceDb.GetAgentPerformanceSummary();

            if (!summaries.Any())
            {
                Debug.WriteLine("No performance data available yet.");
                return;
            }

            Debug.WriteLine("=== AGENT PERFORMANCE SUMMARY ===");
            foreach (var summary in summaries.OrderBy(s => s.AgentName).ThenBy(s => s.QuestionType))
            {
                Debug.WriteLine(
                    $"{summary.AgentName} - {summary.QuestionType}: " +
                    $"{summary.CorrectAnswers}/{summary.TotalAttempts} = " +
                    $"{summary.SuccessRate:P2} (Last updated: {summary.LastUpdated})"
                );
            }
        }

        private void DisplayAgentPerformance(string agentName)
        {
            if (_performanceDb == null) return;
            // Get performance summary for a specific agent
            var summaries = _performanceDb.GetAgentPerformanceSummary(agentName);

            if (!summaries.Any())
            {
                Debug.WriteLine($"No performance data available for agent '{agentName}'.");
                return;
            }

            Debug.WriteLine($"=== PERFORMANCE SUMMARY FOR {agentName.ToUpper()} ===");
            foreach (var summary in summaries.OrderBy(s => s.QuestionType))
            {
                Debug.WriteLine(
                    $"{summary.QuestionType}: " +
                    $"{summary.CorrectAnswers}/{summary.TotalAttempts} = " +
                    $"{summary.SuccessRate:P2}"
                );
            }
        }

        private void DisplayRecentPerformance(int count = 20)
        {
            if (_performanceDb == null) return;
            // Get recent performance details
            var details = _performanceDb.GetRecentPerformanceDetails(count);

            if (!details.Any())
            {
                Debug.WriteLine("No recent performance details available.");
                return;
            }

            Debug.WriteLine($"=== {Math.Min(count, details.Count)} MOST RECENT AGENT INTERACTIONS ===");
            foreach (var detail in details) // Already ordered by DB query
            {
                Debug.WriteLine(
                    $"[{detail.TestDateTime:yyyy-MM-dd HH:mm:ss}] {detail.AgentName} ({detail.QuestionType}): " +
                    $"{(detail.IsCorrect ? "SUCCESS" : "FAILURE")}" // Use Success/Failure for clarity?
                );
                Debug.WriteLine($"  Request: {TruncateResponse(detail.RequestData, 80)}");
                Debug.WriteLine($"  Response: {TruncateResponse(detail.ResponseData, 80)}");
                Debug.WriteLine(""); // Blank line for readability
            }
        }

        private void DisplayPerformanceStats()
        {
            if (_performanceDb == null) return;
            // Get all performance summaries
            var summaries = _performanceDb.GetAgentPerformanceSummary();

            if (!summaries.Any())
            {
                Debug.WriteLine("No performance summary data available to display stats.");
                return;
            }

            // Group by task type (using the generalized task types now)
            var byTaskType = summaries.GroupBy(s => s.QuestionType);

            StringBuilder report = new StringBuilder();
            report.AppendLine("========== AGENT PERFORMANCE REPORT ==========");
            report.AppendLine($"Generated: {DateTime.Now}");
            report.AppendLine();

            foreach (var group in byTaskType.OrderBy(g => g.Key))
            {
                report.AppendLine($"=== {group.Key.ToUpper()} TASKS ===");
                // Order agents within the task type by success rate
                foreach (var summary in group.OrderByDescending(s => s.SuccessRate).ThenBy(s => s.AgentName))
                {
                    report.AppendLine(
                        $"{summary.AgentName,-15}: " + // Align agent names
                        $"{summary.SuccessRate,7:P2} " + // Align success rate
                        $"({summary.CorrectAnswers}/{summary.TotalAttempts})"
                    );
                }
                report.AppendLine();
            }

            // Get overall performance by agent
            var byAgent = summaries.GroupBy(s => s.AgentName);

            report.AppendLine("=== OVERALL AGENT PERFORMANCE (All Tasks) ===");
            // Order agents alphabetically
            foreach (var agentGroup in byAgent.OrderBy(g => g.Key))
            {
                int totalCorrect = agentGroup.Sum(s => s.CorrectAnswers);
                int totalAttempts = agentGroup.Sum(s => s.TotalAttempts);
                double overallRate = totalAttempts > 0 ? (double)totalCorrect / totalAttempts : 0;

                report.AppendLine(
                    $"{agentGroup.Key,-15}: " + // Align agent names
                    $"{overallRate,7:P2} " + // Align success rate
                    $"({totalCorrect}/{totalAttempts})"
                );
            }

            // Display or save the report
            string reportText = report.ToString();
            Debug.WriteLine(reportText); // Log to Debug Output

            // Optionally display in UI or save ShowReportInDialog("Agent Performance Statistics", reportText);
            File.WriteAllText("agent_performance_stats_report.txt", reportText);
            Debug.WriteLine("Performance stats report saved to agent_performance_stats_report.txt");
        }

        private void GenerateAndShowPerformanceReport() // Renamed from GeneratePerformanceReport
        {
            if (_performanceDb == null) return;
            var summaries = _performanceDb.GetAgentPerformanceSummary();

            if (!summaries.Any())
            {
                Debug.WriteLine("No performance data available to generate summary report.");
                MessageBox.Show("No performance data available yet.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("=============== AGENT PERFORMANCE SUMMARY ===============");
            foreach (var summary in summaries.OrderBy(s => s.AgentName).ThenBy(s => s.QuestionType))
            {
                report.AppendLine($"{summary.AgentName} - {summary.QuestionType}: " +
                               $"{summary.CorrectAnswers}/{summary.TotalAttempts} = " +
                               $"{summary.SuccessRate:P2}");
            }
            report.AppendLine("=========================================================");
            Debug.WriteLine(report.ToString());
            ShowReportInDialog("Performance Summary", report.ToString());
        }

        private void ShowPerformanceReportInUI() // Renamed from ShowPerformanceReport
        {
            if (_integration == null) return;
            string report = _integration.GeneratePerformanceReport();
            ShowReportInDialog("Cognitive System Performance Report", report); // Use helper dialog
                                                                               // Optionally save to file System.IO.File.WriteAllText("cognitive_system_report.txt", report);
        }

        // Optional: Re-initialize runtime agents after updating DB prompts
        public async Task ForceUpdateBasePromptsAsync(AgentDatabase db)
        {
            if (db == null) return;
            Debug.WriteLine("Forcing update of agent base prompts...");

            var agentDefs = new Dictionary<string, string>
            {
                { "CHIEF", AgentPromptImporter.ChiefBasePrompt }, // Use the NEW constants here
                { "EVALUATOR", AgentPromptImporter.EvaluatorBasePrompt },
                { "SENTINEL", AgentPromptImporter.SentinelBasePrompt },
                { "NAVIGATOR", AgentPromptImporter.NavigatorBasePrompt },
                { "INNOVATOR", AgentPromptImporter.InnovatorBasePrompt },
                { "STRATEGIST", AgentPromptImporter.StrategistBasePrompt },
                { "CODER", AgentPromptImporter.CoderBasePrompt }
            };

            var allAgents = await db.GetAllAgentsAsync();

            foreach (var agentDef in agentDefs)
            {
                string agentName = agentDef.Key;
                string newBasePrompt = agentDef.Value;

                var agent = allAgents.FirstOrDefault(a => a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));
                if (agent == null)
                {
                    Debug.WriteLine($"Agent '{agentName}' not found for forced update.");
                    continue;
                }

                var currentVersion = await db.GetCurrentAgentVersionAsync(agent.AgentId);

                if (currentVersion == null || currentVersion.Prompt != newBasePrompt)
                {
                    Debug.WriteLine($"Forcing prompt update for {agentName}...");
                    int newVersionNumber = await db.AddAgentVersionAsync(
                       agent.AgentId,
                       newBasePrompt,
                       "Forced Generalized Role Update",
                       "Forcibly updated base prompt to reflect generalized role.",
                       null, null, "SystemForceUpdate", currentVersion?.PerformanceScore ?? 0.0f
                   );
                    Debug.WriteLine($"Added forced prompt version {newVersionNumber} for {agentName}");
                }
                else
                {
                    Debug.WriteLine($"Prompt for {agentName} is already up-to-date.");
                }
            }
            Debug.WriteLine("Forced prompt update complete.");
        }

        private void ResetDatabaseWithConfirmation()
        {
            if (_performanceDb == null) return;
            // Ask for confirmation
            DialogResult result = MessageBox.Show(
                "Are you sure you want to reset the performance database? All historical performance data will be lost.",
                "Confirm Database Reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result == DialogResult.Yes)
            {
                try
                {
                    _performanceDb.ResetDatabase(); // Resets PerformanceSummary and AgentPerformance tables
                    Debug.WriteLine("Performance Database has been reset successfully.");
                    MessageBox.Show("Performance Database has been reset.", "Reset Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    // Optionally clear the in-memory tracker if you decide to keep it _performanceTracker?.Clear();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error resetting performance database: {ex.Message}");
                    MessageBox.Show(
                        $"Error resetting performance database: {ex.Message}",
                        "Database Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        private void VoteForResponse(string requestData, string agentName)
        {
            if (_aiManager == null) return;
            if (_aiManager.Responses.AddVote(requestData, agentName))
            {
                Debug.WriteLine($"Vote added for {agentName}'s response to '{TruncateResponse(requestData, 50)}'");

                // Check if we have a winner
                var winner = _aiManager.Responses.GetWinningResponse(requestData);
                if (winner != null)
                {
                    Debug.WriteLine($"Current winning response is from {winner.AgentName} with {winner.Votes} votes.");
                }
            }
            else
            {
                Debug.WriteLine($"Failed to add vote for {agentName} (response likely not found).");
            }
        }


        /// <summary>
        /// Validates if the extracted module name corresponds to a known, existing agent currently
        /// loaded in the AIManager.
        /// </summary>
        /// <param name="moduleName">The name to validate.</param>
        /// <returns>True if the agent name is valid and exists at runtime, false otherwise.</returns>
        private bool IsValidModule(string moduleName)
        {
            // Check against agents currently managed by AIManager
            if (_aiManager != null && !string.IsNullOrWhiteSpace(moduleName))
            {
                bool exists = _aiManager.AgentExists(moduleName);
                if (!exists)
                {
                    Debug.WriteLine($"[IsValidModule Check] Module '{moduleName}' not found in AIManager's current list.");
                }
                return exists;
            }
            Debug.WriteLine($"[IsValidModule Check] AIManager is null or moduleName is empty. Returning false.");
            return false; // Cannot validate if AIManager is not available or name is empty
        }

        private void LogProcessingRecord(string sourceModule, string outputContent)
        {
            var record = new ProcessingRecord { Timestamp = DateTime.Now, SourceModule = sourceModule, OutputContent = outputContent };
            _processingHistory.Add(record);

            // Update UI (ensure thread-safe)
            InvokeIfNeeded(() =>
            {
                // --- TODO: Replace with your actual UI logging ---
                // Example: Add to a ListBox named lstProcessingLog string logEntry =
                // $"[{record.Timestamp:HH:mm:ss}] {record.SourceModule}:
                // {TruncateResponse(record.OutputContent, 150)}";
                // lstProcessingLog.Items.Add(logEntry); if (lstProcessingLog.Items.Count > 0) {
                // lstProcessingLog.TopIndex = lstProcessingLog.Items.Count - 1; }
                Debug.WriteLine($"PROCESS> [{record.SourceModule}] {record.OutputContent}"); // Log to debug
                                                                                             // --- End
                                                                                             // UI
                                                                                             // Logging
                                                                                             // Example ---
            });
        }

        private void SetUIBusy(bool busy, string statusText = "")
        {
            InvokeIfNeeded(() =>
            {
                // --- TODO: Replace with your actual UI controls --- Example: btnStartTask.Enabled
                // = !busy; // Assuming a start button btnCancelTask.Enabled = busy; // Assuming a
                // cancel button toolStripStatusLabel1.Text = statusText;
                this.Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
                // --- End UI Control Example ---
                Debug.WriteLine($"UI State: Busy={busy}, Status='{statusText}'");
            });
        }

        private void HandleProcessingError(string errorMessage, bool showMessageBox = true)
        {
            Debug.WriteLine($"[Controller ERROR] {errorMessage}");
            var previousState = _currentProcessingState;
            _currentProcessingState = ProcessingState.Error;

            SetUIBusy(false, $"Error: {TruncateResponse(errorMessage, 100)}");

            if (showMessageBox && previousState != ProcessingState.Error)
            {
                InvokeIfNeeded(() =>
                {
                    MessageBox.Show(this, errorMessage, "Processing Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
            // Reset state for next independent operation
            _chiefClarificationAttempts = 0;
            _lastChiefErrorResponse = string.Empty;
        }

        // Helper method to show long text in a scrollable dialog
        private void ShowReportInDialog(string title, string content)
        {
            using (Form reportForm = new Form())
            {
                reportForm.Text = title;
                reportForm.Size = new System.Drawing.Size(600, 400);
                TextBox reportTextBox = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    Font = new System.Drawing.Font("Consolas", 9f), // Monospaced font
                    Text = content
                };
                reportForm.Controls.Add(reportTextBox);
                reportForm.ShowDialog();
            }
        }

        // Truncates string for display, adds ellipsis
        private string TruncateResponse(string? response, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(response)) return string.Empty;
            return response.Length <= maxLength ? response : response.Substring(0, maxLength) + "...";
        }

        private void InvokeIfNeeded(Action action)
        {
            ThreadHelper.InvokeOnUIThread(this, action);
        }

        private async Task<Task> InvokeOnUIAsync(Func<Task> asyncAction)
        {
            return ThreadHelper.InvokeOnUIThreadAsync(this, asyncAction);
        }
    }
}