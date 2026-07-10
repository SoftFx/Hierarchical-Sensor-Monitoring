using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.DataAlerts;
using HSMServer.Model.DataAlertTemplates;
using Xunit;

namespace HSMServer.Core.Tests.DataAlertTemplateViewModelTests
{
    // Regression coverage for #1246: When an Alert Template has two TTL alerts and one is demoted
    // to a regular condition client-side, the form posts one row under DataAlerts[sensorType] and
    // the other under DataAlerts[255]. ToModel must persist both: the demoted row as a regular
    // Policy, the untouched row as a TTLPolicy. Locks the server-side contract so a future
    // refactor can't regress it; the client-side race that fed it bad data is fixed in submitForm.
    public class DataAlertTemplateViewModelMultiRowDemoteTests
    {
        [Fact]
        public void ToModel_DemoteOneOfTwoTtl_PersistsDemotedAsRegularAndOtherAsTtl()
        {
            // Template configured for Integer sensors with two TTL alerts loaded from storage.
            var ttlId1 = Guid.NewGuid();
            var ttlId2 = Guid.NewGuid();
            var templateId = Guid.NewGuid();

            var ttl1 = BuildTtl(ttlId1);
            var ttl2 = BuildTtl(ttlId2);

            var model = new AlertTemplateModel
            {
                Id = templateId,
                Name = "two-ttl-template",
                SensorType = (byte)SensorType.Integer,
                Paths = ["*/intSensor"],
                TtlEntries =
                [
                    new TtlEntry(ttl1, TimeIntervalModel.None),
                    new TtlEntry(ttl2, TimeIntervalModel.None),
                ],
            };

            var vm = new DataAlertTemplateViewModel(model);

            Assert.Single(vm.DataAlerts);
            Assert.Equal(TimeToLiveAlertViewModel.AlertKey, vm.DataAlerts.First().Key);
            Assert.Equal(2, vm.DataAlerts.First().Value.Count);

            // Simulate the form posts that the JS produces after the user demotes alert #1 to a
            // regular Integer condition. The row's data-alert-type flips 255 -> (byte)Integer, so
            // collectAlerts routes it under DataAlerts[4]. The unchanged TTL row stays under 255.
            var demoted = vm.DataAlerts[TimeToLiveAlertViewModel.AlertKey][0];
            var unchangedTtl = vm.DataAlerts[TimeToLiveAlertViewModel.AlertKey][1];

            // Force a non-empty Id on the demoted row so ToModel can route it through
            // Policy.BuildPolicy without inventing a fresh Guid (lets us assert equality below).
            demoted.Id = ttlId1;

            var rebuilt = new DataAlertTemplateViewModel
            {
                Id = templateId,
                Name = "two-ttl-template",
                Type = (byte)SensorType.Integer,
                PathTemplates = ["*/intSensor"],
                DataAlerts = new Dictionary<byte, List<DataAlertViewModelBase>>
                {
                    [(byte)SensorType.Integer] = new() { demoted },
                    [TimeToLiveAlertViewModel.AlertKey] = new() { unchangedTtl },
                },
            };

            var result = rebuilt.ToModel(null);

            // The demoted row is persisted as a regular Integer Policy...
            Assert.Single(result.Policies);
            Assert.Equal(ttlId1, result.Policies[0].Id);

            // ...and the unchanged row is still a TTL.
            Assert.Single(result.TtlEntries);
            Assert.Equal(ttlId2, result.TtlEntries[0].Policy.Id);
        }


        private static TTLPolicy BuildTtl(Guid id)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(TimeIntervalModel.None);

            // Construct via the (setting, entity) ctor so we can stamp a specific Id through the
            // PolicyEntity — TTLPolicy's parameterless ctor generates a new Guid and FullUpdate
            // does not overwrite it from PolicyUpdate.Id (the storage path uses TryUpdate).
            var entity = new PolicyEntity
            {
                Id = id.ToByteArray(),
                TTL = TimeIntervalModel.None.Ticks,
                Destination = new PolicyDestinationEntity { UseDefaultChats = true },
            };
            return new TTLPolicy(ttlSetting, entity);
        }
    }
}
