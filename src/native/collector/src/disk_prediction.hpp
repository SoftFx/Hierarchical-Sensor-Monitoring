// Free-disk-space prediction math (#1426, reworked in #1445) — the portable half of the "Free space
// on disk prediction" sensor, kept free of OS reads so it is unit-testable on any platform (the
// proc_metrics.hpp / tcp_connection_stats.hpp precedent). The platform factories supply the free
// space; this class owns the timing, the smoothing and the posted value/status/comment.
//
// It is a transcription of managed FreeDiskSpacePredictionBase, which is the contract (repo rule
// #10 — one sensor, one acquisition mechanism, mirrored algorithm):
//
//   * a sampling loop every SpaceCheckPeriod (managed DefaultSpaceCheckPeriodInSec = 600 s) computes
//     the SIGNED drain speed over the interval — positive when free space shrank, negative when it
//     grew — and folds EVERY interval into an EMA: the first sample seeds it, every later one is
//     prev*(1-a) + cur*a with a = 1/37. Folding the non-draining intervals too is what lets the
//     estimate decay when the disk stops filling (#1445); the old code folded positive samples
//     only, so one burst of writes pinned a high drain rate forever. The period and the factor are
//     chosen together so the estimate spans SIX HOURS (mean sample age period*(1-a)/a = 36 periods)
//     — the scale this sensor's answer lives on. A minute-scale window sampled write bursts
//     instead of the drain.
//   * the calibration counter advances on that SAMPLING clock, so "(n/3)" means three completed
//     free-space measurements rather than three posts — the first estimate lands ~30 min after
//     Start.
//   * the post loop, every PostDataPeriod (5 min), publishes exactly one of five states. Only
//     Draining carries a real estimate (free_space / speed, status Ok). Every other state posts the
//     CEILING (365 days) with status OffTime and a comment naming the state, so a reader can tell
//     "nothing is draining" from a genuine estimate. A zero value is reserved for its literal
//     meaning — no free space left — and is never used as a placeholder.
//
// Value, status and comment of one post always describe the same instant: NextPost computes all
// three from one snapshot, whatever order the caller reads them in.

#pragma once

#include "double_format.hpp"

#include <cmath>
#include <cstdint>
#include <string>

namespace hsm
{
    namespace collector
    {
        struct DiskPredictionPost
        {
            int64_t value_ms = 0;
            int32_t status = 1; // hsm_sensor_status_t: 1 = Ok, 0 = OffTime
            std::string comment;
        };

        class DiskSpacePrediction
        {
        public:
            // Managed FreeDiskSpacePredictionBase.SpeedSmoothingFactor. Chosen together with the
            // 10-minute sampling period so the mean age of the samples behind the estimate,
            // period * (1 - a) / a, is exactly 36 periods = 6.0 h (#1445).
            static constexpr double kSpeedSmoothingFactor = 1.0 / 37.0;

            // Managed FreeDiskSpacePredictionBase.MaxPrediction = TimeSpan.FromDays(365).
            static constexpr int64_t kMaxPredictionMs = 365LL * 24 * 60 * 60 * 1000;
            static constexpr double kMaxPredictionSeconds = 365.0 * 24 * 60 * 60;

            // `calibration_requests` mirrors DiskSensorOptions.CalibrationRequests (managed default 6).
            explicit DiskSpacePrediction(int64_t calibration_requests)
                : calibration_requests_(calibration_requests)
            {
            }

            // The sampling loop's tick. `free_space` is in the platform's own unit and `elapsed_seconds`
            // is the time since the previous sample; the unit cancels in NextPost's division, so the
            // Windows factory may pass bytes and the Linux one kilobytes — exactly as the managed
            // WindowsDiskInfo/UnixDiskInfo pair does.
            void Sample(double free_space, double elapsed_seconds)
            {
                if (!has_last_space_)
                {
                    // The Start seed (managed StartAsync: _lastAvailableSpace = FreeSpace) — a baseline
                    // only, no speed and no calibration credit from it.
                    last_space_ = free_space;
                    has_last_space_ = true;
                    return;
                }

                if (elapsed_seconds > 0.0)
                {
                    const double speed = (last_space_ - free_space) / elapsed_seconds;

                    change_speed_ = samples_count_ == 0
                                        ? speed
                                        : change_speed_ * (1.0 - kSpeedSmoothingFactor) + speed * kSpeedSmoothingFactor;
                    ++samples_count_;
                }

                last_space_ = free_space;
            }

