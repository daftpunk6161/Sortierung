# Untrusted demo plugin – exists solely so the trust-mode gate has something to skip.
param([hashtable]$Context, [scriptblock]$Log)
return [pscustomobject]@{ PluginHandled = $false; Summary = 'untrusted-demo no-op' }
