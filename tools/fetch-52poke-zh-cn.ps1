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
  if ($Entity -eq "pokemon") { return "" }
  return ""
}

function Find-WikiTitle {
  param(
    [string]$Entity,
    [string]$ChineseName,
    [string]$EnglishName,
    [string]$Identifier,
    [int]$SourceId = 0
  )
  $suffix = Get-EntitySuffix $Entity
  if ([string]::IsNullOrWhiteSpace($suffix) -and $Entity -ne "pokemon") {
    return $null
  }

  if (-not [string]::IsNullOrWhiteSpace($ChineseName)) {
    $directTitles = if ([string]::IsNullOrWhiteSpace($suffix)) { @($ChineseName) } else { @($ChineseName + $suffix, $ChineseName) }
    foreach ($directTitle in $directTitles) {
      try {
        $content = Get-WikiText $directTitle
        if (Test-WikiPageMatch $Entity $content $ChineseName $EnglishName $SourceId) {
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
      $isExactEntityTitle = -not [string]::IsNullOrWhiteSpace($ChineseName) -and $hit.title -eq $ChineseName
        if (($suffix -and $hit.title -like "*$suffix") -or $Entity -eq "move" -or $Entity -eq "pokemon" -or $isExactEntityTitle) {
        try {
          $content = Get-WikiText $hit.title
        } catch {
          continue
        }
        if (-not (Test-WikiPageMatch $Entity $content $ChineseName $EnglishName $SourceId)) {
          continue
        }
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
    [string]$EnglishName,
    [int]$SourceId = 0
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
  if ($Entity -eq "pokemon" -and
      $Content -notmatch "\{\{寶可夢信息框" -and
      $Content -notmatch "\{\{宝可梦信息框") {
    return $false
  }
  if ($Entity -eq "item") {
    $hasItemTemplate = $Content -match "\{\{道具信息框" -or
      $Content -match "\{\{TRtable" -or
      $Content -match "\{\{TMtable"
    if (-not $hasItemTemplate) {
      return $false
    }
  }

  $pageEnglishName = Get-InfoboxField $Content @("enname")
  if ($Entity -eq "item" -and [string]::IsNullOrWhiteSpace($pageEnglishName)) {
    $pageEnglishName = Get-ItemTemplateField $Content 4
  }
  if (-not [string]::IsNullOrWhiteSpace($EnglishName) -and -not [string]::IsNullOrWhiteSpace($pageEnglishName)) {
    return (Normalize-MatchText $pageEnglishName).Equals((Normalize-MatchText $EnglishName), [System.StringComparison]::OrdinalIgnoreCase)
  }

  if ($Entity -eq "move" -and $SourceId -gt 0) {
    $pageSourceId = Get-InfoboxField $Content @("n")
    $parsedSourceId = 0
    if (-not [string]::IsNullOrWhiteSpace($pageSourceId) -and
        [int]::TryParse($pageSourceId, [ref]$parsedSourceId) -and
        $parsedSourceId -ne $SourceId) {
      return $false
    }
  }
  if ($Entity -eq "pokemon" -and $SourceId -gt 0) {
    $pageSourceId = Get-InfoboxField $Content @("ndex")
    $parsedSourceId = 0
    if (-not [string]::IsNullOrWhiteSpace($pageSourceId) -and
        [int]::TryParse($pageSourceId, [ref]$parsedSourceId) -and
        $parsedSourceId -ne $SourceId) {
      return $false
    }
  }

  $pageChineseName = Get-EntityName $Content
  if ($Entity -eq "item") {
    $templateChineseName = Get-ItemTemplateField $Content 1
    if (-not [string]::IsNullOrWhiteSpace($templateChineseName)) {
      $pageChineseName = $templateChineseName
    }
  }
  if (-not [string]::IsNullOrWhiteSpace($ChineseName) -and -not [string]::IsNullOrWhiteSpace($pageChineseName)) {
    return (Normalize-MatchText (Convert-TraditionalTerms $pageChineseName)) -eq (Normalize-MatchText (Convert-TraditionalTerms $ChineseName))
  }

  return $true
}

function Normalize-MatchText {
  param([string]$Text)
  if ($null -eq $Text) {
    return ""
  }
  $value = $Text.Replace([char]0x2018, "'").Replace([char]0x2019, "'").Replace([char]0x02BC, "'")
  return ([regex]::Replace($value, "[\p{C}\s]+", "")).Trim()
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
  $value = [regex]::Replace($value, "[\u200e\u200f\u202a-\u202e\ufeff]", "")
  $value = [regex]::Replace($value, "<!--.*?-->", "", [System.Text.RegularExpressions.RegexOptions]::Singleline)
  $value = [regex]::Replace($value, "<ref[^>]*>.*?</ref>", "", [System.Text.RegularExpressions.RegexOptions]::Singleline)
  $value = [regex]::Replace($value, "<[^>]+>", "")
  $value = [regex]::Replace($value, "目前[类似類似].*?遊戲漏洞.*?。", "")
  $value = [regex]::Replace($value, "\{\{招式效果/击中要害(?:\|[^}]*)?\}\}", "容易击中要害。")
  $value = [regex]::Replace($value, "\{\{招式效果/畏缩\}\}", "使目标畏缩。")
  $value = [regex]::Replace($value, "\{\{招式效果/麻痹\|([^|}]+)\}\}", '有$1%的几率使目标陷入麻痹状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/畏缩\|([^|}]+)\}\}", '有$1%的几率使目标畏缩。')
  $value = [regex]::Replace($value, "\{\{招式效果/灼伤\|([^|}]+)\}\}", '有$1%的几率使目标陷入灼伤状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/中毒\|([^|}]+)\}\}", '有$1%的几率使目标陷入中毒状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/冰冻\|([^|}]+)\}\}", '有$1%的几率使目标陷入冰冻状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/睡眠\|([^|}]+)\}\}", '有$1%的几率使目标陷入睡眠状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/混乱\|([^|}]+)\}\}", '有$1%的几率使目标陷入混乱状态。')
  $value = [regex]::Replace($value, "\{\{招式效果/吸取\|([^|}]+)\}\}", '自身回复造成伤害$1%的ＨＰ。')
  $value = [regex]::Replace($value, "\{\{招式效果/回复ＨＰ\|([^|}]+)\|([^|}]+)\|异常=y\}\}", '治愈$2的异常状态，并使$2回复最大ＨＰ的$1%。')
  $value = [regex]::Replace($value, "\{\{招式效果/回复ＨＰ\|([^|}]+)\|([^|}]+)\}\}", '$2回复最大ＨＰ的$1%。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力降低\|([^|}]+)\|([^|}]+)\|\|([^|}]+)\}\}", '使$3的$1降低$2级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力降低\|([^|}]+)\|([^|}]+)\}\}", '使目标的$1降低$2级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力降低\|([^|}]+)\}\}", '使目标的$1降低1级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力提升\|([^|}]+)\|([^|}]+)\|\|([^|}]+)\}\}", '使$3的$1提高$2级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力提升\|全部\|([^|}]+)\}\}", '使使用者的攻击、防御、特攻、特防和速度提高$1级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力提升\|([^|}]+)\|([^|}]+)\}\}", '使使用者的$1提高$2级。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力提升\|([^|}]+)\}\}", '使使用者的$1提高1级。')
  $value = [regex]::Replace($value, "\{\{招式效果/威力翻倍\|([^}]+)\}\}", '攻击目标造成伤害。$1时，招式威力变成2倍。')
  $value = [regex]::Replace($value, "\{\{招式效果/能力取代\|([^|}]+)\|([^|}]+)\}\}", '使用$1代替$2计算伤害。')
  $value = [regex]::Replace($value, "\{\{招式效果/蓄力\|\|\{\{招式效果/能力提升\|特攻\|1\}\}\|note=.*?\}\}", "第一回合进行蓄力，使使用者的特攻提高1级。第二回合攻击。", [System.Text.RegularExpressions.RegexOptions]::Singleline)
  $value = [regex]::Replace($value, "\{\{招式效果/蓄力\|\|使使用者的特攻提高1级。\|note=.*?\}\}", "第一回合进行蓄力，使使用者的特攻提高1级。第二回合攻击。", [System.Text.RegularExpressions.RegexOptions]::Singleline)
  $value = [regex]::Replace($value, "\{\{招式效果/蓄力\|rain\|使使用者的特攻提高1级。\|note=.*?\}\}", "第一回合进行蓄力，使使用者的特攻提高1级。第二回合攻击。下雨天气时可立即发动。", [System.Text.RegularExpressions.RegexOptions]::Singleline)
  $value = [regex]::Replace($value, "\{\{招式效果/硬直\}\}", "攻击目标造成伤害。使用后下一回合无法行动。")
  $value = [regex]::Replace($value, "\{\{招式效果/多回合攻击\|([^|}]+)\}\}", '连续攻击$1回合。')
  $value = [regex]::Replace($value, "\{\{招式效果/天气影响\|([^|}]+)\|([^|}]+)\}\}", '攻击目标造成伤害。使天气变为$1。携带$2时持续时间延长。')
  $value = [regex]::Replace($value, "\{\{招式效果/保护\|([^|}]+)\}\}", "进入守住状态。")
  $value = [regex]::Replace($value, "\{\{招式效果/反作用力伤害\|([^|}]+)\}\}", '使用者承受对目标造成伤害1/$1的反作用力伤害。')
  $value = [regex]::Replace($value, "\{\{招式效果/固定伤害\|最大ＨＰ的\{\{frac\|1\|2\}\}（向上取整）\|使用者\}\}", '使用者失去最大ＨＰ的1/2（向上取整）。')
  $value = [regex]::Replace($value, "\{\{招式效果/连续\|([^|}]+)\|[^}]+\}\}", '连续攻击$1次。')
  $value = [regex]::Replace($value, "\{\{招式效果/不能连续使用\|([^|}]+)\}\}", '不能连续使用。')
  $value = [regex]::Replace($value, "\{\{招式效果/必中\}\}", "攻击必定会命中。")
  $value = [regex]::Replace($value, "\{\{招式效果/解冻[^}]*\}\}", "使用后可以解除自己的冰冻状态。")
  $value = [regex]::Replace($value, "\{\{招式效果/多种异常\|([^|}]+)\|([^|}]+)\|([^|}]+)\|([^|}]+)\}\}", '有$1%的几率使目标陷入$2状态、$3状态或$4状态。')
  $value = [regex]::Replace($value, "\{\{frac\|([^|}]+)\|([^|}]+)\}\}", '$1/$2')
  $value = [System.Net.WebUtility]::HtmlDecode($value)
  $value = [regex]::Replace($value, "\{\{main\|([^}|]+)\}\}", "")
  $value = [regex]::Replace($value, "\{\{NBPAGENAME\}\}", "该招式")
  $value = [regex]::Replace($value, "\{\{type\|([^}|]+)\}\}", '$1属性')
  $value = [regex]::Replace($value, "\{\{m\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{s\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{S\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{i\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{I\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{形态变化\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{stat\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{a\|([^}|]+)\}\}", '$1')
  $value = [regex]::Replace($value, "\{\{MSP\|[^}]+\}\}", "")
  $value = [regex]::Replace($value, "\[\[[^|\]]+\|([^\]]+)\]\]", '$1')
  $value = [regex]::Replace($value, "\[\[([^\]]+)\]\]", '$1')
  $value = [regex]::Replace($value, "\{\{[^{}]+\}\}", "")
  $value = Convert-TraditionalTerms $value
  $value = [regex]::Replace($value, "'''?", "")
  $value = [regex]::Replace($value, "(?m)^\s*=+[^=`r`n]+=+\s*", "")
  $value = [regex]::Replace($value, "(?m)^\s*[*#:;]+\s*", "")
  $value = [regex]::Replace($value, "(?m)^\s*[\{\}\|!].*$", "")
  $value = [regex]::Replace($value, "\s+", " ")
  $value = [regex]::Replace($value, "\s+([。！？；，])", '$1')
  $value = [regex]::Replace($value, "([。！？；，])\s+", '$1')
  $value = [regex]::Replace($value, "时时", "时")
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
    "隨" = "随"
    "屬性" = "属性"
    "屬" = "属"
    "寶可夢" = "宝可梦"
    "寶" = "宝"
    "狀態" = "状态"
    "狀" = "状"
    "變為" = "变为"
    "變化" = "变化"
    "變" = "变"
    "轉" = "转"
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
    "電氣" = "电气"
    "電" = "电"
    "氣" = "气"
    "強" = "强"
    "鑽" = "钻"
    "節" = "节"
    "鋼" = "钢"
    "蟲" = "虫"
    "飛" = "飞"
    "惡" = "恶"
    "霧" = "雾"
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
    "牠" = "它"
    "隻" = "只"
    "內" = "内"
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
  if ($Text -match "[對連寶會場號屬狀態變體檔擊兩現龍劍類擁個遊戲請見攜電氣強鑽節鋼蟲飛惡霧無與並為傷觸禦極噴發滿時換雙將讓處異級敵標優來學擲幣獲這該傳給後轉牠隻]") {
    return $false
  }
  if ($Text -match "日文︰|英文︰|是第[一二三四五六七八九十]+世代引入|目前类似|游戏漏洞|；\s*；|如、|、等|拥有、|^.*（日文") {
    return $false
  }
  if ($Text -match "^[ー—－-]+$") {
    return $false
  }
  if ($Text -match "的的|可被的|陷入和状态|例如）|特性为或|特性（如|如等|因素：.*；\s*；|无视、|、、、|、、|===|受到接触类招式的攻击时，\s*$|若＞|若＜|该招式优先度\\+1|结实特性不能阻止|（）|的（向|最大ＨＰ的（|使用者的、|目标的、|或等|携带了或|特性为无法|的该副作用|对的该副作用|会 会|时时|自身回复造成伤害$|威力 = [^。]*×\s*。|鳞粉或防止|使用等招式|（向下取整）的反作用力|全部提高|进入状态|陷入状态|携带的宝可梦|对特性的宝可梦|拥有特性的宝可梦|存在特性的宝可梦") {
    return $false
  }
  return $true
}

