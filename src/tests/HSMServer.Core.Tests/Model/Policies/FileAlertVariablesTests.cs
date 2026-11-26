using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Notifications.AddressBook;
using HSMServer.Notifications;
using HSMServer.Core.TableOfChanges;
using Xunit;
using TestSensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.Model.Policies
{
    // The process-wide FileAlertContext is set by these tests: they run one at a time.
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class FileAlertContextCollection
    {
        public const string Name = "File alert context";
    }


    // The alert variables of a File sensor (#1886): $fileName, $fileSize, $link and $fileNote, their place among the older
    // variables, what they print for an ordinary file value, and what a message does with a client's file name.
    [Collection(FileAlertContextCollection.Name)]
    public sealed class FileAlertVariablesTests : IDisposable
    {
        private const string Base = "https://hsm.example.com";


        public FileAlertVariablesTests()
        {
            FileAlertContext.Use(new TestContext(Base, waitsForPassword: true));
        }

        public void Dispose() => FileAlertContext.Use(null);


        [Fact]
        public void NoVariableName_ContainsAnother()
        {
            // A template is substituted by substring: "$sensorLink" would print the sensor name followed by "Link".
            var names = AlertState.VariablesHelp.Keys.ToList();

            foreach (var name in names)
                foreach (var other in names.Where(other => other != name))
                    Assert.False(name.Contains(other, StringComparison.Ordinal), $"{name} contains {other}");
        }

        [Fact]
        public void EveryVariable_PrintsItsOwnProperty()
        {
            // The positions of BuildComment follow the order of the properties: a new variable appended to one and not
            // to the other would shift every variable after it.
            var properties = typeof(AlertState).GetProperties()
                .Where(p => Attribute.IsDefined(p, typeof(AlertVariableAttribute)))
                .ToList();

            var state = new AlertState();

            foreach (var property in properties)
                state[property.Name] = $"<{property.Name}>";

            foreach (var property in properties)
            {
                var variable = ((AlertVariableAttribute)Attribute.GetCustomAttribute(property, typeof(AlertVariableAttribute))).Variable;

                Assert.Equal($"<{property.Name}>", state.BuildComment(AlertState.BuildSystemTemplate(variable).Text));
            }
        }

        [Fact]
        public void TheFileVariables_AreAfterEveryOlderVariable()
        {
            var variables = AlertState.VariablesHelp.Keys.ToList();

            Assert.Equal(["$fileName", "$fileSize", "$link", "$fileNote"], variables.TakeLast(4));
            Assert.Equal("$target", variables[^5]);
        }

        [Fact]
        public void AnOrdinaryFileValue_PrintsItsName_Size_AndPage_WithoutANote()
        {
            var (sensor, policy) = FilePolicy("$fileName|$fileSize|$link|$fileNote|");

            var comment = policy.RebuildState(value: File("report", "csv", 2048));

            Assert.Equal($"report.csv|2.00 KB|{Base}/Home/{sensor.Id}||", comment);
        }

        [Fact]
        public void AFileValue_NeverPrintsItsContent()
        {
            var (_, policy) = FilePolicy("$value");

            Assert.DoesNotContain("AAAA", policy.RebuildState(value: File("a", "txt", 3, content: [65, 65, 65, 65])));
        }

        [Fact]
        public void AClientsFileName_CannotBecomeMarkup_OrBreakTheMessage()
        {
            var (_, policy) = FilePolicy("$fileName");

            var comment = policy.RebuildState(value: File("<markdown>[x](https://evil.example)</markdown>&amp;\nline\u0007two", "txt", 1));

            Assert.DoesNotContain('<', comment);
            Assert.DoesNotContain('>', comment);
            Assert.DoesNotContain('&', comment);
            Assert.DoesNotContain('\n', comment);
            Assert.DoesNotContain('\u0007', comment);
            Assert.DoesNotContain('\u202E', FileAlertVariables.FileName(File("invoice\u202Efdp.exe", "exe", 1)));

            // A format character outside the BMP (a TAG character, a surrogate pair) is one character to replace.
            Assert.Equal("a b.txt", FileAlertVariables.FileName(File("a\U000E0041b", "txt", 1)));
            Assert.Contains("line two", comment);
        }

        [Fact]
        public void ALongFileName_IsCut_SoTheMessageKeepsItsLink()
        {
            var (sensor, policy) = FilePolicy(SensorPolicyCollection.LargeFileAlertTemplate);

            var comment = policy.RebuildState(value: File(new string('d', 1024) + ".dmp", "dmp", 1));
            var name = FileAlertVariables.FileName(File(new string('d', 1024) + ".dmp", "dmp", 1));

            Assert.Equal(FileAlertVariables.MaxFileNameLength, name.Length);
            Assert.EndsWith($"{Base}/Home/{sensor.Id}", comment.TrimEnd());
            Assert.True(TelegramBot.TrimToLimit(comment).Length <= 1000);
            Assert.Contains($"{Base}/Home/{sensor.Id}", TelegramBot.TrimToLimit(comment));
        }

        [Fact]
        public void AGroupOfFileAlerts_IsCutAt1000_AndItsFirstLineKeepsItsLink()
        {
            var (sensor, policy) = FilePolicy(SensorPolicyCollection.LargeFileAlertTemplate);
            var builder = new MessageBuilder();

            for (var i = 0; i < 8; i++)
            {
                policy.RebuildState(value: File($"{new string('x', 150)}-{i}.dmp", "dmp", 1024L * 1024 * (i + 1)));
                builder.AddMessage(new AlertResult(policy));
            }

            var aggregate = builder.GetAggregateMessage();
            var message = TelegramBot.TrimToLimit(aggregate);

            Assert.True(aggregate.Length > 1000);
            var firstLine = message.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];

            // Grouping keeps the files apart (two variables differ); the later lines lose their links to the limit.
            Assert.True(message.Length <= 1000);
            Assert.Contains($"{Base}/Home/{sensor.Id}", firstLine);
        }

        [Fact]
        public void ANonFileSensor_HasNoFileVariables()
        {
            var sensor = TestSensorModelFactory.Build(new SensorEntity { Id = Guid.NewGuid().ToString(), Type = (byte)SensorType.Integer });
            var policy = Policy.BuildPolicy((byte)SensorType.Integer);

            Assert.True(policy.TryUpdate(Update("[$link][$fileName][$fileSize][$fileNote]", sensor), out _, sensor));

            Assert.Equal("[][][][]", policy.RebuildState(value: new IntegerValue { Value = 1, Time = DateTime.UtcNow }));
        }

        [Fact]
        public void EveryWayAFilePolicyIsMade_PrintsTheLink()
        {
            var sensor = FileSensor();
            var link = $"{Base}/Home/{sensor.Id}";

            // Built for a type (templates, change sets, the editor), then updated: the management API and the web UI.
            var updated = Policy.BuildPolicy((byte)SensorType.File);
            Assert.True(updated.TryUpdate(Update("$link", sensor), out _, sensor));

            // Loaded from its stored entity at start.
            var loaded = Policy.BuildPolicy((byte)SensorType.File);
            loaded.Apply(updated.ToEntity(), sensor);

            // Through the sensor's own collection: what a template apply and the policy services call.
            Assert.True(sensor.Policies.TryUpdate([Update("$link", sensor)], InitiatorInfo.System, out _));
            var collected = sensor.Policies.Single();

            // The default alert of a sensor created for large files.
            sensor.Policies.AddLargeFileAlert(InitiatorInfo.System);
            var byDefault = sensor.Policies.Single(p => p.Template == SensorPolicyCollection.LargeFileAlertTemplate);

            foreach (var policy in new[] { updated, loaded, collected, byDefault })
            {
                Assert.IsType<FilePolicy>(policy);
                Assert.Contains(link, policy.RebuildState(value: File("a", "txt", 1)));
            }
        }

        [Fact]
        public void TheDefaultAlert_IsNewData_FromParent_AndJournaled()
        {
            var sensor = FileSensor();
            var journal = new List<string>();
            var uploaded = new List<Policy>();

            sensor.Policies.ChangesHandler += record => journal.Add(record.NewValue);
            sensor.Policies.Uploaded += (_, policy) => uploaded.Add(policy);

            sensor.Policies.AddLargeFileAlert(InitiatorInfo.System);

            var policy = Assert.Single(sensor.Policies);
            var condition = Assert.Single(policy.Conditions);

            Assert.Equal(PolicyProperty.NewSensorData, condition.Property);
            Assert.Equal(PolicyOperation.ReceivedNewValue, condition.Operation);
            Assert.True(policy.Destination.IsFromParentChats);
            Assert.False(policy.IsDisabled);
            Assert.Equal(SensorPolicyCollection.LargeFileAlertTemplate, policy.Template);
            Assert.Same(policy, Assert.Single(uploaded));
            Assert.Single(journal);
        }

        [Fact]
        public void OnlyASecureFileOfAProductWithoutAPassword_Waits_AndAFaultOfTheStoreSaysNothing()
        {
            var productWith = Guid.NewGuid();
            var productWithout = Guid.NewGuid();
            var secure = Guid.NewGuid();
            var notSecure = Guid.NewGuid();
            var broken = Guid.NewGuid();

            var store = new Moq.Mock<HSMDatabase.AccessManager.LargeFiles.ILargeFileStore>();
            store.Setup(s => s.TryGetFile(secure)).Returns(new HSMDatabase.AccessManager.LargeFiles.LargeFileRecord { FileId = secure, Secure = true });
            store.Setup(s => s.TryGetFile(notSecure)).Returns(new HSMDatabase.AccessManager.LargeFiles.LargeFileRecord { FileId = notSecure, Secure = false });
            store.Setup(s => s.TryGetFile(broken)).Throws(new HSMDatabase.AccessManager.LargeFiles.LargeFileStoreException("disk gone"));

            var passwords = new Moq.Mock<HSMServer.Core.LargeFiles.DumpPasswords.IDumpPasswordService>();
            passwords.Setup(p => p.HasPassword(productWith)).Returns(true);

            bool Waits(Guid product, Guid file) => HSMServer.Model.FileSensors.ServerFileAlertContext.WaitsForPassword(store.Object, passwords.Object, product, file);

            Assert.True(Waits(productWithout, secure));
            Assert.False(Waits(productWith, secure));
            Assert.False(Waits(productWithout, notSecure));
            Assert.True(Waits(productWithout, Guid.NewGuid()));
            Assert.False(Waits(productWithout, broken));
        }

        [Fact]
        public void LinkBase_IsTheDomain_ThenTheAgentsUrl_ThenThePathAlone()
        {
            var agent = new HSMServer.ServerConfiguration.AgentConfig();
            var config = new Moq.Mock<HSMServer.ServerConfiguration.IServerConfig>();
            var id = Guid.NewGuid();

            config.Setup(c => c.Agent).Returns(agent);

            agent.ExternalConnectionUrl = "agents.example.com:8443";

            Assert.Equal($"https://hsm.example.com/Home/{id}", Context(config.Object, "hsm.example.com").SensorPageLink(id));
            Assert.Equal($"https://agents.example.com:8443/Home/{id}", Context(config.Object, "").SensorPageLink(id));
            Assert.Equal($"https://[::1]/Home/{id}", Context(config.Object, "::1").SensorPageLink(id));

            agent.ExternalConnectionUrl = "";

            Assert.Equal($"/Home/{id}", Context(config.Object, null).SensorPageLink(id));
        }


        private static HSMServer.Model.FileSensors.ServerFileAlertContext Context(HSMServer.ServerConfiguration.IServerConfig config, string domain) =>
            new(config, (_, _) => false, () => domain);

        private static (BaseSensorModel Sensor, Policy Policy) FilePolicy(string template)
        {
            var sensor = FileSensor();
            var policy = Policy.BuildPolicy((byte)SensorType.File);

            Assert.True(policy.TryUpdate(Update(template, sensor), out var error, sensor), error);

            return (sensor, policy);
        }

        private static BaseSensorModel FileSensor()
        {
            var product = new ProductModel(HSMServer.Core.Tests.Infrastructure.EntitiesFactory.BuildProductEntity(name: "files") with { Id = Guid.NewGuid().ToString() });
            var sensor = TestSensorModelFactory.Build(new SensorEntity { Id = Guid.NewGuid().ToString(), Type = (byte)SensorType.File, DisplayName = "dumps" });

            product.AddSensor(sensor);

            return sensor;
        }

        private static PolicyUpdate Update(string template, BaseSensorModel sensor) => new()
        {
            Id = Guid.NewGuid(),
            Status = SensorStatus.Ok,
            Template = template,
            Destination = new PolicyDestinationUpdate(PolicyDestinationMode.FromParent),
            Conditions =
            [
                new PolicyConditionUpdate(PolicyOperation.ReceivedNewValue, PolicyProperty.NewSensorData,
                    new TargetValue(TargetType.LastValue, sensor.Id.ToString())),
            ],
        };

        private static FileValue File(string name, string extension, long size, byte[] content = null) => new()
        {
            Name = name,
            Extension = extension,
            OriginalSize = size,
            Value = content ?? [],
            Time = DateTime.UtcNow,
            Status = SensorStatus.Ok,
        };


        private sealed class TestContext(string baseAddress, bool waitsForPassword) : IFileAlertContext
        {
            public string SensorPageLink(Guid sensorId) => $"{baseAddress}{FileAlertContext.SensorPagePath(sensorId)}";

            public bool WaitsForDumpPassword(Guid productId, Guid fileId) => waitsForPassword;
        }
    }
}
