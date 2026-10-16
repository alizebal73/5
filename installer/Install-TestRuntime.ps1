[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][ValidateSet("Manager", "Agent")][string]$Mode
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$ScriptsRoot = Join-Path $InstallRoot "Scripts"
$ProgramDataRoot = Join-Path $env:ProgramData "GameNet Manager"
$ServerServiceName = "GameNet 5 Server"
$AgentServiceName = "GameNet 5 Agent"
$ServerServiceAccount = "NT SERVICE\GameNet 5 Server"
$AgentServiceAccount = "NT AUTHORITY\LocalService"
$ServerPort = 5080

function Assert-ElevatedAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run the temporary Setup as Administrator. No application state was changed."
    }
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw "GameNet 5 test Setup requires 64-bit Windows."
    }
}

function Read-RequiredText {
    param([Parameter(Mandatory = $true)][string]$Prompt)
    $value = (Read-Host $Prompt).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "A required setup value was empty. No further changes were made."
    }
    return $value
}

function ConvertTo-PlainText {
    param([Parameter(Mandatory = $true)][Security.SecureString]$SecureValue)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Invoke-PowerShellScript {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter()][string[]]$Arguments = @()
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required setup helper is missing: $Path"
    }
    $ps = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    & $ps -NoLogo -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "A bundled setup helper failed. Review its visible output; credentials are not intentionally printed by this bootstrapper."
    }
}

function Assert-NoExistingGameNetState {
    if (-not (Test-Path -LiteralPath (Join-Path $InstallRoot "Setup\release-manifest.json") -PathType Leaf)) {
        throw "The bundled release manifest is missing. This package is incomplete."
    }

    $gameNetServices = @(Get-CimInstance -ClassName Win32_Service |
        Where-Object { $_.Name -match '(?i)GameNet|Manager Server' -or $_.DisplayName -match '(?i)GameNet|Manager Server' })
    if ($gameNetServices.Count -gt 0) {
        $names = ($gameNetServices | ForEach-Object { $_.Name }) -join ", "
        throw "Existing GameNet/Manager Windows services were detected ($names). This test Setup never upgrades or repairs a machine. Revert the isolated VM snapshot or use a clean test PC."
    }

    if (Test-Path -LiteralPath $ProgramDataRoot) {
        throw "Existing '$ProgramDataRoot' state was found. Setup refuses to overwrite configuration, identities, certificates, secrets or logs. Use a clean isolated VM snapshot."
    }

    $secretEnvironmentKeys = @(
        "GAMENET_DATABASE_CONNECTION",
        "GAMENET_BOOTSTRAP_SECRET",
        "GameNet__DatabaseConnectionString",
        "GameNet__Authentication__SigningKey",
        "GameNet__Agent__ProvisioningKey",
        "GameNet__Idempotency__FingerprintKey",
        "GameNet__Setup__BootstrapSecret",
        "GameNet:DatabaseConnectionString",
        "GameNet:Authentication:SigningKey",
        "GameNet:Agent:ProvisioningKey",
        "GameNet:Idempotency:FingerprintKey",
        "GameNet:Setup:BootstrapSecret"
    )
    foreach ($key in $secretEnvironmentKeys) {
        if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($key, "Process"))) {
            throw "Unexpected secret-bearing process environment setting detected. Close this shell and start Setup from a clean elevated session."
        }
    }
}

function Get-LocalServerIPv4 {
    $addresses = @(
        Get-NetIPAddress -AddressFamily IPv4 -Type Unicast -ErrorAction Stop |
            Where-Object {
                $_.AddressState -eq "Preferred" -and
                $_.IPAddress -notlike "127.*" -and
                $_.IPAddress -notlike "169.254.*" -and
                $_.IPAddress -notlike "0.*"
            } |
            Sort-Object InterfaceAlias, IPAddress
    )
    if ($addresses.Count -eq 0) {
        throw "No usable local IPv4 address was found. Configure the isolated test network first."
    }

    Write-Host ""
    Write-Host "Choose the reserved IPv4 address of this isolated Manager/Server PC:"
    for ($index = 0; $index -lt $addresses.Count; $index++) {
        Write-Host ("  {0}. {1} ({2})" -f ($index + 1), $addresses[$index].IPAddress, $addresses[$index].InterfaceAlias)
    }
    $choiceText = Read-Host "Address number [1]"
    $choice = 1
    if (-not [string]::IsNullOrWhiteSpace($choiceText)) {
        if (-not [int]::TryParse($choiceText, [ref]$choice)) {
            throw "Choose one of the listed address numbers."
        }
    }
    if ($choice -lt 1 -or $choice -gt $addresses.Count) {
        throw "The selected address number is outside the displayed list."
    }
    return [string]$addresses[$choice - 1].IPAddress
}

