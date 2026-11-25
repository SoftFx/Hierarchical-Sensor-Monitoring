#pragma once

/// @file
/// @brief The file name mask of a watched folder (#1887), the same rule in both collectors: `*` any run
/// of characters (also none), `?` exactly one character, everything else literal; the whole name must
/// match. Windows folds ASCII letters only (`A`-`Z` = `a`-`z`); elsewhere the comparison is ordinal. A
/// character is a UTF-8 code point. Masks are restricted to what every system matcher agrees on: no
/// `[`, `]`, `;`, backslash, `/` or control character.

#include <cstddef>
#include <string>

namespace hsm::collector::large_files
{
    /// False, with `why`, for a mask the call refuses.
    inline bool CheckMask(const std::string& mask, std::string& why)
    {
        if (mask.empty())
        {
            why = "the mask is empty";
            return false;
        }
        for (const char c : mask)
        {
            const auto u = static_cast<unsigned char>(c);
            if (c == '[' || c == ']' || c == ';' || c == '\\' || c == '/' || u < 0x20 || u == 0x7F)
            {
                why = "the mask may hold only names with * and ? (no [, ], ;, \\, / or control characters)";
                return false;
            }
        }
        return true;
    }

    namespace mask_detail
    {
        /// The length in bytes of the UTF-8 character at `at` (1 for a byte that does not start one).
        inline std::size_t CharLength(const std::string& text, std::size_t at)
        {
            const auto c = static_cast<unsigned char>(text[at]);
            std::size_t length = c < 0x80 ? 1 : c >= 0xF0 ? 4
                                            : c >= 0xE0   ? 3
                                            : c >= 0xC0   ? 2
                                                          : 1;
            return at + length <= text.size() ? length : 1;
        }

        inline char Fold(char c, bool ignore_case)
        {
            return ignore_case && c >= 'A' && c <= 'Z' ? static_cast<char>(c - 'A' + 'a') : c;
        }
    } // namespace mask_detail

    /// True when `name` matches `mask` (iterative, with the last `*` as the only backtrack point: linear
    /// in practice and never exponential).
    inline bool MaskMatches(const std::string& mask, const std::string& name, bool ignore_case)
    {
        using mask_detail::CharLength;
        using mask_detail::Fold;
        std::size_t m = 0, n = 0;
        std::size_t star = std::string::npos, star_name = 0;
        while (n < name.size())
        {
            if (m < mask.size() && mask[m] == '*')
            {
                star = ++m;
                star_name = n;
                continue;
            }
            if (m < mask.size() && mask[m] == '?')
            {
                ++m;
                n += CharLength(name, n);
                continue;
            }
            if (m < mask.size())
            {
                const std::size_t length = CharLength(mask, m);
                bool same = n + length <= name.size();
                for (std::size_t k = 0; same && k < length; ++k)
                    same = Fold(mask[m + k], ignore_case) == Fold(name[n + k], ignore_case);
                if (same)
                {
                    m += length;
                    n += length;
                    continue;
                }
            }
            if (star == std::string::npos)
                return false;
            m = star;
            star_name += CharLength(name, star_name);
            n = star_name;
        }
        while (m < mask.size() && mask[m] == '*')
            ++m;
        return m == mask.size();
    }

    /// The comparison of this system: Windows ignores the case of ASCII letters.
    inline bool MaskMatches(const std::string& mask, const std::string& name)
    {
#if defined(_WIN32)
        return MaskMatches(mask, name, true);
#else
        return MaskMatches(mask, name, false);
#endif
    }
} // namespace hsm::collector::large_files