function Test-OverrideRowQuality {
  param([object]$Row)
  if ($Row.zhCN_name -and -not (Test-TextQuality $Row.zhCN_name)) {
    return $false
  }
  if ($Row.zhCN_genus -and -not (Test-TextQuality $Row.zhCN_genus)) {
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

function Get-ItemTemplateField {
  param(
    [string]$Content,
    [int]$FieldNumber
  )
  $value = Use-ZhHans $Content
  $match = [regex]::Match($value, "\{\{N\|(?<fields>.+?)\}\}")
  if (-not $match.Success) {
    return ""
  }
  $parts = $match.Groups["fields"].Value -split "\|"
  $index = $FieldNumber - 1
  if ($index -lt 0 -or $index -ge $parts.Count) {
    return ""
  }
  return Convert-WikiTextToPlain $parts[$index]
}

function Get-SectionBody {
  param(
    [string]$Content,
    [string]$Name
  )
  $pattern = "(?ms)^={2,}\s*" + [regex]::Escape($Name) + "\s*={2,}\s*(?<body>.*?)(?=^={2,}[^=`r`n].*?={2,}\s*$|\z)"
  $match = [regex]::Match($Content, $pattern)
  if ($match.Success) {
    return $match.Groups["body"].Value
  }
  return ""
}

function Get-ItemBagDescription {
  param(
    [string]$Content,
    [string]$Identifier = ""
  )
  $value = Use-ZhHans $Content
  if ($Identifier -match "--merge$") {
    $mergeDescription = Get-ItemBagDescriptionFromSection $value "合体前"
    if (-not [string]::IsNullOrWhiteSpace($mergeDescription)) {
      return $mergeDescription
    }
  }
  if ($Identifier -match "--split$") {
    $splitDescription = Get-ItemBagDescriptionFromSection $value "合体后"
    if (-not [string]::IsNullOrWhiteSpace($splitDescription)) {
      return $splitDescription
    }
  }

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

function Get-ItemBagDescriptionFromSection {
  param(
    [string]$Content,
    [string]$SectionName
  )
  $pattern = "(?ms)^={2,}\s*" + [regex]::Escape($SectionName) + "\s*={2,}\s*(?<body>.*?)(?=^={2,}[^=`r`n].*?={2,}\s*$|\z)"
  $match = [regex]::Match($Content, $pattern)
  $section = if ($match.Success) { $match.Groups["body"].Value } else { "" }
  if ([string]::IsNullOrWhiteSpace($section)) {
    return ""
  }

  $bestGeneration = -1
  $bestDescription = ""
  foreach ($line in ($section -split "`n")) {
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

function Get-PokemonPokedexDescription {
  param([string]$Content)

  $section = Get-SectionBody $Content "图鉴介绍"
  if ([string]::IsNullOrWhiteSpace($section)) {
    return ""
  }

  $bestDescription = ""
  foreach ($line in ($section -split "`n")) {
    if ($line -notmatch "^\|\s*[A-Za-z0-9]+dex\s*=\s*(?<value>.+?)\s*$") { continue }
    $rawDescription = ([regex]::Split($Matches["value"], "<hr\s*/?>"))[0]
    $description = Convert-WikiTextToPlain $rawDescription
    if (-not [string]::IsNullOrWhiteSpace($description)) {
      $bestDescription = $description
    }
  }
  return $bestDescription
}

function Get-ItemMachineDescription {
  param(
    [string]$Content,
    [string]$Identifier = ""
  )

  $value = Use-ZhHans $Content
  $isRecord = $Identifier -match "^tr\d{2}$" -or $value -match "\{\{TRtable"
  $isMachine = $Identifier -match "^tm\d{2}$" -or $value -match "\{\{TMtable"
  if (-not $isRecord -and -not $isMachine) {
    return ""
  }

  $moveName = ""
  foreach ($match in [regex]::Matches($value, "(?m)^\|\s*move\d+\s*=\s*(?<move>.+?)\s*$")) {
    $candidate = Convert-WikiTextToPlain $match.Groups["move"].Value
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
      $moveName = $candidate
    }
  }
  if ([string]::IsNullOrWhiteSpace($moveName)) {
    return ""
  }

  if ($isRecord) {
    return "收录着招式「$($moveName)」的招式记录。使用后能让宝可梦学会这个招式，用过之后就会消失。"
  }
  return "收录着招式「$($moveName)」的招式学习器。使用后能让宝可梦学会这个招式，可以使用多次。"
}

function Get-EntityName {
  param([string]$Content)
  $name = Get-InfoboxField $Content @("name")
  if ([string]::IsNullOrWhiteSpace($name)) {
    $name = Get-ItemTemplateField $Content 1
  }
  return $name
}

function Get-PokemonGenus {
  param([string]$Content)
  $species = Get-InfoboxField $Content @("species")
  if ([string]::IsNullOrWhiteSpace($species)) {
    return ""
  }
  if ($species.EndsWith("宝可梦")) {
    return $species
  }
  return $species + "宝可梦"
}

function Get-EntityDescription {
  param(
    [string]$Entity,
    [string]$Content,
    [string]$Snippet,
    [string]$Identifier = ""
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
    $description = Get-ItemBagDescription $Content $Identifier
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Get-ItemMachineDescription $Content $Identifier
    }
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain (Get-SectionBody $Content "使用效果")
    }
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain $Snippet
    }
    return $description
  }

  if ($Entity -eq "pokemon") {
    $description = Get-PokemonPokedexDescription $Content
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain (Get-SectionBody $Content "概述")
    }
    if ([string]::IsNullOrWhiteSpace($description)) {
      $description = Convert-WikiTextToPlain $Snippet
    }
    return $description
  }

  return ""
}

