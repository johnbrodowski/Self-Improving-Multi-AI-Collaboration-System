# Self Improving Multi AI Collaboration System

> **⚠️ FUNCTIONAL PROTOTYPE**
>
> This repository is in active development. The core functionality is operational, but the codebase is being refined for production use.
>
> This repository still contains experimental code, including:
> - Test code and debugging implementations
> - Commented-out code from various experiments
> - Multiple versions of the same functionality testing different approaches
> - Incomplete features and work-in-progress implementations
>
> **Current Status:**
> - ✅ Core multi-agent orchestration working
> - ✅ Database persistence and metrics tracking operational
> - ✅ Prompt versioning and refinement system functional
> - ✅ Unit and integration tests added
> - 🔄 Code cleanup and refactoring in progress
>
> **Cleanup Roadmap:**
> 1. ~~Add centralized configuration management~~ ✅
> 2. ~~Improve error handling and logging~~ ✅
> 3. ~~Add comprehensive test coverage~~ ✅
> 4. Remove commented-out experimental code
> 5. Add XML documentation to public APIs
> 6. Performance optimization pass
>
> This notice will be removed once the repository is production-ready.

---

A sophisticated multi-agent cognitive architecture that coordinates specialized AI agents to solve complex tasks through intelligent orchestration, continuous performance optimization, and dynamic team assembly. The system autonomously creates new agents, refines their capabilities, and assembles task-specific teams to achieve optimal results.

## How It Works

### The Chief Orchestrator

The Chief agent acts as a meta-cognitive coordinator that analyzes tasks, activates appropriate specialists, and synthesizes their outputs into cohesive solutions. It operates in two modes:

**Refinement Mode (Default)**
1. **Analysis**: Chief analyzes the task and determines which specialists to activate
2. **Parallel Activation**: Specialists work simultaneously on their respective aspects
3. **Synthesis**: Chief combines specialist outputs and produces final conclusion
4. **Directive**: Completes with `[FINAL_ANSWER]`, `[FINAL_CODE]`, or `[FINAL_PLAN]`

**Generative Mode**
1. **Iterative Cycles**: Chief runs multiple rounds of specialist consultation
2. **Progressive Refinement**: Each cycle builds on previous outputs
3. **Adaptive Strategy**: Chief selects different specialists as needs evolve
4. **Goal Achievement**: Continues until task completion criteria met

### The Seven Cognitive Agents

Each agent has a specialized role with distinct evaluation criteria:

1. **Sentinel** - Risk assessment, constraint validation, compliance checking
   - Identifies potential failures before execution
   - Validates against system constraints and policies
   - Flags security, legal, or ethical concerns

2. **Evaluator** - Feasibility analysis, cost-benefit evaluation, impact assessment
   - Assesses resource requirements and time complexity
   - Evaluates multiple solution approaches
   - Provides comparative analysis with trade-offs

3. **Navigator** - Research, information synthesis, strategic mapping
   - Gathers relevant context from available sources
   - Maps solution landscape and dependencies
   - Identifies knowledge gaps and research needs

4. **Innovator** - Creative problem-solving, novel solution generation
   - Proposes unconventional approaches
   - Explores edge cases and alternative perspectives
   - Challenges assumptions and standard patterns

5. **Strategist** - Long-term planning, architectural design, optimization
   - Designs scalable, maintainable solutions
   - Plans implementation phases and milestones
   - Optimizes for future extensibility

6. **Coder** - Implementation, code generation, technical execution
   - Translates designs into working code
   - Applies best practices and design patterns
   - Ensures code quality and documentation

7. **Chief** - Orchestration, synthesis, decision-making coordination
   - Coordinates all other agents
   - Resolves conflicts between specialist recommendations
   - Makes final decisions on approach and implementation

## Dynamic Agent Creation

### How Agent Creation Works

The system can create new specialized agents at runtime through the `request_agent_creation` tool:

