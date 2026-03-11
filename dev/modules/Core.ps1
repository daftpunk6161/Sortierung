# Canonical language token set – single source of truth (ISO 639-1 subset)
$script:LANG_TOKENS = [System.Collections.Generic.HashSet[string]]::new(
  [string[]]@('en','fr','de','es','it','pt','nl','sv','no','da','fi','ru','pl','zh','ko','ja','cs','hu','el','tr','ar','he','th','vi','id','ms','ro','bg','uk','hr','sk','sl','et','lv','lt','af','ca','gd','eu'),
  [StringComparer]::OrdinalIgnoreCase
)

# ================================================================
#  Declarative Rule Data - pattern strings, region maps, alias maps
#  All rule data in one versionable structure.
# ================================================================
$script:RULE_DATA = @{
    # --- Region ordered rules (priority-descending) ---
    RegionOrdered = @(
      @{ Key='EU';    Pattern='\((europe|eu|eur|pal)\)' }
      @{ Key='US';    Pattern='\((usa|us|u\.s\.a\.|u\.s\.)(,\s*\w+)*\)' }
      @{ Key='JP';    Pattern='\((japan|jp|jpn)\)' }
      @{ Key='WORLD'; Pattern='\((world|export)\)' }
      @{ Key='AU';    Pattern='\((australia|au|aus)\)' }
      @{ Key='ASIA';  Pattern='\((asia|as)\)' }
      @{ Key='SCAN';  Pattern='\((scandinavia)\)' }
      @{ Key='KR';    Pattern='\((korea|kor)\)' }
      @{ Key='CN';    Pattern='\((china|chn)\)' }
      @{ Key='BR';    Pattern='\((brazil|bra)\)' }
      @{ Key='FR';    Pattern='\((france|fra)\)' }
      @{ Key='DE';    Pattern='\((germany|deu)\)' }
      @{ Key='ES';    Pattern='\((spain|esp)\)' }
      @{ Key='IT';    Pattern='\((italy|ita)\)' }
      @{ Key='NL';    Pattern='\((netherlands|nld)\)' }
      @{ Key='SE';    Pattern='\((sweden|swe)\)' }
      @{ Key='RU';    Pattern='\((russia|rus)\)' }
      @{ Key='PL';    Pattern='\((poland|pol)\)' }
      @{ Key='CA';    Pattern='\((canada|can)\)' }
      @{ Key='LATAM'; Pattern='\((latin\s*america)\)' }
      @{ Key='TR';    Pattern='\((turkey)\)' }
      @{ Key='AE';    Pattern='\((united\s*arab\s*emirates)\)' }
      @{ Key='AU';    Pattern='\((new\s*zealand|nzl)\)' }
      @{ Key='EU';    Pattern='\((uk|united\s*kingdom|great\s*britain|england|belgium|austria|portugal|switzerland|denmark|finland|norway|czech|hungary|croatia|greece|ireland|luxembourg|romania|bulgaria|slovakia|slovenia|estonia|latvia|lithuania|south\s*africa)\)' }
      @{ Key='ASIA';  Pattern='\((taiwan|hong\s*kong|india|singapore|thailand|vietnam|indonesia|malaysia|philippines)\)' }
    )

    # --- Region 2-letter ambiguous codes (only used when no primary match) ---
    Region2Letter = @(
      @{ Key='KR'; Pattern='\((kr)\)' }
      @{ Key='CN'; Pattern='\((cn)\)' }
      @{ Key='BR'; Pattern='\((br)\)' }
      @{ Key='FR'; Pattern='\((fr)\)' }
      @{ Key='DE'; Pattern='\((de)\)' }
      @{ Key='ES'; Pattern='\((es)\)' }
      @{ Key='IT'; Pattern='\((it)\)' }
      @{ Key='NL'; Pattern='\((nl)\)' }
      @{ Key='SE'; Pattern='\((se)\)' }
      @{ Key='AU'; Pattern='\((au)\)' }
      @{ Key='ASIA'; Pattern='\((as)\)' }
      @{ Key='RU'; Pattern='\((ru)\)' }
      @{ Key='PL'; Pattern='\((pl)\)' }
      @{ Key='CA'; Pattern='\((ca)\)' }
    )

    # --- Multiregion detection ---
    MultiRegionPattern = '\([^)]*(?:europe|usa|japan|world)[^)]*(?:europe|usa|japan|world)[^)]*\)'

    # --- Region fallback patterns ---
    FallbackEU = '\b(europe|pal)\b'
    FallbackUS = '\busa\b'
    FallbackJP = '\b(japan|jpn)\b'

    # --- Utility patterns ---
    VerifiedPattern  = '\[!\]'
    RevisionPattern  = '\(rev\s*([a-z0-9.]+)\)'
    VersionPattern   = '\(v\s*(\d+)\.?(\d*)\)'
    LangPattern      = '\((en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu)(?:,\s*(?:en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu))*\)'
    Cleanup1         = '[\._]+'
    Cleanup2         = '\s{2,}'

    # --- BIOS/Firmware ---
    BiosPattern = '\((bios|firmware)\)|\[bios\]|^\s*bios\b'

    # --- Junk tag patterns ---
    JunkTags = (
      '\((alpha\s*\d*|beta\s*\d*|proto(?:type)?\s*\d*|sample|sampler|demo|preview|pre[\s-]*release|promo|kiosk(?:\s*demo)?|debug|trial(?:\s*version)?|taikenban|rehearsal-?\s*ban|location\s*test|test\s*program)\)' +
      '|\((program|application|utility|enhancement\s*chip|test\s*program|test\s*cartridge)\)' +
      '|\((competition\s*cart|service\s*disc|diagnostic|check\s*program)\)' +
      '|\((hack|pirate|bootleg|homebrew|aftermarket|translated|translation)\)' +
      '|\((unl|unlicensed)\)' +
      '|\((not\s*for\s*resale|nfr)\)' +
      '|\[(b\d*|h\d*|p\d*|t\d*|f\d*|o\d*)\]' +
      '|\[(cr|tr|m)\s')
    JunkWords = '\b(demo|sample\s*version|trial\s*version|trial|pre[\s-]*release|not\s*for\s*resale|sampler|bootleg\s*sampler)\b|^gamelist(?:\.xml)?(?:\.old|\.bak)?$'
    JunkTagsAggressive = '\((wip|work\s*in\s*progress|playtest|test\s*build|dev\s*build|qa\s*build|review\s*build|internal\s*build|preview\s*build|prototype\s*build|not\s*for\s*distribution)\)'
    JunkWordsAggressive = '\b(work\s*in\s*progress|wip|playtest|test\s*build|dev\s*build|qa\s*build|review\s*build|internal\s*build|preview\s*build|not\s*for\s*distribution)\b'

    # --- GameKey strip patterns (applied sequentially) ---
    GameKeyPatterns = @(
      '\s*\((europe|eu|eur|pal|usa|us|u\.s\.a\.|u\.s\.|japan|jp|jpn|world|export|asia|as|korea|kr|kor|china|cn|chn|brazil|br|bra|australia|au|aus|france|fr|fra|germany|de|deu|spain|es|esp|italy|it|ita|netherlands|nl|nld|sweden|se|swe|scandinavia|canada|ca|can|russia|ru|rus|poland|pl|pol|uk|united\s*kingdom|great\s*britain|england|belgium|be|austria|at|portugal|pt|switzerland|ch|denmark|dk|finland|fi|norway|no|czech|cz|hungary|hu|croatia|hr|greece|el|ireland|ie|luxembourg|romania|ro|bulgaria|bg|slovakia|sk|slovenia|si|estonia|et|latvia|lv|lithuania|lt|taiwan|tw|hong\s*kong|hk|india|in|turkey|tr|united\s*arab\s*emirates|latin\s*america|south\s*africa|za|new\s*zealand|nz|nzl|singapore|thailand|vietnam|indonesia|malaysia|philippines)(,\s*(europe|eu|eur|pal|usa|us|japan|jp|jpn|world|asia|as|korea|kr|china|cn|brazil|br|australia|au|france|fr|germany|de|spain|es|italy|it|netherlands|nl|sweden|se|scandinavia|canada|ca|russia|ru|rus|poland|pl|pol|uk|united\s*kingdom|great\s*britain|england|belgium|be|austria|at|portugal|pt|switzerland|ch|denmark|dk|finland|fi|norway|no|czech|cz|hungary|hu|taiwan|tw|hong\s*kong|hk|india|in|turkey|tr|south\s*africa|new\s*zealand|nz|latin\s*america))*\)\s*',
      '\s*\((headered|headerless)\)\s*',
      '\s*\((rev\s*[a-z0-9.]+|revision\s*[a-z0-9.]+)\)\s*',
      '\s*\((v\s*[0-9][0-9.]*[a-z]?)\)\s*',
      '\s*\((alpha\s*\d*|beta\s*\d*|proto(?:type)?\s*\d*|sample|demo|preview|pre[\s-]*release|promo|kiosk(?:\s*demo)?|debug|trial(?:\s*version)?|taikenban|rehearsal-?\s*ban|location\s*test)\)\s*',
      '\s*\((program|application|utility|enhancement\s*chip|test\s*program|test\s*cartridge)\)\s*',
      '\s*\((hack|pirate|bootleg|homebrew|aftermarket|translated|translation)\)\s*',
      '\s*\((unl|unlicensed|competition\s*cart|service\s*disc|diagnostic)\)\s*',
      '\s*\((bios|firmware)\)\s*',
      '\s*\((en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu)(,\s*(en|fr|de|es|it|pt|nl|sv|no|da|fi|ru|pl|zh|ko|ja|cs|hu|el|tr|ar|he|th|vi|id|ms|ro|bg|uk|hr|sk|sl|et|lv|lt|af|ca|gd|eu))*\)\s*',
      '\s*\[(\!|b\d*|h\d*|o\d*|p\d*|t\d*|f\d*|a\d*|cr[^\]]*|tr[^\]]*|m\s[^\]]*)\]\s*',
      '\s*\((not\s*for\s*resale|nfr)\)\s*',
      '\s*\((virtual\s*console|switch\s*online|classic\s*mini|wii\s*u|gamecube)\)\s*',
      '\s*\((reprint|rerelease|rerip|alt|alt\s*\d*|collection)\)\s*',
      '\s*\(([^\)]*\b(collection|classics?|anniversary|antholog(?:y|ies)|archives?|museum|evercade|retro-?bit(?:\s*generations)?)\b[^\)]*)\)\s*',
      '\s*\((edc|no\s*edc|libcrypt|sbi|subchannel)\)\s*',
      '\s*\((\d+S(?:,\s*\d+S)*)\)\s*',
      '\s*\((Made\s+in\s+\w+)\)\s*',
      '\s*\([^)]*\bEdition\b[^)]*\)\s*',
      '\s*\((?:Greatest\s*Hits|PlayStation\s*\d+\s*the\s*Best|Platinum|Essentials|Budget|Best\s*Price|The\s*Best|Player''s\s*Choice|Nintendo\s*Selects|PlayStation\s*Hits|Rockstar\s*Classics|Aquaprice\s*\d+)\)\s*',
      '\s*\([A-Z]{4}-\d{4,5}\)\s*',
      '\s*\((?:Fukikaeban|Jimakuban|PlayStation\s*Move\s*Taiou|3D\s*Compatible|Tsuika\s*Contents\s*Disc|Append-ban)\)\s*',
      '\s*\(Version\s*\d+\.?\d*\)\s*',
      '\s*\(FW\d+\.\d+\)\s*',
      '\s*\(\d{4}-\d{2}-\d{2}\)\s*',
      '\s*\(\s*\)\s*'
    )

    # --- Alias edition tag patterns ---
    AliasTagPatterns = @(
      'aga', 'amiga\s*\+\s*pc', 'budget(?:\s*-\s*[^\)]*)?', 'compilation(?:\s*-\s*[^\)]*)?',
      'classics?', 'hits?', 'retro-?bit(?:\s*generations)?', 'pixel\s*heart', 'digital',
      'famicombox', 'evercade', 'castlevania\s*anniversary\s*collection',
      'contra\s*anniversary\s*collection', 'mega\s*man\s*legacy\s*collection',
      'the\s*disney\s*afternoon\s*collection', 'the\s*cowabunga\s*collection',
      'konami\s*collector''s\s*series',
      'namcot\s*collection(?:,\s*namco\s*museum\s*archives\s*vol\s*[12])?',
      'namco\s*museum\s*archives\s*vol\s*[12]', 'animal\s*crossing', 'e-reader', 'datach',
      'limited\s*run\s*games', 'wii\s*virtual\s*console', 'wii\s*u\s*virtual\s*console',
      'wii\s*and\s*wii\s*u\s*virtual\s*console', 'virtual\s*console(?:,\s*switch\s*online)?',
      'switch\b', '3ds\s*virtual\s*console', 'famicom\s*3d\s*system',
      'ninja\s*jajamaru\s*retro\s*collection', 'capcom\s*town',
      'snk\s*40th\s*anniversary\s*collection', 'qubyte\s*classics', 'red\s*art\s*games',
      'metal\s*gear\s*solid\s*collection', 'zelda\s*collection',
      '8-bit\s*adventure\s*anthology\s*-\s*volume\s*i', 'iam8bit',
      'capcom\s*classics\s*mini\s*mix', 'disney\s*classic\s*games', 'steam', 'sunsoft',
      'columbus\s*circle', 'tengen', 'broke\s*studio', 'victor', 'acclaim', 'kemco', 'namco',
      'rockman\s*123', 'possible\s*proto', 'early\s*proto', 'menu\s*cart', 'jou', 'taiwan',
      'ndsi\s*enhanced',
      '[^\)]*\b(collection|classics?|anniversary|antholog(?:y|ies)|archives?|museum)\b[^\)]*'
    )

    # --- Store tag removal pattern ---
    StoreTagPattern = '\s*\((evercade|castlevania\s*anniversary\s*collection|contra\s*anniversary\s*collection|the\s*disney\s*afternoon\s*collection|the\s*cowabunga\s*collection|konami\s*collector''s\s*series|namco\s*museum\s*archives\s*vol\s*[12]|namcot\s*collection(?:,\s*namco\s*museum\s*archives\s*vol\s*[12])?|virtual\s*console(?:,\s*switch\s*online)?|switch\s*online|wii\s*virtual\s*console|wii\s*u\s*virtual\s*console|ndsi\s*enhanced)\)\s*'

    # --- Always-apply alias map (canonical name mapping) ---
    AlwaysAliasMap = [ordered]@{
      'akumajou dracula'                    = 'castlevania'
      'akumajou densetsu'                   = "castlevania iii - dracula's curse"
      'akumajou special - boku dracula-kun' = 'kid dracula'
      'rockman 2'   = 'mega man 2'; 'rockman iii' = 'mega man 3'; 'rockman iv' = 'mega man 4'
      'rockman v'   = 'mega man 5'; 'rockman vi'  = 'mega man 6'
      'rockman 2 (rockman 123)' = 'mega man 2'; 'rockman iii (rockman 123)' = 'mega man 3'
      'rockman iv (rockman 123)' = 'mega man 4'; 'rockman v (rockman 123)' = 'mega man 5'
      'rockman vi (rockman 123)' = 'mega man 6'
      '8 eyes (digital)' = '8 eyes'; '8 eyes (pixel heart)' = '8 eyes'
      'battletoads (iam8bit)' = 'battletoads'
      "ghosts'n goblins (capcom town)" = "ghosts'n goblins"
      'mega man (capcom town)' = 'mega man'; 'mega man 2 (capcom town)' = 'mega man 2'
      'mega man 2 (iam8bit)' = 'mega man 2'
      "castlevania iii - dracula's curse (usa wii virtual console, wii u virtual console)" = "castlevania iii - dracula's curse"
    }

    # --- Optional alias map (edition-keyed dedup) ---
    BaseAliasMap = [ordered]@{
      'abandoned places - a time for heroes'   = 'abandoned places'
      'abandoned places - zeit für helden'     = 'abandoned places'
      'abandoned places - zeit fur helden'     = 'abandoned places'
      'abandoned places - zeit fuer helden'    = 'abandoned places'
    }
}

