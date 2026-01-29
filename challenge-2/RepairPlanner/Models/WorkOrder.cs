using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models
{
    // Models use both System.Text.Json and Newtonsoft.Json attributes
    // for Cosmos DB compatibility.
    public sealed class WorkOrder
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("workOrderNumber")]
        [JsonProperty("workOrderNumber")]
        public string WorkOrderNumber { get; set; } = string.Empty;

        [JsonPropertyName("machineId")]
        [JsonProperty("machineId")]
        public string MachineId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonPropertyName("type")]
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty; // corrective | preventive | emergency

        [JsonPropertyName("priority")]
        [JsonProperty("priority")]
        public string Priority { get; set; } = "medium"; // critical | high | medium | low

        [JsonPropertyName("status")]
        [JsonProperty("status")]
        public string Status { get; set; } = "open";

        [JsonPropertyName("assignedTo")]
        [JsonProperty("assignedTo")]
        public string? AssignedTo { get; set; } // technician id or null

        [JsonPropertyName("notes")]
        [JsonProperty("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("estimatedDuration")]
        [JsonProperty("estimatedDuration")]
        public int EstimatedDuration { get; set; } // minutes

        [JsonPropertyName("partsUsed")]
        [JsonProperty("partsUsed")]
        public List<WorkOrderPartUsage> PartsUsed { get; set; } = new();

        [JsonPropertyName("tasks")]
        [JsonProperty("tasks")]
        public List<RepairTask> Tasks { get; set; } = new();

        [JsonPropertyName("createdAt")]
        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("updatedAt")]
        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