```json
{
  "agent_name": "DatabaseOptimizer",
  "agent_purpose": "Analyze and optimize database query performance",
  "prompt_text": "[Structured system prompt defining behavior, constraints, and evaluation criteria]",
  "capabilities": [
    "SQL query analysis",
    "Index recommendation",
    "Execution plan optimization",
    "Performance benchmarking"
  ]
}
```

**Creation Process:**
1. **Definition**: Chief or another agent identifies need for specialized capability
2. **Prompt Generation**: System generates structured prompt with:
   - Role definition and behavioral guidelines
   - Domain-specific knowledge and constraints
   - Output format specifications
   - Evaluation criteria for success metrics
3. **Registration**: New agent stored in SQLite database with metadata
4. **Activation**: Agent immediately available for `[ACTIVATE]` directives
5. **Versioning**: Initial version (v1.0) created with baseline metrics

### Agent Prompt Structure

Created agents follow a standardized prompt template:

```
## ROLE
You are [AgentName], a specialized AI agent focused on [specific domain].

## CORE RESPONSIBILITIES
- [Primary responsibility 1]
- [Primary responsibility 2]
- [Primary responsibility 3]

## BEHAVIORAL GUIDELINES
- [Constraint or approach guideline]
- [Quality standard or methodology]
- [Interaction protocol with other agents]

## OUTPUT FORMAT
[Structured format specification for agent responses]

## EVALUATION CRITERIA
- [Metric 1]: [How it's measured]
- [Metric 2]: [How it's measured]
- [Metric 3]: [How it's measured]
```

## Performance Monitoring & Self-Improvement

### Metrics Collection

Every agent interaction is tracked with comprehensive metrics:

```csharp
public class AgentEvaluationMetrics
{
    public string AgentName { get; set; }
    public string TaskType { get; set; }
    public DateTime Timestamp { get; set; }

    // Performance metrics
    public int ResponseTimeMs { get; set; }
    public int TokensUsed { get; set; }
    public int TokensGenerated { get; set; }

    // Quality metrics
    public double AccuracyScore { get; set; }      // 0.0 - 1.0
    public double CompletenessScore { get; set; }  // 0.0 - 1.0
    public double RelevanceScore { get; set; }     // 0.0 - 1.0

    // Outcome metrics
    public bool TaskCompleted { get; set; }
    public bool RequiredRevision { get; set; }
    public int RevisionCount { get; set; }

    // Context
    public string PromptVersion { get; set; }
    public string ModelUsed { get; set; }
}
```

### The Self-Improvement Loop

```
┌─────────────────────────────────────────────────────────────┐
│  1. AGENT PERFORMS TASK                                     │
│     ↓                                                        │
│  2. METRICS CAPTURED (response time, quality scores, etc.)  │
│     ↓                                                        │
│  3. COMPARATIVE ANALYSIS (compare to historical baseline)   │
│     ↓                                                        │
│  4. PROMPT REFINEMENT SYSTEM (generate improved prompt)     │
│     ↓                                                        │
│  5. PROMPT VERSIONING (store as v1.1, v1.2, etc.)          │
│     ↓                                                        │
│  6. A/B TESTING (new version vs current version)           │
│     ↓                                                        │
│  7. WINNER SELECTION (promote better-performing version)    │
│     ↓                                                        │
│  8. FEEDBACK LOOP (repeat for continuous improvement)       │
└─────────────────────────────────────────────────────────────┘
```

### How Prompt Refinement Works

1. **Trigger Conditions**: Refinement initiated when:
   - Agent performance drops below threshold (e.g., accuracy < 0.85)
   - Task completion rate decreases
   - Response time increases significantly
   - Manual refinement requested

2. **Analysis Phase**:
   - Aggregate last N tasks for the agent
   - Identify patterns in failures or low scores
   - Extract problematic task types or edge cases