if (Get-Command Import-RomCleanupJsonData -ErrorAction SilentlyContinue) {
  $ruleDataFromJson = Import-RomCleanupJsonData -FileName 'rules.json'
  if ($ruleDataFromJson -and ($ruleDataFromJson -is [System.Collections.IDictionary]) -and $ruleDataFromJson.Contains('RegionOrdered')) {
    $script:RULE_DATA = $ruleDataFromJson
  }
}

function Initialize-RulePatterns {
    <# Compiles RULE_DATA pattern strings into $script:RX_* compiled regex variables.
       Call once at startup and after rule overrides. #>

    $rd = $script:RULE_DATA

    # BUG-044 FIX: All regex compilations use a 5-second timeout to prevent ReDoS
    $rxTimeout = [TimeSpan]::FromSeconds(5)
    $script:RX_MULTIREGION = [regex]::new($rd.MultiRegionPattern, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_FALLBACK_EU = [regex]::new($rd.FallbackEU, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_FALLBACK_US = [regex]::new($rd.FallbackUS, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_FALLBACK_JP = [regex]::new($rd.FallbackJP, 'IgnoreCase, Compiled', $rxTimeout)

    $script:RX_VERIFIED = [regex]::new($rd.VerifiedPattern, 'Compiled', $rxTimeout)
    $script:RX_REVISION = [regex]::new($rd.RevisionPattern, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_VERSION  = [regex]::new($rd.VersionPattern, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_LANG     = [regex]::new($rd.LangPattern, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_CLEANUP1 = [regex]::new($rd.Cleanup1, 'Compiled', $rxTimeout)
    $script:RX_CLEANUP2 = [regex]::new($rd.Cleanup2, 'Compiled', $rxTimeout)

    $script:RX_BIOS = [regex]::new($rd.BiosPattern, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_JUNK_TAGS = [regex]::new($rd.JunkTags, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_JUNK_WORDS = [regex]::new($rd.JunkWords, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_JUNK_TAGS_AGGRESSIVE = [regex]::new($rd.JunkTagsAggressive, 'IgnoreCase, Compiled', $rxTimeout)
    $script:RX_JUNK_WORDS_AGGRESSIVE = [regex]::new($rd.JunkWordsAggressive, 'IgnoreCase, Compiled', $rxTimeout)

    $script:UseAggressiveJunk = $false
    if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
      [void](Set-AppStateValue -Key 'UseAggressiveJunk' -Value $false)
    }

    $script:RX_GAMEKEY = @()
    foreach ($pat in $rd.GameKeyPatterns) {
        $script:RX_GAMEKEY += [regex]::new($pat, 'IgnoreCase, Compiled', $rxTimeout)
    }
    # Invalidate cached combined regexes (rebuilt lazily in ConvertTo-GameKey)
    Set-Variable -Name RX_GAMEKEY_COMBINED -Scope Script -Value $null -ErrorAction SilentlyContinue
    Set-Variable -Name RX_GAMEKEY_CLEANUP_COMBINED -Scope Script -Value $null -ErrorAction SilentlyContinue

    $script:RX_GAMEKEY_STORE_TAGS = [regex]::new($rd.StoreTagPattern, 'IgnoreCase, Compiled', $rxTimeout)

    $script:GAMEKEY_ALIAS_TAG_PATTERNS = $rd.AliasTagPatterns
    $script:RX_GAMEKEY_ALIAS_TAGS = [regex]::new(
        ('\s*\(({0})\)\s*' -f ($rd.AliasTagPatterns -join '|')),
        'IgnoreCase, Compiled', $rxTimeout)

    $script:GAMEKEY_ALIAS_MAP = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($k in $rd.BaseAliasMap.Keys) {
        $nk = [regex]::Replace([string]$k, '\s+', '')
        $nv = [regex]::Replace([string]$rd.BaseAliasMap[$k], '\s+', '')
        if ($script:GAMEKEY_ALIAS_MAP.ContainsKey($nk)) {
          $existing = [string]$script:GAMEKEY_ALIAS_MAP[$nk]
          $pick = @($existing, $nv) | Sort-Object Length, @{ Expression = { $_ }; Descending = $false } | Select-Object -First 1
          $script:GAMEKEY_ALIAS_MAP[$nk] = [string]$pick
        } else {
          $script:GAMEKEY_ALIAS_MAP[$nk] = $nv
        }
    }
    $script:GAMEKEY_ALIAS_MAP_BASE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($k in $script:GAMEKEY_ALIAS_MAP.Keys) {
        $script:GAMEKEY_ALIAS_MAP_BASE[$k] = [string]$script:GAMEKEY_ALIAS_MAP[$k]
    }

    $script:GAMEKEY_ALWAYS_ALIAS_MAP = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($k in $rd.AlwaysAliasMap.Keys) {
        $nk = [regex]::Replace([string]$k, '\s+', '')
        $nv = [regex]::Replace([string]$rd.AlwaysAliasMap[$k], '\s+', '')
        if ($script:GAMEKEY_ALWAYS_ALIAS_MAP.ContainsKey($nk)) {
          $existing = [string]$script:GAMEKEY_ALWAYS_ALIAS_MAP[$nk]
          $pick = @($existing, $nv) | Sort-Object Length, @{ Expression = { $_ }; Descending = $false } | Select-Object -First 1
          $script:GAMEKEY_ALWAYS_ALIAS_MAP[$nk] = [string]$pick
        } else {
          $script:GAMEKEY_ALWAYS_ALIAS_MAP[$nk] = $nv
        }
    }

    $script:RX_REGION_ORDERED = @()
    foreach ($entry in $rd.RegionOrdered) {
        $script:RX_REGION_ORDERED += @{
            Key = $entry.Key
            Rx  = [regex]::new($entry.Pattern, 'IgnoreCase, Compiled', $rxTimeout)
        }
    }

    $script:RX_REGION_2LETTER = @()
    foreach ($entry in $rd.Region2Letter) {
        $script:RX_REGION_2LETTER += @{
            Key = $entry.Key
            Rx  = [regex]::new($entry.Pattern, 'IgnoreCase, Compiled', $rxTimeout)
        }
    }

    $script:RX_REGION_ORDERED_OVERRIDE = $null
    $script:RX_REGION_2LETTER_OVERRIDE = $null
    $script:RX_LANG_OVERRIDE = $null

    # Reset combined-regex caches so they get rebuilt
    # PERF-09: Eagerly build combined regex (GameKeyPatterns + StoreTagPattern + Cleanup1)
    $combinedParts = New-Object System.Collections.Generic.List[string]
    foreach ($rx in $script:RX_GAMEKEY) {
      if ($rx) { [void]$combinedParts.Add(('(?:{0})' -f $rx.ToString())) }
    }
    [void]$combinedParts.Add(('(?:{0})' -f $rd.StoreTagPattern))
    [void]$combinedParts.Add(('(?:{0})' -f $rd.Cleanup1))
    $script:RX_GAMEKEY_COMBINED = [regex]::new(($combinedParts -join '|'), 'IgnoreCase, Compiled', $rxTimeout)
    # Cleanup2 (\s{2,}) is redundant: space-insensitive key normalisation collapses all spaces
    $script:RX_GAMEKEY_CLEANUP_COMBINED = $null
}

# --- Default ROM extension list (shared by GUI + CLI) ---
$script:ALL_ROM_EXTENSIONS = '.chd,.rvz,.gcz,.cso,.zso,.dax,.jso,.wbfs,.wia,.nkit.iso,.nkit.gcz,.iso,.bin,.img,.mdf,.nrg,.cdi,.cue,.gdi,.ccd,.m3u,.toc,.zip,.7z,.rar,.gz,.pbp,.ecm,.nds,.3ds,.cia,.nsp,.xci,.nsz,.xcz,.n64,.z64,.v64,.ndd,.nes,.fds,.unf,.sfc,.smc,.swc,.fig,.gb,.gbc,.gba,.sgb,.gcm,.dol,.elf,.wad,.md,.gen,.smd,.32x,.sms,.gg,.cdc,.a26,.a52,.a78,.pce,.sgx,.ngp,.ngc,.ws,.wsc,.vb,.vec,.lnx,.jag,.j64'

# Default initialisation
Initialize-RulePatterns

function Resolve-RegionTagFromTokens {
  param([string]$Name)

  if ([string]::IsNullOrWhiteSpace($Name)) { return 'UNKNOWN' }

  $languageTokens = $script:LANG_TOKENS

  $regionTokens = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @(
      @{ K='world'; V='WORLD' }, @{ K='europe'; V='EU' }, @{ K='eur'; V='EU' }, @{ K='pal'; V='EU' },
      @{ K='uk'; V='EU' }, @{ K='united kingdom'; V='EU' }, @{ K='great britain'; V='EU' }, @{ K='england'; V='EU' },
      @{ K='belgium'; V='EU' }, @{ K='portugal'; V='EU' },
      @{ K='germany'; V='DE' }, @{ K='de'; V='DE' }, @{ K='deu'; V='DE' },
      @{ K='spain'; V='ES' }, @{ K='es'; V='ES' }, @{ K='esp'; V='ES' },
      @{ K='italy'; V='IT' }, @{ K='it'; V='IT' }, @{ K='ita'; V='IT' },
      @{ K='netherlands'; V='NL' }, @{ K='nl'; V='NL' }, @{ K='holland'; V='NL' },
      @{ K='sweden'; V='SE' }, @{ K='se'; V='SE' }, @{ K='swe'; V='SE' },
      @{ K='brazil'; V='BR' }, @{ K='br'; V='BR' }, @{ K='bra'; V='BR' },
      @{ K='korea'; V='KR' }, @{ K='kr'; V='KR' }, @{ K='kor'; V='KR' },
      @{ K='china'; V='CN' }, @{ K='cn'; V='CN' }, @{ K='chn'; V='CN' },
      @{ K='russia'; V='RU' }, @{ K='ru'; V='RU' }, @{ K='rus'; V='RU' },
      @{ K='usa'; V='US' }, @{ K='us'; V='US' }, @{ K='u.s.a'; V='US' }, @{ K='north america'; V='US' },
      @{ K='japan'; V='JP' }, @{ K='jpn'; V='JP' }, @{ K='jp'; V='JP' },
      @{ K='australia'; V='AU' }, @{ K='au'; V='AU' }, @{ K='aus'; V='AU' },
      @{ K='asia'; V='ASIA' }, @{ K='taiwan'; V='ASIA' }, @{ K='hong kong'; V='ASIA' }, @{ K='india'; V='ASIA' },
      @{ K='singapore'; V='ASIA' }, @{ K='thailand'; V='ASIA' }, @{ K='vietnam'; V='ASIA' },
      @{ K='indonesia'; V='ASIA' }, @{ K='malaysia'; V='ASIA' }, @{ K='philippines'; V='ASIA' },
      @{ K='canada'; V='CA' }, @{ K='ca'; V='CA' }, @{ K='can'; V='CA' },
      @{ K='poland'; V='PL' }, @{ K='pl'; V='PL' }, @{ K='pol'; V='PL' },
      @{ K='scandinavia'; V='EU' },
      @{ K='turkey'; V='TR' }, @{ K='tr'; V='TR' },
      @{ K='united arab emirates'; V='AE' },
      @{ K='latin america'; V='LATAM' },
      @{ K='new zealand'; V='AU' }, @{ K='nz'; V='AU' },
      @{ K='south africa'; V='EU' }, @{ K='za'; V='EU' },
      @{ K='croatia'; V='EU' }, @{ K='hr'; V='EU' },
      @{ K='greece'; V='EU' }, @{ K='ireland'; V='EU' },
      @{ K='luxembourg'; V='EU' }, @{ K='romania'; V='EU' },
      @{ K='bulgaria'; V='EU' }, @{ K='slovakia'; V='EU' },
      @{ K='slovenia'; V='EU' }, @{ K='estonia'; V='EU' },
      @{ K='latvia'; V='EU' }, @{ K='lithuania'; V='EU' },
      # BUG-016 FIX: Add 'fr' to regionTokens so token-parser is consistent with Region2Letter regex fallback
      @{ K='france'; V='FR' }, @{ K='fr'; V='FR' }, @{ K='fra'; V='FR' }
    )) {
    $regionTokens[$entry.K] = $entry.V
  }

  $foundRegions = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  $regionMatches = [regex]::Matches($Name, '\(([^)]*)\)')
  foreach ($m in $regionMatches) {
    $inside = [string]$m.Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($inside)) { continue }

    $parts = @($inside -split ',')
    foreach ($part in $parts) {
      $token = $part.Trim().ToLowerInvariant()
      if ([string]::IsNullOrWhiteSpace($token)) { continue }
      # BUG CORE-001 FIX: Check region tokens FIRST — ambiguous tokens (fr/de/es) should count as regions
      if ($regionTokens.ContainsKey($token)) {
        [void]$foundRegions.Add([string]$regionTokens[$token])
        continue
      }
      if ($languageTokens.Contains($token)) { continue }
    }
  }

  if ($foundRegions.Count -eq 0) { return 'UNKNOWN' }
  if ($foundRegions.Count -gt 1) { return 'WORLD' }
  foreach ($region in $foundRegions) { return [string]$region }
  return 'UNKNOWN'
}

function Get-RegionTag {
  param([Parameter(Mandatory=$false)][string]$Name)

  if ($Name -match '\((?:[^)]*\b(?:uk|united\s*kingdom|great\s*britain|england)\b[^)]*)\)') { return 'EU' }

  if ($script:RX_MULTIREGION.IsMatch($Name)) { return 'WORLD' }

  $parsedRegion = Resolve-RegionTagFromTokens -Name $Name
  if ($parsedRegion -and $parsedRegion -ne 'UNKNOWN') { return $parsedRegion }

  $orderedRules = if ($script:RX_REGION_ORDERED_OVERRIDE) { $script:RX_REGION_ORDERED_OVERRIDE } else { $script:RX_REGION_ORDERED }
  foreach ($entry in $orderedRules) {
    if ($entry.Rx.IsMatch($Name)) { return $entry.Key }
  }

  $twoLetterRules = if ($script:RX_REGION_2LETTER_OVERRIDE) { $script:RX_REGION_2LETTER_OVERRIDE } else { $script:RX_REGION_2LETTER }
  foreach ($entry in $twoLetterRules) {
    if ($entry.Rx.IsMatch($Name)) { return $entry.Key }
  }

  if ($script:RX_FALLBACK_EU.IsMatch($Name)) { return 'EU' }
  if ($script:RX_FALLBACK_US.IsMatch($Name)) { return 'US' }
  if ($script:RX_FALLBACK_JP.IsMatch($Name)) { return 'JP' }

  return 'UNKNOWN'
}

function ConvertTo-AsciiFold {
  param([string]$Text)

  if ([string]::IsNullOrWhiteSpace($Text)) { return $Text }

  $work = [string]$Text
  $work = $work.Replace('ß', 'ss').Replace('ẞ', 'ss')
  # BUG-027 FIX: Turkish İ/ı dotless-i does not decompose via NFD — explicit mapping
  $work = $work.Replace([string][char]0x0131, 'i').Replace([string][char]0x0130, 'I')
  $work = $work.Replace([string][char]0x2019, "'").Replace([string][char]0x2018, "'")
  $work = $work.Replace([string][char]0x2013, '-').Replace([string][char]0x2014, '-')

  $normalized = $work.Normalize([System.Text.NormalizationForm]::FormD)
  $builder = New-Object System.Text.StringBuilder
  foreach ($ch in $normalized.ToCharArray()) {
    $category = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch)
    if (
      $category -ne [System.Globalization.UnicodeCategory]::NonSpacingMark -and
      $category -ne [System.Globalization.UnicodeCategory]::SpacingCombiningMark -and
      $category -ne [System.Globalization.UnicodeCategory]::EnclosingMark
    ) {
      [void]$builder.Append($ch)
    }
  }

  return $builder.ToString().Normalize([System.Text.NormalizationForm]::FormC)
}

function Remove-MsDosMetadataTags {
  param([string]$Text)

  if ([string]::IsNullOrWhiteSpace($Text)) { return $Text }

  $value = [string]$Text
  $value = [regex]::Replace($value, '\s*(?:\[[^\]]+\]\s*)+$', ' ', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

  $rxTrailingNonDiscTag = [regex]::new(
    '\s*\((?!\s*(?:disc|disk|side|cd\s*\d*|floppy|tape)\b)[^)]*\)\s*$',
    'IgnoreCase, Compiled')

  while ($rxTrailingNonDiscTag.IsMatch($value)) {
    $value = $rxTrailingNonDiscTag.Replace($value, ' ')
  }

  return $value
}

function Initialize-GameKeyLruCache {
  if (Get-Variable -Name GAMEKEY_LRU_CACHE -Scope Script -ErrorAction SilentlyContinue) {
    if ($script:GAMEKEY_LRU_CACHE) { return $script:GAMEKEY_LRU_CACHE }
  }
  if (-not (Get-Command New-LruCache -ErrorAction SilentlyContinue)) { return $null }

  $maxEntries = 50000
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      $configured = Get-AppStateValue -Key 'GameKeyCacheMaxEntries' -Default $maxEntries
      if ($configured -is [int] -or $configured -is [long]) {
        $maxEntries = [int][Math]::Max(2000, [int64]$configured)
      }
    } catch { }
  }

  $script:GAMEKEY_LRU_CACHE = New-LruCache -MaxEntries $maxEntries -Name 'GameKeyCache'
  if (Get-Command Register-LruCache -ErrorAction SilentlyContinue) {
    Register-LruCache -Cache $script:GAMEKEY_LRU_CACHE
  }
  return $script:GAMEKEY_LRU_CACHE
}

function ConvertTo-GameKey {
  param(
    [Parameter(Mandatory=$false)][string]$BaseName,
    [bool]$AliasEditionKeying = $false,
    [string]$ConsoleType = $null
  )

  $gameKeyCache = Initialize-GameKeyLruCache
  $cacheKey = ('{0}|{1}|{2}' -f [string]$BaseName, [bool]$AliasEditionKeying, [string]$ConsoleType)
  if ($gameKeyCache -and (Get-Command Get-LruCacheValue -ErrorAction SilentlyContinue)) {
    $cached = Get-LruCacheValue -Cache $gameKeyCache -Key $cacheKey
    if (-not [string]::IsNullOrWhiteSpace([string]$cached)) {
      return [string]$cached
    }
  }

  $s = ConvertTo-AsciiFold -Text $BaseName
  if ($ConsoleType -and $ConsoleType.Trim().ToUpperInvariant() -eq 'DOS') {
    $s = Remove-MsDosMetadataTags -Text $s
  }

  # PERF-09: Use pre-built combined regex (GameKeyPatterns + StoreTagPattern + Cleanup1)
  if ($script:RX_GAMEKEY_COMBINED) {
    $s = $script:RX_GAMEKEY_COMBINED.Replace($s, ' ')
  } else {
    # Fallback: apply patterns individually if combined not yet built
    foreach ($rx in $script:RX_GAMEKEY) {
      $s = $rx.Replace($s, ' ')
    }
    if ($script:RX_GAMEKEY_STORE_TAGS) { $s = $script:RX_GAMEKEY_STORE_TAGS.Replace($s, ' ') }
    if ($script:RX_CLEANUP1) { $s = $script:RX_CLEANUP1.Replace($s, ' ') }
    if ($script:RX_CLEANUP2) { $s = $script:RX_CLEANUP2.Replace($s, ' ') }
  }
  if ($AliasEditionKeying) {
    $s = $script:RX_GAMEKEY_ALIAS_TAGS.Replace($s, ' ')
  }
  $key = $s.Trim().ToLowerInvariant()

  # Space-insensitive normalization: collapse all spaces so that
  # "Brain Dead 13" and "BrainDead 13" produce the same key.
  $key = [regex]::Replace($key, '\s+', '')

  if ($script:GAMEKEY_ALWAYS_ALIAS_MAP.ContainsKey($key)) {
    $key = [string]$script:GAMEKEY_ALWAYS_ALIAS_MAP[$key]
  }
  if ($AliasEditionKeying -and $script:GAMEKEY_ALIAS_MAP.ContainsKey($key)) {
    $key = [string]$script:GAMEKEY_ALIAS_MAP[$key]
  }
  if ([string]::IsNullOrWhiteSpace($key)) {
    $key = $BaseName.Trim().ToLowerInvariant()
  }
  if ([string]::IsNullOrWhiteSpace($key)) {
    $key = '__empty_key_' + [guid]::NewGuid().ToString('N').Substring(0, 8)
  }

  if ($gameKeyCache -and (Get-Command Set-LruCacheValue -ErrorAction SilentlyContinue)) {
    Set-LruCacheValue -Cache $gameKeyCache -Key $cacheKey -Value $key
  }

  return $key
}

function Get-VersionScore {
  param([Parameter(Mandatory=$false)][string]$BaseName = '')

  if ([string]::IsNullOrWhiteSpace($BaseName)) { return 0 }

  $score = 0

  if ($script:RX_VERIFIED.IsMatch($BaseName)) { $score += 500 }

  $m = $script:RX_REVISION.Match($BaseName)
  if ($m.Success) {
    $rev = $m.Groups[1].Value.ToLowerInvariant()
    if ($rev -match '^[a-z]+$') {
      $letters = $rev.ToCharArray()
      $letterScore = 0
      foreach ($ch in $letters) {
        $letterScore = ($letterScore * 26) + (([int][char]$ch - [int][char]'a') + 1)
      }
      $score += ($letterScore * 10)
    } elseif ($rev -match '^(\d+)([a-z]+)?$') {
      $numeric = [int]$Matches[1]
      $suffix = [string]$Matches[2]
      $suffixScore = 0
      if (-not [string]::IsNullOrWhiteSpace($suffix)) {
        foreach ($ch in $suffix.ToCharArray()) {
          $suffixScore = ($suffixScore * 26) + (([int][char]$ch - [int][char]'a') + 1)
        }
      }
      $score += ($numeric * 100) + $suffixScore
    } elseif ($rev -match '^\d+') {
      $score += [int]$Matches[0] * 10
    }
  }

  $m = $script:RX_VERSION.Match($BaseName)
  if ($m.Success) {
    $versionToken = $m.Value
    $segments = New-Object System.Collections.Generic.List[int]
    foreach ($seg in [regex]::Matches($versionToken, '\d+')) {
      [void]$segments.Add([int]$seg.Value)
    }
    if ($segments.Count -gt 0) {
      $weight = [long]1
      for ($i = 1; $i -lt $segments.Count; $i++) {
        $weight *= 1000
      }
      $versionScore = [long]0
      foreach ($seg in $segments) {
        $versionScore += ([long]$seg * $weight)
        if ($weight -gt 1) { $weight = [long]($weight / 1000) }
      }
      $score += $versionScore
    }
  }

  # BUG CORE-003 FIX: Honour language regex override if set via Set-RegionRulesOverride
  $langRx = if ($script:RX_LANG_OVERRIDE) { $script:RX_LANG_OVERRIDE } else { $script:RX_LANG }
  $m = $langRx.Match($BaseName)
  if ($m.Success) {
    $langs = $m.Value.ToLowerInvariant()
    if ($langs -match '\ben\b') {
      $score += 50
      $langCount = $langs.Split(',').Count
      $score += $langCount * 5
    }
    if ($langs -match '\bde\b') {
      $score += 25
    }
  }

  return $score
}

function Select-Winner {
  param(
    [Parameter(Mandatory=$true)][psobject[]]$Items
  )

  if (-not $Items -or $Items.Count -eq 0) { return $null }
  if ($Items.Count -eq 1) { return $Items[0] }

  # OPT-03: Fast path for the common 2-item case (avoids full Sort-Object pipeline)
  if ($Items.Count -eq 2) {
    $a = $Items[0]; $b = $Items[1]
    $csA = if ($a.PSObject.Properties['CompletenessScore']) { $a.CompletenessScore } else { 0 }
    $csB = if ($b.PSObject.Properties['CompletenessScore']) { $b.CompletenessScore } else { 0 }
    if ($csA -ne $csB) { return $(if ($csA -gt $csB) { $a } else { $b }) }
    $dA = if ($a.PSObject.Properties['DatMatch'] -and $a.DatMatch) { 1 } else { 0 }
    $dB = if ($b.PSObject.Properties['DatMatch'] -and $b.DatMatch) { 1 } else { 0 }
    if ($dA -ne $dB) { return $(if ($dA -gt $dB) { $a } else { $b }) }
    if ($a.RegionScore -ne $b.RegionScore) { return $(if ($a.RegionScore -gt $b.RegionScore) { $a } else { $b }) }
    if ($a.HeaderScore -ne $b.HeaderScore) { return $(if ($a.HeaderScore -gt $b.HeaderScore) { $a } else { $b }) }
    if ($a.VersionScore -ne $b.VersionScore) { return $(if ($a.VersionScore -gt $b.VersionScore) { $a } else { $b }) }
    if ($a.FormatScore -ne $b.FormatScore) { return $(if ($a.FormatScore -gt $b.FormatScore) { $a } else { $b }) }
    $sA = if ($a.PSObject.Properties['SizeTieBreakScore']) { $a.SizeTieBreakScore } else { -1 * [long]$a.SizeBytes }
    $sB = if ($b.PSObject.Properties['SizeTieBreakScore']) { $b.SizeTieBreakScore } else { -1 * [long]$b.SizeBytes }
    if ($sA -ne $sB) { return $(if ($sA -gt $sB) { $a } else { $b }) }
    return $(if ([string]$a.MainPath -le [string]$b.MainPath) { $a } else { $b })
  }

  return $Items |
    Sort-Object -Property `
      @{Expression={if ($_.PSObject.Properties['CompletenessScore']) { $_.CompletenessScore } else { 0 }};Descending=$true},
      @{Expression={if ($_.PSObject.Properties['DatMatch'] -and $_.DatMatch) { 1 } else { 0 }};Descending=$true},
      @{Expression='RegionScore';Descending=$true},
      @{Expression='HeaderScore';Descending=$true},
      @{Expression='VersionScore';Descending=$true},
      @{Expression='FormatScore';Descending=$true},
      @{Expression={if ($_.PSObject.Properties['SizeTieBreakScore']) { $_.SizeTieBreakScore } else { -1 * [long]$_.SizeBytes }};Descending=$true},
      # BUG-011 FIX: Alphabetical tiebreaker on MainPath ensures deterministic winner selection
      @{Expression='MainPath';Descending=$false} |
    Select-Object -First 1
}

# ================================================================
#  REGION RULES & REGEX HELPERS  (extracted from simple_sort.ps1 - Sprint 2)
# ================================================================

function ConvertFrom-RegionRulesText {
  param([string]$Text)
  $rows = New-Object System.Collections.Generic.List[object]
  if ([string]::IsNullOrWhiteSpace($Text)) { return @() }

  $textValue = [string]$Text
  $lines = [regex]::Split($textValue, "\r?\n")
  foreach ($line in $lines) {
    $trim = $line.Trim()
    if ($trim -eq '' -or $trim.StartsWith('#')) { continue }
    $pipeIndex = $trim.IndexOf('|')
    if ($pipeIndex -lt 1 -or $pipeIndex -ge ($trim.Length - 1)) { continue }
    $key = $trim.Substring(0, $pipeIndex).Trim()
    $pattern = $trim.Substring($pipeIndex + 1).Trim()
    if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($pattern)) { continue }
    $rows.Add([pscustomobject]@{ Key = $key; Pattern = $pattern }) | Out-Null
  }

  return $rows.ToArray()
}

function Test-RegexSafe {
  <#
    Prueft ein Regex-Muster auf Gueltigkeit und ReDoS-Sicherheit.
    Gibt [pscustomobject]@{ Valid; Regex; Error } zurueck.
    Timeout schuetzt gegen katastrophales Backtracking (ReDoS).
  #>
  param(
    [string]$Pattern,
    [System.Text.RegularExpressions.RegexOptions]$Options = 'IgnoreCase, Compiled',
    [int]$TimeoutMs = 2000
  )
  if ([string]::IsNullOrWhiteSpace($Pattern)) {
    return [pscustomobject]@{ Valid = $false; Regex = $null; Error = 'Leeres Muster' }
  }
  try {
    $timeout = [TimeSpan]::FromMilliseconds($TimeoutMs)
    $rx = [regex]::new($Pattern, $Options, $timeout)
    # Schnelltest mit typischem ROM-Namen um fruehes ReDoS zu erkennen
    $testInput = 'Game Name (Europe) (Rev A) (Demo) (Unl).zip'
    try {
      [void]$rx.IsMatch($testInput)
    } catch [System.Text.RegularExpressions.RegexMatchTimeoutException] {
      return [pscustomobject]@{ Valid = $false; Regex = $null; Error = "ReDoS: Muster laeuft in Timeout bei Teststring" }
    }
    return [pscustomobject]@{ Valid = $true; Regex = $rx; Error = $null }
  } catch {
    return [pscustomobject]@{ Valid = $false; Regex = $null; Error = $_.Exception.Message }
  }
}

function Set-RegionRulesOverride {
  param(
    [string]$OrderedText,
    [string]$TwoLetterText,
    [string]$LangPattern
  )

  $errors = New-Object System.Collections.Generic.List[string]
  $orderedOverride = $null
  $twoLetterOverride = $null
  $langOverride = $null

  if (-not [string]::IsNullOrWhiteSpace($OrderedText)) {
    $ordered = New-Object System.Collections.Generic.List[object]
    foreach ($row in (ConvertFrom-RegionRulesText -Text $OrderedText)) {
      $check = Test-RegexSafe -Pattern $row.Pattern
      if (-not $check.Valid) {
        [void]$errors.Add("Ungueltiges RegEx Muster: $($row.Pattern) - $($check.Error)")
        continue
      }
      $ordered.Add([pscustomobject]@{ Key = $row.Key; Rx = $check.Regex }) | Out-Null
    }
    if ($ordered.Count -gt 0) { $orderedOverride = $ordered.ToArray() }
  }

  if (-not [string]::IsNullOrWhiteSpace($TwoLetterText)) {
    $twoLetter = New-Object System.Collections.Generic.List[object]
    foreach ($row in (ConvertFrom-RegionRulesText -Text $TwoLetterText)) {
      $check = Test-RegexSafe -Pattern $row.Pattern
      if (-not $check.Valid) {
        [void]$errors.Add("Ungueltiges RegEx Muster: $($row.Pattern) - $($check.Error)")
        continue
      }
      $twoLetter.Add([pscustomobject]@{ Key = $row.Key; Rx = $check.Regex }) | Out-Null
    }
    if ($twoLetter.Count -gt 0) { $twoLetterOverride = $twoLetter.ToArray() }
  }

  if (-not [string]::IsNullOrWhiteSpace($LangPattern)) {
    $check = Test-RegexSafe -Pattern $LangPattern
    if (-not $check.Valid) {
      [void]$errors.Add("Ungueltiges RegEx Muster: $LangPattern - $($check.Error)")
    } else {
      $langOverride = $check.Regex
    }
  }

  $success = ($errors.Count -eq 0)
  if ($success) {
    $script:RX_REGION_ORDERED_OVERRIDE = $orderedOverride
    $script:RX_REGION_2LETTER_OVERRIDE = $twoLetterOverride
    $script:RX_LANG_OVERRIDE = $langOverride
  }

  return [pscustomobject]@{
    Success = $success
    Errors  = @($errors)
  }
}

# ================================================================
#  CUSTOM ALIAS MAP  (extracted from simple_sort.ps1 - Sprint 2)
# ================================================================

function ConvertTo-CustomAliasMap {
  param([string]$Text)

  $result = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  if ([string]::IsNullOrWhiteSpace($Text)) { return $result }

  $lineNo = 0
  foreach ($line in ($Text -split "`r?`n")) {
    [void]($lineNo++)
    $work = [string]$line
    if ([string]::IsNullOrWhiteSpace($work)) { continue }
    if ($work.TrimStart().StartsWith('#')) { continue }

    $pair = $work -split '=', 2
    if ($pair.Count -lt 2) {
      $pair = $work -split '\|', 2
    }
    if ($pair.Count -lt 2) { continue }

    $source = [string]$pair[0]
    $target = [string]$pair[1]
    if ([string]::IsNullOrWhiteSpace($source) -or [string]::IsNullOrWhiteSpace($target)) { continue }

    # BUG CORE-005 FIX: Normalize whitespace identical to ConvertTo-GameKey (collapse all spaces)
    $sourceKey = [regex]::Replace((ConvertTo-AsciiFold -Text $source).Trim().ToLowerInvariant(), '\s+', '')
    $targetKey = [regex]::Replace((ConvertTo-AsciiFold -Text $target).Trim().ToLowerInvariant(), '\s+', '')
    if ([string]::IsNullOrWhiteSpace($sourceKey) -or [string]::IsNullOrWhiteSpace($targetKey)) { continue }
    $result[$sourceKey] = $targetKey
  }

  return $result
}

function Set-CustomAliasMapText {
  param(
    [string]$Text,
    [scriptblock]$Log
  )

  $custom = ConvertTo-CustomAliasMap -Text $Text

  $merged = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($k in $script:GAMEKEY_ALIAS_MAP_BASE.Keys) {
    $merged[$k] = [string]$script:GAMEKEY_ALIAS_MAP_BASE[$k]
  }
  foreach ($k in $custom.Keys) {
    $merged[$k] = [string]$custom[$k]
  }
  $script:GAMEKEY_ALIAS_MAP = $merged

  if ($Log) {
    & $Log ('Alias-Map aktiv: {0} Basis + {1} Custom = {2} Einträge' -f $script:GAMEKEY_ALIAS_MAP_BASE.Count, $custom.Count, $script:GAMEKEY_ALIAS_MAP.Count)
  }

  return $custom.Count
}
