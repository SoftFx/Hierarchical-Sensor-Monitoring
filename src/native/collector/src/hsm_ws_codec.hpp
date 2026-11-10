#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

// RFC 6455 pieces the native collector needs to speak WebSocket as a CLIENT (#1662, ADR-0017):
// the handshake digest, client frame encoding and server frame parsing. Pure, header-only and free
// of libcurl and sockets, so it is unit-tested in the default build lane on every platform.
//
// Why the collector frames WebSocket itself instead of using libcurl's curl_ws_* API: that API
// needs libcurl 8.11+ built with the feature, and the Linux probe links the distribution's libcurl
// on purpose (it wants the distribution's TLS fixes). Debian 12 and Ubuntu 22.04/24.04 ship a
// libcurl without it, so sessions would not exist there. Framing over a plain libcurl connection
// (CONNECT_ONLY + curl_easy_send/recv) works with every libcurl the collector already supports.
namespace hsm::http::ws
{
    enum Opcode : uint8_t
    {
        kContinuation = 0x0,
        kText = 0x1,
        kBinary = 0x2,
        kClose = 0x8,
        kPing = 0x9,
        kPong = 0xA
    };

    inline uint32_t RotateLeft(uint32_t value, int bits)
    {
        return (value << bits) | (value >> (32 - bits));
    }

    // SHA-1 (FIPS 180-1). Used only for the Sec-WebSocket-Accept check of the handshake, which the
    // protocol fixes to SHA-1; it protects nothing secret.
    inline std::array<uint8_t, 20> Sha1(std::string_view input)
    {
        uint32_t h0 = 0x67452301u, h1 = 0xEFCDAB89u, h2 = 0x98BADCFEu, h3 = 0x10325476u, h4 = 0xC3D2E1F0u;

        std::string message(input);
        const uint64_t bit_length = static_cast<uint64_t>(input.size()) * 8u;
        message.push_back(static_cast<char>(0x80));
        while (message.size() % 64 != 56)
            message.push_back('\0');
        for (int shift = 56; shift >= 0; shift -= 8)
            message.push_back(static_cast<char>((bit_length >> shift) & 0xFFu));

        for (size_t chunk = 0; chunk < message.size(); chunk += 64)
        {
            uint32_t w[80];
            for (size_t i = 0; i < 16; ++i)
            {
                const size_t at = chunk + i * 4;
                w[i] = (static_cast<uint32_t>(static_cast<uint8_t>(message[at])) << 24) |
                       (static_cast<uint32_t>(static_cast<uint8_t>(message[at + 1])) << 16) |
                       (static_cast<uint32_t>(static_cast<uint8_t>(message[at + 2])) << 8) |
                       static_cast<uint32_t>(static_cast<uint8_t>(message[at + 3]));
            }
            for (size_t i = 16; i < 80; ++i)
                w[i] = RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

            uint32_t a = h0, b = h1, c = h2, d = h3, e = h4;
            for (size_t i = 0; i < 80; ++i)
            {
                uint32_t f = 0, k = 0;
                if (i < 20)
                {
                    f = (b & c) | (~b & d);
                    k = 0x5A827999u;
                }
                else if (i < 40)
                {
                    f = b ^ c ^ d;
                    k = 0x6ED9EBA1u;
                }
                else if (i < 60)
                {
                    f = (b & c) | (b & d) | (c & d);
                    k = 0x8F1BBCDCu;
                }
                else
                {
                    f = b ^ c ^ d;
                    k = 0xCA62C1D6u;
                }
                const uint32_t temp = RotateLeft(a, 5) + f + e + k + w[i];
                e = d;
                d = c;
                c = RotateLeft(b, 30);
                b = a;
                a = temp;
            }
            h0 += a;
            h1 += b;
            h2 += c;
            h3 += d;
            h4 += e;
        }

        std::array<uint8_t, 20> digest{};
        const uint32_t words[5] = { h0, h1, h2, h3, h4 };
        for (size_t i = 0; i < 5; ++i)
        {
            digest[i * 4] = static_cast<uint8_t>((words[i] >> 24) & 0xFFu);
            digest[i * 4 + 1] = static_cast<uint8_t>((words[i] >> 16) & 0xFFu);
            digest[i * 4 + 2] = static_cast<uint8_t>((words[i] >> 8) & 0xFFu);
            digest[i * 4 + 3] = static_cast<uint8_t>(words[i] & 0xFFu);
        }
        return digest;
    }

    inline std::string Base64(const uint8_t* data, size_t size)
    {
        static const char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        std::string out;
        out.reserve((size + 2) / 3 * 4);
        for (size_t i = 0; i < size; i += 3)
        {
            const uint32_t b0 = data[i];
            const uint32_t b1 = i + 1 < size ? data[i + 1] : 0u;
            const uint32_t b2 = i + 2 < size ? data[i + 2] : 0u;
            const uint32_t triple = (b0 << 16) | (b1 << 8) | b2;
            out.push_back(alphabet[(triple >> 18) & 0x3Fu]);
            out.push_back(alphabet[(triple >> 12) & 0x3Fu]);
            out.push_back(i + 1 < size ? alphabet[(triple >> 6) & 0x3Fu] : '=');
            out.push_back(i + 2 < size ? alphabet[triple & 0x3Fu] : '=');
        }
        return out;
    }

