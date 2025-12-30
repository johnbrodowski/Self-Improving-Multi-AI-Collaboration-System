using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AnthropicApp.AICollaborationSystem
{
    public class AgentEvaluationMetrics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulResponses { get; set; }
        public int FailedResponses { get; set; }
        public double AverageResponseTime { get; set; }
        public int ContributionCount { get; set; }
        public double RelevanceScore { get; set; }
        public double CreativityScore { get; set; }
        public double AccuracyScore { get; set; }
        public double ConsensusScore { get; set; }

        public double OverallEffectivenessScore =>
            (RelevanceScore + CreativityScore + AccuracyScore + ConsensusScore) / 4.0;
    }

    public class MetricsTracker
    {
        private readonly Dictionary<string, AgentEvaluationMetrics> _agentMetrics =
            new Dictionary<string, AgentEvaluationMetrics>();

        public void RecordResponse(string agentName, bool isSuccessful, TimeSpan responseTime)
        {
            if (!_agentMetrics.ContainsKey(agentName))
            {
                _agentMetrics[agentName] = new AgentEvaluationMetrics();
            }

            var metrics = _agentMetrics[agentName];
            metrics.TotalRequests++;

            if (isSuccessful)
            {
                metrics.SuccessfulResponses++;
            }
            else
            {
                metrics.FailedResponses++;
            }

            // Update average response time
            double totalTime = metrics.AverageResponseTime * (metrics.TotalRequests - 1);
            totalTime += responseTime.TotalSeconds;
            metrics.AverageResponseTime = totalTime / metrics.TotalRequests;

            Debug.WriteLine($"Metrics updated for {agentName}: " +
                           $"Success rate: {(double)metrics.SuccessfulResponses / metrics.TotalRequests:P2}, " +
                           $"Avg time: {metrics.AverageResponseTime:F2}s");
        }

        public void UpdateQualityMetrics(string agentName, double relevance, double creativity,
                                       double accuracy, double consensus)
        {
            if (!_agentMetrics.ContainsKey(agentName))
            {
                _agentMetrics[agentName] = new AgentEvaluationMetrics();
            }

            var metrics = _agentMetrics[agentName];
            metrics.ContributionCount++;

            // Update quality metrics with running average
            metrics.RelevanceScore = UpdateRunningAverage(metrics.RelevanceScore,
                                                        metrics.ContributionCount, relevance);
            metrics.CreativityScore = UpdateRunningAverage(metrics.CreativityScore,
                                                         metrics.ContributionCount, creativity);
            metrics.AccuracyScore = UpdateRunningAverage(metrics.AccuracyScore,
                                                       metrics.ContributionCount, accuracy);
            metrics.ConsensusScore = UpdateRunningAverage(metrics.ConsensusScore,
                                                        metrics.ContributionCount, consensus);

            Debug.WriteLine($"Quality metrics updated for {agentName}: " +
                           $"Effectiveness: {metrics.OverallEffectivenessScore:F2}");
        }

        private double UpdateRunningAverage(double currentAvg, int count, double newValue)
        {
            return ((currentAvg * (count - 1)) + newValue) / count;
        }

        public string GenerateMetricsReport()
        {
            var report = new StringBuilder();
            report.AppendLine("=== AGENT EVALUATION METRICS REPORT ===");

            foreach (var entry in _agentMetrics.OrderByDescending(m => m.Value.OverallEffectivenessScore))
            {
                var metrics = entry.Value;
                report.AppendLine($"Agent: {entry.Key}");
                report.AppendLine($"  Success Rate: {(double)metrics.SuccessfulResponses / metrics.TotalRequests:P2}");
                report.AppendLine($"  Avg Response Time: {metrics.AverageResponseTime:F2}s");
                report.AppendLine($"  Quality Metrics:");
                report.AppendLine($"    Relevance: {metrics.RelevanceScore:F2}");
                report.AppendLine($"    Creativity: {metrics.CreativityScore:F2}");
                report.AppendLine($"    Accuracy: {metrics.AccuracyScore:F2}");
                report.AppendLine($"    Consensus: {metrics.ConsensusScore:F2}");
                report.AppendLine($"  Overall Effectiveness: {metrics.OverallEffectivenessScore:F2}");
                report.AppendLine();
            }

            return report.ToString();
        }
    }
}