$pokemonEnglishNames = Get-LocalizedNameMap (Join-Path $SourcePath "pokemon_species_names.csv") "pokemon_species_id" "9"
$pokemonChineseNames = Get-LocalizedNameMap (Join-Path $SourcePath "pokemon_species_names.csv") "pokemon_species_id" "12"
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
  } elseif ($row.entity -eq "pokemon") {
    if ($pokemonEnglishNames.ContainsKey($sourceId)) { $englishName = $pokemonEnglishNames[$sourceId] }
    if ($pokemonChineseNames.ContainsKey($sourceId)) { $chineseName = $pokemonChineseNames[$sourceId] }
  } elseif ($row.entity -eq "ability") {
    if ($abilityEnglishNames.ContainsKey($sourceId)) { $englishName = $abilityEnglishNames[$sourceId] }
    if ($abilityChineseNames.ContainsKey($sourceId)) { $chineseName = $abilityChineseNames[$sourceId] }
  } elseif ($row.entity -eq "item") {
    if ($itemEnglishNames.ContainsKey($sourceId)) { $englishName = $itemEnglishNames[$sourceId] }
    if ($itemChineseNames.ContainsKey($sourceId)) { $chineseName = $itemChineseNames[$sourceId] }
  }

  Write-Output ("Resolving {0} {1} {2} / {3}" -f $row.entity, $row.source_id, $chineseName, $englishName)
  try {
    $titleHit = Find-WikiTitle $row.entity $chineseName $englishName $row.identifier $sourceId
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

  if (-not (Test-WikiPageMatch $row.entity $content $chineseName $englishName $sourceId)) {
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

  $genus = ""
  if ($row.entity -eq "pokemon") {
    $genus = Get-PokemonGenus $content
  }

  $description = ""
  if ($row.missing_description -eq "1") {
    $description = Get-EntityDescription $row.entity $content $titleHit.Snippet $row.identifier
  }

  if (($row.missing_name -eq "1" -and [string]::IsNullOrWhiteSpace($name)) -or
      ($row.missing_description -eq "1" -and [string]::IsNullOrWhiteSpace($description))) {
    Write-Warning ("Incomplete Chinese text for {0} {1} from {2}" -f $row.entity, $row.source_id, $titleHit.Title)
    $processed++
    continue
  }

  if (($row.missing_name -eq "1" -and -not (Test-TextQuality $name)) -or
      (-not [string]::IsNullOrWhiteSpace($genus) -and -not (Test-TextQuality $genus)) -or
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
    zhCN_genus = $genus
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
