param([string]$saida, [int]$r = 110)
# Pac-Man de fliperama: fundo preto, labirinto neon azul, Pac-Man com brilho, pastilhas, power pellet,
# 4 fantasmas e placar. So System.Drawing (vem com o Windows).
Add-Type -AssemblyName System.Drawing
$W = [int]($r * 12.5); $H = [int]($r * 5)
$b = New-Object Drawing.Bitmap $W, $H
$g = [Drawing.Graphics]::FromImage($b)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([Drawing.Color]::FromArgb(255, 4, 4, 14))
function Cor($a, $rr, $gg, $bb) { [Drawing.Color]::FromArgb($a, $rr, $gg, $bb) }
function Arred([float]$x, [float]$y, [float]$w, [float]$h, [float]$rad) {
  $p = New-Object Drawing.Drawing2D.GraphicsPath
  $d = $rad * 2
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p
}
# labirinto: corredor central entre duas paredes duplas com brilho
$m = [float]($r * 0.55); $cy = [float]($H / 2)
$corredor = [float]($r * 1.5)
foreach ($camada in @(@(14, 40), @(8, 90), @(3, 255))) {
  $pen = New-Object Drawing.Pen (Cor $camada[1] 33 80 255), ([float]$camada[0])
  $g.DrawPath($pen, (Arred $m $m ($W - 2 * $m) ($cy - $corredor - $m) ($r * 0.3)))
  $g.DrawPath($pen, (Arred $m ($cy + $corredor) ($W - 2 * $m) ($H - $cy - $corredor - $m) ($r * 0.3)))
  $pen.Dispose()
}
# Pac-Man
$px = [float]($r * 2.2)
foreach ($gl in 6..1) {
  $br = New-Object Drawing.SolidBrush (Cor ([int](10 + 6 * (7 - $gl))) 255 220 0)
  $rr = $r + $gl * ($r * 0.06)
  $g.FillPie($br, $px - $rr, $cy - $rr, 2 * $rr, 2 * $rr, 35, 290); $br.Dispose()
}
$corpo = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF ($px - $r), ($cy - $r))), ((New-Object Drawing.PointF ($px + $r), ($cy + $r))), (Cor 255 255 240 80), (Cor 255 255 170 0)
$g.FillPie($corpo, $px - $r, $cy - $r, 2 * $r, 2 * $r, 35, 290)
$g.FillEllipse([Drawing.Brushes]::Black, $px + $r * 0.05, $cy - $r * 0.62, $r * 0.2, $r * 0.2)
$g.FillEllipse([Drawing.Brushes]::White, $px + $r * 0.1, $cy - $r * 0.58, $r * 0.07, $r * 0.07)
# pastilhas e power pellet
$x0 = $px + $r * 1.25
for ($k = 0; $k -lt 6; $k++) {
  $x = $x0 + $k * $r * 0.55
  if ($k -eq 5) {
    foreach ($gl in 4..1) { $br = New-Object Drawing.SolidBrush (Cor (20 * (5 - $gl)) 255 200 160); $s = $r * (0.28 + 0.08 * $gl); $g.FillEllipse($br, $x - $s / 2, $cy - $s / 2, $s, $s); $br.Dispose() }
    $s = $r * 0.32; $g.FillEllipse((New-Object Drawing.SolidBrush (Cor 255 255 210 180)), $x - $s / 2, $cy - $s / 2, $s, $s)
  } else { $s = $r * 0.13; $g.FillEllipse((New-Object Drawing.SolidBrush (Cor 255 255 210 180)), $x - $s / 2, $cy - $s / 2, $s, $s) }
}
# fantasmas
function Fantasma([float]$fx, [float]$fy, [float]$fr, $cor, [bool]$assustado) {
  $p = New-Object Drawing.Drawing2D.GraphicsPath
  $p.AddArc($fx - $fr, $fy - $fr, 2 * $fr, 2 * $fr, 180, 180)
  $base = $fy + $fr * 0.9
  $p.AddLine($fx + $fr, $fy, $fx + $fr, $base)
  $n = 4; $passo = 2 * $fr / $n
  for ($i = 0; $i -lt $n; $i++) {
    $xa = $fx + $fr - $i * $passo
    $p.AddLine($xa, $base, $xa - $passo / 2, $base - $fr * 0.25); $p.AddLine($xa - $passo / 2, $base - $fr * 0.25, $xa - $passo, $base)
  }
  $p.CloseFigure()
  foreach ($gl in 4..1) { $pen = New-Object Drawing.Pen (Cor (18 * (5 - $gl)) $cor.R $cor.G $cor.B), ([float]($gl * $fr * 0.12)); $g.DrawPath($pen, $p); $pen.Dispose() }
  $g.FillPath((New-Object Drawing.SolidBrush $cor), $p)
  if ($assustado) {
    $g.FillEllipse([Drawing.Brushes]::White, $fx - $fr * 0.45, $fy - $fr * 0.2, $fr * 0.25, $fr * 0.25)
    $g.FillEllipse([Drawing.Brushes]::White, $fx + $fr * 0.2, $fy - $fr * 0.2, $fr * 0.25, $fr * 0.25)
  } else {
    foreach ($ox in @(-0.42, 0.12)) {
      $g.FillEllipse([Drawing.Brushes]::White, $fx + $fr * $ox, $fy - $fr * 0.35, $fr * 0.36, $fr * 0.44)
      $g.FillEllipse((New-Object Drawing.SolidBrush (Cor 255 30 60 220)), $fx + $fr * ($ox - 0.04), $fy - $fr * 0.2, $fr * 0.2, $fr * 0.24)
    }
  }
}
$fr = [float]($r * 0.62)
$fx = $x0 + 6 * $r * 0.55 + $r * 0.4
Fantasma $fx $cy $fr (Cor 255 255 30 30) $false
Fantasma ($fx + $fr * 2.3) $cy $fr (Cor 255 255 150 220) $false
Fantasma ($fx + $fr * 4.6) $cy $fr (Cor 255 0 230 240) $false
Fantasma ($fx + $fr * 6.9) $cy $fr (Cor 255 255 170 60) $false
# placar
$fonte = New-Object Drawing.Font 'Consolas', ([float]($r * 0.32)), ([Drawing.FontStyle]::Bold)
$g.DrawString('1UP  ODIN', $fonte, (New-Object Drawing.SolidBrush (Cor 255 255 255 255)), $m * 1.2, $r * 0.02)
$g.DrawString('HIGH SCORE  999990', $fonte, (New-Object Drawing.SolidBrush (Cor 255 255 60 60)), $W / 2 - $r * 1.6, $r * 0.02)
$g.DrawString('READY!', $fonte, (New-Object Drawing.SolidBrush (Cor 255 255 220 0)), $W / 2 - $r * 0.6, $H - $r * 0.45)
$b.Save($saida, [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $b.Dispose()
'OK'
