# HSM Linux probe — operator runbook

Install, upgrade, roll back and remove `hsm-linux-probe` on a Debian 13 (trixie) amd64 host; what
it keeps on the host and for how long; how to prove it does not wake sleeping disks; what it costs.
Everything about *what* the probe reports is in [`README.md`](README.md); the design is
[`docs/initiatives/linux-docker-probe.md`](../../docs/initiatives/linux-docker-probe.md) (epic #1413).

Commands run as root (`sudo`). `<v>` is a probe version such as `0.8.0`.

## Where the package comes from

| Source | What it is | Use it for |
|---|---|---|
| GitHub Release `probe-v<v>` | `hsm-linux-probe_<v>_amd64.deb` + `hsm-linux-probe_<v>_amd64.deb.sha256`, built and install-smoked by `.github/workflows/probe-release.yml` | every real install |
| The server's **Download Linux probe** bundle | the `.deb` of the release pinned in the server's `probe-release.txt`, byte-identical, plus a generated config, the product key and `install.sh` | the usual first install (below) |
| CI artifact `hsm-linux-probe-deb-ci` (`probe-linux.yml`) | `hsm-linux-probe_<v>~ci_amd64.deb` from a PR or branch | trials only; `~ci` sorts below `<v>`, so the release replaces it on upgrade |
| `packaging/build-deb.sh <v>~trialN` | a local build ([README](README.md#building-the-deb)) | trials only, same sorting rule |

Only plain `X.Y.Z` versions are released, each above every earlier release, so `apt-get` always
sees a release as an upgrade of a trial or of the previous release.

## Install

**With the server bundle (recommended).** An admin opens the product in the HSM web UI → *Edit* →
**Download Linux probe**, copies `hsm-linux-probe-<product>.tar.gz` to the host, and runs:

```bash
tar xzf hsm-linux-probe-<product>.tar.gz
sudo ./hsm-linux-probe-<product>/install.sh
rm -f hsm-linux-probe-<product>.tar.gz   # it still contains the product key
```

`install.sh` places the config and the key, installs the package and `ca-certificates`, trusts the
server's certificate when the bundle carries one, writes the top-CPU drop-in
`/etc/systemd/system/hsm-linux-probe.service.d/top-cpu.conf` when the config it leaves in
`/etc/hsm-linux-probe/` enables `topCpu` (and removes it when that config does not), then
`systemctl enable --now` and prints the unit status (details:
`aicontext/features/server/linux-probe-download/feature.md`). **One product per host:** two hosts
installed from one product's bundle write into the same sensors.

**From the release, by hand** (no server bundle, e.g. a key issued separately):

```bash
v=<v>
base=https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/releases/download/probe-v$v
curl -fLO "$base/hsm-linux-probe_${v}_amd64.deb"
curl -fLO "$base/hsm-linux-probe_${v}_amd64.deb.sha256"
sha256sum -c "hsm-linux-probe_${v}_amd64.deb.sha256"           # must print: OK

sudo apt-get install -y "./hsm-linux-probe_${v}_amd64.deb"      # creates the hsm-probe user,
                                                                 # seeds /etc/hsm-linux-probe/config.json,
                                                                 # does NOT start the unit
sudo install -m 0400 -o root -g root /path/to/product-key /etc/hsm-linux-probe/access-key
sudoedit /etc/hsm-linux-probe/config.json                        # hsm.address = https://<server>, hsm.port
sudo systemctl enable --now hsm-linux-probe
```

The key file holds the product access key and nothing else. It is never written into
`config.json`: `hsm.accessKeyFile` stays `"access-key"`, which the unit's `LoadCredential=` hands
to the probe. A server with a private certificate: copy its certificate to
`/usr/local/share/ca-certificates/hsm-server.crt` (the `.crt` extension is required) and run
`sudo update-ca-certificates`. The probe has no switch to skip certificate checks.

**Check** (both routes):

```bash
hsm-linux-probe --version                       # hsm-linux-probe <v> (collector <c>)
systemctl is-active hsm-linux-probe             # active
journalctl -u hsm-linux-probe -n 50 --no-pager  # "Registered <n> sensor(s) on connect", no ERROR lines
```

On the server the product shows `.computer/…` and `.probe/…` within a minute; `.probe/.module/Service
alive` is `True`.

### Top CPU processes

`topCpu` in `config.json` (off by default; README "Top CPU processes"). The unit's
`ProtectProc=invisible` hides every other process from the probe, so the feature also needs the
drop-in `/etc/systemd/system/hsm-linux-probe.service.d/top-cpu.conf`. The server bundle's
`install.sh` manages it from the config **on the host**, whatever the bundle's switch says:

| Config in `/etc/hsm-linux-probe/` after `install.sh` | Bundle switch | `install.sh` |
|---|---|---|
| enables `topCpu` | on or off | writes the drop-in, prints `Top CPU processes: on in …` |
| does not (the existing config was kept) | on | no drop-in, prints a **WARNING**: top CPU stays off |
| does not | off | no drop-in; removes an existing one |

After that WARNING, either re-run `sudo ./install.sh --force-config` (the bundle's config replaces
the host's — including any hand edits in it), or keep the host's config and enable it by hand: add
`"topCpu": { "enabled": true, "periodMs": 60000, "minPercent": 1.0, "count": 10 }` at the top level
of `/etc/hsm-linux-probe/config.json`, then

```bash
sudo mkdir -p /etc/systemd/system/hsm-linux-probe.service.d
printf '[Service]\nProtectProc=default\n' | sudo tee /etc/systemd/system/hsm-linux-probe.service.d/top-cpu.conf
sudo systemctl daemon-reload && sudo systemctl restart hsm-linux-probe
```

(the same by hand on a host installed from the release). Without the drop-in the probe runs and
logs one INFO line (`/proc is mounted with hidepid=invisible`).

## Upgrade

```bash
v=<new v>
# download + sha256sum -c as in "Install", then:
sudo apt-get install -y -o Dpkg::Options::=--force-confold "./hsm-linux-probe_${v}_amd64.deb"
hsm-linux-probe --version && systemctl is-active hsm-linux-probe
```

The old package's `prerm` stops a running probe and leaves a marker in `/run`; the new `postinst`
starts it again, so an upgrade does not end monitoring (a probe that was stopped stays stopped).
`/etc/hsm-linux-probe/config.json`, the key and `/var/lib/hsm-linux-probe` are not touched: the
config is not a dpkg conffile (from 0.3.1), so no upgrade stops at a conffile prompt, and
`--force-confold` is only belt and braces. Read the [README](README.md) upgrade notes of the
versions in between first: some releases moved sensors (e.g. 0.4.0, 0.6.0, 0.6.1, 0.6.2; 0.7.0
and 0.8.0 move none), and the old nodes then go stale on the server and are removed there by hand.
Upgrading to **0.8.1**: Docker stats are sampled once a minute by default, but a config seeded by
an earlier package pins `"samplePeriodSec": 5` — remove that key from `probe.docker` (or set `60`)
in `/etc/hsm-linux-probe/config.json` and `sudo systemctl restart hsm-linux-probe`; the start log
flags a pinned faster period in one INFO line (README, "Upgrade note (0.8.1)").

Re-running a newer server bundle's `install.sh` upgrades the same way and keeps the existing config
and key; `install.sh --force-config` replaces the config with the bundle's.

Upgrading from a `0.1.0~trial*` package: its old `prerm` leaves no marker, so start the unit once by
hand (`sudo systemctl start hsm-linux-probe`).

## Roll back

Install the previous release over the current one. `apt-get` treats it as a downgrade and needs
`--allow-downgrades`:

```bash
v=<previous v>
# download + sha256sum -c as in "Install", then:
sudo apt-get install -y --allow-downgrades -o Dpkg::Options::=--force-confold \
    "./hsm-linux-probe_${v}_amd64.deb"
hsm-linux-probe --version && systemctl is-active hsm-linux-probe
```

The restart marker works the same way (for targets from 0.2.0 on). Config, key and state stay.
The versioned state files (`disk-written.json`, `docker-state.json`) carry a format version; an
older probe that does not know it treats the file as absent and that source starts fresh, as on a
first install (logged once). Fields added without a new version — the read totals in
`disk-written.json` (#1506) — are ignored by an older probe, which keeps the written day; back on
the newer one, the read day starts afresh with its "measured since" comment. A rollback across a release that moved sensors
puts the sensors back under the older paths. Below 0.7.0 the `topCpu` block is ignored (unknown keys
are), its sensors time out, and a `top-cpu.conf` drop-in only relaxes `ProtectProc` for nothing:
delete it (`sudo rm /etc/systemd/system/hsm-linux-probe.service.d/top-cpu.conf && sudo systemctl
daemon-reload`) before the restart. To make the server's bundles serve the older package
too, set `src/server/HSMServer/probe-release.txt` back to it and release the server.

## Remove

```bash
sudo ./hsm-linux-probe-<product>/uninstall.sh   # from a bundle: purge + key + config + CA + drop-ins
# or by hand:
sudo systemctl disable --now hsm-linux-probe
sudo shred -u /etc/hsm-linux-probe/access-key
sudo apt-get purge -y hsm-linux-probe           # removes config, state, logs, the Docker and
                                                # top-CPU drop-ins (the latter from 0.7.0 on)
```

The `hsm-probe` user and group stay (Debian convention for system users). The sensor history on
the HSM server is never touched; delete the product's nodes there if they are no longer wanted.

## What the probe keeps on the host (retention)

Sensor values are **not** stored on the host: the collector queues them in memory (bounded) and
sends them to the server, which keeps the history per sensor (`KeepHistory`). A value that cannot
be delivered by the time the queue overflows or the stop drain ends is dropped and logged at WARN.

| What | Where | Written | Kept |
|---|---|---|---|
| Probe log | `/var/log/hsm-linux-probe/hsm-linux-probe_<UTC date>.log` (`LogsDirectory=`, 0750) | every line at or above `logging.level` (default `info`) | one file per UTC day; files not written for **30 days** are deleted by `systemd-tmpfiles-clean.timer` (`/usr/lib/tmpfiles.d/hsm-linux-probe.conf`, in packages built since #1418; older ones never delete a log file); purge deletes the directory |
| Journal | journald (the unit's stderr) | the same lines | the host's journald limits (default: 10 % of the filesystem, at most 4 GiB); not removed by purge |
| Disk names | `/var/lib/hsm-linux-probe/disk-names.json` (`StateDirectory=`, 0700) | when a mount point first gets a name | **for good**: a name is never given away or removed, so the sensors of a mount point keep their history across restarts; one small entry per mount point ever reported; purge deletes it |
| Disk write day | `/var/lib/hsm-linux-probe/disk-written.json` | every 5 min, at the day's post, on stop | the running local day — its written and read totals (the read fields since #1506) — and each disk's last counters; a disk not seen for more than a day is dropped |
| Docker state | `/var/lib/hsm-linux-probe/docker-state.json` | on change (last-seen at most hourly; the hourly write total at most every 5 min, per hour and on stop) | per Compose service; a service that disappeared is reported `Stopped` and kept for **7 days** after last seen, then forgotten; an OOM latch lasts `probe.docker.oomLatchHours` (24 h) |
| Upgrade marker | `/run/hsm-linux-probe.restart-after-upgrade` | by `prerm` during an upgrade of a running probe | until the new `postinst` (tmpfs: gone at reboot) |
| Config, key | `/etc/hsm-linux-probe/config.json`, `access-key` | by the operator / `install.sh`; the package only seeds a missing config | until purge (config) / `uninstall.sh` or by hand (key) |
| Unit drop-ins | `/etc/systemd/system/hsm-linux-probe.service.d/`: `docker.conf` (postinst, where a docker group exists), `top-cpu.conf` (`install.sh`, while the config on the host enables top-CPU) | at install | until `uninstall.sh` or remove/purge |

Nothing else is written: `ProtectSystem=strict` leaves the state and log directories as the unit's
only writable paths. Top CPU processes (#1479) keeps nothing on the host: its per-process baseline
lives in memory, and a restart only costs one period without values.

## Acceptance: sleeping disks stay asleep

The probe reports every mounted real filesystem, including archive disks that spin down. It only
calls `statvfs(2)` on a mount point — answered from the filesystem's in-memory superblock — and
reads `/proc/self/mountinfo`, sysfs `uevent`/`name`/`type` attributes and `/proc/diskstats`, all
kernel memory. It **never opens, lists or reads anything under a mount**, never reads a disk's
temperature input, and skips automounted filesystems (a `statfs` there would remount and wake the
disk). This was measured on FUSE-NTFS and ext4 archive disks (`smartctl -n standby` before and
after); run the procedure below on every host that has disks which spin down, at the first install
and whenever the host's disks change.

`smartctl` comes from `smartmontools`. `-n standby` makes it answer without spinning the disk up:
exit status **2** and `Device is in STANDBY mode` mean asleep; any other result means the disk was
already awake. USB enclosures may need `-d sat`.

1. Find the spinning disks and the filesystems on them:
   `lsblk -o NAME,ROTA,TYPE,SIZE,MOUNTPOINTS` (`ROTA` = 1).
2. Pick a window in which nothing else touches those disks (no backup or media job), and let them
   spin down on their own timers.
3. **Before** installing, upgrading or starting the probe, for each such disk:
   ```bash
   sudo smartctl -n standby -i /dev/<hdd>; echo "exit=$?"   # Device is in STANDBY mode … exit=2
   ```
   If a disk is not in standby, wait until it is; the check proves nothing on an awake disk.
4. Install/upgrade (or `sudo systemctl restart hsm-linux-probe`) and note the time.
5. **15 minutes later** — three 5-minute disk samples and one 10-minute mount re-scan — repeat
   step 3 for each disk. Every disk that was asleep must still report STANDBY (exit 2).
6. Confirm the disks were actually polled meanwhile: on the server,
   `.computer/Disks monitoring/Free space on <name> disk` of each archive filesystem has values
   from that window, and `journalctl -u hsm-linux-probe` lists the filesystems at start
   (`disks: '<name>' = <mount point> (…)`) with no ERROR lines.

If a disk woke up, stop the probe (`sudo systemctl stop hsm-linux-probe`), let the disk sleep and
repeat steps 3–5 without the probe to rule out another cause. If the probe is the cause, exclude the
mount point (`probe.disks.exclude` in `/etc/hsm-linux-probe/config.json`, then restart) and report
it with the filesystem type and mount options.

## Cost

Measured with systemd's accounting (`systemctl status`/`show`: `CPUUsageNSec`, `MemoryPeak`) against
the unit's budget of `MemoryMax=64M` and `CPUQuota=5%`:

| Run | Build | Load on the host | CPU | Memory |
|---|---|---|---|---|
| 24 h | 0.1.0 trial, parity set only | 15 sensors | 180 s ≈ **0.2 %** of one core | 3.9 MB after start, **6.7 MB** peak |
| 12 h 14 min | probe 0.5.0 | 113 sensors: 12 Compose services, 4 filesystems | 132.4 s ≈ **0.30 %** of one core | **7.9 MB** peak |

Both on a 4-core Debian 13 host, with no restarts and no errors in the journal. Process RSS
including shared pages (libcurl, OpenSSL) is higher, about 17–20 MB. History cost on the server:
see the records/day columns in the [README](README.md#probe-only-sensors).