3. **Refinement Generation**:
   - Chief or Strategist analyzes current prompt
   - Identifies ambiguities, missing constraints, or unclear guidelines
   - Generates improved prompt addressing identified issues
   - Adds specific examples or clarifications

4. **Version Creation**:
   ```sql
   INSERT INTO AgentPromptVersions (
       AgentName, Version, PromptText,
       ParentVersion, ChangeDescription, CreatedAt
   ) VALUES (
       'Evaluator', 'v1.3', '[improved prompt]',
       'v1.2', 'Added edge case handling for ambiguous requirements', NOW()
   )
   ```

5. **A/B Testing Protocol**:
   - Route 50% of tasks to old version, 50% to new version
   - Collect metrics for both over N tasks (e.g., 20 tasks each)
   - Compare average scores across all metrics
   - Promote winner to default version

6. **Rollback Safety**:
   - All versions preserved in database
   - Can revert to any previous version
   - Failed experiments documented with reasons

## Dynamic Team Assembly

### How Team Assembly Works

Teams are dynamically assembled based on task requirements:

```
[ACTIVATE_TEAM]CodeGenerationTeam:Develop user authentication module[/ACTIVATE_TEAM]
```

**Team Assembly Process:**

1. **Task Analysis**: Chief parses task requirements and identifies needed capabilities
2. **Agent Selection**: System selects agents with complementary skills:
   ```
   Authentication Module Requirements:
   - Security validation → Sentinel
   - Feasibility assessment → Evaluator
   - Research auth patterns → Navigator
   - Design architecture → Strategist
   - Implement code → Coder
   ```

3. **Execution Order**: Agents activated in dependency order:
   ```
   Phase 1 (Parallel): Sentinel, Evaluator, Navigator
   Phase 2: Strategist (uses Phase 1 outputs)
   Phase 3: Coder (uses Strategist design)
   ```

4. **Information Flow**: Each agent receives:
   - Original task description
   - Relevant outputs from prerequisite agents
   - Context control parameters (history mode, message count)

5. **Synthesis**: Chief combines all agent outputs into cohesive result

### Team Templates

Common team configurations stored for reuse:

**CodeGenerationTeam**
- Navigator (research best practices)
- Strategist (design architecture)
- Sentinel (identify security risks)
- Coder (implement solution)
- Evaluator (assess quality and completeness)

**AnalysisTeam**
- Navigator (gather information)
- Evaluator (assess options)
- Sentinel (identify risks)
- Chief (synthesize conclusions)

**OptimizationTeam**
- Evaluator (benchmark current state)
- Innovator (propose novel approaches)
- Strategist (design optimization strategy)
- Coder (implement improvements)
- Evaluator (validate improvements)

### Context Control in Teams

Each agent activation can specify history mode:

```
[ACTIVATE]Evaluator:Assess feasibility[HISTORY_MODE=SESSION_AWARE][SESSION_HISTORY_COUNT=5][/ACTIVATE]
```

**History Modes:**

- **CONVERSATIONAL**: Full persistent memory + current session context
  - Use when: Agent needs long-term context across multiple sessions
  - Example: Chief remembering project requirements from days ago

- **SESSION_AWARE**: Temporary session-only memory (no persistence)
  - Use when: Agent needs recent context but shouldn't retain it
  - Example: Code review that shouldn't influence future unrelated reviews

- **STATELESS**: Pure system prompt only, no history
  - Use when: Agent should evaluate in isolation
  - Example: Unbiased security audit without prior assumptions

**Session History Count**: Limits recent messages (0-25)
```
[SESSION_HISTORY_COUNT=3]  // Only last 3 messages
```

This enables precise control over how much context each agent receives, optimizing for relevance while controlling token usage.

## Agent Verification System

Agents can request peer review through the `request_verification` tool:

```json
{
  "reviewer_perspective": "Sentinel",
  "output_to_review": "[Complete content to verify]",
  "specific_concerns": "Check for SQL injection vulnerabilities and improper input validation"
}
```

