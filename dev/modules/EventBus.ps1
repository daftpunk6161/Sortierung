# ================================================================
#  EventBus.ps1 - Lightweight in-process observer/event bus
# ================================================================

function Get-RomEventBusState {
    $busVar = Get-Variable -Scope Script -Name RomEventBus -ErrorAction SilentlyContinue
    if ($busVar -and $busVar.Value) {
        return $busVar.Value
    }
    return $null
}

function Get-RomEventBusOrInitialize {
    $bus = Get-RomEventBusState
    if (-not $bus) {
        Initialize-RomEventBus
        $bus = Get-RomEventBusState
    }
    return $bus
}

function Get-RomEventBusSequenceValue {
    $seqVar = Get-Variable -Scope Script -Name RomEventBusSequence -ErrorAction SilentlyContinue
    if ($seqVar) {
        return [int]$seqVar.Value
    }
    return 0
}

function Initialize-RomEventBus {
    $script:RomEventBus = @{}
    $script:RomEventBusSequence = 0
}

function Register-RomEventSubscriber {
    param(
        [Parameter(Mandatory = $true)][string]$Topic,
        [Parameter(Mandatory = $true)][scriptblock]$Handler,
        [string]$SubscriptionId
    )

    if ([string]::IsNullOrWhiteSpace($Topic)) {
        throw 'Topic must not be empty.'
    }

    $bus = Get-RomEventBusOrInitialize

    $topicKey = [string]$Topic.Trim()
    if (-not $bus.ContainsKey($topicKey)) {
        $bus[$topicKey] = New-Object System.Collections.Generic.List[object]
    }

    if ([string]::IsNullOrWhiteSpace($SubscriptionId)) {
        $next = (Get-RomEventBusSequenceValue) + 1
        $script:RomEventBusSequence = [int]$next
        $SubscriptionId = ('sub-{0}' -f [int]$next)
    }

    $entry = [pscustomobject]@{
        Id = [string]$SubscriptionId
        Handler = $Handler
    }

    [void]$bus[$topicKey].Add($entry)
    return [string]$SubscriptionId
}

function Unregister-RomEventSubscriber {
    param(
        [Parameter(Mandatory = $true)][string]$SubscriptionId
    )

    if ([string]::IsNullOrWhiteSpace($SubscriptionId)) { return $false }
    $bus = Get-RomEventBusState
    if (-not $bus) { return $false }

    foreach ($topicKey in @($bus.Keys)) {
        $list = $bus[$topicKey]
        if (-not $list) { continue }

        for ($index = $list.Count - 1; $index -ge 0; $index--) {
            if ([string]$list[$index].Id -eq [string]$SubscriptionId) {
                $list.RemoveAt($index)
                return $true
            }
        }
    }

    return $false
}


function Publish-RomEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Topic,
        [hashtable]$Data = @{},
        [string]$Source = '',
        [switch]$ContinueOnError
    )

    if ([string]::IsNullOrWhiteSpace($Topic)) {
        throw 'Topic must not be empty.'
    }

    $bus = Get-RomEventBusState
    if (-not $bus) {
        return [pscustomobject]@{
            Topic = [string]$Topic
            Delivered = 0
            Failed = 0
            Errors = @()
        }
    }

    $payload = [pscustomobject]@{
        Topic = [string]$Topic
        Timestamp = (Get-Date).ToString('o')
        Source = [string]$Source
        Data = if ($Data) { $Data } else { @{} }
    }

    # Collect subscribers: exact match + wildcard matches
    $allSubscribers = New-Object System.Collections.Generic.List[object]

    # Exact topic match
    if ($bus.ContainsKey($Topic)) {
        $bucket = $bus[$Topic]
        if ($null -ne $bucket) {
            if ($bucket -is [System.Collections.Generic.List[object]]) {
                foreach ($sub in @($bucket.ToArray())) { [void]$allSubscribers.Add($sub) }
            } elseif ($bucket -is [System.Collections.IEnumerable] -and -not ($bucket -is [string])) {
                foreach ($sub in @($bucket)) { [void]$allSubscribers.Add($sub) }
            } else {
                [void]$allSubscribers.Add($bucket)
            }
        }
    }

    # Wildcard topic match (e.g. 'AppState.*' matches 'AppState.Changed')
    foreach ($topicKey in @($bus.Keys)) {
        if ($topicKey -eq $Topic) { continue }
        if (-not $topicKey.Contains('*')) { continue }
        $pattern = '^' + [regex]::Escape($topicKey).Replace('\*', '.*') + '$'
        if ($Topic -match $pattern) {
            $bucket = $bus[$topicKey]
            if ($null -ne $bucket) {
                if ($bucket -is [System.Collections.Generic.List[object]]) {
                    foreach ($sub in @($bucket.ToArray())) { [void]$allSubscribers.Add($sub) }
                } elseif ($bucket -is [System.Collections.IEnumerable] -and -not ($bucket -is [string])) {
                    foreach ($sub in @($bucket)) { [void]$allSubscribers.Add($sub) }
                } else {
                    [void]$allSubscribers.Add($bucket)
                }
            }
        }
    }

    $delivered = 0
    $errors = New-Object System.Collections.Generic.List[string]

    foreach ($subscriber in $allSubscribers) {
        if (-not $subscriber -or -not $subscriber.Handler) { continue }
        try {
            $handler = [scriptblock]$subscriber.Handler
            & $handler $payload
            $delivered++
        } catch {
            [void]$errors.Add([string]$_.Exception.Message)
            # BUG EVBUS-002 FIX: Always continue to remaining subscribers (never abort the loop)
        }
    }

    return [pscustomobject]@{
        Topic = [string]$Topic
        Delivered = [int]$delivered
        Failed = [int]$errors.Count
        Errors = @($errors)
    }
}
