using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AnthropicApp.AICollaborationSystem
{
    public class PromptRefinementSystem
    {
        private readonly AgentDatabase _agentDb;
        private readonly AIManager _aiManager;
        private readonly MetricsTracker _metricsTracker;
        private readonly ComparativeAnalysis _comparativeAnalysis;

        public PromptRefinementSystem(AgentDatabase agentDb, AIManager aiManager,
                                    MetricsTracker metricsTracker, ComparativeAnalysis comparativeAnalysis)
        {
            _agentDb = agentDb;
            _aiManager = aiManager;
            _metricsTracker = metricsTracker;
            _comparativeAnalysis = comparativeAnalysis;
        }

        public async Task<Dictionary<string, PromptAnalysisResult>> AnalyzeAgentPerformanceAsync()
        {
            Debug.WriteLine("Analyzing agent performance for prompt refinement...");

            // Get all agents
            var agents = await _agentDb.GetAllAgentsAsync();

            // Results dictionary
            var results = new Dictionary<string, PromptAnalysisResult>();

            foreach (var agent in agents)
            {
                Debug.WriteLine($"Analyzing performance for {agent.Name}...");

                // Get agent performance data
                var performanceStats = await _agentDb.GetAgentPerformanceStatsAsync(agent.AgentId);

                // Get agent capabilities
                var capabilities = await _agentDb.GetAgentCapabilitiesAsync(agent.AgentId);

                // Create analysis result
                var result = new PromptAnalysisResult
                {
                    AgentId = agent.AgentId,
                    AgentName = agent.Name,
                    OverallSuccessRate = agent.TotalInteractions > 0
                        ? agent.SuccessfulInteractions / (float)agent.TotalInteractions
                        : 0f,
                    PerformanceByTaskType = new Dictionary<string, float>(),
                    StrongCapabilities = new List<string>(),
                    WeakCapabilities = new List<string>(),
                    RecommendedImprovements = new List<string>()
                };

                // Analyze performance by task type
                foreach (var stat in performanceStats)
                {
                    result.PerformanceByTaskType[stat.TaskType] = stat.SuccessRate;

                    // Performance thresholds
                    if (stat.SuccessRate > 0.8f)
                    {
                        result.StrongTaskTypes.Add(stat.TaskType);
                    }
                    else if (stat.SuccessRate < 0.6f)
                    {
                        result.WeakTaskTypes.Add(stat.TaskType);
                    }
                }

                // Analyze capabilities
                foreach (var capability in capabilities)
                {
                    if (capability.PerformanceRating > 0.8f)
                    {
                        result.StrongCapabilities.Add(capability.CapabilityName);
                    }
                    else if (capability.PerformanceRating < 0.6f)
                    {
                        result.WeakCapabilities.Add(capability.CapabilityName);
                    }
                }

                // Determine areas for improvement
                DetermineImprovementAreas(result);

                // Add to results
                results[agent.Name] = result;

                Debug.WriteLine($"Analysis completed for {agent.Name}");
            }

            return results;
        }

        // Class to hold analysis results
        public class PromptAnalysisResult
        {
            public int AgentId { get; set; }
            public string AgentName { get; set; }
            public float OverallSuccessRate { get; set; }
            public Dictionary<string, float> PerformanceByTaskType { get; set; } =
                new Dictionary<string, float>();
            public List<string> StrongTaskTypes { get; set; } = new List<string>();
            public List<string> WeakTaskTypes { get; set; } = new List<string>();
            public List<string> StrongCapabilities { get; set; } = new List<string>();
            public List<string> WeakCapabilities { get; set; } = new List<string>();
            public List<string> RecommendedImprovements { get; set; } = new List<string>();
        }

        private void DetermineImprovementAreas(PromptAnalysisResult result)
        {
            // Add improvement recommendations based on weak areas
            foreach (var weakTask in result.WeakTaskTypes)
            {
                result.RecommendedImprovements.Add($"Enhance {weakTask} handling in prompt");
            }

            foreach (var weakCapability in result.WeakCapabilities)
            {
                result.RecommendedImprovements.Add($"Strengthen {weakCapability} instructions");
            }

            // Add general improvements based on agent role
            switch (result.AgentName)
            {
                case "Chief":
                    if (result.WeakTaskTypes.Contains("Coordination"))
                    {
                        result.RecommendedImprovements.Add("Improve multi-agent coordination instructions");
                    }
                    break;

                case "Innovator":
                    if (result.WeakTaskTypes.Contains("CreativeThinking"))
                    {
                        result.RecommendedImprovements.Add("Enhance creative thinking techniques");
                    }
                    break;

                case "Evaluator":
                    if (result.WeakTaskTypes.Contains("Analysis"))
                    {
                        result.RecommendedImprovements.Add("Strengthen critical analysis framework");
                    }
                    break;

                    // Add cases for other agents
            }

            // If overall performance is good but no standout strengths
            if (result.OverallSuccessRate > 0.7f && result.StrongTaskTypes.Count == 0)
            {
                result.RecommendedImprovements.Add("Focus prompt on specialization rather than general competence");
            }
        }
    }
}
