//! Registration of the Docker sensors, per service, as each becomes meaningful.
//!
//! No empty nodes: the state sensors (`Service status`, `Restart count`, `OOM killed`) register on
//! a service's first sighting; `Health` only for a service whose container defines a healthcheck;
//! `CPU` and `Memory used %` once the service first yields stats (it has run), and `Disk written
//! per hour` once those stats carry a block-device write counter. A
//! service remembered from the state file but no longer listed registers `Service status` alone —
//! the only value it still reports.
//!
//! Sensors are registered while the collector runs; the collector sends each registration
//! immediately.

use std::time::Duration;

use hsm_collector::{
    BoolSensor, Collector, DoubleBarSensor, DoubleSensor, EnumOption, EnumSensor, IntSensor,
    Result, SensorOptions, STATISTICS_EMA,
};

use super::alerts::{self, Target};
use super::contract::{self, health, service_status};

/// The sensor handles of one service. `None` = not registered (yet).
pub struct ServiceSensors<'c> {
    pub node: String,
    pub status: Option<EnumSensor<'c>>,
    pub restart_count: Option<IntSensor<'c>>,
    pub oom_killed: Option<BoolSensor<'c>>,
    pub health: Option<EnumSensor<'c>>,
    pub cpu: Option<DoubleBarSensor<'c>>,
    pub memory_used: Option<DoubleBarSensor<'c>>,
    pub disk_written: Option<DoubleSensor<'c>>,
    /// The memory limit (MB, unlimited) the `Memory used %` description currently states.
    pub described_limit: Option<(i32, bool)>,
    /// The configured stats period (`docker.samplePeriodSec`), stated in the stats descriptions.
    pub sample_period: Duration,
}

impl<'c> ServiceSensors<'c> {
    pub fn new(node: String, sample_period: Duration) -> Self {
        Self {
            node,
            status: None,
            restart_count: None,
            oom_killed: None,
            health: None,
            cpu: None,
            memory_used: None,
            disk_written: None,
            described_limit: None,
            sample_period,
        }
    }

    fn path(&self, sensor: &str) -> String {
        format!("{}/{sensor}", self.node)
    }

    /// Register what a poll says this service needs. Each sensor is attempted independently; a
    /// failed registration is retried at the next poll without blocking the others. Returns what
    /// went wrong, for the caller to log.
    pub fn ensure_state_sensors(
        &mut self,
        collector: &'c Collector,
        present: bool,
        has_healthcheck: bool,
    ) -> Vec<String> {
        let mut problems = Vec::new();
        if self.status.is_none() {
            let path = self.path(contract::SERVICE_STATUS);
            keep(
                &mut self.status,
                register_service_status(collector, &path),
                &path,
                &mut problems,
            );
        }
        if present && self.restart_count.is_none() {
            let path = self.path(contract::RESTART_COUNT);
            keep(
                &mut self.restart_count,
                register_restart_count(collector, &path),
                &path,
                &mut problems,
            );
        }
        if present && self.oom_killed.is_none() {
            let path = self.path(contract::OOM_KILLED);
            keep(
                &mut self.oom_killed,
                register_oom_killed(collector, &path),
                &path,
                &mut problems,
            );
        }
        if present && has_healthcheck && self.health.is_none() {
            let path = self.path(contract::HEALTH);
            keep(
                &mut self.health,
                register_health(collector, &path),
                &path,
                &mut problems,
            );
        }
        problems
    }

    /// Register `CPU` and `Memory used %` on the service's first stats, and `Disk written per hour`
    /// once they carry a write counter (`writes`). `limit` is the memory limit the percentage is
    /// taken against (MB, unlimited), stated in the description.
    pub fn ensure_stats_sensors(
        &mut self,
        collector: &'c Collector,
        limit: Option<(i32, bool)>,
        writes: bool,
    ) -> Vec<String> {
        let mut problems = Vec::new();
        if self.cpu.is_none() {
            let path = self.path(contract::CPU);
            keep(
                &mut self.cpu,
                register_cpu(collector, &path, self.sample_period),
                &path,
                &mut problems,
            );
        }
        if self.memory_used.is_none() {
            let path = self.path(contract::MEMORY_USED);
            keep(
                &mut self.memory_used,
                register_memory_used(collector, &path, limit, self.sample_period),
                &path,
                &mut problems,
            );
            if self.memory_used.is_some() {
                self.described_limit = limit;
            }
        }
        if writes {
            problems.extend(self.ensure_disk_written(collector));
        }
        problems
    }