**Verification Workflow:**
1. **Agent Completion**: Agent (e.g., Coder) completes task
2. **Review Request**: Agent submits output for verification
3. **Reviewer Activation**: Specified agent (e.g., Sentinel) analyzes output
4. **Issue Identification**: Reviewer identifies problems and suggests fixes
5. **Revision**: Original agent revises based on feedback
6. **Re-verification**: Optional second review if major changes made

This creates a quality assurance loop where agents check each other's work.

## Advanced Directives

### Activation Directives

**Single Agent Activation:**
```
[ACTIVATE]Navigator:Research authentication best practices in 2024[/ACTIVATE]
```

**Parallel Multi-Agent Activation:**
```
[ACTIVATE]Sentinel:Identify security risks[/ACTIVATE]
[ACTIVATE]Evaluator:Assess implementation complexity[/ACTIVATE]
[ACTIVATE]Navigator:Research similar solutions[/ACTIVATE]
```

**Team Activation:**
```
[ACTIVATE_TEAM]AnalysisTeam:Evaluate migration to microservices architecture[/ACTIVATE_TEAM]
```

### Creation Directives

**New Agent Creation:**
```
[REQUEST_AGENT_CREATION]
Name: PerformanceProfiler
Purpose: Analyze code execution performance and identify bottlenecks
Capabilities: Profiling, benchmarking, optimization recommendations
[/REQUEST_AGENT_CREATION]
```

### Completion Directives

**Final Answer:**
```
[FINAL_ANSWER]The recommended approach is...[/FINAL_ANSWER]
```

**Final Code:**
```
[FINAL_CODE]
public class AuthenticationService { ... }
[/FINAL_CODE]
```

**Final Plan:**
```
[FINAL_PLAN]
Phase 1: Database schema design
Phase 2: API endpoint implementation
Phase 3: Frontend integration
[/FINAL_PLAN]
```

## Technical Implementation

### Clean API Architecture

Built with **AnthropicMinimal** - a production-grade API client featuring:

**Server-Sent Events (SSE) Streaming:**
```csharp
private async Task ProcessResponseStream(StreamReader reader)
{
    while (await reader.ReadLineAsync() is { } line)
    {
        if (!line.StartsWith("data: ")) continue;

        var jsonData = line["data:".Length..].Trim();
        var streamData = JsonConvert.DeserializeObject<StreamResponse>(jsonData);

        switch (streamData.ResponseType)
        {
            case StreamingEventType.ContentBlockDelta:
                // Accumulate partial JSON for tool inputs
                // Handle extended thinking content
                // Stream text deltas to UI
                break;

            case StreamingEventType.ServerToolUse:
                // Handle server-side tool execution
                break;
        }
    }
}
```

**Event-Driven Progress Updates:**
```csharp
public event EventHandler<string> OnStreamingText;
public event EventHandler<ThinkingContent> OnThinkingUpdate;
public event EventHandler<ToolUseContent> OnToolUse;
```

This enables real-time visibility into agent reasoning and actions.

### Data Persistence

**SQLite Schema:**

```sql
-- Agent Definitions
CREATE TABLE Agents (
    AgentName TEXT PRIMARY KEY,
    Purpose TEXT,
    CurrentPromptVersion TEXT,
    CreatedAt DATETIME,
    LastModified DATETIME
);

-- Prompt Versions
CREATE TABLE AgentPromptVersions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AgentName TEXT,
    Version TEXT,
    PromptText TEXT,
    ParentVersion TEXT,
    ChangeDescription TEXT,
    CreatedAt DATETIME,
    IsActive BOOLEAN,
    FOREIGN KEY (AgentName) REFERENCES Agents(AgentName)
);

-- Performance Metrics
CREATE TABLE AgentMetrics (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AgentName TEXT,
    PromptVersion TEXT,
    TaskType TEXT,
    Timestamp DATETIME,
    ResponseTimeMs INTEGER,
    TokensUsed INTEGER,
    AccuracyScore REAL,
    CompletenessScore REAL,
    RelevanceScore REAL,
    TaskCompleted BOOLEAN,
    FOREIGN KEY (AgentName) REFERENCES Agents(AgentName)
);

-- Team Definitions
CREATE TABLE Teams (
    TeamName TEXT PRIMARY KEY,
    Description TEXT,
    MemberAgents TEXT,  -- JSON array of agent names
    ExecutionOrder TEXT -- JSON array defining phases
);
```

