using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class ProductPolicyCollectionTests
    {
        // === Concurrency: same-template AddPolicy must converge on one group ===

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task AddPolicy_ConcurrentSameTemplate_ProducesSingleGroup()
        {
            const int threadCount = 10;
            const int policiesPerThread = 100;
            const int totalPolicies = threadCount * policiesPerThread;

            var collection = new ProductPolicyCollection();

            var policies = Enumerable.Range(0, totalPolicies)
                .Select(_ => BuildPolicy(template: "shared-template"))
                .ToList();

            // Sanity: every policy must hash to the same _templateToGroup key.
            var template = policies[0].ToString();
            Assert.All(policies, p => Assert.Equal(template, p.ToString()));

            using var barrier = new Barrier(threadCount);
            var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < policiesPerThread; ++i)
                    collection.AddPolicy(policies[t * policiesPerThread + i]);
            })).ToArray();
            await Task.WhenAll(tasks);

            Assert.Single(collection.TemplateToGroup);

            var group = collection.TemplateToGroup[template];
            Assert.NotNull(group);
            Assert.Equal(totalPolicies, group.Policies.Count);

            var expectedIds = policies.Select(p => p.Id).ToHashSet();
            Assert.Equal(expectedIds, group.Policies.Keys.ToHashSet());
        }

        [Fact]
        [Trait("Category", "Concurrency")]
        public async Task AddPolicy_ConcurrentDistinctTemplates_ProducesOneGroupPerTemplate()
        {
            const int threadCount = 8;
            const int policiesPerThread = 25;

            var collection = new ProductPolicyCollection();
            var templates = Enumerable.Range(0, threadCount)
                .Select(i => $"template-{i}")
                .ToList();

            // Pre-compute the deterministic ToString key per template so the
            // assertion below uses the same key the collection will register.
            var expectedKeys = templates.Select(t => BuildPolicy(t).ToString()).ToHashSet();

            using var barrier = new Barrier(threadCount);
            var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int i = 0; i < policiesPerThread; ++i)
                    collection.AddPolicy(BuildPolicy(templates[t]));
            })).ToArray();
            await Task.WhenAll(tasks);

            Assert.Equal(threadCount, collection.TemplateToGroup.Count);
            foreach (var key in expectedKeys)
            {
                Assert.True(collection.TemplateToGroup.ContainsKey(key));
                Assert.Equal(policiesPerThread, collection.TemplateToGroup[key].Policies.Count);
            }
        }


        // === Regression: AddPolicy / RemovePolicy round-trip ===

        [Fact]
        public void AddPolicy_SinglePolicy_RegistersGroupAndPolicy()
        {
            var collection = new ProductPolicyCollection();
            var policy = BuildPolicy("solo-template");

            collection.AddPolicy(policy);

            var (_, group) = Assert.Single(collection.TemplateToGroup);
            Assert.Single(group.Policies);
            Assert.Equal(policy.Id, group.Policies.First().Key);
        }

        [Fact]
        public void AddPolicy_DuplicatePolicyId_DoesNotDoubleAdd()
        {
            var collection = new ProductPolicyCollection();
            var policy = BuildPolicy("dupe-template");

            collection.AddPolicy(policy);
            collection.AddPolicy(policy);

            var (_, group) = Assert.Single(collection.TemplateToGroup);
            Assert.Single(group.Policies);
        }

        [Fact]
        public void ReceivePolicyUpdate_Delete_RemovesPolicyAndClearsEmptyGroup()
        {
            var collection = new ProductPolicyCollection();
            var policy = BuildPolicy("removable-template");

            collection.ReceivePolicyUpdate(ActionType.Add, policy);
            Assert.Single(collection.TemplateToGroup);

            collection.ReceivePolicyUpdate(ActionType.Delete, policy);
            Assert.Empty(collection.TemplateToGroup);
        }

        [Fact]
        public void ReceivePolicyUpdate_Update_ReplacesPolicyInPlace()
        {
            var collection = new ProductPolicyCollection();
            var policy = BuildPolicy("updatable-template");

            collection.ReceivePolicyUpdate(ActionType.Add, policy);
            Assert.Single(collection.TemplateToGroup);
            Assert.Single(collection.TemplateToGroup[policy.ToString()].Policies);

            // Update is implemented as Remove + Add; the empty group is cleared
            // by Remove, then Add recreates it. Net result: one group, one policy.
            collection.ReceivePolicyUpdate(ActionType.Update, policy);

            Assert.Single(collection.TemplateToGroup);
            var (_, currentGroup) = Assert.Single(collection.TemplateToGroup);
            Assert.Single(currentGroup.Policies);
            Assert.Equal(policy.Id, currentGroup.Policies.First().Key);
        }


        // === Helpers ===

        private static Policy BuildPolicy(string template)
        {
            var policy = new IntegerPolicy();
            var update = new PolicyUpdate
            {
                Id = Guid.NewGuid(),
                Template = template,
                Destination = new PolicyDestinationUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.Equal,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "1")),
                ],
            };
            policy.TryUpdate(update, out _, null);
            return policy;
        }
    }
}
