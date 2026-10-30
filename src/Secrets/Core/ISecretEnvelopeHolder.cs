// ISecretEnvelopeHolder.cs - where a stored secret value actually lives
// A profile's secret variable holds either the envelope itself (the form the secret verbs
// write) or a "mount:<id>" reference into secretMount.json (the form the schema migration
// produces). Rotation has to resolve the second form before it can hand anything to a
// provider, and it has to put the rotated envelope back where it found it, so the
// Secrets.Manager facade takes this port instead of learning about mounts. The mount is its
// own aggregate (GLOSSARY: Secret Mount): a profile only ever names one.
// License: Apache-2.0

namespace EnvManager.Secrets.Core;

internal interface ISecretEnvelopeHolder
{
    // True when storedValue references an envelope held elsewhere. False means storedValue
    // IS the envelope, and the caller owns the write-back into the profile.
    bool IsReference(string storedValue);

    // The envelope behind a reference. Throws when the reference cannot be resolved (no
    // such mount, or a mount that carries no envelope): counting an unresolvable secret as
    // rotated is exactly the silent omission this port exists to prevent.
    string ResolveReference(string storedValue);

    // Persist a rotated envelope back to the holder the reference names.
    void CommitReference(string storedValue, string rotatedEnvelope);
}