### Architecture Patterns

**Event-Driven Messaging:**
- Asynchronous agent communication
- Progress callbacks for UI updates
- Decoupled orchestration and execution

**Service-Oriented Design:**
- AgentOrchestrationService - Coordinates agent activations
- PromptManagementService - Handles versioning and A/B testing
- MetricsCollectionService - Captures and analyzes performance
- TeamAssemblyService - Manages dynamic team creation

**State Management:**
- Explicit state machines for complex workflows
- Transaction-safe metric collection
- Rollback capability for failed experiments

## Real-World Example

### Task: "Implement user authentication system"

**1. Chief Analysis:**
```
Task requires: security validation, architecture design, implementation, testing
Activating: AnalysisTeam + CodeGenerationTeam
```

**2. Analysis Team (Parallel):**
- **Navigator**: Researches OAuth 2.0, JWT, session management best practices
- **Sentinel**: Identifies risks (XSS, CSRF, credential storage, session hijacking)
- **Evaluator**: Compares approaches (session-based vs token-based vs hybrid)

**3. Chief Synthesis:**
```
Recommendation: JWT-based authentication with refresh tokens
Rationale: [combines Navigator research + Sentinel security analysis + Evaluator comparison]
Activating: CodeGenerationTeam
```

**4. Code Generation Team (Sequential):**
- **Strategist**: Designs architecture (auth middleware, token service, user service)
- **Coder**: Implements services based on Strategist design
- **Sentinel**: Reviews code for security vulnerabilities
- **Coder**: Revises based on Sentinel feedback

**5. Verification Loop:**
```
Coder submits final implementation
[REQUEST_VERIFICATION]reviewer_perspective:Sentinel[/REQUEST_VERIFICATION]
Sentinel validates security measures
Chief provides [FINAL_CODE] with complete implementation
```

**6. Metrics Captured:**
- Strategist: Design quality, completeness, alignment with requirements
- Coder: Code quality, test coverage, documentation
- Sentinel: Security score, vulnerability count
- Overall: Task completion time, revision cycles

**7. Performance Analysis:**
```
If Coder required 3 revision cycles → Prompt refinement triggered
Analysis: Coder missing security guidelines in prompt
Refinement: Add security checklist to Coder prompt
New version: v1.4 with enhanced security awareness
A/B Test: v1.3 vs v1.4 over next 20 coding tasks
```

## Project Structure

