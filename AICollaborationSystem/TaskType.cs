using System;
using System.Collections.Generic;

namespace AnthropicApp.AICollaborationSystem
{
    /// <summary>
    /// Defines the types of tasks that can be processed by the cognitive system.
    /// </summary>
    public enum TaskType
    {
        /// <summary>Default task type when no specific category matches.</summary>
        General,

        /// <summary>Tasks involving creation, generation, or implementation of new features.</summary>
        Implementation,

        /// <summary>Tasks involving analysis, evaluation, or assessment.</summary>
        Analysis,

        /// <summary>Tasks involving system or architectural design.</summary>
        Design,

        /// <summary>Tasks involving testing, verification, or validation.</summary>
        Testing,

        /// <summary>Tasks involving optimization, improvement, or refactoring.</summary>
        Optimization,

        /// <summary>Mathematical addition operations.</summary>
        Addition,

        /// <summary>Mathematical subtraction operations.</summary>
        Subtraction,

        /// <summary>Mathematical multiplication operations.</summary>
        Multiplication,

        /// <summary>Mathematical division operations.</summary>
        Division
    }

    /// <summary>
    /// Provides methods to classify and work with task types.
    /// </summary>
    public static class TaskTypeClassifier
    {
        private static readonly Dictionary<TaskType, string[]> TaskKeywords = new()
        {
            { TaskType.Implementation, new[] { "create", "generate", "implement", "build", "develop", "write" } },
            { TaskType.Analysis, new[] { "analyze", "evaluate", "assess", "examine", "review", "inspect" } },
            { TaskType.Design, new[] { "design", "architect", "plan", "structure", "layout" } },
            { TaskType.Testing, new[] { "test", "verify", "validate", "check", "confirm" } },
            { TaskType.Optimization, new[] { "improve", "optimize", "refactor", "enhance", "streamline" } }
        };

        /// <summary>
        /// Determines the task type from request data using keyword matching.
        /// </summary>
        /// <param name="requestData">The request text to classify.</param>
        /// <returns>The classified TaskType.</returns>
        public static TaskType Classify(string requestData)
        {
            if (string.IsNullOrWhiteSpace(requestData))
                return TaskType.General;

            var lowerRequest = requestData.ToLowerInvariant();

            // Check keyword-based categories
            foreach (var (taskType, keywords) in TaskKeywords)
            {
                foreach (var keyword in keywords)
                {
                    if (lowerRequest.Contains(keyword))
                        return taskType;
                }
            }

            // Check math operations
            if (lowerRequest.Contains("+"))
                return TaskType.Addition;
            if (lowerRequest.Contains("-") && !lowerRequest.Contains("--"))
                return TaskType.Subtraction;
            if (lowerRequest.Contains("*") || lowerRequest.Contains("×"))
                return TaskType.Multiplication;
            if (lowerRequest.Contains("/") || lowerRequest.Contains("÷"))
                return TaskType.Division;

            return TaskType.General;
        }

        /// <summary>
        /// Converts a TaskType to its string representation for database storage.
        /// </summary>
        public static string ToStorageString(TaskType taskType)
        {
            return taskType switch
            {
                TaskType.General => "GeneralTask",
                TaskType.Implementation => "Implementation",
                TaskType.Analysis => "Analysis",
                TaskType.Design => "Design",
                TaskType.Testing => "Testing",
                TaskType.Optimization => "Optimization",
                TaskType.Addition => "Addition",
                TaskType.Subtraction => "Subtraction",
                TaskType.Multiplication => "Multiplication",
                TaskType.Division => "Division",
                _ => "GeneralTask"
            };
        }
    }
}
