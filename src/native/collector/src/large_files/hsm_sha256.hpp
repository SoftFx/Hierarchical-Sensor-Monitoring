#pragma once

/// @file
/// @brief SHA-256 from the system or the TLS library already in the process (#1887): Windows CNG
/// (BCryptHash), OpenSSL libcrypto elsewhere. Internal; compiled with HSM_COLLECTOR_HTTP and
/// HSM_COLLECTOR_TABLES only. No hash code of our own.

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>

namespace hsm::collector::large_files
{
    using Sha256Digest = std::array<std::uint8_t, 32>;

    /// One running hash. Not thread-safe; nothing throws. A failure of the library (it cannot allocate a
    /// context) makes Ok() false and Final() fail: the caller treats it as a fault of that attempt.
    class Sha256
    {
    public:
        Sha256() noexcept;
        ~Sha256();

        Sha256(const Sha256&) = delete;
        Sha256& operator=(const Sha256&) = delete;

        bool Ok() const noexcept { return state_ != nullptr && ok_; }
        void Update(const void* data, std::size_t size) noexcept;
        /// The digest of everything given since construction. The object is spent afterwards.
        bool Final(Sha256Digest& out) noexcept;

    private:
        void* state_ = nullptr;
        bool ok_ = true;
    };

    /// The digest of one buffer; false when the library failed.
    bool Sha256Of(const void* data, std::size_t size, Sha256Digest& out) noexcept;

    /// Lower-case hex of `size` bytes.
    std::string ToHex(const std::uint8_t* data, std::size_t size);
} // namespace hsm::collector::large_files
