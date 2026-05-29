param(
  [string]$MissingChinesePath,
  [string]$SourcePath,
  [string]$OutPath,
  [int]$Limit = 10,
  [string]$Entity,
  [int]$FromSourceId = 0,
  [int]$DelayMs = 1000,
  [int]$MaxRetries = 3,
  [string]$UserAgent = "PodexDesktopDataImporter/0.1 (Chinese catalog expansion)"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $MissingChinesePath) {
  $MissingChinesePath = Join-Path $Root "artifacts\missing-chinese.csv"
}
if (-not $SourcePath) {
  $SourcePath = Join-Path $Root "tools\import-data\source-cache\pokeapi-csv"
}
if (-not $OutPath) {
  $OutPath = Join-Path $Root "tools\import-data\overrides\zh-cn.csv"
}

function Read-RequiredCsv {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path)) {
    throw "CSV file not found: $Path"
  }
  return @(Import-Csv -LiteralPath $Path -Encoding UTF8)
}

function Get-LocalizedNameMap {
  param(
    [string]$Path,
    [string]$IdColumn,
    [string]$LanguageId
  )
  $map = @{}
  foreach ($row in (Read-RequiredCsv $Path)) {
    if ($row.local_language_id -eq $LanguageId -and $row.$IdColumn) {
      $map[[int]$row.$IdColumn] = $row.name
    }
  }
  return $map
}

function Get-QueryString {
  param([hashtable]$Params)
  $pairs = New-Object System.Collections.Generic.List[string]
  foreach ($key in $Params.Keys) {
    $pairs.Add(([uri]::EscapeDataString([string]$key) + "=" + [uri]::EscapeDataString([string]$Params[$key])))
  }
  return ($pairs -join "&")
}

function Invoke-WikiApi {
  param([hashtable]$Params)
  $uri = "https://wiki.52poke.com/api.php?" + (Get-QueryString $Params)
  for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
    try {
      $result = Invoke-RestMethod -Uri $uri -Headers @{ "User-Agent" = $UserAgent } -TimeoutSec 30
      if ($DelayMs -gt 0) {
        Start-Sleep -Milliseconds $DelayMs
      }
      return $result
    } catch {
      if ($attempt -ge $MaxRetries) {
        throw
      }
      $sleepMs = $DelayMs * $attempt
      if ($sleepMs -lt 1000) {
        $sleepMs = 1000
      }
      Write-Warning ("52poke request failed, retrying {0}/{1}: {2}" -f $attempt, $MaxRetries, $_.Exception.Message)
      Start-Sleep -Milliseconds $sleepMs
    }
  }
}

function Convert-IdentifierToSearchName {
  param([string]$Identifier)
  if ([string]::IsNullOrWhiteSpace($Identifier)) {
    return ""
  }
  $value = $Identifier -replace "--.*$", ""
  $words = @()
  foreach ($part in ($value -split "-")) {
    if ($part.Length -eq 0) { continue }
    if ($part.Length -eq 1) {
      $words += $part.ToUpperInvariant()
    } else {
      $words += $part.Substring(0, 1).ToUpperInvariant() + $part.Substring(1)
    }
  }
  return ($words -join " ")
}

function Get-EntitySuffix {
  param([string]$Entity)
  if ($Entity -eq "move") { return "（招式）" }
  if ($Entity -eq "ability") { return "（特性）" }
  if ($Entity -eq "item") { return "（道具）" }
  return ""
}

