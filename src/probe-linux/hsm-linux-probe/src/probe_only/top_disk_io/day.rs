//! `Written per day by process/<name>` and `Read per day by process/<name>` (#1547): the bytes each
//! process name wrote and read during one local day, posted once, just before local midnight —
//! the rules of `Written per day on <disk>` (#1498/#1506, `disks::written`), per process name.
//!
//! **Accounting.** Every read adds each name's delta since the previous read to the current local
//! day (the host's timezone). The day turns at local midnight; once the day is posted, what is
//! still read before midnight counts towards the next day. A delta that cannot be placed only sets
//! a baseline: the first read, a clock that went backwards, and a gap longer than three periods
//! that crosses midnight.
//!
//! **Posting.** The caller posts the day's top names in its last 30 seconds ([`DayLedger::top`]).
//! A day with no measured interval is not posted — never an invented 0; a day whose measurement
//! began after midnight (the probe started during the day) says from when in the comment. A day
//! that ended measured but unposted (its window was missed) is reported once ([`DayLedger::missed`])
//! and never posted late.
//!
//! **Kept in memory only.** Unlike the disk ledger nothing is persisted: a probe restart starts
//! the day afresh from the restart (the comment says since when). The processes outlive a restart
//! but their counters would have to be saved per pid every few minutes for that; a whole-day file
//! for a top-N display was not worth it.

use std::collections::BTreeMap;
use std::time::Duration;

use super::procfs::{Direction, NameIo};
use crate::probe_only::disks::written::{LocalTime, BYTES_PER_GB};

/// The most names one day keeps. The table only grows with distinct process names (a few hundred
/// on a busy host); a host spawning uniquely named processes stops adding names at this bound and
/// says so once ([`DayLedger::add`] returns the dropped count).
pub const MAX_DAY_NAMES: usize = 4096;

/// Why a read added nothing to the day.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Skip {
    ClockBackwards,
    /// The probe missed reads across midnight: the bytes belong to either day.
    GapAcrossDays,
}

/// One name's totals of the day.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub struct Totals {
    pub written: u64,
    pub read: u64,
}

impl Totals {
    fn bytes(&self, direction: Direction) -> u64 {
        match direction {
            Direction::Write => self.written,
            Direction::Read => self.read,
        }
    }
}

/// The running day of every process name.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct DayLedger {
    /// The local day being counted; `None` before the first measured interval.
    pub day: Option<i64>,
    pub names: BTreeMap<String, Totals>,
    /// At least one interval was added today; `false` ⇒ nothing to post.
    pub measured: bool,
    /// The local second the measurement began, when that was not the day's start.
    pub since_second: Option<i64>,
    /// The last local day whose totals were posted.
    pub posted_day: Option<i64>,
    /// Days that ended measured but unposted, for the caller's log (drained there).
    pub missed: Vec<i64>,
}

impl DayLedger {
    /// Add one read's `deltas`, measured from `previous_ms` to `now_ms`. Returns how many new
    /// names could not be kept ([`MAX_DAY_NAMES`]), or why the read was not added.
    pub fn add(
        &mut self,
        deltas: &BTreeMap<String, NameIo>,
        previous_ms: i64,
        now_ms: i64,
        local: LocalTime,
        period: Duration,
    ) -> Result<usize, Skip> {
        let (day, _) = local(now_ms);
        // Once today is posted, what is still moved before midnight counts towards tomorrow.
        let target = match self.posted_day {
            Some(posted) if posted >= day => day + 1,
            _ => day,
        };
        // Checked before the day turns: a clock stepped back across midnight must not wipe the
        // day it came from.
        if now_ms < previous_ms {
            return Err(Skip::ClockBackwards);
        }
        if self.day.is_none_or(|current| target > current) {
            if let Some(ended) = self.day {
                if self.measured && self.posted_day.is_none_or(|posted| posted < ended) {
                    self.missed.push(ended);
                }
            }
            self.day = Some(target);
            self.names.clear();
            self.measured = false;
            self.since_second = None;
        }
        let (previous_day, previous_second) = local(previous_ms);
        let gap = u64::try_from(now_ms - previous_ms).unwrap_or(u64::MAX);
        let longest = u64::try_from(period.as_millis())
            .unwrap_or(u64::MAX)
            .saturating_mul(3);
        if gap > longest && previous_day != day {
            return Err(Skip::GapAcrossDays);
        }
        if !self.measured {
            self.measured = true;
            // A delta that began yesterday — or one counted towards tomorrow after today's post —
            // means the day was watched from its start.
            self.since_second = (previous_day == day && target == day).then_some(previous_second);
        }
        let mut dropped = 0;
        for (name, io) in deltas {
            if !self.names.contains_key(name) && self.names.len() >= MAX_DAY_NAMES {
                dropped += 1;
                continue;
            }
            let totals = self.names.entry(name.clone()).or_default();
            totals.written = totals.written.saturating_add(io.written);
            totals.read = totals.read.saturating_add(io.read);
        }
        Ok(dropped)
    }

