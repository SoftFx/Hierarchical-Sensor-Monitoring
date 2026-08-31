using System;
using System.Security.Cryptography;

namespace HSMServer.Authentication
{
    // Domain-separated SHA-256 verifier for API tokens:
    //   SHA-256(ASCII("HSM-API-TOKEN") || 0x00 || versionByte[1] || tokenId[16] || secret[32])
    // Only this irreversible verifier is ever persisted. A single fast hash is deliberate:
    // every secret is server-generated from 256 CSPRNG bits, so slow password-hashing
    // (Argon2id/PBKDF2/bcrypt) adds nothing — those protect low-entropy human passwords.
    // No pepper exists; the database alone is sufficient after backup/restore.
    public static class ApiTokenVerifier
    {
        private const string DomainSeparatorText = "HSM-API-TOKEN";

        private static readonly byte[] _domainSeparator =
        [
            ..System.Text.Encoding.ASCII.GetBytes(DomainSeparatorText),
            0x00,
        ];


        // Dummy verifier compared against when the presented TokenId is unknown: the
        // unknown path performs the same hash-and-compare work as the found path and
        // cannot be distinguished from it. Drawn from the CSPRNG rather than computed
        // from constants: a dummy derived from the token format (e.g. the verifier of
        // an all-zero id+secret pair) is by construction the verifier of a presentable
        // credential, and a caller that treats the compare result alone as the decision
        // would authenticate that one fixed token. Random per process is equally
        // constant-time — the dummy is never persisted or compared across restarts.
        // Exposed as a span: a writable array would let any code in the assembly mutate
        // the comparison constant.
        private static readonly byte[] _dummyVerifier = RandomNumberGenerator.GetBytes(SHA256.HashSizeInBytes);

        public static ReadOnlySpan<byte> DummyVerifier => _dummyVerifier;


        public static byte[] ComputeVerifier(byte versionByte, ReadOnlySpan<byte> tokenId, ReadOnlySpan<byte> secret)
        {
            if (tokenId.Length != ApiTokenMaterial.TokenIdBytesLength)
                throw new ArgumentException($"Token id must be exactly {ApiTokenMaterial.TokenIdBytesLength} bytes.", nameof(tokenId));

            if (secret.Length != ApiTokenMaterial.SecretBytesLength)
                throw new ArgumentException($"Secret must be exactly {ApiTokenMaterial.SecretBytesLength} bytes.", nameof(secret));

            // Lengths are attacker-independent: version(1) + id(16) + secret(32) + separator.
            var input = new byte[_domainSeparator.Length + 1 + tokenId.Length + secret.Length];

            try
            {
                _domainSeparator.CopyTo(input, 0);
                input[_domainSeparator.Length] = versionByte;
                tokenId.CopyTo(input.AsSpan(_domainSeparator.Length + 1));
                secret.CopyTo(input.AsSpan(_domainSeparator.Length + 1 + tokenId.Length));

                return SHA256.HashData(input);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(input);
            }
        }


        // Constant-time comparison of a computed candidate against the stored-or-dummy
        // verifier. Callers must select DummyVerifier for unknown records BEFORE comparing,
        // and must still fail the request when no record was found — the compare result
        // alone is never the authentication decision.
        public static bool Verify(byte[] candidate, ReadOnlySpan<byte> storedOrDummyVerifier) =>
            candidate is { Length: SHA256.HashSizeInBytes } &&
            storedOrDummyVerifier.Length == SHA256.HashSizeInBytes &&
            CryptographicOperations.FixedTimeEquals(candidate, storedOrDummyVerifier);
    }
}
