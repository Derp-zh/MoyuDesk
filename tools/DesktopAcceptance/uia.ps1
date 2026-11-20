param([Parameter(Mandatory)][int]$TargetPid,[string]$Output,[string]$WindowTitle='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,WindowsBase
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Collections.Generic;
public static class AcceptanceWindows {
 delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr root,Callback c,IntPtr p);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,System.Text.StringBuilder b,int n);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr p);
 public static IntPtr[] All(int pid,string filter){var a=new List<IntPtr>();EnumWindows((h,p)=>{uint n;GetWindowThreadProcessId(h,out n);var t=new System.Text.StringBuilder(256);GetWindowText(h,t,256);if(n==pid&&IsWindowVisible(h)&&(filter.Length==0||(filter=="@menu"?t.Length==0:t.ToString().Contains(filter)))){a.Add(h);EnumChildWindows(h,(c,q)=>{a.Add(c);return true;},IntPtr.Zero);}return true;},IntPtr.Zero);return a.ToArray();}
}
'@
[AcceptanceWindows]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))|Out-Null
$result=[Collections.Generic.List[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new()
$cache=[Windows.Automation.CacheRequest]::new()
foreach($p in @([Windows.Automation.AutomationElement]::NameProperty,[Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.AutomationElement]::BoundingRectangleProperty,[Windows.Automation.AutomationElement]::IsOffscreenProperty,[Windows.Automation.AutomationElement]::AutomationIdProperty,[Windows.Automation.AutomationElement]::RuntimeIdProperty,[Windows.Automation.ValuePattern]::ValueProperty)){$cache.Add($p)}
foreach($hwnd in [AcceptanceWindows]::All($TargetPid,$WindowTitle)){
 try{
  $root=[Windows.Automation.AutomationElement]::FromHandle($hwnd)
  $scope=$cache.Activate()
  try{$all=$root.FindAll([Windows.Automation.TreeScope]::Subtree,[Windows.Automation.Condition]::TrueCondition)}finally{$scope.Dispose()}
  foreach($e in $all){$v=$e.Cached;$id=$e.GetCachedPropertyValue([Windows.Automation.AutomationElement]::RuntimeIdProperty)-join ','
   if($v.IsOffscreen -or $v.BoundingRectangle.Width -le 0 -or -not $seen.Add($id)){continue}
   $value=$e.GetCachedPropertyValue([Windows.Automation.ValuePattern]::ValueProperty,$true);if($value -isnot [string]){$value=$null}
   $r=$v.BoundingRectangle;$result.Add([pscustomobject]@{name=$v.Name;type=$v.ControlType.ProgrammaticName;id=$v.AutomationId;bounds=@($r.X,$r.Y,$r.Width,$r.Height);handle=$hwnd.ToInt64();value=$value})
  }
 }catch{}
}
$json=ConvertTo-Json -InputObject $result.ToArray() -Depth 6 -Compress
if($Output){[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),$json,[Text.UTF8Encoding]::new($false))}else{$json}
