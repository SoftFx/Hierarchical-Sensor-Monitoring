# One-time SSH key setup (password → key login)

The deploy skills run many `ssh` commands per update; a password prompt on each one
breaks the flow. Switch the machine to key-based login once — after that ssh is
non-interactive and nothing secret is stored anywhere (the skills' machine config
holds only `user@host`, port, and the deployment directory).

Any existing key pair will do — check what the workstation already has and reuse it
(the public half goes to the server; the private one never leaves this machine):

```bash
ls ~/.ssh/id_*.pub
```

If there is none, generate an Ed25519 key **with a passphrase** — this key grants
docker-level (root-equivalent) access to production machines, so it must not sit
unencrypted on disk:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/id_ed25519
```

A passphrase does not break the non-interactive flow: load the key into ssh-agent
once per session and `BatchMode` logins keep working:

```bash
eval "$(ssh-agent -s)" && ssh-add ~/.ssh/id_ed25519
```

Below, `<key>` is the key chosen above (`id_ed25519`, an existing `id_rsa`, …).

## Option A — ssh-copy-id (when available)

```bash
ssh-copy-id -i ~/.ssh/<key>.pub -p <port> user@host
```

Enter the password one last time. Check availability with `command -v ssh-copy-id`;
Git for Windows usually ships it in `/usr/bin`. If it is missing, use option B.

## Option B — manual append (works everywhere, incl. any Git Bash)

```bash
cat ~/.ssh/<key>.pub | ssh -p <port> user@host 'mkdir -p ~/.ssh && chmod 700 ~/.ssh && touch ~/.ssh/authorized_keys && { [ -z "$(tail -c1 ~/.ssh/authorized_keys)" ] || echo >> ~/.ssh/authorized_keys; } && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys'
```

Enter the password one last time. This appends the public key to the machine's
`~/.ssh/authorized_keys` (Ubuntu Server accepts key auth out of the box). The
newline guard matters: appended to a file whose last line lacks a trailing
newline (a key pasted with an editor or `echo -n`), the new line would glue onto
it and break **both** keys — including the one that already worked.

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
