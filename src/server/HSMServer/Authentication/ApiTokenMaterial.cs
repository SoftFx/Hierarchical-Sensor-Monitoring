using System;
using System.Security.Cryptography;

namespace HSMServer.Authentication
{
    // Generation and strict parsing of hsm_pat_v1_* token material.
    // TokenId: 128 random bits as exactly 22 canonical unpadded Base64URL characters — a
    // public lookup key, not a secret. Secret: 256 random bits as exactly 43 canonical
    // characters. The parser rejects padding, wrong lengths, characters outside the Base64URL
    // alphabet and any non-canonical alias BEFORE any database access, so malformed or
    // oversized credentials can never trigger a lookup or excessive work.
    public static class ApiTokenMaterial
    {
        public const string TokenPrefix = "hsm_pat_v1_";

        // hsm_pat_v1_ maps only to versionByte 0x01 and must match the persisted record.
        public const byte CurrentVersionByte = 0x01;

        public const int TokenIdLength = 22;
        public const int SecretLength = 43;

        public const int TokenIdBytesLength = 16;
        public const int SecretBytesLength = 32;

        private const char Base64UrlMinus = '-';
        private const char Base64UrlUnderscore = '_';
        private const char Base64Plus = '+';
        private const char Base64Slash = '/';


        public sealed record GeneratedMaterial(string TokenId, string Secret, byte[] TokenIdBytes, byte[] SecretBytes);


        // One fresh candidate: a completely new id/secret pair (never a partial reuse), with
        // the decoded bytes needed for verifier computation. Randomness comes only from
        // RandomNumberGenerator.
        public static GeneratedMaterial Generate() => new(
            GenerateTokenId(out var tokenIdBytes), GenerateSecret(out var secretBytes), tokenIdBytes, secretBytes);


        public static string FormatToken(string tokenId, string secret) => $"{TokenPrefix}{tokenId}.{secret}";


        // Strict parse of a presented bearer credential. Checks the exact total length,
        // the version prefix, the '.' separator, the alphabet, and that every decoded value
        // has exactly one possible encoding (rejecting aliases and padded variants). The
        // accepted prefix is only hsm_pat_v1_, so the version byte is always
        // CurrentVersionByte — there is nothing per-token to extract; callers that need it
        // read the constant. On failure every out parameter is null.
        public static bool TryParse(string token, out byte[] tokenId, out byte[] secret)
        {
            tokenId = null;
            secret = null;

            if (token is null)
                return false;

            if (token.Length != TokenPrefix.Length + TokenIdLength + 1 + SecretLength)
                return false;

            if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
                return false;

            if (token[TokenPrefix.Length + TokenIdLength] != '.')
                return false;

            if (!TryDecodeCanonical(token.AsSpan(TokenPrefix.Length, TokenIdLength), TokenIdBytesLength, out var decodedTokenId))
                return false;

            if (!TryDecodeCanonical(token.AsSpan(TokenPrefix.Length + TokenIdLength + 1, SecretLength), SecretBytesLength, out var decodedSecret))
                return false;

            tokenId = decodedTokenId;
            secret = decodedSecret;

            return true;
        }


        // Strict validation of a stored TokenId (22 canonical Base64URL characters, 16 bytes).
        public static bool IsValidTokenId(string tokenId) =>
            tokenId is { Length: TokenIdLength } && TryDecodeCanonical(tokenId, TokenIdBytesLength, out _);


        // Best-effort cleanup of decoded secret bytes. The textual form lives in immutable
        // strings and cannot be cleared; buffers used for hashing are cleared eagerly.
        public static void Clear(byte[] bytes)
        {
            if (bytes is not null)
                CryptographicOperations.ZeroMemory(bytes);
        }


        private static string GenerateTokenId(out byte[] tokenIdBytes)
        {
            tokenIdBytes = RandomNumberGenerator.GetBytes(TokenIdBytesLength);
            return EncodeCanonicalBase64Url(tokenIdBytes);
        }

        private static string GenerateSecret(out byte[] secretBytes)
        {
            secretBytes = RandomNumberGenerator.GetBytes(SecretBytesLength);
            return EncodeCanonicalBase64Url(secretBytes);
        }

        private static string EncodeCanonicalBase64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace(Base64Plus, Base64UrlMinus).Replace(Base64Slash, Base64UrlUnderscore);

        private static bool TryDecodeCanonical(ReadOnlySpan<char> part, int expectedBytes, out byte[] bytes)
        {
            bytes = null;

            // Reject padding and any character outside the Base64URL alphabet before decoding.
            foreach (var c in part)
                if (!IsBase64UrlChar(c))
                    return false;

            // Translate to the standard alphabet and pad to a multiple of four so the
            // standard decoder can run; then require the exact expected byte length.
            var base64Length = checked(part.Length + (4 - part.Length % 4) % 4);
            Span<char> base64 = stackalloc char[base64Length];

            for (var i = 0; i < part.Length; i++)
            {
                var c = part[i];
                base64[i] = c == Base64UrlMinus ? Base64Plus : c == Base64UrlUnderscore ? Base64Slash : c;
            }

            base64[part.Length..].Fill('=');

            Span<byte> decoded = stackalloc byte[expectedBytes];

            if (!Convert.TryFromBase64Chars(base64, decoded, out var written) || written != expectedBytes)
                return false;

            // Canonical-encoding check without re-encoding: every bit of every character
            // except the last is significant, so the only possible alias is a final
            // character with non-zero trailing bits (22 chars carry 132 bits for a 16-byte
            // id, 43 carry 258 for a 32-byte secret). Checking the bits directly keeps the
            // hot path allocation-free and never copies the secret into an uncleared string.
            var unusedBits = part.Length * 6 - expectedBytes * 8;

            if (unusedBits > 0 && !HasZeroTrailingBits(part[^1], unusedBits))
                return false;

            bytes = decoded.ToArray();

            return true;
        }

        // True when the final character's low `unusedBits` bits are all zero, which is the
        // exact condition for the encoding to be the canonical one.
        private static bool HasZeroTrailingBits(char last, int unusedBits)
        {
            // Unreachable after the alphabet check above; kept so the helper stands alone.
            if (!TryGetBase64UrlValue(last, out var value))
                return false;

            return (value & ((1 << unusedBits) - 1)) == 0;
        }

        private static bool TryGetBase64UrlValue(char c, out int value)
        {
            if (c >= 'A' && c <= 'Z')
            {
                value = c - 'A';
                return true;
            }

            if (c >= 'a' && c <= 'z')
            {
                value = c - 'a' + 26;
                return true;
            }

            if (c >= '0' && c <= '9')
            {
                value = c - '0' + 52;
                return true;
            }

            if (c == Base64UrlMinus)
            {
                value = 62;
                return true;
            }

            if (c == Base64UrlUnderscore)
            {
                value = 63;
                return true;
            }

            value = 0;

            return false;
        }

        private static bool IsBase64UrlChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == Base64UrlMinus || c == Base64UrlUnderscore;
    }
}
