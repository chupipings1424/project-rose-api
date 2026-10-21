<#
.SYNOPSIS
    Downloads the offline translation model for ROSE Lingo (NLLB-200 distilled 600M, ONNX, ~1 GB).

.DESCRIPTION
    Run this ONCE on a PC that can reach huggingface.co (for example at home). Afterwards ROSE Lingo
    translates fully offline. To use it on another laptop, copy the whole data\models folder into that
    laptop's project-rose-api\data\ folder - no internet needed there.

    Downloads are resumable: if the connection drops, just run the script again.
    Files are saved as *.part and only renamed when complete, so ROSE never sees half a model.

    Model licence: NLLB-200 is CC-BY-NC 4.0 (Meta AI) - fine for personal, non-commercial use.

.EXAMPLE
    .\setup-lingo.cmd
.EXAMPLE
    .\setup-lingo.cmd -Destination D:\rose-models\nllb-200-distilled-600M
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot 'data\models\nllb-200-distilled-600M'),
    [string]$BaseUrl = 'https://huggingface.co/Xenova/nllb-200-distilled-600M/resolve/main',
    [string]$SentencePieceUrl = 'https://huggingface.co/facebook/nllb-200-distilled-600M/resolve/main/sentencepiece.bpe.model'
)

$ErrorActionPreference = 'Stop'

function Get-ModelFile([string]$Url, [string]$Name) {
    $target = Join-Path $Destination $Name
    if (Test-Path -LiteralPath $target) {
        Write-Host "  [skip] $Name is already downloaded."
        return $true
    }
    $part = "$target.part"
    Write-Host "  [get ] $Name"
    # -C - resumes a previous .part; --fail turns HTTP errors (404, 403...) into a non-zero exit code.
    & curl.exe -L --fail --retry 3 --retry-delay 2 -C - --progress-bar -o $part $Url
    if ($LASTEXITCODE -ne 0) {
        # 22 = HTTP error: the file isn't there, so drop the empty .part. Anything else keeps it for resuming.
        if ($LASTEXITCODE -eq 22 -and (Test-Path -LiteralPath $part) -and (Get-Item -LiteralPath $part).Length -eq 0) {
            Remove-Item -LiteralPath $part
        }
        Write-Host "         (not downloaded - curl exit code $LASTEXITCODE)"
        return $false
    }
    Move-Item -LiteralPath $part -Destination $target -Force
    return $true
}

if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
    throw 'curl.exe was not found. It ships with Windows 10 (1803) and later.'
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Write-Host ''
Write-Host 'ROSE Lingo - offline translator setup'
Write-Host "Saving to $Destination"
Write-Host 'About 1 GB. If it stops halfway, run this again and it carries on.'
Write-Host ''

$ok = Get-ModelFile $SentencePieceUrl 'sentencepiece.bpe.model'
$ok = (Get-ModelFile "$BaseUrl/onnx/encoder_model_quantized.onnx" 'encoder_model_quantized.onnx') -and $ok

# Prefer the single merged decoder; older exports only have the two-file (first step + with past) layout.
$decoder = Get-ModelFile "$BaseUrl/onnx/decoder_model_merged_quantized.onnx" 'decoder_model_merged_quantized.onnx'
if (-not $decoder) {
    Write-Host '  Merged decoder not available - trying the two-file decoder instead.'
    $first = Get-ModelFile "$BaseUrl/onnx/decoder_model_quantized.onnx" 'decoder_model_quantized.onnx'
    $past = Get-ModelFile "$BaseUrl/onnx/decoder_with_past_model_quantized.onnx" 'decoder_with_past_model_quantized.onnx'
    $decoder = $first -and $past
}
$ok = $decoder -and $ok

Write-Host ''
if (-not $ok) {
    Write-Host 'Some files could not be downloaded. Check that huggingface.co opens in your browser'
    Write-Host '(company networks often block it - try at home), then run setup-lingo.cmd again.'
    exit 1
}

Write-Host 'Done! The offline translator is installed.'
Write-Host ' - Restart ROSE (start-rose.bat) and open ROSE Lingo.'
Write-Host ' - Another laptop: copy the data\models folder into its project-rose-api\data\ folder.'
exit 0