function Find-PostgreSql17 {
    $service = Get-CimInstance -ClassName Win32_Service |
        Where-Object { $_.Name -match '^postgresql-x64-17(?:-|$)' -and $_.State -eq "Running" } |
        Select-Object -First 1
    if ($null -eq $service) {
        throw "PostgreSQL 17 is not running on this isolated Manager PC. Install and start PostgreSQL 17 first; this temporary Setup does not install PostgreSQL."
    }

    $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    $candidates = @(
        (Join-Path $env:ProgramFiles "PostgreSQL\17\bin\psql.exe"),
        (Join-Path $programFilesX86 "PostgreSQL\17\bin\psql.exe")
    )
    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }
    throw "PostgreSQL 17 is running, but psql.exe was not found in the standard PostgreSQL 17 installation paths."
}

function Invoke-PsqlStdin {
    param(
        [Parameter(Mandatory = $true)][string]$PsqlPath,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$AdminUser,
        [Parameter(Mandatory = $true)][string]$AdminPassword,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $PsqlPath
    $startInfo.Arguments = "-X -w -h 127.0.0.1 -p $Port -U $AdminUser -d postgres -v ON_ERROR_STOP=1"
    $startInfo.WorkingDirectory = Split-Path -Parent $PsqlPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables["PGPASSWORD"] = $AdminPassword

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "PostgreSQL test provisioning process could not be started."
        }
        $process.StandardInput.WriteLine($Sql)
        $process.StandardInput.Close()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $null = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "PostgreSQL rejected a disposable test setup operation. Database command output is intentionally suppressed."
        }
        return $stdout
    }
    finally {
        $process.Dispose()
        $startInfo.EnvironmentVariables.Remove("PGPASSWORD")
    }
}

function New-DisposableTestDatabase {
    $psql = Find-PostgreSql17
    $adminUser = Read-Host "Local PostgreSQL administrator username [postgres]"
    if ([string]::IsNullOrWhiteSpace($adminUser)) {
        $adminUser = "postgres"
    }
    if ($adminUser -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') {
        throw "PostgreSQL administrator username must use letters, digits and underscores only."
    }

    $portText = Read-Host "Local PostgreSQL port [5432]"
    $port = 5432
    if (-not [string]::IsNullOrWhiteSpace($portText)) {
        if (-not [int]::TryParse($portText, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
            throw "Enter a valid PostgreSQL TCP port between 1 and 65535."
        }
    }

    $secureAdminPassword = Read-Host "Local PostgreSQL administrator password (input is hidden)" -AsSecureString
    $adminPassword = $null
    $appPassword = $null
    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 10)
    $databaseName = "gamenet_test_$suffix"
    $roleName = "gamenet_test_app_$suffix"
    $randomBytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($randomBytes)
        $appPassword = [BitConverter]::ToString($randomBytes).Replace("-", "").ToLowerInvariant()
    }
    finally {
        [Array]::Clear($randomBytes, 0, $randomBytes.Length)
        $rng.Dispose()
    }

    try {
        $adminPassword = ConvertTo-PlainText $secureAdminPassword
        $null = Invoke-PsqlStdin -PsqlPath $psql -Port $port -AdminUser $adminUser -AdminPassword $adminPassword -Sql "SELECT 1;"
        $null = Invoke-PsqlStdin -PsqlPath $psql -Port $port -AdminUser $adminUser -AdminPassword $adminPassword -Sql "CREATE ROLE $roleName LOGIN PASSWORD '$appPassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;"
        try {
            $null = Invoke-PsqlStdin -PsqlPath $psql -Port $port -AdminUser $adminUser -AdminPassword $adminPassword -Sql "CREATE DATABASE $databaseName OWNER $roleName TEMPLATE template0 ENCODING 'UTF8';"
        }
        catch {
            try { $null = Invoke-PsqlStdin -PsqlPath $psql -Port $port -AdminUser $adminUser -AdminPassword $adminPassword -Sql "DROP ROLE IF EXISTS $roleName;" } catch { }
            throw
        }

        return [pscustomobject]@{
            DatabaseName = $databaseName
            RoleName = $roleName
            ConnectionString = "Host=127.0.0.1;Port=$port;Database=$databaseName;Username=$roleName;Password=$appPassword;Pooling=false;SSL Mode=Disable;Timeout=10;Command Timeout=30"
        }
    }
    finally {
        $secureAdminPassword.Dispose()
        $adminPassword = $null
        $appPassword = $null
    }
}

