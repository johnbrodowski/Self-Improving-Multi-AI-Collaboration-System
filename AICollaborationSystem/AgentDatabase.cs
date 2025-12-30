using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Threading.Tasks;
using System.Text; // Added for StringBuilder
using System.Linq;
using System.Diagnostics; // Added for Linq methods like FirstOrDefault
using System.Windows.Forms; // Added for Application.OpenForms for logging - consider removing/refactoring


namespace AnthropicApp.AICollaborationSystem
{
    /// <summary>
    /// Manages the database operations for AI agents including version history, performance tracking,
    /// team compositions, and Chain of Thought frameworks.
    /// </summary>
    public class AgentDatabase : IDisposable
    {
        private readonly string _connectionString; // Made readonly
        private bool _initialized = false;
        private bool _disposed = false;

        private readonly object _dbLock = new object(); // For basic thread safety



        /// <summary>
        /// Initializes a new instance of the AgentDatabase class.
        /// </summary>
        /// <param name="dbFilePath">Path to the SQLite database file. Default is "cognitive_agents.db".</param>
        public AgentDatabase(string dbFilePath = "cognitive_agents.db")
        {
            // Ensure directory exists
            string directory = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={dbFilePath};Mode=ReadWriteCreate;Cache=Shared"; // Shared cache for better concurrency
            // Initialize MUST be called separately after construction.
        }

        private SqliteConnection GetConnection()
        {
            // Your implementation to create and return an SqliteConnection
            // Ensure pooling is handled correctly if applicable
            return new SqliteConnection(_connectionString); // Example
        }



        // --- Initialization and Core Tables ---

        /// <summary>
        /// Initializes the database schema if it doesn't exist.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                // Enable Foreign Key support
                using (var command = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
                {
                    command.ExecuteNonQuery();
                }

                using (var command = new SqliteCommand("", connection))
                {
                    // --- Agent & Versioning Tables ---
                    // Create Agents table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Agents (
                        AgentId INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        Purpose TEXT NOT NULL,
                        CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        LastModifiedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        IsActive BOOLEAN NOT NULL DEFAULT 1,
                        BaseScore REAL DEFAULT 0.5, -- Use REAL for floating point
                        TotalInteractions INTEGER DEFAULT 0,
                        SuccessfulInteractions INTEGER DEFAULT 0
                    );";
                    command.ExecuteNonQuery();

                    // Create AgentVersions table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AgentVersions (
                        VersionId INTEGER PRIMARY KEY AUTOINCREMENT,
                        AgentId INTEGER NOT NULL,
                        VersionNumber INTEGER NOT NULL,
                        Prompt TEXT NOT NULL,
                        Comments TEXT,
                        KnownIssues TEXT,
                        CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        CreatedBy TEXT,
                        PerformanceScore REAL DEFAULT 0.0, -- Use REAL
                        IsActive BOOLEAN NOT NULL DEFAULT 1,
                        FOREIGN KEY (AgentId) REFERENCES Agents(AgentId) ON DELETE CASCADE, -- Cascade delete
                        UNIQUE (AgentId, VersionNumber)
                    );";
                    command.ExecuteNonQuery();

                    // Create PromptModifications table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS PromptModifications (
                        ModificationId INTEGER PRIMARY KEY AUTOINCREMENT,
                        VersionId INTEGER NOT NULL,
                        PreviousVersionId INTEGER,
                        ModificationReason TEXT NOT NULL,
                        ChangeSummary TEXT NOT NULL,
                        PerformanceBeforeChange REAL, -- Use REAL
                        PerformanceAfterChange REAL, -- Use REAL
                        ModificationDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (VersionId) REFERENCES AgentVersions(VersionId) ON DELETE CASCADE,
                        FOREIGN KEY (PreviousVersionId) REFERENCES AgentVersions(VersionId) ON DELETE SET NULL -- Avoid chain delete issues if prev version deleted
                    );";
                    command.ExecuteNonQuery();

