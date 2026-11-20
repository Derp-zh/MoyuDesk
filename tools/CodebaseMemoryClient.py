"""Run a bounded local stdio MCP batch when this chat's transport is closed.

Uses the already configured executable and environment. Never edits Codex config,
restarts the app, prints environment values, or touches task-board user data.
"""
from pathlib import Path
import json,os,queue,subprocess,sys,threading,time,tomllib

def main():
 root=Path(__file__).resolve().parents[1];request=Path(sys.argv[1]).resolve();output=Path(sys.argv[2]).resolve()
 assert request.is_relative_to(root/'test-output') and output.is_relative_to(root/'test-output')
 data=json.loads(request.read_text(encoding='utf-8'));config=tomllib.loads((Path.home()/'.codex/config.toml').read_text(encoding='utf-8'))['mcp_servers']['codebase-memory-mcp']
 command=config['command'];assert Path(command).name.lower()=='codebase-memory-mcp.exe' and Path(command).is_file()
 env=os.environ.copy();env.update(config.get('env',{}));stderr=output.with_suffix('.stderr.log').open('w',encoding='utf-8')
 child=subprocess.Popen([command,*config.get('args',[])],cwd=root,env=env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=stderr,text=True,encoding='utf-8',creationflags=subprocess.CREATE_NO_WINDOW)
 replies=queue.Queue()
 def receive():
  for line in child.stdout:
   try:replies.put(json.loads(line))
   except json.JSONDecodeError:replies.put(dict(non_json=line[:400]))
 threading.Thread(target=receive,daemon=True).start();serial=0;events=[]
 def send(method,params,notify=False,timeout=180):
  nonlocal serial
  serial+=1;message=dict(jsonrpc='2.0',method=method,params=params)
  if not notify:message['id']=serial
  child.stdin.write(json.dumps(message,ensure_ascii=False)+'\n');child.stdin.flush()
  if notify:return None
  deadline=time.monotonic()+timeout
  while time.monotonic()<deadline:
   try:item=replies.get(timeout=min(1,max(.01,deadline-time.monotonic())))
   except queue.Empty:
    if child.poll() is not None:raise RuntimeError('Configured MCP process exited: '+str(child.returncode))
    continue
   if item.get('id')==serial:
    if 'error' in item:raise RuntimeError(json.dumps(item['error']))
    return item['result']
   events.append(item)
  raise TimeoutError(method)
 result=dict(method='independent connection to configured local stdio MCP',codexRestarted=False,configEdited=False,requests=[])
 try:
  result['initialize']=send('initialize',dict(protocolVersion='2024-11-05',capabilities={},clientInfo=dict(name='taskboard-local-verification',version='1')))
  send('notifications/initialized',{},True)
  for call in data:
   response=send('tools/call',dict(name=call['name'],arguments=call.get('arguments',{})))
   result['requests'].append(dict(request=call,response=response));output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
   if response.get('isError'):raise RuntimeError('MCP tool returned error: '+call['name'])
  result['passed']=True;result['events']=events;output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(dict(passed=True,calls=len(data),receipt=str(output))))
 finally:
  child.stdin.close()
  try:child.wait(timeout=5)
  except subprocess.TimeoutExpired:child.terminate();child.wait(timeout=5)
  stderr.close()
if __name__=='__main__':main()