function Register-TestService {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$Account
    )
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        throw "Service executable is missing: $Executable"
    }
    $serviceArguments = @(
        "create",
        $Name,
        ('binPath= "' + $Executable + '"'),
        "start= demand",
        ('obj= "' + $Account + '"'),
        ('DisplayName= "' + $Name + '"')
    )
    Invoke-CheckedNative -FilePath (Join-Path $env:SystemRoot "System32\sc.exe") -Arguments $serviceArguments -FailureMessage "Windows could not register the test service '$Name'."
    Invoke-CheckedNative -FilePath (Join-Path $env:SystemRoot "System32\sc.exe") -Arguments @("sidtype", $Name, "unrestricted") -FailureMessage "Could not enable the dedicated service SID for '$Name'."
}

function Invoke-TestSecretProvisioning {
    param(
        [Parameter(Mandatory = $true)][string]$ServerExecutable,
        [Parameter(Mandatory = $true)][string]$ConnectionString
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ServerExecutable
    $startInfo.Arguments = "--provision-test-secrets-stdin"
    $startInfo.WorkingDirectory = Split-Path -Parent $ServerExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "The bundled Server secret provisioner could not be started."
        }
        $process.StandardInput.WriteLine($ConnectionString)
        $process.StandardInput.Close()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $null = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "Server refused the disposable test database or did not create its protected secret store. The connection string and raw error output were withheld."
        }
        foreach ($line in ($stdout -split "\r?\n")) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { Write-Host $line }
        }
    }
    finally {
        $process.Dispose()
    }
}

function Wait-ForServerReady {
    param([Parameter(Mandatory = $true)][string]$BaseUrl)
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        try {
            $response = Invoke-RestMethod -Method Get -Uri "$BaseUrl/health" -TimeoutSec 4
            if ($response.data.readiness -eq "Ready") {
                Write-Host "Server health check: Ready"
                return
            }
        }
        catch {
            # Bounded startup retry only; certificate validation stays enabled.
        }
        Start-Sleep -Seconds 2
    }
    throw "The Server did not report Ready over validated HTTPS within 90 seconds. Keep the VM isolated and inspect its service state."
}

