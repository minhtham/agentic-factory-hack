using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner
{
    public sealed class RepairPlannerAgent(
        AIProjectClient projectClient,
        CosmosDbService cosmosDb,
        IFaultMappingService faultMapping,
        string modelDeploymentName,
        ILogger<RepairPlannerAgent> logger)
    {
        private const string AgentName = "RepairPlannerAgent";
        private const string AgentInstructions = """
You are a Repair Planner Agent for tire manufacturing equipment.
Generate a repair plan with tasks, timeline, and resource allocation.
Return the response as valid JSON matching the WorkOrder schema.

Output JSON with these fields:
- workOrderNumber, machineId, title, description
- type: "corrective" | "preventive" | "emergency"
- priority: "critical" | "high" | "medium" | "low"
- status, assignedTo (technician id or null), notes
- estimatedDuration: integer (minutes, e.g. 60 not "60 minutes")
- partsUsed: [{ partId, partNumber, quantity }]
- tasks: [{ sequence, title, description, estimatedDurationMinutes (integer), requiredSkills, safetyNotes }]

IMPORTANT: All duration fields must be integers representing minutes (e.g. 90), not strings.

Rules:
- Assign the most qualified available technician
- Include only relevant parts; empty array if none needed
- Tasks must be ordered and actionable
""";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
        {
            logger.LogInformation("Creating agent '{AgentName}' with model '{Model}'", AgentName, modelDeploymentName);
            var definition = new PromptAgentDefinition(model: modelDeploymentName) { Instructions = AgentInstructions };
            await projectClient.Agents.CreateAgentVersionAsync(AgentName, new AgentVersionCreationOptions(definition), ct).ConfigureAwait(false);
            // For demo purposes record a generated version id
            var versionId = Guid.NewGuid().ToString()[..8];
            logger.LogInformation("Agent version: {VersionId}", versionId);
        }

        public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(DiagnosedFault fault, CancellationToken ct = default)
        {
            if (fault == null) throw new ArgumentNullException(nameof(fault));

            logger.LogInformation("Planning repair for {MachineId}, fault={FaultType}", fault.MachineId, fault.FaultType);

            // 1. Get required skills and parts from mapping
            var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType ?? string.Empty);
            var requiredParts = faultMapping.GetRequiredParts(fault.FaultType ?? string.Empty);

            // 2. Query technicians and parts from Cosmos DB
            var technicians = await cosmosDb.QueryTechniciansBySkillsAsync(requiredSkills, ct).ConfigureAwait(false);
            if (!technicians.Any())
            {
                // Fallback to all technicians
                technicians = (await cosmosDb.GetAllTechniciansAsync(ct).ConfigureAwait(false)).ToList();
            }

            var partsInventory = await cosmosDb.GetAllPartsAsync(ct).ConfigureAwait(false);

            // 3. Build prompt with context
            var prompt = BuildPrompt(fault, requiredSkills, requiredParts, technicians, partsInventory);

            // Ensure agent version exists
            await EnsureAgentVersionAsync(ct).ConfigureAwait(false);

            // 4. Invoke the agent
            var agent = projectClient.GetAIAgent(AgentName);
            logger.LogInformation("Invoking agent '{AgentName}'", AgentName);
            var runResponse = await agent.RunAsync(prompt).ConfigureAwait(false);
            var text = runResponse.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogError("Agent returned empty response for fault {FaultId}", fault.Id);
                throw new InvalidOperationException("Agent returned empty response");
            }

            // 5. Parse response into WorkOrder
            WorkOrder wo;
            var cleaned = ExtractJsonFromAgent(text);
            logger.LogDebug("Raw agent response: {Raw}", text);
            logger.LogDebug("Cleaned agent response: {Cleaned}", cleaned);

            try
            {
                wo = JsonSerializer.Deserialize<WorkOrder>(cleaned, JsonOptions) ?? throw new JsonException("Deserialized WorkOrder is null");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to parse agent response. Raw response: {Raw}. Cleaned response: {Cleaned}", text, cleaned);
                throw;
            }

            // Apply defaults and normalization
            wo.Id ??= Guid.NewGuid().ToString();
            wo.MachineId ??= fault.MachineId;
            wo.CreatedAt = DateTime.UtcNow;
            wo.UpdatedAt = DateTime.UtcNow;
            wo.Priority ??= "medium";
            wo.Status ??= "open";

            // Ensure integer durations are present (JsonNumberHandling allows strings)
            foreach (var task in wo.Tasks ?? new System.Collections.Generic.List<RepairTask>())
            {
                if (task.EstimatedDurationMinutes < 0) task.EstimatedDurationMinutes = 0;
            }

            // 6. Assign technician if not set: choose most qualified available
            if (string.IsNullOrWhiteSpace(wo.AssignedTo))
            {
                var best = technicians
                    .Where(t => t.IsAvailable)
                    .OrderByDescending(t => t.Skills.Count(s => requiredSkills.Contains(s, StringComparer.OrdinalIgnoreCase)))
                    .ThenBy(t => t.NextAvailableAt ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (best != null)
                {
                    wo.AssignedTo = best.Id;
                }
            }

            // 7. Map partsUsed partNumber -> partId when possible
            if (wo.PartsUsed != null)
            {
                foreach (var pu in wo.PartsUsed)
                {
                    if (!string.IsNullOrWhiteSpace(pu.PartNumber))
                    {
                        var part = partsInventory.FirstOrDefault(p => string.Equals(p.PartNumber, pu.PartNumber, StringComparison.OrdinalIgnoreCase));
                        if (part != null)
                        {
                            pu.PartId = part.Id;
                        }
                    }
                }
            }

            // 8. Save to Cosmos DB
            await cosmosDb.UpsertWorkOrderAsync(wo, ct).ConfigureAwait(false);

            logger.LogInformation("Created WorkOrder {WorkOrderNumber} for fault {FaultId}", wo.WorkOrderNumber, fault.Id);

            return wo;
        }

        private static string ExtractJsonFromAgent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
            var t = text.Trim();

            // If wrapped in triple backticks, extract inside
            var triple = "```";
            if (t.Contains(triple))
            {
                var first = t.IndexOf(triple, StringComparison.Ordinal);
                var last = t.LastIndexOf(triple, StringComparison.Ordinal);
                if (first != -1 && last != -1 && last > first)
                {
                    var inner = t.Substring(first + triple.Length, last - first - triple.Length).Trim();
                    return inner.Trim('`', '\n', '\r', ' ', '\t');
                }
            }

            // If starts with a single backtick or other stray char, remove surrounding backticks
            t = t.Trim('`', '\u200B');

            // Find first JSON object/array start
            var firstBrace = t.IndexOf('{');
            var firstBracket = t.IndexOf('[');
            int start = -1;
            if (firstBrace >= 0 && (firstBracket == -1 || firstBrace < firstBracket)) start = firstBrace;
            else if (firstBracket >= 0) start = firstBracket;

            if (start >= 0)
            {
                // find last matching closing
                var lastBrace = t.LastIndexOf('}');
                var lastBracket = t.LastIndexOf(']');
                int end = Math.Max(lastBrace, lastBracket);
                if (end > start)
                {
                    return t.Substring(start, end - start + 1).Trim();
                }
            }

            // Fallback: return trimmed text
            return t;
        }
        private static string BuildPrompt(DiagnosedFault fault, IReadOnlyList<string> skills, IReadOnlyList<string> parts, System.Collections.Generic.IReadOnlyList<Technician> technicians, System.Collections.Generic.IReadOnlyList<Part> inventory)
        {
            // Keep the prompt concise but include required context; agent instructions are registered separately.
            var techSummary = string.Join("\n", technicians.Select(t => $"- {t.Id}: {t.Name}; skills=[{string.Join(",", t.Skills)}]; available={t.IsAvailable}"));
            var partsSummary = parts.Any() ? string.Join(", ", parts) : "[]";

            return $"DiagnosedFault:\nId: {fault.Id}\nFaultType: {fault.FaultType}\nMachineId: {fault.MachineId}\nSeverity: {fault.Severity}\nConfidence: {fault.Confidence}\n\nRequiredSkills: [{string.Join(", ", skills)}]\nRequiredParts: [{partsSummary}]\n\nAvailableTechnicians:\n{techSummary}\n\nPartsInventoryCount: {inventory.Count}\n\nProduce a JSON WorkOrder matching the schema and rules registered for this agent.";
        }
    }
}
