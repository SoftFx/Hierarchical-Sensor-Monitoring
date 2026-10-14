using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // #1565: a template whose path pattern matches NO sensors is a legal save
    // (the template may precede its sensors — the #1210 validation deliberately
    // allows zero-match paths), but before #1565 the save answered clean
    // success and the dead pattern surfaced only when someone re-checked the
    // UI. Pinned here against the real cache wiring:
    //  - the save answers SUCCESS with one warning per dead pattern (the
    //    SensorPath slot carries the PATTERN itself — there is no sensor to
    //    name); the #1401 invariant holds, warnings never flip Success;
    //  - patterns that DO match produce no zero-match noise, and their sensors
    //    still get the policies — a dead sibling pattern does not poison the
    //    apply of the live one.
    [Collection("Database collection")]
    public class TemplatePathZeroMatchWarningTests : TemplateFailureTestsBase
    {
        private readonly TemplateConcurrencyFixture _fixture;


        public TemplatePathZeroMatchWarningTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _fixture = fixture;
        }


        private async Task CreateSensor(string path, SensorType type)
        {
            var value = SensorValuesFactory.BuildSensorValue(type, path, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            await Task.Delay(300);
        }

        // A concrete-type template matching the sensor it should reach: an
        // Integer template with a Value condition an Integer sensor accepts, so
        // the ONLY warnings a save can produce here are the zero-match ones.
        private AlertTemplateModel BuildIntegerTemplate(List<string> paths, Guid policyId)
        {
            var policy = Policy.BuildPolicy((byte)SensorType.Integer);

            policy.UpdatePolicy(new PolicyUpdate { Id = policyId, ConfirmationPeriod = 0 });

            policy.Conditions.Add(new PolicyIntegerCondition<IntegerValue>
            {
                Operation = PolicyOperation.GreaterThan,
                Property = PolicyProperty.Value,
                Target = new TargetValue(TargetType.Const, "0"),
            });

            var template = new AlertTemplateModel
            {
                Name = $"Zero-match template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)SensorType.Integer,
                Paths = paths,
                Policies = [policy],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task AddTemplate_DeadPathPattern_WarnsPerDeadPattern_StillAppliesLivePaths()
        {
            // One live path and two dead ones, each dead for a different
            // documented reason (#1565 problem statement): no '/' at all — a
            // pattern without a segment separator can never match because every
            // sensor path has at least Product/SensorName — and a '**'
            // any-depth intuition, which is two single-segment classes with the
            // depth still fixed by the segment count.
            const string livePath = "zeroMatchLive";
            const string noSlashPattern = "*Total CPU*";
            const string anyDepthPattern = "*/**/zeroMatchNoDepth";

            await CreateSensor(livePath, SensorType.Integer);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, livePath, out var liveSensor));

            var template = BuildIntegerTemplate([$"*/{livePath}", noSlashPattern, anyDepthPattern], Guid.NewGuid());

            var (success, error, warnings) = await _valuesCache.AddAlertTemplateAsync(template);

            // The save PERSISTED — a dead pattern is not a failure (sensors may
            // come later); warnings never flip Success.
            Assert.True(success, error);

            // Exactly the two dead patterns, nothing else: the Integer sensor
            // accepts the condition, so no per-policy rejection rides along and
            // the live path produces no zero-match noise.
            Assert.Equal(2, warnings.Count);
            Assert.All(warnings, w => Assert.Contains("matched no sensors", w.Reason));
            Assert.Contains(warnings, w => w.SensorPath == noSlashPattern);
            Assert.Contains(warnings, w => w.SensorPath == anyDepthPattern);

            // The live path is real, not just un-warned: its sensor got the
            // template's policy.
            Assert.Contains(liveSensor.Policies, p => p.TemplateId == template.Id);
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task AddTemplate_AllPathsMatch_NoZeroMatchNoise()
        {
            // The clean boundary: when every pattern matches, the warnings list
            // stays EMPTY — a warning-free save keeps its pre-warnings byte
            // shape on every transport (#1401), so the zero-match diagnostic
            // must not fire on healthy templates.
            const string livePath = "zeroMatchClean";

            await CreateSensor(livePath, SensorType.Integer);

            // Two live patterns: the wildcard form and the product-name-anchored
            // form, both resolving to the same sensor — the fixture DB is shared
            // across suites, so the patterns stay scoped to THIS test's sensor.
            var template = BuildIntegerTemplate([$"*/{livePath}", $"ProductA_concurrency/{livePath}"], Guid.NewGuid());

            var (success, error, warnings) = await _valuesCache.AddAlertTemplateAsync(template);

            Assert.True(success, error);
            Assert.Empty(warnings);
        }
    }
}
