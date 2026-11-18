using System;
using System.Diagnostics;
using System.Linq;
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
        // #1709 review: the host after the credential is NOT parsed, so
        // replica-set host lists, bracketed IPv6 literals and URLs embedded
        // in JSON, parentheses or trailing punctuation (exception text in a
        // comment) all mask.
        [InlineData("mongodb://u:p@h1:27017,h2:27017/db", "mongodb://***@h1:27017,h2:27017/db")]
        [InlineData("postgres://u:p@[::1]:5432/db", "postgres://***@[::1]:5432/db")]
        [InlineData("Failed to connect mongodb://u:p@mongo:27017.", "Failed to connect mongodb://***@mongo:27017.")]
        [InlineData("{\"c\":\"mongodb://u:p@mongo:27017\"}", "{\"c\":\"mongodb://***@mongo:27017\"}")]
        [InlineData("(mongodb://u:p@mongo:27017)", "(mongodb://***@mongo:27017)")]
        // two credentials in ONE word, separated by a path delimiter
        [InlineData("a://u:p@h/x://y:z@w", "a://***@h/x://***@w")]
        // #1709 review: a password with BOTH a raw '@' and a raw '/' is
        // only PARTLY masked — the separator is the last '@' of the
        // AUTHORITY, so the tail past the '/' leaks. Preferring the zone's
        // last '@' here would also grab the '@' of a query-string email
        // (mongodb://u:p@h/db?email=a@b) and destroy a credential-free
        // value; a documented limit (wire contract).
        [InlineData("mongodb://u:p@ss/x@host/db", "mongodb://***@ss/x@host/db")]
        // #1709 review: an @-free zone before a boundary proves nothing
        // inside it can mask — the scan resumes AT the boundary, so a later
        // well-formed credential in the SAME word still masks.
        [InlineData("{\"a\":\"x://u:p/q\",\"b\":\"mongodb://u:p@h/db\"}", "{\"a\":\"x://u:p/q\",\"b\":\"mongodb://***@h/db\"}")]
        [InlineData("see http://a:b/c,mongodb://u:p@h/db", "see http://a:b/c,mongodb://***@h/db")]
        public void MasksTheCredentialPart(string input, string expected)
        {
            Assert.Equal(expected, CredentialMasker.MaskCredentials(input));
        }

        [Theory]
        [InlineData("https://host/path")]
        [InlineData("mongodb://mongo:27017/db")]
        [InlineData("mongodb://user@mongo/db")]
        [InlineData("mongodb://user:@mongo/db")] // empty password — nothing to leak
        [InlineData("http://host:8080/path?email=a@b")] // host:port is not a credential — an '@' in the path/query never masks
        [InlineData("http://host:8080/a@b/c")]
        // #1709 review: RFC 3986 defines 'port = *DIGIT' — an EMPTY port
        // is still host:port, so the '@' after the path never masks.
        [InlineData("http://example.com:/a@b")]
        // #1709 review: the raw-password fallback only fires for a
        // userinfo-shaped authority — empty, bracketed and host:port
        // authorities keep their later '@'s, which live in the path.
        [InlineData("http://[::1]/a:b@c")]
        [InlineData("file:///C:/x@y")]
        // #1709 review: an all-digit password prefix before a raw '/' reads
        // as host:port — indistinguishable from a real scheme://host:port/
        // ...@path URL (http://host:8080/a@b above), so this ONE obfuscated-
        // password shape stays unmasked (documented in the wire contract).
        [InlineData("mongodb://backup:1234/56@mongo/db")]
        // #1709 review: the authority ends at characters RFC 3986 excludes
        // from it, so a URL without a path does not own the rest of its
        // word — the JSON tail / the email after the comma is NOT a
        // credential, and these values store verbatim (no data loss).
        [InlineData("{\"url\":\"http://example.com\",\"owner\":\"admin@corp.com\"}")]
        [InlineData("{\"conn\":\"mongodb://h1:27017,h2:27017/db\",\"owner\":\"a@b.com\"}")]
        [InlineData("https://example.com,contact:ops@corp.com")]
        // #1709 review: a raw RFC-illegal character (';', ',', '"', ...) in
        // an UNENCODED password also ends the authority/credential zone, so
        // these password shapes stay unmasked — a documented limit: treating
        // them as password text would read the credential-free JSON tails
        // pinned above ('{"x":"http://a:b/c","o":"d@e.com"}'-shaped values)
        // as credentials and destroy them. '&' does NOT share the limit: it
        // always travels entity-encoded, and entities are stepped over.
        [InlineData("mongodb://u:p;ss@host/db")]
        [InlineData("mongodb://u:p,ss@host/db")]
        [InlineData("mongodb://u:p\"x@h/db")]
        [InlineData("plain text, no url at all")]
        public void LeavesCredentialFreeTextAlone(string input)
        {
            Assert.Equal(input, CredentialMasker.MaskCredentials(input));
        }

        // #1709 review: every value and comment rides the masker on the
        // request thread — the scan must stay linear. Before the rewrite a
        // 200 KB battery of "a://" took 32 s in ONE MaskCredentials call;
        // the bound below is generous (linear is milliseconds here, a
        // quadratic scan is minutes at these sizes).
        [Fact]
        public void AdversarialLongInput_StaysLinear()
        {
            const string filler = "x://h:80/"; // port-shaped authority: full work per "://", never masks
            const string fallbackFiller = "a://q:x/"; // userinfo-shaped: the raw-password fallback fires per "://"
            var noAtBattery = string.Concat(Enumerable.Repeat("a://", 250_000)); // 1 MB word without '@'
            var portBattery = string.Concat(Enumerable.Repeat(filler, 60_000)) + "@z mongodb://u:p@mongo:27017/db";
            // the ONLY '@' sits before the first "://": every fallback scan
            // runs to the end of the word and finds nothing — without the
            // break it is one full-tail rescan per "://" (quadratic).
            var atFrontBattery = "@" + string.Concat(Enumerable.Repeat(fallbackFiller, 60_000));
            // same, but one ';' terminates the word: the first zone scan
            // crosses the whole tail, and linearity then depends on the scan
            // resuming AT the boundary — resuming at schemeAt+1 instead
            // rescans the tail once per "://" (quadratic).
            var inWordBattery = "@" + string.Concat(Enumerable.Repeat("a://q:x/", 60_000)) + ";";

            var stopwatch = Stopwatch.StartNew();
            var noAtResult = CredentialMasker.MaskCredentials(noAtBattery);
            var portResult = CredentialMasker.MaskCredentials(portBattery);
            var atFrontResult = CredentialMasker.MaskCredentials(atFrontBattery);
            var inWordResult = CredentialMasker.MaskCredentials(inWordBattery);
            stopwatch.Stop();

            Assert.Equal(noAtBattery, noAtResult);
            Assert.Equal(string.Concat(Enumerable.Repeat(filler, 60_000)) + "@z mongodb://***@mongo:27017/db", portResult);
            Assert.Equal(atFrontBattery, atFrontResult);
            Assert.Equal(inWordBattery, inWordResult);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"masking the batteries took {stopwatch.Elapsed}");
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
            // pass the SanitizeText gate — mask, sanitize, mask again — so
            // the secret never reaches storage even when it travels inside
            // a comment.
            var converted = new HSMSensorDataObjects.SensorValueRequests.StringSensorValue
            {
                Path = "Scheduled Tasks/Mongo Backup Task",
                Value = "mongodb://backup:secret@mongo:27017/db",
                Comment = "conn: mongodb://backup:secret@mongo:27017/db",
            }.Convert();

            Assert.Equal("mongodb://***@mongo:27017/db", converted.Value);
            Assert.Equal("conn: mongodb://***@mongo:27017/db", converted.Comment);
        }

        // #1709 review: the gate masks on BOTH sides of the sanitizer. The
        // PRE-sanitize pass catches a raw '<' in the password: the
        // sanitizer reads '<ss@…' as an unclosed tag and DROPS everything
        // after it, so with the old sanitize-then-mask order the value
        // below was stored as 'mongodb://u:p' — the user and the password
        // prefix in clear. The POST-sanitize pass catches the shapes the
        // sanitizer rewrites into form — an entity-decoded '@' or a tag
        // splitting the URL from its '@'. The '&' case pins the entity
        // handling: the sanitizer RE-ENCODES '&' as '&amp;', and the masker
        // steps over the entity instead of ending the credential at its ';'.
        [Theory]
        [InlineData("mongodb://backup:secret&#64;mongo:27017/db", "mongodb://***@mongo:27017/db")]
        [InlineData("mongodb://backup:secret<i class=\"x\"></i>@mongo/db", "mongodb://***@mongo/db")]
        [InlineData("mongodb://u:p&ss@host/db", "mongodb://***@host/db")]
        // unclosed '<tag' shape — only the PRE-sanitize pass can mask it
        [InlineData("mongodb://u:p<ss@host/db", "mongodb://***@host/db")]
        // closed tag: the PRE pass cannot cross '>', the sanitizer strips
        // the tag and the POST pass masks the joined text
        [InlineData("mongodb://u:p<ss>t@host/db", "mongodb://***@host/db")]
        public void SanitizerRewrittenUrls_MaskAtIngestion(string raw, string expected)
        {
            var converted = new HSMSensorDataObjects.SensorValueRequests.StringSensorValue
            {
                Path = "Scheduled Tasks/Mongo Backup Task",
                Value = raw,
            }.Convert();

            Assert.Equal(expected, converted.Value);
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
