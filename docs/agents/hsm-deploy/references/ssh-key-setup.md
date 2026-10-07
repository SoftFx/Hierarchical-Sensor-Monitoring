# One-time SSH key setup (password → key login)

The deploy skills run many `ssh` commands per update; a password prompt on each one
breaks the flow. Switch the machine to key-based login once — after that ssh is
non-interactive and nothing secret is stored anywhere (the skills' machine config
holds only `user@host`, port, and the deployment directory).

The workstation already has a key pair: `~/.ssh/id_rsa` (private, never leaves this
machine) and `~/.ssh/id_rsa.pub` (public, goes to the server). If `id_rsa.pub` is
missing, generate a pair first:

```bash
ssh-keygen -t rsa -b 4096 -f ~/.ssh/id_rsa -N ""
```

## Option A — ssh-copy-id (when available)

```bash
ssh-copy-id -i ~/.ssh/id_rsa.pub -p <port> user@host
```

Enter the password one last time. Check availability with `command -v ssh-copy-id`;
Git for Windows usually ships it in `/usr/bin`. If it is missing, use option B.

## Option B — manual append (works everywhere, incl. any Git Bash)

```bash
cat ~/.ssh/id_rsa.pub | ssh -p <port> user@host 'mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys'
```

Enter the password one last time. This appends the public key to the machine's
`~/.ssh/authorized_keys` (Ubuntu Server accepts key auth out of the box).

## Verify non-interactive login

```bash
ssh -o BatchMode=yes -p <port> user@host 'echo ok'
```

- prints `ok` → done; the machine is ready for the deploy skills.
- `Permission denied (publickey,password)` → the key was not accepted. On the machine,
  check that pubkey auth is not disabled: `sudo grep -E "PubkeyAuthentication|AuthorizedKeysFile" /etc/ssh/sshd_config`
  (Ubuntu default: `PubkeyAuthentication yes`, keys from `~/.ssh/authorized_keys`).
  If the home dir is on certain NFS mounts, `StrictModes` may also reject loose
  permissions — `chmod go-w ~` on the machine fixes that.

## Notes

- Only the public key ever leaves the workstation; the skills never store passwords.
- To revoke access later, remove the key's line from `~/.ssh/authorized_keys` on the
  machine.
- Optional hardening once BOTH machines log in by key (user's decision, do not do this
  as part of a deploy): on the machine set `PasswordAuthentication no` in
  `/etc/ssh/sshd_config`, then `sudo systemctl restart ssh`.
