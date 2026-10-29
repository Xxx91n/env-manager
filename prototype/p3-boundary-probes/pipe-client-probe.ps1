# pipe-client-probe.ps1 - PROTOTYPE helper: the client half of a pipe access check,
# runnable as any identity. Self-contained on purpose, because it is launched under a
# standard user account through cmd.exe and must not depend on anything writable.
#
# Prints one line per observation to stdout; the caller captures it.

param(
    [string]$Pipe = 'EnvManager.Service',
    [string]$Label = ''
)

$src = @'
using System;
using System.Runtime.InteropServices;
public static class LowProto {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);
    public static string Open(string path, uint access) {
        IntPtr h = CreateFileW(path, access, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) {
            int e = Marshal.GetLastWin32Error();
            string d = "";
            if (e == 2) d = " ERROR_FILE_NOT_FOUND(no instance)";
            else if (e == 5) d = " ERROR_ACCESS_DENIED(SD refused)";
            else if (e == 231) d = " ERROR_PIPE_BUSY";
            return "REFUSED err=" + e + d;
        }
        CloseHandle(h);
        return "OPENED ok";
    }
}
'@
try { Add-Type -TypeDefinition $src -Language CSharp -ErrorAction Stop } catch { Write-Output "ADD-TYPE-FAILED $($_.Exception.Message)" }

$path = "\\.\pipe\$Pipe"
Write-Output "PROBE label=$Label pipe=$path"
Write-Output "PROBE identity=$env:USERDOMAIN\$env:USERNAME pid=$PID"
Write-Output "PROBE open_duplex=$([LowProto]::Open($path, 0x120000))"
Write-Output "PROBE open_readonly=$([LowProto]::Open($path, 0x80000000))"
Write-Output "PROBE open_writeonly=$([LowProto]::Open($path, 0x40000000))"

try {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $Pipe, [System.IO.Pipes.PipeDirection]::InOut)
    $client.Connect(5000)
    $w = New-Object System.IO.StreamWriter($client, (New-Object System.Text.UTF8Encoding($false)), 1024, $true)
    $w.WriteLine('{"method":"ping"}')
    $w.Flush()
    $rd = New-Object System.IO.StreamReader($client, [System.Text.Encoding]::UTF8, $false, 1024, $true)
    $resp = $rd.ReadLine()
    Write-Output "PROBE stream_connected=True response=$resp"
    try { $client.Dispose() } catch { }
} catch {
    Write-Output ('PROBE stream_connected=False error={0}: {1}' -f $_.Exception.GetType().Name, $_.Exception.Message)
}
Write-Output 'PROBE done'