    /// Register `Disk written per hour` unless it is; `Some(problem)` when that failed.
    pub fn ensure_disk_written(&mut self, collector: &'c Collector) -> Option<String> {
        if self.disk_written.is_some() {
            return None;
        }
        let path = self.path(contract::DISK_WRITTEN);
        match register_disk_written(collector, &path, self.sample_period) {
            Ok(sensor) => {
                self.disk_written = Some(sensor);
                None
            }
            Err(error) => Some(format!("{path}: not registered: {error}")),
        }
    }

    /// Keep the `Memory used %` description on the current limit: a changed limit (a recreated
    /// container with a new `mem_limit`) re-registers the sensor with the new text. The limit is
    /// no sensor of its own (owner decision): it is what the percentage means.
    pub fn follow_memory_limit(&mut self, limit: (i32, bool)) -> Result<()> {
        let Some(sensor) = &self.memory_used else {
            return Ok(());
        };
        if self.described_limit == Some(limit) {
            return Ok(());
        }
        sensor.set_description(Some(&memory_used_description(
            Some(limit),
            self.sample_period,
        )))?;
        self.described_limit = Some(limit);
        Ok(())
    }
}

/// A registration: the sensor plus the outcome of attaching its alert.
type Registered<T> = Result<(T, Result<()>)>;

/// Store a registered sensor, or record why it is not registered. A registered sensor whose alert
/// could not be attached is kept (registering it again would duplicate it); the alert failure is
/// reported instead.
fn keep<T>(slot: &mut Option<T>, result: Registered<T>, path: &str, problems: &mut Vec<String>) {
    match result {
        Ok((sensor, alert)) => {
            *slot = Some(sensor);
            if let Err(error) = alert {
                problems.push(format!("{path}: alert not attached: {error}"));
            }
        }
        Err(error) => problems.push(format!("{path}: not registered: {error}")),
    }
}

fn enum_options(options: &[(i32, &str, &str, i32)]) -> Vec<EnumOption> {
    options
        .iter()
        .map(|(key, value, description, color)| EnumOption {
            key: *key,
            value: (*value).to_string(),
            color: *color,
            description: Some((*description).to_string()),
        })
        .collect()
}

// `Service status`, `Health` and `OOM killed` are polled every minute but change rarely: with
// AggregateData the server stores only the changes (the Windows service-status prototype does the
// same), which is what keeps a service at ~580 records a day.

fn register_service_status<'c>(collector: &'c Collector, path: &str) -> Registered<EnumSensor<'c>> {
    let options = SensorOptions::default()
        .with_description(SERVICE_STATUS_DESCRIPTION)
        .with_aggregate_data(true);
    let sensor = collector.enum_sensor_with_options(
        path,
        &options,
        &enum_options(&service_status::OPTIONS),
    )?;
    let alert = alerts::attach(collector, Target::ServiceStatus(&sensor));
    Ok((sensor, alert))
}

fn register_health<'c>(collector: &'c Collector, path: &str) -> Registered<EnumSensor<'c>> {
    let options = SensorOptions::default()
        .with_description(HEALTH_DESCRIPTION)
        .with_aggregate_data(true);
    let sensor =
        collector.enum_sensor_with_options(path, &options, &enum_options(&health::OPTIONS))?;
    let alert = alerts::attach(collector, Target::Health(&sensor));
    Ok((sensor, alert))
}

fn register_restart_count<'c>(collector: &'c Collector, path: &str) -> Registered<IntSensor<'c>> {
    let options = SensorOptions::default()
        .with_description(RESTART_COUNT_DESCRIPTION)
        .with_unit(contract::UNIT_COUNT);
    let sensor = collector.int_sensor(path, &options)?;
    let alert = alerts::attach(collector, Target::RestartCount(&sensor));
    Ok((sensor, alert))
}