    /// Today's top `count` names by `direction` — decimal GB, three decimals, largest first, ties
    /// by name; a name whose total rounds to 0 is left out — and the comment of every value, if
    /// any. `None` when today has no measurement (or the counted day is already over).
    pub fn top(
        &self,
        direction: Direction,
        count: usize,
        now_ms: i64,
        local: LocalTime,
    ) -> Option<DayTop> {
        let day = self.day?;
        // A day behind the clock is over: never report it as today. A clock behind the day keeps
        // posting it, so the sensors do not go silent into their TTL.
        if !self.measured || day < local(now_ms).0 {
            return None;
        }
        let mut top: Vec<(&String, u64)> = self
            .names
            .iter()
            .map(|(name, totals)| (name, totals.bytes(direction)))
            .filter(|(_, bytes)| gigabytes(*bytes) > 0.0)
            .collect();
        top.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(b.0)));
        top.truncate(count);
        let what = match direction {
            Direction::Write => "writes",
            Direction::Read => "reads",
        };
        // A minute of slack: a probe started at 00:00:30 watched the day.
        let comment = self
            .since_second
            .filter(|second| *second > 60)
            .map(|second| {
                format!(
                    "measured since {:02}:{:02} local time ({what} before that are not counted)",
                    second / 3600,
                    second % 3600 / 60
                )
            });
        Some(DayTop {
            names: top
                .into_iter()
                .map(|(name, bytes)| (name.clone(), gigabytes(bytes)))
                .collect(),
            comment,
        })
    }
}

/// A day's top names (decimal GB, largest first) and the comment every value carries.
#[derive(Clone, Debug, PartialEq)]
pub struct DayTop {
    pub names: Vec<(String, f64)>,
    pub comment: Option<String>,
}

