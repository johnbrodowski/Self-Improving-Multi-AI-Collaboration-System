using System;
using Microsoft.Data.Sqlite; // Or Microsoft.Data.Sqlite depending on which package you're using
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;


namespace AnthropicApp.AICollaborationSystem
{
    public class PerformanceDatabase
    {
        private string _connectionString;
        private bool _initialized = false;

        public PerformanceDatabase(string dbFilePath = "agent_performance.db")
        {
            // Ensure directory exists
            string directory = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={dbFilePath}";
        }

        public void Initialize()
        {
            if (_initialized) return;

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                // Create the performance table if it doesn't exist
                string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS AgentPerformance (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AgentName TEXT NOT NULL,
                    QuestionType TEXT NOT NULL,
                    TestDateTime TEXT NOT NULL,
                    IsCorrect INTEGER NOT NULL,
                    RequestData TEXT,
                    ResponseData TEXT
                );";

                using (var command = new SqliteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }

                // Create the summary table for quick access to aggregated data
                string createSummaryTableQuery = @"
                CREATE TABLE IF NOT EXISTS PerformanceSummary (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AgentName TEXT NOT NULL,
                    QuestionType TEXT NOT NULL,
                    CorrectAnswers INTEGER NOT NULL,
                    TotalAttempts INTEGER NOT NULL,
                    LastUpdated TEXT NOT NULL,
                    UNIQUE(AgentName, QuestionType)
                );";

                using (var command = new SqliteCommand(createSummaryTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            _initialized = true;
        }

        public void RecordPerformance(string agentName, string questionType, bool isCorrect,
                                      string requestData = null, string responseData = null)
        {
            if (!_initialized) Initialize();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Insert performance record
                        string insertQuery = @"
                        INSERT INTO AgentPerformance 
                            (AgentName, QuestionType, TestDateTime, IsCorrect, RequestData, ResponseData)
                        VALUES 
                            (@AgentName, @QuestionType, @TestDateTime, @IsCorrect, @RequestData, @ResponseData);";

                        // using (var command = new SqliteCommand(insertQuery, connection))
                        using (var command = new SqliteCommand(insertQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@AgentName", agentName);
                            command.Parameters.AddWithValue("@QuestionType", questionType);
                            command.Parameters.AddWithValue("@TestDateTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            command.Parameters.AddWithValue("@IsCorrect", isCorrect ? 1 : 0);
                            command.Parameters.AddWithValue("@RequestData", requestData ?? string.Empty);
                            command.Parameters.AddWithValue("@ResponseData", responseData ?? string.Empty);
                            command.ExecuteNonQuery();
                        }

                        // Update summary table
                        string upsertSummaryQuery = @"
                        INSERT INTO PerformanceSummary 
                            (AgentName, QuestionType, CorrectAnswers, TotalAttempts, LastUpdated)
                        VALUES 
                            (@AgentName, @QuestionType, @CorrectDelta, 1, @LastUpdated)
                        ON CONFLICT(AgentName, QuestionType) DO UPDATE SET
                            CorrectAnswers = CorrectAnswers + @CorrectDelta,
                            TotalAttempts = TotalAttempts + 1,
                            LastUpdated = @LastUpdated;";

                        using (var command = new SqliteCommand(upsertSummaryQuery, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@AgentName", agentName);
                            command.Parameters.AddWithValue("@QuestionType", questionType);
                            command.Parameters.AddWithValue("@CorrectDelta", isCorrect ? 1 : 0);
                            command.Parameters.AddWithValue("@LastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        System.Diagnostics.Debug.WriteLine($"Database error: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        public List<PerformanceSummary> GetAgentPerformanceSummary(string agentName = null)
        {
            if (!_initialized) Initialize();

            var results = new List<PerformanceSummary>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT AgentName, QuestionType, CorrectAnswers, TotalAttempts, LastUpdated FROM PerformanceSummary";

                if (!string.IsNullOrEmpty(agentName))
                {
                    query += " WHERE AgentName = @AgentName";
                }

                query += " ORDER BY AgentName, QuestionType";

                using (var command = new SqliteCommand(query, connection))
                {
                    if (!string.IsNullOrEmpty(agentName))
                    {
                        command.Parameters.AddWithValue("@AgentName", agentName);
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new PerformanceSummary
                            {
                                AgentName = reader.GetString(0),
                                QuestionType = reader.GetString(1),
                                CorrectAnswers = reader.GetInt32(2),
                                TotalAttempts = reader.GetInt32(3),
                                LastUpdated = DateTime.Parse(reader.GetString(4))
                            });
                        }
                    }
                }
            }

            return results;
        }

        public List<PerformanceDetail> GetRecentPerformanceDetails(int limit = 50)
        {
            if (!_initialized) Initialize();

            var results = new List<PerformanceDetail>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                string query = @"
                SELECT AgentName, QuestionType, TestDateTime, IsCorrect, RequestData, ResponseData 
                FROM AgentPerformance
                ORDER BY TestDateTime DESC
                LIMIT @Limit";

                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Limit", limit);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new PerformanceDetail
                            {
                                AgentName = reader.GetString(0),
                                QuestionType = reader.GetString(1),
                                TestDateTime = DateTime.Parse(reader.GetString(2)),
                                IsCorrect = reader.GetInt32(3) == 1,
                                RequestData = reader.GetString(4),
                                ResponseData = reader.GetString(5)
                            });
                        }
                    }
                }
            }

            return results;
        }

 

        public void ResetDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string deletePerfQuery = "DELETE FROM AgentPerformance;";
                        using (var command = new SqliteCommand(deletePerfQuery, connection))
                        {
                            command.ExecuteNonQuery();
                        }

                        string deleteSummaryQuery = "DELETE FROM PerformanceSummary;";
                        using (var command = new SqliteCommand(deleteSummaryQuery, connection))
                        {
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Debug.WriteLine($"Database reset error: {ex.Message}");
                        throw;
                    }
                }
            }
        }






    }

    public class PerformanceSummary
    {
        public string AgentName { get; set; }
        public string QuestionType { get; set; }
        public int CorrectAnswers { get; set; }
        public int TotalAttempts { get; set; }
        public DateTime LastUpdated { get; set; }

        public double SuccessRate => TotalAttempts > 0 ? (double)CorrectAnswers / TotalAttempts : 0;
    }

    public class PerformanceDetail
    {
        public string AgentName { get; set; }
        public string QuestionType { get; set; }
        public DateTime TestDateTime { get; set; }
        public bool IsCorrect { get; set; }
        public string RequestData { get; set; }
        public string ResponseData { get; set; }
    }
}