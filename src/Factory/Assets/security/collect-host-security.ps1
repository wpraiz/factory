<#
.SYNOPSIS
    Coletor da secao SEGURANCA do Factory (postura do Windows, Defender/ASR, persistencia, portas, HardeningKitty).
    So LE o sistema. Saida: um JSON compacto no stdout. O Factory chama com
    `powershell -NoProfile -ExecutionPolicy Bypass -File Assets\security\collect-host-security.ps1` e guarda 60 s em memoria.
.NOTES
    Rodando sem admin (o normal do Factory), BitLocker e parte do Defender podem vir vazios: cada sonda devolve
    o motivo em `erros.<sonda>` em vez de inventar valor — a tela mostra "sem leitura" (regra: nao inventar estado).
    Base: guia de implementacao "odin host security", adaptado pro WinUI (sem servidor HTTP).
#>
[CmdletBinding()]
param(
    [int]$Horas = 24,
    # Pasta com os CSV do HardeningKitty (Invoke-HardeningKitty -Report -ReportFile ...). Vazio = procura nos lugares de sempre.
    [string]$HardeningReportDir = ''
)

$ErrorActionPreference = 'Stop'
# Pipe do PowerShell 5.1 sai em OEM 850 por padrao: caminho com acento chegava quebrado no C#.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$erros = [ordered]@{}
function Tenta([string]$nome, [scriptblock]$bloco) {
    try { & $bloco } catch { $erros[$nome] = ($_.Exception.Message -split "`r?`n")[0]; $null }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

# ---------- 1. Postura ----------
$bitlocker = Tenta 'bitlocker' {
    # Get-BitLockerVolume exige admin; sem admin, o Shell expoe o estado pela propriedade System.Volume.BitLockerProtection.
    if ($isAdmin) {
        $v = Get-BitLockerVolume -MountPoint 'C:'
        [pscustomobject]@{ status = "$($v.VolumeStatus)"; protecao = "$($v.ProtectionStatus)"; fonte = 'Get-BitLockerVolume' }
    } else {
        $p = (New-Object -ComObject Shell.Application).NameSpace('C:').Self.ExtendedProperty('System.Volume.BitLockerProtection')
        # 1 = ligado, 2 = desligado, 3 = suspenso, 5 = ligado e destravado, 6 = desligado (criptografando?) — doc do Shell
        $txt = switch ($p) { 1 { 'On' } 3 { 'Suspended' } 5 { 'On' } 2 { 'Off' } 6 { 'Off' } $null { 'Unknown' } default { "Code$p" } }
        [pscustomobject]@{ status = $txt; protecao = $txt; fonte = 'Shell (sem admin)' }
    }
}

$deviceGuard = Tenta 'hvci' {
    Get-CimInstance -ClassName Win32_DeviceGuard -Namespace 'root\Microsoft\Windows\DeviceGuard'
}
$hvci = if ($deviceGuard) { @($deviceGuard.SecurityServicesRunning) -contains 2 } else { $null }
$credGuard = if ($deviceGuard) { @($deviceGuard.SecurityServicesRunning) -contains 1 } else { $null }

$mp = Tenta 'defender' { Get-MpPreference }
$mpStatus = Tenta 'defenderStatus' { Get-MpComputerStatus }
$asr = @()
if ($mp -and $mp.AttackSurfaceReductionRules_Ids) {
    $ids = @($mp.AttackSurfaceReductionRules_Ids); $acts = @($mp.AttackSurfaceReductionRules_Actions)
    for ($i = 0; $i -lt $ids.Count; $i++) {
        # 0 desligada, 1 bloqueio, 2 auditoria, 6 avisar
        $asr += [pscustomobject]@{ id = "$($ids[$i])".ToLower(); acao = [int]($acts[$i]) }
    }
}
$asrLeitura = if ($mp -and "$($mp.AttackSurfaceReductionRules_Ids)" -match 'administrator') { $false } else { [bool]$mp }

$firewall = Tenta 'firewall' {
    Get-NetFirewallProfile | ForEach-Object { [pscustomobject]@{ perfil = $_.Name; ligado = [bool]($_.Enabled -eq 'True' -or $_.Enabled -eq 1) } }
}

$secureBoot = Tenta 'secureBoot' { Confirm-SecureBootUEFI }

# ---------- 2. Portas em escuta (fora de loopback) ----------
$procs = @{}
Tenta 'processos' { Get-Process | ForEach-Object { $procs[$_.Id] = $_ } } | Out-Null
$portas = Tenta 'portas' {
    $tcp = Get-NetTCPConnection -State Listen | ForEach-Object { $_ | Add-Member -NotePropertyName Proto -NotePropertyValue 'TCP' -PassThru }
    $udp = Get-NetUDPEndpoint | ForEach-Object { $_ | Add-Member -NotePropertyName Proto -NotePropertyValue 'UDP' -PassThru }
    @($tcp) + @($udp) |
        Where-Object { $_.LocalAddress -notmatch '^(127\.|::1$)' } |
        Sort-Object Proto, LocalPort, OwningProcess -Unique |
        ForEach-Object {
            $p = $procs[[int]$_.OwningProcess]
            [pscustomobject]@{
                proto    = $_.Proto
                endereco = "$($_.LocalAddress)"
                porta    = [int]$_.LocalPort
                pid      = [int]$_.OwningProcess
                processo = if ($p) { $p.ProcessName } else { '?' }
                caminho  = if ($p -and $p.Path) { $p.Path } else { '' }
            }
        }
}

# ---------- 3. Persistencia: tarefas agendadas + chaves Run + pasta Inicializar ----------
$persist = @()
$tarefas = Tenta 'tarefas' {
    Get-ScheduledTask | Where-Object {
        $_.State -ne 'Disabled' -and (
            $_.TaskPath -eq '\' -or
            (@($_.Actions | ForEach-Object { $_.Execute }) -join ' ') -match '(?i)(powershell|pwsh|cmd|cscript|wscript|mshta|rundll32|regsvr32|\\users\\|\\temp\\|appdata)')
    } | ForEach-Object {
        [pscustomobject]@{
            tipo  = 'tarefa'
            nome  = $_.TaskName
            onde  = $_.TaskPath
            acao  = (@($_.Actions | ForEach-Object { "$($_.Execute) $($_.Arguments)".Trim() }) -join ' ; ')
            autor = "$($_.Author)"
        }
    }
}
if ($tarefas) { $persist += $tarefas }

$chavesRun = @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'
)
foreach ($k in $chavesRun) {
    $r = Tenta "run:$k" {
        if (Test-Path $k) {
            $item = Get-ItemProperty -Path $k
            $item.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS(Path|ParentPath|ChildName|Drive|Provider)$' } | ForEach-Object {
                [pscustomobject]@{ tipo = 'run'; nome = $_.Name; onde = ($k -replace ':', ''); acao = "$($_.Value)"; autor = '' }
            }
        }
    }
    if ($r) { $persist += $r }
}
foreach ($pasta in @([Environment]::GetFolderPath('Startup'), [Environment]::GetFolderPath('CommonStartup'))) {
    $r = Tenta 'startup' {
        if ($pasta -and (Test-Path $pasta)) {
            Get-ChildItem -Path $pasta -File | Where-Object { $_.Name -ne 'desktop.ini' } | ForEach-Object {
                [pscustomobject]@{ tipo = 'inicializar'; nome = $_.BaseName; onde = $pasta; acao = $_.Name; autor = '' }
            }
        }
    }
    if ($r) { $persist += $r }
}