/// Decimal GB (10⁹ bytes), three decimals, as `Written per day on <disk>`.
pub fn gigabytes(bytes: u64) -> f64 {
    (bytes as f64 / BYTES_PER_GB * 1000.0).round() / 1000.0
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::probe_only::disks::written::tests::{utc, MIDNIGHT};

    const PERIOD: Duration = Duration::from_secs(60);
    const MIN: i64 = 60_000;
    const HOUR: i64 = 60 * MIN;
    const GB: u64 = 1_000_000_000;

    fn deltas(entries: &[(&str, u64, u64)]) -> BTreeMap<String, NameIo> {
        entries
            .iter()
            .map(|(name, written, read)| {
                (
                    name.to_string(),
                    NameIo {
                        written: *written,
                        read: *read,
                        ..NameIo::default()
                    },
                )
            })
            .collect()
    }

    fn names(top: &Option<DayTop>) -> Vec<(String, f64)> {
        top.as_ref()
            .map(|top| top.names.clone())
            .unwrap_or_default()
    }

    #[test]
    fn a_day_sums_each_name_and_posts_the_largest_first() {
        let mut ledger = DayLedger::default();
        let day = utc(MIDNIGHT).0;
        // Watched from before midnight: no comment.
        let mut at = MIDNIGHT - MIN;
        for _ in 0..3 {
            let next = at + MIN;
            ledger
                .add(
                    &deltas(&[
                        ("postgres", GB, 0),
                        ("rsync", 2 * GB, 3 * GB),
                        ("cat", 0, 1),
                    ]),
                    at,
                    next,
                    utc,
                    PERIOD,
                )
                .expect("add");
            at = next;
        }
        assert_eq!(ledger.day, Some(day));
        let written = ledger.top(Direction::Write, 10, at, utc);
        assert_eq!(
            names(&written),
            [("rsync".to_string(), 6.0), ("postgres".to_string(), 3.0)]
        );
        assert_eq!(written.unwrap().comment, None);
        // One byte rounds to 0 GB: left out, never posted as 0.
        assert_eq!(
            names(&ledger.top(Direction::Read, 10, at, utc)),
            [("rsync".to_string(), 9.0)]
        );
        assert_eq!(names(&ledger.top(Direction::Write, 1, at, utc)).len(), 1);
    }

    #[test]
    fn a_day_measured_from_a_start_says_since_when() {
        let mut ledger = DayLedger::default();
        let start = MIDNIGHT + 9 * HOUR + 30 * MIN;
        ledger
            .add(&deltas(&[("tar", GB, 0)]), start, start + MIN, utc, PERIOD)
            .expect("add");
        let comment = ledger
            .top(Direction::Write, 10, start + MIN, utc)
            .expect("top")
            .comment;
        assert_eq!(
            comment.as_deref(),
            Some("measured since 09:30 local time (writes before that are not counted)")
        );
        let comment = ledger
            .top(Direction::Read, 10, start + MIN, utc)
            .expect("top")
            .comment;
        assert!(comment.unwrap().contains("(reads before that"));
    }

    #[test]
    fn after_the_post_the_rest_of_the_day_counts_towards_the_next() {
        let mut ledger = DayLedger::default();
        let day = utc(MIDNIGHT).0;
        let before = MIDNIGHT + 23 * HOUR + 58 * MIN;
        let window = MIDNIGHT + 24 * HOUR - 20_000;
        ledger
            .add(&deltas(&[("a", GB, 0)]), before, window, utc, PERIOD)
            .expect("add");
        assert_eq!(
            names(&ledger.top(Direction::Write, 10, window, utc)),
            [("a".to_string(), 1.0)]
        );
        ledger.posted_day = Some(day);

        // 23:59:55: after the post, before midnight — tomorrow's.
        let late = window + 15_000;
        ledger
            .add(&deltas(&[("a", 2 * GB, 0)]), window, late, utc, PERIOD)
            .expect("add");
        assert_eq!(ledger.day, Some(day + 1));
        assert!(ledger.missed.is_empty(), "the posted day is not missed");
        // Still "today" by the clock, but counted as tomorrow: the next day's top holds it,
        // watched from its start (no comment).
        let after = MIDNIGHT + 24 * HOUR + MIN;
        ledger
            .add(&deltas(&[("a", GB, 0)]), late, after, utc, PERIOD)
            .expect("add");
        let top = ledger.top(Direction::Write, 10, after, utc);
        assert_eq!(names(&top), [("a".to_string(), 3.0)]);
        assert_eq!(top.unwrap().comment, None);
    }

    #[test]
    fn a_day_that_ends_unposted_is_reported_once_and_never_posted_late() {
        let mut ledger = DayLedger::default();
        let day = utc(MIDNIGHT).0;
        let noon = MIDNIGHT + 12 * HOUR;
        ledger
            .add(&deltas(&[("a", GB, 0)]), noon, noon + MIN, utc, PERIOD)
            .expect("add");
        // The window was missed (a stalled source): the next read is after midnight, within
        // three periods, so it counts towards the new day.
        let before = MIDNIGHT + 24 * HOUR - 10_000;
        ledger
            .add(&deltas(&[]), noon + MIN, before, utc, PERIOD)
            .expect("add");
        let after = before + 2 * MIN;
        ledger
            .add(&deltas(&[("b", GB, 0)]), before, after, utc, PERIOD)
            .expect("add");
        assert_eq!(ledger.missed, [day]);
        assert_eq!(ledger.day, Some(day + 1));
        assert_eq!(
            names(&ledger.top(Direction::Write, 10, after, utc)),
            [("b".to_string(), 1.0)]
        );
        // A day behind the clock is never offered.
        assert!(ledger
            .top(Direction::Write, 10, after + 24 * HOUR, utc)
            .is_none());
    }

    #[test]
    fn a_gap_across_midnight_or_a_clock_step_back_adds_nothing() {
        let mut ledger = DayLedger::default();
        let evening = MIDNIGHT + 22 * HOUR;
        ledger
            .add(
                &deltas(&[("a", GB, 0)]),
                evening,
                evening + MIN,
                utc,
                PERIOD,
            )
            .expect("add");
        // Suspended over midnight: four hours in one delta belong to either day.
        let morning = MIDNIGHT + 26 * HOUR;
        assert_eq!(
            ledger.add(
                &deltas(&[("a", 9 * GB, 0)]),
                evening + MIN,
                morning,
                utc,
                PERIOD
            ),
            Err(Skip::GapAcrossDays)
        );
        assert!(ledger.top(Direction::Write, 10, morning, utc).is_none());
        // A long gap inside one day counts.
        assert_eq!(
            ledger.add(
                &deltas(&[("a", GB, 0)]),
                morning,
                morning + 3 * HOUR,
                utc,
                PERIOD
            ),
            Ok(0)
        );
        // The clock went backwards: nothing added, the day kept.
        assert_eq!(
            ledger.add(
                &deltas(&[("a", GB, 0)]),
                morning + 3 * HOUR,
                morning + 2 * HOUR,
                utc,
                PERIOD
            ),
            Err(Skip::ClockBackwards)
        );
        assert_eq!(
            names(&ledger.top(Direction::Write, 10, morning + 3 * HOUR, utc)),
            [("a".to_string(), 1.0)]
        );
    }

    #[test]
    fn the_name_table_is_bounded() {
        let mut ledger = DayLedger::default();
        let many: Vec<(String, u64, u64)> = (0..MAX_DAY_NAMES + 3)
            .map(|i| (format!("p{i:05}"), GB, 0))
            .collect();
        let entries: Vec<(&str, u64, u64)> = many
            .iter()
            .map(|(name, written, read)| (name.as_str(), *written, *read))
            .collect();
        let noon = MIDNIGHT + 12 * HOUR;
        assert_eq!(
            ledger.add(&deltas(&entries), noon, noon + MIN, utc, PERIOD),
            Ok(3)
        );
        assert_eq!(ledger.names.len(), MAX_DAY_NAMES);
        // A known name keeps counting.
        ledger
            .add(
                &deltas(&[("p00000", GB, 0)]),
                noon + MIN,
                noon + 2 * MIN,
                utc,
                PERIOD,
            )
            .expect("add");
        assert_eq!(ledger.names["p00000"].written, 2 * GB);
    }
}