function Find-WikiTitle {
  param(
    [string]$Entity,
    [string]$ChineseName,
    [string]$EnglishName,
    [string]$Identifier
  )
  $suffix = Get-EntitySuffix $Entity
  if ([string]::IsNullOrWhiteSpace($suffix)) {
    return $null
  }

  if (-not [string]::IsNullOrWhiteSpace($ChineseName)) {
    $directTitles = @($ChineseName + $suffix, $ChineseName)
    foreach ($directTitle in $directTitles) {
      try {
        $content = Get-WikiText $directTitle
        if (-not [string]::IsNullOrWhiteSpace($content)) {
          return [pscustomobject]@{
            Title = $directTitle
            Snippet = ""
          }
        }
      } catch {
      }
    }
  }

  $queries = New-Object System.Collections.Generic.List[string]
  if (-not [string]::IsNullOrWhiteSpace($ChineseName)) {
    $queries.Add('"' + $ChineseName + '"')
  }
  if (-not [string]::IsNullOrWhiteSpace($EnglishName)) {
    $queries.Add('"' + $EnglishName + '"')
  }
  $fallback = Convert-IdentifierToSearchName $Identifier
  if (-not [string]::IsNullOrWhiteSpace($fallback) -and $fallback -ne $EnglishName) {
    $queries.Add('"' + $fallback + '"')
  }

  foreach ($query in $queries) {
    $result = Invoke-WikiApi @{
      action = "query"
      list = "search"
      srsearch = $query
      srlimit = "10"
      format = "json"
    }
    foreach ($hit in @($result.query.search)) {
      if ($hit.title -like "*$suffix") {
        return [pscustomobject]@{
          Title = $hit.title
          Snippet = $hit.snippet
        }
      }
    }
  }

  return $null
}

function Test-WikiPageMatch {
  param(
    [string]$Entity,
    [string]$Content,
    [string]$ChineseName,
    [string]$EnglishName
  )
  if ([string]::IsNullOrWhiteSpace($Content)) {
    return $false
  }

  if ($Entity -eq "move" -and $Content -notmatch "\{\{招式信息框") {
    return $false
  }
  if ($Entity -eq "ability" -and $Content -notmatch "\{\{特性信息框") {
    return $false
  }
  if ($Entity -eq "item" -and $Content -notmatch "\{\{道具信息框") {
    return $false
  }

  $pageEnglishName = Get-InfoboxField $Content @("enname")
  if (-not [string]::IsNullOrWhiteSpace($EnglishName) -and -not [string]::IsNullOrWhiteSpace($pageEnglishName)) {
    return $pageEnglishName.Equals($EnglishName, [System.StringComparison]::OrdinalIgnoreCase)
  }

  $pageChineseName = Get-EntityName $Content
  if (-not [string]::IsNullOrWhiteSpace($ChineseName) -and -not [string]::IsNullOrWhiteSpace($pageChineseName)) {
    return (Convert-TraditionalTerms $pageChineseName) -eq (Convert-TraditionalTerms $ChineseName)
  }

  return $true
}

function Get-WikiText {
  param([string]$Title)
  $result = Invoke-WikiApi @{
    action = "query"
    prop = "revisions"
    rvprop = "content"
    rvslots = "main"
    titles = $Title
    format = "json"
  }
  $page = @($result.query.pages.PSObject.Properties.Value)[0]
  if (-not $page -or -not $page.revisions) {
    return ""
  }
  $main = $page.revisions[0].slots.main
  $contentProperty = $main.PSObject.Properties["*"]
  if ($contentProperty) {
    return [string]$contentProperty.Value
  }
  if ($main.content) {
    return [string]$main.content
  }
  return ""
}

function Use-ZhHans {
  param([string]$Text)
  if ($null -eq $Text) {
    return ""
  }
  $options = [System.Text.RegularExpressions.RegexOptions]::Singleline
  $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $match.Groups[1].Value }
  $value = [regex]::Replace($Text, "-\{zh-hans:(.*?);zh-hant:.*?\}-", $evaluator, $options)
  $value = [regex]::Replace($value, "-\{zh-cn:(.*?);zh-tw:.*?\}-", $evaluator, $options)
  return $value
}

