#pragma once

/// @file
/// @brief One reusable zstd compression context over libzstd (#1848). Internal: compiled only with
/// HSM_COLLECTOR_TABLES, never part of the public headers.
///
/// The table block writer compresses one frame per block with the one-shot API (ZSTD_compress2, the
/// content size in the frame). The context is kept between blocks and exposed through Context(), so a
/// later user that needs the streaming API (ZSTD_compressStream2) for inputs too large to hold in memory
/// can drive the same context: CompressFrame resets the parameters it relies on every time it runs.

#include <zstd.h>

#include <cstddef>
#include <cstdint>
#include <utility>
#include <vector>

namespace hsm::collector::tables
{
    /// Owns one ZSTD_CCtx, created on first use and freed with the object. Not thread-safe: one owner
    /// thread at a time (the context is the reason the object exists).
    class ZstdCompressor
    {
    public:
        /// The level of a table block (the format's recommendation, and the managed writer's default).
        static constexpr int kDefaultLevel = 3;

        explicit ZstdCompressor(int level = kDefaultLevel) noexcept
            : level_(level)
        {
        }

        ~ZstdCompressor()
        {
            ZSTD_freeCCtx(context_);
        }

        ZstdCompressor(const ZstdCompressor&) = delete;
        ZstdCompressor& operator=(const ZstdCompressor&) = delete;

        ZstdCompressor(ZstdCompressor&& other) noexcept
            : level_(other.level_), context_(std::exchange(other.context_, nullptr))
        {
        }

        ZstdCompressor& operator=(ZstdCompressor&& other) noexcept
        {
            if (this != &other)
            {
                ZSTD_freeCCtx(context_);
                level_ = other.level_;
                context_ = std::exchange(other.context_, nullptr);
            }
            return *this;
        }

        int Level() const noexcept { return level_; }

        /// The context, created on first call; null only when libzstd could not allocate one. A caller
        /// that sets its own parameters on it must not rely on them surviving the next CompressFrame.
        ZSTD_CCtx* Context() noexcept
        {
            if (context_ == nullptr)
                context_ = ZSTD_createCCtx();
            return context_;
        }

        /// Compresses [data, data + size) as exactly one zstd frame that declares its content size, and
        /// appends it to `out` (after out.size()). False with `error` set when libzstd refuses; `out` is
        /// then left as it was. `error` is a static text (libzstd's own error names are static too), so
        /// a failure allocates nothing. Growing `out` may throw (std::bad_alloc, std::length_error) unless
        /// the caller reserved out.size() + ZSTD_compressBound(size) first.
        bool CompressFrame(const void* data, std::size_t size, std::vector<std::uint8_t>& out, const char*& error)
        {
            ZSTD_CCtx* context = Context();
            if (context == nullptr)
            {
                error = "zstd could not allocate a compression context";
                return false;
            }

            // A previous streaming user may have left a session or other parameters behind.
            std::size_t result = ZSTD_CCtx_reset(context, ZSTD_reset_session_and_parameters);
            if (!ZSTD_isError(result))
                result = ZSTD_CCtx_setParameter(context, ZSTD_c_compressionLevel, level_);
            if (!ZSTD_isError(result))
                result = ZSTD_CCtx_setParameter(context, ZSTD_c_contentSizeFlag, 1);
            if (ZSTD_isError(result))
            {
                error = ZSTD_getErrorName(result);
                return false;
            }

            const std::size_t start = out.size();
            out.resize(start + ZSTD_compressBound(size));

            const std::size_t written = ZSTD_compress2(context, out.data() + start, out.size() - start, data, size);
            if (ZSTD_isError(written))
            {
                out.resize(start);
                error = ZSTD_getErrorName(written);
                return false;
            }

            out.resize(start + written);
            return true;
        }

    private:
        int level_;
        ZSTD_CCtx* context_ = nullptr;
    };
} // namespace hsm::collector::tables
