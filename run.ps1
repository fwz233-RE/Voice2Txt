# run.ps1 - dev launcher: compile Voice2Txt.cs in memory and run it.
# PowerShell 7 (arm64) runs the app natively; Windows PowerShell 5.1 falls
# back to building and launching bin\Voice2Txt.exe.
#
# Usage: .\run.ps1

$ErrorActionPreference = 'Stop'
$src = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'Voice2Txt.cs')

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $refs = @(
        'System.Windows.Forms', 'System.Windows.Forms.Primitives',
        'System.Drawing', 'System.Drawing.Common', 'System.Drawing.Primitives',
        'System.ComponentModel', 'System.ComponentModel.Primitives',
        'System.ComponentModel.TypeConverter', 'System.Private.Windows.Core',
        'System.Collections', 'System.Runtime', 'System.Runtime.InteropServices',
        'System.Threading', 'System.Threading.Tasks', 'System.Threading.Thread', 'System.IO', 'System.Linq',
        'System.Diagnostics.Process', 'Microsoft.Win32.Primitives',
        'System.Net.Primitives', 'System.Net.Requests', 'System.Net.Http',
        'System.Net.WebSockets', 'System.Net.WebSockets.Client',
        'System.Text.Encoding.Extensions'
    )
    Add-Type -TypeDefinition $src -ReferencedAssemblies $refs
    [Voice2Txt.Program]::Run()
} else {
    & (Join-Path $PSScriptRoot 'build.ps1') -ForceFramework
    Start-Process (Join-Path $PSScriptRoot 'bin\Voice2Txt.exe')
}