function Convert-WikiTextToPlain {
  param([string]$Text)
  $value = Use-ZhHans $Text
  $value = [regex]::Replace($value, "<!--.*?-->", "", [System.Text.RegularExpressions.RegexOptions]::Singleline)
  $value = [regex]::Replace($value, "<ref[^>]*>.*?</ref>", "", [System.Text.RegularExpressions.RegexOptions]::Singleline)
  $value = [regex]::Replace($value, "<[^>]+>", "")
  $value = [regex]::Replace($value, "\{\{招式效果/击中要害\|[^}]+\}\}", "容易击中要害。")
  $value = [regex]::Replace($value, "\{\{招式效果/麻痹\|([^|}]+)\}\}", '有$1%的几率使目标陷入麻痹状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/畏缩\|([^|}]+)\}\}", '有$1%的几率使目标畏缩。')
  $value = [regex]::Replace($value, "\{\{招式效果/灼伤\|([^|}]+)\}\}", '有$1%的几率使目标陷入灼伤状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/中毒\|([^|}]+)\}\}", '有$1%的几率使目标陷入中毒状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/冰冻\|([^|}]+)\}\}", '有$1%的几率使目标陷入冰冻状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/睡眠\|([^|}]+)\}\}", '有$1%的几率使目标陷入睡眠状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/混乱\|([^|}]+)\}\}", '有$1%的几率使目标陷入混乱状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/吸取\|([^|}]+)\}\}", '自身回复造成伤害$1%的ＨＰ。')
  $value = [regex]::Replace($value, "\{\{招式效果/回复ＨＰ\|([^|}]+)\|([^|}]+)\}\}", '$2回复最大ＨＰ的$1%。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力降低\|([^|}]+)\|([^|}]+)\|\|([^|}]+)\}\}", '使$3的$1降低$2级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力降低\|([^|}]+)\|([^|}]+)\}\}", '使目标的$1降低$2级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力降低\|([^|}]+)\}\}", '使目标的$1降低1级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力提升\|([^|}]+)\|([^|}]+)\}\}", '使使用者的$1提高$2级。')
  $value = [regex]::Replace($value, "\{\{招式效果/保护\|([^|}]+)\}\}", "进入守住状态。")
  $value = [regex]::Replace($value, "\{\{招式效果/反作用力伤害\|([^|}]+)\}\}", '使用者承受对目标造成伤害1/$1的反作用力伤害。')
  $value = [regex]::Replace($value, "\{\{招式效果/固定伤害\|最大ＨＰ的\{\{frac\|1\|2\}\}（向上取整）\|使用者\}\}", '使用者失去最大ＨＰ的1/2（向上取整）。')
  $value = [regex]::Replace($value, "\{\{招式效果/连续\|([^|}]+)\|[^}]+\}\}", '连续攻击$1次。')
  $value = [regex]::Replace($value, "\{\{招式效果/不能连续使用\|([^|}]+)\}\}", '不能连续使用。')
  $value = [regex]::Replace($value, "\{\{招式效果/必中\}\}", "攻击必定会命中。")
  $value = [regex]::Replace($value, "\{\{招式效果/解冻[^}]*\}\}", "使用后可以解除自己的冰冻状态。")
  $value = [regex]::Replace($value, "\{\{招式效果/多种异常\|([^|}]+)\|([^|}]+)\|([^|}]+)\|([^|}]+)\}\}", '有$1%的几率使目标陷入$2状态、$3状态或$4状态。')
  $value = [System.Net.WebUtility]::HtmlDecode($value)
  $value = [regex]::Replace($value, "\{\{type\|([^}|]+)\}\}", '$1属性')
  $value = [regex]::Replace($value, "\{\{s\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{stat\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{a\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{MSP\|[^}]+\}\}", "")
  $value = [regex]::Replace($value, "\[\[[^|\]]+\|([^\]]+)\]\]", '$1')
  $value = [regex]::Replace($value, "\[\[([^\]]+)\]\]", '$1')
  $value = [regex]::Replace($value, "\{\{[^{}]+\}\}", "")
  $value = Convert-TraditionalTerms $value
  $value = [regex]::Replace($value, "'''?", "")
  $value = [regex]::Replace($value, "(?m)^\s*[*#:;]+\s*", "")
  $value = [regex]::Replace($value, "(?m)^\s*[\{\}\|!].*$", "")
  $value = [regex]::Replace($value, "\s+", " ")
  return $value.Trim()
}

