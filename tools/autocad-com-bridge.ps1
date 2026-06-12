param(
  [Parameter(Mandatory=$true, Position=0)]
  [ValidateSet('ping','snapshot','layers','add-layer','add-line','add-rectangle','add-text','send-command')]
  [string]$Action,

  [string]$Layer = '',
  [int]$Color = 7,
  [double]$X1 = 0,
  [double]$Y1 = 0,
  [double]$X2 = 1000,
  [double]$Y2 = 0,
  [double]$X = 0,
  [double]$Y = 0,
  [double]$Width = 1000,
  [double]$Height = 1000,
  [string]$Text = '',
  [double]$TextHeight = 250,
  [string]$Command = '',
  [int]$Limit = 300
)

$ErrorActionPreference = 'Stop'

function Out-Json($obj) {
  $obj | ConvertTo-Json -Depth 12 -Compress
}

function Get-AcadApp {
  try {
    return [Runtime.InteropServices.Marshal]::GetActiveObject('AutoCAD.Application')
  } catch {
    throw 'AutoCAD COM server is not running. Start AutoCAD first, then retry.'
  }
}

function Pt([double]$x, [double]$y, [double]$z = 0) {
  return [double[]]@($x, $y, $z)
}

function SafeProp($obj, [string]$name, $fallback = $null) {
  try { return $obj.$name } catch { return $fallback }
}

function Get-Doc {
  $app = Get-AcadApp
  $doc = $app.ActiveDocument
  if ($null -eq $doc) { throw 'AutoCAD has no active document.' }
  return $doc
}

function Ensure-Layer($doc, [string]$name, [int]$color) {
  if ([string]::IsNullOrWhiteSpace($name)) { return $null }
  try {
    $layerObj = $doc.Layers.Item($name)
  } catch {
    $layerObj = $doc.Layers.Add($name)
  }
  try { $layerObj.Color = $color } catch {}
  return $layerObj
}

function Set-EntityLayer($entity, [string]$layerName) {
  if ([string]::IsNullOrWhiteSpace($layerName)) { return }
  try { $entity.Layer = $layerName } catch {}
}

function Entity-Summary($entity) {
  $objName = SafeProp $entity 'ObjectName' ''
  $summary = [ordered]@{
    object_name = $objName
    handle = SafeProp $entity 'Handle' ''
    layer = SafeProp $entity 'Layer' ''
    color = SafeProp $entity 'Color' $null
  }

  if ($objName -match 'Line') {
    $summary.start = SafeProp $entity 'StartPoint' $null
    $summary.end = SafeProp $entity 'EndPoint' $null
  } elseif ($objName -match 'Text') {
    $summary.text = SafeProp $entity 'TextString' ''
    $summary.position = SafeProp $entity 'InsertionPoint' $null
    $summary.height = SafeProp $entity 'Height' $null
  } elseif ($objName -match 'Polyline') {
    $coords = SafeProp $entity 'Coordinates' $null
    if ($null -ne $coords) { $summary.coordinates = @($coords) }
  } elseif ($objName -match 'BlockReference') {
    $summary.name = SafeProp $entity 'Name' ''
    $summary.position = SafeProp $entity 'InsertionPoint' $null
  }

  return $summary
}

$doc = Get-Doc

switch ($Action) {
  'ping' {
    Out-Json ([ordered]@{
      ok = $true
      app = SafeProp (Get-AcadApp) 'Name' 'AutoCAD'
      document = SafeProp $doc 'Name' ''
      full_name = SafeProp $doc 'FullName' ''
    })
  }

  'layers' {
    $items = @()
    foreach ($layerObj in $doc.Layers) {
      $items += [ordered]@{
        name = SafeProp $layerObj 'Name' ''
        color = SafeProp $layerObj 'Color' $null
        lock = SafeProp $layerObj 'Lock' $null
        freeze = SafeProp $layerObj 'Freeze' $null
        layer_on = SafeProp $layerObj 'LayerOn' $null
      }
    }
    Out-Json ([ordered]@{ ok = $true; document = $doc.Name; layers = $items })
  }

  'snapshot' {
    $entities = @()
    $count = 0
    foreach ($entity in $doc.ModelSpace) {
      $count++
      if ($entities.Count -lt $Limit) {
        $entities += Entity-Summary $entity
      }
    }
    Out-Json ([ordered]@{
      ok = $true
      document = $doc.Name
      full_name = SafeProp $doc 'FullName' ''
      model_space_count = $count
      returned = $entities.Count
      entities = $entities
    })
  }

  'add-layer' {
    if ([string]::IsNullOrWhiteSpace($Layer)) { throw '-Layer is required.' }
    $layerObj = Ensure-Layer $doc $Layer $Color
    Out-Json ([ordered]@{ ok = $true; action = 'add-layer'; name = $layerObj.Name; color = $layerObj.Color })
  }

  'add-line' {
    Ensure-Layer $doc $Layer $Color | Out-Null
    $entity = $doc.ModelSpace.AddLine((Pt $X1 $Y1), (Pt $X2 $Y2))
    Set-EntityLayer $entity $Layer
    try { $entity.Update() } catch {}
    Out-Json ([ordered]@{ ok = $true; action = 'add-line'; entity = (Entity-Summary $entity) })
  }

  'add-rectangle' {
    Ensure-Layer $doc $Layer $Color | Out-Null
    $p1 = Pt $X $Y
    $p2 = Pt ($X + $Width) $Y
    $p3 = Pt ($X + $Width) ($Y + $Height)
    $p4 = Pt $X ($Y + $Height)
    $handles = @()
    foreach ($pair in @(@($p1,$p2), @($p2,$p3), @($p3,$p4), @($p4,$p1))) {
      $line = $doc.ModelSpace.AddLine($pair[0], $pair[1])
      Set-EntityLayer $line $Layer
      try { $line.Update() } catch {}
      $handles += (SafeProp $line 'Handle' '')
    }
    Out-Json ([ordered]@{ ok = $true; action = 'add-rectangle'; handles = $handles; layer = $Layer })
  }

  'add-text' {
    if ([string]::IsNullOrWhiteSpace($Text)) { throw '-Text is required.' }
    Ensure-Layer $doc $Layer $Color | Out-Null
    $entity = $doc.ModelSpace.AddText($Text, (Pt $X $Y), $TextHeight)
    Set-EntityLayer $entity $Layer
    try { $entity.Update() } catch {}
    Out-Json ([ordered]@{ ok = $true; action = 'add-text'; entity = (Entity-Summary $entity) })
  }

  'send-command' {
    if ([string]::IsNullOrWhiteSpace($Command)) { throw '-Command is required.' }
    $doc.SendCommand($Command + "`n")
    Out-Json ([ordered]@{ ok = $true; action = 'send-command'; command = $Command; note = 'AutoCAD SendCommand is asynchronous.' })
  }
}