# ---------- 3b. Triagem (guia Autoruns/Process Explorer): o arquivo ainda existe? tem assinatura digital valida? ----------
# Linha amarela do Autoruns = "File Not Found" (residuo de desinstalacao). Rosa = "Unverified Signer".
$win = $env:windir
$assinCache = @{}
# cache de assinaturas entre leituras (e cache: regenera; chave = caminho + data de gravacao do arquivo)
$cacheFile = Join-Path $env:LOCALAPPDATA 'Factory\assinaturas.json'
$disco = @{}
try { if (Test-Path $cacheFile) { (Get-Content $cacheFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $disco[$_.Name] = $_.Value } } } catch { }
$discoMudou = $false
function Triagem([string]$exe) {
    if (-not $exe) { return $null }
    # "wscript.exe", "msg.exe": sem pasta -> procura no System32 e no PATH antes de dizer que sumiu
    if (-not [IO.Path]::IsPathRooted($exe)) {
        $achou = Join-Path (Join-Path $win 'System32') $exe
        if (-not (Test-Path -LiteralPath $achou)) { $cmd = Get-Command $exe -ErrorAction SilentlyContinue | Select-Object -First 1; $achou = if ($cmd) { $cmd.Source } else { $exe } }
        $exe = $achou
    }
    if ($assinCache.ContainsKey($exe)) { return $assinCache[$exe] }
    $r = [pscustomobject]@{ existe = $false; assinatura = ''; assinante = '' }
    if (Test-Path -LiteralPath $exe -PathType Leaf) {
        $r.existe = $true
        # dentro do Windows nao gasta tempo assinando: e o proprio sistema
        if (-not $exe.StartsWith($win, [StringComparison]::OrdinalIgnoreCase)) {
            $chave = "$exe|$((Get-Item -LiteralPath $exe).LastWriteTimeUtc.Ticks)"
            if ($disco.ContainsKey($chave)) { $r.assinatura = $disco[$chave].a; $r.assinante = $disco[$chave].s }
            else {
                $sig = Get-AuthenticodeSignature -LiteralPath $exe -ErrorAction SilentlyContinue
                if ($sig) {
                    $r.assinatura = "$($sig.Status)"
                    if ($sig.SignerCertificate) { $r.assinante = ((($sig.SignerCertificate.Subject -split ',(?=(?:[^"]*"[^"]*")*[^"]*$)') | Where-Object { $_ -match '^\s*CN=' } | Select-Object -First 1) -replace '^\s*CN=', '').Trim('" ') }
                    $disco[$chave] = [pscustomobject]@{ a = $r.assinatura; s = $r.assinante }
                    $script:discoMudou = $true
                }
            }
        }
    }
    $assinCache[$exe] = $r
    $r
}
function ExeDe([string]$linha) {
    $a = $linha.Trim()
    if ($a.StartsWith('"')) { $f = $a.IndexOf('"', 1); $e = if ($f -gt 0) { $a.Substring(1, $f - 1) } else { $a.Trim('"') } }
    else { $i = $a.IndexOf('.exe', [StringComparison]::OrdinalIgnoreCase); $e = if ($i -gt 0) { $a.Substring(0, $i + 4) } else { ($a -split ' ')[0] } }
    [Environment]::ExpandEnvironmentVariables($e)
}
foreach ($p in $persist) {
    $t = if ($p.tipo -eq 'inicializar') { $null } else { Triagem (ExeDe $p.acao) }
    $p | Add-Member -NotePropertyName existe -NotePropertyValue $(if ($t) { $t.existe } else { $null }) -Force
    $p | Add-Member -NotePropertyName assinatura -NotePropertyValue $(if ($t) { $t.assinatura } else { '' }) -Force
    $p | Add-Member -NotePropertyName assinante -NotePropertyValue $(if ($t) { $t.assinante } else { '' }) -Force
}
foreach ($p in @($portas)) {
    $t = if ($p.caminho) { Triagem $p.caminho } else { $null }
    $p | Add-Member -NotePropertyName assinatura -NotePropertyValue $(if ($t) { $t.assinatura } else { '' }) -Force
    $p | Add-Member -NotePropertyName assinante -NotePropertyValue $(if ($t) { $t.assinante } else { '' }) -Force
}