function Convert-TraditionalTerms {
  param([string]$Text)
  $value = $Text
  $replacements = [ordered]@{
    "連續" = "连续"
    "攻擊" = "攻击"
    "對手" = "对手"
    "對" = "对"
    "兩" = "两"
    "搭檔" = "搭档"
    "夥伴" = "伙伴"
    "場上" = "场上"
    "場" = "场"
    "信號" = "信号"
    "號" = "号"
    "會" = "会"
    "屬性" = "属性"
    "屬" = "属"
    "寶可夢" = "宝可梦"
    "寶" = "宝"
    "狀態" = "状态"
    "狀" = "状"
    "變為" = "变为"
    "變化" = "变化"
    "變" = "变"
    "防禦" = "防御"
    "出現" = "出现"
    "龍" = "龙"
    "劍" = "剑"
    "類" = "类"
    "擁有" = "拥有"
    "擁" = "拥"
    "個" = "个"
    "遊戲" = "游戏"
    "請見" = "请见"
    "攜帶" = "携带"
    "攜" = "携"
    "無" = "无"
    "與" = "与"
    "並" = "并"
    "為" = "为"
    "傷害" = "伤害"
    "觸發" = "触发"
    "極" = "极"
    "噴發" = "喷发"
    "噴" = "喷"
    "發" = "发"
    "滿" = "满"
    "時" = "时"
    "換" = "换"
    "雙" = "双"
    "將" = "将"
    "讓" = "让"
    "處於" = "处于"
    "處" = "处"
    "無法" = "无法"
    "異常" = "异常"
    "級" = "级"
    "敵方" = "敌方"
    "目標" = "目标"
    "標" = "标"
    "優先度" = "优先度"
    "優" = "优"
    "原來" = "原来"
    "來" = "来"
    "學会" = "学会"
    "擲" = "掷"
    "幣" = "币"
    "對戰" = "对战"
    "獲勝" = "获胜"
    "這些" = "这些"
    "白霧" = "白雾"
    "該" = "该"
    "傳遞給" = "传递给"
    "傳" = "传"
    "給" = "给"
    "後" = "后"
  }
  foreach ($key in $replacements.Keys) {
    $value = $value.Replace($key, $replacements[$key])
  }
  return $value
}

function Test-TextQuality {
  param([string]$Text)
  if ([string]::IsNullOrWhiteSpace($Text)) {
    return $false
  }
  if ($Text.Length -gt 280) {
    return $false
  }
  if ($Text -match "<[^>]+>|&[a-zA-Z#0-9]+;|\{\{|\}\}|\[\[|\]\]") {
    return $false
  }
  if ($Text -match "[對連寶會場號屬狀態變體檔擊兩現龍劍類擁個遊戲請見攜無與並為傷觸禦極噴發滿時換雙將讓處異級敵標優來學擲幣獲這該傳給後]") {
    return $false
  }
  if ($Text -match "日文︰|英文︰|是第[一二三四五六七八九十]+世代引入|目前类似|游戏漏洞|；\s*；|如、|、等|拥有、|^.*（日文") {
    return $false
  }
  if ($Text -match "的的|可被的|陷入和状态|例如）|特性为或|特性（如|如等|因素：.*；\s*；|无视、|、、、|、、|===|受到接触类招式的攻击时，\s*$|若＞|若＜|该招式优先度\\+1|结实特性不能阻止|（）|的（向|最大ＨＰ的（|使用者的、|目标的、|或等|携带了或|特性为无法|的该副作用|对的该副作用|会 会|自身回复造成伤害$|威力 = [^。]*×\s*。|鳞粉或防止|使用等招式|（向下取整）的反作用力") {
    return $false
  }
  return $true
}

function Test-OverrideRowQuality {
  param([object]$Row)
  if ($Row.zhCN_name -and -not (Test-TextQuality $Row.zhCN_name)) {
    return $false
  }
  if ($Row.zhCN_description -and -not (Test-TextQuality $Row.zhCN_description)) {
    return $false
  }
  return $true
}

function Save-Overrides {
  param(
    [string]$Path,
    [object[]]$ExistingRows,
    [System.Collections.Generic.List[object]]$NewRows
  )
  $outDirectory = Split-Path -Parent $Path
  if (-not [string]::IsNullOrWhiteSpace($outDirectory)) {
    New-Item -ItemType Directory -Force -Path $outDirectory | Out-Null
  }

  $allRows = New-Object System.Collections.Generic.List[object]
  foreach ($row in @($ExistingRows)) {
    $allRows.Add($row)
  }
  foreach ($row in $NewRows) {
    $allRows.Add($row)
  }
  $allRows | Export-Csv -LiteralPath $Path -Encoding UTF8 -NoTypeInformation
}

function Get-InfoboxField {
  param(
    [string]$Content,
    [string[]]$Names
  )
  $value = Use-ZhHans $Content
  foreach ($name in $Names) {
    $pattern = "(?m)^\|\s*" + [regex]::Escape($name) + "\s*=\s*(?<value>.+?)\s*$"
    $match = [regex]::Match($value, $pattern)
    if ($match.Success) {
      $plain = Convert-WikiTextToPlain $match.Groups["value"].Value
      if (-not [string]::IsNullOrWhiteSpace($plain)) {
        return $plain
      }
    }
  }
  return ""
}

