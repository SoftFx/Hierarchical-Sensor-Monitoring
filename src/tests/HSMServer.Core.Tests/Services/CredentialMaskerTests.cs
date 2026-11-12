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
        public void MasksTheCredentialPart(string input, string expected)
        {
            Assert.Equal(expected, CredentialMasker.MaskCredentials(input));
        }

        [Theory]
        [InlineData("https://host/path")]
        [InlineData("mongodb://mongo:27017/db")]
        [InlineData("mongodb://user@mongo/db")]
        [InlineData("http://host:8080/path?email=a@b")] // host:port is not a credential
        [InlineData("plain text, no url at all")]
        public void LeavesCredentialFreeTextAlone(string input)
        {
            Assert.Equal(input, CredentialMasker.MaskCredentials(input));
        }

        [Fact]
        public void NullPassesThrough()
        {
            Assert.Null(CredentialMasker.MaskCredentials(null));
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
    }
}
