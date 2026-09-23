// Portable /proc parsers + delta math for the Linux metric sources (#1414). See proc_metrics.hpp
// for the managed reference each function mirrors. No OS reads here — this translation unit
// compiles and is unit-tested on every platform.

#include "proc_metrics.hpp"

#include <algorithm>
#include <vector>

namespace hsm::collector
{
    namespace
    {
        bool IsFieldSeparator(char c)
        {
            // The managed parsers split on { ' ', '\t', '\r' } with RemoveEmptyEntries.
            return c == ' ' || c == '\t' || c == '\r';
        }

        std::vector<std::string> SplitFields(const std::string& text)
        {
            std::vector<std::string> fields;
            std::string::size_type start = 0;
            while (start < text.size())
            {
                while (start < text.size() && IsFieldSeparator(text[start]))
                    ++start;
                if (start >= text.size())
                    break;
                auto end = start;
                while (end < text.size() && !IsFieldSeparator(text[end]))
                    ++end;
                fields.emplace_back(text, start, end - start);
                start = end;
            }
            return fields;
        }

        // Equivalent of long.TryParse(text, NumberStyles.Integer, InvariantCulture): an optional
        // leading sign followed by decimal digits, nothing else. Overflow is a parse failure. That is
        // exact for /proc/meminfo (ProcMeminfo uses long.TryParse). ProcStat uses double.TryParse,
        // which would ACCEPT a field above INT64_MAX; the native parser rejects it instead. The
        // difference is unreachable with real jiffy counters (2^63 jiffies is ~2.9 billion years at
        // 100 Hz), so one integer parser serves both files.
        bool TryParseInteger(const std::string& text, std::int64_t& value)
        {
            if (text.empty())
                return false;

            std::string::size_type index = 0;
            bool negative = false;
            if (text[index] == '+' || text[index] == '-')
            {
                negative = text[index] == '-';
                ++index;
            }
            if (index >= text.size())
                return false;

            std::uint64_t magnitude = 0;
            const std::uint64_t limit = negative
                                            ? static_cast<std::uint64_t>(INT64_MAX) + 1u
                                            : static_cast<std::uint64_t>(INT64_MAX);
            for (; index < text.size(); ++index)
            {
                const char c = text[index];
                if (c < '0' || c > '9')
                    return false;
                const auto digit = static_cast<std::uint64_t>(c - '0');
                if (magnitude > (limit - digit) / 10u)
                    return false;
                magnitude = magnitude * 10u + digit;
            }

            // INT64_MIN is spelled out rather than negated: `-static_cast<int64_t>(2^63)` is
            // signed-overflow UB (the UBSan lane traps it), and an unsigned round-trip would be
            // merely implementation-defined in C++17. Every other magnitude negates normally.
            if (negative && magnitude == static_cast<std::uint64_t>(INT64_MAX) + 1u)
                value = INT64_MIN;
            else
                value = negative ? -static_cast<std::int64_t>(magnitude) : static_cast<std::int64_t>(magnitude);
            return true;
        }

        std::string FirstLine(const std::string& text)
        {
            const auto newline = text.find('\n');
            return newline == std::string::npos ? text : text.substr(0, newline);
        }
    } // namespace

    std::optional<ProcStatCpuTimes> ParseProcStatCpuTimes(const std::string& proc_stat_content)
    {
        // parts[0]="cpu", [1]=user, [2]=nice, [3]=system, [4]=idle, [5]=iowait, [6]=irq,
        // [7]=softirq, [8]=steal, [9]=guest, [10]=guest_nice.
        constexpr std::size_t kIdleFieldIndex = 4;
        constexpr std::size_t kGuestFieldIndex = 9;
        constexpr std::size_t kGuestNiceFieldIndex = 10;

        if (proc_stat_content.empty())
            return std::nullopt;

        const auto fields = SplitFields(FirstLine(proc_stat_content));

        // Only the aggregate line qualifies: the per-core lines are "cpu0", "cpu1", ... and must not
        // match, so the name is compared exactly rather than by prefix.
        if (fields.size() <= kIdleFieldIndex || fields[0] != "cpu")
            return std::nullopt;

        ProcStatCpuTimes times;
        for (std::size_t i = 1; i < fields.size(); ++i)
        {
            std::int64_t field = 0;
            if (!TryParseInteger(fields[i], field))
                return std::nullopt;

            // guest/guest_nice are already included in user/nice by Linux — summing them again
            // would inflate the total and understate busy%.
            if (i != kGuestFieldIndex && i != kGuestNiceFieldIndex)
                times.total += static_cast<double>(field);

            if (i == kIdleFieldIndex) // idle field only; iowait is deliberately left in "busy"
                times.idle += static_cast<double>(field);
        }

        return times;
    }

