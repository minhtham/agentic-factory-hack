using System.Linq;
using RepairPlanner.Services;
using Xunit;

namespace RepairPlanner.Tests
{
    public class FaultMappingServiceTests
    {
        [Fact]
        public void CuringTemperatureExcessive_ReturnsExpectedSkillsAndParts()
        {
            var svc = new FaultMappingService();

            var skills = svc.GetRequiredSkills("curing_temperature_excessive").ToList();
            var parts = svc.GetRequiredParts("curing_temperature_excessive").ToList();

            Assert.Contains("tire_curing_press", skills);
            Assert.Contains("temperature_control", skills);
            Assert.Contains("instrumentation", skills);

            Assert.Contains("TCP-HTR-4KW", parts);
            Assert.Contains("GEN-TS-K400", parts);
        }

        [Fact]
        public void UnknownFault_ReturnsDefaultSkillsAndEmptyParts()
        {
            var svc = new FaultMappingService();

            var skills = svc.GetRequiredSkills("some_unknown_fault");
            var parts = svc.GetRequiredParts("some_unknown_fault");

            Assert.Single(skills);
            Assert.Equal("general_maintenance", skills.First());
            Assert.Empty(parts);
        }
    }
}
