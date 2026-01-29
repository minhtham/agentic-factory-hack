using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models
{
    public sealed class DiagnosedFault
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("faultType")]
        [JsonProperty("faultType")]
        public string FaultType { get; set; } = string.Empty;

        [JsonPropertyName("machineId")]
        [JsonProperty("machineId")]
        public string MachineId { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        [JsonProperty("severity")]
        public string Severity { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("timestamp")]
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("notes")]
        [JsonProperty("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("metadata")]
        [JsonProperty("metadata")]
        public IDictionary<string, object>? Metadata { get; set; }
    }
}
