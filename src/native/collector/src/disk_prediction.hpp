// Free-disk-space prediction math (#1426, reworked in #1445) — the portable half of the "Free space
// on disk prediction" sensor, kept free of OS reads so it is unit-testable on any platform (the
// proc_metrics.hpp / tcp_connection_stats.hpp precedent). The platform factories supply the free
// space; this class owns the timing, the smoothing and the posted value/status/comment.
//
// It is a transcription of managed FreeDiskSpacePredictionBase, which is the contract (repo rule
// #10 — one sensor, one acquisition mechanism, mirrored algorithm):
//
//   * a sampling loop every SpaceCheckPeriod (managed DefaultSpaceCheckPeriodInSec = 30 s) computes
//     the SIGNED drain speed over the interval — positive when free space shrank, negative when it
//     grew — and folds EVERY interval into an EMA: the first sample seeds it, every later one is
//     prev*0.9 + cur*0.1. Folding the non-draining intervals too is what lets the estimate decay
//     when the disk stops filling (#1445); the old code folded positive samples only, so one burst
//     of writes pinned a high drain rate forever.
//   * the calibration counter advances on that SAMPLING clock, so "(n/6)" means six completed
//     free-space measurements rather than six posts.
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
            // Managed FreeDiskSpacePredictionBase.SpeedSmoothingFactor.
            static constexpr double kSpeedSmoothingFactor = 0.1;

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

                // Managed divides the speed by 1 MiB and labels it "Mbytes/sec" whatever unit the
                // platform's IDiskInfo reports in (bytes on Windows, kB on Unix). Mirrored rather
                // than corrected: the two collectors must produce the same comment for the same
                // host, and changing the managed label is a separate, user-visible decision.
                const double mb_per_sec = change_speed_ / (1024.0 * 1024.0);

                if (change_speed_ < 0.0)
                {
                    post.value_ms = kMaxPredictionMs;
                    post.status = 0; // OffTime
                    post.comment = "Free space increases by " + FormatSpeed(-mb_per_sec) +
                                   " Mbytes/sec. Value cannot be calculated.";
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
                        "Free space decreases by " + FormatSpeed(mb_per_sec) + " Mbytes/sec. More than 365 days left.";
                    return post;
                }

                post.value_ms = SecondsToMilliseconds(seconds);
                post.status = 1; // Ok
                post.comment = "Free space decreases by " + FormatSpeed(mb_per_sec) + " Mbytes/sec.";
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

            // double.ToString() on an invariant culture — the shortest round-trip form, which is what
            // the managed comment interpolates.
            static std::string FormatSpeed(double value) { return DoubleToInvariantString(value); }

            const int64_t calibration_requests_;

            int64_t samples_count_ = 0;
            double change_speed_ = 0.0;
            double last_space_ = 0.0;
            bool has_last_space_ = false;
        };
    } // namespace collector
} // namespace hsm