function Get-SectionBody {
  param(
    [string]$Content,
    [string]$Name
  )
  $pattern = "(?ms)^==\s*" + [regex]::Escape($Name) + "\s*==\s*(?<body>.*?)(?=^==[^=].*?==|\z)"
  $match = [regex]::Match($Content, $pattern)
  if ($match.Success) {
    return $match.Groups["body"].Value
  }
  return ""
}

function Get-ItemBagDescription {
  param([string]$Content)
  $value = Use-ZhHans $Content
  $bestGeneration = -1
  $bestDescription = ""
  foreach ($line in ($value -split "`n")) {
    if ($line -notmatch "\{\{包包信息框\|") { continue }
    if ($line -match "\{\{包包信息框/h") { continue }
    $inner = $line.Trim()
    $inner = $inner -replace "^\{\{", ""
    $inner = $inner -replace "\}\}$", ""
    $parts = $inner -split "\|"
    if ($parts.Count -lt 6 -or $parts[0] -ne "包包信息框") { continue }
    $generation = 0
    [void][int]::TryParse($parts[1], [ref]$generation)
    $description = Convert-WikiTextToPlain $parts[5]
    if (-not [string]::IsNullOrWhiteSpace($description) -and $generation -ge $bestGeneration) {
      $bestGeneration = $generation
      $bestDescription = $description
    }
  }
  return $bestDescription
}

function Get-EntityName {
  param([string]$Content)
  return Get-InfoboxField $Content @("name")
}

function Get-EntityDescription {
  param(
    [string]$Entity,
    [string]$Content,
    [string]$Snippet
  )
  if ($Entity -eq "move") {
    $description = Convert-WikiTextToPlain (Get-SectionBody $Content "招式附加效果")
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain (Get-SectionBody $Content "招式说明")
    }
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain $Snippet
    }
    return $description
  }

  if ($Entity -eq "ability") {
    $description = Get-InfoboxField $Content @("text")
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain (Get-SectionBody $Content "特性效果")
    }
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain $Snippet
    }
    return $description
  }

  if ($Entity -eq "item") {
    $description = Get-ItemBagDescription $Content
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain (Get-SectionBody $Content "使用效果")
    }
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain $Snippet
    }
    return $description
  }

  return ""
}

$moveEnglishNames = Get-LocalizedNameMap (Join-Path $SourcePath "move_names.csv") "move_id" "9"
$moveChineseNames = Get-LocalizedNameMap (Join-Path $SourcePath "move_names.csv") "move_id" "12"
$abilityEnglishNames = Get-LocalizedNameMap (Join-Path $SourcePath "ability_names.csv") "ability_id" "9"
$abilityChineseNames = Get-LocalizedNameMap (Join-Path $SourcePath "ability_names.csv") "ability_id" "12"
$itemEnglishNames = Get-LocalizedNameMap (Join-Path $SourcePath "item_names.csv") "item_id" "9"
$itemChineseNames = Get-LocalizedNameMap (Join-Path $SourcePath "item_names.csv") "item_id" "12"
$missingRows = Read-RequiredCsv $MissingChinesePath

$existingRows = @()
$seen = @{}
if (Test-Path -LiteralPath $OutPath) {
  $loadedRows = @(Import-Csv -LiteralPath $OutPath -Encoding UTF8)
  foreach ($row in $loadedRows) {
    if (-not (Test-OverrideRowQuality $row)) {
      Write-Warning ("Dropped low-quality existing override for {0} {1}" -f $row.entity, $row.source_id)
      continue
    }
    $existingRows += $row
    if ($row.entity -and $row.source_id) {
      $seen[$row.entity + "|" + $row.source_id] = $true
    }
  }
}

$newRows = New-Object System.Collections.Generic.List[object]
$processed = 0