                    // --- Performance & Interaction Tables ---
                    // Create AgentPerformance table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AgentPerformance (
                        PerformanceId INTEGER PRIMARY KEY AUTOINCREMENT,
                        AgentId INTEGER NOT NULL,
                        VersionId INTEGER NOT NULL,
                        TaskType TEXT NOT NULL,
                        CorrectResponses INTEGER DEFAULT 0,
                        TotalAttempts INTEGER DEFAULT 0,
                        AverageResponseTime REAL, -- Use REAL
                        LastEvaluationDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (AgentId) REFERENCES Agents(AgentId) ON DELETE CASCADE,
                        FOREIGN KEY (VersionId) REFERENCES AgentVersions(VersionId) ON DELETE CASCADE,
                        UNIQUE (AgentId, VersionId, TaskType)
                    );";
                    command.ExecuteNonQuery();

                    // Create InteractionHistory table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS InteractionHistory (
                        InteractionId INTEGER PRIMARY KEY AUTOINCREMENT,
                        AgentId INTEGER NOT NULL,
                        VersionId INTEGER NOT NULL,
                        TaskType TEXT NOT NULL,
                        RequestData TEXT NOT NULL,
                        ResponseData TEXT NOT NULL,
                        IsCorrect BOOLEAN,
                        ProcessingTime REAL, -- Use REAL
                        InteractionDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        EvaluationNotes TEXT,
                        FOREIGN KEY (AgentId) REFERENCES Agents(AgentId) ON DELETE CASCADE,
                        FOREIGN KEY (VersionId) REFERENCES AgentVersions(VersionId) ON DELETE CASCADE
                    );";
                    command.ExecuteNonQuery();

                    // --- Capabilities & Teams Tables ---
                    // Create AgentCapabilities table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AgentCapabilities (
                        CapabilityId INTEGER PRIMARY KEY AUTOINCREMENT,
                        AgentId INTEGER NOT NULL,
                        CapabilityName TEXT NOT NULL,
                        CapabilityDescription TEXT,
                        PerformanceRating REAL DEFAULT 0.0, -- Use REAL
                        FOREIGN KEY (AgentId) REFERENCES Agents(AgentId) ON DELETE CASCADE,
                        UNIQUE (AgentId, CapabilityName)
                    );";
                    command.ExecuteNonQuery();

                    // Create TeamCompositions table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS TeamCompositions (
                        TeamId INTEGER PRIMARY KEY AUTOINCREMENT,
                        TeamName TEXT NOT NULL UNIQUE, -- Added UNIQUE
                        ChiefAgentId INTEGER NOT NULL,
                        CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        PerformanceScore REAL DEFAULT 0.0, -- Use REAL
                        Description TEXT,
                        FOREIGN KEY (ChiefAgentId) REFERENCES Agents(AgentId) ON DELETE CASCADE
                    );";
                    command.ExecuteNonQuery();

                    // Create TeamMembers table
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS TeamMembers (
                        TeamId INTEGER NOT NULL,
                        AgentId INTEGER NOT NULL,
                        Role TEXT NOT NULL,
                        AssignmentReason TEXT,
                        PerformanceInTeam REAL DEFAULT 0.0, -- Use REAL
                        PRIMARY KEY (TeamId, AgentId),
                        FOREIGN KEY (TeamId) REFERENCES TeamCompositions(TeamId) ON DELETE CASCADE,
                        FOREIGN KEY (AgentId) REFERENCES Agents(AgentId) ON DELETE CASCADE
                    );";
                    command.ExecuteNonQuery();

                    // --- Indexes ---
                    command.CommandText = "CREATE INDEX IF NOT EXISTS idx_agent_version ON AgentVersions(AgentId, IsActive, VersionNumber);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS idx_agent_performance ON AgentPerformance(AgentId, VersionId, TaskType);";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX IF NOT EXISTS idx_interaction_history ON InteractionHistory(AgentId, VersionId, InteractionDate);";
                    command.ExecuteNonQuery();

                }
            }

            _initialized = true;
            Debug.WriteLine("AgentDatabase schema initialized/verified.");
        }

        /// <summary>
        /// Calculates performance scores for a specific agent version based on AgentPerformance data.
        /// </summary>
        /// <param name="versionId">The ID of the agent version.</param>
        /// <param name="connection">An open SQLite connection.</param>
        /// <param name="transaction">An active SQLite transaction.</param>
        /// <returns>A tuple containing the overall success rate (0-1) and a dictionary of task-specific success rates.</returns>
        private async Task<(float OverallSuccessRate, Dictionary<string, float> TaskSuccessRates)> CalculateVersionScoresInternalAsync(int versionId, SqliteConnection connection, SqliteTransaction transaction)
        {
            float overallSuccessRate = 0.0f;
            var taskSuccessRates = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            // Calculate overall score
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = @"
                SELECT SUM(CorrectResponses), SUM(TotalAttempts)
                FROM AgentPerformance
                WHERE VersionId = @VersionId AND TotalAttempts > 0;";
                command.Parameters.AddWithValue("@VersionId", versionId);

                using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await reader.ReadAsync() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
                    {
                        long totalCorrect = reader.GetInt64(0); // Use Int64 for SUM
                        long totalAttempts = reader.GetInt64(1);
                        if (totalAttempts > 0)
                        {
                            overallSuccessRate = (float)totalCorrect / totalAttempts;
                        }
                    }
                }
            }
            Debug.WriteLine($"[CalculateScores] Version {versionId}: Overall Rate = {overallSuccessRate:P2}");

            // Calculate score per task type
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = @"
                SELECT TaskType, CorrectResponses, TotalAttempts
                FROM AgentPerformance
                WHERE VersionId = @VersionId AND TotalAttempts > 0;";
                command.Parameters.AddWithValue("@VersionId", versionId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string taskType = reader.GetString(0);
                        int correct = reader.GetInt32(1);
                        int attempts = reader.GetInt32(2);
                        if (attempts > 0)
                        {
                            taskSuccessRates[taskType] = (float)correct / attempts;
                            Debug.WriteLine($"[CalculateScores] Version {versionId}, Task '{taskType}': Rate = {taskSuccessRates[taskType]:P2} ({correct}/{attempts})");
                        }
                    }
                }
            }

            return (overallSuccessRate, taskSuccessRates);
        }

        /// <summary>
        /// Calculates and updates the PerformanceScore for a specific AgentVersion record.
        /// Also updates the BaseScore for the parent Agent based on this version's score.
        /// </summary>
        /// <param name="versionId">The ID of the agent version to update.</param>
        /// <returns>True if the update was successful, false otherwise.</returns>
        public async Task<bool> UpdateCalculatedPerformanceScoresAsync(int versionId)
        {
            if (versionId <= 0) return false;
            // if (!_initialized) Initialize(); // Initialization should happen externally

            Debug.WriteLine($"[UpdateScores] Attempting to update scores for Version ID: {versionId}");

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 1. Calculate Scores
                        var (overallRate, taskRates) = await CalculateVersionScoresInternalAsync(versionId, connection, transaction);

                        // 2. Get Agent ID for this Version
                        int agentId = 0;
                        using (var cmd = new SqliteCommand("SELECT AgentId FROM AgentVersions WHERE VersionId = @VersionId", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@VersionId", versionId);
                            var result = await cmd.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                agentId = Convert.ToInt32(result);
                            }
                        }

                        if (agentId == 0)
                        {
                            Debug.WriteLine($"[UpdateScores] Error: Could not find AgentId for VersionId {versionId}. Aborting score update.");
                            transaction.Rollback();
                            return false;
                        }
                        Debug.WriteLine($"[UpdateScores] Found Agent ID {agentId} for Version ID {versionId}.");

                        // 3. Update AgentVersions Table
                        int rowsAffectedVersion = 0;
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
                            UPDATE AgentVersions
                            SET PerformanceScore = @PerformanceScore
                            WHERE VersionId = @VersionId;";
                            command.Parameters.AddWithValue("@VersionId", versionId);
                            command.Parameters.AddWithValue("@PerformanceScore", overallRate); // Use calculated overall rate
                            rowsAffectedVersion = await command.ExecuteNonQueryAsync();
                            Debug.WriteLine($"[UpdateScores] Updated AgentVersions for Version ID {versionId}. Score: {overallRate:P2}. Rows affected: {rowsAffectedVersion}");
                        }

                        // 4. Update Agents Table (Optional: Update BaseScore with latest version score)
                        // You might want more complex logic here (e.g., average across versions)
                        int rowsAffectedAgent = 0;
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction;
                            command.CommandText = @"
                            UPDATE Agents
                            SET BaseScore = @BaseScore, -- Update BaseScore
                                LastModifiedDate = CURRENT_TIMESTAMP
                            WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            command.Parameters.AddWithValue("@BaseScore", overallRate); // Set BaseScore to latest version's score
                            rowsAffectedAgent = await command.ExecuteNonQueryAsync();
                            Debug.WriteLine($"[UpdateScores] Updated Agents table for Agent ID {agentId}. BaseScore: {overallRate:P2}. Rows affected: {rowsAffectedAgent}");
                        }

                        // 5. (Optional) Update PromptModifications (if needed - current logic might be sufficient)
                        // The logic in UpdateVersionPerformanceInternalAsync already handles this,
                        // but calling it again ensures consistency if needed.
                        // await UpdateVersionPerformanceInternalAsync(versionId, connection, transaction);

                        transaction.Commit();
                        Debug.WriteLine($"[UpdateScores] Successfully updated scores for Version ID: {versionId}. Transaction committed.");
                        return rowsAffectedVersion > 0; // Return success based on updating the version score
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Debug.WriteLine($"[UpdateScores] Error updating scores for VersionId {versionId}: {ex.Message}\n{ex.StackTrace}");
                        // Consider re-throwing or returning false based on desired error handling
                        return false;
                        // throw;
                    }
                }
            }
        }

        /// <summary>
        /// Calculates and updates performance scores for the *active* version of a specific agent.
        /// </summary>
        /// <param name="agentId">The ID of the agent whose active version scores should be updated.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> UpdateActiveVersionScoresForAgentAsync(int agentId)
        {
            if (agentId <= 0) return false;
            Debug.WriteLine($"[UpdateScores] Updating scores for ACTIVE version of Agent ID: {agentId}");
            // Get active version ID first (outside transaction to avoid nesting issues if GetActiveVersionIdAsync creates one)
            int activeVersionId = await GetActiveVersionIdAsync(agentId); // Use overload without connection/transaction
            if (activeVersionId <= 0)
            {
                Debug.WriteLine($"[UpdateScores] No active version found for Agent ID {agentId}. Cannot update scores.");
                return false;
            }
            // Now call the version-specific update method
            return await UpdateCalculatedPerformanceScoresAsync(activeVersionId);
        }

        /// <summary>
        /// Gets the active VersionId for an agent. Creates its own connection.
        /// </summary>
        public async Task<int> GetActiveVersionIdAsync(int agentId)
        {
            // if (!_initialized) Initialize();
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqliteCommand("SELECT VersionId FROM AgentVersions WHERE AgentId = @AgentId AND IsActive = 1 LIMIT 1;", connection))
                {
                    command.Parameters.AddWithValue("@AgentId", agentId);
                    object result = await command.ExecuteScalarAsync();
                    return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;
                }
            }
        }


        /// <summary>
        /// Internal method to update the overall performance score for an agent version using an existing connection and transaction.
        /// Calculates score based on AgentPerformance table.
        /// </summary>
        private async Task<bool> UpdateVersionPerformanceInternalAsync(int versionId, SqliteConnection connection, SqliteTransaction transaction)
        {
            float performanceScore = 0.0f; // Default to 0

            // Calculate overall performance score (CorrectResponses / TotalAttempts) for this specific version
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = @"
                SELECT CAST(SUM(CorrectResponses) AS REAL) / SUM(TotalAttempts)
                FROM AgentPerformance
                WHERE VersionId = @VersionId AND TotalAttempts > 0;"; // Ensure TotalAttempts > 0 to avoid division by zero
                command.Parameters.AddWithValue("@VersionId", versionId);

                object result = await command.ExecuteScalarAsync();
                if (result != DBNull.Value && result != null)
                {
                    // Handle potential conversion errors gracefully
                    if (float.TryParse(result.ToString(), out float score))
                    {
                        performanceScore = score;
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: Could not parse performance score for VersionId {versionId}. Result: {result}");
                    }
                }
            }

            // Update AgentVersions table
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = @"
                UPDATE AgentVersions
                SET PerformanceScore = @PerformanceScore
                WHERE VersionId = @VersionId;";
                command.Parameters.AddWithValue("@VersionId", versionId);
                command.Parameters.AddWithValue("@PerformanceScore", performanceScore);
                int rowsAffectedAgentVersions = await command.ExecuteNonQueryAsync();

                // Update PromptModifications table if this version has a modification record
                command.CommandText = @"
                UPDATE PromptModifications
                SET PerformanceAfterChange = @PerformanceScore
                WHERE VersionId = @VersionId;";
                // Parameters (@VersionId, @PerformanceScore) are already set
                await command.ExecuteNonQueryAsync(); // No need to check rows affected here

                return rowsAffectedAgentVersions > 0;
            }
        }

        /// <summary>
        /// Updates the overall performance score for an agent version based on all its interactions.
        /// This method creates its own connection and transaction.
        /// </summary>
        public async Task<bool> UpdateVersionPerformanceAsync(int versionId)
        {
            //  if (!_initialized) Initialize();
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        bool result = await UpdateVersionPerformanceInternalAsync(versionId, connection, transaction);
                        transaction.Commit();
                        return result;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Debug.WriteLine($"Error in UpdateVersionPerformanceAsync for VersionId {versionId}: {ex.Message}");
                        throw; // Re-throw after logging
                    }
                }
            }
        }


        // --- Interaction Recording ---

        /// <summary>
        /// Records an agent interaction with performance data, updating relevant statistics.
        /// </summary>
        public async Task<int> RecordInteractionAsync(int agentId, string taskType, string requestData,
            string responseData, bool isCorrect, float processingTime, string evaluationNotes = null)
        {
            //   if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 1. Get current active version ID for the agent
                        int versionId = await GetActiveVersionIdAsync(agentId, connection, transaction);
                        if (versionId == 0)
                        {
                            throw new InvalidOperationException($"No active version found for agent ID {agentId}. Cannot record interaction.");
                        }

                        // 2. Record the interaction in InteractionHistory
                        int interactionId = await InsertInteractionHistoryAsync(agentId, versionId, taskType, requestData, responseData, isCorrect, processingTime, evaluationNotes, connection, transaction);

                        // 3. Update overall agent interaction counts in Agents table
                        await UpdateAgentInteractionCountsAsync(agentId, isCorrect, connection, transaction);

                        // 4. Update or insert detailed performance in AgentPerformance table
                        await UpsertAgentPerformanceAsync(agentId, versionId, taskType, isCorrect, processingTime, connection, transaction);

                        // 5. Update the overall PerformanceScore for the specific version in AgentVersions table
                        await UpdateVersionPerformanceInternalAsync(versionId, connection, transaction);

                        transaction.Commit();
                        return interactionId;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Debug.WriteLine($"Error recording interaction for AgentId {agentId}: {ex.Message}");
                        throw; // Re-throw after logging
                    }
                }
            }
        }

        // Helper to get active version ID
        private async Task<int> GetActiveVersionIdAsync(int agentId, SqliteConnection connection, SqliteTransaction transaction)
        {
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT VersionId FROM AgentVersions WHERE AgentId = @AgentId AND IsActive = 1 LIMIT 1;";
                command.Parameters.AddWithValue("@AgentId", agentId);
                object result = await command.ExecuteScalarAsync();
                return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;
            }
        }

        // Helper to insert into InteractionHistory
        private async Task<int> InsertInteractionHistoryAsync(int agentId, int versionId, string taskType, string requestData, string responseData, bool isCorrect, float processingTime, string evaluationNotes, SqliteConnection connection, SqliteTransaction transaction)
        {
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO InteractionHistory (AgentId, VersionId, TaskType, RequestData, ResponseData, IsCorrect, ProcessingTime, EvaluationNotes, InteractionDate)
                    VALUES (@AgentId, @VersionId, @TaskType, @RequestData, @ResponseData, @IsCorrect, @ProcessingTime, @EvaluationNotes, CURRENT_TIMESTAMP);
                    SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@AgentId", agentId);
                command.Parameters.AddWithValue("@VersionId", versionId);
                command.Parameters.AddWithValue("@TaskType", taskType);
                command.Parameters.AddWithValue("@RequestData", requestData);
                command.Parameters.AddWithValue("@ResponseData", responseData);
                command.Parameters.AddWithValue("@IsCorrect", isCorrect ? 1 : 0);
                command.Parameters.AddWithValue("@ProcessingTime", processingTime);
                command.Parameters.AddWithValue("@EvaluationNotes", (object)evaluationNotes ?? DBNull.Value);
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
        }

        // Helper to update Agents table counts
        private async Task UpdateAgentInteractionCountsAsync(int agentId, bool isCorrect, SqliteConnection connection, SqliteTransaction transaction)
        {
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    UPDATE Agents
                    SET TotalInteractions = TotalInteractions + 1,
                        SuccessfulInteractions = SuccessfulInteractions + @SuccessIncrement
                    WHERE AgentId = @AgentId;";
                command.Parameters.AddWithValue("@AgentId", agentId);
                command.Parameters.AddWithValue("@SuccessIncrement", isCorrect ? 1 : 0);
                await command.ExecuteNonQueryAsync();
            }
        }

        // Helper to update/insert AgentPerformance records
        private async Task UpsertAgentPerformanceAsync(int agentId, int versionId, string taskType, bool isCorrect, float processingTime, SqliteConnection connection, SqliteTransaction transaction)
        {
            using (var command = new SqliteCommand("", connection))
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO AgentPerformance (AgentId, VersionId, TaskType, CorrectResponses, TotalAttempts, AverageResponseTime, LastEvaluationDate)
                    VALUES (@AgentId, @VersionId, @TaskType, @CorrectIncrement, 1, @ProcessingTime, CURRENT_TIMESTAMP)
                    ON CONFLICT(AgentId, VersionId, TaskType) DO UPDATE SET
                        CorrectResponses = CorrectResponses + excluded.CorrectResponses,
                        TotalAttempts = TotalAttempts + 1,
                        -- Calculate new average: (old_avg * old_attempts + new_time) / new_attempts
                        AverageResponseTime = (IIF(TotalAttempts > 0, AverageResponseTime * TotalAttempts, 0) + excluded.AverageResponseTime) / (TotalAttempts + 1),
                        LastEvaluationDate = excluded.LastEvaluationDate;";

                command.Parameters.AddWithValue("@AgentId", agentId);
                command.Parameters.AddWithValue("@VersionId", versionId);
                command.Parameters.AddWithValue("@TaskType", taskType);
                command.Parameters.AddWithValue("@CorrectIncrement", isCorrect ? 1 : 0);
                command.Parameters.AddWithValue("@ProcessingTime", processingTime);
                await command.ExecuteNonQueryAsync();
            }
        }



        /// <summary>
        /// Gets the complete prompt for an agent's CURRENT ACTIVE version.
        /// In the Cognitive Architecture model, this is just the base prompt. 
        /// </summary>
        public async Task<string> GetCompleteAgentPromptAsync(int agentId)
        {
            // if (!_initialized) Initialize(); // Ensure initialized if needed

            var version = await GetCurrentAgentVersionAsync(agentId); // Gets the ACTIVE version
            if (version == null)
            {
                Debug.WriteLine($"[GetCompleteAgentPromptAsync Warning] No active version found for agent ID {agentId}. Returning null.");
                // Consider throwing an exception or returning a default error prompt if an active version is mandatory
                // For now, returning null to indicate failure to find the prompt.
                var agentInfo = await GetAgentAsync(agentId); // Get agent name for logging
                string agentName = agentInfo?.Name ?? $"ID {agentId}";
                // Log error to Form1 or central Logger if available
                // Example: LogError($"Critical: No active prompt version found for agent '{agentName}'.");
                return null; // Indicate failure
            }

            // --- COGNITIVE ARCHITECTURE: Return ONLY the base prompt ---
            Debug.WriteLine($"[GetCompleteAgentPromptAsync] Returning base prompt for agent ID {agentId} (Version: {version.VersionNumber}).");
            return version.Prompt;

        }




        // --- Agent Management Methods (Example: AddAgentAsync) ---
        /// <summary>
        /// Adds a new agent and its initial version to the database.
        /// </summary>
        public async Task<int> AddAgentAsync(string name, string purpose, string initialPrompt,
                 string comments = null, string knownIssues = null, string createdBy = "System")
        {
            //  if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int agentId;
                        // Insert into Agents table
                        string agentInsertSql = @"
                            INSERT INTO Agents (Name, Purpose, CreatedDate, LastModifiedDate)
                            VALUES (@Name, @Purpose, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                            SELECT last_insert_rowid();";

                        // *** CORRECTED CONSTRUCTOR USAGE ***
                        using (var command = new SqliteCommand(agentInsertSql, connection, transaction))
                        {
                            // command.Transaction = transaction; // Already set in constructor
                            command.Parameters.AddWithValue("@Name", name);
                            command.Parameters.AddWithValue("@Purpose", purpose);
                            // command.Parameters.AddWithValue("@CreatedDate", DateTime.Now); // Using CURRENT_TIMESTAMP
                            // command.Parameters.AddWithValue("@LastModifiedDate", DateTime.Now); // Using CURRENT_TIMESTAMP
                            agentId = Convert.ToInt32(await command.ExecuteScalarAsync());
                        } // command disposed here

                        // Insert initial version (Version 1, IsActive = 1)
                        string versionInsertSql = @"
                            INSERT INTO AgentVersions (AgentId, VersionNumber, Prompt, Comments, KnownIssues, CreatedBy, IsActive, CreatedDate)
                            VALUES (@AgentId, 1, @Prompt, @Comments, @KnownIssues, @CreatedBy, 1, CURRENT_TIMESTAMP);";

                        // *** CORRECTED CONSTRUCTOR USAGE ***
                        using (var command = new SqliteCommand(versionInsertSql, connection, transaction))
                        {
                            // command.Transaction = transaction; // Already set in constructor
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            command.Parameters.AddWithValue("@Prompt", initialPrompt);
                            command.Parameters.AddWithValue("@Comments", (object)comments ?? DBNull.Value);
                            command.Parameters.AddWithValue("@KnownIssues", (object)knownIssues ?? DBNull.Value);
                            command.Parameters.AddWithValue("@CreatedBy", (object)createdBy ?? DBNull.Value);
                            // command.Parameters.AddWithValue("@CreatedDate", DateTime.Now); // Using CURRENT_TIMESTAMP
                            await command.ExecuteNonQueryAsync();
                        } // command disposed here

                        transaction.Commit();
                        return agentId;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Debug.WriteLine($"Error in AddAgentAsync for '{name}': {ex.Message}");
                        throw; // Re-throw after logging
                    }
                } // transaction disposed here
            } // connection disposed here
        }



        private string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
                return input;
            return input.Substring(0, maxLength) + "...";
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // No explicit connection pool to manage here, as SQLite connections are typically opened/closed per method.
                    // If you were using a persistent connection or pool, dispose it here.
                    SqliteConnection.ClearAllPools(); // Helps ensure connections are closed if pooling is active implicitly
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Finalizer (optional, for unmanaged resources if any)
        ~AgentDatabase()
        {
            Dispose(false);
        }






        public async Task<bool> UpdateAgentAsync(int agentId, string name = null, string purpose = null, bool? isActive = null)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    var updates = new List<string>();

                    if (name != null)
                        updates.Add("Name = @Name");

                    if (purpose != null)
                        updates.Add("Purpose = @Purpose");

                    if (isActive.HasValue)
                        updates.Add("IsActive = @IsActive");

                    if (updates.Count == 0)
                        return true; // Nothing to update

                    updates.Add("LastModifiedDate = @LastModifiedDate");

                    command.CommandText = $@"
                    UPDATE Agents
                    SET {string.Join(", ", updates)}
                    WHERE AgentId = @AgentId;";

                    command.Parameters.AddWithValue("@AgentId", agentId);
                    command.Parameters.AddWithValue("@LastModifiedDate", DateTime.Now);

                    if (name != null)
                        command.Parameters.AddWithValue("@Name", name);

                    if (purpose != null)
                        command.Parameters.AddWithValue("@Purpose", purpose);

                    if (isActive.HasValue)
                        command.Parameters.AddWithValue("@IsActive", isActive.Value ? 1 : 0);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }

        //        /// <summary>
        //        /// Deletes an agent and all associated data.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent to delete.</param>
        //        /// <returns>True if the deletion was successful.</returns>
        public async Task<bool> DeleteAgentAsync(int agentId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction

                            // Delete from TeamMembers first (foreign key)
                            command.CommandText = "DELETE FROM TeamMembers WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            await command.ExecuteNonQueryAsync();
                            command.Parameters.Clear();

                            // Delete from InteractionHistory
                            command.CommandText = "DELETE FROM InteractionHistory WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            await command.ExecuteNonQueryAsync();
                            command.Parameters.Clear();

                            // Delete from AgentPerformance
                            command.CommandText = "DELETE FROM AgentPerformance WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            await command.ExecuteNonQueryAsync();
                            command.Parameters.Clear();

                            // Delete from AgentCapabilities
                            command.CommandText = "DELETE FROM AgentCapabilities WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            await command.ExecuteNonQueryAsync();
                            command.Parameters.Clear();

                            // Get all version IDs to delete from PromptModifications
                            command.CommandText = "SELECT VersionId FROM AgentVersions WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            var versionIds = new List<int>();
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    versionIds.Add(reader.GetInt32(0));
                                }
                            }
                            command.Parameters.Clear();

                            // Delete from PromptModifications
                            if (versionIds.Count > 0)
                            {
                                foreach (var versionId in versionIds)
                                {
                                    command.CommandText = "DELETE FROM PromptModifications WHERE VersionId = @VersionId OR PreviousVersionId = @VersionId;";
                                    command.Parameters.AddWithValue("@VersionId", versionId);
                                    await command.ExecuteNonQueryAsync();
                                    command.Parameters.Clear();
                                }
                            }

                            // Delete from AgentVersions
                            command.CommandText = "DELETE FROM AgentVersions WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            await command.ExecuteNonQueryAsync();
                            command.Parameters.Clear();

                            // Finally delete the agent
                            command.CommandText = "DELETE FROM Agents WHERE AgentId = @AgentId;";
                            command.Parameters.AddWithValue("@AgentId", agentId);
                            int rowsAffected = await command.ExecuteNonQueryAsync();

                            transaction.Commit();
                            return rowsAffected > 0;
                        }
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }



        public async Task<bool> RemoveAgentCompletelyAsync(int agentId)
        {
            if (agentId <= 0)
            {
                Debug.WriteLine($"[RemoveAgentCompletelyAsync Error] Invalid Agent ID: {agentId}");
                return false;
            }

            Debug.WriteLine($"[RemoveAgentCompletelyAsync] Attempting to remove Agent ID: {agentId} and all associated data...");

            bool success = false;
            SqliteTransaction? transaction = null; // Nullable SqliteTransaction

            // Use Task.Run for database operations to avoid blocking potential UI thread
            await Task.Run(() =>
            {
                lock (_dbLock) // Ensure thread safety for DB operations
                {
                    using (var connection = GetConnection()) // Assumes GetConnection() returns an open connection
                    {
                        try
                        {
                            connection.Open(); // Ensure connection is open if GetConnection doesn't guarantee it
                            transaction = connection.BeginTransaction(); // Start transaction

                            // --- Deletion Order Matters if Foreign Keys are Enforced ---

                            // 1. Delete from tables referencing AgentVersions (which references Agents)
                            //    - AgentPerformance (references AgentVersions.VersionId, Agents.AgentId)
                            //    - InteractionHistory (references AgentVersions.VersionId, Agents.AgentId)
                            //    - PromptModifications (references AgentVersions.VersionId twice)
                            //    We need to get all VersionIds for the agent first.
                            var versionIds = new List<int>();
                            using (var cmdVersions = new SqliteCommand("SELECT VersionId FROM AgentVersions WHERE AgentId = @AgentId", connection, transaction))
                            {
                                cmdVersions.Parameters.AddWithValue("@AgentId", agentId);
                                using (var reader = cmdVersions.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        versionIds.Add(reader.GetInt32(0));
                                    }
                                }
                            }
                            Debug.WriteLine($"[RemoveAgentCompletelyAsync] Found {versionIds.Count} VersionIds for Agent ID {agentId}.");

                            if (versionIds.Any())
                            {
                                string versionIdList = string.Join(",", versionIds);
                                // Delete from AgentPerformance referencing these versions OR the agent directly
                                using (var cmd = new SqliteCommand($"DELETE FROM AgentPerformance WHERE AgentId = @AgentId OR VersionId IN ({versionIdList})", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@AgentId", agentId);
                                    int deleted = cmd.ExecuteNonQuery();
                                    Debug.WriteLine($"[RemoveAgentCompletelyAsync] Deleted {deleted} rows from AgentPerformance.");
                                }
                                // Delete from InteractionHistory referencing these versions OR the agent directly
                                using (var cmd = new SqliteCommand($"DELETE FROM InteractionHistory WHERE AgentId = @AgentId OR VersionId IN ({versionIdList})", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@AgentId", agentId);
                                    int deleted = cmd.ExecuteNonQuery();
                                    Debug.WriteLine($"[RemoveAgentCompletelyAsync] Deleted {deleted} rows from InteractionHistory.");
                                }
                                // Delete from PromptModifications referencing these versions
                                using (var cmd = new SqliteCommand($"DELETE FROM PromptModifications WHERE VersionId IN ({versionIdList}) OR PreviousVersionId IN ({versionIdList})", connection, transaction))
                                {
                                    // No agentId parameter needed here as it only references VersionId
                                    int deleted = cmd.ExecuteNonQuery();
                                    Debug.WriteLine($"[RemoveAgentCompletelyAsync] Deleted {deleted} rows from PromptModifications.");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"[RemoveAgentCompletelyAsync] No versions found, skipping dependent table deletions (AgentPerformance, InteractionHistory, PromptModifications).");
                            }


                            // 2. Delete from tables directly referencing Agents
                            //    - AgentCapabilities
                            using (var cmd = new SqliteCommand("DELETE FROM AgentCapabilities WHERE AgentId = @AgentId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@AgentId", agentId);
                                int deleted = cmd.ExecuteNonQuery();
                                Debug.WriteLine($"[RemoveAgentCompletelyAsync] Deleted {deleted} rows from AgentCapabilities.");
                            }
                            //    - TeamMembers (Agent is a member)
                            using (var cmd = new SqliteCommand("DELETE FROM TeamMembers WHERE AgentId = @AgentId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@AgentId", agentId);
                                int deleted = cmd.ExecuteNonQuery();
                                Debug.WriteLine($"[RemoveAgentCompletelyAsync] Deleted {deleted} rows from TeamMembers.");
                            }
                            //    - AgentVersions (Must be deleted before Agent)
                            using (var cmd = new SqliteCommand("DELETE FROM AgentVersions WHERE AgentId = @AgentId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@AgentId", agentId);
                                int deleted = cmd.ExecuteNonQuery();
                                Debug.WriteLine($"[RemoveAgentCompletelyAsync] Deleted {deleted} rows from AgentVersions.");
                            }
                            //    - TeamCompositions (Agent is the Chief - WARNING: This leaves team without chief!)
                            //      Consider if you want to delete the team or handle reassignment separately.
                            //      We will NOT delete from TeamCompositions here by default.
                            //      You could add: DELETE FROM TeamCompositions WHERE ChiefAgentId = @AgentId


                            // 3. Finally, delete the agent from the Agents table
                            using (var cmd = new SqliteCommand("DELETE FROM Agents WHERE AgentId = @AgentId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@AgentId", agentId);
                                int deleted = cmd.ExecuteNonQuery();
                                Debug.WriteLine($"[RemoveAgentCompletelyAsync] Deleted {deleted} rows from Agents.");
                                if (deleted == 0)
                                {
                                    Debug.WriteLine($"[RemoveAgentCompletelyAsync Warning] Agent ID {agentId} was not found in the Agents table itself.");
                                    // Decide if this is an error or acceptable (maybe already deleted?)
                                    // We'll consider it success if other related data was potentially removed.
                                }
                            }

                            // If all commands succeeded, commit the transaction
                            transaction.Commit();
                            success = true;
                            Debug.WriteLine($"[RemoveAgentCompletelyAsync] Successfully removed Agent ID: {agentId} and associated data. Transaction committed.");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[RemoveAgentCompletelyAsync Error] Exception occurred: {ex.Message}\n{ex.StackTrace}");
                            try
                            {
                                transaction?.Rollback(); // Rollback on any error
                                Debug.WriteLine("[RemoveAgentCompletelyAsync] Transaction rolled back due to error.");
                            }
                            catch (Exception rbEx)
                            {
                                Debug.WriteLine($"[RemoveAgentCompletelyAsync Error] Exception during transaction rollback: {rbEx.Message}");
                            }
                            success = false;
                        }
                        finally
                        {
                            transaction?.Dispose(); // Dispose transaction object
                                                    // Close connection only if GetConnection doesn't manage pooling/lifetime
                                                    // If GetConnection provides a new connection each time: connection.Close();
                        }
                    } // using connection
                } // lock
            }); // Task.Run

            return success;
        }



        public async Task<bool> RemoveTeamCompletelyAsync(int teamId)
        {
            if (teamId <= 0)
            {
                Debug.WriteLine($"[RemoveTeamCompletelyAsync Error] Invalid Team ID: {teamId}");
                return false;
            }

            Debug.WriteLine($"[RemoveTeamCompletelyAsync] Attempting to remove Team ID: {teamId} and its members...");

            bool success = false;
            SqliteTransaction? transaction = null;

            // Use Task.Run for database operations
            await Task.Run(() =>
            {
                lock (_dbLock) // Ensure thread safety
                {
                    using (var connection = GetConnection())
                    {
                        try
                        {
                            connection.Open();
                            transaction = connection.BeginTransaction();

                            // --- Deletion Order ---
                            // 1. Delete from TeamMembers first (references TeamCompositions)
                            using (var cmdMembers = new SqliteCommand("DELETE FROM TeamMembers WHERE TeamId = @TeamId", connection, transaction))
                            {
                                cmdMembers.Parameters.AddWithValue("@TeamId", teamId);
                                int deletedMembers = cmdMembers.ExecuteNonQuery();
                                Debug.WriteLine($"[RemoveTeamCompletelyAsync] Deleted {deletedMembers} rows from TeamMembers for Team ID {teamId}.");
                            }

                            // 2. Delete from TeamCompositions
                            using (var cmdTeam = new SqliteCommand("DELETE FROM TeamCompositions WHERE TeamId = @TeamId", connection, transaction))
                            {
                                cmdTeam.Parameters.AddWithValue("@TeamId", teamId);
                                int deletedTeams = cmdTeam.ExecuteNonQuery();
                                Debug.WriteLine($"[RemoveTeamCompletelyAsync] Deleted {deletedTeams} rows from TeamCompositions for Team ID {teamId}.");
                                if (deletedTeams == 0)
                                {
                                    Debug.WriteLine($"[RemoveTeamCompletelyAsync Warning] Team ID {teamId} was not found in the TeamCompositions table.");
                                    // If members were deleted but the team wasn't found, maybe still consider it success? Or partial failure?
                                    // Let's consider it success if no errors occurred, even if team was already gone.
                                }
                            }

                            // Commit transaction if all commands succeeded
                            transaction.Commit();
                            success = true;
                            Debug.WriteLine($"[RemoveTeamCompletelyAsync] Successfully removed Team ID: {teamId} and associated members. Transaction committed.");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[RemoveTeamCompletelyAsync Error] Exception occurred for Team ID {teamId}: {ex.Message}\n{ex.StackTrace}");
                            try
                            {
                                transaction?.Rollback();
                                Debug.WriteLine($"[RemoveTeamCompletelyAsync] Transaction rolled back for Team ID {teamId} due to error.");
                            }
                            catch (Exception rbEx)
                            {
                                Debug.WriteLine($"[RemoveTeamCompletelyAsync Error] Exception during transaction rollback for Team ID {teamId}: {rbEx.Message}");
                            }
                            success = false;
                        }
                        finally
                        {
                            transaction?.Dispose();
                            // connection.Close(); // If needed
                        }
                    } // using connection
                } // lock
            }); // Task.Run

            return success;
        }

        //        /// <summary>
        //        /// Gets all agents in the database.
        //        /// </summary>
        //        /// <param name="activeOnly">Whether to retrieve only active agents.</param>
        //        /// <returns>A list of AgentInfo objects.</returns>

        public async Task<int> GetAgentId(string agentName) // Renamed parameter for consistency
        {
            if (string.IsNullOrWhiteSpace(agentName)) return -1; // Handle empty/null input
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqliteCommand("", connection))
                {
                    // For case-insensitive comparison if your 'Name' column doesn't have COLLATE NOCASE
                    command.CommandText = @"
                    SELECT AgentId
                    FROM Agents
                    WHERE LOWER(Name) = LOWER(@Name);"; // Query uses LOWER()
                    command.Parameters.AddWithValue("@Name", agentName); // Pass original name, SQL handles conversion

                    // If 'Name' column has COLLATE NOCASE, you can use:
                    // command.CommandText = "SELECT AgentId FROM Agents WHERE Name = @Name;";
                    // command.Parameters.AddWithValue("@Name", agentName);

                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result);
                    }
                    return -1; // Not found
                }
            }
        }

        /// <summary>
        /// Retrieves the agent information by the specified agent name.
        /// </summary>
        /// <param name="agentName">The name of the agent to retrieve information for.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the agent information if found; otherwise, null.</returns>
        /// <summary>

        public async Task<AgentInfo> GetAgentUsingNameAsync(string agentName)
        {
            var agentId = await GetAgentId(agentName);

            if (agentId <= 0) // Check if agentId is valid
            {
                return new AgentInfo(); // Return null if agent not found
            }

            var agent = await GetAgentAsync(agentId);

            return agent; // Return the AgentInfo object directly
        }



        //        /// <summary>
        //        /// Gets an agent by ID.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent to retrieve.</param>
        //        /// <returns>An AgentInfo object or null if not found.</returns>
        public async Task<AgentInfo> GetAgentAsync(int agentId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT AgentId, Name, Purpose, CreatedDate, LastModifiedDate, IsActive,
                                   BaseScore, TotalInteractions, SuccessfulInteractions
                            FROM Agents
                            WHERE AgentId = @AgentId;";

                    command.Parameters.AddWithValue("@AgentId", agentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new AgentInfo
                            {
                                AgentId = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Purpose = reader.GetString(2),
                                CreatedDate = reader.GetDateTime(3),
                                LastModifiedDate = reader.GetDateTime(4),
                                IsActive = reader.GetBoolean(5),
                                BaseScore = reader.GetFloat(6),
                                TotalInteractions = reader.GetInt32(7),
                                SuccessfulInteractions = reader.GetInt32(8)
                            };
                        }
                    }
                }
            }

            return null;
        }



        public async Task<List<AgentInfo>> GetAllAgentsAsync(bool activeOnly = false)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    string query = @"
                            SELECT AgentId, Name, Purpose, CreatedDate, LastModifiedDate, IsActive,
                                   BaseScore, TotalInteractions, SuccessfulInteractions
                            FROM Agents";

                    if (activeOnly)
                    {
                        query += " WHERE IsActive = 1";
                    }

                    query += " ORDER BY Name;";

                    command.CommandText = query;

                    var agents = new List<AgentInfo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            agents.Add(new AgentInfo
                            {
                                AgentId = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Purpose = reader.GetString(2),
                                CreatedDate = reader.GetDateTime(3),
                                LastModifiedDate = reader.GetDateTime(4),
                                IsActive = reader.GetBoolean(5),
                                BaseScore = reader.GetFloat(6),
                                TotalInteractions = reader.GetInt32(7),
                                SuccessfulInteractions = reader.GetInt32(8)
                            });
                        }
                    }

                    return agents;
                }
            }
        }

        //        #endregion

        //        #region Agent Version Management

        //        /// <summary>
        //        /// Adds a new version of an agent's prompt.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <param name="newPrompt">The new prompt text.</param>
        //        /// <param name="modificationReason">The reason for the modification.</param>
        //        /// <param name="changeSummary">A summary of changes from the previous version.</param>
        //        /// <param name="comments">Optional comments about this version.</param>
        //        /// <param name="knownIssues">Optional known issues with this version.</param>
        //        /// <param name="createdBy">The creator of this version.</param>
        //        /// <param name="performanceBeforeChange">The performance score before this change.</param>
        //        /// <returns>The new version number.</returns>
        public async Task<int> AddAgentVersionAsync(int agentId, string newPrompt, string modificationReason,
            string changeSummary, string comments = null, string knownIssues = null, string createdBy = null,
            float performanceBeforeChange = 0)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Get current version number and versionId
                        int currentVersionNumber = 0;
                        int currentVersionId = 0;

                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction
                            command.CommandText = @"
                                    SELECT VersionId, VersionNumber
                                    FROM AgentVersions
                                    WHERE AgentId = @AgentId
                                    ORDER BY VersionNumber DESC
                                    LIMIT 1;";

                            command.Parameters.AddWithValue("@AgentId", agentId);

                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    currentVersionId = reader.GetInt32(0);
                                    currentVersionNumber = reader.GetInt32(1);
                                }
                            }
                        }

                        // Set all existing versions as inactive
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction
                            command.CommandText = @"
                                    UPDATE AgentVersions
                                    SET IsActive = 0
                                    WHERE AgentId = @AgentId;";

                            command.Parameters.AddWithValue("@AgentId", agentId);
                            await command.ExecuteNonQueryAsync();
                        }

                        // Insert new version
                        int newVersionId;
                        int newVersionNumber = currentVersionNumber + 1;

                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction
                            command.CommandText = @"
                                    INSERT INTO AgentVersions
                                    (AgentId, VersionNumber, Prompt, Comments, KnownIssues, CreatedDate, CreatedBy, IsActive)
                                    VALUES
                                    (@AgentId, @VersionNumber, @Prompt, @Comments, @KnownIssues, @CreatedDate, @CreatedBy, 1);
                                    SELECT last_insert_rowid();";

                            command.Parameters.AddWithValue("@AgentId", agentId);
                            command.Parameters.AddWithValue("@VersionNumber", newVersionNumber);
                            command.Parameters.AddWithValue("@Prompt", newPrompt);
                            command.Parameters.AddWithValue("@Comments", comments ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@KnownIssues", knownIssues ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@CreatedDate", DateTime.Now);
                            command.Parameters.AddWithValue("@CreatedBy", createdBy ?? (object)DBNull.Value);

                            newVersionId = Convert.ToInt32(await command.ExecuteScalarAsync());
                        }

                        // Record the modification
                        if (currentVersionId > 0)
                        {
                            using (var command = new SqliteCommand("", connection))
                            {
                                command.Transaction = transaction; // Assign transaction
                                command.CommandText = @"
                                        INSERT INTO PromptModifications
                                        (VersionId, PreviousVersionId, ModificationReason, ChangeSummary, PerformanceBeforeChange, ModificationDate)
                                        VALUES
                                        (@VersionId, @PreviousVersionId, @ModificationReason, @ChangeSummary, @PerformanceBeforeChange, @ModificationDate);";

                                command.Parameters.AddWithValue("@VersionId", newVersionId);
                                command.Parameters.AddWithValue("@PreviousVersionId", currentVersionId);
                                command.Parameters.AddWithValue("@ModificationReason", modificationReason);
                                command.Parameters.AddWithValue("@ChangeSummary", changeSummary);
                                command.Parameters.AddWithValue("@PerformanceBeforeChange", performanceBeforeChange);
                                command.Parameters.AddWithValue("@ModificationDate", DateTime.Now);

                                await command.ExecuteNonQueryAsync();
                            }
                        }

                        // Update agent's LastModifiedDate
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction
                            command.CommandText = @"
                                    UPDATE Agents
                                    SET LastModifiedDate = @LastModifiedDate
                                    WHERE AgentId = @AgentId;";

                            command.Parameters.AddWithValue("@AgentId", agentId);
                            command.Parameters.AddWithValue("@LastModifiedDate", DateTime.Now);

                            await command.ExecuteNonQueryAsync();
                        }

                        transaction.Commit();
                        return newVersionNumber;
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        //        /// <summary>
        //        /// Gets the current active version of an agent's prompt.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <returns>An AgentVersionInfo object or null if not found.</returns>
        public async Task<AgentVersionInfo> GetCurrentAgentVersionAsync(int agentId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT VersionId, AgentId, VersionNumber, Prompt, Comments, KnownIssues,
                                   CreatedDate, CreatedBy, PerformanceScore
                            FROM AgentVersions
                            WHERE AgentId = @AgentId AND IsActive = 1;";

                    command.Parameters.AddWithValue("@AgentId", agentId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new AgentVersionInfo
                            {
                                VersionId = reader.GetInt32(0),
                                AgentId = reader.GetInt32(1),
                                VersionNumber = reader.GetInt32(2),
                                Prompt = reader.GetString(3),
                                Comments = reader.IsDBNull(4) ? null : reader.GetString(4),
                                KnownIssues = reader.IsDBNull(5) ? null : reader.GetString(5),
                                CreatedDate = reader.GetDateTime(6),
                                CreatedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                                PerformanceScore = reader.GetFloat(8)
                            };
                        }
                    }
                }
            }

            return null;
        }

        //        /// <summary>
        //        /// Gets the version history for an agent.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <returns>A list of AgentVersionInfo objects ordered by version number.</returns>
        public async Task<List<AgentVersionInfo>> GetAgentVersionHistoryAsync(int agentId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT VersionId, AgentId, VersionNumber, Prompt, Comments, KnownIssues,
                                   CreatedDate, CreatedBy, PerformanceScore, IsActive
                            FROM AgentVersions
                            WHERE AgentId = @AgentId
                            ORDER BY VersionNumber DESC;";

                    command.Parameters.AddWithValue("@AgentId", agentId);

                    var versions = new List<AgentVersionInfo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            versions.Add(new AgentVersionInfo
                            {
                                VersionId = reader.GetInt32(0),
                                AgentId = reader.GetInt32(1),
                                VersionNumber = reader.GetInt32(2),
                                Prompt = reader.GetString(3),
                                Comments = reader.IsDBNull(4) ? null : reader.GetString(4),
                                KnownIssues = reader.IsDBNull(5) ? null : reader.GetString(5),
                                CreatedDate = reader.GetDateTime(6),
                                CreatedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                                PerformanceScore = reader.GetFloat(8),
                                IsActive = reader.GetBoolean(9)
                            });
                        }
                    }

                    return versions;
                }
            }
        }

        //        /// <summary>
        //        /// Updates the performance score of an agent version.
        //        /// </summary>
        //        /// <param name="versionId">The ID of the version to update.</param>
        //        /// <param name="performanceScore">The new performance score.</param>
        //        /// <returns>True if the update was successful.</returns>
        public async Task<bool> UpdateVersionPerformanceScoreAsync(int versionId, float performanceScore)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction
                            command.CommandText = @"
                                    UPDATE AgentVersions
                                    SET PerformanceScore = @PerformanceScore
                                    WHERE VersionId = @VersionId;";

                            command.Parameters.AddWithValue("@VersionId", versionId);
                            command.Parameters.AddWithValue("@PerformanceScore", performanceScore);

                            int rowsAffected = await command.ExecuteNonQueryAsync();

                            if (rowsAffected > 0)
                            {
                                // Update PromptModifications record if exists
                                command.CommandText = @"
                                        UPDATE PromptModifications
                                        SET PerformanceAfterChange = @PerformanceScore
                                        WHERE VersionId = @VersionId;";

                                // Parameters are already set, no need to clear
                                // command.Parameters.Clear();
                                // command.Parameters.AddWithValue("@VersionId", versionId);
                                // command.Parameters.AddWithValue("@PerformanceScore", performanceScore);

                                await command.ExecuteNonQueryAsync();
                            }

                            transaction.Commit();
                            return rowsAffected > 0;
                        }
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        //        #endregion

        //        #region Performance Tracking


        //        /// <summary>
        //        /// Gets performance statistics for an agent by task type.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <param name="taskType">Optional task type filter.</param>
        //        /// <returns>A list of AgentPerformanceInfo objects.</returns>
        public async Task<List<AgentPerformanceInfo>> GetAgentPerformanceStatsAsync(int agentId, string taskType = null)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    string query = @"
                            SELECT p.AgentId, p.VersionId, v.VersionNumber, p.TaskType,
                                   p.CorrectResponses, p.TotalAttempts, p.AverageResponseTime, p.LastEvaluationDate
                            FROM AgentPerformance p
                            JOIN AgentVersions v ON p.VersionId = v.VersionId
                            WHERE p.AgentId = @AgentId";

                    if (!string.IsNullOrEmpty(taskType))
                    {
                        query += " AND p.TaskType = @TaskType";
                    }

                    query += " ORDER BY p.TaskType, v.VersionNumber DESC;";

                    command.CommandText = query;
                    command.Parameters.AddWithValue("@AgentId", agentId);

                    if (!string.IsNullOrEmpty(taskType))
                    {
                        command.Parameters.AddWithValue("@TaskType", taskType);
                    }

                    var performanceStats = new List<AgentPerformanceInfo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            performanceStats.Add(new AgentPerformanceInfo
                            {
                                AgentId = reader.GetInt32(0),
                                VersionId = reader.GetInt32(1),
                                VersionNumber = reader.GetInt32(2),
                                TaskType = reader.GetString(3),
                                CorrectResponses = reader.GetInt32(4),
                                TotalAttempts = reader.GetInt32(5),
                                AverageResponseTime = reader.IsDBNull(6) ? 0 : reader.GetFloat(6), // Handle null
                                LastEvaluationDate = reader.GetDateTime(7)
                            });
                        }
                    }

                    return performanceStats;
                }
            }
        }

        //        /// <summary>
        //        /// Gets recent interactions for an agent.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <param name="limit">The maximum number of interactions to retrieve.</param>
        //        /// <returns>A list of InteractionInfo objects.</returns>
        public async Task<List<InteractionInfo>> GetRecentInteractionsAsync(int agentId, int limit = 20)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT i.InteractionId, i.AgentId, i.VersionId, v.VersionNumber, i.TaskType,
                                   i.RequestData, i.ResponseData, i.IsCorrect, i.ProcessingTime,
                                   i.InteractionDate, i.EvaluationNotes
                            FROM InteractionHistory i
                            JOIN AgentVersions v ON i.VersionId = v.VersionId
                            WHERE i.AgentId = @AgentId
                            ORDER BY i.InteractionDate DESC
                            LIMIT @Limit;";

                    command.Parameters.AddWithValue("@AgentId", agentId);
                    command.Parameters.AddWithValue("@Limit", limit);

                    var interactions = new List<InteractionInfo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            interactions.Add(new InteractionInfo
                            {
                                InteractionId = reader.GetInt32(0),
                                AgentId = reader.GetInt32(1),
                                VersionId = reader.GetInt32(2),
                                VersionNumber = reader.GetInt32(3),
                                TaskType = reader.GetString(4),
                                RequestData = reader.GetString(5),
                                ResponseData = reader.GetString(6),
                                IsCorrect = reader.IsDBNull(7) ? false : reader.GetBoolean(7), // Handle null
                                ProcessingTime = reader.IsDBNull(8) ? 0 : reader.GetFloat(8), // Handle null
                                InteractionDate = reader.GetDateTime(9),
                                EvaluationNotes = reader.IsDBNull(10) ? null : reader.GetString(10)
                            });
                        }
                    }

                    return interactions;
                }
            }
        }

        //        #endregion

        //        #region Agent Capabilities

        //        /// <summary>
        //        /// Adds a capability to an agent.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <param name="capabilityName">The name of the capability.</param>
        //        /// <param name="capabilityDescription">A Description of the capability.</param>
        //        /// <param name="initialRating">Initial performance rating for this capability.</param>
        //        /// <returns>The ID of the newly added capability.</returns>
        public async Task<int> AddAgentCapabilityAsync(int agentId, string capabilityName,
            string capabilityDescription = null, float initialRating = 0.0f)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            INSERT INTO AgentCapabilities
                            (AgentId, CapabilityName, CapabilityDescription, PerformanceRating)
                            VALUES
                            (@AgentId, @CapabilityName, @CapabilityDescription, @PerformanceRating);
                            SELECT last_insert_rowid();";

                    command.Parameters.AddWithValue("@AgentId", agentId);
                    command.Parameters.AddWithValue("@CapabilityName", capabilityName);
                    command.Parameters.AddWithValue("@CapabilityDescription", capabilityDescription ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@PerformanceRating", initialRating);

                    return Convert.ToInt32(await command.ExecuteScalarAsync());
                }
            }
        }











        //        /// <summary>
        //        /// Updates the rating for an agent capability.
        //        /// </summary>
        //        /// <param name="capabilityId">The ID of the capability.</param>
        //        /// <param name="newRating">The new performance rating.</param>
        //        /// <returns>True if the update was successful.</returns>
        public async Task<bool> UpdateCapabilityRatingAsync(int capabilityId, float newRating)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            UPDATE AgentCapabilities
                            SET PerformanceRating = @PerformanceRating
                            WHERE CapabilityId = @CapabilityId;";

                    command.Parameters.AddWithValue("@CapabilityId", capabilityId);
                    command.Parameters.AddWithValue("@PerformanceRating", newRating);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }

        //        /// <summary>
        //        /// Gets all capabilities for an agent.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <returns>A list of AgentCapabilityInfo objects.</returns>
        public async Task<List<AgentCapabilityInfo>> GetAgentCapabilitiesAsync(int agentId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT CapabilityId, AgentId, CapabilityName, CapabilityDescription, PerformanceRating
                            FROM AgentCapabilities
                            WHERE AgentId = @AgentId
                            ORDER BY CapabilityName;";

                    command.Parameters.AddWithValue("@AgentId", agentId);

                    var capabilities = new List<AgentCapabilityInfo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            capabilities.Add(new AgentCapabilityInfo
                            {
                                CapabilityId = reader.GetInt32(0),
                                AgentId = reader.GetInt32(1),
                                CapabilityName = reader.GetString(2),
                                CapabilityDescription = reader.IsDBNull(3) ? null : reader.GetString(3),
                                PerformanceRating = reader.GetFloat(4)
                            });
                        }
                    }

                    return capabilities;
                }
            }
        }

        //        #endregion

        //        #region Team Management

        //        /// <summary>
        //        /// Creates a new team with a chief agent.
        //        /// </summary>
        //        /// <param name="teamName">The name of the team.</param>
        //        /// <param name="chiefAgentId">The ID of the chief agent.</param>
        //        /// <param name="Description">Optional Description of the team.</param>
        //        /// <returns>The ID of the newly created team.</returns>
        public async Task<int> CreateTeamAsync(string teamName, int chiefAgentId, string description = null)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int teamId;
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction
                            command.CommandText = @"
                                    INSERT INTO TeamCompositions
                                    (TeamName, ChiefAgentId, Description, CreatedDate)
                                    VALUES
                                    (@TeamName, @ChiefAgentId, @Description, @CreatedDate);
                                    SELECT last_insert_rowid();";

                            command.Parameters.AddWithValue("@TeamName", teamName);
                            command.Parameters.AddWithValue("@ChiefAgentId", chiefAgentId);
                            command.Parameters.AddWithValue("@Description", description ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@CreatedDate", DateTime.Now);

                            teamId = Convert.ToInt32(await command.ExecuteScalarAsync());
                        }

                        // Add chief as a team member with "Chief" role
                        using (var command = new SqliteCommand("", connection))
                        {
                            command.Transaction = transaction; // Assign transaction
                            command.CommandText = @"
                                    INSERT INTO TeamMembers
                                    (TeamId, AgentId, Role, AssignmentReason)
                                    VALUES
                                    (@TeamId, @AgentId, 'Chief', 'Team leader');";

                            command.Parameters.AddWithValue("@TeamId", teamId);
                            command.Parameters.AddWithValue("@AgentId", chiefAgentId);

                            await command.ExecuteNonQueryAsync();
                        }

                        transaction.Commit();
                        return teamId;
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        //        /// <summary>
        //        /// Adds an agent to a team.
        //        /// </summary>
        //        /// <param name="teamId">The ID of the team.</param>
        //        /// <param name="agentId">The ID of the agent to add.</param>
        //        /// <param name="role">The role of the agent in the team.</param>
        //        /// <param name="assignmentReason">Optional reason for the assignment.</param>
        //        /// <returns>True if the agent was added successfully.</returns>
        public async Task<bool> AddAgentToTeamAsync(int teamId, int agentId, string role, string assignmentReason = null)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Check if the agent is already in the team
                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT COUNT(*)
                            FROM TeamMembers
                            WHERE TeamId = @TeamId AND AgentId = @AgentId;";

                    command.Parameters.AddWithValue("@TeamId", teamId);
                    command.Parameters.AddWithValue("@AgentId", agentId);

                    int count = Convert.ToInt32(await command.ExecuteScalarAsync());

                    if (count > 0)
                    {
                        // Agent is already in the team, update their role
                        command.CommandText = @"
                                UPDATE TeamMembers
                                SET Role = @Role, AssignmentReason = @AssignmentReason
                                WHERE TeamId = @TeamId AND AgentId = @AgentId;";

                        command.Parameters.AddWithValue("@Role", role);
                        command.Parameters.AddWithValue("@AssignmentReason", assignmentReason ?? (object)DBNull.Value);

                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0;
                    }
                    else
                    {
                        // Add the agent to the team
                        command.CommandText = @"
                                INSERT INTO TeamMembers
                                (TeamId, AgentId, Role, AssignmentReason)
                                VALUES
                                (@TeamId, @AgentId, @Role, @AssignmentReason);";

                        command.Parameters.AddWithValue("@Role", role);
                        command.Parameters.AddWithValue("@AssignmentReason", assignmentReason ?? (object)DBNull.Value);

                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        return rowsAffected > 0;
                    }
                }
            }
        }

        //        /// <summary>
        //        /// Removes an agent from a team.
        //        /// </summary>
        //        /// <param name="teamId">The ID of the team.</param>
        //        /// <param name="agentId">The ID of the agent to remove.</param>
        //        /// <returns>True if the agent was removed successfully.</returns>
        public async Task<bool> RemoveAgentFromTeamAsync(int teamId, int agentId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    // Check if this is the chief agent
                    command.CommandText = @"
                            SELECT ChiefAgentId
                            FROM TeamCompositions
                            WHERE TeamId = @TeamId;";

                    command.Parameters.AddWithValue("@TeamId", teamId);

                    object result = await command.ExecuteScalarAsync();

                    if (result != null && result != DBNull.Value && Convert.ToInt32(result) == agentId)
                    {
                        // This is the chief agent, can't remove
                        throw new InvalidOperationException("Cannot remove the chief agent from the team.");
                    }

                    // Remove the agent from the team
                    command.CommandText = @"
                            DELETE FROM TeamMembers
                            WHERE TeamId = @TeamId AND AgentId = @AgentId;";

                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@TeamId", teamId);
                    command.Parameters.AddWithValue("@AgentId", agentId);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }





        public async Task<bool> RemoveAgentFromTeamAsync2(int teamId, int agentId)
        {
            if (teamId <= 0 || agentId <= 0)
            {
                Debug.WriteLine($"[RemoveAgentFromTeamAsync Error] Invalid Team ID ({teamId}) or Agent ID ({agentId}).");
                return false;
            }

            Debug.WriteLine($"[RemoveAgentFromTeamAsync] Attempting to remove Agent ID: {agentId} from Team ID: {teamId}...");

            bool success = false;
            if (!_initialized) Initialize();

            // Use Task.Run for database operations
            await Task.Run(() =>
            {
                lock (_dbLock) // Ensure thread safety
                {
                    using (var connection = new SqliteConnection(_connectionString))
                    //  using (var connection = GetConnection())
                    {
                        SqliteTransaction? transaction = null; // Keep transaction local if only one operation
                        try
                        {
                            connection.Open();
                            transaction = connection.BeginTransaction(); // Use transaction for atomicity

                            using (var cmd = new SqliteCommand("DELETE FROM TeamMembers WHERE TeamId = @TeamId AND AgentId = @AgentId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@TeamId", teamId);
                                cmd.Parameters.AddWithValue("@AgentId", agentId);

                                int rowsAffected = cmd.ExecuteNonQuery();

                                if (rowsAffected > 0)
                                {
                                    Debug.WriteLine($"[RemoveAgentFromTeamAsync] Successfully removed Agent ID {agentId} from Team ID {teamId}.");
                                }
                                else
                                {
                                    // It's not necessarily an error if the agent wasn't in the team to begin with.
                                    Debug.WriteLine($"[RemoveAgentFromTeamAsync] Agent ID {agentId} was not found as a member of Team ID {teamId} (or already removed).");
                                }
                            }

                            transaction.Commit();
                            success = true; // Operation completed without throwing an exception
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[RemoveAgentFromTeamAsync Error] Exception occurred for Team ID {teamId}, Agent ID {agentId}: {ex.Message}\n{ex.StackTrace}");
                            try
                            {
                                transaction?.Rollback();
                                Debug.WriteLine($"[RemoveAgentFromTeamAsync] Transaction rolled back for Team ID {teamId}, Agent ID {agentId} due to error.");
                            }
                            catch (Exception rbEx)
                            {
                                Debug.WriteLine($"[RemoveAgentFromTeamAsync Error] Exception during transaction rollback: {rbEx.Message}");
                            }
                            success = false;
                        }
                        finally
                        {
                            transaction?.Dispose();
                            // connection.Close(); // If needed
                        }
                    } // using connection
                } // lock
            }); // Task.Run

            return success;
        }









        public async Task<TeamInfo> GetTeamAsync(int teamId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Get team details
                TeamInfo team = null;
                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT TeamId, TeamName, ChiefAgentId, CreatedDate, PerformanceScore, Description
                            FROM TeamCompositions
                            WHERE TeamId = @TeamId;";

                    command.Parameters.AddWithValue("@TeamId", teamId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            team = new TeamInfo
                            {
                                TeamId = reader.GetInt32(0),
                                TeamName = reader.GetString(1),
                                ChiefAgentId = reader.GetInt32(2),
                                CreatedDate = reader.GetDateTime(3),
                                PerformanceScore = reader.IsDBNull(4) ? 0 : reader.GetFloat(4), // Handle null
                                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                                Members = new List<TeamMemberInfo>()
                            };
                        }
                    }
                }

                if (team != null)
                {
                    // Get team members
                    using (var command = new SqliteCommand("", connection))
                    {
                        command.CommandText = @"
                                SELECT tm.AgentId, a.Name, tm.Role, tm.AssignmentReason, tm.PerformanceInTeam
                                FROM TeamMembers tm
                                JOIN Agents a ON tm.AgentId = a.AgentId
                                WHERE tm.TeamId = @TeamId
                                ORDER BY CASE WHEN tm.Role = 'Chief' THEN 0 ELSE 1 END, a.Name;";

                        command.Parameters.AddWithValue("@TeamId", teamId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                team.Members.Add(new TeamMemberInfo
                                {
                                    AgentId = reader.GetInt32(0),
                                    AgentName = reader.GetString(1),
                                    Role = reader.GetString(2),
                                    AssignmentReason = reader.IsDBNull(3) ? null : reader.GetString(3),
                                    PerformanceInTeam = reader.IsDBNull(4) ? 0 : reader.GetFloat(4) // Handle null
                                });
                            }
                        }
                    }
                }

                return team;
            }
        }



        //        /// <summary>
        //        /// Gets details about a team.
        //        /// </summary>
        //        /// <param name="teamId">The ID of the team.</param>
        //        /// <returns>A TeamInfo object with members or null if not found.</returns>
        public async Task<TeamInfo> GetTeamAsync(string teamName)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Get team details
                TeamInfo team = null;
                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT TeamId, TeamName, ChiefAgentId, CreatedDate, PerformanceScore, Description
                            FROM TeamCompositions
                            WHERE TeamName = @TeamName;";

                    command.Parameters.AddWithValue("@TeamName", teamName);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            team = new TeamInfo
                            {
                                TeamId = reader.GetInt32(0),
                                TeamName = reader.GetString(1),
                                ChiefAgentId = reader.GetInt32(2),
                                CreatedDate = reader.GetDateTime(3),
                                PerformanceScore = reader.IsDBNull(4) ? 0 : reader.GetFloat(4), // Handle null
                                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                                Members = new List<TeamMemberInfo>()
                            };
                        }
                    }
                }

                if (team != null)
                {
                    // Get team members
                    using (var command = new SqliteCommand("", connection))
                    {
                        command.CommandText = @"
                                SELECT tm.AgentId, a.Name, tm.Role, tm.AssignmentReason, tm.PerformanceInTeam
                                FROM TeamMembers tm
                                JOIN Agents a ON tm.AgentId = a.AgentId
                                WHERE tm.TeamId = @TeamId
                                ORDER BY CASE WHEN tm.Role = 'Chief' THEN 0 ELSE 1 END, a.Name;";

                        command.Parameters.AddWithValue("@TeamId", team.TeamId);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                team.Members.Add(new TeamMemberInfo
                                {
                                    AgentId = reader.GetInt32(0),
                                    AgentName = reader.GetString(1),
                                    Role = reader.GetString(2),
                                    AssignmentReason = reader.IsDBNull(3) ? null : reader.GetString(3),
                                    PerformanceInTeam = reader.IsDBNull(4) ? 0 : reader.GetFloat(4) // Handle null
                                });
                            }
                        }
                    }
                }

                return team;
            }
        }



        /// <summary>
        /// Gets all members of a team, given the team name.
        /// </summary>
        /// <param name="teamName">The name of the team.</param>
        /// <returns>A list of TeamMemberInfo objects, or an empty list if team not found or has no members.</returns>
        public async Task<List<TeamMemberInfo>> GetTeamMembersByTeamNameAsync(string teamName)
        {
            if (string.IsNullOrWhiteSpace(teamName)) return new List<TeamMemberInfo>();
            // if (!_initialized) Initialize(); // Ensure DB is initialized

            var teamMembers = new List<TeamMemberInfo>();
            int teamId = 0;

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                // First, get the TeamId for the given teamName
                using (var cmdGetTeamId = new SqliteCommand("SELECT TeamId FROM TeamCompositions WHERE LOWER(TeamName) = LOWER(@TeamName) LIMIT 1", connection))
                {
                    cmdGetTeamId.Parameters.AddWithValue("@TeamName", teamName);
                    var result = await cmdGetTeamId.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        teamId = Convert.ToInt32(result);
                    }
                }

                if (teamId == 0)
                {
                    Debug.WriteLine($"[DB] Team '{teamName}' not found.");
                    return teamMembers; // Team not found
                }

                // Now, get the members for that TeamId
                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                SELECT tm.AgentId, a.Name AS AgentName, tm.Role, tm.AssignmentReason, tm.PerformanceInTeam
                FROM TeamMembers tm
                JOIN Agents a ON tm.AgentId = a.AgentId
                WHERE tm.TeamId = @TeamId
                ORDER BY a.Name;"; // Or by Role, etc.
                    command.Parameters.AddWithValue("@TeamId", teamId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            teamMembers.Add(new TeamMemberInfo
                            {
                                AgentId = reader.GetInt32(reader.GetOrdinal("AgentId")),
                                AgentName = reader.GetString(reader.GetOrdinal("AgentName")),
                                Role = reader.GetString(reader.GetOrdinal("Role")),
                                AssignmentReason = reader.IsDBNull(reader.GetOrdinal("AssignmentReason")) ? null : reader.GetString(reader.GetOrdinal("AssignmentReason")),
                                PerformanceInTeam = reader.IsDBNull(reader.GetOrdinal("PerformanceInTeam")) ? 0 : reader.GetFloat(reader.GetOrdinal("PerformanceInTeam"))
                            });
                        }
                    }
                }
            }
            Debug.WriteLine($"[DB] Found {teamMembers.Count} members for team '{teamName}' (ID: {teamId}).");
            return teamMembers;
        }





        //        /// <summary>
        //        /// Gets all teams in the database.
        //        /// </summary>
        //        /// <returns>A list of TeamInfo objects without members.</returns>
        public async Task<List<TeamInfo>> GetAllTeamsAsync()
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT t.TeamId, t.TeamName, t.ChiefAgentId, a.Name AS ChiefName,
                                   t.CreatedDate, t.PerformanceScore, t.Description,
                                   (SELECT COUNT(*) FROM TeamMembers WHERE TeamId = t.TeamId) AS MemberCount
                            FROM TeamCompositions t
                            JOIN Agents a ON t.ChiefAgentId = a.AgentId
                            ORDER BY t.TeamName;";

                    var teams = new List<TeamInfo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            teams.Add(new TeamInfo
                            {
                                TeamId = reader.GetInt32(0),
                                TeamName = reader.GetString(1),
                                ChiefAgentId = reader.GetInt32(2),
                                ChiefName = reader.GetString(3),
                                CreatedDate = reader.GetDateTime(4),
                                PerformanceScore = reader.IsDBNull(5) ? 0 : reader.GetFloat(5), // Handle null
                                Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                                MemberCount = reader.GetInt32(7)
                            });
                        }
                    }

                    return teams;
                }
            }
        }

        //        /// <summary>
        //        /// Updates the performance score of a team member.
        //        /// </summary>
        //        /// <param name="teamId">The ID of the team.</param>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <param name="performanceScore">The new performance score.</param>
        //        /// <returns>True if the update was successful.</returns>
        public async Task<bool> UpdateTeamMemberPerformanceAsync(int teamId, int agentId, float performanceScore)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            UPDATE TeamMembers
                            SET PerformanceInTeam = @PerformanceScore
                            WHERE TeamId = @TeamId AND AgentId = @AgentId;";

                    command.Parameters.AddWithValue("@TeamId", teamId);
                    command.Parameters.AddWithValue("@AgentId", agentId);
                    command.Parameters.AddWithValue("@PerformanceScore", performanceScore);

                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        // Update the overall team performance score
                        await UpdateTeamPerformanceAsync(teamId);
                    }

                    return rowsAffected > 0;
                }
            }
        }

        //        /// <summary>
        //        /// Updates the overall performance score of a team based on its members.
        //        /// </summary>
        //        /// <param name="teamId">The ID of the team.</param>
        //        /// <returns>True if the update was successful.</returns>
        private async Task<bool> UpdateTeamPerformanceAsync(int teamId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Calculate average performance of team members
                float teamPerformance;
                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT AVG(PerformanceInTeam)
                            FROM TeamMembers
                            WHERE TeamId = @TeamId;";

                    command.Parameters.AddWithValue("@TeamId", teamId);

                    object result = await command.ExecuteScalarAsync();
                    teamPerformance = result == DBNull.Value || result == null ? 0 : Convert.ToSingle(result); // Handle null
                }

                // Update the team's performance score
                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            UPDATE TeamCompositions
                            SET PerformanceScore = @PerformanceScore
                            WHERE TeamId = @TeamId;";

                    command.Parameters.AddWithValue("@TeamId", teamId);
                    command.Parameters.AddWithValue("@PerformanceScore", teamPerformance);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    return rowsAffected > 0;
                }
            }
        }

        //        #endregion

        //        #region Reporting and Analysis

        //        /// <summary>
        //        /// Generates a performance report for an agent.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <returns>A formatted performance report.</returns>
        public async Task<string> GenerateAgentPerformanceReportAsync(int agentId)
        {
            if (!_initialized) Initialize();

            var agent = await GetAgentAsync(agentId);
            if (agent == null)
            {
                return "Agent not found.";
            }

            var currentVersion = await GetCurrentAgentVersionAsync(agentId);
            var performance = await GetAgentPerformanceStatsAsync(agentId);
            var capabilities = await GetAgentCapabilitiesAsync(agentId);
            var recentInteractions = await GetRecentInteractionsAsync(agentId, 5);

            var report = new StringBuilder();
            report.AppendLine($"=== PERFORMANCE REPORT: {agent.Name} ===");
            report.AppendLine($"Generated: {DateTime.Now}");
            report.AppendLine();

            report.AppendLine("AGENT INFORMATION");
            report.AppendLine($"ID: {agent.AgentId}");
            report.AppendLine($"Name: {agent.Name}");
            report.AppendLine($"Purpose: {agent.Purpose}");
            report.AppendLine($"Created: {agent.CreatedDate}");
            report.AppendLine($"Last Modified: {agent.LastModifiedDate}");
            report.AppendLine($"Status: {(agent.IsActive ? "Active" : "Inactive")}");
            report.AppendLine($"Base Score: {agent.BaseScore:F2}");
            report.AppendLine($"Overall Success Rate: {(agent.TotalInteractions > 0 ? (float)agent.SuccessfulInteractions / agent.TotalInteractions * 100 : 0):F2}% ({agent.SuccessfulInteractions}/{agent.TotalInteractions})");
            report.AppendLine();

            if (currentVersion != null)
            {
                report.AppendLine("CURRENT VERSION");
                report.AppendLine($"Version Number: {currentVersion.VersionNumber}");
                report.AppendLine($"Created: {currentVersion.CreatedDate}");
                report.AppendLine($"Performance Score: {currentVersion.PerformanceScore:F2}");
                if (!string.IsNullOrEmpty(currentVersion.Comments))
                {
                    report.AppendLine($"Comments: {currentVersion.Comments}");
                }
                if (!string.IsNullOrEmpty(currentVersion.KnownIssues))
                {
                    report.AppendLine($"Known Issues: {currentVersion.KnownIssues}");
                }
                report.AppendLine();
            }

            if (performance.Count > 0)
            {
                report.AppendLine("PERFORMANCE BY TASK TYPE");
                foreach (var stat in performance)
                {
                    float successRate = stat.TotalAttempts > 0 ? (float)stat.CorrectResponses / stat.TotalAttempts * 100 : 0;
                    report.AppendLine($"{stat.TaskType}: {successRate:F2}% ({stat.CorrectResponses}/{stat.TotalAttempts}), Avg. Response Time: {stat.AverageResponseTime:F2}s");
                }
                report.AppendLine();
            }

            if (capabilities.Count > 0)
            {
                report.AppendLine("CAPABILITIES");
                foreach (var capability in capabilities.OrderByDescending(c => c.PerformanceRating))
                {
                    report.AppendLine($"{capability.CapabilityName}: {capability.PerformanceRating:F2}");
                    if (!string.IsNullOrEmpty(capability.CapabilityDescription))
                    {
                        report.AppendLine($"  {capability.CapabilityDescription}");
                    }
                }
                report.AppendLine();
            }

            if (recentInteractions.Count > 0)
            {
                report.AppendLine("RECENT INTERACTIONS");
                foreach (var interaction in recentInteractions)
                {
                    report.AppendLine($"Date: {interaction.InteractionDate}");
                    report.AppendLine($"Task Type: {interaction.TaskType}");
                    report.AppendLine($"Success: {(interaction.IsCorrect ? "Yes" : "No")}");
                    report.AppendLine($"Processing Time: {interaction.ProcessingTime:F2}s");
                    report.AppendLine($"Request: {TruncateString(interaction.RequestData, 100)}");
                    report.AppendLine($"Response: {TruncateString(interaction.ResponseData, 100)}");
                    if (!string.IsNullOrEmpty(interaction.EvaluationNotes))
                    {
                        report.AppendLine($"Notes: {interaction.EvaluationNotes}");
                    }
                    report.AppendLine();
                }
            }

            return report.ToString();
        }

        //        /// <summary>
        //        /// Finds agents that excel at a specific task type.
        //        /// </summary>
        //        /// <param name="taskType">The task type to search for.</param>
        //        /// <param name="minimumSuccessRate">The minimum success rate (0-1).</param>
        //        /// <param name="minimumAttempts">The minimum number of attempts required.</param>
        //        /// <returns>A list of agents with their success rates for the task.</returns>
        public async Task<List<(AgentInfo Agent, float SuccessRate)>> FindAgentsForTaskAsync(
            string taskType, float minimumSuccessRate = 0.7f, int minimumAttempts = 5)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT a.AgentId, a.Name, a.Purpose, a.CreatedDate, a.LastModifiedDate,
                                   a.IsActive, a.BaseScore, a.TotalInteractions, a.SuccessfulInteractions,
                                   p.CorrectResponses, p.TotalAttempts
                            FROM Agents a
                            JOIN AgentPerformance p ON a.AgentId = p.AgentId
                            JOIN AgentVersions v ON p.VersionId = v.VersionId
                            WHERE p.TaskType = @TaskType
                              AND v.IsActive = 1
                              AND a.IsActive = 1
                              AND p.TotalAttempts >= @MinimumAttempts
                              AND (CAST(p.CorrectResponses AS FLOAT) / p.TotalAttempts) >= @MinimumSuccessRate
                            ORDER BY (CAST(p.CorrectResponses AS FLOAT) / p.TotalAttempts) DESC;";

                    command.Parameters.AddWithValue("@TaskType", taskType);
                    command.Parameters.AddWithValue("@MinimumSuccessRate", minimumSuccessRate);
                    command.Parameters.AddWithValue("@MinimumAttempts", minimumAttempts);

                    var results = new List<(AgentInfo Agent, float SuccessRate)>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var agent = new AgentInfo
                            {
                                AgentId = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Purpose = reader.GetString(2),
                                CreatedDate = reader.GetDateTime(3),
                                LastModifiedDate = reader.GetDateTime(4),
                                IsActive = reader.GetBoolean(5),
                                BaseScore = reader.GetFloat(6),
                                TotalInteractions = reader.GetInt32(7),
                                SuccessfulInteractions = reader.GetInt32(8)
                            };

                            int correctResponses = reader.GetInt32(9);
                            int totalAttempts = reader.GetInt32(10);
                            float successRate = totalAttempts > 0 ? (float)correctResponses / totalAttempts : 0; // Avoid division by zero

                            results.Add((agent, successRate));
                        }
                    }

                    return results;
                }
            }
        }

        //        /// <summary>
        //        /// Analyzes prompt improvements and their impact on performance.
        //        /// </summary>
        //        /// <param name="agentId">The ID of the agent.</param>
        //        /// <returns>A list of prompt modifications with performance impact.</returns>
        public async Task<List<PromptModificationInfo>> AnalyzePromptImprovementsAsync(int agentId)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqliteCommand("", connection))
                {
                    command.CommandText = @"
                            SELECT pm.ModificationId, pm.VersionId, v.VersionNumber, pm.PreviousVersionId, pv.VersionNumber AS PrevVersionNumber,
                                   pm.ModificationReason, pm.ChangeSummary, pm.PerformanceBeforeChange,
                                   pm.PerformanceAfterChange, pm.ModificationDate
                            FROM PromptModifications pm
                            JOIN AgentVersions v ON pm.VersionId = v.VersionId
                            LEFT JOIN AgentVersions pv ON pm.PreviousVersionId = pv.VersionId
                            WHERE v.AgentId = @AgentId
                            ORDER BY pm.ModificationDate DESC;";

                    command.Parameters.AddWithValue("@AgentId", agentId);

                    var modifications = new List<PromptModificationInfo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            modifications.Add(new PromptModificationInfo
                            {
                                ModificationId = reader.GetInt32(0),
                                VersionId = reader.GetInt32(1),
                                VersionNumber = reader.GetInt32(2),
                                PreviousVersionId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                                PreviousVersionNumber = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                                ModificationReason = reader.GetString(5),
                                ChangeSummary = reader.GetString(6),
                                PerformanceBeforeChange = reader.IsDBNull(7) ? 0 : reader.GetFloat(7), // Handle null
                                PerformanceAfterChange = reader.IsDBNull(8) ? (float?)null : reader.GetFloat(8),
                                ModificationDate = reader.GetDateTime(9),
                                PerformanceImprovement = (reader.IsDBNull(8) || reader.IsDBNull(7)) ? (float?)null :
                                    reader.GetFloat(8) - reader.GetFloat(7)
                            });
                        }
                    }

                    return modifications;
                }
            }
        }

    }

    // --- Data Models ---
    // Ensure these are defined within this namespace or are accessible.

    #region Data Models

    // Agent & Version Info
    public class AgentInfo
    {
        public int AgentId { get; set; }
        public string Name { get; set; }
        public string Purpose { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public bool IsActive { get; set; }
        public float BaseScore { get; set; }
        public int TotalInteractions { get; set; }
        public int SuccessfulInteractions { get; set; }
        public string ChiefName { get; set; } // Only populated in specific queries (e.g., GetAllTeamsAsync)
    }

    public class AgentVersionInfo
    {
        public int VersionId { get; set; }
        public int AgentId { get; set; }
        public int VersionNumber { get; set; }
        public string Prompt { get; set; }
        public string Comments { get; set; }
        public string KnownIssues { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; }
        public float PerformanceScore { get; set; }
        public bool IsActive { get; set; }
    }

    public class PromptModificationInfo
    {
        public int ModificationId { get; set; }
        public int VersionId { get; set; }
        public int VersionNumber { get; set; } // Populated via JOIN
        public int? PreviousVersionId { get; set; }
        public int? PreviousVersionNumber { get; set; } // Populated via JOIN
        public string ModificationReason { get; set; }
        public string ChangeSummary { get; set; }
        public float PerformanceBeforeChange { get; set; }
        public float? PerformanceAfterChange { get; set; }
        public DateTime ModificationDate { get; set; }
        public float? PerformanceImprovement { get; set; } // Calculated property
    }

    // Performance & Interaction Info
    public class AgentPerformanceInfo
    {
        public int AgentId { get; set; }
        public int VersionId { get; set; }
        public int VersionNumber { get; set; } // Populated via JOIN
        public string TaskType { get; set; }
        public int CorrectResponses { get; set; }
        public int TotalAttempts { get; set; }
        public float AverageResponseTime { get; set; }
        public DateTime LastEvaluationDate { get; set; }
        public float SuccessRate => TotalAttempts > 0 ? (float)CorrectResponses / TotalAttempts : 0;
    }

    public class InteractionInfo
    {
        public int InteractionId { get; set; }
        public int AgentId { get; set; }
        public int VersionId { get; set; }
        public int VersionNumber { get; set; } // Populated via JOIN
        public string TaskType { get; set; }
        public string RequestData { get; set; }
        public string ResponseData { get; set; }
        public bool IsCorrect { get; set; }
        public float ProcessingTime { get; set; }
        public DateTime InteractionDate { get; set; }
        public string EvaluationNotes { get; set; }
    }

    // Capabilities & Teams Info
    public class AgentCapabilityInfo
    {
        public int CapabilityId { get; set; }
        public int AgentId { get; set; }
        public string CapabilityName { get; set; }
        public string CapabilityDescription { get; set; }
        public float PerformanceRating { get; set; }
    }

    public class TeamInfo
    {
        public int TeamId { get; set; }
        public string? TeamName { get; set; }
        public int ChiefAgentId { get; set; }
        public string? ChiefName { get; set; } // Populated via JOIN
        public DateTime CreatedDate { get; set; }
        public float PerformanceScore { get; set; }
        public string? Description { get; set; }
        public List<TeamMemberInfo> Members { get; set; } = new List<TeamMemberInfo>(); // Initialize
        public int MemberCount { get; set; } // Populated in specific queries
    }

    public class TeamMemberInfo
    {
        public int AgentId { get; set; }
        public string? AgentName { get; set; } // Populated via JOIN
        public string? Role { get; set; }
        public string? AssignmentReason { get; set; }
        public float PerformanceInTeam { get; set; }
    }


    #endregion Data Models
}