    ProcStatCpuUsage::ProcStatCpuUsage(const std::string& initial_proc_stat_content)
        : previous_(ParseProcStatCpuTimes(initial_proc_stat_content))
    {
    }

    std::optional<double> ProcStatCpuUsage::NextBusyPercent(const std::string& proc_stat_content)
    {
        const auto current = ParseProcStatCpuTimes(proc_stat_content);
        if (!current.has_value())
            return std::nullopt;

        const auto previous = previous_;
        previous_ = current;

        if (!previous.has_value())
            return std::nullopt;

        const double total_delta = current->total - previous->total;
        const double idle_delta = current->idle - previous->idle;

        // No elapsed time (identical samples) or a counter reset / CPU-count change that moved the
        // totals backwards: nothing meaningful to report this tick.
        if (total_delta <= 0.0)
            return std::nullopt;

        const double busy = (1.0 - (idle_delta / total_delta)) * 100.0;

        if (busy < 0.0)
            return 0.0;
        if (busy > 100.0)
            return 100.0;

        return busy;
    }

    namespace
    {
        // Managed ProcMeminfo.TryParseLine: the line must START with the key, and the first
        // whitespace-separated token after it must be an integer.
        bool TryParseMeminfoLine(const std::string& line, const char* key, std::int64_t& kb)
        {
            const std::string needle(key);
            if (line.size() < needle.size() || line.compare(0, needle.size(), needle) != 0)
                return false;

            const auto fields = SplitFields(line.substr(needle.size()));
            return !fields.empty() && TryParseInteger(fields[0], kb);
        }
    } // namespace

    std::optional<std::int64_t> ParseMeminfoAvailableKb(const std::string& meminfo_content)
    {
        if (meminfo_content.empty())
            return std::nullopt;

        std::optional<std::int64_t> mem_free;
        std::optional<std::int64_t> buffers;
        std::optional<std::int64_t> cached;
        std::optional<std::int64_t> s_reclaimable;
        std::optional<std::int64_t> shmem;

        std::string::size_type start = 0;
        while (start <= meminfo_content.size())
        {
            const auto newline = meminfo_content.find('\n', start);
            const auto line = meminfo_content.substr(
                start, newline == std::string::npos ? std::string::npos : newline - start);

            std::int64_t value = 0;
            if (TryParseMeminfoLine(line, "MemAvailable:", value))
                return value; // the kernel's own estimate always wins

            if (TryParseMeminfoLine(line, "MemFree:", value))
                mem_free = value;
            else if (TryParseMeminfoLine(line, "Buffers:", value))
                buffers = value;
            else if (TryParseMeminfoLine(line, "Cached:", value))
                cached = value;
            else if (TryParseMeminfoLine(line, "SReclaimable:", value))
                s_reclaimable = value;
            else if (TryParseMeminfoLine(line, "Shmem:", value))
                shmem = value;

            if (newline == std::string::npos)
                break;
            start = newline + 1;
        }

        if (mem_free || buffers || cached || s_reclaimable || shmem)
        {
            const std::int64_t estimate = mem_free.value_or(0) + buffers.value_or(0) + cached.value_or(0) +
                                          s_reclaimable.value_or(0) - shmem.value_or(0);
            return std::max<std::int64_t>(0, estimate);
        }

        return std::nullopt;
    }

