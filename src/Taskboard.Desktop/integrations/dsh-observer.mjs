// Offline DSH v4 structural log reader. No DSH imports, network, writes or UI.
// Frame layout independently implemented from the installed MIT DSH persistence
// contract: one complete checksummed Zstd frame per header/append batch.
// CLI: node dsh-observer.mjs --catalog <DSH_HOME>
// CLI: node dsh-observer.mjs --events <DSH_HOME> <sessionId>
//      [--offset N] [--after-seq N] [--turn N] [--max-bytes N] [--max-events N]
// Stdout: catalog JSON array, or event JSONL followed by one kind=batch watermark.
import fs from 'node:fs';
import path from 'node:path';
import { zstdDecompressSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';

const SUPPORTED_VERSION = 4;
const HEADER_LIMIT = 1024 * 1024;
const DEFAULT_BYTES = 8 * 1024 * 1024;
const MAX_BYTES = 128 * 1024 * 1024;
const OUTPUT_LIMIT = 64 * 1024 * 1024;
const KNOWN = new Set(['agent-preset/selected','agent/inbox/spliced','approval/asked','approval/decided','approval/policy','assistant/attempt','assistant/message','command/done','command/run','compaction/end','compaction/prune','compaction/start','compaction/summary','deliverables/presented','developer/message','feedback/message-delete','feedback/message-put','feedback/record','goal/change','hook/invoked','hook/result','image/offload','job/registered','job/settled','llm/retry','llm/retry-started','model/selection','permission/preset','plan/mode','request/context','request/header','sandbox/mode','schedule/change','session-log-deepseek/delivery-accepted','session/end-seed','session/title','session/title-llm-request','step/end','step/start','subagent/catalog','subagent/descriptor','subagent/model-selection-policy','system/message','team/member','team/message/delivered','team/message/queued','team/task','todo/write','tool-workflow/agent-end','tool-workflow/agent-start','tool-workflow/run-end','tool-workflow/run-start','tool/call','tool/ptc-dispatch','tool/ptc-dispatch-start','tool/result','turn/end','turn/start','user/message','web/deepseek-search-llm-request','working-directory/change','workspace/changes']);
const QUESTION_TOOLS = new Set(['ask_user_question', 'exit_plan_mode']);
const text = (v, max = 256) => typeof v === 'string' && v.length <= max ? v : null;
const number = v => Number.isSafeInteger(v) && v >= 0 ? v : null;
const fail = message => { throw new Error(message); };

export function scanFrames(buffer, maxFrames = Infinity) {
  const frames = []; let offset = 0;
  const torn = start => ({frames, tornStart: start});
  while (offset < buffer.length) {
    const start = offset;
    if (buffer.length - offset < 4) return torn(start);
    if (buffer.readUInt32LE(offset) !== 0xfd2fb528) fail('invalid-zstd-magic');
    offset += 4;
    if (offset === buffer.length) return torn(start);
    const descriptor = buffer[offset++];
    if (descriptor & 24) fail('reserved-zstd-header-bits');
    const sizeFlag = descriptor >>> 6;
    const single = (descriptor & 32) !== 0;
    const dictionaryFlag = descriptor & 3;
    const headerBytes = (single ? 0 : 1) + (dictionaryFlag === 3 ? 4 : dictionaryFlag) + (sizeFlag === 0 ? (single ? 1 : 0) : 1 << sizeFlag);
    if (buffer.length - offset < headerBytes) return torn(start);
    offset += headerBytes;
    for (;;) {
      if (buffer.length - offset < 3) return torn(start);
      const block = buffer.readUIntLE(offset, 3); offset += 3;
      const type = (block >>> 1) & 3;
      if (type === 3) fail('reserved-zstd-block-type');
      const bytes = type === 1 ? 1 : block >>> 3;
      if (buffer.length - offset < bytes) return torn(start);
      offset += bytes;
      if (block & 1) break;
    }
    if (descriptor & 4) { if (buffer.length - offset < 4) return torn(start); offset += 4; }
    frames.push({start,end:offset});
    if (frames.length >= maxFrames) return {frames};
  }
  return {frames};
}

function encodeSegment(raw) {
  if (!raw) fail('empty-session-id');
  if (raw === '.') return '~002E';
  if (raw === '..') return '~002E~002E';
  let out = '';
  for (let n = 0; n < raw.length; n++) {
    const ch = raw[n]; out += /^[A-Za-z0-9._-]$/.test(ch) ? ch : '~' + raw.charCodeAt(n).toString(16).toUpperCase().padStart(4,'0');
  }
  return out;
}
function directories(dir) {
  try { return fs.readdirSync(dir,{withFileTypes:true}).filter(x => x.isDirectory() && !x.isSymbolicLink()).map(x => path.join(dir,x.name)); }
  catch (e) { if (e.code === 'ENOENT') return []; throw e; }
}
function selectedGeneration(sessionDir) {
  const generations = fs.readdirSync(sessionDir,{withFileTypes:true}).filter(x => x.isFile() && !x.isSymbolicLink()).flatMap(x => {
    const m = /^session(?:\.v([1-9][0-9]*))?\.jsonl(\.zstd)?$/.exec(x.name);
    return m ? [{file:path.join(sessionDir,x.name),version:m[1] ? Number(m[1]) : 0,compressed:!!m[2]}] : [];
  }).sort((a,b) => b.version-a.version);
  if (!generations.length) return null;
  if (generations.length > 1 && generations[0].version === generations[1].version) fail('ambiguous-physical-encoding');
  return generations[0];
}
function selectedSessions(home, wantedId = null) {
  const root = path.resolve(home,'sessions');
  const results = [];
  const encoded = wantedId === null ? null : encodeSegment(wantedId);
  for (const project of directories(root)) for (const dir of directories(project)) {
    if (encoded !== null && path.basename(dir) !== encoded) continue;
    const selected = selectedGeneration(dir); if (selected) results.push(selected);
  }
  return results;
}
function readChunk(file, offset, length) {
  const fd = fs.openSync(file,'r');
  try {
    const stat = fs.fstatSync(fd);
    const count = Math.min(length,Math.max(0,stat.size-offset));
    const buffer = Buffer.alloc(count); let read = 0;
    while (read < count) { const got = fs.readSync(fd,buffer,read,count-read,offset+read); if (!got) break; read += got; }
    return {buffer:buffer.subarray(0,read),stat};
  } finally { fs.closeSync(fd); }
}
function parseHeaderBytes(bytes) {
  if (!bytes.length || bytes.at(-1) !== 10 || bytes.indexOf(10) !== bytes.length-1) fail('invalid-header-record');
  const h = JSON.parse(bytes.toString('utf8'));
  if (h.type !== 'session' || text(h.id,1024) === null || number(h.version) === null || number(h.createdAt) === null) fail('invalid-session-header');
  return {id:h.id,version:h.version,createdAt:h.createdAt,isSeeded:h.isSeeded===true};
}
function readHeader(generation) {
  let size = 4096;
  while (size <= HEADER_LIMIT) {
    const {buffer,stat} = readChunk(generation.file,0,size);
    if (generation.compressed) {
      const found = scanFrames(buffer,1);
      if (found.frames.length) {
        const f = found.frames[0];
        const h = parseHeaderBytes(zstdDecompressSync(buffer.subarray(f.start,f.end),{maxOutputLength:HEADER_LIMIT}));
        if (h.version !== generation.version) fail('header-generation-mismatch');
        return {...h,headerBytes:f.end,stat};
      }
    } else {
      const end = buffer.indexOf(10);
      if (end >= 0) {
        const h = parseHeaderBytes(buffer.subarray(0,end+1));
        if (h.version !== generation.version) fail('header-generation-mismatch');
        return {...h,headerBytes:end+1,stat};
      }
    }
    if (buffer.length === stat.size) fail('incomplete-header');
    size *= 2;
  }
  fail('header-budget-exceeded');
}
function titleFromProjection(home,id) {
  // ID is an arbitrary string. Use the same safe segment encoding; never use it
  // as a raw path. No transcript field is selected or returned.
  const file = path.resolve(home,'storages','session_projcache','sessions',encodeSegment(id)+'.json');
  try {
    const stat = fs.lstatSync(file);
    if (!stat.isFile() || stat.isSymbolicLink() || stat.size > 4*1024*1024) return '';
    const doc=JSON.parse(fs.readFileSync(file,'utf8'));
    const title=doc?.record?.rows?.title?.val;
    return typeof title === 'string' ? title.replace(/\s+/g,' ').trim().slice(0,160) : '';
  } catch { return ''; }
}
export function catalog(home) {
  const values=[];
  for (const g of selectedSessions(home)) {
    try { const h=readHeader(g); values.push({id:h.id,name:titleFromProjection(home,h.id)||h.id,updatedAt:Math.floor(h.stat.mtimeMs)}); }
    catch { /* A catalog candidate is unavailable, not an invented state. */ }
  }
  return values.sort((a,b)=>b.updatedAt-a.updatedAt||a.id.localeCompare(b.id));
}

function projectEvent(e,currentTurn) {
  const d=e.data; const t=number(d.turn)??currentTurn;
  const base={kind:'event',type:e.type,seq:e.seq,time:e.time,turn:t};
  const code = error => text(error?.code,128) ?? null;
  switch(e.type) {
    case 'turn/start': case 'step/start': case 'step/end':
      return {...base,data:{turn:number(d.turn),step:number(d.step)}};
    case 'turn/end':
      return {...base,data:{turn:number(d.turn),reason:text(d.reason?.kind,64),errorCode:code(d.reason?.error),abortKind:text(d.reason?.reason?.kind,64)}};
    case 'approval/asked':
      if (text(d.id)===null) fail('invalid-approval-id');
      return {...base,data:{id:d.id,callId:text(d.callId),toolName:text(d.toolName)}};
    case 'approval/decided':
      if (text(d.id)===null) fail('invalid-approval-id');
      return {...base,data:{id:d.id,outcome:text(d.outcome,64)}};
    case 'plan/mode':
      if (typeof d.active !== 'boolean') fail('invalid-plan-mode');
      return {...base,data:{active:d.active}};
    case 'tool/call':
      return QUESTION_TOOLS.has(d.name) ? {...base,data:{callId:text(d.callId),name:d.name,step:number(d.step)}} : null;
    case 'tool/ptc-dispatch-start': case 'tool/ptc-dispatch':
      return QUESTION_TOOLS.has(d.name) ? {...base,data:{callId:text(d.subCallId),name:d.name,isError:d.isError===true,pending:pendingFromCanonicalResult(d.content),errorCode:code(d.error),step:number(d.step)}} : null;
    case 'tool/result':
      return {...base,data:{callId:text(d.message?.toolCallId),isError:d.message?.isError===true,errorCode:code(d.error),pending:pendingFromCanonicalResult(d.message?.content),step:number(d.step)}};
    case 'request/header':
      return {...base,data:{model:text(d.header?.config?.model,128),reason:text(d.reason,64),startsSeries:d.startsSeries===true}};
    case 'request/context':
      return {...base,data:{model:text(d.model,128),provider:text(d.provider,128)}};
    case 'assistant/attempt': case 'assistant/message':
      return {...base,data:{step:number(d.step)}};
    case 'user/message':
      return {...base,data:{sourceKind:text(d.source?.kind,128),callId:text(d.source?.callId)}};
    case 'session/end-seed':
      return {...base,data:{inherited:d.inherited===true}};
    default: return null;
  }
}

function pendingFromCanonicalResult(content) {
  const block=Array.isArray(content)?content.find(x=>x?.type==='text'):null;
  if(typeof block?.text!=='string'||block.text.length>1024*1024) return null;
  try { const value=JSON.parse(block.text); return value!==null&&typeof value==='object'&&!Array.isArray(value)?value.pending===true:null; }
  catch { return null; }
}

export function events(home,id,options={}) {
  const found=selectedSessions(home,id);
  if (found.length !== 1) fail(found.length ? 'ambiguous-session-id' : 'session-not-found');
  const g=found[0], h=readHeader(g);
  if (h.id!==id) fail('session-identity-mismatch');
  if (h.version!==SUPPORTED_VERSION) fail('unsupported-session-format-'+h.version);
  const offset=options.offset??0;
  if (number(offset)===null || offset>h.stat.size || offset>0 && offset<h.headerBytes) fail('invalid-compressed-offset');
  const maxBytes=Math.min(MAX_BYTES,Math.max(4096,options.maxBytes??DEFAULT_BYTES));
  const maxEvents=Math.min(50000,Math.max(1,options.maxEvents??4096));
  const start=offset===0?h.headerBytes:offset;
  const {buffer,stat}=readChunk(g.file,start,maxBytes);
  const scan=g.compressed?scanFrames(buffer):{frames:[]};
  if (!g.compressed) {
    if (start>h.headerBytes && readChunk(g.file,start-1,1).buffer[0]!==10) fail('raw-offset-not-record-boundary');
    const end=buffer.lastIndexOf(10); if (end>=0) scan.frames.push({start:0,end:end+1}); else if(buffer.length) scan.tornStart=0;
  }
  let last=options.afterSeq??(start===h.headerBytes?-1:null), turn=options.turn??null;
  const sequenceVerified=last!==null;
  const out=[]; let next=start; let consumedRows=0;
  for(const f of scan.frames) {
    if(consumedRows>=maxEvents) break;
    const bytes=g.compressed?zstdDecompressSync(buffer.subarray(f.start,f.end),{maxOutputLength:OUTPUT_LIMIT}):buffer.subarray(f.start,f.end);
    if(!bytes.length||bytes.at(-1)!==10) fail('torn-record-inside-complete-frame');
    const rows=bytes.toString('utf8').split('\n'); rows.pop();
    for(const raw of rows) {
      const e=JSON.parse(raw);
      if(number(e.seq)===null||number(e.time)===null||typeof e.type!=='string'||e.data===null||typeof e.data!=='object'||Array.isArray(e.data)) fail('invalid-event-envelope');
      if(last!==null&&e.seq!==last+1) fail('sequence-gap');
      last=e.seq; consumedRows++;
      if(!KNOWN.has(e.type)&&e.ignorable!==true) fail('unknown-required-event-'+e.type);
      if(e.type==='turn/start') turn=number(e.data.turn);
      const event=projectEvent(e,turn); if(event) out.push({...event,sessionId:id});
      if(e.type==='turn/end') turn=null;
    }
    next=start+f.end;
  }
  const stable=fs.statSync(g.file);
  if(stable.size<stat.size||stable.ino!==stat.ino||stable.dev!==stat.dev) fail('file-generation-changed');
  const hasMore=next<stat.size;
  const blocked=hasMore&&next===start&&buffer.length>=maxBytes;
  if(blocked) fail('frame-exceeds-read-budget');
  return {events:out,batch:{kind:'batch',sessionId:id,version:h.version,createdAt:h.createdAt,isSeeded:h.isSeeded,fromOffset:offset,nextOffset:next,lastSequence:last,currentTurn:turn,sequenceVerified,sourceSize:stat.size,hasMore,tornTail:scan.tornStart!==undefined,fileIdentity:String(stat.dev)+':'+String(stat.ino),consumedRows}};
}

function cli(argv) {
  if(argv[0]==='--self-check') {
    const major=Number(process.versions.node.split('.')[0]);
    if(major<24||typeof zstdDecompressSync!=='function') fail('node24-required');
    console.log(JSON.stringify({readerVersion:1,sessionVersion:SUPPORTED_VERSION,nodeMajor:major,zstd:true})); return;
  }
  if(argv[0]==='--catalog'&&argv[1]) { console.log(JSON.stringify(catalog(argv[1]))); return; }
  if(argv[0]!=='--events'||!argv[1]||!argv[2]) fail('usage: --catalog DSH_HOME | --events DSH_HOME sessionId [--offset N --after-seq N --turn N --max-bytes N --max-events N]');
  const flags={'--offset':'offset','--after-seq':'afterSeq','--turn':'turn','--max-bytes':'maxBytes','--max-events':'maxEvents'}, opts={};
  for(let n=3;n<argv.length;n+=2) {
    const key=flags[argv[n]],value=Number(argv[n+1]);
    if(!key||!Number.isSafeInteger(value)||(key==='afterSeq'?value< -1:value<0)) fail('invalid-option');
    opts[key]=value;
  }
  const result=events(argv[1],argv[2],opts);
  for(const event of result.events) console.log(JSON.stringify(event));
  console.log(JSON.stringify(result.batch));
}
if(process.argv[1]&&path.resolve(process.argv[1])===path.resolve(fileURLToPath(import.meta.url))) {
  try { cli(process.argv.slice(2)); }
  catch(e) {
    const reason=e instanceof SyntaxError?'invalid-json-record':/^[a-z0-9_/-]{1,180}$/i.test(e.message??'')?e.message:'read-or-decode-error';
    console.log(JSON.stringify({kind:'error',reason})); process.exitCode=2;
  }
}
