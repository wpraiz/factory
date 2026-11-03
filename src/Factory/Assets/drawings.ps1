param([string]$forma, [string]$saida, [int]$r = 150)
# Desenhos prontos do Odin (modo "imagem" da mao desenhar): pikachu, estrela,
# coracao, sol, foguete, lua. So System.Drawing. Cada forma cabe num quadrado de lado 3r, fundo branco.
Add-Type -AssemblyName System.Drawing
$W = [int]($r * 3); $H = [int]($r * 3); $cx = [float]($W / 2); $cy = [float]($H / 2)
$b = New-Object Drawing.Bitmap $W, $H
$g = [Drawing.Graphics]::FromImage($b)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([Drawing.Color]::White)
function Cor($a, $rr, $gg, $bb) { [Drawing.Color]::FromArgb($a, $rr, $gg, $bb) }
function Pen($cor, [float]$w) { New-Object Drawing.Pen $cor, $w }
function Brush($cor) { New-Object Drawing.SolidBrush $cor }
function Pts($lista) { $lista | ForEach-Object { New-Object Drawing.PointF ([float]$_[0]), ([float]$_[1]) } }
$traco = Pen ([Drawing.Color]::Black) ([float][Math]::Max(3, $r / 30))

switch ($forma) {
  'pikachu' {
    $am = Cor 255 255 222 0; $amE = Cor 255 230 170 0
    $pa = [float]($r * 0.72)                      # raio da cabeca
    $hy = $cy + $r * 0.05
    # orelhas (triangulos longos, ponta preta)
    foreach ($lado in @(-1, 1)) {
      $base1 = @(($cx + $lado * $pa * 0.35), ($hy - $pa * 0.75))
      $base2 = @(($cx + $lado * $pa * 0.85), ($hy - $pa * 0.45))
      $ponta = @(($cx + $lado * $pa * 1.25), ($hy - $pa * 1.9))
      $g.FillPolygon((Brush $am), (Pts @($base1, $base2, $ponta)))
      $g.DrawPolygon($traco, (Pts @($base1, $base2, $ponta)))
      $p1 = @(($cx + $lado * $pa * 1.0), ($hy - $pa * 1.45)); $p2 = @(($cx + $lado * $pa * 0.72), ($hy - $pa * 1.22))
      $g.FillPolygon((Brush ([Drawing.Color]::Black)), (Pts @($p1, $p2, $ponta)))
    }
    # cabeca
    $g.FillEllipse((Brush $am), $cx - $pa, $hy - $pa * 0.85, 2 * $pa, 1.75 * $pa)
    $g.DrawEllipse($traco, $cx - $pa, $hy - $pa * 0.85, 2 * $pa, 1.75 * $pa)
    # bochechas
    foreach ($lado in @(-1, 1)) { $g.FillEllipse((Brush (Cor 255 230 40 40)), $cx + $lado * $pa * 0.62 - $pa * 0.17, $hy + $pa * 0.22, $pa * 0.34, $pa * 0.3) }
    # olhos
    foreach ($lado in @(-1, 1)) {
      $g.FillEllipse((Brush ([Drawing.Color]::Black)), $cx + $lado * $pa * 0.38 - $pa * 0.11, $hy - $pa * 0.2, $pa * 0.22, $pa * 0.26)
      $g.FillEllipse((Brush ([Drawing.Color]::White)), $cx + $lado * $pa * 0.38 - $pa * 0.03, $hy - $pa * 0.16, $pa * 0.08, $pa * 0.09)
    }
    # nariz e boca
    $g.FillEllipse((Brush ([Drawing.Color]::Black)), $cx - $pa * 0.04, $hy + $pa * 0.1, $pa * 0.08, $pa * 0.05)
    $g.DrawArc($traco, $cx - $pa * 0.3, $hy + $pa * 0.05, $pa * 0.3, $pa * 0.3, 20, 140)
    $g.DrawArc($traco, $cx, $hy + $pa * 0.05, $pa * 0.3, $pa * 0.3, 20, 140)
  }
  'estrela' {
    $pts = @(); for ($i = 0; $i -lt 10; $i++) { $rr = if ($i % 2 -eq 0) { $r * 1.3 } else { $r * 0.55 }; $a = -[Math]::PI / 2 + $i * [Math]::PI / 5; $pts += , @(($cx + $rr * [Math]::Cos($a)), ($cy + $rr * [Math]::Sin($a))) }
    $g.FillPolygon((Brush (Cor 255 255 210 0)), (Pts $pts)); $g.DrawPolygon($traco, (Pts $pts))
  }
  'coracao' {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $s = $r * 1.2
    $p.AddArc($cx - $s, $cy - $s * 0.9, $s, $s, 135, 225)
    $p.AddArc($cx, $cy - $s * 0.9, $s, $s, 180, 225)
    $p.AddLine($cx + $s * 0.85, $cy + $s * 0.05, $cx, $cy + $s * 1.0)
    $p.AddLine($cx, $cy + $s * 1.0, $cx - $s * 0.85, $cy + $s * 0.05)
    $p.CloseFigure()
    $g.FillPath((Brush (Cor 255 230 30 60)), $p); $g.DrawPath($traco, $p)
  }
  'sol' {
    $rr = $r * 0.7
    for ($i = 0; $i -lt 12; $i++) { $a = $i * [Math]::PI / 6; $g.DrawLine((Pen (Cor 255 255 170 0) ([float]($r / 12))), $cx + $rr * 1.15 * [Math]::Cos($a), $cy + $rr * 1.15 * [Math]::Sin($a), $cx + $rr * 1.45 * [Math]::Cos($a), $cy + $rr * 1.45 * [Math]::Sin($a)) }
    $g.FillEllipse((Brush (Cor 255 255 215 0)), $cx - $rr, $cy - $rr, 2 * $rr, 2 * $rr); $g.DrawEllipse($traco, $cx - $rr, $cy - $rr, 2 * $rr, 2 * $rr)
    foreach ($lado in @(-1, 1)) { $g.FillEllipse((Brush ([Drawing.Color]::Black)), $cx + $lado * $rr * 0.35 - $rr * 0.07, $cy - $rr * 0.25, $rr * 0.14, $rr * 0.18) }
    $g.DrawArc($traco, $cx - $rr * 0.4, $cy - $rr * 0.1, $rr * 0.8, $rr * 0.6, 20, 140)
  }
  'lua' {
    # lua crescente = disco amarelo menos um disco branco deslocado (regiao), contorno so da parte visivel
    $rr = $r * 1.1
    $cheia = New-Object Drawing.Drawing2D.GraphicsPath; $cheia.AddEllipse($cx - $rr, $cy - $rr, 2 * $rr, 2 * $rr)
    $furo = New-Object Drawing.Drawing2D.GraphicsPath; $furo.AddEllipse($cx - $rr * 0.45, $cy - $rr * 0.95, 1.9 * $rr, 1.9 * $rr)
    $reg = New-Object Drawing.Region $cheia; $reg.Exclude($furo)
    $g.FillRegion((Brush (Cor 255 250 230 120)), $reg)
    $g.Clip = $reg; $g.DrawPath((Pen ([Drawing.Color]::Black) ([float]($r / 15))), $cheia); $g.DrawPath((Pen ([Drawing.Color]::Black) ([float]($r / 15))), $furo); $g.ResetClip()
    # estrelinhas
    foreach ($e in @(@(1.55, -0.9), @(1.75, -0.2), @(1.5, 0.6))) { $ex = $cx + $r * 0.3 * $e[0]; $ey = $cy + $r * $e[1]; $s = $r * 0.09; $g.FillPolygon((Brush (Cor 255 255 200 0)), (Pts @(@($ex, ($ey - $s)), @(($ex + $s * 0.3), ($ey - $s * 0.3)), @(($ex + $s), $ey), @(($ex + $s * 0.3), ($ey + $s * 0.3)), @($ex, ($ey + $s)), @(($ex - $s * 0.3), ($ey + $s * 0.3)), @(($ex - $s), $ey), @(($ex - $s * 0.3), ($ey - $s * 0.3))))) }
  }
  'foguete' {
    $w = $r * 0.6; $h = $r * 1.8; $top = $cy - $h / 2
    $corpo = New-Object Drawing.Drawing2D.GraphicsPath
    $corpo.AddArc($cx - $w / 2, $top, $w, $w, 180, 180)
    $corpo.AddLine($cx + $w / 2, $top + $w / 2, $cx + $w / 2, $top + $h)
    $corpo.AddLine($cx + $w / 2, $top + $h, $cx - $w / 2, $top + $h)
    $corpo.CloseFigure()
    $g.FillPath((Brush (Cor 255 220 225 235)), $corpo); $g.DrawPath($traco, $corpo)
    foreach ($lado in @(-1, 1)) { $aleta = Pts @(@(($cx + $lado * $w / 2), ($top + $h * 0.6)), @(($cx + $lado * $w * 1.1), ($top + $h * 1.05)), @(($cx + $lado * $w / 2), ($top + $h))); $g.FillPolygon((Brush (Cor 255 220 40 40)), $aleta); $g.DrawPolygon($traco, $aleta) }
    $g.FillEllipse((Brush (Cor 255 90 170 255)), $cx - $w * 0.22, $top + $w * 0.55, $w * 0.44, $w * 0.44); $g.DrawEllipse($traco, $cx - $w * 0.22, $top + $w * 0.55, $w * 0.44, $w * 0.44)
    $fogo = Pts @(@(($cx - $w * 0.35), ($top + $h)), @($cx, ($top + $h + $r * 0.7)), @(($cx + $w * 0.35), ($top + $h)))
    $g.FillPolygon((Brush (Cor 255 255 140 0)), $fogo)
    $fogo2 = Pts @(@(($cx - $w * 0.18), ($top + $h)), @($cx, ($top + $h + $r * 0.4)), @(($cx + $w * 0.18), ($top + $h)))
    $g.FillPolygon((Brush (Cor 255 255 230 60)), $fogo2)
  }
  default { 'ERRO|forma desconhecida'; exit }
}
$b.Save($saida, [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $b.Dispose()
'OK'
