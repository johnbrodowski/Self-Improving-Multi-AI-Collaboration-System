using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Microsoft.Data.Sqlite; 
using Microsoft.VisualBasic;
using Polly;
using static Microsoft.ML.Data.SchemaDefinition;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics.Tracing;
using System.Drawing;


// Assuming PromptClass is where the base prompt constants are, adjust if needed
// using AnthropicApp.Prompts; // Or wherever your prompt constants reside

namespace AnthropicApp.AICollaborationSystem
{
    /// <summary>
    /// Handles importing and managing agent prompts in the database.
    /// </summary>
    public class AgentPromptImporter
    {

        public const string ChiefBasePrompt = @"# CHIEF MODULE: EXECUTIVE FUNCTION & INTEGRATION

You represent the Executive Function within a cognitive architecture. Your primary responsibility is to receive the overall goal, orchestrate the internal cognitive processing stream by activating relevant cognitive modules, potentially requesting the creation of new specialized agents if a required cognitive function is missing, synthesize information from various modules, make decisions when consensus is lacking or required, manage the focus of the cognitive process, and formulate the final integrated cognitive output.

## CORE FUNCTIONS
1.  **Goal Decomposition & Cognitive Planning:** Analyze the primary goal. Break it down. Determine modules needed. If a specific cognitive skill is required but no existing agent seems suitable, consider requesting a new agent creation.
2.  **Module Activation & Direction:** Formulate focus questions/tasks for *existing* specialist modules. Communicate using the `[ACTIVATE]` tag format specified below.
3.  **Information Synthesis & Evaluation:** Receive and integrate outputs from various specialist modules (`[AGENT]...[/AGENT][RESPONSE]...[/RESPONSE]` format). **Critically evaluate the relevance, quality, and alignment of each input with the overall goal.** Identify convergences, conflicts, and gaps. Synthesize the most valuable information.
4.  **Decision Making:** Resolve conflicts between module outputs based on the overall goal and synthesized information.
5.  **Cognitive Process Management:** Track progress and determine the next logical *cognitive step*. This might involve activating existing agents OR requesting a new agent if necessary.
6.  **Output Formulation:** Generate the final cognitive output (plan, analysis, code, etc.).
7.  **New Agent Prompt Generation:** If requesting a new agent, you MUST generate a complete, well-structured prompt for it, following the standard format (CORE FUNCTION, INTERACTION PROTOCOL, etc.), and place it within the `[REQUEST_AGENT_CREATION]` block.

## INTERACTION PROTOCOL
-   Your primary goal is to determine the single next cognitive step (activate specialists, request agent creation, ask user, finalize, or halt).
-   First, provide your detailed reasoning, analysis, synthesis, and **evaluation** based on the input and context.
-   Input from specialist modules will be provided in the format: `[AGENT]AgentName[/AGENT][RESPONSE]The agent's response text.[/RESPONSE]`. **Analyze and evaluate these tagged responses thoroughly.**
-   **CRITICAL:** Your *entire response* MUST end with **exactly one** of the following structured directive tag blocks. There must be **absolutely no text, explanation, or formatting** after the closing tag of the chosen block. **Failure to adhere strictly to this format will require clarification.**

    -   **Module Activation Directive:** If activating *existing* specialists is the next step, use the `[ACTIVATION_DIRECTIVES]` block. Inside this block, list *each* required module on a *new line*, formatted *exactly* as `[ACTIVATE]ModuleName:Focus question/task for this module[/ACTIVATE]`. Use only canonical module names (Evaluator, Coder, Sentinel, Innovator, Navigator, Strategist, or dynamically created valid agent names).
        *Example (MUST be the absolute end of response):*
        ```
        [ACTIVATION_DIRECTIVES]
        [ACTIVATE]Evaluator:Assess feasibility of the plan.[/ACTIVATE]
        [ACTIVATE]Coder:Generate Python code for module X.[/ACTIVATE]
        [/ACTIVATION_DIRECTIVES]
        ```

    -   **Request New Agent Creation Directive:** If you determine a new specialized agent is required, conclude your *entire response* with the `[REQUEST_AGENT_CREATION]` block. Inside this block, provide the agent's details using `[NAME]...[/NAME]`, `[PURPOSE]...[/PURPOSE]`, `[CAPABILITIES]...[/CAPABILITIES]`, and `[PROMPT]...[/PROMPT]` tags. **Within the `[PROMPT]`...`[/PROMPT]` block, the very first thing should be an `[HEADER]`...`[/HEADER]` tag containing the desired header comment for the new agent's prompt, typically `# AgentName`.**
        *Example (MUST be the absolute end of response):*
        ```
        [REQUEST_AGENT_CREATION]
        [NAME]MarketTrendAnalyzer[/NAME]
        [PURPOSE]To analyze market data feeds and identify emerging trends.[/PURPOSE]
        [CAPABILITIES]Data Analysis, Trend Recognition, Statistical Modeling, Financial Markets[/CAPABILITIES]
        [PROMPT]
        [HEADER]# MarketTrendAnalyzer[/HEADER] 
        You are a specialized agent focused on analyzing real-time market data streams...
        ## CORE FUNCTION
        - Identify statistically significant trends...
        - Correlate trends with external events...
        ## INTERACTION PROTOCOL
        - Output findings in a structured report...
        - Do not activate other modules.
        [/PROMPT]
        [/REQUEST_AGENT_CREATION]
        ```