function Invoke-ManagerSetup {
    Write-Host ""
    Write-Host "TEMPORARY TEST SETUP — MANAGER / SERVER"
    Write-Host "Use only a new isolated Windows test PC/VM. PostgreSQL 17 must already be installed and running on this machine."
    $confirmation = Read-Host "Type exactly ISOLATED TEST MACHINE to continue"
    if ($confirmation -cne "ISOLATED TEST MACHINE") {
        throw "Isolation confirmation was not entered. No install steps were started."
    }

    $firewallDisplayName = "GameNet 5 Test Server HTTPS 5080"
    if (Get-NetFirewallRule -DisplayName $firewallDisplayName -ErrorAction SilentlyContinue) {
        throw "The test HTTPS firewall rule already exists. Setup refuses to modify existing firewall state."
    }

    $serverIp = Get-LocalServerIPv4
    $database = New-DisposableTestDatabase
    $serverServiceCreated = $false
    $baseUrl = "https://" + $serverIp + ":" + $ServerPort
    $serverExe = Join-Path $InstallRoot "Server\GameNet.Server.exe"
    $publicCertificate = Join-Path (Join-Path $InstallRoot "artifacts") "gamenet-server.cer"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $publicCertificate) | Out-Null

    try {
        Register-TestService -Name $ServerServiceName -Executable $serverExe -Account $ServerServiceAccount
        $serverServiceCreated = $true

        Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "configure-server-tls.ps1") -Arguments @(
            "-ServerIp", $serverIp,
            "-DnsName", "gamenet.local",
            "-HttpsPort", "$ServerPort",
            "-ServiceAccount", $ServerServiceAccount,
            "-PublicCertificatePath", $publicCertificate
        )

        Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "write-protected-server-settings.ps1") -Arguments @(
            "-ServiceAccount", $ServerServiceAccount,
            "-PublicCertificatePath", $publicCertificate
        )

        Invoke-TestSecretProvisioning -ServerExecutable $serverExe -ConnectionString $database.ConnectionString

        $oldAspNet = [Environment]::GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Process")
        $oldDotNet = [Environment]::GetEnvironmentVariable("DOTNET_ENVIRONMENT", "Process")
        try {
            $env:ASPNETCORE_ENVIRONMENT = "Production"
            $env:DOTNET_ENVIRONMENT = "Production"
            Push-Location (Split-Path -Parent $serverExe)
            try {
                Invoke-CheckedNative -FilePath $serverExe -Arguments @("--migrate-only") -FailureMessage "Disposable test database migration failed; the new test database is retained for inspection."
            }
            finally {
                Pop-Location
            }
        }
        finally {
            if ([string]::IsNullOrEmpty($oldAspNet)) { Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue } else { $env:ASPNETCORE_ENVIRONMENT = $oldAspNet }
            if ([string]::IsNullOrEmpty($oldDotNet)) { Remove-Item Env:DOTNET_ENVIRONMENT -ErrorAction SilentlyContinue } else { $env:DOTNET_ENVIRONMENT = $oldDotNet }
        }

        New-NetFirewallRule -DisplayName $firewallDisplayName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $ServerPort -RemoteAddress LocalSubnet -Profile Any | Out-Null
        Invoke-CheckedNative -FilePath (Join-Path $env:SystemRoot "System32\sc.exe") -Arguments @("config", $ServerServiceName, "start= delayed-auto") -FailureMessage "Could not set the test Server service startup mode."
        Start-Service -Name $ServerServiceName
        Wait-ForServerReady -BaseUrl $baseUrl

        Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "configure-runtime-endpoint.ps1") -Arguments @(
            "-ServerBaseUrl", $baseUrl,
            "-Component", "Desktop"
        )

        Write-Host ""
        Write-Host "Create the initial Owner account. Choose a bootstrap secret of at least 32 characters and remember it for this setup step."
        Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "bootstrap-admin.ps1") -Arguments @("-ServerBaseUrl", $baseUrl)

        Write-Host ""
        Write-Host "Open Start Menu > GameNet Manager (TEST) and sign in with that Owner account."
        Write-Host "After sign-in, return here and press Enter. If sign-in fails, stop and revert the isolated VM snapshot."
        [void](Read-Host "Press Enter only after a successful Owner sign-in")

        Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "finalize-server-setup.ps1") -Arguments @("-ServerBaseUrl", $baseUrl)
        Restart-Service -Name $ServerServiceName -Force
        Wait-ForServerReady -BaseUrl $baseUrl

        $certSha = (Get-FileHash -LiteralPath $publicCertificate -Algorithm SHA256).Hash.ToUpperInvariant()
        Write-Host ""
        Write-Host "MANAGER TEST SETUP COMPLETE"
        Write-Host ("Server URL: {0}" -f $baseUrl)
        Write-Host ("Public certificate to copy to the isolated client PC: {0}" -f $publicCertificate)
        Write-Host ("Certificate SHA-256 fingerprint (verify independently on the client): {0}" -f $certSha)
        Write-Host ("New disposable local database: {0}" -f $database.DatabaseName)
        Write-Host "No database password, connection string, signing key or Agent provisioning key was intentionally printed."
        Write-Host "Next: run this Setup.exe on the dedicated isolated Agent PC and choose Agent only. Then create a PC Station and tariff and bind this Agent in Desktop before testing cash checkout."
    }
    catch {
        if ($serverServiceCreated) {
            Write-Warning "A test Server service or protected test artifacts may already exist. Setup will not delete them automatically. Keep the machine isolated and revert its VM snapshot after retaining any needed evidence."
        }
        throw
    }
    finally {
        if ($null -ne $database) { $database.ConnectionString = $null }
    }
}