    std::optional<ProcSelfStat> ParseProcSelfStat(const std::string& stat_content)
    {
        // Fields after the comm field, 0-based: index = (stat field number) - 3.
        constexpr std::size_t kUtimeIndex = 11; // field 14
        constexpr std::size_t kStimeIndex = 12; // field 15
        constexpr std::size_t kRssIndex = 21;   // field 24

        // The comm field (field 2) is parenthesized and may itself contain spaces and parentheses, so
        // the scan starts after the LAST ')' — the same rule the kernel documents. Restrict to the
        // FIRST LINE before that search: if the buffer ever carried a trailing line containing a ')',
        // the "last ')'" would land in the wrong line and shift every field offset silently, which is
        // exactly the mis-report the rule exists to prevent.
        const std::string line = FirstLine(stat_content);
        const auto close = line.rfind(')');
        if (close == std::string::npos || close + 1 >= line.size())
            return std::nullopt;

        const auto fields = SplitFields(line.substr(close + 1));
        if (fields.size() <= kRssIndex)
            return std::nullopt;

        std::int64_t utime = 0;
        std::int64_t stime = 0;
        std::int64_t rss = 0;
        if (!TryParseInteger(fields[kUtimeIndex], utime) || !TryParseInteger(fields[kStimeIndex], stime) ||
            !TryParseInteger(fields[kRssIndex], rss))
            return std::nullopt;

        if (utime < 0 || stime < 0)
            return std::nullopt;

        ProcSelfStat parsed;
        parsed.utime_ticks = static_cast<std::uint64_t>(utime);
        parsed.stime_ticks = static_cast<std::uint64_t>(stime);
        parsed.rss_pages = rss;
        return parsed;
    }

    ProcessCpuUsage::ProcessCpuUsage(double clock_ticks_per_second)
        : ticks_per_second_(clock_ticks_per_second > 0.0 ? clock_ticks_per_second : 100.0)
    {
    }

    std::optional<double> ProcessCpuUsage::NextCpuPercent(std::uint64_t cpu_ticks, std::int64_t wall_clock_ms)
    {
        const bool had_previous = has_previous_;
        const std::uint64_t previous_cpu = previous_cpu_ticks_;
        const std::int64_t previous_wall = previous_wall_ms_;

        previous_cpu_ticks_ = cpu_ticks;
        previous_wall_ms_ = wall_clock_ms;
        has_previous_ = true;

        if (!had_previous)
            return std::nullopt; // baseline sample, mirroring UnixProcessCpu's constructor seeding

        // The interval must be at least one clock tick. The managed sensor never sees a shorter one
        // (CollectableBarMonitoringSensorBase samples a full bar tick after the constructor seeds the
        // baseline). The Linux source seeds on its first scheduled read for the same reason, and this
        // guard is the second line of defense for any caller that samples back-to-back: utime/stime
        // are QUANTIZED to whole ticks, so a single tick landing in a 2 ms window would read as 500%.
        // Below one tick the ratio carries no information, so the sample is skipped rather than
        // posted as a nonsense spike.
        const std::int64_t elapsed_ms = wall_clock_ms - previous_wall;
        const double min_interval_ms = 1000.0 / ticks_per_second_;
        if (elapsed_ms <= 0 || static_cast<double>(elapsed_ms) < min_interval_ms)
            return std::nullopt;

        // A process's cumulative CPU time cannot decrease, so this cannot trigger in practice — it
        // is guarded only so an unsigned wrap can never manufacture an astronomical percentage.
        if (cpu_ticks < previous_cpu)
            return std::nullopt;

        const double cpu_used_ms = (static_cast<double>(cpu_ticks - previous_cpu) / ticks_per_second_) * 1000.0;

        // UnixProcessCpu: (cpuUsedMs / totalMsPassed).ToPercent(). Not divided by core count, and
        // not clamped — a process saturating two cores legitimately reads 200%.
        return (cpu_used_ms / static_cast<double>(elapsed_ms)) * 100.0;
    }
} // namespace hsm::collector
