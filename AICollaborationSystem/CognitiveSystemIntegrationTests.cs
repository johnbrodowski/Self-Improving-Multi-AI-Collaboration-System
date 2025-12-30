using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AnthropicApp.Tests
{
    /// <summary>
    /// Unit tests for the CognitiveSystemIntegration class.
    /// Tests agent creation from database, task type classification, and event handling.
    /// </summary>
    public class CognitiveSystemIntegrationTests : IDisposable
    {
        private readonly string _testDbPath = "test_integration.db";
        private AICollaborationSystem.AIManager _manager;
        private AICollaborationSystem.AgentDatabase _agentDb;
        private AICollaborationSystem.PerformanceDatabase _perfDb;
        private AICollaborationSystem.CognitiveSystemIntegration _integration;

        public CognitiveSystemIntegrationTests()
        {
            // Clean up any existing test database
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);

            _manager = new AICollaborationSystem.AIManager();
            _agentDb = new AICollaborationSystem.AgentDatabase(_testDbPath);
            _agentDb.Initialize();
            _perfDb = new AICollaborationSystem.PerformanceDatabase();
            _integration = new AICollaborationSystem.CognitiveSystemIntegration(_manager, _agentDb, _perfDb);
        }

        /// <summary>
        /// Runs all CognitiveSystemIntegration tests.
        /// </summary>
        public async Task RunAllTestsAsync()
        {
            Debug.WriteLine("=== RUNNING COGNITIVE SYSTEM INTEGRATION TESTS ===");

            try
            {
                await TestCreateAgentFromDatabaseAsync();
                await TestCreateAgentFromDatabase_NotFoundAsync();
                await TestTryCreateAgentFromDatabaseAsync();
                TestTaskTypeClassification();
                TestCollaborationSystemConfig();

                Debug.WriteLine("=== ALL COGNITIVE SYSTEM INTEGRATION TESTS PASSED ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TEST FAILED: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tests creating an agent from database records.
        /// </summary>
        public async Task TestCreateAgentFromDatabaseAsync()
        {
            Debug.WriteLine("Testing CreateAgentFromDatabaseAsync...");

            // Add a test agent to the database
            int agentId = await _agentDb.AddAgentAsync(
                "TestIntegrationAgent",
                "Test agent for integration testing",
                "You are a test agent for integration testing."
            );

            // Create the agent from database
            var agent = await _integration.CreateAgentFromDatabaseAsync(agentId);

            AssertNotNull(agent, "Agent should be created from database");
            AssertEquals("TestIntegrationAgent", agent.Name, "Agent name should match database record");
            AssertTrue(_manager.AgentExists("TestIntegrationAgent"), "Agent should exist in manager");

            Debug.WriteLine("✓ CreateAgentFromDatabaseAsync test passed");
        }

        /// <summary>
        /// Tests that CreateAgentFromDatabaseAsync throws for non-existent agents.
        /// </summary>
        public async Task TestCreateAgentFromDatabase_NotFoundAsync()
        {
            Debug.WriteLine("Testing CreateAgentFromDatabaseAsync with non-existent ID...");

            bool exceptionThrown = false;
            try
            {
                await _integration.CreateAgentFromDatabaseAsync(99999);
            }
            catch (InvalidOperationException ex)
            {
                exceptionThrown = true;
                AssertTrue(ex.Message.Contains("not found"), "Exception message should indicate agent not found");
            }

            AssertTrue(exceptionThrown, "Should throw InvalidOperationException for non-existent agent");

            Debug.WriteLine("✓ CreateAgentFromDatabaseAsync not found test passed");
        }

        /// <summary>
        /// Tests the TryCreateAgentFromDatabaseAsync method returns null on failure.
        /// </summary>
        public async Task TestTryCreateAgentFromDatabaseAsync()
        {
            Debug.WriteLine("Testing TryCreateAgentFromDatabaseAsync...");

            var result = await _integration.TryCreateAgentFromDatabaseAsync(99999);
            AssertNull(result, "Should return null for non-existent agent");

            Debug.WriteLine("✓ TryCreateAgentFromDatabaseAsync test passed");
        }

        /// <summary>
        /// Tests the TaskTypeClassifier correctly classifies different task types.
        /// </summary>
        public void TestTaskTypeClassification()
        {
            Debug.WriteLine("Testing TaskTypeClassifier...");

            // Test Implementation tasks
            AssertEquals(AICollaborationSystem.TaskType.Implementation,
                AICollaborationSystem.TaskTypeClassifier.Classify("create a new feature"),
                "Should classify 'create' as Implementation");

            AssertEquals(AICollaborationSystem.TaskType.Implementation,
                AICollaborationSystem.TaskTypeClassifier.Classify("generate code for login"),
                "Should classify 'generate' as Implementation");

            // Test Analysis tasks
            AssertEquals(AICollaborationSystem.TaskType.Analysis,
                AICollaborationSystem.TaskTypeClassifier.Classify("analyze the performance"),
                "Should classify 'analyze' as Analysis");

            AssertEquals(AICollaborationSystem.TaskType.Analysis,
                AICollaborationSystem.TaskTypeClassifier.Classify("evaluate the approach"),
                "Should classify 'evaluate' as Analysis");

            // Test Design tasks
            AssertEquals(AICollaborationSystem.TaskType.Design,
                AICollaborationSystem.TaskTypeClassifier.Classify("design a new architecture"),
                "Should classify 'design' as Design");

            // Test Testing tasks
            AssertEquals(AICollaborationSystem.TaskType.Testing,
                AICollaborationSystem.TaskTypeClassifier.Classify("test the functionality"),
                "Should classify 'test' as Testing");

            AssertEquals(AICollaborationSystem.TaskType.Testing,
                AICollaborationSystem.TaskTypeClassifier.Classify("verify the implementation"),
                "Should classify 'verify' as Testing");

            // Test Optimization tasks
            AssertEquals(AICollaborationSystem.TaskType.Optimization,
                AICollaborationSystem.TaskTypeClassifier.Classify("improve the performance"),
                "Should classify 'improve' as Optimization");

            AssertEquals(AICollaborationSystem.TaskType.Optimization,
                AICollaborationSystem.TaskTypeClassifier.Classify("refactor the code"),
                "Should classify 'refactor' as Optimization");

            // Test Math operations
            AssertEquals(AICollaborationSystem.TaskType.Addition,
                AICollaborationSystem.TaskTypeClassifier.Classify("what is 5 + 3"),
                "Should classify '+' as Addition");

            AssertEquals(AICollaborationSystem.TaskType.Multiplication,
                AICollaborationSystem.TaskTypeClassifier.Classify("calculate 5 * 3"),
                "Should classify '*' as Multiplication");

            // Test General fallback
            AssertEquals(AICollaborationSystem.TaskType.General,
                AICollaborationSystem.TaskTypeClassifier.Classify("random unrelated text"),
                "Should classify unknown text as General");

            // Test ToStorageString
            AssertEquals("Implementation",
                AICollaborationSystem.TaskTypeClassifier.ToStorageString(AICollaborationSystem.TaskType.Implementation),
                "ToStorageString should return correct string");

            Debug.WriteLine("✓ TaskTypeClassifier test passed");
        }

        /// <summary>
        /// Tests the CollaborationSystemConfig validation and defaults.
        /// </summary>
        public void TestCollaborationSystemConfig()
        {
            Debug.WriteLine("Testing CollaborationSystemConfig...");

            // Test default config
            var config = AICollaborationSystem.CollaborationSystemConfig.Default;
            AssertNotNull(config, "Default config should not be null");
            AssertEquals("cognitive_agents.db", config.DatabasePath, "Default database path");
            AssertEquals(90, config.MetricsRetentionDays, "Default metrics retention days");
            AssertEquals(8192, config.MaxTokens, "Default max tokens");

            // Test custom config validation
            var customConfig = new AICollaborationSystem.CollaborationSystemConfig
            {
                DatabasePath = "custom.db",
                MetricsRetentionDays = 30,
                MaxTokens = 4096,
                PromptRefinementThreshold = 0.9,
                StrongPerformanceThreshold = 0.85,
                WeakPerformanceThreshold = 0.5
            };

            // Should not throw
            customConfig.Validate();

            // Test invalid config
            var invalidConfig = new AICollaborationSystem.CollaborationSystemConfig
            {
                MetricsRetentionDays = 0 // Invalid
            };

            bool validationFailed = false;
            try
            {
                invalidConfig.Validate();
            }
            catch (InvalidOperationException)
            {
                validationFailed = true;
            }
            AssertTrue(validationFailed, "Validation should fail for invalid config");

            Debug.WriteLine("✓ CollaborationSystemConfig test passed");
        }

        #region Assertion Helpers

        private void AssertEquals<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new Exception($"Assertion failed: {message}. Expected: {expected}, Actual: {actual}");
        }

        private void AssertTrue(bool condition, string message)
        {
            if (!condition)
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

        #endregion

        public void Dispose()
        {
            _manager?.Dispose();

            // Clean up test database
            if (File.Exists(_testDbPath))
            {
                try { File.Delete(_testDbPath); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }
}
