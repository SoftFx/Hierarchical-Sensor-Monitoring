#include "hsm_sha256.hpp"

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <bcrypt.h>
#else
#include <openssl/evp.h>
#endif

#include <climits>

namespace hsm::collector::large_files
{
#if defined(_WIN32)
    namespace
    {
        // One algorithm provider for the process, opened on first use; BCrypt providers may be shared by
        // threads. Never closed: it lives as long as the process.
        BCRYPT_ALG_HANDLE Provider() noexcept
        {
            static const BCRYPT_ALG_HANDLE provider = [] {
                BCRYPT_ALG_HANDLE handle = nullptr;
                if (BCryptOpenAlgorithmProvider(&handle, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0)
                    return static_cast<BCRYPT_ALG_HANDLE>(nullptr);
                return handle;
            }();
            return provider;
        }
    } // namespace

    Sha256::Sha256() noexcept
    {
        const BCRYPT_ALG_HANDLE provider = Provider();
        BCRYPT_HASH_HANDLE hash = nullptr;
        if (provider != nullptr && BCryptCreateHash(provider, &hash, nullptr, 0, nullptr, 0, 0) == 0)
            state_ = hash;
    }

    Sha256::~Sha256()
    {
        if (state_ != nullptr)
            BCryptDestroyHash(static_cast<BCRYPT_HASH_HANDLE>(state_));
    }

    void Sha256::Update(const void* data, std::size_t size) noexcept
    {
        if (!Ok())
            return;
        const auto* bytes = static_cast<const UCHAR*>(data);
        while (size > 0)
        {
            const ULONG chunk = size > ULONG_MAX ? ULONG_MAX : static_cast<ULONG>(size);
            if (BCryptHashData(static_cast<BCRYPT_HASH_HANDLE>(state_), const_cast<PUCHAR>(bytes), chunk, 0) != 0)
            {
                ok_ = false;
                return;
            }
            bytes += chunk;
            size -= chunk;
        }
    }

    bool Sha256::Final(Sha256Digest& out) noexcept
    {
        if (!Ok())
            return false;
        ok_ = BCryptFinishHash(static_cast<BCRYPT_HASH_HANDLE>(state_), out.data(), static_cast<ULONG>(out.size()), 0) == 0;
        const bool done = ok_;
        ok_ = false;
        return done;
    }
#else
    namespace
    {
        // The implementation fetched once for the process (OpenSSL 3): every hash then skips the name
        // lookup an implicit fetch of EVP_sha256() makes each time. Never freed: it lives as the process.
        const EVP_MD* Sha256Md() noexcept
        {
#if OPENSSL_VERSION_NUMBER >= 0x30000000L
            static const EVP_MD* const fetched = EVP_MD_fetch(nullptr, "SHA256", nullptr);
            if (fetched != nullptr)
                return fetched;
#endif
            return EVP_sha256();
        }
    } // namespace

    Sha256::Sha256() noexcept
    {
        EVP_MD_CTX* context = EVP_MD_CTX_new();
        if (context == nullptr)
            return;
        if (EVP_DigestInit_ex(context, Sha256Md(), nullptr) != 1)
        {
            EVP_MD_CTX_free(context);
            return;
        }
        state_ = context;
    }

    Sha256::~Sha256()
    {
        if (state_ != nullptr)
            EVP_MD_CTX_free(static_cast<EVP_MD_CTX*>(state_));
    }

    void Sha256::Update(const void* data, std::size_t size) noexcept
    {
        if (!Ok())
            return;
        if (size > 0 && EVP_DigestUpdate(static_cast<EVP_MD_CTX*>(state_), data, size) != 1)
            ok_ = false;
    }

    bool Sha256::Final(Sha256Digest& out) noexcept
    {
        if (!Ok())
            return false;
        unsigned int length = 0;
        ok_ = EVP_DigestFinal_ex(static_cast<EVP_MD_CTX*>(state_), out.data(), &length) == 1 && length == out.size();
        const bool done = ok_;
        ok_ = false;
        return done;
    }
#endif

    bool Sha256Of(const void* data, std::size_t size, Sha256Digest& out) noexcept
    {
        Sha256 hash;
        hash.Update(data, size);
        return hash.Final(out);
    }

    std::string ToHex(const std::uint8_t* data, std::size_t size)
    {
        static const char digits[] = "0123456789abcdef";
        std::string text;
        text.reserve(size * 2);
        for (std::size_t i = 0; i < size; ++i)
        {
            text.push_back(digits[data[i] >> 4]);
            text.push_back(digits[data[i] & 0x0F]);
        }
        return text;
    }
} // namespace hsm::collector::large_files
