// Free-disk-space prediction math (#1426) — the portable half of the "Free space on disk
// prediction" sensor, kept free of OS reads so it is unit-testable on any platform (the
// proc_metrics.hpp / tcp_connection_stats.hpp precedent). The platform factories supply the free
// space; this class owns the timing, the smoothing and the posted value/status/comment.
//
// It is a transcription of managed FreeDiskSpacePredictionBase, which is the contract (repo rule
// #10 — one sensor, one acquisition mechanism, mirrored algorithm):
//
//   * a sampling loop every SpaceCheckPeriod (managed DefaultSpaceCheckPeriodInSec = 30 s) computes
//     the drain speed over the interval and folds it into an EMA: first positive speed seeds it,
//     every later positive speed is prev*0.9 + cur*0.1. A NEGATIVE or zero speed (free space grew
//     or held) is NOT folded in — it only moves the baseline.
//   * the post loop, every PostDataPeriod (5 min), publishes:
//       - during calibration (the first CalibrationRequests reads): TimeSpan.Zero, status OffTime,
//         comment "Calibration request (n/N)";
//       - otherwise free_space / speed seconds when the speed is positive, else the PREVIOUS
//         prediction (so a flat period repeats the last estimate rather than reporting zero).
//
// Managed's evaluation ORDER is part of the contract and is reproduced exactly: GetValue runs first
// (and is what advances the calibration counter), then GetStatus, then GetComment. That is why the
// FIRST post after calibration still carries TimeSpan.Zero while already reporting a non-calibration
// status and comment — see NextPost.

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
                    // only, no speed from it.
                    last_space_ = free_space;
                    has_last_space_ = true;
                    return;
                }

                if (elapsed_seconds > 0.0)
                {
                    const double speed = (last_space_ - free_space) / elapsed_seconds;
                    if (speed > 0.0)
                        change_speed_ = std::abs(change_speed_) > 0.0 ? change_speed_ * 0.9 + speed * 0.1 : speed;
                }

                last_space_ = free_space;
            }

            // The post loop's tick, in managed's GetValue -> GetStatus -> GetComment order.
            DiskPredictionPost NextPost(double free_space)
            {
                DiskPredictionPost post;

                // ---- GetValue ----
                if (requests_count_ <= calibration_requests_)
                {
                    ++requests_count_;
                    post.value_ms = 0;
                }
                else
                {
                    off_time_ = change_speed_ < 0.0;

                    if (change_speed_ > 0.0)
                    {
                        prev_prediction_ms_ = SecondsToMilliseconds(free_space / change_speed_);
                    }

                    post.value_ms = prev_prediction_ms_;
                }

                // ---- GetStatus ---- (re-evaluates IsCalibration AFTER the counter moved)
                const bool calibrating = requests_count_ <= calibration_requests_;
                post.status = (calibrating || off_time_) ? 0 /*OffTime*/ : 1 /*Ok*/;

                // ---- GetComment ----
                if (calibrating)
                {
                    post.comment = "Calibration request (" + std::to_string(requests_count_) + "/" +
                                   std::to_string(calibration_requests_) + ")";
                }
                else
                {
                    // Managed divides the speed by 1 MiB and labels it "Mbytes/sec" whatever unit the
                    // platform's IDiskInfo reports in (bytes on Windows, kB on Unix). Mirrored rather
                    // than corrected: the two collectors must produce the same comment for the same
                    // host, and changing the managed label is a separate, user-visible decision.
                    const double mb_per_sec = change_speed_ / (1024.0 * 1024.0);
                    post.comment = off_time_
                                       ? "Free space increases by " + FormatSpeed(-mb_per_sec) +
                                             " Mbytes/sec. Value cannot be calculated."
                                       : "Free space decreases by " + FormatSpeed(mb_per_sec) + " Mbytes/sec.";
                }

                return post;
            }

        private:
            // TimeSpan.FromSeconds rounds to the nearest millisecond and throws on a non-finite or
            // out-of-range argument; the collector cannot throw across the C boundary, so an
            // unrepresentable prediction is clamped to the largest TimeSpan instead.
            static int64_t SecondsToMilliseconds(double seconds)
            {
                constexpr double kMaxMs = 922337203685477.0; // TimeSpan.MaxValue in whole ms
                if (!std::isfinite(seconds) || seconds < 0.0)
                    return 0;
                const double ms = seconds * 1000.0;
                return ms >= kMaxMs ? static_cast<int64_t>(kMaxMs) : static_cast<int64_t>(ms + 0.5);
            }

            // double.ToString() on an invariant culture — the shortest round-trip form, which is what
            // the managed comment interpolates.
            static std::string FormatSpeed(double value) { return DoubleToInvariantString(value); }

            const int64_t calibration_requests_;

            int64_t requests_count_ = 0;
            int64_t prev_prediction_ms_ = 0;
            double change_speed_ = 0.0;
            double last_space_ = 0.0;
            bool has_last_space_ = false;
            bool off_time_ = false;
        };
    } // namespace collector
} // namespace hsm