foreach ($row in $missingRows) {
  if ($Limit -gt 0 -and $processed -ge $Limit) { break }
  if (-not $row.entity -or -not $row.source_id) { continue }
  $sourceId = [int]$row.source_id
  if (-not [string]::IsNullOrWhiteSpace($Entity) -and $row.entity -ne $Entity) { continue }
  if ($FromSourceId -gt 0 -and $sourceId -lt $FromSourceId) { continue }
  $key = $row.entity + "|" + $row.source_id
  if ($seen.ContainsKey($key)) { continue }

  $englishName = ""
  $chineseName = ""
  if ($row.entity -eq "move") {
    if ($moveEnglishNames.ContainsKey($sourceId)) { $englishName = $moveEnglishNames[$sourceId] }
    if ($moveChineseNames.ContainsKey($sourceId)) { $chineseName = $moveChineseNames[$sourceId] }
  } elseif ($row.entity -eq "ability") {
    if ($abilityEnglishNames.ContainsKey($sourceId)) { $englishName = $abilityEnglishNames[$sourceId] }
    if ($abilityChineseNames.ContainsKey($sourceId)) { $chineseName = $abilityChineseNames[$sourceId] }
  } elseif ($row.entity -eq "item") {
    if ($itemEnglishNames.ContainsKey($sourceId)) { $englishName = $itemEnglishNames[$sourceId] }
    if ($itemChineseNames.ContainsKey($sourceId)) { $chineseName = $itemChineseNames[$sourceId] }
  }

  Write-Output ("Resolving {0} {1} {2} / {3}" -f $row.entity, $row.source_id, $chineseName, $englishName)
  try {
    $titleHit = Find-WikiTitle $row.entity $chineseName $englishName $row.identifier
  } catch {
    Write-Warning ("Failed to search 52poke for {0} {1}: {2}" -f $row.entity, $row.source_id, $_.Exception.Message)
    $processed++
    continue
  }
  if (-not $titleHit) {
    Write-Warning ("No 52poke page found for {0} {1} ({2})" -f $row.entity, $row.source_id, $englishName)
    $processed++
    continue
  }

  try {
    $content = Get-WikiText $titleHit.Title
  } catch {
    Write-Warning ("Failed to fetch 52poke page {0}: {1}" -f $titleHit.Title, $_.Exception.Message)
    $processed++
    continue
  }

  if (-not (Test-WikiPageMatch $row.entity $content $chineseName $englishName)) {
    Write-Warning ("Rejected mismatched 52poke page for {0} {1}: {2}" -f $row.entity, $row.source_id, $titleHit.Title)
    $processed++
    continue
  }
  if ([string]::IsNullOrWhiteSpace($content)) {
    Write-Warning ("No wiki text found for {0}" -f $titleHit.Title)
    $processed++
    continue
  }

  $name = ""
  if ($row.missing_name -eq "1") {
    $name = Get-EntityName $content
  }

  $description = ""
  if ($row.missing_description -eq "1") {
    $description = Get-EntityDescription $row.entity $content $titleHit.Snippet
  }

  if (($row.missing_name -eq "1" -and [string]::IsNullOrWhiteSpace($name)) -or
      ($row.missing_description -eq "1" -and [string]::IsNullOrWhiteSpace($description))) {
    Write-Warning ("Incomplete Chinese text for {0} {1} from {2}" -f $row.entity, $row.source_id, $titleHit.Title)
    $processed++
    continue
  }

  if (($row.missing_name -eq "1" -and -not (Test-TextQuality $name)) -or
      ($row.missing_description -eq "1" -and -not (Test-TextQuality $description))) {
    Write-Warning ("Rejected low-quality Chinese text for {0} {1} from {2}" -f $row.entity, $row.source_id, $titleHit.Title)
    $processed++
    continue
  }

  $newRows.Add([pscustomobject]@{
    entity = $row.entity
    source_id = $row.source_id
    identifier = $row.identifier
    zhCN_name = $name
    zhCN_description = $description
    source_title = $titleHit.Title
    source_url = "https://wiki.52poke.com/wiki/" + [uri]::EscapeDataString($titleHit.Title)
    license = "CC BY-NC-SA 3.0"
  })
  $seen[$key] = $true
  Save-Overrides $OutPath $existingRows $newRows
  $processed++
}

Save-Overrides $OutPath $existingRows $newRows

Write-Output ("Chinese overrides written: {0}" -f $OutPath)
Write-Output ("New overrides: {0}" -f $newRows.Count)
