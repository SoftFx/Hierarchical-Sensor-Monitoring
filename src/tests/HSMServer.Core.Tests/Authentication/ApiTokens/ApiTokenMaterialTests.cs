using System;
using System.Collections.Generic;
using HSMServer.Authentication;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    public sealed class ApiTokenMaterialTests
    {
        private static string TokenIdA => new('A', ApiTokenMaterial.TokenIdLength);

        private static string SecretA => new('A', ApiTokenMaterial.SecretLength);

        private static string Token(string tokenId, string secret) =>
            ApiTokenMaterial.FormatToken(tokenId, secret);


        [Fact]
        public void Generate_ProducesCanonicalLengthsAndAlphabet()
        {
            var material = ApiTokenMaterial.Generate();

            Assert.Equal(ApiTokenMaterial.TokenIdLength, material.TokenId.Length);
            Assert.Equal(ApiTokenMaterial.SecretLength, material.Secret.Length);
            Assert.Equal(ApiTokenMaterial.TokenIdBytesLength, material.TokenIdBytes.Length);
            Assert.Equal(ApiTokenMaterial.SecretBytesLength, material.SecretBytes.Length);

            foreach (var c in material.TokenId)
                Assert.True(IsBase64UrlChar(c), $"TokenId contains '{c}'");

            foreach (var c in material.Secret)
                Assert.True(IsBase64UrlChar(c), $"Secret contains '{c}'");
        }

        [Fact]
        public void Generate_LargeSampleHasNoDuplicates()
        {
            const int samples = 10_000;

            var tokenIds = new HashSet<string>();
            var secrets = new HashSet<string>();

            for (var i = 0; i < samples; i++)
            {
                var material = ApiTokenMaterial.Generate();

                tokenIds.Add(material.TokenId);
                secrets.Add(material.Secret);
            }

            Assert.Equal(samples, tokenIds.Count);
            Assert.Equal(samples, secrets.Count);
        }

        [Fact]
        public void FormatToken_TryParse_RoundTripsBytesAndVersion()
        {
            var material = ApiTokenMaterial.Generate();

            var token = ApiTokenMaterial.FormatToken(material.TokenId, material.Secret);

            Assert.True(ApiTokenMaterial.TryParse(token, out var versionByte, out var tokenId, out var secret));

            Assert.Equal(ApiTokenMaterial.CurrentVersionByte, versionByte);
            Assert.Equal(material.TokenIdBytes, tokenId);
            Assert.Equal(material.SecretBytes, secret);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryParse_EmptyInput_IsRejected(string token)
        {
            Assert.False(ApiTokenMaterial.TryParse(token, out _, out _, out _));
        }

        [Fact]
        public void TryParse_WrongVersionPrefix_IsRejected()
        {
            var token = $"hsm_pat_v2_{TokenIdA}.{SecretA}";

            Assert.False(ApiTokenMaterial.TryParse(token, out _, out _, out _));
        }

        [Fact]
        public void TryParse_MissingOrDuplicatedSeparator_IsRejected()
        {
            Assert.False(ApiTokenMaterial.TryParse($"hsm_pat_v1_{TokenIdA}", out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse($"hsm_pat_v1_{TokenIdA}.", out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse($"hsm_pat_v1_.{SecretA}", out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse($"hsm_pat_v1_{TokenIdA}..{SecretA}", out _, out _, out _));
        }

        [Fact]
        public void TryParse_WrongPartLengths_IsRejected()
        {
            var shortId = new string('A', ApiTokenMaterial.TokenIdLength - 1);
            var longId = new string('A', ApiTokenMaterial.TokenIdLength + 1);
            var shortSecret = new string('A', ApiTokenMaterial.SecretLength - 1);
            var longSecret = new string('A', ApiTokenMaterial.SecretLength + 1);

            Assert.False(ApiTokenMaterial.TryParse(Token(shortId, SecretA), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(longId, SecretA), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(TokenIdA, shortSecret), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(TokenIdA, longSecret), out _, out _, out _));
        }

        [Fact]
        public void TryParse_PaddingOrForeignAlphabet_IsRejected()
        {
            var paddedId = $"{new string('A', ApiTokenMaterial.TokenIdLength - 1)}=";
            var paddedSecret = $"{new string('A', ApiTokenMaterial.SecretLength - 1)}=";

            var plusId = $"{new string('A', ApiTokenMaterial.TokenIdLength - 1)}+";
            var slashSecret = $"{new string('A', ApiTokenMaterial.SecretLength - 1)}/";
            var spacedSecret = $"{new string('A', ApiTokenMaterial.SecretLength - 1)} ";

            Assert.False(ApiTokenMaterial.TryParse(Token(paddedId, SecretA), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(TokenIdA, paddedSecret), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(plusId, SecretA), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(TokenIdA, slashSecret), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(TokenIdA, spacedSecret), out _, out _, out _));
        }

        [Fact]
        public void TryParse_NonCanonicalAlias_IsRejected()
        {
            // 22 Base64URL chars encode 132 bits but a TokenId is 128 bits: only the top two
            // bits of the last character are significant, so a last character with non-zero
            // trailing bits decodes to 16 bytes that re-encode differently — an alias that
            // must be rejected. 'A' (value 0) is canonical; 'B' (value 1) is not.
            var idAlias = $"{new string('A', ApiTokenMaterial.TokenIdLength - 1)}B";
            var secretAlias = $"{new string('A', ApiTokenMaterial.SecretLength - 1)}B";

            Assert.True(ApiTokenMaterial.TryParse(Token(TokenIdA, SecretA), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(idAlias, SecretA), out _, out _, out _));
            Assert.False(ApiTokenMaterial.TryParse(Token(TokenIdA, secretAlias), out _, out _, out _));
        }

        [Fact]
        public void IsValidTokenId_ChecksShapeAndCanonicalEncoding()
        {
            Assert.True(ApiTokenMaterial.IsValidTokenId(new string('A', ApiTokenMaterial.TokenIdLength)));
            Assert.True(ApiTokenMaterial.IsValidTokenId(ApiTokenMaterial.Generate().TokenId));
            Assert.False(ApiTokenMaterial.IsValidTokenId(new string('A', ApiTokenMaterial.TokenIdLength - 1)));
            Assert.False(ApiTokenMaterial.IsValidTokenId(new string('A', ApiTokenMaterial.TokenIdLength + 1)));
            Assert.False(ApiTokenMaterial.IsValidTokenId($"{new string('A', ApiTokenMaterial.TokenIdLength - 1)}="));
            Assert.False(ApiTokenMaterial.IsValidTokenId($"{new string('A', ApiTokenMaterial.TokenIdLength - 1)}B"));
            Assert.False(ApiTokenMaterial.IsValidTokenId(null));
        }


        private static bool IsBase64UrlChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
    }
}
