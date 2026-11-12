# Env Manager — Glossary

The canonical vocabulary for this project: terms plus their avoid pairs. Each entry gives a short meaning and the near-synonyms to avoid. Implementation details belong in code comments, not here.

## Profiles

**Profile**:
A named set of variables, PATH entries, and links to other profiles. Applying or launching it turns those into one final set of values.
_Avoid_: config file, configuration file, environment set

**Global profile**:
A profile that writes its values into the Windows registry when applied. It cannot be applied while it holds or inherits a secret, and it cannot inherit directly from a Launch profile.
_Avoid_: system profile, machine profile, applied profile

**Launch profile**:
A profile that puts its values into the environment of one child process. It never writes the registry, and only it can pass secrets to a program.
_Avoid_: local profile, child profile, run profile

**Apply**:
Write a Global profile's values into the registry.
_Avoid_: deploy, activate

**Launch**:
Start a program with a Launch profile's values in its environment.
_Avoid_: spawn, execute, start a profile

**Inheritance**:
A profile taking values from another profile. The inheriting profile wins when both set the same variable; the earlier PATH entry wins when two entries name the same path.
_Avoid_: profile nesting, parent chain, connection

## Secrets

**Secret**:
A value the tool stores encrypted instead of as plain text, so the plaintext never appears in the registry or in a profile file. The tool keeps the encrypted form, or a reference to where the provider keeps the value.
_Avoid_: credential blob, encrypted value, masked value

**Secret provider**:
The component that encrypts a secret or fetches it from a remote store. The app ships nine providers.
_Avoid_: backend, secret manager, broker

**Endpoint override**:
A setting that makes one provider send its requests to a different address instead of the real service, so you can run against a local stand-in. Production leaves it unset.
_Avoid_: endpoint hook, custom endpoint, base URL

**Secret envelope**:
The stored form of one secret, written by the provider. It either holds the encrypted form or names where the provider keeps the value.
_Avoid_: ciphertext blob, secret record, stored blob

**Secret mount**:
A separate record for one secret. A profile can point to it by id instead of holding the secret itself.
_Avoid_: mount point, secret reference, secret link

**Refresh**:
Checking a mount's metadata and recording when it was last fetched. It does not decrypt or replace the secret.
_Avoid_: reload, re-fetch, poll

**Refresh policy**:
Whether a secret is fetched only when a Launch profile starts, or also checked on a timer.
_Avoid_: refresh mode, fetch mode, lease policy

**Rotation**:
Decrypting a secret with the provider that stored it, then encrypting it again with the provider the app is set to use.
_Avoid_: rekey, key rollover, secret renewal
