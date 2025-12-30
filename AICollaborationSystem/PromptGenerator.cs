using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace AnthropicApp.AICollaborationSystem
{
    public class PromptGenerator
    {
        private readonly AgentDatabase _agentDb;
        private readonly AIManager _aiManager;

        public PromptGenerator(AgentDatabase agentDb, AIManager aiManager)
        {
            _agentDb = agentDb;
            _aiManager = aiManager;
        }














        public async Task<string> GeneratePromptImprovementSuggestionsAsync(
       int agentId,
       PromptRefinementSystem.PromptAnalysisResult analysis,
       string currentPrompt)
        {
            Debug.WriteLine($"Generating prompt improvement SUGGESTIONS for {analysis.AgentName}...");

            // --- Option A: Use Chief/PromptEngineer Agent ---
            if (_aiManager.AgentExists("Chief")) // Or use a dedicated "PromptEngineer" agent if created
            {
                string metaPrompt = BuildRefinementMetaPrompt(analysis.AgentName, currentPrompt, analysis);
                Debug.WriteLine($"--- Meta-Prompt for Prompt Refinement ---\n{metaPrompt}\n---");

                // Request suggestions from the meta-agent
                // Use RunModuleInteractionTestAsync or a similar direct invocation
                // We need to wait for the response directly here.
                var responseArgs = await _aiManager.RequestAsyncAndWait("Chief", metaPrompt, TimeSpan.FromMinutes(2)); // Add a helper Wait method to AIManager or use TCS

                if (responseArgs != null && !string.IsNullOrWhiteSpace(responseArgs.ResponseData))
                {
                    Debug.WriteLine($"Received suggestions from Chief/PromptEngineer for {analysis.AgentName}.");
                    // TODO: Parse the response to extract *only* the suggestions, removing conversational filler.
                    // Maybe ask the meta-agent to format suggestions like:
                    // [SUGGESTION]Add instruction X for Task Y.[/SUGGESTION]
                    // [SUGGESTION]Rephrase section Z for clarity.[/SUGGESTION]
                    string suggestions = ParseSuggestionsFromMetaResponse(responseArgs.ResponseData); // Implement this parser
                    return suggestions;
                }
                else
                {
                    Debug.WriteLine($"Warning: Failed to get suggestions from Chief/PromptEngineer for {analysis.AgentName}.");
                    return $"// Failed to generate suggestions via AI. Weak areas: {string.Join(", ", analysis.WeakTaskTypes)}"; // Fallback
                }
            }
            // --- Option B: Fallback to simpler built-in logic (Less effective) ---
            else
            {
                Debug.WriteLine($"Warning: Chief/PromptEngineer agent not found. Using basic suggestion logic for {analysis.AgentName}.");
                return GenerateSuggestionsWithBuiltInLogic(analysis); // Simpler method returning only suggestions
            }
        }

        // Helper to build the meta-prompt for the AI
        private string BuildRefinementMetaPrompt(string agentNameToRefine, string currentPrompt, PromptRefinementSystem.PromptAnalysisResult analysis)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Objective: Generate specific, actionable suggestions to improve the system prompt for the '{agentNameToRefine}' agent.");
            sb.AppendLine($"The goal is to address identified performance weaknesses without harming existing strengths.");
            sb.AppendLine("\n--- CURRENT PROMPT ---");
            sb.AppendLine(currentPrompt);
            sb.AppendLine("--- END CURRENT PROMPT ---");

            sb.AppendLine("\n--- PERFORMANCE ANALYSIS ---");
            sb.AppendLine($"- Overall Success Rate: {analysis.OverallSuccessRate:P2}");
            if (analysis.WeakTaskTypes.Any()) sb.AppendLine($"- Weak Task Types: {string.Join(", ", analysis.WeakTaskTypes)}");
            if (analysis.StrongTaskTypes.Any()) sb.AppendLine($"- Strong Task Types: {string.Join(", ", analysis.StrongTaskTypes)}");
            // Add capabilities if desired

            sb.AppendLine("\n--- TASK ---");
            sb.AppendLine("Based ONLY on the current prompt and the performance analysis, provide a list of concrete suggestions for improvement.");
            sb.AppendLine("Focus on adding specific instructions or clarifying existing sections to address the weak task types.");
            sb.AppendLine("Do NOT rewrite the entire prompt.");
            sb.AppendLine("Format your output clearly, listing each suggestion. Example:");
            sb.AppendLine("[SUGGESTION]Add detailed steps for handling 'XYZ' task.[/SUGGESTION]");
            sb.AppendLine("[SUGGESTION]Clarify the expected output format in the INTERACTION PROTOCOL section.[/SUGGESTION]");

            return sb.ToString();
        }

        // Helper to parse suggestions (implement based on expected format)
        private string ParseSuggestionsFromMetaResponse(string rawResponse)
        {
            // Example using Regex for [SUGGESTION]...[/SUGGESTION] tags
            var suggestionPattern = new Regex(@"\[SUGGESTION\](.*?)\[/SUGGESTION\]", RegexOptions.Singleline);
            var matches = suggestionPattern.Matches(rawResponse);
            if (matches.Count > 0)
            {
                return string.Join("\n", matches.Select(m => $"- {m.Groups[1].Value.Trim()}"));
            }
            // Fallback: return raw response trimmed if tags not found
            return rawResponse.Trim();
        }


        // Simpler fallback logic (generates basic suggestions string)
        private string GenerateSuggestionsWithBuiltInLogic(PromptRefinementSystem.PromptAnalysisResult analysis)
        {
            var suggestions = new List<string>();
            foreach (var weakTask in analysis.WeakTaskTypes)
            {
                suggestions.Add($"- Add/Enhance instructions specifically for handling '{weakTask}' tasks.");
            }
            foreach (var weakCap in analysis.WeakCapabilities)
            {
                suggestions.Add($"- Add/Enhance instructions related to the '{weakCap}' capability.");
            }
            if (!suggestions.Any()) suggestions.Add("- General review for clarity and conciseness recommended.");

            return string.Join("\n", suggestions);
        }




















        public async Task<string> GenerateRefinedPromptAsync(int agentId,
                                                          PromptRefinementSystem.PromptAnalysisResult analysis)
        {
            Debug.WriteLine($"Generating refined prompt for {analysis.AgentName}...");

            // Get current prompt
            var currentVersion = await _agentDb.GetCurrentAgentVersionAsync(agentId);
            if (currentVersion == null)
            {
                Debug.WriteLine($"No current version found for agent {agentId}");
                return null;
            }

            string currentPrompt = currentVersion.Prompt;

            // If we have Chief agent, use it to help refine the prompt
            if (_aiManager.AgentExists("Chief") && analysis.AgentName != "Chief")
            {
                return await GeneratePromptWithChiefAsync(analysis.AgentName, currentPrompt, analysis);
            }
            else
            {
                // Otherwise use built-in refinement logic
                return GeneratePromptWithBuiltInLogic(analysis.AgentName, currentPrompt, analysis);
            }
        }

        private async Task<string> GeneratePromptWithChiefAsync(string agentName, string currentPrompt,
                                                            PromptRefinementSystem.PromptAnalysisResult analysis)
        {
            // Create a prompt for the Chief to generate a refined prompt
            StringBuilder promptBuilder = new StringBuilder();
            promptBuilder.AppendLine($"You need to refine the prompt for the {agentName} agent based on performance analysis.");
            promptBuilder.AppendLine("\nCurrent prompt:");
            promptBuilder.AppendLine(currentPrompt);

            promptBuilder.AppendLine("\nPerformance analysis:");
            promptBuilder.AppendLine($"- Overall success rate: {analysis.OverallSuccessRate:P2}");

            if (analysis.StrongTaskTypes.Count > 0)
            {
                promptBuilder.AppendLine("- Strong task types: " + string.Join(", ", analysis.StrongTaskTypes));
            }

            if (analysis.WeakTaskTypes.Count > 0)
            {
                promptBuilder.AppendLine("- Weak task types: " + string.Join(", ", analysis.WeakTaskTypes));
            }

            if (analysis.StrongCapabilities.Count > 0)
            {
                promptBuilder.AppendLine("- Strong capabilities: " + string.Join(", ", analysis.StrongCapabilities));
            }

            if (analysis.WeakCapabilities.Count > 0)
            {
                promptBuilder.AppendLine("- Weak capabilities: " + string.Join(", ", analysis.WeakCapabilities));
            }

            promptBuilder.AppendLine("\nRecommended improvements:");
            foreach (var improvement in analysis.RecommendedImprovements)
            {
                promptBuilder.AppendLine($"- {improvement}");
            }

            promptBuilder.AppendLine("\nGenerate a refined prompt for this agent that addresses the weak areas while maintaining the strong aspects. The prompt should maintain the same general structure but with specific improvements.");

            // Request prompt generation from Chief
            string refineRequest = promptBuilder.ToString();
            await _aiManager.RequestAsync("Chief", refineRequest);

            // In a real implementation, you'd capture the Chief's response
            // For this example, we'll just return a placeholder

            // Simulated Chief response
            string refinedPrompt = $"Refined prompt for {agentName} would be returned here...";

            Debug.WriteLine($"Generated refined prompt for {agentName} using Chief agent");
            return refinedPrompt;
        }

        private string GeneratePromptWithBuiltInLogic(string agentName, string currentPrompt,
                                                   PromptRefinementSystem.PromptAnalysisResult analysis)
        {
            Debug.WriteLine($"Using built-in logic to refine prompt for {agentName}");

            // Start with current prompt
            string refinedPrompt = currentPrompt;

            // Apply standard improvements
            foreach (var weakTask in analysis.WeakTaskTypes)
            {
                refinedPrompt = AddTaskSpecificInstructions(refinedPrompt, weakTask);
            }

            foreach (var weakCapability in analysis.WeakCapabilities)
            {
                refinedPrompt = AddCapabilityInstructions(refinedPrompt, weakCapability);
            }

            // Add agent-specific improvements
            refinedPrompt = AddAgentSpecificImprovements(refinedPrompt, agentName, analysis);

            return refinedPrompt;
        }

        private string AddTaskSpecificInstructions(string prompt, string taskType)
        {
            // Library of task-specific instruction improvements
            var taskInstructions = new Dictionary<string, string>
            {
                ["Analysis"] = "\n\nWhen performing analysis tasks:\n" +
                              "1. Break down the problem into components\n" +
                              "2. Consider multiple perspectives\n" +
                              "3. Identify key constraints and assumptions\n" +
                              "4. Evaluate the reliability of information\n" +
                              "5. Support conclusions with evidence",

                ["Design"] = "\n\nWhen working on design tasks:\n" +
                            "1. Consider the user perspective first\n" +
                            "2. Balance flexibility and simplicity\n" +
                            "3. Apply appropriate design patterns\n" +
                            "4. Ensure maintainability and scalability\n" +
                            "5. Document critical design decisions",

                ["Implementation"] = "\n\nWhen implementing solutions:\n" +
                                   "1. Start with a clear outline\n" +
                                   "2. Follow established coding standards\n" +
                                   "3. Build incrementally and test as you go\n" +
                                   "4. Consider error handling and edge cases\n" +
                                   "5. Focus on readability and maintainability",

                ["Coordination"] = "\n\nWhen coordinating with other agents:\n" +
                                 "1. Clearly define responsibilities\n" +
                                 "2. Ensure information is shared effectively\n" +
                                 "3. Identify dependencies and potential conflicts\n" +
                                 "4. Establish decision-making processes\n" +
                                 "5. Regularly check alignment on goals"
            };

            // Add task instructions if available
            if (taskInstructions.ContainsKey(taskType) && !prompt.Contains(taskInstructions[taskType]))
            {
                return prompt + taskInstructions[taskType];
            }

            return prompt;
        }

        private string AddCapabilityInstructions(string prompt, string capability)
        {
            // Library of capability-specific instruction improvements
            var capabilityInstructions = new Dictionary<string, string>
            {
                ["Decision Making"] = "\n\nTo improve decision making:\n" +
                                    "1. Identify all available options\n" +
                                    "2. Evaluate each option against clear criteria\n" +
                                    "3. Consider both short and long-term implications\n" +
                                    "4. Weigh trade-offs explicitly\n" +
                                    "5. Commit decisively once analysis is complete",

                ["Creative Thinking"] = "\n\nTo enhance creative thinking:\n" +
                                      "1. Challenge conventional assumptions\n" +
                                      "2. Consider analogies from different domains\n" +
                                      "3. Generate multiple alternatives before evaluating\n" +
                                      "4. Combine elements from different solutions\n" +
                                      "5. Use 'what if' scenarios to explore possibilities",

                ["Critical Analysis"] = "\n\nTo strengthen critical analysis:\n" +
                                      "1. Question assumptions and identify biases\n" +
                                      "2. Distinguish between facts and opinions\n" +
                                      "3. Examine methodology and data quality\n" +
                                      "4. Consider alternative interpretations\n" +
                                      "5. Evaluate the strength of arguments and evidence"
            };

            // Add capability instructions if available
            if (capabilityInstructions.ContainsKey(capability) && !prompt.Contains(capabilityInstructions[capability]))
            {
                return prompt + capabilityInstructions[capability];
            }

            return prompt;
        }

        private string AddAgentSpecificImprovements(string prompt, string agentName,
                                                 PromptRefinementSystem.PromptAnalysisResult analysis)
        {
            // Agent-specific improvements
            switch (agentName)
            {
                case "Chief":
                    if (analysis.WeakTaskTypes.Contains("Coordination") ||
                        analysis.WeakCapabilities.Contains("Decision Making"))
                    {
                        prompt += "\n\nAs Chief, your primary responsibility is coordination and final decision making. " +
                                 "Always ensure you:\n" +
                                 "1. Gather input from all relevant agents\n" +
                                 "2. Synthesize conflicting viewpoints\n" +
                                 "3. Make clear, decisive choices when consensus cannot be reached\n" +
                                 "4. Distribute tasks based on agent specializations\n" +
                                 "5. Maintain focus on the overall objective";
                    }
                    break;

                case "Innovator":
                    if (analysis.OverallSuccessRate < 0.7f)
                    {
                        prompt += "\n\nAs Innovator, focus on generating novel ideas and approaches. " +
                                 "Remember to:\n" +
                                 "1. Start with divergent thinking before convergent thinking\n" +
                                 "2. Challenge assumptions explicitly\n" +
                                 "3. Propose multiple alternatives rather than a single solution\n" +
                                 "4. Consider how ideas from other domains might apply\n" +
                                 "5. Balance creativity with practicality";
                    }
                    break;

                    // Add cases for other agents
            }

            return prompt;
        }
    }
}
