//! `hsm-linux-probe` — a systemd-hosted Linux host probe for HSM.
//!
//! The probe is plumbing, not a sensor library: metric acquisition that HSM already has lives in
//! the shared native collector (`src/native/collector`), which this process hosts through its
//! stable C ABI. In this phase it registers exactly the sensor set the managed HSMDataCollector
//! registers on Linux and nothing of its own (parity contract: `src/probe-linux/README.md`);
//! probe-only signals (initiative `docs/initiatives/linux-docker-probe.md` §4.2) come later.
//!
//! Linux is the only supported platform; see `src/probe-linux/README.md`.

mod config;
mod logging;
mod probe;
mod secret;
mod shutdown;

use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::Arc;

use config::{Config, DEFAULT_CONFIG_PATH};
use logging::Logger;

const USAGE: &str = "\
hsm-linux-probe — HSM Linux host probe

USAGE:
    hsm-linux-probe [--config <path>]

OPTIONS:
    -c, --config <path>  Configuration file (default: /etc/hsm-linux-probe/config.json)
    -V, --version        Print the probe and collector versions and exit
    -h, --help           Print this help and exit

The access key is never a command-line argument: the config file names the file that holds it.";

fn main() -> ExitCode {
    match parse_args(std::env::args().skip(1)) {
        Ok(Command::Help) => {
            println!("{USAGE}");
            ExitCode::SUCCESS
        }
        Ok(Command::Version) => {
            println!(
                "hsm-linux-probe {} (collector {})",
                probe::PROBE_VERSION,
                hsm_collector::library_version_string()
            );
            ExitCode::SUCCESS
        }
        Ok(Command::Run { config_path }) => run(config_path),
        Err(message) => {
            eprintln!("{message}\n\n{USAGE}");
            ExitCode::FAILURE
        }
    }
}

fn run(config_path: PathBuf) -> ExitCode {
    let config = match Config::load(&config_path) {
        Ok(config) => config,
        Err(error) => {
            eprintln!("hsm-linux-probe: {error}");
            return ExitCode::FAILURE;
        }
    };

    let logger = Arc::new(Logger::new(
        logging::parse_level(&config.logging.level),
        config.logging.directory.as_deref(),
    ));

    // Installed before the collector starts so a signal arriving during startup is not lost.
    shutdown::install();

    match probe::run(&config, Arc::clone(&logger)) {
        Ok(()) => {
            logger.info("hsm-linux-probe stopped");
            ExitCode::SUCCESS
        }
        Err(error) => {
            logger.error(format!("fatal: {error}"));
            ExitCode::FAILURE
        }
    }
}

enum Command {
    Run { config_path: PathBuf },
    Version,
    Help,
}

fn parse_args(mut args: impl Iterator<Item = String>) -> Result<Command, String> {
    let mut config_path = PathBuf::from(DEFAULT_CONFIG_PATH);

    while let Some(arg) = args.next() {
        match arg.as_str() {
            "-h" | "--help" => return Ok(Command::Help),
            "-V" | "--version" => return Ok(Command::Version),
            "-c" | "--config" => {
                let value = args.next().ok_or_else(|| format!("{arg} needs a path"))?;
                config_path = PathBuf::from(value);
            }
            other => {
                if let Some(value) = other.strip_prefix("--config=") {
                    config_path = PathBuf::from(value);
                } else {
                    return Err(format!("unrecognized argument '{other}'"));
                }
            }
        }
    }

    Ok(Command::Run { config_path })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parse(args: &[&str]) -> Result<Command, String> {
        parse_args(args.iter().map(|arg| arg.to_string()))
    }

    #[test]
    fn defaults_to_the_packaged_config_path() {
        let command = parse(&[]).expect("parse");
        assert!(
            matches!(command, Command::Run { config_path } if config_path == std::path::Path::new(DEFAULT_CONFIG_PATH))
        );
    }

    #[test]
    fn accepts_both_config_spellings() {
        for args in [
            vec!["--config", "/tmp/a.json"],
            vec!["-c", "/tmp/a.json"],
            vec!["--config=/tmp/a.json"],
        ] {
            let command = parse(&args).expect("parse");
            assert!(
                matches!(command, Command::Run { config_path } if config_path == std::path::Path::new("/tmp/a.json"))
            );
        }
    }

    #[test]
    fn help_and_version_short_circuit() {
        assert!(matches!(parse(&["--help"]).expect("parse"), Command::Help));
        assert!(matches!(parse(&["-V"]).expect("parse"), Command::Version));
    }

    #[test]
    fn unknown_arguments_and_missing_values_are_errors() {
        assert!(parse(&["--nonsense"]).is_err());
        assert!(parse(&["--config"]).is_err());
    }
}