fn register_oom_killed<'c>(collector: &'c Collector, path: &str) -> Registered<BoolSensor<'c>> {
    let options = SensorOptions::default()
        .with_description(OOM_KILLED_DESCRIPTION)
        .with_aggregate_data(true);
    let sensor = collector.bool_sensor(path, &options)?;
    let alert = alerts::attach(collector, Target::OomKilled(&sensor));
    Ok((sensor, alert))
}

fn register_cpu<'c>(
    collector: &'c Collector,
    path: &str,
    sample_period: Duration,
) -> Registered<DoubleBarSensor<'c>> {
    let options = SensorOptions::default()
        .with_description(cpu_description(sample_period))
        .with_unit(contract::UNIT_PERCENTS);
    let sensor = collector.double_bar_sensor(
        path,
        contract::BAR_PERIOD,
        contract::BAR_POST_PERIOD,
        contract::BAR_PRECISION,
        &options,
    )?;
    let alert = alerts::attach(collector, Target::Cpu(&sensor));
    Ok((sensor, alert))
}

fn register_memory_used<'c>(
    collector: &'c Collector,
    path: &str,
    limit: Option<(i32, bool)>,
    sample_period: Duration,
) -> Registered<DoubleBarSensor<'c>> {
    let options = SensorOptions::default()
        .with_description(memory_used_description(limit, sample_period))
        .with_unit(contract::UNIT_PERCENTS);
    let sensor = collector.double_bar_sensor(
        path,
        contract::BAR_PERIOD,
        contract::BAR_POST_PERIOD,
        contract::BAR_PRECISION,
        &options,
    )?;
    let alert = alerts::attach(collector, Target::MemoryUsed(&sensor));
    Ok((sensor, alert))
}

// No alert (owner decision): what is "too much" differs per service; EMA statistics give the
// server a smoothed trend to read instead.
fn register_disk_written<'c>(
    collector: &'c Collector,
    path: &str,
    sample_period: Duration,
) -> Result<DoubleSensor<'c>> {
    let options = SensorOptions::default()
        .with_description(disk_written_description(sample_period))
        .with_unit(contract::UNIT_MB)
        .with_statistics(STATISTICS_EMA);
    collector.double_sensor(path, &options)
}

/// "every N s": the configured stats period as the descriptions state it. A config that still
/// pins an older period (the skeleton wrote 5 s before 0.8.1) is described as it runs.
fn every(sample_period: Duration) -> String {
    format!("every {} s", sample_period.as_secs())
}

fn disk_written_description(sample_period: Duration) -> String {
    format!(
        "Megabytes (decimal: 1 MB = 10⁶ bytes, the unit SSD endurance is rated in) the Compose \
service's containers wrote to **block devices** during one clock hour (UTC), replicas and disks \
summed — who is wearing the disk. Physical writes: a write through LVM or dm-crypt counts once, \
on the disk; a mirrored write (md RAID1/10) counts once per member disk. Source: the \
containers' cgroup I/O counters (`blkio_stats.io_service_bytes_recursive`, op write) from the \
Docker Engine API, sampled {every} (docker.samplePeriodSec). Writes still in the page cache \
count when they \
are flushed; reads and tmpfs never count. One value per hour, **sent just after the hour it \
covers**: its time is about one hour later than the writes, and the comment names the window \
(e.g. `13:00–14:00 UTC`) and, when the probe did not watch the whole hour, how much of it was \
measured. The first sample of a container (a recreate), a restart of it or a counter reset \
only sets a baseline; an hour with no measurement is skipped, never sent as 0. With EMA \
statistics.",
        every = every(sample_period)
    )
}

fn cpu_description(sample_period: Duration) -> String {
    format!(
        "CPU used by the Compose service's containers, as a percentage of \
the **whole host** (all cores together = 100 %), replicas summed. Sampled {every} \
(docker.samplePeriodSec, 60 s by default) from the Docker Engine API and aggregated into \
5-minute bars. Note: `docker stats` shows per-core percent (up to 100 % × the number of \
cores); divide its figure by the host's core count to compare. A sample that cannot give an \
honest delta (first sample, counter reset, recreated container, irregular interval) is skipped, \
never sent as 0.",
        every = every(sample_period)
    )
}

