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
        // re-encodes to exactly the presented text (rejecting aliases and padded variants).
        public static bool TryParse(string token, out byte versionByte, out byte[] tokenId, out byte[] secret)
        {
            versionByte = 0;
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

            if (!TryDecodeCanonical(token.AsSpan(TokenPrefix.Length, TokenIdLength), TokenIdBytesLength, out tokenId))
                return false;

            if (!TryDecodeCanonical(token.AsSpan(TokenPrefix.Length + TokenIdLength + 1, SecretLength), SecretBytesLength, out secret))
                return false;

            versionByte = CurrentVersionByte;
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

            bytes = decoded.ToArray();

            // Canonical-encoding check: the decoded bytes must re-encode to exactly the
            // presented text, rejecting encodings with non-zero trailing bits or aliases.
            return EncodeCanonicalBase64Url(bytes).AsSpan().SequenceEqual(part);
        }

        private static bool IsBase64UrlChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == Base64UrlMinus || c == Base64UrlUnderscore;
    }
}
