param([string]$BaseUrl = 'http://localhost:5080')
$ErrorActionPreference = 'Stop'

# Execute uma vez para criar os mesmos três Pix do GIF.
# Cada requisição grava um Pix + seu evento em uma transação própria.
$examples = @(
    @{ Label = 'Pix #001'; AmountInCents = 2590 },
    @{ Label = 'Pix #002'; AmountInCents = 4200 },
    @{ Label = 'Pix #003'; AmountInCents = 10000 }
)
foreach ($example in $examples) {
    $body = @{ amountInCents = $example.AmountInCents } | ConvertTo-Json
    $pix = Invoke-RestMethod "$BaseUrl/pix" -Method Post -ContentType 'application/json' -Body $body
    [pscustomobject]@{ Exemplo = $example.Label; PixId = $pix.id; ValorEmCentavos = $pix.amountInCents }
}
Write-Host 'Outbox atual (um evento distinto para cada Pix):'
Invoke-RestMethod "$BaseUrl/outbox" | Format-Table sequence,eventId,aggregateId,publishedAtUtc
