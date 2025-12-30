using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AnthropicApp.Tests
{
    /// <summary>
    /// Integration tests for end-to-end multi-agent workflows.
    /// These tests verify the complete system works together correctly.
    /// Note: Some tests require a valid API key to run fully.
    /// </summary>
    public class IntegrationTests : IDisposable
    {
        private readonly string _testDbPath = "test_e2e_integration.db";
        private AICollaborationSystem.AIManager _manager;
        private AICollaborationSystem.AgentDatabase _agentDb;
        private AICollaborationSystem.PerformanceDatabase _perfDb;
        private AICollaborationSystem.CognitiveSystemIntegration _integration;
        private AICollaborationSystem.AgentPromptImporter _promptImporter;

        public IntegrationTests()
        {
            // Clean up any existing test database
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);

            _manager = new AICollaborationSystem.AIManager();
            _agentDb = new AICollaborationSystem.AgentDatabase(_testDbPath);
            _agentDb.Initialize();
            _perfDb = new AICollaborationSystem.PerformanceDatabase();
            _integration = new AICollaborationSystem.CognitiveSystemIntegration(_manager, _agentDb, _perfDb);
            _promptImporter = new AICollaborationSystem.AgentPromptImporter();
        }

        /// <summary>
        /// Runs all integration tests.
        /// </summary>
        public async Task RunAllTestsAsync()
        {
            Debug.WriteLine("=== RUNNING END-TO-END INTEGRATION TESTS ===");

            try
            {
                await TestPromptImportWorkflowAsync();
                await TestMultiAgentCreationWorkflowAsync();
                await TestResponseCollectionWorkflowAsync();
                TestPerformanceTrackingWorkflow();
                await TestPromptVersioningWorkflowAsync();

                Debug.WriteLine("=== ALL INTEGRATION TESTS PASSED ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"INTEGRATION TEST FAILED: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tests the complete workflow of importing prompts from BasePrompts folder.
        /// </summary>
        public async Task TestPromptImportWorkflowAsync()
        {
            Debug.WriteLine("Testing prompt import workflow...");

            // Import prompts from BasePrompts folder
            var agentIds = await _promptImporter.ImportCognitiveAgentPromptsAsync(_agentDb);

            // Verify all expected agents were imported
            var expectedAgents = new[] { "Chief", "Sentinel", "Evaluator", "Navigator", "Innovator", "Strategist", "Coder" };

            foreach (var agentName in expectedAgents)
            {
                AssertTrue(agentIds.ContainsKey(agentName), $"Should import {agentName} agent");
            }

            // Verify agents exist in database
            var allAgents = await _agentDb.GetAllAgentsAsync();
            AssertTrue(allAgents.Count >= expectedAgents.Length, "Database should contain all imported agents");

            Debug.WriteLine($"✓ Prompt import workflow test passed - imported {agentIds.Count} agents");
        }

        /// <summary>
        /// Tests creating multiple agents from database and verifying they're ready to use.
        /// </summary>
        public async Task TestMultiAgentCreationWorkflowAsync()
        {
            Debug.WriteLine("Testing multi-agent creation workflow...");

            // Get all agents from database
            var allAgents = await _agentDb.GetAllAgentsAsync();
            var createdCount = 0;

            foreach (var agent in allAgents.Take(3)) // Create first 3 agents
            {
                var runtimeAgent = await _integration.TryCreateAgentFromDatabaseAsync(agent.AgentId);
                if (runtimeAgent != null)
                {
                    createdCount++;
                    AssertTrue(_manager.AgentExists(agent.Name), $"Manager should contain {agent.Name}");
                }
            }

            AssertTrue(createdCount > 0, "Should create at least one agent from database");

            // Verify agent names are tracked
            var agentNames = _manager.GetAgentNames();
            AssertTrue(agentNames.Count >= createdCount, "Manager should track all created agents");

            Debug.WriteLine($"✓ Multi-agent creation workflow test passed - created {createdCount} agents");
        }

        /// <summary>
        /// Tests that the response collection system properly tracks responses.
        /// </summary>
        public async Task TestResponseCollectionWorkflowAsync()
        {
            Debug.WriteLine("Testing response collection workflow...");

            // Manually add some test responses to verify collection behavior
            var responses = _manager.Responses;
            var testRequest = "test_request_" + Guid.NewGuid().ToString();

            // Simulate adding responses (normally done by agent response events)
            responses.AddResponse(testRequest, "TestAgent1", "Response from agent 1");
            responses.AddResponse(testRequest, "TestAgent2", "Response from agent 2");

            // Verify responses are collected
            var collectedResponses = responses.GetResponsesForRequest(testRequest);
            AssertEquals(2, collectedResponses.Count, "Should collect 2 responses");

            // Verify response data
            var agent1Response = collectedResponses.FirstOrDefault(r => r.AgentName == "TestAgent1");
            AssertNotNull(agent1Response, "Should have response from TestAgent1");
            AssertEquals("Response from agent 1", agent1Response.ResponseData, "Response data should match");

            // Test clear
            responses.ClearResponsesForRequest(testRequest);
            var clearedResponses = responses.GetResponsesForRequest(testRequest);
            AssertEquals(0, clearedResponses.Count, "Should clear responses");

            await Task.CompletedTask; // Satisfy async signature

            Debug.WriteLine("✓ Response collection workflow test passed");
        }

        /// <summary>
        /// Tests the performance tracking workflow.
        /// </summary>
        public void TestPerformanceTrackingWorkflow()
        {
            Debug.WriteLine("Testing performance tracking workflow...");

            // Record some test performance data
            _perfDb.RecordPerformance("TestEvaluator", "Analysis", true, "test input", "test output");
            _perfDb.RecordPerformance("TestEvaluator", "Analysis", true, "test input 2", "test output 2");
            _perfDb.RecordPerformance("TestEvaluator", "Analysis", false, "test input 3", "test output 3");
            _perfDb.RecordPerformance("TestCoder", "Implementation", true, "test input", "test output");

            // Get performance summary
            var summaries = _perfDb.GetAgentPerformanceSummary();

            // Find TestEvaluator's Analysis performance
            var evaluatorAnalysis = summaries.FirstOrDefault(s =>
                s.AgentName == "TestEvaluator" && s.QuestionType == "Analysis");

            AssertNotNull(evaluatorAnalysis, "Should have performance data for TestEvaluator");
            AssertEquals(3, evaluatorAnalysis.TotalAttempts, "Should have 3 total attempts");
            AssertEquals(2, evaluatorAnalysis.CorrectAnswers, "Should have 2 correct answers");

            // Generate report
            var report = _integration.GeneratePerformanceReport();
            AssertTrue(report.Contains("COGNITIVE SYSTEM PERFORMANCE REPORT"), "Report should have header");
            AssertTrue(report.Contains("TestEvaluator"), "Report should contain agent name");

            Debug.WriteLine("✓ Performance tracking workflow test passed");
        }

        /// <summary>
        /// Tests the prompt versioning workflow.
        /// </summary>
        public async Task TestPromptVersioningWorkflowAsync()
        {
            Debug.WriteLine("Testing prompt versioning workflow...");

            // Create a test agent
            int agentId = await _agentDb.AddAgentAsync(
                "VersionTestAgent",
                "Agent for testing versioning",
                "Version 1 prompt"
            );

            // Get current version
            var version1 = await _agentDb.GetCurrentAgentVersionAsync(agentId);
            AssertNotNull(version1, "Should have initial version");
            AssertEquals(1, version1.VersionNumber, "Initial version should be 1");

            // Create new version
            await _agentDb.AddAgentVersionAsync(agentId, "Version 2 prompt", "Updated for testing", "Test version upgrade");

            // Get all versions
            var allVersions = await _agentDb.GetAgentVersionHistoryAsync(agentId);
            AssertEquals(2, allVersions.Count, "Should have 2 versions");

            // Verify latest version
            var currentVersion = await _agentDb.GetCurrentAgentVersionAsync(agentId);
            AssertEquals(2, currentVersion.VersionNumber, "Current version should be 2");
            AssertEquals("Version 2 prompt", currentVersion.Prompt, "Prompt should be updated");

            Debug.WriteLine("✓ Prompt versioning workflow test passed");
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

    /// <summary>
    /// Test runner to execute all test suites.
    /// </summary>
    public static class TestRunner
    {
        /// <summary>
        /// Runs all test suites in sequence.
        /// </summary>
        public static async Task RunAllTestSuitesAsync()
        {
            Debug.WriteLine("========================================");
            Debug.WriteLine("   COLLABORATION SYSTEM TEST RUNNER");
            Debug.WriteLine("========================================");
            Debug.WriteLine("");

            var results = new List<(string Suite, bool Passed, string Error)>();

            // Run AIManager tests
            try
            {
                using var aiManagerTests = new AIManagerTests();
                await aiManagerTests.RunAllTestsAsync();
                results.Add(("AIManagerTests", true, null));
            }
            catch (Exception ex)
            {
                results.Add(("AIManagerTests", false, ex.Message));
            }

            // Run CognitiveSystemIntegration tests
            try
            {
                using var integrationTests = new CognitiveSystemIntegrationTests();
                await integrationTests.RunAllTestsAsync();
                results.Add(("CognitiveSystemIntegrationTests", true, null));
            }
            catch (Exception ex)
            {
                results.Add(("CognitiveSystemIntegrationTests", false, ex.Message));
            }

            // Run AgentDatabase tests
            try
            {
                var dbTests = new AgentDatabaseTests();
                await dbTests.RunAllTestsAsync();
                results.Add(("AgentDatabaseTests", true, null));
            }
            catch (Exception ex)
            {
                results.Add(("AgentDatabaseTests", false, ex.Message));
            }

            // Run E2E Integration tests
            try
            {
                using var e2eTests = new IntegrationTests();
                await e2eTests.RunAllTestsAsync();
                results.Add(("IntegrationTests (E2E)", true, null));
            }
            catch (Exception ex)
            {
                results.Add(("IntegrationTests (E2E)", false, ex.Message));
            }

            // Print summary
            Debug.WriteLine("");
            Debug.WriteLine("========================================");
            Debug.WriteLine("           TEST RESULTS SUMMARY");
            Debug.WriteLine("========================================");

            foreach (var (suite, passed, error) in results)
            {
                var status = passed ? "✓ PASSED" : "✗ FAILED";
                Debug.WriteLine($"{status}: {suite}");
                if (!passed && error != null)
                {
                    Debug.WriteLine($"         Error: {error}");
                }
            }

            var passedCount = results.Count(r => r.Passed);
            var totalCount = results.Count;
            Debug.WriteLine("");
            Debug.WriteLine($"Total: {passedCount}/{totalCount} test suites passed");
            Debug.WriteLine("========================================");
        }
    }
}
