// CertificateStore.cs - machine-store certificate lookup and shape rules
// License: Apache-2.0

// A service account runs as NT SERVICE\EnvManagerService and has no user profile, so
// Cert:\CurrentUser\My is empty for it. The machine store is the only store both an
// interactive session and the service can see.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using EnvManager.Secrets.Core;

namespace EnvManager.Secrets.Providers;

internal static class CertificateStore
{
    /// <summary>
    /// The one certificate named by its SHA-1 fingerprint. Anything else - missing,
    /// expired, no key, failing the shape rules - is an authentication failure, never a
    /// silent pick of a different certificate.
    /// </summary>
    internal static X509Certificate2 SelectForRolesAnywhere(
        string provider, string operation, string sha1, DateTimeOffset now)
    {
        string wanted = AwsRolesAnywhereSessionExchange.Fingerprint(sha1);
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        try
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        }
        catch (Exception ex) when (ex is CryptographicException or System.Security.SecurityException)
        {
            throw new SecretProviderAuthFailedException(provider, operation,
                "cannot open Cert:\\LocalMachine\\My (" + ex.GetType().Name + ")");
        }

        X509Certificate2Collection found = store.Certificates.Find(X509FindType.FindByThumbprint, wanted, validOnly: false);
        X509Certificate2? cert = null;
        for (int i = 0; i < found.Count; i++)
        {
            if (string.Equals(found[i].Thumbprint, wanted, StringComparison.OrdinalIgnoreCase))
            {
                cert = found[i];
                break;
            }
        }
        if (cert == null)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " is not in Cert:\\LocalMachine\\My");

        ValidateForRolesAnywhere(provider, operation, cert, wanted, now);
        return cert;
    }

    /// <summary>
    /// The shape rules the service enforces on a certificate it is asked to sign with.
    /// Split out from the store lookup so the rules can be checked without a machine
    /// store, which is what the offline suite does.
    /// </summary>
    internal static void ValidateForRolesAnywhere(
        string provider, string operation, X509Certificate2 cert, string sha1, DateTimeOffset now)
    {
        string wanted = AwsRolesAnywhereSessionExchange.Fingerprint(sha1);
        if (cert.NotAfter.ToUniversalTime() <= now.UtcDateTime)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " expired on " + cert.NotAfter.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        if (!cert.HasPrivateKey)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " has no private key in the machine store");
        if (cert.Version != 3)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " is X.509 v" + cert.Version + "; Roles Anywhere requires v3");
        if (!HasDigitalSignatureKeyUsage(cert))
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " does not allow digital signature in its key usage");
    }

    /// <summary>
    /// Intermediate certificates from the built chain, leaf and self-signed root
    /// excluded, capped at the five the service accepts.
    /// </summary>
    internal static List<X509Certificate2> IntermediatesFor(X509Certificate2 leaf)
    {
        var result = new List<X509Certificate2>();
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (!chain.Build(leaf))
        {
            // A chain that will not build offline is not fatal here: the trust anchor may
            // hold what the local machine does not, and the service decides. Send nothing
            // rather than a guess.
            return result;
        }
        for (int i = 0; i < chain.ChainElements.Count; i++)
        {
            X509Certificate2 element = chain.ChainElements[i].Certificate;
            if (i == 0) continue;                                    // the leaf travels separately
            if (ElementIsSelfSigned(element)) continue;               // the trust anchor owns the root
            if (result.Count >= 5) break;                             // the service accepts at most five
            result.Add(new X509Certificate2(element.RawData));
        }
        return result;
    }

    private static bool ElementIsSelfSigned(X509Certificate2 cert) =>
        string.Equals(cert.Subject, cert.Issuer, StringComparison.OrdinalIgnoreCase);

    private static bool HasDigitalSignatureKeyUsage(X509Certificate2 cert)
    {
        // Other extensions (BasicConstraints, SubjectKeyIdentifier, ...) are common and
        // may come first; casting every extension would throw on the first one that is
        // not a key usage.
        foreach (X509Extension extension in cert.Extensions)
        {
            if (extension is not X509KeyUsageExtension usage) continue;
            return (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0;
        }
        // No key-usage extension means unrestricted, which satisfies the requirement.
        return true;
    }
}
