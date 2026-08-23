param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Get-ConfigValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Profile,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [bool]$Required = $true
    )

    $property = $Profile.PSObject.Properties[$Name]
    if ($null -eq $property) {
        if ($Required) { throw "AI provider profile is missing '$Name'." }
        return ''
    }

    $value = [string]$property.Value
    if ($Required -and [string]::IsNullOrWhiteSpace($value)) {
        throw "AI provider profile has empty '$Name'."
    }

    return $value
}

function Encode-Secret {
    param([string]$Value)

    [byte[]]$key = 0x42,0x1A,0x7C,0xE3,0x8F,0x55,0xC9,0xD0,0x3B,0x96,0x64,0xAF,0x2D,0xF8,0x17,0x8A
    [byte[]]$bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = $bytes[$i] -bxor $key[$i % $key.Length]
    }

    if ($bytes.Length -eq 0) { return '' }
    return ($bytes | ForEach-Object { '0x{0:X2}' -f $_ }) -join ', '
}

function To-ArrayLine {
    param(
        [string]$FieldName,
        [string]$Value
    )

    $encoded = Encode-Secret $Value
    if ([string]::IsNullOrWhiteSpace($encoded)) {
        return "    private static readonly byte[] $FieldName = System.Array.Empty<byte>();"
    }

    return "    private static readonly byte[] $FieldName = { $encoded };"
}

if (!(Test-Path -LiteralPath $ConfigPath)) {
    $examplePath = Join-Path (Split-Path -Parent $ConfigPath) 'ai-provider.local.example.json'
    if (Test-Path -LiteralPath $examplePath) {
        $ConfigPath = $examplePath
    }
    else {
        throw "Cannot find AI provider config: $ConfigPath"
    }
}

$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$activeProfile = [string]$config.ActiveProfile
if ([string]::IsNullOrWhiteSpace($activeProfile)) {
    throw "AI provider config must declare ActiveProfile."
}

$profileProperty = $config.Profiles.PSObject.Properties[$activeProfile]
if ($null -eq $profileProperty) {
    throw "AI provider profile '$activeProfile' was not found."
}

$profile = $profileProperty.Value
$provider = Get-ConfigValue $profile 'Provider'
$url = Get-ConfigValue $profile 'Url'
$apiKey = Get-ConfigValue $profile 'ApiKey' $false
$model = Get-ConfigValue $profile 'Model'

$outputDir = Split-Path -Parent $OutputPath
if (![string]::IsNullOrWhiteSpace($outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$providerLine = To-ArrayLine '_provider' $provider
$urlLine = To-ArrayLine '_url' $url
$apiKeyLine = To-ArrayLine '_apiKey' $apiKey
$modelLine = To-ArrayLine '_model' $model

$source = @"
#nullable enable
using System.Runtime.CompilerServices;

namespace OCR.Business.Security;

internal static class GeneratedAiProviderSecret
{
$providerLine
$urlLine
$apiKeyLine
$modelLine

    private static string? _providerValue, _urlValue, _apiKeyValue, _modelValue;

    internal static string Provider => _providerValue ??= Decode(_provider);
    internal static string Url => _urlValue ??= Decode(_url);
    internal static string ApiKey => _apiKeyValue ??= Decode(_apiKey);
    internal static string Model => _modelValue ??= Decode(_model);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Decode(byte[] encoded) => SecretHelper.Decode(encoded);
}
"@

[System.IO.File]::WriteAllText($OutputPath, $source, [System.Text.UTF8Encoding]::new($false))