    // The value a server must return in Sec-WebSocket-Accept for a given Sec-WebSocket-Key.
    inline std::string AcceptKey(std::string_view client_key)
    {
        std::string material(client_key);
        material += "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        const auto digest = Sha1(material);
        return Base64(digest.data(), digest.size());
    }

    // One complete frame as a client sends it: FIN set, masked with `mask` (RFC 6455 5.3 requires
    // every client frame to be masked with a fresh unpredictable key).
    inline std::string EncodeClientFrame(uint8_t opcode, std::string_view payload, const std::array<uint8_t, 4>& mask)
    {
        std::string frame;
        frame.reserve(payload.size() + 14);
        frame.push_back(static_cast<char>(0x80u | (opcode & 0x0Fu)));

        const uint64_t size = payload.size();
        if (size < 126)
        {
            frame.push_back(static_cast<char>(0x80u | static_cast<uint8_t>(size)));
        }
        else if (size <= 0xFFFFu)
        {
            frame.push_back(static_cast<char>(0x80u | 126u));
            frame.push_back(static_cast<char>((size >> 8) & 0xFFu));
            frame.push_back(static_cast<char>(size & 0xFFu));
        }
        else
        {
            frame.push_back(static_cast<char>(0x80u | 127u));
            for (int shift = 56; shift >= 0; shift -= 8)
                frame.push_back(static_cast<char>((size >> shift) & 0xFFu));
        }

        for (const uint8_t byte : mask)
            frame.push_back(static_cast<char>(byte));
        for (size_t i = 0; i < payload.size(); ++i)
            frame.push_back(static_cast<char>(static_cast<uint8_t>(payload[i]) ^ mask[i % 4]));
        return frame;
    }

    struct Frame
    {
        uint8_t opcode = 0;
        bool fin = false;
        std::string payload;
    };

    enum class ParseStatus
    {
        NeedMore,     // the buffer does not hold a complete frame yet
        Complete,     // `frame` is filled and `consumed` bytes can be dropped from the buffer
        ProtocolError // the bytes are not a frame a server may send; `error` says why
    };

    // Parses ONE frame sent by a server from the front of `buffer`. `max_payload` bounds what is
    // accepted before any allocation, so a hostile length cannot make the client reserve memory.
    inline ParseStatus ParseServerFrame(
        std::string_view buffer,
        uint64_t max_payload,
        Frame& frame,
        size_t& consumed,
        std::string& error)
    {
        if (buffer.size() < 2)
            return ParseStatus::NeedMore;

        const uint8_t b0 = static_cast<uint8_t>(buffer[0]);
        const uint8_t b1 = static_cast<uint8_t>(buffer[1]);

        if ((b0 & 0x70u) != 0)
        {
            error = "reserved bits set without a negotiated extension";
            return ParseStatus::ProtocolError;
        }
        if ((b1 & 0x80u) != 0)
        {
            error = "a server frame must not be masked";
            return ParseStatus::ProtocolError;
        }

        const uint8_t opcode = b0 & 0x0Fu;
        const bool fin = (b0 & 0x80u) != 0;
        const bool control = (opcode & 0x08u) != 0;
        if (opcode != kContinuation && opcode != kText && opcode != kBinary && opcode != kClose && opcode != kPing && opcode != kPong)
        {
            error = "unknown opcode";
            return ParseStatus::ProtocolError;
        }

        uint64_t length = b1 & 0x7Fu;
        size_t header = 2;
        if (length == 126)
        {
            if (buffer.size() < 4)
                return ParseStatus::NeedMore;
            length = (static_cast<uint64_t>(static_cast<uint8_t>(buffer[2])) << 8) | static_cast<uint8_t>(buffer[3]);
            header = 4;
        }
        else if (length == 127)
        {
            if (buffer.size() < 10)
                return ParseStatus::NeedMore;
            length = 0;
            for (size_t i = 2; i < 10; ++i)
                length = (length << 8) | static_cast<uint8_t>(buffer[i]);
            header = 10;
        }

        if (control && (!fin || length > 125))
        {
            error = "a control frame must be final and at most 125 bytes";
            return ParseStatus::ProtocolError;
        }
        if (length > max_payload)
        {
            error = "frame exceeds the size limit";
            return ParseStatus::ProtocolError;
        }

        if (buffer.size() - header < length)
            return ParseStatus::NeedMore;

        frame.opcode = opcode;
        frame.fin = fin;
        frame.payload.assign(buffer.data() + header, static_cast<size_t>(length));
        consumed = header + static_cast<size_t>(length);
        return ParseStatus::Complete;
    }
} // namespace hsm::http::ws