function Invoke-AgentSetup {
    Write-Host ""
    Write-Host "TEMPORARY TEST SETUP — AGENT CLIENT"
    Write-Host "Use only a dedicated isolated PC/VM. The Manager/Server must already be running and Ready."
    $confirmation = Read-Host "Type exactly ISOLATED TEST MACHINE to continue"
    if ($confirmation -cne "ISOLATED TEST MACHINE") {
        throw "Isolation confirmation was not entered. No install steps were started."
    }

    $baseUrlText = Read-RequiredText "Server HTTPS URL shown by Manager Setup (example: https://192.168.0.9:5080)"
    $serverUri = $null
    if (-not [Uri]::TryCreate($baseUrlText, [UriKind]::Absolute, [ref]$serverUri) -or
        $serverUri.Scheme -ne [Uri]::UriSchemeHttps -or
        $serverUri.IsLoopback -or
        -not [string]::IsNullOrEmpty($serverUri.UserInfo) -or
        $serverUri.AbsolutePath -notin @("", "/") -or
        -not [string]::IsNullOrEmpty($serverUri.Query) -or
        -not [string]::IsNullOrEmpty($serverUri.Fragment)) {
        throw "Enter the Manager's exact HTTPS origin. Network HTTP, loopback and URLs with credentials or paths are not accepted."
    }
    $baseUrl = $serverUri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")

    $certificatePath = Read-RequiredText "Path to the copied public server-trust .cer file"
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        throw "The public server certificate file could not be found."
    }
    $fingerprint = Read-RequiredText "Paste the 64-character SHA-256 certificate fingerprint displayed by Manager Setup"
    Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "trust-server-certificate.ps1") -Arguments @(
        "-CertificatePath", $certificatePath,
        "-ExpectedSha256Fingerprint", $fingerprint
    )

    $deviceId = Read-RequiredText "Stable DeviceId for this test PC (for example station-pc-01)"
    if ($deviceId -notmatch '^[A-Za-z0-9._-]{1,128}$') {
        throw "DeviceId must contain only ASCII letters, digits, dots, dashes or underscores."
    }

    Register-TestService -Name $AgentServiceName -Executable (Join-Path $InstallRoot "Agent\GameNet.Agent.exe") -Account $AgentServiceAccount
    Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "configure-runtime-endpoint.ps1") -Arguments @(
        "-ServerBaseUrl", $baseUrl,
        "-Component", "Agent",
        "-ServiceAccount", $AgentServiceAccount
    )
    Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "configure-agent-runtime-state.ps1")
    Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "provision-agent-enrollment.ps1") -Arguments @("-DeviceId", $deviceId)

    Invoke-CheckedNative -FilePath (Join-Path $env:SystemRoot "System32\sc.exe") -Arguments @("config", $AgentServiceName, "start= delayed-auto") -FailureMessage "Could not set the test Agent service startup mode."
    Start-Service -Name $AgentServiceName
    Start-Sleep -Seconds 15
    $service = Get-Service -Name $AgentServiceName
    if ($service.Status -ne "Running") {
        throw "The Agent service did not remain Running. Do not disable TLS validation or delete protected Agent state; inspect logs and revert the isolated VM snapshot if needed."
    }

    Invoke-PowerShellScript -Path (Join-Path $ScriptsRoot "test-runtime-endpoint.ps1") -Arguments @("-Component", "Agent")
    Write-Host ""
    Write-Host "AGENT TEST SETUP COMPLETE"
    Write-Host ("DeviceId: {0}" -f $deviceId)
    Write-Host "In Manager Desktop, bind this DeviceId to its test PC Station and verify Agent is online/leased before starting a Session."
    Write-Host "This does not prove OS lock/unlock enforcement or production readiness."
}

try {
    Assert-ElevatedAdministrator
    Assert-NoExistingGameNetState

    if ($Mode -eq "Manager") {
        if (-not (Test-Path -LiteralPath (Join-Path $InstallRoot "Server\GameNet.Server.exe") -PathType Leaf) -or
            -not (Test-Path -LiteralPath (Join-Path $InstallRoot "Desktop\GameNet.Manager.Desktop.exe") -PathType Leaf)) {
            throw "The Manager package does not contain both Server and Desktop components."
        }
        Invoke-ManagerSetup
    }
    else {
        if (-not (Test-Path -LiteralPath (Join-Path $InstallRoot "Agent\GameNet.Agent.exe") -PathType Leaf)) {
            throw "The Agent package does not contain the Agent component."
        }
        Invoke-AgentSetup
    }

    Write-Host ""
    Write-Host "Temporary Setup finished its automated steps. This package is not a production release."
}
catch {
    Write-Host ""
    Write-Host "TEMPORARY TEST SETUP STOPPED SAFELY" -ForegroundColor Red
    Write-Host $_.Exception.Message
    Write-Host "Do not point this build at a live GameNet database or Manager PC. Revert the isolated test VM snapshot if setup changed any state."
    try { [void](Read-Host "Press Enter to close this window") } catch { }
    exit 1
}