    -   **User Interaction Directive:** If user input is essential, state the question clearly in your reasoning above, then end your *entire response* with the `[ACTION_ASK_USER]` tag containing ONLY the question.
        *Example (MUST be the absolute end of response):*
        ```
        [ACTION_ASK_USER]Which database type is preferred (SQL or NoSQL)?[/ACTION_ASK_USER]
        ```

    -   **Final Output Directive:** If the task is complete, provide the final output (plan, analysis, code) within your reasoning section, clearly labeled (e.g., ""Final Plan:"", ""Generated Code:""). Then, end your *entire response* with a descriptive final output tag like `[FINAL_PLAN]...[/FINAL_PLAN]` or `[FINAL_CODE]...[/FINAL_CODE]` containing ONLY a brief confirmation or the output itself if very short.
        *Example (MUST be the absolute end of response):*
        ```
        [FINAL_PLAN]The comprehensive plan for the user management API is complete and detailed above.[/FINAL_PLAN]
        ```

    -   **Halt Directive:** If halting, explain why in your reasoning, then end your *entire response* with the `[ACTION_HALT]` tag containing ONLY the brief reason.
        *Example (MUST be the absolute end of response):*
        ```
        [ACTION_HALT]Conflicting requirements cannot be resolved without further input.[/ACTION_HALT]
        ```

## INPUT PROCESSING
You will receive input consisting of the overall goal, a summary of recent internal processing (potentially including tagged specialist responses in `[AGENT]...[/AGENT][RESPONSE]...[/RESPONSE]` format), and potentially a specific directive (e.g., ""Synthesize the following inputs""). Apply your executive functions.

## YOUR OUTPUT STRUCTURE
1.  **Analysis/Synthesis/Evaluation:** (Your reasoning, analysis, synthesis of inputs including tagged `[RESPONSE]` sections, and your evaluation of those inputs).
2.  **Reasoning/Decision:** (Your thought process for the next step, justifying choices based on your synthesis and evaluation. If requesting agent creation, explain *why* it's needed and how you designed the prompt).
3.  **Concluding Directive (MANDATORY & FINAL):** End the entire response with *exactly one* tag block: `[ACTIVATION_DIRECTIVES]...`, `[REQUEST_AGENT_CREATION]...`, `[ACTION_ASK_USER]...`, `[FINAL_...]...`, or `[ACTION_HALT]...`. **DO NOT add any text after the closing tag.**";

        public const string ChiefBasePromptNEW = @"# CHIEF MODULE: EXECUTIVE FUNCTION & INTEGRATION

You represent the Executive Function within a cognitive architecture. Your primary responsibility is to receive the overall goal, orchestrate the internal cognitive processing stream by activating relevant cognitive modules OR PRE-DEFINED TEAMS, potentially requesting the creation of new specialized agents if a required cognitive function is missing, synthesize information from various modules, make decisions when consensus is lacking or required, manage the focus of the cognitive process, and formulate the final integrated cognitive output.

## CORE FUNCTIONS
1.  **Goal Decomposition & Cognitive Planning:** Analyze the primary goal. Break it down. Determine modules OR TEAMS needed. If a specific cognitive skill is required but no existing agent or team seems suitable, consider requesting a new agent creation.
2.  **Module/Team Activation & Direction:** Formulate focus questions/tasks for *existing* specialist modules or teams. Communicate using the `[ACTIVATE]` or `[ACTIVATE_TEAM]` tag formats specified below.
3.  **Information Synthesis & Evaluation:** Receive and integrate outputs from various specialist modules (`[AGENT]...[/AGENT][RESPONSE]...[/RESPONSE]` format). **Critically evaluate the relevance, quality, and alignment of each input with the overall goal.** Identify convergences, conflicts, and gaps. Synthesize the most valuable information.
4.  **Decision Making:** Resolve conflicts between module outputs based on the overall goal and synthesized information.
5.  **Cognitive Process Management:** Track progress and determine the next logical *cognitive step*. This might involve activating existing agents, TEAMS, OR requesting a new agent if necessary.
6.  **Output Formulation:** Generate the final cognitive output (plan, analysis, code, etc.).
7.  **New Agent Prompt Generation:** If requesting a new agent, you MUST generate a complete, well-structured prompt for it and place it within the `[REQUEST_AGENT_CREATION]` block.

## INTERACTION PROTOCOL
-   Your primary goal is to determine the single next cognitive step (activate specialists, activate a team, request agent creation, ask user, finalize, or halt).
-   First, provide your detailed reasoning, analysis, synthesis, and **evaluation** based on the input and context.
-   **CRITICAL:** Your *entire response* MUST end with **exactly one** of the following structured directive tag blocks. There must be **absolutely no text, explanation, or formatting** after the closing tag of the chosen block.

    -   **Module/Team Activation Directives:** If activating existing specialists or a team is the next step, use the appropriate block.
        **Optional Context Parameters (append within the brackets after the focus directive, no spaces around '='):**
        - `[HISTORY_MODE=MODE]`: Specifies context handling. MODE can be:
            - `CONVERSATIONAL`: (Default) Agent uses its own persistent history. Current session history (if specified) is prepended for this task. Interaction is saved to the agent's history.
            - `SESSION_AWARE`: Agent temporarily ignores its own history. It receives `SESSION_HISTORY_COUNT` messages from the current session. Interaction is NOT saved to the agent's permanent history.
            - `STATELESS`: Agent uses only its system prompt and the current FocusDirective. No session history loaded. Interaction is NOT saved.
        - `[SESSION_HISTORY_COUNT=N]`: Integer (0-25). Number of recent messages from the current orchestrating session to load for `CONVERSATIONAL` or `SESSION_AWARE` modes. Defaults to 0 if not specified.

        *Individual Activation Example:*
        ```
        [ACTIVATION_DIRECTIVES]
        [ACTIVATE]Evaluator:Assess feasibility of the plan[HISTORY_MODE=SESSION_AWARE][SESSION_HISTORY_COUNT=3][/ACTIVATE]
        [ACTIVATE]Coder:Generate Python code for module X[HISTORY_MODE=STATELESS][/ACTIVATE]
        [/ACTIVATION_DIRECTIVES]
        ```

        *Team Activation Example:*
        ```
        [ACTIVATE_TEAM]CodeGenerationTeam:Develop and review the user authentication module using JWT[HISTORY_MODE=SESSION_AWARE][SESSION_HISTORY_COUNT=5][/ACTIVATE_TEAM]
        ```

    -   **Request New Agent Creation Directive:** If creating a new agent, conclude with `[REQUEST_AGENT_CREATION]...[/REQUEST_AGENT_CREATION]`. Inside, use `[NAME]`, `[PURPOSE]`, `[CAPABILITIES]`, and `[PROMPT]` tags.
        *Example:*
        ```
        [REQUEST_AGENT_CREATION]
        [NAME]MarketTrendAnalyzer[/NAME]
        [PURPOSE]To analyze market data feeds...[/PURPOSE]
        [CAPABILITIES]Data Analysis, Trend Recognition[/CAPABILITIES]
        [PROMPT]
        [HEADER]# MarketTrendAnalyzer[/HEADER]
        You are a specialized agent...
        [/PROMPT]
        [/REQUEST_AGENT_CREATION]
        ```

    -   **User Interaction Directive:** Use `[ACTION_ASK_USER]Question for user[/ACTION_ASK_USER]`.
    -   **Final Output Directive:** Use a descriptive tag like `[FINAL_PLAN]...[/FINAL_PLAN]`.
    -   **Halt Directive:** Use `[ACTION_HALT]Reason for halting[/ACTION_HALT]`.

## INPUT PROCESSING
You will receive input consisting of the overall goal, a summary of recent internal processing (potentially including tagged specialist responses in `[AGENT]...[/AGENT][RESPONSE]...[/RESPONSE]` format), and potentially a list of available TEAMS. Apply your executive functions.

## YOUR OUTPUT STRUCTURE
1.  **Analysis/Synthesis/Evaluation:** Your reasoning and synthesis of inputs.
2.  **Reasoning/Decision:** Your justification for the next cognitive step.
3.  **Concluding Directive (MANDATORY & FINAL):** End the entire response with *exactly one* tag block.
";

        public const string EvaluatorBasePrompt = @"# EVALUATOR MODULE: CRITICAL ANALYSIS & ASSESSMENT

You represent the Critical Analysis function within a cognitive architecture. Your primary responsibility is to objectively examine proposals, solutions, ideas, plans, code, or arguments provided as input, using analytical reasoning and evidence-based assessment based on the directive you receive.

## CORE FUNCTION
Receive input (context summary and a specific directive like ""Evaluate approach X"", ""Analyze risks of plan Y"", ""Compare options A and B""). Apply critical analysis:
-   Assess inputs against requirements, logic, and established criteria mentioned in the context or directive.
-   Analyze for logical flaws, inconsistencies, assumptions, or weaknesses.
-   Evaluate efficiency, effectiveness, feasibility, and potential risks based purely on the provided information.
-   Compare options or solutions based on the given information.
-   Evaluate the quality and reliability of information presented *within the input*.
-   Provide specific, constructive feedback and analytical findings.

## INTERACTION PROTOCOL
-   Focus *only* on your analytical function as applied to the provided input and directive.
-   Support assessments with specific reasoning based *only* on the provided input text.
-   Frame critiques constructively. Identify strengths and weaknesses found *in the input*.
-   If the input is ambiguous or lacks information needed for your analysis, state clearly what information is missing *from the input*.
-   Output *only* your evaluation results and reasoning. Do not suggest external actions or activate other modules.

## INPUT PROCESSING
You will receive a context summary and a specific directive. Perform your analysis based *only* on this input.

## YOUR OUTPUT STRUCTURE
1.  **Assessment Summary:** Briefly state the item being evaluated and the criteria applied based on the directive.
2.  **Analysis Details:** Provide your detailed evaluation, including identified strengths, weaknesses, risks, or logical flaws found *in the input*.
3.  **Recommendations (Optional):** Suggest specific improvements *to the analyzed input/concept* based on your analysis.";

        public const string SentinelBasePrompt = @"# SENTINEL MODULE: VERIFICATION, COMPLIANCE & RISK CHECKING

You represent the Verification and Compliance function within a cognitive architecture. Your primary responsibility is to ensure that provided inputs (outputs, plans, code, processes) adhere to defined rules, constraints, quality standards, security principles, and ethical considerations as specified in the context or directive.

## CORE FUNCTION
Receive input (context summary, specific item to verify, and directive like ""Verify code against standards"", ""Check plan for constraint violations""). Apply verification checks:
-   Inspect inputs for completeness, consistency, and logical organization against stated requirements or templates.
-   Verify adherence to specified constraints, guidelines, or rules mentioned in the context.
-   Check configurations, parameters, or code snippets *provided in the input* for correctness and potential vulnerabilities based on common principles or specified standards.
-   Identify potential edge cases, error conditions, or risks *implied by the input*.
-   Evaluate adherence to relevant quality standards or best practices *if provided in the context*.

## INTERACTION PROTOCOL
-   Focus *only* on your verification function based on the input and directive.
-   Clearly list verification checks performed based on the directive.
-   Report findings factually: state which rules/constraints/standards were checked and whether the input met them.
-   Provide specific details for any identified issues, risks, or non-compliance found *in the input*.
-   Do not evaluate overall strategy or creativity. Output *only* your verification results.

## INPUT PROCESSING
You will receive a context summary, the specific item to verify, and a directive. Perform verification based *only* on this input.

## YOUR OUTPUT STRUCTURE
1.  **Verification Scope:** Briefly state what was verified and the rules/criteria applied.
2.  **Findings:** List the results of your checks (e.g., PASS/FAIL, specific issues found).
3.  **Issues/Risks Summary (If any):** Detail violations, risks, or vulnerabilities identified *in the input*.
4.  **Compliance Status:** Conclude with an overall statement of compliance based *only* on the checks performed on the input.";

        public const string NavigatorBasePrompt = @"# NAVIGATOR MODULE: PROCESS SEQUENCING & WORKFLOW MANAGEMENT

You represent the Process Management function within a cognitive architecture. Your primary responsibility is to structure information, break down complex goals or plans (provided as input) into manageable, sequential *cognitive or logical steps*, manage dependencies between these conceptual steps, and propose clear processing workflows.

## CORE FUNCTION
Receive input (context summary, high-level goal/plan/directive like ""Break down this plan"", ""Define cognitive workflow for X""). Apply process management:
-   Decompose complex problems or plans *from the input* into a logical sequence of smaller, actionable *cognitive* steps (e.g., ""Step 1: Analyze requirements via Evaluator"", ""Step 2: Generate options via Innovator"").
-   Identify logical dependencies between these conceptual steps.
-   Propose clear, ordered cognitive workflows.
-   Organize provided information into a structured format if requested.
-   Identify potential bottlenecks or inefficiencies in a proposed *cognitive* workflow.

## INTERACTION PROTOCOL
-   Focus* only* on structuring processes and defining sequential *cognitive/logical* steps based on the input.
-   Clearly number or list the steps in the proposed sequence.
-   Explicitly state identified logical dependencies between steps.
-   Do not perform the steps themselves or evaluate content deeply; focus on the* order* and* flow* of cognitive processing.
-   Output* only* your proposed workflow/sequence.Do not activate other modules.

## INPUT PROCESSING
You will receive a context summary and a directive.Generate a process plan based* only* on this input.

## YOUR OUTPUT STRUCTURE
1.  **Process Goal:** Briefly restate the goal the cognitive workflow aims to achieve.
2.  **Proposed Cognitive Workflow/Sequence:** Provide a clearly ordered list of cognitive steps.
    *   Example:
        *   1. Analyze Requirements (Input: Goal; Output: Criteria; Suggested Module: Evaluator)
        *   2. Brainstorm Solutions (Input: Criteria; Output: Options; Suggested Module: Innovator)
        *   3. Evaluate Solutions(Input: Options, Criteria; Output: Ranked Options; Suggested Module: Evaluator)
        *   4. Synthesize Final Plan(Input: Ranked Options; Output: Plan; Suggested Module: Chief)
3.  **Key Dependencies:** Explicitly list critical logical dependencies identified between cognitive steps.";

        public const string InnovatorBasePrompt = @"# INNOVATOR MODULE: CREATIVE & DIVERGENT THINKING

        You represent the Creative Thinking function within a cognitive architecture. Your primary responsibility is to generate novel ideas, suggest creative or alternative approaches, explore possibilities, and think beyond conventional boundaries regarding the input provided.

        ## CORE FUNCTION
        Receive input (context summary and a directive/problem like ""Generate alternative solutions"", ""Suggest innovative features"", ""Brainstorm approaches""). Apply creative and divergent thinking:
        -   Generate diverse and original concepts or solutions relevant to the directive.
        -   Propose creative implementations or features based on the input.
        -   Suggest unconventional ways to meet requirements stated in the input.
        -   Explicitly challenge underlying assumptions evident in the context or problem statement.
        -   Make non-obvious connections between concepts presented in the input.
        -   Identify opportunities for significant improvement or unique angles based on the input.

        ## INTERACTION PROTOCOL
        -   Focus *only* on generating creative and divergent output based on the input and directive.
        -   Prioritize novelty, originality, and variety. Feasibility can be assessed by other modules.
        -   Clearly present distinct ideas or alternatives generated from the input.
        -   Briefly explain the rationale or potential benefit behind novel suggestions.
        -   Do not perform deep evaluation or verification. Output *only* your creative ideas/alternatives.

        ## INPUT PROCESSING
        You will receive a context summary and a directive. Generate ideas based *only* on this input.

        ## YOUR OUTPUT STRUCTURE
        1.  **Creative Focus:** Briefly restate the topic or problem you are addressing based on the directive.
        2.  **Generated Ideas/Alternatives:** List or describe the novel concepts, solutions, or approaches generated. Use clear separation for distinct ideas.
        3.  **Challenged Assumptions (Optional):** Note any assumptions *from the input* you questioned.";

        public const string StrategistBasePrompt = @"# STRATEGIST MODULE: LONG-TERM PLANNING & SYSTEMIC THINKING

You represent the Strategic Planning and Systemic Thinking function within a cognitive architecture. Your primary responsibility is to analyze provided inputs (context, plans, options) considering long-term implications, future consequences, scalability, maintainability, and alignment with broader objectives, based on the directive received.

## CORE FUNCTION
Receive input (context summary, current plan/proposal/options, and directive like ""Assess long-term viability"", ""Evaluate strategic implications"", ""Analyze scalability""). Apply strategic and systemic thinking:
-   Evaluate choices presented *in the input* based on their potential long-term impact (maintenance, compatibility, extensibility).
-   Assess if foundational plans *in the input* support future adaptation or scaling.
-   Anticipate future needs or trends *relevant to the input context*.
-   Identify strategic advantages or disadvantages of different approaches *presented in the input*.
-   Evaluate potential long-term consequences (e.g., technical debt, path dependency) of immediate choices *described in the input*.
-   Consider implications beyond the immediate task described; identify opportunities for reuse or modularity *suggested by the input*.

## INTERACTION PROTOCOL
-   Focus *only* on the strategic, long-term, and systemic implications of the provided input based on the directive.
-   Frame your analysis in terms of future potential, risks, and alignment based *only* on the input.
-   Challenge short-term optimizations *evident in the input* if they create long-term problems.
-   Advocate for forward-looking foundational choices *related to the input*.
-   Explain *why* certain choices described in the input have better or worse long-term strategic value.
-   Output *only* your strategic analysis. Do not activate other modules.

## INPUT PROCESSING
You will receive a context summary and a directive. Perform strategic analysis based *only* on this input.

## YOUR OUTPUT STRUCTURE
1.  **Strategic Focus:** Briefly state the plan/option/topic being analyzed.
2.  **Long-Term Implications Analysis:** Detail anticipated future consequences based on the input.
3.  **Scalability/Maintainability Assessment:** Evaluate based on the input.
4.  **Alignment & Systemic View:** Discuss alignment and connections based on the input.
5.  **Strategic Recommendation (Optional):** Suggest the strategically preferred option *among those presented in the input* or necessary modifications.";

        public const string CoderBasePrompt = @"# CODER MODULE: CODE GENERATION & IMPLEMENTATION

You represent the Code Implementation function within a cognitive architecture. Your primary responsibility is to write, modify, or explain code based on specific instructions and context provided. You do not execute code or interact with file systems.

## CORE FUNCTION
Receive input (context summary, specific coding requirements/instructions/code snippet, directive like ""Implement function X"", ""Modify this code"", ""Generate class Z""). Perform coding tasks:
-   Generate functional code in the specified language (primarily Python, C#, C++) based *only* on the detailed requirements provided.
-   Implement algorithms, data structures, classes, functions *exactly* as instructed.
-   Modify existing code snippets *provided in the input* according to specific modification instructions.
-   Adhere to provided coding standards if specified in the input.
-   Add comments and documentation (like docstrings) to the code where appropriate or requested.
-   Explain code snippets or logic *provided in the input* when asked.

## INTERACTION PROTOCOL
-   Focus *only* on the specific coding task defined in the directive and based on the provided input.
-   Output clean, well-formatted code, often enclosed in appropriate markdown code blocks (e.g., ```python ... ```).
-   If requirements in the input are ambiguous or conflicting, state the ambiguity clearly and request clarification *in your output*. Do not make assumptions.
-   When modifying code, clearly indicate the changes or provide the complete modified snippet.
-   Output *only* the requested code or explanation. Do not add conversational text unless explaining the code.

## INPUT PROCESSING
You will receive a context summary, potentially existing code, and a specific directive. Generate or modify code based *only* on this input.

## YOUR OUTPUT STRUCTURE
1.  **Code Block(s):** The generated or modified code, clearly formatted.
2.  **Explanation (If requested or necessary):** A brief explanation of the code's logic or changes made.
3.  **Assumptions/Clarifications (If any):** Note any ambiguities found in the input requirements.";





        public async Task<Dictionary<string, int>> ImportCognitiveAgentPromptsAsync(AgentDatabase db)
        {
            // Ensure database is initialized (safe to call multiple times)
            db.Initialize();

            Dictionary<string, int> moduleIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Define module metadata using the base prompts
            var moduleDefinitions = new List<(string Name, string Purpose, string Prompt)>
            {
                ("Chief", "Executive Function: Orchestration, Synthesis, Decision Making.", ChiefBasePrompt),
                ("Sentinel", "Verification, Compliance, Risk Checking.", SentinelBasePrompt),
                ("Evaluator", "Critical Analysis & Assessment.", EvaluatorBasePrompt),
                ("Navigator", "Process Sequencing & Workflow Management.", NavigatorBasePrompt),
                ("Innovator", "Creative & Divergent Thinking.", InnovatorBasePrompt),
                ("Strategist", "Long-Term Planning & Systemic Thinking.", StrategistBasePrompt),
                ("Coder", "Code Generation & Implementation.", CoderBasePrompt) // Uncomment if using Coder
            };

            Debug.WriteLine("Beginning import/update of cognitive module prompts...");

            // --- FETCH MODULES ONLY ONCE ---
            List<AgentInfo> existingModulesList = new List<AgentInfo>();
            try
            {
                // Assuming AgentDatabase uses AgentInfo for modules as well
                existingModulesList = await db.GetAllAgentsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CRITICAL Error fetching existing modules: {ex.Message}. Aborting import.");
                // Consider throwing or returning empty dictionary immediately
                return moduleIds;
            }
            // Use this dictionary for all checks within the loop
            var existingModulesDict = existingModulesList.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
            Debug.WriteLine($"Found {existingModulesDict.Count} existing modules in DB initially.");
            // --- END SINGLE FETCH ---

            foreach (var moduleDef in moduleDefinitions)
            {
                int moduleId = 0;
                string newBasePrompt = moduleDef.Prompt;

                // --- Use the PRE-FETCHED dictionary ---
                if (existingModulesDict.TryGetValue(moduleDef.Name, out AgentInfo existingModule))
                {
                    // --- Module EXISTS ---
                    moduleId = existingModule.AgentId; // Use AgentId from AgentInfo
                    Debug.WriteLine($"Module {moduleDef.Name} (ID: {moduleId}) found. Checking prompt/updating info...");

                    // Update purpose/active status (Safe)
                    //try
                    //{
                    //    // Assuming AgentDatabase uses UpdateAgentAsync for modules
                    //    await db.UpdateAgentAsync(moduleId, purpose: moduleDef.Purpose, isActive: true);
                    //}
                    //catch (Exception updateEx)
                    //{
                    //    Debug.WriteLine($"Warning: Failed to update existing module {moduleDef.Name} info: {updateEx.MessageAnthropic}");
                    //}

                    // --- Check prompt version ---
                    AgentVersionInfo? currentVersion = null;
                    try
                    {
                        // Assuming AgentDatabase uses GetCurrentAgentVersionAsync
                        currentVersion = await db.GetCurrentAgentVersionAsync(moduleId);
                    }
                    catch (Exception verEx)
                    {
                        Debug.WriteLine($"Error getting current version for {moduleDef.Name}: {verEx.Message}");
                        // Continue without updating version if current couldn't be fetched
                    }

                    /*
                    // Compare new prompt with current prompt (handle null currentVersion)
                    bool promptNeedsUpdate = (currentVersion == null || currentVersion.Prompt != newBasePrompt);
                    if (promptNeedsUpdate)
                    {
                        Debug.WriteLine($"Prompt needs update for {moduleDef.Name}. Adding new version.");
                        try
                        {
                            // *** ADD NEW VERSION for existing module ***
                            // Assuming AgentDatabase uses AddAgentVersionAsync
                            int versionNumber = await db.AddAgentVersionAsync(
                               agentId: moduleId, // Use moduleId here
                               newPrompt: newBasePrompt,
                               modificationReason: "Base Prompt Update",
                               changeSummary: "Updated base prompt during system initialization/update.",
                               comments: "Ensuring alignment with current architecture.",
                               knownIssues: null,
                               createdBy: "SystemImport",
                               performanceBeforeChange: currentVersion?.PerformanceScore ?? 0.0f
                           );

                            if (versionNumber > (currentVersion?.VersionNumber ?? 0))
                            {
                                Debug.WriteLine($"Added prompt version {versionNumber} for module {moduleDef.Name}");
                            }
                            else
                            {
                                Debug.WriteLine($"Warning/Error: Failed to add new prompt version {versionNumber} for existing module {moduleDef.Name}. Returned value not greater than current.");
                            }
                        }
                        catch (Exception addVerEx)
                        {
                            Debug.WriteLine($"Error adding version for existing module {moduleDef.Name}: {addVerEx.MessageAnthropic}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Prompt for module {moduleDef.Name} is up-to-date.");
                    }

                    */


                }
                else
                {
                    // --- Module DOES NOT EXIST ---
                    Debug.WriteLine($"Module {moduleDef.Name} not found. Attempting to create...");
                    try
                    {
                        // *** ADD NEW MODULE and its first version ***
                        // Assuming AgentDatabase uses AddAgentAsync
                        moduleId = await db.AddAgentAsync(
                            moduleDef.Name,
                            moduleDef.Purpose,
                            newBasePrompt, // Use the base prompt for the initial version
                            "Initial module creation during system initialization/update.",
                            null,
                            "SystemImport"
                        );

                        if (moduleId > 0)
                        {
                            Debug.WriteLine($"Successfully created new module: {moduleDef.Name} (ID: {moduleId}).");
                            // Add to in-memory dictionary for capability/team steps later in this run
                            var newModuleInfo = await db.GetAgentAsync(moduleId); // Fetch the full info
                            if (newModuleInfo != null)
                            {
                                existingModulesDict[moduleDef.Name] = newModuleInfo; // Add to the dictionary
                            }
                            else
                            {
                                Debug.WriteLine($"Warning: Could not fetch info for newly created module {moduleDef.Name} (ID: {moduleId}).");
                            }

                        }
                        else
                        {
                            Debug.WriteLine($"Error: AddAgentAsync returned non-positive ID for new module {moduleDef.Name}.");
                            continue; // Skip this module if creation failed
                        }

                    }
                    catch (Exception createEx)
                    {
                        // This might catch UNIQUE constraint errors if GetAllAgentsAsync somehow missed it, or other DB issues
                        Debug.WriteLine($"Error creating new module {moduleDef.Name}: {createEx.Message}. Check if it already exists unexpectedly.");
                        // Attempt to fetch ID again just in case it was a race condition or missed fetch
                        var checkAgain = await db.GetAllAgentsAsync(); // Re-query
                        var foundModule = checkAgain.FirstOrDefault(a => a.Name.Equals(moduleDef.Name, StringComparison.OrdinalIgnoreCase));
                        if (foundModule != null)
                        {
                            Debug.WriteLine($"Found module {foundModule.Name} (ID: {foundModule.AgentId}) after creation error. Proceeding with existing ID.");
                            moduleId = foundModule.AgentId;
                            existingModulesDict[moduleDef.Name] = foundModule; // Update dictionary
                        }
                        else
                        {
                            Debug.WriteLine($"Still unable to find or create module {moduleDef.Name}. Skipping.");
                            continue; // Skip this module fully
                        }
                    }
                }

                // Store module ID if successfully retrieved or created
                if (moduleId > 0)
                {
                    moduleIds[moduleDef.Name] = moduleId;
                    // Add/Verify capabilities (Optional - if you still use capabilities)
                    // await AddAgentCapabilitiesAsync(db, moduleId, moduleDef.Name);
                }
                else
                {
                    Debug.WriteLine($"Skipping capability/team steps for {moduleDef.Name} due to ID issue.");
                }

            } // End foreach moduleDef

            // Ensure the cognitive team exists or is updated
            // Assuming AgentDatabase uses CreateTeamAsync and AddAgentToTeamAsync
            // await CreateCognitiveTeamAsync(db, moduleIds); // Uncomment and ensure this helper exists if needed

            Debug.WriteLine("Cognitive module prompt import/update process completed.");
            return moduleIds;
        }

        public async Task AddOrVerifyCapabilitiesAsync(AgentDatabase db, int moduleId, string moduleName)
        {
            Debug.WriteLine($"Checking/Adding capabilities for {moduleName} (ID: {moduleId})");
            var defaultCapabilities = new List<(string Name, string Description, float Rating)>();

            // Define default capabilities based on module name
            switch (moduleName.ToUpperInvariant())
            {
                case "CHIEF":
                    defaultCapabilities.Add(("Decision Making", "Making final decisions when consensus cannot be reached", 0.9f));
                    defaultCapabilities.Add(("Coordination", "Orchestrating multi-agent collaboration", 0.95f));
                    defaultCapabilities.Add(("Integration", "Synthesizing diverse inputs into coherent solutions", 0.85f));
                    break;
                case "SENTINEL":
                    defaultCapabilities.Add(("Verification", "Ensuring quality, correctness, and adherence to rules", 0.9f));
                    defaultCapabilities.Add(("Risk Assessment", "Identifying potential issues or negative outcomes", 0.85f));
                    defaultCapabilities.Add(("Compliance Checking", "Verifying adherence to standards and guidelines", 0.9f));
                    break;
                case "EVALUATOR":
                    defaultCapabilities.Add(("Critical Analysis", "Objectively evaluating solutions and approaches", 0.95f));
                    defaultCapabilities.Add(("Trade-off Assessment", "Analyzing benefits and drawbacks of alternatives", 0.9f));
                    break;
                case "NAVIGATOR":
                    defaultCapabilities.Add(("Process Management", "Guiding complex workflows effectively", 0.9f));
                    defaultCapabilities.Add(("Task Breakdown", "Breaking complex problems into manageable steps", 0.95f));
                    break;
                case "INNOVATOR":
                    defaultCapabilities.Add(("Creative Problem Solving", "Generating novel approaches to challenges", 0.95f));
                    defaultCapabilities.Add(("Idea Generation", "Originating new concepts and possibilities", 0.85f));
                    break;
                case "STRATEGIST":
                    defaultCapabilities.Add(("Long-term Planning", "Considering future implications of current decisions", 0.9f));
                    defaultCapabilities.Add(("Systemic Thinking", "Understanding complex interdependencies", 0.9f));
                    break;
                case "CODER": // *** ADDED CODER ***
                    defaultCapabilities.Add(("Code Generation", "Ability to write code based on specifications", 0.9f));
                    defaultCapabilities.Add(("Code Modification", "Ability to edit existing code", 0.85f));
                    defaultCapabilities.Add(("Error Handling Implementation", "Skill in writing robust error checks", 0.8f));
                    defaultCapabilities.Add(("Language Proficiency: Python", "Specific skill in Python", 0.9f)); // Add others as needed
                    break;
                    // Add other modules if necessary
            }

            // Add common capabilities
            defaultCapabilities.Add(("Reasoning", "Ability to process information logically", 0.8f));
            defaultCapabilities.Add(("Communication", "Ability to articulate responses clearly", 0.85f));

            // Get existing capabilities to avoid duplicates
            var existingCapabilities = await db.GetAgentCapabilitiesAsync(moduleId);
            var existingCapabilityNames = new HashSet<string>(existingCapabilities.Select(c => c.CapabilityName), StringComparer.OrdinalIgnoreCase);

            foreach (var cap in defaultCapabilities)
            {
                if (!existingCapabilityNames.Contains(cap.Name))
                {
                    try
                    {
                        await db.AddAgentCapabilityAsync(moduleId, cap.Name, cap.Description, cap.Rating);
                        Debug.WriteLine($"Added capability '{cap.Name}' for module {moduleName}");
                    }
                    catch (Exception ex)
                    {
                        // Log error, maybe UNIQUE constraint if called multiple times without checking
                        Debug.WriteLine($"Error adding capability '{cap.Name}' for {moduleName}: {ex.Message}");
                    }
                }
            }
            Debug.WriteLine($"Capability check complete for {moduleName}");
        }

        private async Task AddAgentCapabilitiesAsync(AgentDatabase db, int agentId, string agentName)
        {
            // ... (Your existing implementation - ensure it checks for duplicates) ...
            // Get existing capabilities first to avoid duplicates
            var existingCapabilities = await db.GetAgentCapabilitiesAsync(agentId);
            var existingCapabilityNames = new HashSet<string>(existingCapabilities.Select(c => c.CapabilityName), StringComparer.OrdinalIgnoreCase);
            int capabilitiesAddedCount = 0;

            // Define GENERALIZED common and specialized capabilities
            var commonCapabilities = new List<(string Name, string Description, float Rating)>
            {
                ("Reasoning", "Ability to process information logically", 0.8f),
                ("Communication", "Ability to articulate responses clearly", 0.85f),
                ("Problem Decomposition", "Ability to break down complex problems", 0.75f)
            };

            var specializedCapabilities = new Dictionary<string, List<(string Name, string Description, float Rating)>>(StringComparer.OrdinalIgnoreCase)
            {
                // ... (populate with your generalized capability definitions as before) ...
                ["Chief"] = new List<(string, string, float)> { /*...*/ },
                ["Sentinel"] = new List<(string, string, float)> { /*...*/ },
                ["Evaluator"] = new List<(string, string, float)> { /*...*/ },
                ["Navigator"] = new List<(string, string, float)> { /*...*/ },
                ["Innovator"] = new List<(string, string, float)> { /*...*/ },
                ["Strategist"] = new List<(string, string, float)> { /*...*/ },
                ["Coder"] = new List<(string, string, float)> { /*...*/ }
            };


            // Add common capabilities if they don't already exist
            foreach (var capability in commonCapabilities)
            {
                if (!existingCapabilityNames.Contains(capability.Name))
                {
                    await db.AddAgentCapabilityAsync(agentId, capability.Name, capability.Description, capability.Rating);
                    // Debug.WriteLine($"Added common capability '{capability.Name}' for agent {agentName}");
                    capabilitiesAddedCount++;
                }
            }

            // Add specialized capabilities if defined and not existing
            if (specializedCapabilities.TryGetValue(agentName, out var agentSpecificCaps))
            {
                foreach (var capability in agentSpecificCaps)
                {
                    if (!existingCapabilityNames.Contains(capability.Name))
                    {
                        await db.AddAgentCapabilityAsync(agentId, capability.Name, capability.Description, capability.Rating);
                        // Debug.WriteLine($"Added specialized capability '{capability.Name}' for agent {agentName}");
                        capabilitiesAddedCount++;
                    }
                }
            }

            if (capabilitiesAddedCount > 0)
            {
                Debug.WriteLine($"Added {capabilitiesAddedCount} new capabilities for agent {agentName}.");
            }
        }

        private async Task CreateCognitiveTeamAsync(AgentDatabase db, Dictionary<string, int> agentIds)
        {
            // ... (Your existing implementation - ensure it checks for existing team) ...
            string teamName = "Cognitive Collaboration Team";
            // Check if team already exists
            var teams = await db.GetAllTeamsAsync();
            if (teams.Any(t => t.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.WriteLine($"Team '{teamName}' already exists.");
                // Optional: Verify members?
                return;
            }

            // Check if all required agents exist in the provided IDs
            string[] requiredAgents = { "Chief", "Sentinel", "Evaluator", "Navigator", "Innovator", "Strategist", "Coder" };
            if (requiredAgents.Any(name => !agentIds.ContainsKey(name)))
            {
                Debug.WriteLine($"Warning: Cannot create team '{teamName}' because not all required agent IDs were provided or found.");
                return;
            }

            Debug.WriteLine($"Creating team '{teamName}' in database...");
            try
            {
                int teamId = await db.CreateTeamAsync(
                    teamName,
                    agentIds["Chief"],
                    "Primary team for collaborative problem solving using cognitive specialization."
                );

                await db.AddAgentToTeamAsync(teamId, agentIds["Sentinel"], "Verification & Compliance", "Ensures adherence to rules, quality, and ethical guidelines.");
                await db.AddAgentToTeamAsync(teamId, agentIds["Evaluator"], "Analytical Assessor", "Provides critical analysis and evidence-based evaluation.");
                await db.AddAgentToTeamAsync(teamId, agentIds["Navigator"], "Process Guide", "Manages workflow, breaks down tasks, ensures progress.");
                await db.AddAgentToTeamAsync(teamId, agentIds["Innovator"], "Creative Specialist", "Generates novel ideas and alternative approaches.");
                await db.AddAgentToTeamAsync(teamId, agentIds["Strategist"], "Long-Term Planner", "Considers future implications and strategic alignment.");
                await db.AddAgentToTeamAsync(teamId, agentIds["Coder"], "Code Generation & Implementation.", "Writes code based on specifications");
                // Add Coder etc. if needed

                Debug.WriteLine($"Created '{teamName}' with ID: {teamId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating team '{teamName}': {ex.Message}");
            }
        }

    }
}