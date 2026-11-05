using System;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensors.SensorModels;
using HSMServer.Dashboards;
using Xunit;

namespace HSMServer.Core.Tests.Dashboards
{
    // #1607 review: a dashboard panel subscription whose path template fails
    // to compile (regex metacharacters in a constant part, e.g. an
    // unterminated '[' set) must match NO sensors. The subscription ignores
    // ApplyNewTemplate's return value, so the converter's post-failure state
    // decides: before the fix it was indistinguishable from "no template"
    // and matched EVERY sensor (the panel filled up to MaxCountOfSources on
    // apply, and every newly registered sensor joined while subscribed).
    public class PanelSubscriptionTests
    {
        [Fact]
        public void MalformedStoredTemplate_MatchesNoSensors()
        {
            // The startup path: a stored subscription entity with a bad
            // template loads through the entity constructor.
            var subscription = BuildSubscription("Root/cpu[");

            Assert.False(subscription.IsMatch(BuildSensor()), "a malformed template must match no sensors");
        }

        [Fact]
        public void UpdateToMalformedTemplate_MatchesNoSensors()
        {
            // The edit path: a working filter replaced by a broken one must
            // stop matching instead of silently becoming match-all.
            var subscription = BuildSubscription("*/cpu");

            Assert.True(subscription.IsMatch(BuildSensor()));

            subscription.Update(new PanelSubscriptionUpdate { PathTemplate = "Root/cpu[" });

            Assert.False(subscription.IsMatch(BuildSensor()), "a broken edit must not degrade into match-all");
        }


        private static PanelSubscription BuildSubscription(string pathTemplate) =>
            new(new PanelSubscriptionEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                PathTemplate = pathTemplate,
                Color = "Red", // the entity ctor parses the color name
                Folders = null, // no folder filter: the template is the only gate
            });

        private static BaseSensorModel BuildSensor()
        {
            var product = new ProductModel("Root");

            var sensor = new IntegerSensorModel(new SensorEntity
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = product.Id.ToString(),
                DisplayName = "cpu",
                Type = (byte)SensorType.Integer,
                CreationDate = DateTime.UtcNow.Ticks,
            }, database: null, provider: null);

            sensor.AddParent(product);

            return sensor;
        }
    }
}
