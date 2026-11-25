#pragma once

/// @file
/// @brief Callback isolation and the single-worker periodic task of the native collector. Internal.
///
/// Shared by the collector core (hsm_collector.cpp: the periodic sensor scheduler) and the table
/// pipeline (src/tables/: the table tick), which is why it is a header of its own.

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <limits>
#include <mutex>
#include <thread>
#include <utility>

namespace hsm::collector::detail
{
    // Host-callback isolation: a throwing callback may neither cross the C ABI boundary nor
    // break the component that invoked it — lifecycle listeners, log sinks, scheduler actions
    // and onError. overview.md "Callback exception isolation".
    template <typename Fn>
    void InvokeIsolated(Fn&& fn) noexcept
    {
        try
        {
            fn();
        }
        catch (...)
        {
            // Swallowed by contract.
        }
    }

    // Worker threads (the scheduler task and the dispatch worker) are always joined by their owning
    // object's teardown — never detached. hsm_collector_destroy() disposes the collector, joining every
    // worker, BEFORE the last shared_ptr is dropped (see the note there), so a worker can never be the
    // collector's final owner and thus never runs teardown on its own thread. A self-join would
    // therefore only arise from the forbidden re-entrancy of a lifecycle call inside a callback; that
    // must fail loudly (a self-join throws EDEADLK) rather than silently detach a live thread onto
    // freed state — the earlier "self-safe" detach did the latter and caused a heap-use-after-free of
    // the ScheduledTask (the scheduler thread kept running Loop() and read `stop_` after the free).

    // Single-worker periodic task — the portable ScheduledTaskHandle (issue #1095 §13).
    // Idempotent Start/Stop; the worker sleeps until the next due time reported by the
    // injected clock (event-driven, not a busy poll — bounded by kMaxWaitMs so a dynamic
    // schedule change or virtual-clock jump is still picked up), runs the action with no
    // overlap (one worker, sequential) and exception isolation, and Stop joins after at
    // most one in-flight action (bounded wait-for-current-run).
    class ScheduledTask
    {
    public:
        // now_ms reports the current monotonic time through the collector's (swappable) clock;
        // next_due_ms reports the next absolute due time; action is the work to run when due.
        ScheduledTask(std::function<int64_t()> now_ms, std::function<int64_t()> next_due_ms, std::function<void()> action)
            : now_ms_(std::move(now_ms)), next_due_ms_(std::move(next_due_ms)), action_(std::move(action))
        {
        }

        ScheduledTask(const ScheduledTask&) = delete;
        ScheduledTask& operator=(const ScheduledTask&) = delete;

        ~ScheduledTask()
        {
            Stop();
        }

        void Start()
        {
            std::lock_guard<std::mutex> guard(mutex_);
            if (running_)
                return;

            stop_ = false;
            running_ = true;
            worker_ = std::thread([this] { Loop(); });
        }

        void Stop()
        {
            {
                std::lock_guard<std::mutex> guard(mutex_);
                if (!running_)
                    return;

                stop_ = true;
            }

            cv_.notify_all();
            if (worker_.joinable())
                worker_.join();

            std::lock_guard<std::mutex> guard(mutex_);
            running_ = false;
        }

        // Re-evaluate the schedule now (e.g. a periodic sensor was added, or virtual time
        // advanced). Cheap and lock-free against the worker — just nudges the wait.
        void Wake()
        {
            // The flag makes a Wake that lands while the loop is computing its next due time (the
            // wide window: it walks every sensor) count — the wait predicate sees it instead of
            // sleeping out kMaxWaitMs. It is NOT stored under mutex_, because callers may hold the
            // collector lock, which the loop takes under mutex_ (lock-order inversion); so a Wake in
            // the narrow gap between the predicate check and the block can still be lost, bounded by
            // kMaxWaitMs as before.
            wake_requested_.store(true);
            cv_.notify_all();
        }

    private:
        static constexpr int64_t kMaxWaitMs = 1000;

        void Loop()
        {
            std::unique_lock<std::mutex> lock(mutex_);

            while (!stop_)
            {
                const int64_t now = now_ms_();
                const int64_t due = next_due_ms_();

                if (now >= due)
                {
                    lock.unlock();
                    InvokeIsolated(action_);
                    lock.lock();
                    continue;
                }

                // due > now here. Guard the subtraction against overflow when due is the
                // "nothing scheduled" sentinel (int64_max) or the (virtual) clock is far
                // negative — just cap the sleep at kMaxWaitMs.
                int64_t wait_ms = kMaxWaitMs;
                if (due < (std::numeric_limits<int64_t>::max)())
                    wait_ms = std::min<int64_t>(due - now, kMaxWaitMs);

                cv_.wait_for(lock, std::chrono::milliseconds(wait_ms), [this] { return stop_ || wake_requested_.load(); });
                // Clearing may swallow a Wake that lands right now; harmless, the loop re-reads the
                // clock and the due time immediately. Do not turn this into a spin on the flag.
                wake_requested_.store(false);
            }
        }

        std::function<int64_t()> now_ms_;
        std::function<int64_t()> next_due_ms_;
        std::function<void()> action_;

        std::atomic<bool> wake_requested_{ false };
        std::mutex mutex_;
        std::condition_variable cv_;
        std::thread worker_;
        bool running_ = false;
        bool stop_ = false;
    };
} // namespace hsm::collector::detail