            // The post loop's tick: one snapshot of the sampler state turned into value, status and
            // comment together.
            DiskPredictionPost NextPost(double free_space)
            {
                DiskPredictionPost post;

                if (samples_count_ < calibration_requests_)
                {
                    post.value_ms = kMaxPredictionMs;
                    post.status = 0; // OffTime
                    post.comment = "Calibration request (" + std::to_string(samples_count_) + "/" +
                                   std::to_string(calibration_requests_) + "). Value cannot be calculated yet.";
                    return post;
                }

                // Managed divides the speed by 1 MiB whatever unit the platform's IDiskInfo reports
                // in (bytes on Windows, kB on Unix). Mirrored rather than corrected: the two
                // collectors must produce the same comment for the same host. The comment then
                // renders this per HOUR (FormatRatePerHour, #1460) — both collectors changed
                // together, since the corpus pins the text byte-for-byte.
                const double mb_per_sec = change_speed_ / (1024.0 * 1024.0);

                if (change_speed_ < 0.0)
                {
                    post.value_ms = kMaxPredictionMs;
                    post.status = 0; // OffTime
                    post.comment = "Free space increases by " + FormatRatePerHour(-mb_per_sec) +
                                   " Mbytes/hour. Value cannot be calculated.";
                    return post;
                }

                if (change_speed_ == 0.0)
                {
                    post.value_ms = kMaxPredictionMs;
                    post.status = 0; // OffTime
                    post.comment = "Free space is not decreasing. Value cannot be calculated.";
                    return post;
                }

                const double seconds = free_space / change_speed_;

                // Written so a NaN also takes the ceiling branch instead of producing a bogus value.
                if (!(seconds < kMaxPredictionSeconds))
                {
                    post.value_ms = kMaxPredictionMs;
                    post.status = 0; // OffTime
                    post.comment =
                        "Free space decreases by " + FormatRatePerHour(mb_per_sec) +
                        " Mbytes/hour. More than 365 days left.";
                    return post;
                }

                post.value_ms = SecondsToMilliseconds(seconds);
                post.status = 1; // Ok
                post.comment = "Free space decreases by " + FormatRatePerHour(mb_per_sec) + " Mbytes/hour.";
                return post;
            }

        private:
            // TimeSpan.FromSeconds rounds to the nearest millisecond. The caller has already bounded
            // `seconds` below the ceiling, so this only has to reproduce that rounding.
            static int64_t SecondsToMilliseconds(double seconds)
            {
                if (!std::isfinite(seconds) || seconds < 0.0)
                    return 0;

                const double ms = seconds * 1000.0;
                return ms >= static_cast<double>(kMaxPredictionMs) ? kMaxPredictionMs : static_cast<int64_t>(ms + 0.5);
            }

            // The operator-facing rate, in MEGABYTES PER HOUR with three fixed decimals (#1460).
            // Rendering MB/sec through the payload formatter (shortest round-trip, which is what
            // the managed comment interpolates) printed a realistic idle drain as
            // "1.6574101944286661E-06" — 17 digits of scientific notation in a sentence an
            // operator reads. Per hour is the scale this sensor answers on anyway, and three
            // decimals keep a KB/h-sized trickle visible.
            //
            // The digits are produced by INTEGER arithmetic on purpose: the two collectors must
            // emit byte-identical text, and a printf/ToString("F3") pair does not guarantee that
            // at a decimal midpoint (round-half-even vs round-half-away-from-zero). Scaling the
            // same IEEE double by 1000 and rounding half away from zero (std::llround; C# does
            // Math.Round(x, MidpointRounding.AwayFromZero)) is defined identically on both sides.
            // An absurd magnitude that would overflow the scaled integer falls back to the
            // round-trip form rather than to undefined behavior — mirrored in managed.
            static std::string FormatRatePerHour(double mb_per_sec)
            {
                const double per_hour = mb_per_sec * 3600.0;
                const double scaled = per_hour * 1000.0;

                // A non-finite rate cannot go through the round-trip formatter (it parses the
                // exponent out of std::to_chars output, which has none for nan/inf), so name it the
                // way managed double.ToString does on an invariant culture.
                if (std::isnan(per_hour))
                    return "NaN";
                if (std::isinf(per_hour))
                    return per_hour > 0.0 ? "Infinity" : "-Infinity";

                if (!std::isfinite(scaled) || std::fabs(scaled) >= 9.0e15)
                    return DoubleToInvariantString(per_hour);

                const long long units = std::llround(scaled);
                const unsigned long long magnitude =
                    units < 0 ? 0ULL - static_cast<unsigned long long>(units) : static_cast<unsigned long long>(units);

                std::string fraction = std::to_string(magnitude % 1000ULL);
                fraction.insert(fraction.begin(), 3 - fraction.size(), '0');

                return (units < 0 ? "-" : "") + std::to_string(magnitude / 1000ULL) + "." + fraction;
            }

            const int64_t calibration_requests_;

            int64_t samples_count_ = 0;
            double change_speed_ = 0.0;
            double last_space_ = 0.0;
            bool has_last_space_ = false;
        };
    } // namespace collector
} // namespace hsm