/// The `Memory used %` description, stating the limit the percentage is taken against — the
/// container's own limit, or the host's total memory when none is set. `None` before the limit is
/// known (the first stats round replaces it).
pub fn memory_used_description(limit: Option<(i32, bool)>, sample_period: Duration) -> String {
    let basis = match limit {
        Some((megabytes, false)) => {
            format!("of the containers' memory limit — **{megabytes} MB** on this host")
        }
        Some((megabytes, true)) => format!(
            "of the host's total memory — **no memory limit is set**, so the basis is MemTotal \
             ({megabytes} MB)"
        ),
        None => "of the containers' memory limit (or of the host's total memory when no limit \
                 is set)"
            .to_string(),
    };
    format!(
        "Memory used by the Compose service's containers as a percentage {basis}: \
         (usage − inactive_file) / limit, the `docker stats` convention. Replicas: summed usage \
         over summed limits, capped at the host's memory. 5-minute bars of samples taken {every} \
         (docker.samplePeriodSec). The description follows a changed limit.",
        every = every(sample_period)
    )
}

const SERVICE_STATUS_DESCRIPTION: &str = "State of the Compose service's containers, on the same \
status scale as a Windows service: running → Running; created, restarting → StartPending; \
paused → Paused; exited, dead, removing → Stopped. A service that was seen before but whose \
containers were removed reports Stopped for 7 days after it was last seen. With several \
replicas the worst state wins. Polled every minute.";

const HEALTH_DESCRIPTION: &str = "Result of the healthcheck the image or compose file defines \
(`State.Health.Status`): starting, healthy or unhealthy. Registered only for services whose \
containers define a healthcheck. With several replicas the worst result wins. Polled every \
minute.";

const RESTART_COUNT_DESCRIPTION: &str = "Cumulative number of restarts of the Compose service's \
containers by their restart policy (Docker `RestartCount`), summed over replicas and carried \
across container recreates, so it never goes down. Sent only when it changes.";

const OOM_KILLED_DESCRIPTION: &str = "True when a container of the service was killed by the \
kernel for running out of memory (`State.OOMKilled`). Latched for 24 hours by default \
(docker.oomLatchHours), across container recreates, so a quick restart does not hide it. Polled \
every minute.";

#[cfg(test)]
mod tests {
    use super::*;

    const MINUTE: Duration = contract::DEFAULT_SAMPLE_PERIOD;

    #[test]
    fn the_memory_used_description_states_the_limit() {
        assert!(memory_used_description(Some((1024, false)), MINUTE)
            .contains("**1024 MB** on this host"));
        let unlimited = memory_used_description(Some((15917, true)), MINUTE);
        assert!(unlimited.contains("**no memory limit is set**"));
        assert!(unlimited.contains("MemTotal (15917 MB)"));
        assert!(memory_used_description(None, MINUTE).contains("or of the host's total memory"));
    }

    #[test]
    fn the_disk_written_description_states_the_offset_and_the_unit() {
        let text = disk_written_description(MINUTE);
        assert!(text.contains("sent just after the hour it covers"));
        assert!(text.contains("1 MB = 10⁶ bytes"));
        assert!(text.contains("never sent as 0"));
    }

    /// The stats descriptions state the period the probe actually samples at: a config that
    /// still pins 5 s (the skeleton before 0.8.1) must not be described as "once a minute".
    #[test]
    fn the_stats_descriptions_state_the_configured_period() {
        let five = Duration::from_secs(5);
        for text in [
            cpu_description(five),
            memory_used_description(Some((1024, false)), five),
            disk_written_description(five),
        ] {
            assert!(text.contains("every 5 s (docker.samplePeriodSec"), "{text}");
            assert!(!text.contains("every 60 s"), "{text}");
        }
        assert!(cpu_description(MINUTE)
            .contains("Sampled every 60 s (docker.samplePeriodSec, 60 s by default)"));
        assert!(memory_used_description(None, MINUTE).contains("taken every 60 s"));
        assert!(disk_written_description(MINUTE).contains("sampled every 60 s"));
    }
}
