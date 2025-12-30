using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AnthropicApp.Tests
{
    /// <summary>
    /// Unit tests for the AIManager class.
    /// Tests agent lifecycle management, event handling, and request processing.
    /// </summary>
    public class AIManagerTests : IDisposable
    {
        private AICollaborationSystem.AIManager _manager;
        private readonly List<string> _eventLog = new();

        public AIManagerTests()
        {
            _manager = new AICollaborationSystem.AIManager();
            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            _manager.AgentAdded += (s, e) => _eventLog.Add($"AgentAdded:{e.AgentName}");
            _manager.AgentRemoved += (s, e) => _eventLog.Add($"AgentRemoved:{e.AgentName}");
            _manager.AgentError += (s, e) => _eventLog.Add($"AgentError:{e.AgentName}:{e.Exception.Message}");
            _manager.AgentCompleted += (s, e) => _eventLog.Add($"AgentCompleted:{e.AgentName}:{e.Success}");
        }

        /// <summary>
        /// Runs all AIManager tests.
        /// </summary>
        public async Task RunAllTestsAsync()
        {
            Debug.WriteLine("=== RUNNING AIMANAGER TESTS ===");

            try
            {
                TestAgentCreation();
                TestAgentExists();
                TestAgentRetrieval();
                TestDuplicateAgentCreation();
                TestAgentRemoval();
                TestGetAgentNames();
                await TestCancelAllRequestsAsync();

                Debug.WriteLine("=== ALL AIMANAGER TESTS PASSED ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TEST FAILED: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tests that agents can be created successfully.
        /// </summary>
        public void TestAgentCreation()
        {
            Debug.WriteLine("Testing agent creation...");
            _eventLog.Clear();

            var agent = _manager.CreateAgent("TestAgent1", "You are a test agent.");

            AssertNotNull(agent, "Created agent should not be null");
            AssertEquals("TestAgent1", agent.Name, "Agent name should match");
            AssertContains(_eventLog, "AgentAdded:TestAgent1", "AgentAdded event should fire");

            Debug.WriteLine("✓ Agent creation test passed");
        }

        /// <summary>
        /// Tests the AgentExists method.
        /// </summary>
        public void TestAgentExists()
        {
            Debug.WriteLine("Testing AgentExists...");

            // TestAgent1 was created in previous test
            AssertTrue(_manager.AgentExists("TestAgent1"), "Should find existing agent");
            AssertFalse(_manager.AgentExists("NonExistentAgent"), "Should not find non-existent agent");

            Debug.WriteLine("✓ AgentExists test passed");
        }

        /// <summary>
        /// Tests retrieving an existing agent.
        /// </summary>
        public void TestAgentRetrieval()
        {
            Debug.WriteLine("Testing agent retrieval...");

            var agent = _manager.GetAgent("TestAgent1");
            AssertNotNull(agent, "Should retrieve existing agent");
            AssertEquals("TestAgent1", agent.Name, "Retrieved agent name should match");

            var nonExistent = _manager.GetAgent("NonExistentAgent");
            AssertNull(nonExistent, "Should return null for non-existent agent");

            Debug.WriteLine("✓ Agent retrieval test passed");
        }

        /// <summary>
        /// Tests that creating a duplicate agent throws an exception.
        /// </summary>
        public void TestDuplicateAgentCreation()
        {
            Debug.WriteLine("Testing duplicate agent creation...");

            bool exceptionThrown = false;
            try
            {
                _manager.CreateAgent("TestAgent1", "Duplicate prompt");
            }
            catch (InvalidOperationException)
            {
                exceptionThrown = true;
            }

            AssertTrue(exceptionThrown, "Should throw exception for duplicate agent name");

            Debug.WriteLine("✓ Duplicate agent creation test passed");
        }

        /// <summary>
        /// Tests agent removal.
        /// </summary>
        public void TestAgentRemoval()
        {
            Debug.WriteLine("Testing agent removal...");
            _eventLog.Clear();

            // Create a temporary agent to remove
            _manager.CreateAgent("TempAgent", "Temporary agent");
            _eventLog.Clear();

            bool removed = _manager.RemoveAgent("TempAgent");
            AssertTrue(removed, "Should successfully remove existing agent");
            AssertFalse(_manager.AgentExists("TempAgent"), "Removed agent should no longer exist");
            AssertContains(_eventLog, "AgentRemoved:TempAgent", "AgentRemoved event should fire");

            bool removedAgain = _manager.RemoveAgent("TempAgent");
            AssertFalse(removedAgain, "Should return false when removing non-existent agent");

            Debug.WriteLine("✓ Agent removal test passed");
        }

        /// <summary>
        /// Tests GetAgentNames returns all agent names.
        /// </summary>
        public void TestGetAgentNames()
        {
            Debug.WriteLine("Testing GetAgentNames...");

            // Create additional agents
            _manager.CreateAgent("TestAgent2", "Test prompt 2");
            _manager.CreateAgent("TestAgent3", "Test prompt 3");

            var names = _manager.GetAgentNames();

            AssertTrue(names.Contains("TestAgent1"), "Should contain TestAgent1");
            AssertTrue(names.Contains("TestAgent2"), "Should contain TestAgent2");
            AssertTrue(names.Contains("TestAgent3"), "Should contain TestAgent3");

            Debug.WriteLine("✓ GetAgentNames test passed");
        }

        /// <summary>
        /// Tests the CancelAllRequests method.
        /// </summary>
        public async Task TestCancelAllRequestsAsync()
        {
            Debug.WriteLine("Testing CancelAllRequests...");

            // This test just verifies the method doesn't throw
            _manager.CancelAllRequests();

            await Task.Delay(100); // Give cancellation time to propagate

            Debug.WriteLine("✓ CancelAllRequests test passed");
        }

        #region Assertion Helpers

        private void AssertEquals<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Assertion failed: {message}. Expected: {expected}, Actual: {actual}");
        }

        private void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception($"Assertion failed: {message}");
        }

        private void AssertFalse(bool condition, string message)
        {
            if (condition)
                throw new Exception($"Assertion failed: {message}");
        }

        private void AssertNotNull(object obj, string message)
        {
            if (obj == null)
                throw new Exception($"Assertion failed: {message}");
        }

        private void AssertNull(object obj, string message)
        {
            if (obj != null)
                throw new Exception($"Assertion failed: {message}");
        }

        private void AssertContains(List<string> list, string item, string message)
        {
            if (!list.Contains(item))
                throw new Exception($"Assertion failed: {message}. Item '{item}' not found in list.");
        }

        #endregion

        public void Dispose()
        {
            _manager?.Dispose();
        }
    }
}
