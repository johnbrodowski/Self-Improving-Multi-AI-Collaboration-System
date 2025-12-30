using System;
using System.IO;

namespace AnthropicApp.AICollaborationSystem
{
    /// <summary>
    /// Central configuration for the AI Collaboration System.
    /// Consolidates all configuration settings in one place.
    /// </summary>
    public class CollaborationSystemConfig
    {
        /// <summary>
        /// Default configuration instance using standard settings.
        /// </summary>
        public static CollaborationSystemConfig Default { get; } = new();

        #region Database Settings

        /// <summary>
        /// Path to the SQLite database file for cognitive agents.
        /// </summary>
        public string DatabasePath { get; set; } = "cognitive_agents.db";

        /// <summary>
        /// Number of days to retain performance metrics before cleanup.
        /// </summary>
        public int MetricsRetentionDays { get; set; } = 90;

        #endregion

        #region API Settings

        /// <summary>
        /// The Anthropic API endpoint URL.
        /// </summary>
        public string AnthropicApiUrl { get; set; } = "https://api.anthropic.com/v1/messages";

        /// <summary>
        /// Default model to use for agent requests.
        /// </summary>
        public string DefaultModel { get; set; } = "claude-sonnet-4-5";

        /// <summary>
        /// Maximum tokens for agent responses.
        /// </summary>
        public int MaxTokens { get; set; } = 8192;

        /// <summary>
        /// Request timeout in seconds.
        /// </summary>
        public int RequestTimeoutSeconds { get; set; } = 120;

        #endregion

        #region Performance Thresholds

        /// <summary>
        /// Minimum accuracy threshold to trigger prompt refinement (0.0 - 1.0).
        /// When agent accuracy drops below this, refinement is triggered.
        /// </summary>
        public double PromptRefinementThreshold { get; set; } = 0.85;

        /// <summary>
        /// Minimum number of samples required before running A/B tests.
        /// </summary>
        public int ABTestMinimumSamples { get; set; } = 20;

        /// <summary>
        /// Threshold for considering an agent's performance as "strong" (0.0 - 1.0).
        /// </summary>
        public double StrongPerformanceThreshold { get; set; } = 0.8;

        /// <summary>
        /// Threshold for considering an agent's performance as "weak" (0.0 - 1.0).
        /// </summary>
        public double WeakPerformanceThreshold { get; set; } = 0.6;

        #endregion

        #region Agent Settings

        /// <summary>
        /// Maximum number of messages to include in session history.
        /// </summary>
        public int MaxSessionHistoryCount { get; set; } = 25;

        /// <summary>
        /// Default history mode for agent activations.
        /// </summary>
        public string DefaultHistoryMode { get; set; } = "CONVERSATIONAL";

        /// <summary>
        /// Path to the folder containing base agent prompts.
        /// </summary>
        public string BasePromptsPath { get; set; } = "BasePrompts";

        #endregion

        #region Logging Settings

        /// <summary>
        /// Enable verbose debug logging.
        /// </summary>
        public bool EnableDebugLogging { get; set; } = true;

        /// <summary>
        /// Log file path for persistent logging. Null disables file logging.
        /// </summary>
        public string? LogFilePath { get; set; } = null;

        #endregion

        /// <summary>
        /// Validates the configuration and throws if invalid.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(DatabasePath))
                throw new InvalidOperationException("DatabasePath cannot be empty.");

            if (MetricsRetentionDays < 1)
                throw new InvalidOperationException("MetricsRetentionDays must be at least 1.");

            if (MaxTokens < 100)
                throw new InvalidOperationException("MaxTokens must be at least 100.");

            if (PromptRefinementThreshold < 0 || PromptRefinementThreshold > 1)
                throw new InvalidOperationException("PromptRefinementThreshold must be between 0.0 and 1.0.");

            if (StrongPerformanceThreshold <= WeakPerformanceThreshold)
                throw new InvalidOperationException("StrongPerformanceThreshold must be greater than WeakPerformanceThreshold.");

            if (MaxSessionHistoryCount < 0 || MaxSessionHistoryCount > 25)
                throw new InvalidOperationException("MaxSessionHistoryCount must be between 0 and 25.");
        }

        /// <summary>
        /// Gets the full path to the database file.
        /// </summary>
        public string GetFullDatabasePath()
        {
            if (Path.IsPathRooted(DatabasePath))
                return DatabasePath;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DatabasePath);
        }

        /// <summary>
        /// Gets the full path to the base prompts folder.
        /// </summary>
        public string GetFullBasePromptsPath()
        {
            if (Path.IsPathRooted(BasePromptsPath))
                return BasePromptsPath;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BasePromptsPath);
        }
    }
}
