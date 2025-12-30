using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AnthropicApp.AICollaborationSystem
{
    public class ComparativeAnalysis
    {
        private readonly AIManager _aiManager;
        private readonly AgentDatabase _agentDb;
        private readonly Dictionary<string, List<PerformanceDataPoint>> _performanceHistory =
            new Dictionary<string, List<PerformanceDataPoint>>();

        public ComparativeAnalysis(AIManager aiManager, AgentDatabase agentDb)
        {
            _aiManager = aiManager;
            _agentDb = agentDb;
        }

        public class PerformanceDataPoint
        {
            public DateTime Timestamp { get; set; }
            public string TaskType { get; set; }
            public double EffectivenessScore { get; set; }
            public int PromptVersion { get; set; }
        }

        public async Task RecordPerformanceDataPointAsync(string agentName, string taskType,
                                                       double effectivenessScore)
        {
            if (!_performanceHistory.ContainsKey(agentName))
            {
                _performanceHistory[agentName] = new List<PerformanceDataPoint>();
            }

            // Get current prompt version
            var agent = await GetAgentInfoAsync(agentName);
            var currentVersion = await _agentDb.GetCurrentAgentVersionAsync(agent.AgentId);

            // Add performance data point
            _performanceHistory[agentName].Add(new PerformanceDataPoint
            {
                Timestamp = DateTime.Now,
                TaskType = taskType,
                EffectivenessScore = effectivenessScore,
                PromptVersion = currentVersion?.VersionNumber ?? 0
            });

            Debug.WriteLine($"Recorded performance data point for {agentName}, task: {taskType}, " +
                           $"score: {effectivenessScore:F2}, version: {currentVersion?.VersionNumber ?? 0}");
        }

        private async Task<AgentInfo> GetAgentInfoAsync(string agentName)
        {
            var agents = await _agentDb.GetAllAgentsAsync();
            return agents.FirstOrDefault(a => a.Name == agentName);
        }

        public string GenerateComparativeReport(string taskType = null)
        {
            var report = new StringBuilder();
            report.AppendLine("=== COMPARATIVE AGENT PERFORMANCE REPORT ===");

            if (taskType != null)
            {
                report.AppendLine($"Task Type: {taskType}");
            }

            // Calculate average performance by agent and version
            var performanceByAgentVersion = new Dictionary<string, Dictionary<int, List<double>>>();

            foreach (var agentEntry in _performanceHistory)
            {
                string agentName = agentEntry.Key;
                var dataPoints = agentEntry.Value;

                // Filter by task type if specified
                if (taskType != null)
                {
                    dataPoints = dataPoints.Where(dp => dp.TaskType == taskType).ToList();
                }

                // Skip if no data points match
                if (dataPoints.Count == 0) continue;

                // Initialize agent entry
                if (!performanceByAgentVersion.ContainsKey(agentName))
                {
                    performanceByAgentVersion[agentName] = new Dictionary<int, List<double>>();
                }

                // Group by version
                foreach (var dataPoint in dataPoints)
                {
                    if (!performanceByAgentVersion[agentName].ContainsKey(dataPoint.PromptVersion))
                    {
                        performanceByAgentVersion[agentName][dataPoint.PromptVersion] = new List<double>();
                    }

                    performanceByAgentVersion[agentName][dataPoint.PromptVersion].Add(dataPoint.EffectivenessScore);
                }
            }

            // Generate report for each agent
            foreach (var agentEntry in performanceByAgentVersion)
            {
                string agentName = agentEntry.Key;
                var versionScores = agentEntry.Value;

                report.AppendLine($"\nAgent: {agentName}");
                report.AppendLine("  Performance by Version:");

                foreach (var versionEntry in versionScores.OrderBy(v => v.Key))
                {
                    int version = versionEntry.Key;
                    var scores = versionEntry.Value;
                    double avgScore = scores.Count > 0 ? scores.Average() : 0;

                    report.AppendLine($"    Version {version}: {avgScore:F2} (from {scores.Count} interactions)");
                }

                // Calculate improvement between versions
                if (versionScores.Count > 1)
                {
                    report.AppendLine("  Version Improvements:");

                    var versions = versionScores.Keys.OrderBy(v => v).ToList();
                    for (int i = 1; i < versions.Count; i++)
                    {
                        int prevVersion = versions[i - 1];
                        int currVersion = versions[i];

                        double prevAvg = versionScores[prevVersion].Average();
                        double currAvg = versionScores[currVersion].Average();
                        double improvement = currAvg - prevAvg;

                        report.AppendLine($"    {prevVersion} → {currVersion}: " +
                                         $"{(improvement >= 0 ? "+" : "")}{improvement:F2} " +
                                         $"({improvement / prevAvg:P2})");
                    }
                }
            }

            return report.ToString();
        }
    }
}
