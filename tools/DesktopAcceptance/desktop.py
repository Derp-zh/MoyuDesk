"""Physical input driver for an explicitly authorized, isolated acceptance run."""
import ctypes as c
from ctypes import wintypes as w
import json, sys, time, subprocess
from pathlib import Path
from PIL import ImageGrab

U=c.windll.user32
U.SetProcessDpiAwarenessContext(c.c_void_p(-4))
U.GetForegroundWindow.restype=w.HWND
U.WindowFromPoint.argtypes=[w.POINT];U.WindowFromPoint.restype=w.HWND
U.GetAncestor.argtypes=[w.HWND,w.UINT];U.GetAncestor.restype=w.HWND
U.IsWindow.argtypes=[w.HWND]
U.SetForegroundWindow.argtypes=[w.HWND]
class M(c.Structure):_fields_=[('dx',w.LONG),('dy',w.LONG),('data',w.DWORD),('flags',w.DWORD),('time',w.DWORD),('extra',c.c_size_t)]
class K(c.Structure):_fields_=[('vk',w.WORD),('scan',w.WORD),('flags',w.DWORD),('time',w.DWORD),('extra',c.c_size_t)]
class A(c.Union):_fields_=[('m',M),('k',K)]
class I(c.Structure):_fields_=[('kind',w.DWORD),('a',A)]
ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'test-output/desktop-0942-20261107'
BASE.mkdir(exist_ok=True)
def save(name,obj): (BASE/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf-8')
def load(name):return json.loads((BASE/name).read_text(encoding='utf-8-sig'))
def owner(hwnd):
 p=w.DWORD();U.GetWindowThreadProcessId(w.HWND(hwnd),c.byref(p));return p.value
def check_point(x,y):
 hwnd=U.WindowFromPoint(w.POINT(x,y));pid=owner(hwnd)
 if pid!=load('process.json')['pid']:raise RuntimeError(f'Input target is outside isolated process: {pid} at {x},{y}')
 return int(hwnd)
def record(action,**kwargs):
 with (BASE/'actions.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(dict(at=time.time(),action=action,**kwargs),ensure_ascii=False)+'\n')
def send(value):
 if U.SendInput(1,c.byref(value),c.sizeof(I))!=1:raise RuntimeError('SendInput failed')
def mouse(flags,data=0):send(I(0,A(m=M(0,0,data,flags,0,0))))
def key(vk,up=False):send(I(1,A(k=K(vk,0,2 if up else 0,0,0))))
def windows():
 out=[];pid=load('process.json')['pid'];cb=c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
 def f(h,p):
  if owner(h)==pid and U.IsWindowVisible(h):
   rect=w.RECT();U.GetWindowRect(h,c.byref(rect));text=c.create_unicode_buffer(500);U.GetWindowTextW(h,text,500)
   out.append(dict(hwnd=int(h),title=text.value,rect=[rect.left,rect.top,rect.right,rect.bottom]))
  return True
 U.EnumWindows(cb(f),0);return out
args=sys.argv[1:];action=args[0]
if action=='init':
 if (BASE/'baseline.json').exists():raise RuntimeError('Keep the original baseline')
 p=w.POINT();fg=U.GetForegroundWindow()
 if not fg or not U.GetCursorPos(c.byref(p)):raise PermissionError('Interactive desktop access is unavailable')
 save('baseline.json',dict(foreground=int(fg),cursor=[p.x,p.y]));print('baseline saved')
elif action=='windows':print(json.dumps(windows(),ensure_ascii=True))
elif action=='wait-ready':
 deadline=time.monotonic()+15
 while time.monotonic()<deadline:
  if any(x['title']=='摸鱼台入口' for x in windows()):break
  time.sleep(.1)
 else:raise TimeoutError('Owned application did not present its ball window')
 print('owned window ready')
elif action=='element':
 path=(BASE/args[1]).resolve()
 if not path.is_relative_to(BASE.resolve()):raise RuntimeError('Own UIA snapshot required')
 rows=load(args[1])
 if args[2]=='@title':
  fields=[r for r in rows if r['type']=='ControlType.Edit' and r['bounds'][2]>250]
  row=min(fields,key=lambda r:(r['bounds'][1],r['bounds'][0]))
 else:
  candidates=[r for r in rows if r['type']=='ControlType.Button' and r['name']==args[2]]
  row=max(candidates,key=lambda r:r['bounds'][0])
 x,y,width,height=row['bounds'];subprocess.run([sys.executable,__file__,'click',str(round(x+width/2)),str(round(y+height/2))],check=True)
 record('element-coordinate-click',file=args[1],name=args[2],bounds=row['bounds'])
elif action=='rapid-toggle':
 x,y=map(int,args[1:3]);hwnd=check_point(x,y);U.SetCursorPos(x,y)
 for n in range(2):mouse(2);time.sleep(.015);mouse(4);time.sleep(.025)
 record('rapid-toggle',x=x,y=y,hwnd=hwnd);time.sleep(.65)
elif action=='doubleclick':
 x,y=map(int,args[1:3]);hwnd=check_point(x,y);U.SetCursorPos(x,y);time.sleep(.08)
 for n in range(2):mouse(2);time.sleep(.04);mouse(4);time.sleep(.07)
 record('doubleclick',x=x,y=y,hwnd=hwnd);time.sleep(.5)
elif action=='click':
 x,y=map(int,args[1:3]);hwnd=check_point(x,y);U.SetCursorPos(x,y);time.sleep(.09);right=len(args)>3 and args[3]=='right'
 mouse(8 if right else 2);time.sleep(.07);mouse(16 if right else 4);record('right-click' if right else 'click',x=x,y=y,hwnd=hwnd);time.sleep(.35)
elif action=='move':
 x,y=map(int,args[1:3]);U.SetCursorPos(x,y);record('move',x=x,y=y);time.sleep(.3)
elif action=='drag':
 x,y,tx,ty=map(int,args[1:5]);hwnd=check_point(x,y);right=len(args)>5 and args[5]=='right';U.SetCursorPos(x,y);time.sleep(.1);mouse(8 if right else 2);time.sleep(.45 if right else .1)
 try:
  for n in range(1,25):U.SetCursorPos(round(x+(tx-x)*n/24),round(y+(ty-y)*n/24));time.sleep(.018)
 finally:mouse(16 if right else 4)
 record('drag',start=[x,y],end=[tx,ty],right=right,hwnd=hwnd);time.sleep(.45)
elif action in ['key','text','chord','wheel']:
 if owner(U.GetForegroundWindow())!=load('process.json')['pid']:raise RuntimeError('Keyboard target is outside isolated app')
 if action=='key':key(int(args[1]));key(int(args[1]),True)
 elif action=='chord':
  values=[int(x) for x in args[1:]]
  for k in values:key(k)
  for k in reversed(values):key(k,True)
 elif action=='wheel':mouse(0x800,int(args[1])&0xffffffff)
 else:
  raw=args[1].encode('utf-16-le')
  for i in range(0,len(raw),2):
   code=int.from_bytes(raw[i:i+2],'little');send(I(1,A(k=K(0,code,4,0,0))));send(I(1,A(k=K(0,code,6,0,0))))
 record(action,value=args[1:]);time.sleep(.35)
elif action=='capture':
 all_windows=[x for x in windows() if x['rect'][2]>x['rect'][0] and x['rect'][3]>x['rect'][1]]
 if len(args)>2:all_windows=[x for x in all_windows if x['hwnd']==int(args[2][1:])] if args[2].startswith('#') else [x for x in all_windows if args[2] in x['title']]
 if not all_windows:raise RuntimeError('No isolated visible windows')
 box=[min(x['rect'][0] for x in all_windows),min(x['rect'][1] for x in all_windows),max(x['rect'][2] for x in all_windows),max(x['rect'][3] for x in all_windows)]
 box=[max(0,box[0]),max(0,box[1]),min(U.GetSystemMetrics(0),box[2]),min(U.GetSystemMetrics(1),box[3])]
 file=BASE/(args[1]+'.png');ImageGrab.grab(bbox=tuple(box)).save(file);save(args[1]+'.json',dict(box=box,windows=all_windows));print(str(file))
elif action=='restore':
 b=load('baseline.json');ok=bool(U.SetForegroundWindow(w.HWND(b['foreground']))) if U.IsWindow(w.HWND(b['foreground'])) else False
 U.SetCursorPos(*b['cursor']);time.sleep(.2);p=w.POINT();U.GetCursorPos(c.byref(p));save('restored.json',dict(foregroundRestored=int(U.GetForegroundWindow())==b['foreground'],cursorRestored=[p.x,p.y]==b['cursor']));print('restored')
else:raise RuntimeError('Unknown action')