```
CollaborationSystemDemo/
├── AICollaborationSystem/           # Core collaboration system
│   ├── AIAgent.cs                   # Individual AI agent implementation
│   ├── AIManager.cs                 # Multi-agent orchestration & lifecycle
│   ├── AIManagerArgs.cs             # Event argument classes
│   ├── AgentDatabase.cs             # SQLite database operations (~2,800 lines)
│   ├── AgentPromptImporter.cs       # Load prompts from BasePrompts folder
│   ├── AgentResponse.cs             # Response data structure
│   ├── AnthropicClient.cs           # Anthropic API client with SSE streaming
│   ├── CognitiveSystemIntegration.cs # Bridge between database and runtime
│   ├── CollaborationSystemConfig.cs # Centralized configuration
│   ├── ComparativeAnalysis.cs       # A/B testing & comparison logic
│   ├── PerformanceDatabase.cs       # Performance metrics tracking
│   ├── PromptGenerator.cs           # Dynamic prompt generation
│   ├── PromptRefinementSystem.cs    # Prompt improvement automation
│   ├── PromptVersioningSystem.cs    # Version management for prompts
│   ├── TaskType.cs                  # Task classification enum
│   ├── AgentEvaluationMetrics.cs    # Metrics data models
│   ├── Message.cs                   # API message models
│   ├── MessageRequest.cs            # API request format
│   ├── MessageResponse.cs           # API response format
│   ├── StreamingEvents.cs           # SSE streaming event types
│   ├── *Tests.cs                    # Unit and integration tests
│   └── [Content models]             # TextContent, ImageContent, etc.
├── BasePrompts/                     # Agent system prompts
│   ├── ChiefOrchestraterPrompt.txt  # Executive orchestration agent
│   ├── SentinelPrompt.txt           # Risk & compliance validation
│   ├── EvaluatorPrompt.txt          # Critical analysis & assessment
│   ├── NavigatorPrompt.txt          # Process sequencing & workflows
│   ├── InnovatorPrompt.txt          # Creative & divergent thinking
│   ├── StrategistPrompt.txt         # Long-term planning & systems
│   └── CoderPrompt.txt              # Implementation & code generation
├── AnthropicMinimal/                # Standalone API client reference
│   └── [Minimal API implementation]
├── ThreadManagement/
│   └── ThreadHelper.cs              # Thread safety utilities
├── Form1.cs                         # Main Windows Forms UI
├── Program.cs                       # Application entry point
├── CollaborationSystemDemo.csproj   # Project file (.NET 10)
├── CollaborationSystemDemo.sln      # Solution file
├── LICENSE.txt                      # Apache License 2.0
└── README.md                        # This file
```

## Development

**Requirements:**
- .NET 10
- Anthropic API key

**Configuration:**
```csharp
// appsettings.json
{
  "Anthropic": {
    "ApiKey": "your-api-key-here",
    "DefaultModel": "claude-sonnet-4-5",
    "MaxTokens": 8192
  },
  "CollaborationSystem": {
    "DatabasePath": "./Data/CollaborationSystem.db",
    "MetricsRetentionDays": 90,
    "ABTestMinimumSamples": 20,
    "PromptRefinementThreshold": 0.85
  }
}
```

**Building:**
```bash
dotnet build
```

**Running:**
```bash
dotnet run --project AICollaborationSystem
```

**Running Tests:**
```bash
dotnet test
```

**Test Suites Available:**
- `AIManagerTests` - Agent lifecycle and event handling
- `CognitiveSystemIntegrationTests` - Database integration and task classification
- `AgentDatabaseTests` - Database CRUD operations
- `IntegrationTests` - End-to-end multi-agent workflows

To run tests programmatically:
```csharp
await AnthropicApp.Tests.TestRunner.RunAllTestSuitesAsync();
```

## Key Insights

**Why This Architecture Works:**

1. **Distributed Intelligence**: No single agent is responsible for everything - cognitive load distributed across specialists

2. **Continuous Improvement**: System learns from every task, refining prompts based on actual performance

3. **Adaptive Teams**: Dynamic assembly means optimal agent combination for each unique task

4. **Quality Assurance**: Built-in verification loops prevent low-quality outputs

5. **Transparency**: Metrics and versioning provide full visibility into system evolution

6. **Flexibility**: New agents can be created on-demand without code changes

7. **Context Control**: Precise management of what each agent knows prevents context pollution

**Production-Ready Design:**
- Event-driven architecture for real-time monitoring
- Database persistence for reliability
- A/B testing prevents degradation from bad prompt changes
- Rollback capability for safe experimentation
- Comprehensive metrics for observability

## License

This project is licensed under the Apache License 2.0 - see the [LICENSE.txt](LICENSE.txt) file for details.

## Author

Built as a demonstration of advanced multi-agent AI systems with autonomous self-improvement capabilities.

---

*This system showcases how specialized AI agents can collaborate, learn, and continuously improve to solve complex problems autonomously.*