if ($discoMudou) { try { New-Item -ItemType Directory -Force (Split-Path $cacheFile) | Out-Null; $disco | ConvertTo-Json -Depth 3 -Compress | Set-Content -LiteralPath $cacheFile -Encoding UTF8 } catch { } }

# ---------- 4. Defender: deteccoes e ASR nas ultimas N horas ----------
$eventos = Tenta 'eventos' {
    $tipos = @{ 1116 = 'ameaca_detectada'; 1117 = 'ameaca_bloqueada'; 1121 = 'asr_bloqueou'; 1122 = 'asr_auditoria' }
    Get-WinEvent -FilterHashtable @{
        LogName   = 'Microsoft-Windows-Windows Defender/Operational'
        Id        = 1116, 1117, 1121, 1122
        StartTime = (Get-Date).AddHours(-$Horas)
    } -MaxEvents 100 -ErrorAction Stop | ForEach-Object {
        $x = [xml]$_.ToXml()
        $d = @{}; foreach ($n in $x.Event.EventData.Data) { $d[$n.Name] = "$($n.'#text')" }
        $alvo = if ($d['Process Name']) { $d['Process Name'] } elseif ($d['Path']) { $d['Path'] } else { '' }
        [pscustomobject]@{
            eventId  = $_.Id
            quando   = $_.TimeCreated.ToString('o')
            tipo     = $tipos[$_.Id]
            ameaca   = "$($d['Threat Name'])"
            processo = "$alvo"
            regra    = "$($d['ID'])".ToLower()
            caminho  = "$($d['Path'])"
        }
    }
}
# "Nenhum evento encontrado" e resposta valida (lista vazia), nao erro.
if ($erros['eventos'] -match 'No events were found|Nenhum evento') { $erros.Remove('eventos'); $eventos = @() }

