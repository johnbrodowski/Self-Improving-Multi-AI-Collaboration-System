using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AnthropicApp.AICollaborationSystem;


namespace AnthropicApp.Tests
{
    /// <summary>
    /// Class for testing and demonstrating the AgentDatabase functionality.
    /// This can be used to verify all aspects of the database are working correctly.
    /// </summary>
    public class AgentDatabaseTests
    {
        private readonly string _dbPath;
        private AgentDatabase _db;

        /// <summary>
        /// Initializes a new instance of the AgentDatabaseTests class.
        /// </summary>
        /// <param name="dbPath">Path to the test database file. Default is "test_agent_database.db".</param>
        public AgentDatabaseTests(string dbPath = "test_agent_database.db")
        {
            _dbPath = dbPath;

            // Delete the test database if it exists
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }

            _db = new AgentDatabase(_dbPath);
            _db.Initialize();
        }

        /// <summary>
        /// Runs a comprehensive test of all database functionality.
        /// </summary>
        public async Task RunAllTestsAsync()
        {
            Debug.WriteLine("=== RUNNING ALL DATABASE TESTS ===");

            try
            {
                await TestAgentManagementAsync();
                await TestVersioningAsync();
                await TestPerformanceTrackingAsync();
                await TestCapabilitiesAsync();
                await TestTeamManagementAsync();
                await TestReportingAsync();

                Debug.WriteLine("\n✅ ALL TESTS COMPLETED SUCCESSFULLY");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"\n❌ TEST FAILED: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// Tests the agent management functionality.
        /// </summary>
        public async Task TestAgentManagementAsync()
        {
            Debug.WriteLine("\n=== TESTING AGENT MANAGEMENT ===");

            // Add new agents
            Debug.WriteLine("Adding agents...");
            int chiefId = await _db.AddAgentAsync(
                "Chief",
                "Orchestrates and makes final decisions",
                "You are the Chief agent responsible for orchestrating the collaboration between specialized cognitive modules. Your role is to analyze requests, delegate tasks, and synthesize responses into coherent solutions.",
                "Central coordinator of the cognitive collaboration system",
                "May struggle with domain-specific details without specialists"
            );

            int sentinelId = await _db.AddAgentAsync(
                "Sentinel",
                "Enforces rules and monitors compliance",
                "You are the Sentinel agent responsible for ensuring compliance with guidelines and rules. Your role is to identify potential issues, enforce constraints, and maintain the integrity of solutions."
            );

            int evaluatorId = await _db.AddAgentAsync(
                "Evaluator",
                "Provides analytical assessment",
                "You are the Evaluator agent responsible for critical analysis. Your role is to assess proposals, identify weaknesses, and suggest improvements based on objective criteria."
            );

            Debug.WriteLine($"Added agents with IDs: {chiefId}, {sentinelId}, {evaluatorId}");

            // Get agent by ID
            var chief = await _db.GetAgentAsync(chiefId);
            Debug.WriteLine($"Retrieved agent: {chief.Name} (Purpose: {chief.Purpose})");

            // Update agent
            bool updateResult = await _db.UpdateAgentAsync(
                chiefId,
                purpose: "Orchestrates team collaboration and makes final decisions"
            );
            Debug.WriteLine($"Updated agent purpose: {updateResult}");

            // Get all agents
            var allAgents = await _db.GetAllAgentsAsync();
            Debug.WriteLine($"Total agents: {allAgents.Count}");
            foreach (var agent in allAgents)
            {
                Debug.WriteLine($"- {agent.Name}: {agent.Purpose}");
            }

            Debug.WriteLine("✅ Agent management tests passed");
        }

        /// <summary>
        /// Tests the agent versioning functionality.
        /// </summary>
        public async Task TestVersioningAsync()
        {
            Debug.WriteLine("\n=== TESTING AGENT VERSIONING ===");

            // Get the first agent
            var agents = await _db.GetAllAgentsAsync();
            var agent = agents.First();

            // Get current version
            var currentVersion = await _db.GetCurrentAgentVersionAsync(agent.AgentId);
            Debug.WriteLine($"Current version for {agent.Name}: {currentVersion.VersionNumber}");
            Debug.WriteLine($"Current prompt: {currentVersion.Prompt.Substring(0, 50)}...");

            // Add new version
            int newVersionNumber = await _db.AddAgentVersionAsync(
                agent.AgentId,
                currentVersion.Prompt + "\n\nAdditional instruction: Pay special attention to the context and maintain consistency across responses.",
                "Improving context awareness",
                "Added instruction to maintain contextual consistency",
                "Testing version update",
                "None at this time",
                "Test User",
                currentVersion.PerformanceScore
            );

            Debug.WriteLine($"Added new version: {newVersionNumber}");

            // Get version history
            var versionHistory = await _db.GetAgentVersionHistoryAsync(agent.AgentId);
            Debug.WriteLine($"Version history count: {versionHistory.Count}");
            foreach (var version in versionHistory)
            {
                Debug.WriteLine($"- Version {version.VersionNumber}: Created {version.CreatedDate}, Score: {version.PerformanceScore:F2}");
            }

            // Update version performance
            bool updateResult = await _db.UpdateVersionPerformanceScoreAsync(
                versionHistory.First().VersionId,
                0.85f
            );
            Debug.WriteLine($"Updated version performance: {updateResult}");

            Debug.WriteLine("✅ Agent versioning tests passed");
        }

        /// <summary>
        /// Tests the performance tracking functionality.
        /// </summary>
        public async Task TestPerformanceTrackingAsync()
        {
            Debug.WriteLine("\n=== TESTING PERFORMANCE TRACKING ===");

            // Get the first agent
            var agents = await _db.GetAllAgentsAsync();
            var agent = agents.First();

            // Record interactions
            Debug.WriteLine("Recording interactions...");

            // Successful interaction
            int interaction1Id = await _db.RecordInteractionAsync(
                agent.AgentId,
                "Addition",
                "What is 2 + 2?",
                "The answer is 4.",
                true,
                0.8f,
                "Perfect response"
            );

            // Another successful interaction
            int interaction2Id = await _db.RecordInteractionAsync(
                agent.AgentId,
                "Addition",
                "What is 5 + 7?",
                "5 + 7 = 12",
                true,
                0.9f
            );

            // Failed interaction
            int interaction3Id = await _db.RecordInteractionAsync(
                agent.AgentId,
                "Multiplication",
                "What is 6 * 7?",
                "The answer is 36.",
                false,
                1.2f,
                "Incorrect response"
            );

            Debug.WriteLine($"Recorded interactions with IDs: {interaction1Id}, {interaction2Id}, {interaction3Id}");

            // Get recent interactions
            var recentInteractions = await _db.GetRecentInteractionsAsync(agent.AgentId);
            
            
            Debug.WriteLine($"Recent interactions: {recentInteractions.Count}");


            foreach (var interaction in recentInteractions)
            {
                Debug.WriteLine($"- {interaction.TaskType}: {(interaction.IsCorrect ? "Correct" : "Incorrect")}, Time: {interaction.ProcessingTime:F2}s");
            }

            // Get performance stats
            var performanceStats = await _db.GetAgentPerformanceStatsAsync(agent.AgentId);
            Debug.WriteLine("Performance stats:");
            foreach (var stat in performanceStats)
            {
                Debug.WriteLine($"- {stat.TaskType}: {stat.SuccessRate:P2} ({stat.CorrectResponses}/{stat.TotalAttempts})");
            }

            Debug.WriteLine("✅ Performance tracking tests passed");
        }

        /// <summary>
        /// Tests the agent capabilities functionality.
        /// </summary>
        public async Task TestCapabilitiesAsync()
        {
            Debug.WriteLine("\n=== TESTING AGENT CAPABILITIES ===");

            // Get the first agent
            var agents = await _db.GetAllAgentsAsync();
            var agent = agents.First();

            // Add capabilities
            Debug.WriteLine("Adding capabilities...");
            int capability1Id = await _db.AddAgentCapabilityAsync(
                agent.AgentId,
                "Planning",
                "Ability to create organized, step-by-step plans",
                0.8f
            );

            int capability2Id = await _db.AddAgentCapabilityAsync(
                agent.AgentId,
                "Decision Making",
                "Ability to make sound decisions based on available information",
                0.75f
            );

            Debug.WriteLine($"Added capabilities with IDs: {capability1Id}, {capability2Id}");

            // Update capability rating
            bool updateResult = await _db.UpdateCapabilityRatingAsync(
                capability1Id,
                0.85f
            );
            Debug.WriteLine($"Updated capability rating: {updateResult}");

            // Get agent capabilities
            var capabilities = await _db.GetAgentCapabilitiesAsync(agent.AgentId);
            Debug.WriteLine($"Agent capabilities: {capabilities.Count}");
            foreach (var capability in capabilities)
            {
                Debug.WriteLine($"- {capability.CapabilityName}: {capability.PerformanceRating:F2} ({capability.CapabilityDescription})");
            }

            Debug.WriteLine("✅ Agent capabilities tests passed");
        }

        /// <summary>
        /// Tests the team management functionality.
        /// </summary>
        public async Task TestTeamManagementAsync()
        {
            Debug.WriteLine("\n=== TESTING TEAM MANAGEMENT ===");

            // Get agents
            var agents = await _db.GetAllAgentsAsync();
            var chief = agents.First(a => a.Name == "Chief");
            var sentinel = agents.First(a => a.Name == "Sentinel");
            var evaluator = agents.First(a => a.Name == "Evaluator");

            // Create team
            Debug.WriteLine("Creating team...");
            int teamId = await _db.CreateTeamAsync(
                "Core Analysis Team",
                chief.AgentId,
                "Team responsible for analyzing complex problems"
            );
            Debug.WriteLine($"Created team with ID: {teamId}");

            // Add members to team
            await _db.AddAgentToTeamAsync(
                teamId,
                sentinel.AgentId,
                "Security Advisor",
                "Responsible for identifying security concerns"
            );

            await _db.AddAgentToTeamAsync(
                teamId,
                evaluator.AgentId,
                "Analytical Expert",
                "Responsible for critical analysis of proposals"
            );

            Debug.WriteLine("Added members to team");

            // Update team member performance
            await _db.UpdateTeamMemberPerformanceAsync(
                teamId,
                sentinel.AgentId,
                0.82f
            );

            await _db.UpdateTeamMemberPerformanceAsync(
                teamId,
                evaluator.AgentId,
                0.78f
            );

            Debug.WriteLine("Updated team member performance");

            // Get team details
            var team = await _db.GetTeamAsync(teamId);
            Debug.WriteLine($"Team: {team.TeamName} (Score: {team.PerformanceScore:F2})");
            Debug.WriteLine("Team members:");
            foreach (var member in team.Members)
            {
                Debug.WriteLine($"- {member.AgentName} ({member.Role}): {member.PerformanceInTeam:F2}");
            }

            // Get all teams
            var teams = await _db.GetAllTeamsAsync();
            Debug.WriteLine($"Total teams: {teams.Count}");

            Debug.WriteLine("✅ Team management tests passed");
        }

        /// <summary>
        /// Tests the reporting and analysis functionality.
        /// </summary>
        public async Task TestReportingAsync()
        {
            Debug.WriteLine("\n=== TESTING REPORTING AND ANALYSIS ===");

            // Get the first agent
            var agents = await _db.GetAllAgentsAsync();
            var agent = agents.First();

            // Generate performance report
            Debug.WriteLine("Generating performance report...");
            string report = await _db.GenerateAgentPerformanceReportAsync(agent.AgentId);
            Debug.WriteLine("Performance report excerpt:");
            string[] reportLines = report.Split('\n');
            for (int i = 0; i < Math.Min(10, reportLines.Length); i++)
            {
                Debug.WriteLine(reportLines[i]);
            }
            Debug.WriteLine("...");

            // Find agents for task
            var agentsForTask = await _db.FindAgentsForTaskAsync("Addition", 0.5f, 1);
            Debug.WriteLine($"Agents good at Addition: {agentsForTask.Count}");
            foreach (var result in agentsForTask)
            {
                Debug.WriteLine($"- {result.Agent.Name}: {result.SuccessRate:P2}");
            }

            // Analyze prompt improvements
            var promptImprovements = await _db.AnalyzePromptImprovementsAsync(agent.AgentId);
            Debug.WriteLine($"Prompt improvements: {promptImprovements.Count}");
            foreach (var improvement in promptImprovements)
            {
                Debug.WriteLine($"- Version {improvement.VersionNumber}: {improvement.ChangeSummary}");
                Debug.WriteLine($"  Impact: {improvement.PerformanceBeforeChange:F2} -> {improvement.PerformanceAfterChange:F2} ({improvement.PerformanceImprovement:F2})");
            }

            Debug.WriteLine("✅ Reporting and analysis tests passed");
        }



































    }

 
}