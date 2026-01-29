using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services
{
    public sealed class CosmosDbService : IAsyncDisposable
    {
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly Microsoft.Extensions.Logging.ILogger<CosmosDbService>? _logger;

        // Containers and partition key names (per spec)
        private const string TechniciansContainer = "Technicians"; // partition key: department
        private const string PartsContainer = "PartsInventory"; // partition key: category
        private const string WorkOrdersContainer = "WorkOrders"; // partition key: status

        public CosmosDbService(CosmosDbOptions options, Microsoft.Extensions.Logging.ILogger<CosmosDbService>? logger = null)
        {
            _logger = logger;
            // Create CosmosClient; in real apps use DefaultAzureCredential or DI
            _client = new CosmosClient(options.Endpoint, options.Key);
            _database = _client.CreateDatabaseIfNotExistsAsync(options.DatabaseName).GetAwaiter().GetResult();

            // Ensure containers exist (idempotent)
            _database.CreateContainerIfNotExistsAsync(TechniciansContainer, "/department").GetAwaiter().GetResult();
            _database.CreateContainerIfNotExistsAsync(PartsContainer, "/category").GetAwaiter().GetResult();
            _database.CreateContainerIfNotExistsAsync(WorkOrdersContainer, "/status").GetAwaiter().GetResult();
        }

        public async Task<IReadOnlyList<Technician>> GetAllTechniciansAsync(CancellationToken ct = default)
        {
            var container = _database.GetContainer(TechniciansContainer);
            var query = new QueryDefinition("SELECT * FROM c");
            var it = container.GetItemQueryIterator<Technician>(query);
            var results = new List<Technician>();
            while (it.HasMoreResults)
            {
                var page = await it.ReadNextAsync(ct).ConfigureAwait(false);
                results.AddRange(page.Resource);
            }

            _logger?.LogInformation("Fetched {Count} technicians from CosmosDb", results.Count);
            return results;
        }

        public async Task<IReadOnlyList<Part>> GetAllPartsAsync(CancellationToken ct = default)
        {
            var container = _database.GetContainer(PartsContainer);
            var query = new QueryDefinition("SELECT * FROM c");
            var it = container.GetItemQueryIterator<Part>(query);
            var results = new List<Part>();
            while (it.HasMoreResults)
            {
                var page = await it.ReadNextAsync(ct).ConfigureAwait(false);
                results.AddRange(page.Resource);
            }

            _logger?.LogInformation("Fetched {Count} parts from CosmosDb", results.Count);
            return results;
        }

        public async Task<IReadOnlyList<Technician>> QueryTechniciansBySkillsAsync(IEnumerable<string> skills, CancellationToken ct = default)
        {
            var container = _database.GetContainer(TechniciansContainer);
            // Simple query: return technicians where any skill IN skills array.
            // Use parameterized IN clause
            var skillsList = skills?.ToList() ?? new List<string>();
            if (!skillsList.Any())
                return Array.Empty<Technician>();

            var inClause = string.Join(",", skillsList.Select((s, i) => $"@s{i}"));
            var sql = $"SELECT * FROM c WHERE ARRAY_CONTAINS(@skills, c.skill) = false OR CONTAINS(c.skills, @skills[0])";
            // Simpler approach: read all and filter in-memory to avoid complex SQL here
            var all = await GetAllTechniciansAsync(ct).ConfigureAwait(false);
            var filtered = all.Where(t => t.Skills.Any(s => skillsList.Contains(s, StringComparer.OrdinalIgnoreCase))).ToList();
            _logger?.LogInformation("Found {Count} available technicians matching skills", filtered.Count);
            return filtered;
        }

        public async Task<Part?> GetPartByPartNumberAsync(string partNumber, CancellationToken ct = default)
        {
            var container = _database.GetContainer(PartsContainer);
            var query = new QueryDefinition("SELECT * FROM c WHERE c.partNumber = @pn").WithParameter("@pn", partNumber);
            var it = container.GetItemQueryIterator<Part>(query);
            while (it.HasMoreResults)
            {
                var page = await it.ReadNextAsync(ct).ConfigureAwait(false);
                var found = page.Resource.FirstOrDefault();
                if (found != null) return found;
            }

            return null;
        }

        public async Task UpsertWorkOrderAsync(WorkOrder wo, CancellationToken ct = default)
        {
            var container = _database.GetContainer(WorkOrdersContainer);
            wo.UpdatedAt = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(wo.Id)) wo.Id = Guid.NewGuid().ToString();
            await container.UpsertItemAsync(wo, new PartitionKey(wo.Status), cancellationToken: ct).ConfigureAwait(false);
            _logger?.LogInformation("Saved work order {WorkOrderNumber} (id={Id}, status={Status}, assignedTo={AssignedTo})", wo.WorkOrderNumber, wo.Id, wo.Status, wo.AssignedTo);
        }

        public async Task<bool> TryDeductPartQuantityAsync(string partNumber, int quantity, CancellationToken ct = default)
        {
            var container = _database.GetContainer(PartsContainer);
            var part = await GetPartByPartNumberAsync(partNumber, ct).ConfigureAwait(false);
            if (part == null) return false;

            if (part.QuantityAvailable < quantity) return false;

            // Read item to get ETag for optimistic concurrency
            try
            {
                var response = await container.ReadItemAsync<Part>(part.Id, new PartitionKey(part.Category ?? ""), cancellationToken: ct).ConfigureAwait(false);
                var current = response.Resource;
                current.QuantityAvailable -= quantity;
                var requestOptions = new ItemRequestOptions { IfMatchEtag = response.ETag };
                await container.ReplaceItemAsync(current, current.Id, new PartitionKey(current.Category ?? ""), requestOptions, ct).ConfigureAwait(false);
                return true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                // ETag mismatch; caller may retry
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            await Task.CompletedTask;
        }
    }
}
