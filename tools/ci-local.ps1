# CI LOCAL do Factory. Quando o GitHub Actions nao esta disponivel, o CI roda AQUI com as mesmas etapas do
# .github/workflows/ci.yml e publica o resultado no GitHub como "commit status" (contexto ci-local), que aparece
# como check verde/vermelho no PR. A API de status nao depende do Actions.
# Uso:  pwsh -File tools\ci-local.ps1            (confere o HEAD contra origin/master e publica o status)
#       pwsh -File tools\ci-local.ps1 -SemPublicar
# Roda sozinho no pre-push (.githooks/pre-push). O repo (owner/nome) vem de GITHUB_REPOSITORY ou do remote origin.
param([switch]$SemPublicar, [string]$Base = 'origin/master')
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..')
$sha = (git rev-parse HEAD).Trim()
$repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else {
  $origin = (git remote get-url origin 2>$null | Out-String).Trim()
  if ($origin -match '[:/]([^/:]+/[^/]+?)(\.git)?$') { $Matches[1] } else { '' }
}
$t0 = Get-Date
$etapas = @()

function Etapa([string]$nome, [scriptblock]$cmd) {
  $ini = Get-Date
  $saida = & $cmd 2>&1 | Out-String
  $ok = $LASTEXITCODE -eq 0
  $script:etapas += [pscustomobject]@{ nome = $nome; ok = $ok; s = [math]::Round(((Get-Date) - $ini).TotalSeconds) }
  Write-Output ("{0} {1} ({2}s)" -f ($(if ($ok) { 'OK ' } else { 'XX ' })), $nome, [math]::Round(((Get-Date) - $ini).TotalSeconds))
  if (-not $ok) { Write-Output ($saida -split "`n" | Select-Object -Last 15) }
  return $ok
}

git fetch -q origin 2>$null
$ok1 = Etapa 'privacidade' { node tools/privacy-check.mjs --base $Base --ci }
$ok2 = Etapa 'build x64' { dotnet build src/Factory/Factory.csproj -c Debug -p:Platform=x64 -nologo -v q }
$tudoOk = $ok1 -and $ok2
$resumo = ($etapas | ForEach-Object { "$($_.nome) $(if ($_.ok) {'ok'} else {'falhou'}) $($_.s)s" }) -join ' | '
Write-Output "ci-local: $(if ($tudoOk) {'VERDE'} else {'VERMELHO'}) | $resumo"

if (-not $SemPublicar -and $repo) {
  $estado = if ($tudoOk) { 'success' } else { 'failure' }
  $desc = "local ($env:COMPUTERNAME): $resumo"
  if ($desc.Length -gt 140) { $desc = $desc.Substring(0, 140) }
  gh api "repos/$repo/statuses/$sha" -f state=$estado -f context=ci-local -f "description=$desc" --silent 2>&1 | Out-Null
  if ($LASTEXITCODE -eq 0) { Write-Output "status ci-local=$estado publicado no commit $($sha.Substring(0,7))" }
  else { Write-Output 'aviso: nao publiquei o status no GitHub (gh offline?)' }
}
if (-not $tudoOk) { exit 1 }
