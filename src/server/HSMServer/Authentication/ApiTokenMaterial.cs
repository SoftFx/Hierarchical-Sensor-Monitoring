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

        // Version-independent family prefix of every HSM API-token credential. Guards that
        // only ask "is this an HSM credential at all" (LegacyBearerGuardMiddleware) match
        // the family, so a future hsm_pat_v2_ credential cannot slip past them into the
        // legacy pipeline; the strict versioned prefix above stays the single source for
        // parsing and shape checks.
        public const string TokenFamilyPrefix = "hsm_pat_";

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


        // The canonical TokenId text of a token that TryParse accepted — the exact string
        // the authentication index is keyed by. Call only after a successful parse of the
        // same string; offsets live here so callers never slice by hand.
        public static string TokenIdOf(string token) => token.Substring(TokenPrefix.Length, TokenIdLength);


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

        // Cheap shape check for a presented bearer credential: exact total length, the
        // version prefix and the '.' separator, with no decoding or allocation. Alphabet and
        // canonical-encoding validation stay in TryParse — this only separates "clearly not
        // our credential" from "our credential, malformed" before any manager work.
        public static bool IsValidCredentialShape(string credential) =>
            credential is not null &&
            credential.Length == TokenPrefix.Length + TokenIdLength + 1 + SecretLength &&
            credential.StartsWith(TokenPrefix, StringComparison.Ordinal) &&
            credential[TokenPrefix.Length + TokenIdLength] == '.';

        // The one place that unpacks a raw Authorization header value into a bearer
        // credential: scheme "Bearer" (case-insensitive) followed by a non-empty
        // parameter. False for any other scheme or a missing parameter — the handler
        // treats that as "not this scheme's credential" and the legacy guard as
        // "pass through", so the shared shape cannot drift between them.
        public static bool TryReadBearerCredential(string headerValue, out string credential)
        {
            credential = null;

            if (string.IsNullOrEmpty(headerValue))
                return false;

            var separator = headerValue.IndexOf(' ');

            if (separator <= 0 || !headerValue[..separator].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                return false;

            credential = headerValue[(separator + 1)..].Trim();

            return credential.Length > 0;
        }


        // Best-effort cleanup of decoded secret bytes. The textual form lives in immutable
        // strings and cannot be cleared; buffers used for hashing are cleared eagerly.
        public static void Clear(byte[] bytes)
        {
            if (bytes is not null)
                CryptographicOperations.ZeroMemory(bytes);
        }

        // Replaces every hsm_pat_v1_<id>.<secret> occurrence in free text (an exception
        // message, an error payload) with its TokenId alone. The id is the public lookup
        // key and safe to name; the secret is the credential and never survives redaction.
        // Forward-only scan: the replacement itself contains the prefix, so re-scanning
        // from the start would loop forever.
        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var builder = new System.Text.StringBuilder(text.Length);
            var consumed = 0;

            while (consumed < text.Length)
            {
                var index = text.IndexOf(TokenPrefix, consumed, StringComparison.Ordinal);

                if (index < 0)
                {
                    builder.Append(text.AsSpan(consumed));
                    break;
                }

                builder.Append(text.AsSpan(consumed, index - consumed));

                var idStart = index + TokenPrefix.Length;
                var hasId = idStart + TokenIdLength < text.Length &&
                    text[idStart + TokenIdLength] == '.';

                if (hasId)
                {
                    // "hsm_pat_v1_<id>.«redacted»" — id kept, secret dropped (also when
                    // truncated mid-secret).
                    builder.Append(TokenPrefix)
                           .Append(text.AsSpan(idStart, TokenIdLength))
                           .Append(".«redacted»");

                    var secretStart = idStart + TokenIdLength + 1;
                    consumed = secretStart + Math.Min(SecretLength, text.Length - secretStart);
                }
                else
                {
                    // A lone/truncated prefix: consume the credential-ish tail that
                    // follows — a truncated id/secret fragment must not survive. The tail
                    // charset includes the separator/encoding forms a secret can appear
                    // in ('.', '%' for percent-encoded separators such as %2E in URLs,
                    // '=' '+' '/' for standard-base64 spellings); the run stops only at
                    // characters no credential spelling contains. Over-redacting one
                    // word is safe; leaking a fragment is not.
                    builder.Append("«redacted»");

                    consumed = idStart;

                    while (consumed < text.Length && IsCredentialTailChar(text[consumed]))
                        consumed++;
                }
            }

            return builder.ToString();
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
            var base64Length = part.Length + (4 - part.Length % 4) % 4;
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
            // id, 43 carry 258 for a 32-byte secret). Checking the bits directly keeps this
            // parse path allocation-free and free of secret-bearing transient strings —
            // unlike Generate(), whose transient encode intermediates are conceded in the
            // Clear comment below.
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

        // Characters a credential fragment can be spelled with in free text, beyond the
        // Base64URL alphabet (see the Redact else-branch).
        private static bool IsCredentialTailChar(char c) =>
            IsBase64UrlChar(c) || c == '.' || c == '%' || c == '=' || c == Base64Plus || c == Base64Slash;
    }
}
