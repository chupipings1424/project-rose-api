param()

# Native always-on-top alarm popup. Launched by PowerShellDesktopAlarm when a reminder rings.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$taskTitle = $env:ALARM_TITLE
$taskDue   = $env:ALARM_DUE
$taskId    = $env:ALARM_TASKID
$baseUrl   = $env:ALARM_BASEURL
$token     = $env:ALARM_TOKEN
if ([string]::IsNullOrWhiteSpace($taskTitle)) { $taskTitle = "Task" }

# Win32 helpers so the window really comes to the front, even over full-screen apps.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NativeTop {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
  [DllImport("user32.dll")] public static extern bool FlashWindow(IntPtr hWnd, bool bInvert);
  public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
  public const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_SHOWWINDOW = 0x0040;
  public static void ForceTop(IntPtr hWnd) {
    SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    IntPtr fg = GetForegroundWindow();
    uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero);
    uint myThread = GetCurrentThreadId();
    if (fgThread != myThread) AttachThreadInput(myThread, fgThread, true);
    ShowWindow(hWnd, 9); // SW_RESTORE
    SetForegroundWindow(hWnd);
    if (fgThread != myThread) AttachThreadInput(myThread, fgThread, false);
    FlashWindow(hWnd, true);
  }
}
"@

$result = @{ action = "dismiss"; minutes = 0 }

$form = New-Object System.Windows.Forms.Form
$form.Text = "Alarm - $taskTitle"
$form.Size = New-Object System.Drawing.Size(400, 260)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true
$form.ShowInTaskbar = $true
$form.StartPosition = 'CenterScreen'
$form.BackColor = [System.Drawing.Color]::FromArgb(127, 29, 29)
$form.ForeColor = [System.Drawing.Color]::White

$header = New-Object System.Windows.Forms.Label
$header.Text = "TIME'S UP"
$header.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$header.ForeColor = [System.Drawing.Color]::FromArgb(254, 202, 202)
$header.TextAlign = 'MiddleCenter'
$header.SetBounds(10, 14, 370, 22)
$form.Controls.Add($header)

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = $taskTitle
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Bold)
$titleLabel.TextAlign = 'MiddleCenter'
$titleLabel.SetBounds(10, 40, 370, 52)
$form.Controls.Add($titleLabel)

$timeLabel = New-Object System.Windows.Forms.Label
$timeLabel.Text = $taskDue
$timeLabel.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$timeLabel.ForeColor = [System.Drawing.Color]::FromArgb(254, 226, 226)
$timeLabel.TextAlign = 'MiddleCenter'
$timeLabel.SetBounds(10, 92, 370, 20)
$form.Controls.Add($timeLabel)

function New-AlarmButton($text, $x, $y, $w, $h, $back, $fore) {
  $b = New-Object System.Windows.Forms.Button
  $b.Text = $text
  $b.SetBounds($x, $y, $w, $h)
  $b.FlatStyle = 'Flat'
  $b.FlatAppearance.BorderSize = 0
  $b.BackColor = $back
  $b.ForeColor = $fore
  $b.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
  $b.Cursor = [System.Windows.Forms.Cursors]::Hand
  return $b
}

$blue  = [System.Drawing.Color]::FromArgb(37, 99, 235)
$green = [System.Drawing.Color]::FromArgb(16, 185, 129)
$grey  = [System.Drawing.Color]::FromArgb(71, 85, 105)
$white = [System.Drawing.Color]::White

$snooze5  = New-AlarmButton "Snooze 5 min"  20 124 175 34 $blue $white
$snooze10 = New-AlarmButton "Snooze 10 min" 205 124 175 34 $blue $white
$doneBtn  = New-AlarmButton "Mark as done"  20 165 175 34 $green $white
$dismiss  = New-AlarmButton "Dismiss"       205 165 175 34 $grey $white

$snooze5.Add_Click({  $script:result = @{ action = "snooze"; minutes = 5 };  $form.Close() })
$snooze10.Add_Click({ $script:result = @{ action = "snooze"; minutes = 10 }; $form.Close() })
$doneBtn.Add_Click({  $script:result = @{ action = "done"; minutes = 0 };    $form.Close() })
$dismiss.Add_Click({  $script:result = @{ action = "dismiss"; minutes = 0 }; $form.Close() })

$form.Controls.AddRange(@($snooze5, $snooze10, $doneBtn, $dismiss))

# Ring and keep re-asserting topmost so nothing can bury the window.
$flash = $true
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1200
$timer.Add_Tick({
  [System.Media.SystemSounds]::Exclamation.Play()
  $script:flash = -not $script:flash
  $form.BackColor = if ($script:flash) {
    [System.Drawing.Color]::FromArgb(127, 29, 29)
  } else {
    [System.Drawing.Color]::FromArgb(185, 28, 28)
  }
  $form.TopMost = $true
  [NativeTop]::ForceTop($form.Handle) | Out-Null
})

$form.Add_Shown({
  $form.Activate()
  [NativeTop]::ForceTop($form.Handle) | Out-Null
  [System.Media.SystemSounds]::Exclamation.Play()
  $timer.Start()
})
$form.Add_FormClosing({ $timer.Stop() })

[void]$form.ShowDialog()
$form.Dispose()

# Report the choice back to the server.
if ($baseUrl) {
  $action = if ($result.action -eq "done") { "complete" } else { $result.action }
  $body = @{ minutes = $result.minutes } | ConvertTo-Json -Compress
  try {
    $headers = @{ Authorization = "Bearer $token" }
    Invoke-RestMethod -Uri "$($baseUrl.TrimEnd('/'))/api/reminders/items/$taskId/$action" -Method Post -Headers $headers -ContentType "application/json" -Body $body -TimeoutSec 5 | Out-Null
  } catch { }
}
