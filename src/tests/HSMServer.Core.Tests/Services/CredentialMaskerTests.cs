using HSMServer.Core.ApiObjectsConverters;
using HSMServer.Core.Services;
using Xunit;

namespace HSMServer.Core.Tests.Services
{
    // #1709: a credential-bearing connection string must never reach
    // storage; the scheme and the host stay readable for monitoring.
    public class CredentialMaskerTests
    {
        [Theory]
        [InlineData("mongodb://backup:secret@mongo:27017/db", "mongodb://***@mongo:27017/db")]
        // A percent-encoded secret ('/' and '@' travel as %2F/%40) is masked whole.
        [InlineData("mongodb://backup:p%2Fs%40s@mongo:27017/db", "mongodb://***@mongo:27017/db")]
        [InlineData("postgresql://u:p@db.internal:5432/x?ssl=true", "postgresql://***@db.internal:5432/x?ssl=true")]
        // An EMPTY user is a real shape (redis://:pass@host).
        [InlineData("redis://:pass@redis.internal:6379/0", "redis://***@redis.internal:6379/0")]
        [InlineData("pre https://a:b@c.d post mongodb://x:y@z end", "pre https://***@c.d post mongodb://***@z end")]
        // #1709 review: producers that build connection strings by plain
        // concatenation do not percent-encode the password. A raw '@' in the
        // password — the separator is the LAST '@' before the path, so no
        // password tail leaks; raw '/', '?' and '#' in the password are
        // covered by the not-a-host:port fallback.
        [InlineData("mongodb://backup:p@ss@mongo:27017/db", "mongodb://***@mongo:27017/db")]
        [InlineData("mongodb://backup:pa/ss@mongo/db", "mongodb://***@mongo/db")]
        [InlineData("mongodb://backup:pa?ss@mongo/db", "mongodb://***@mongo/db")]
        [InlineData("mongodb://backup:pa#ss@mongo/db", "mongodb://***@mongo/db")]
        public void MasksTheCredentialPart(string input, string expected)
        {
            Assert.Equal(expected, CredentialMasker.MaskCredentials(input));
        }

        [Theory]
        [InlineData("https://host/path")]
        [InlineData("mongodb://mongo:27017/db")]
        [InlineData("mongodb://user@mongo/db")]
        [InlineData("http://host:8080/path?email=a@b")] // host:port is not a credential — an '@' in the path/query never masks
        [InlineData("http://host:8080/a@b/c")]
        [InlineData("plain text, no url at all")]
        public void LeavesCredentialFreeTextAlone(string input)
        {
            Assert.Equal(input, CredentialMasker.MaskCredentials(input));
        }

        [Fact]
        public void NullPassesThrough()
        {
            Assert.Null(CredentialMasker.MaskCredentials(null));
            Assert.Equal(string.Empty, CredentialMasker.MaskCredentials(string.Empty));
        }

        [Fact]
        public void StringSensorValues_MaskAtIngestion()
        {
            // The converter both ingest paths ride: the value AND the comment
            // are masked before the sanitizer, so the secret never reaches
            // storage even when it travels inside a comment.
            var converted = new HSMSensorDataObjects.SensorValueRequests.StringSensorValue
            {
                Path = "Scheduled Tasks/Mongo Backup Task",
                Value = "mongodb://backup:secret@mongo:27017/db",
                Comment = "conn: mongodb://backup:secret@mongo:27017/db",
            }.Convert();

            Assert.Equal("mongodb://***@mongo:27017/db", converted.Value);
            Assert.Equal("conn: mongodb://***@mongo:27017/db", converted.Comment);
        }

        // #1709 review: comments are the field most likely to carry an
        // exception message or a connection string — the mask must not be a
        // string-sensor-only rule.
        [Fact]
        public void NonStringSensorComments_MaskAtIngestion()
        {
            var converted = new HSMSensorDataObjects.SensorValueRequests.IntSensorValue
            {
                Path = "Scheduled Tasks/Backup Result",
                Value = 1,
                Comment = "conn: mongodb://backup:secret@mongo:27017/db",
            }.Convert();

            Assert.Equal("conn: mongodb://***@mongo:27017/db", converted.Comment);
        }

        // #1709 review: the HTTP-side twin of the Core converter (the
        // /api/sensors/* typed routes) is a hand-maintained copy — pin it so
        // the two cannot drift apart. Called statically: the DTO types are
        // the shared ones, only the converter class differs.
        [Fact]
        public void TheHttpConverterTwin_MasksValueAndCommentOfEveryType()
        {
            var converted = HSMServer.ApiObjectsConverters.ApiConverters.Convert(
                new HSMSensorDataObjects.SensorValueRequests.StringSensorValue
                {
                    Path = "twin/string",
                    Value = "mongodb://backup:secret@mongo:27017/db",
                    Comment = "conn: mongodb://backup:secret@mongo:27017/db",
                });

            Assert.Equal("mongodb://***@mongo:27017/db", converted.Value);
            Assert.Equal("conn: mongodb://***@mongo:27017/db", converted.Comment);

            var intConverted = HSMServer.ApiObjectsConverters.ApiConverters.Convert(
                new HSMSensorDataObjects.SensorValueRequests.IntSensorValue
                {
                    Path = "twin/int",
                    Value = 1,
                    Comment = "mongodb://backup:secret@mongo:27017/db",
                });

            Assert.Equal("mongodb://***@mongo:27017/db", intConverted.Comment);
        }
    }
}
