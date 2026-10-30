// SopsToolPinTests.cs - the CI installer and the test provisioner pin the same sops/age
// License: Apache-2.0
//
// Why this exists: scripts/install-sops-age.ps1 puts sops + age on the CI runners' PATH
// (with SHA-256 checks) and L1ToolProvisioner downloads the same tools for local L1
// runs. The two cannot share a constant across PowerShell and C#, so this test is the
// link: bump one pin without the other and the windows verify lane goes red.

using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace EnvManager.Engine.Tests;

public sealed class SopsToolPinTests
{
    [Fact]
    public void InstallScript_PinsTheSameVersionsAsTheProvisioner()
    {
        string script = File.ReadAllText(Path.Combine(FindRepoRoot(), "scripts", "install-sops-age.ps1"));

        Assert.Equal(L1ToolProvisioner.SopsVersion, ReadPin(script, "SopsVersion"));
        Assert.Equal(L1ToolProvisioner.AgeVersion, ReadPin(script, "AgeVersion"));
    }

    private static string ReadPin(string script, string name)
    {
        Match m = Regex.Match(script, @"^\$" + name + @"\s*=\s*'([^']+)'", RegexOptions.Multiline);
        Assert.True(m.Success, $"${name} assignment not found in scripts/install-sops-age.ps1");
        return m.Groups[1].Value;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "scripts", "install-sops-age.ps1"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("scripts/install-sops-age.ps1 not found above the test assembly");
    }
}
