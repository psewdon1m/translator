function Get-PeInfo {
  param([string]$Path)
  $bytes = [IO.File]::ReadAllBytes($Path)
  function ReadU16([int]$o){ [BitConverter]::ToUInt16($bytes,$o) }
  function ReadU32([int]$o){ [BitConverter]::ToUInt32($bytes,$o) }
  function ReadU64([int]$o){ [BitConverter]::ToUInt64($bytes,$o) }
  function ReadAsciiZ([int]$o){ $sb=New-Object Text.StringBuilder; while($o -lt $bytes.Length -and $bytes[$o] -ne 0){ [void]$sb.Append([char]$bytes[$o]); $o++ }; $sb.ToString() }

  $e_lfanew = ReadU32 0x3C
  $pe = ReadU32 $e_lfanew
  if ($pe -ne 0x4550) { throw "Not PE" }
  $coff = $e_lfanew + 4
  $numSections = ReadU16 ($coff + 2)
  $sizeOpt = ReadU16 ($coff + 16)
  $opt = $coff + 20
  $magic = ReadU16 $opt
  $is64 = $magic -eq 0x20B
  $ddBase = if($is64){ $opt + 112 } else { $opt + 96 }
  $exportRva = ReadU32 ($ddBase + 0)
  $importRva = ReadU32 ($ddBase + 8)
  $sections = @()
  $sec = $opt + $sizeOpt
  for($i=0;$i -lt $numSections;$i++){
    $off = $sec + 40*$i
    $nameBytes = $bytes[$off..($off+7)]
    $name = ([Text.Encoding]::ASCII.GetString($nameBytes)).Trim([char]0)
    $vsize = ReadU32 ($off+8)
    $vaddr = ReadU32 ($off+12)
    $rawSize = ReadU32 ($off+16)
    $rawPtr = ReadU32 ($off+20)
    $sections += [pscustomobject]@{Name=$name;VA=[uint32]$vaddr;VS=[uint32]$vsize;RawPtr=[uint32]$rawPtr;RawSize=[uint32]$rawSize}
  }
  function RvaToOff([uint32]$rva){
    foreach($s in $sections){
      $maxSize = [Math]::Max([uint32]$s.VS,[uint32]$s.RawSize)
      if($rva -ge [uint32]$s.VA -and $rva -lt ([uint32]$s.VA + $maxSize)){
        return [int]([uint32]$s.RawPtr + ($rva - [uint32]$s.VA))
      }
    }
    return $null
  }

  $exports = @()
  if($exportRva -ne 0){
    $eo = RvaToOff ([uint32]$exportRva)
    if($eo -ne $null){
      $numNames = ReadU32 ($eo + 24)
      $addrNames = ReadU32 ($eo + 32)
      $addrOrds = ReadU32 ($eo + 36)
      $no = RvaToOff ([uint32]$addrNames)
      $oo = RvaToOff ([uint32]$addrOrds)
      for($i=0;$i -lt $numNames;$i++){
        $nameRva = ReadU32 ($no + 4*$i)
        $ordIdx = ReadU16 ($oo + 2*$i)
        $exports += [pscustomobject]@{Name=(ReadAsciiZ (RvaToOff ([uint32]$nameRva))); Ordinal=$ordIdx}
      }
    }
  }

  $imports = @()
  if($importRva -ne 0){
    $io = RvaToOff ([uint32]$importRva)
    if($io -ne $null){
      $descSize = 20
      $highBit = ([UInt64]1 -shl 63)
      for($d=0;;$d++){
        $do = $io + $d*$descSize
        $origThunk = ReadU32 ($do + 0)
        $nameRva = ReadU32 ($do + 12)
        $firstThunk = ReadU32 ($do + 16)
        if($origThunk -eq 0 -and $nameRva -eq 0 -and $firstThunk -eq 0){ break }
        $dllName = ReadAsciiZ (RvaToOff ([uint32]$nameRva))
        $thunkRva = if($origThunk -ne 0){ $origThunk } else { $firstThunk }
        $to = RvaToOff ([uint32]$thunkRva)
        $funcs = New-Object System.Collections.Generic.List[string]
        if($to -ne $null){
          $step = if($is64){8}else{4}
          for($t=0;;$t++){
            if($is64){ [UInt64]$val = ReadU64 ($to + $t*$step) } else { [UInt32]$val = ReadU32 ($to + $t*$step) }
            if($val -eq 0){ break }
            $isOrdinal = if($is64){ (($val -band $highBit) -ne 0) } else { (($val -band 0x80000000) -ne 0) }
            if($isOrdinal){
              [void]$funcs.Add('#' + ($val -band 0xFFFF))
            } else {
              $hnRva = [uint32]$val
              $hnOff = RvaToOff $hnRva
              if($hnOff -ne $null){ [void]$funcs.Add((ReadAsciiZ ($hnOff + 2))) }
            }
          }
        }
        $imports += [pscustomobject]@{Dll=$dllName; Functions=($funcs -join ', ')}
      }
    }
  }

  [pscustomobject]@{ Path=$Path; Is64=$is64; Exports=$exports; Imports=$imports }
}

$files = @('example\\Punto Switcher\\pshook64.dll','example\\Punto Switcher\\pshook.dll','example\\Punto Switcher\\punto.exe')
foreach($f in $files){
  $pe = Get-PeInfo $f
  "=== $($pe.Path) ==="
  if($pe.Exports.Count){ 'Exports:'; $pe.Exports | Sort-Object Name | Format-Table -AutoSize | Out-String | Write-Output }
  'Imports:'
  $pe.Imports | Format-Table -Wrap -AutoSize | Out-String | Write-Output
}
