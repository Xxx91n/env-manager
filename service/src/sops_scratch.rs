// Service-owned scratch root for the delegated CLI's sops plaintext (#177).
//
// Why this exists: sops is file-in/file-out, so `SopsProvider` writes the value it is
// about to encrypt - or the value it just decrypted - into
// `%TEMP%\env-manager-sops-<guid>\` and removes that directory in a `finally`. A service
// stop terminates the CLI before the `finally` runs (#153), and the kill-on-close job
// object ends processes, not files (#160), so one stop that landed on a rotation left one
// plaintext file per secret on disk under the service identity's `%TEMP%`: no expiry, no
// sweep, no audit. The plaintext lifecycle table in `docs/architecture.md` had to carry a
// stop note for it, i.e. the table documented a routine operational action as an
// exception to its own rule.
//
// Chosen over keeping sops off disk entirely (encrypt from stdin, decrypt to stdout):
// that shape needs the `sops encrypt` / `sops decrypt` subcommands, which arrive in sops
// 3.9, while the flag form the provider uses today has no stdin at all (`Error: no file
// specified`, measured on 3.7.3) and `winget` on Windows still installs 3.7.x. A
// version-gated fallback would put the plaintext file straight back, so the subcommand
// form buys a second code path without removing the surface. The decision comment on
// #177 records the probe and the versions it was taken on.
//
// Shape: one root per delegation, handed to the CLI through `SCRATCH_ENV` and reclaimed
// when `reconcile::run_cli_child` returns. Owning the directory is what makes the
// guarantee reachable at all - the process that cleans up is the one still alive when the
// CLI is killed. `SopsProvider.CreateWorkingDirectory` falls back to its own `%TEMP%`
// directory when the variable is unset or the root is not writable, so a CLI started by
// hand behaves exactly as it did before.

use std::path::{Path, PathBuf};
use std::time::Duration;

/// The variable `SopsProvider` reads to place its sops working directories
/// (`src/Secrets/Providers/SopsProvider.cs`). Spelled after the existing
/// `ENVMANAGER_LOCALAPPDATA` seam rather than with the `ENV_MANAGER_` split.
pub(crate) const SCRATCH_ENV: &str = "ENVMANAGER_SOPS_SCRATCH_DIR";

/// Prefix of the service-owned root. Deliberately *not* `env-manager-sops-`: that is the
/// name of the provider's own working directories, and a root left behind by a service
/// that was killed rather than stopped has to be recognisable as a service artefact
/// instead of as something the provider wrote.
const ROOT_PREFIX: &str = "env-manager-rotate-scratch-";

/// Retry budget for a delete that still meets a held handle. Closing the job handle asks
/// the kernel to terminate its members, which is asynchronous, so a `sops` process can
/// still own the plaintext file for a moment after the stop path returns. Five 200ms
/// attempts stay inside `crate::SHUTDOWN_DRAIN`, the window the stop path shares with
/// terminate-and-reap.
const RECLAIM_ATTEMPTS: u32 = 5;
const RECLAIM_BACKOFF: Duration = Duration::from_millis(200);

/// A directory this service created for one delegated CLI child and owns until it is
/// gone. Dropping it without reclaiming it is the leak #177 is about, so `Drop` makes one
/// last attempt: that path is only reachable when the caller's future is dropped
/// mid-await, the same class of exit `Child::kill_on_drop` covers for the child.
pub(crate) struct SopsScratch {
    root: PathBuf,
    reclaimed: bool,
}

impl SopsScratch {
    /// Create the root, or `None` when it cannot be created.
    ///
    /// A failure here is not a reason to refuse the rotation: the CLI falls back to its
    /// own `%TEMP%` directory, which is the pre-#177 behaviour, and the warning names the
    /// lost guarantee so the service log does not read as if the root existed.
    pub(crate) fn create() -> Option<Self> {
        let root = std::env::temp_dir().join(format!("{}{}", ROOT_PREFIX, uuid::Uuid::new_v4()));
        match std::fs::create_dir_all(&root) {
            Ok(()) => Some(Self {
                root,
                reclaimed: false,
            }),
            Err(e) => {
                tracing::warn!(
                    "sops scratch root could not be created, the delegated CLI keeps using its own %TEMP% and a stop can leave plaintext behind: {}",
                    e
                );
                None
            }
        }
    }

    /// The path handed to the CLI. Only meaningful while this value is alive.
    pub(crate) fn path(&self) -> &Path {
        &self.root
    }

    /// Delete the tree, retrying while a killed helper still holds a handle.
    ///
    /// Every failure that survives the budget is logged at error level: the ticket's
    /// "删除失败必须留痕" is this line, because a silent leftover is exactly the defect the
    /// directory is meant to remove.
    pub(crate) async fn reclaim(mut self) {
        self.reclaimed = true;
        for attempt in 1..=RECLAIM_ATTEMPTS {
            match remove_tree(&self.root) {
                Ok(()) => return,
                Err(err) if attempt == RECLAIM_ATTEMPTS => {
                    tracing::error!(
                        "sops scratch root {} could not be removed after {} attempts, plaintext may remain on disk: {}",
                        self.root.display(),
                        attempt,
                        err
                    );
                    return;
                }
                Err(_) => tokio::time::sleep(RECLAIM_BACKOFF).await,
            }
        }
    }
}

impl Drop for SopsScratch {
    fn drop(&mut self) {
        if self.reclaimed {
            return;
        }
        if let Err(err) = remove_tree(&self.root) {
            tracing::error!(
                "sops scratch root {} was left behind when its delegation was abandoned: {}",
                self.root.display(),
                err
            );
        }
    }
}

/// `Ok` when the tree is gone - including "it was never there", which a concurrent
/// reclaim makes a normal outcome - and the OS message otherwise.
fn remove_tree(root: &Path) -> Result<(), String> {
    match std::fs::remove_dir_all(root) {
        Ok(()) => Ok(()),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(e.to_string()),
    }
}
