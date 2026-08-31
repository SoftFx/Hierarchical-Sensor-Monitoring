using System;
using System.Security.Cryptography;
using System.Text;
using HSMServer.Authentication;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    public sealed class ApiTokenVerifierTests
    {
        [Fact]
        public void ComputeVerifier_MatchesPinnedDomainSeparatedFormula()
        {
            var material = ApiTokenMaterial.Generate();

            // Independent re-implementation of the pinned contract:
            // SHA-256(ASCII("HSM-API-TOKEN") || 0x00 || versionByte || tokenId[16] || secret[32])
            var expectedInput = new byte[13 + 1 + 1 + 16 + 32];
            Encoding.ASCII.GetBytes("HSM-API-TOKEN").CopyTo(expectedInput, 0);
            expectedInput[13] = 0x00;
            expectedInput[14] = ApiTokenMaterial.CurrentVersionByte;
            material.TokenIdBytes.CopyTo(expectedInput, 15);
            material.SecretBytes.CopyTo(expectedInput, 31);

            var expected = SHA256.HashData(expectedInput);

            var actual = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ComputeVerifier_ChangedVersionOrIdOrSecret_ProducesDifferentVerifier()
        {
            var material = ApiTokenMaterial.Generate();

            var baseline = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes);

            var changedVersion = ApiTokenVerifier.ComputeVerifier(
                0x02, material.TokenIdBytes, material.SecretBytes);
            var changedId = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, ApiTokenMaterial.Generate().TokenIdBytes, material.SecretBytes);
            var changedSecret = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, ApiTokenMaterial.Generate().SecretBytes);

            Assert.NotEqual(baseline, changedVersion);
            Assert.NotEqual(baseline, changedId);
            Assert.NotEqual(baseline, changedSecret);
        }

        [Fact]
        public void ComputeVerifier_WrongInputLengths_Throw()
        {
            Assert.Throws<ArgumentException>(() =>
                ApiTokenVerifier.ComputeVerifier(0x01, new byte[15], new byte[32]));

            Assert.Throws<ArgumentException>(() =>
                ApiTokenVerifier.ComputeVerifier(0x01, new byte[16], new byte[31]));
        }

        [Fact]
        public void Verify_CorrectVerifier_True_Tampered_False()
        {
            var material = ApiTokenMaterial.Generate();

            var stored = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes);
            var candidate = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes);
            var tampered = (byte[])candidate.Clone();
            tampered[0] ^= 0xFF;

            Assert.True(ApiTokenVerifier.Verify(candidate, stored));
            Assert.False(ApiTokenVerifier.Verify(tampered, stored));
        }

        [Fact]
        public void Verify_WrongLengths_False()
        {
            var stored = new byte[32];

            Assert.False(ApiTokenVerifier.Verify(null, stored));
            Assert.False(ApiTokenVerifier.Verify(new byte[31], stored));
            Assert.False(ApiTokenVerifier.Verify(new byte[32], null));
            Assert.False(ApiTokenVerifier.Verify(new byte[32], new byte[16]));
        }

        [Fact]
        public void DummyVerifier_IsStableAndNeverMatchesRealVerifiers()
        {
            var material = ApiTokenMaterial.Generate();

            var real = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, material.TokenIdBytes, material.SecretBytes);

            Assert.Equal(32, ApiTokenVerifier.DummyVerifier.Length);
            Assert.False(ApiTokenVerifier.Verify(real, ApiTokenVerifier.DummyVerifier));

            // Stable across calls: recomputing the dummy from the same constants matches.
            var recomputed = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte,
                new byte[ApiTokenMaterial.TokenIdBytesLength],
                new byte[ApiTokenMaterial.SecretBytesLength]);

            Assert.Equal(recomputed, ApiTokenVerifier.DummyVerifier);
        }
    }
}