# ---------- 5. HardeningKitty: ultimo CSV de relatorio ----------
$hk = $null
$hkModulo = [bool](Get-Module -ListAvailable -Name HardeningKitty -ErrorAction SilentlyContinue)
# Historico do score e dado do usuario: fica em <FACTORY_DATA>\factory\hardening (fora do repo, onde o backup pega).
$factoryRoot = if ($env:FACTORY_ROOT) { $env:FACTORY_ROOT } else { Join-Path $HOME 'Projects' }
$factoryData = if ($env:FACTORY_DATA) { $env:FACTORY_DATA } else { Join-Path $factoryRoot '.factory' }
$dirs = @($HardeningReportDir, (Join-Path $factoryData 'factory\hardening')) | Where-Object { $_ -and (Test-Path $_) }
$csv = $dirs | ForEach-Object { Get-ChildItem -Path $_ -Filter '*.csv' -File } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($csv) {
    $hk = Tenta 'hardening' {
        $linhas = Import-Csv -Path $csv.FullName
        $ok = @($linhas | Where-Object { $_.TestResult -eq 'Passed' }).Count
        $falhas = @($linhas | Where-Object { $_.TestResult -eq 'Failed' })
        $baixo = @($falhas | Where-Object { $_.Severity -eq 'Low' }).Count
        $medio = @($falhas | Where-Object { $_.Severity -eq 'Medium' }).Count
        $alto = @($falhas | Where-Object { $_.Severity -eq 'High' }).Count
        $total = $ok + $baixo + $medio + $alto
        # Mesma conta do HardeningKitty: (passou*4 + baixo*2 + medio) / (total*4) * 5 + 1  -> escala 1..6
        $score = if ($total) { [math]::Round((($ok * 4 + $baixo * 2 + $medio) / ($total * 4)) * 5 + 1, 2) } else { 0 }
        [pscustomobject]@{
            arquivo = $csv.FullName
            quando  = $csv.LastWriteTime.ToString('o')
            score   = $score
            total   = $total
            passou  = $ok
            baixo   = $baixo
            medio   = $medio
            alto    = $alto
            altos   = @($falhas | Where-Object { $_.Severity -eq 'High' } | Select-Object -First 30 | ForEach-Object {
                [pscustomobject]@{ id = $_.ID; categoria = $_.Category; nome = $_.Name; atual = $_.Result; recomendado = $_.Recommended }
            })
        }
    }
}

# ---------- payload ----------
[pscustomobject]@{
    versao   = 'host-security/v1'
    quando   = (Get-Date).ToString('o')
    maquina  = $env:COMPUTERNAME
    admin    = $isAdmin
    postura  = [pscustomobject]@{
        bitlocker      = $bitlocker
        hvci           = $hvci
        credentialGuard = $credGuard
        secureBoot     = $secureBoot
        defenderLigado = if ($mpStatus) { [bool]$mpStatus.RealTimeProtectionEnabled } else { $null }
        assinaturas    = if ($mpStatus) { $mpStatus.AntivirusSignatureLastUpdated.ToString('o') } else { $null }
        tamperProtection = if ($mpStatus) { [bool]$mpStatus.IsTamperProtected } else { $null }
        asrLeitura     = $asrLeitura
        asr            = @($asr)
        firewall       = @($firewall)
    }
    portas       = @($portas)
    persistencia = @($persist)
    eventos      = @($eventos)
    hardening    = $hk
    hardeningKittyInstalado = $hkModulo
    erros        = $erros
} | ConvertTo-Json -Depth 6 -Compress
