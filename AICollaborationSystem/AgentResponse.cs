using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AnthropicApp.AICollaborationSystem
{
    /// <summary>
    /// Represents a response from an AI agent that can be used for voting and collection
    /// </summary>
    public class AgentResponse
    {
        public string AgentName { get; }
        public string ResponseData { get; }
        public DateTime Timestamp { get; }
        public int Votes { get; set; }

        public AgentResponse(string agentName, string response)
        {
            AgentName = agentName;
            ResponseData = response;
            Timestamp = DateTime.Now;
            Votes = 0;
        }
    }

    /// <summary>
    /// Thread-safe collection of agent responses
    /// </summary>
    public class ResponseCollection
    {
        private readonly ConcurrentDictionary<string, List<AgentResponse>> _responses =
            new ConcurrentDictionary<string, List<AgentResponse>>();

        /// <summary>
        /// Adds a response to the collection, grouped by request
        /// </summary>
        public void AddResponse(string requestData, string agentName, string responseData)
        {
            var response = new AgentResponse(agentName, responseData);

            _responses.AddOrUpdate(
                requestData,
                // If key doesn't exist, create new list with this response
                _ => new List<AgentResponse> { response },
                // If key exists, add this response to the existing list
                (_, existingList) =>
                {
                    lock (existingList)
                    {
                        existingList.Add(response);
                        return existingList;
                    }
                }
            );

            Debug.WriteLine($"Added response from {agentName} to collection for request: {requestData}");
        }

        /// <summary>
        /// Gets all responses for a specific request
        /// </summary>
        public List<AgentResponse> GetResponsesForRequest(string requestData)
        {
            if (_responses.TryGetValue(requestData, out var responseList))
            {
                lock (responseList)
                {
                    return responseList.ToList(); // Return a copy to avoid thread safety issues
                }
            }

            return new List<AgentResponse>();
        }

        /// <summary>
        /// Gets all requests that have responses
        /// </summary>
        public List<string> GetAllRequests()
        {
            return _responses.Keys.ToList();
        }

        /// <summary>
        /// Clears responses for a specific request
        /// </summary>
        public bool ClearResponsesForRequest(string requestData)
        {
            return _responses.TryRemove(requestData, out _);
        }

        /// <summary>
        /// Clears all responses
        /// </summary>
        public void ClearAllResponses()
        {
            _responses.Clear();
        }

        /// <summary>
        /// Add a vote to a specific response
        /// </summary>
        public bool AddVote(string requestData, string agentName)
        {
            if (_responses.TryGetValue(requestData, out var responseList))
            {
                lock (responseList)
                {
                    var response = responseList.FirstOrDefault(r => r.AgentName == agentName);
                    if (response != null)
                    {
                        response.Votes++;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the winning response based on votes for a specific request
        /// </summary>
        public AgentResponse GetWinningResponse(string requestData)
        {
            if (_responses.TryGetValue(requestData, out var responseList))
            {
                lock (responseList)
                {
                    return responseList.OrderByDescending(r => r.Votes).FirstOrDefault();
                }
            }

            return null;
        }
    }
}
