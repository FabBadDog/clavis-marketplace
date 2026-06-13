// clavis-pwsh.jsx — A PowerShell console rendered in CLAVIS register.
// Original design — references PowerShell language conventions, not Windows Terminal UI.

const { useState: useStatePS, useEffect: useEffectPS, useRef: useRefPS, useMemo: useMemoPS } = React;

// ─────────────────────────────────────────────────────────────────────────────
// Tokenizer — minimal PowerShell colorizer
// ─────────────────────────────────────────────────────────────────────────────

const PS_KEYWORDS = new Set([
  'if','else','elseif','foreach','for','while','do','switch','function','filter',
  'return','break','continue','try','catch','finally','throw','param','process','begin','end',
  'in','not','and','or','class','enum','using','module'
]);
const PS_OPERATORS = new Set([
  '-eq','-ne','-lt','-gt','-le','-ge','-like','-notlike','-match','-notmatch','-contains',
  '-notcontains','-in','-notin','-replace','-split','-join','-and','-or','-not','-band','-bor','-xor',
  '-is','-isnot','-as','-f'
]);

function tokenize(line) {
  // returns array of {t, v} where t = kind
  const out = [];
  let i = 0;
  const push = (t, v) => out.push({ t, v });
  while (i < line.length) {
    const c = line[i];
    // comment
    if (c === '#') { push('cmt', line.slice(i)); break; }
    // string
    if (c === '"' || c === "'") {
      const q = c; let j = i + 1;
      while (j < line.length && line[j] !== q) j++;
      push('str', line.slice(i, j + 1)); i = j + 1; continue;
    }
    // variable
    if (c === '$') {
      let j = i + 1;
      if (line[j] === '{') { while (j < line.length && line[j] !== '}') j++; j++; }
      else { while (j < line.length && /[A-Za-z0-9_:]/.test(line[j])) j++; }
      push('var', line.slice(i, j)); i = j; continue;
    }
    // operator / parameter (-Name)
    if (c === '-' && /[A-Za-z]/.test(line[i + 1] || '')) {
      let j = i + 1; while (j < line.length && /[A-Za-z]/.test(line[j])) j++;
      const tok = line.slice(i, j);
      push(PS_OPERATORS.has(tok.toLowerCase()) ? 'op' : 'param', tok);
      i = j; continue;
    }
    // pipe / redirect / brace / paren / semicolon
    if ('|;{}()[]'.includes(c)) { push('punct', c); i++; continue; }
    // number
    if (/[0-9]/.test(c)) {
      let j = i; while (j < line.length && /[0-9.]/.test(line[j])) j++;
      // unit?
      while (j < line.length && /[KMGTPkmgtpb]/.test(line[j])) j++;
      push('num', line.slice(i, j)); i = j; continue;
    }
    // word — could be cmdlet (Verb-Noun), keyword, or bareword
    if (/[A-Za-z_]/.test(c)) {
      let j = i; while (j < line.length && /[A-Za-z0-9_\-:.]/.test(line[j])) j++;
      const w = line.slice(i, j);
      if (PS_KEYWORDS.has(w.toLowerCase())) push('kw', w);
      else if (/^[A-Z][a-z]+-[A-Z][A-Za-z]+$/.test(w)) push('cmd', w);
      else push('id', w);
      i = j; continue;
    }
    // whitespace / other
    let j = i; while (j < line.length && /\s/.test(line[j])) j++;
    if (j > i) { push('ws', line.slice(i, j)); i = j; continue; }
    push('txt', c); i++;
  }
  return out;
}

const TOK_COLOR = {
  kw:    { color: 'var(--text-bright)', fontWeight: 500 },
  cmd:   { color: 'var(--clavis)' },
  param: { color: 'var(--gold)', opacity: .9 },
  op:    { color: 'var(--info)', opacity: .85 },
  var:   { color: 'var(--human)' },
  str:   { color: '#C7A86B' },        // muted amber for strings
  num:   { color: '#B89A66' },
  cmt:   { color: 'var(--text-dim)', fontStyle: 'italic' },
  punct: { color: 'var(--text-dim)' },
  id:    { color: 'var(--text)' },
  ws:    {},
  txt:   { color: 'var(--text)' },
};

function Tokens({ tokens }) {
  return (
    <>
      {tokens.map((t, i) => (
        <span key={i} style={TOK_COLOR[t.t] || TOK_COLOR.txt}>{t.v}</span>
      ))}
    </>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Prompt — Verb-Noun · Path · time · mode  · »
// ─────────────────────────────────────────────────────────────────────────────

function Prompt({ user = 'corvus', host = 'NORDIC-RUNE', cwd = 'C:\\src\\clavis', mode = 'PS', git, history }) {
  return (
    <div style={{
      display:'flex', alignItems:'baseline', gap:0,
      fontFamily:'var(--font-mono)', fontSize:11, lineHeight:1.6,
      color:'var(--text-dim)',
    }}>
      <span style={{
        color:'var(--clavis)', opacity:.9, letterSpacing:'1px',
        fontFamily:'var(--font-sans)', fontWeight:600, fontSize:9, textTransform:'uppercase',
        marginRight:8, paddingRight:8, borderRight:'1px solid var(--faint)'
      }}>{mode}</span>
      <span style={{ color:'var(--human)', opacity:.85 }}>{user}</span>
      <span style={{ color:'var(--text-faint)', margin:'0 2px' }}>@</span>
      <span style={{ color:'var(--text)', opacity:.75 }}>{host}</span>
      <span style={{ color:'var(--text-xfaint)', margin:'0 8px' }}>·</span>
      <span style={{ color:'var(--text)', opacity:.85 }}>{cwd}</span>
      {git && (
        <>
          <span style={{ color:'var(--text-xfaint)', margin:'0 8px' }}>·</span>
          <span style={{ color:'var(--gold)', opacity:.85 }}>⎇ {git.branch}</span>
          {git.dirty > 0 && <span style={{ color:'var(--warn)', marginLeft:5, opacity:.85 }}>±{git.dirty}</span>}
          {git.ahead > 0 && <span style={{ color:'var(--text-dim)', marginLeft:5 }}>↑{git.ahead}</span>}
        </>
      )}
      <span style={{ marginLeft:'auto', color:'var(--text-xfaint)', fontSize:9, letterSpacing:'.4px' }}>
        {history && `#${history}`}
      </span>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Output blocks — informational, table, error, warning, verbose, success
// ─────────────────────────────────────────────────────────────────────────────

const STREAM_COLOR = {
  output:  { color:'var(--text)' },
  error:   { color:'var(--error)' },
  warning: { color:'var(--warn)' },
  verbose: { color:'var(--info)', opacity:.85 },
  debug:   { color:'var(--text-dim)' },
  success: { color:'var(--ok)' },
  info:    { color:'var(--clavis)' },
};

function StreamLine({ stream = 'output', children }) {
  const tag = stream === 'error' ? 'ERR' : stream === 'warning' ? 'WRN' : stream === 'verbose' ? 'VRB' : stream === 'debug' ? 'DBG' : stream === 'success' ? 'OK ' : stream === 'info' ? 'INF' : '   ';
  const showTag = stream !== 'output';
  return (
    <div style={{
      display:'flex', gap:10, padding:'1px 0',
      fontFamily:'var(--font-mono)', fontSize:11, lineHeight:1.65,
    }}>
      {showTag && (
        <span style={{
          fontFamily:'var(--font-sans)', fontSize:8, fontWeight:600, letterSpacing:'1.5px',
          color: STREAM_COLOR[stream].color, opacity:.7,
          width:24, flexShrink:0, paddingTop:2,
        }}>{tag}</span>
      )}
      {!showTag && <span style={{ width:24, flexShrink:0 }} />}
      <span style={{ ...STREAM_COLOR[stream], whiteSpace:'pre-wrap' }}>{children}</span>
    </div>
  );
}

// Format-Table style output
function PSTable({ columns, rows }) {
  return (
    <div style={{ fontFamily:'var(--font-mono)', fontSize:11, lineHeight:1.55, padding:'4px 0 6px 24px' }}>
      <div style={{ display:'grid', gridTemplateColumns: columns.map(c => c.w || '1fr').join(' '), columnGap:18 }}>
        {columns.map((c,i) => (
          <div key={i} style={{
            color:'var(--text-bright)', fontWeight:500,
            fontFamily:'var(--font-sans)', fontSize:9, letterSpacing:'1.5px', textTransform:'uppercase',
            paddingBottom:4, borderBottom:'1px solid var(--faint)',
            textAlign: c.align || 'left',
          }}>{c.name}</div>
        ))}
      </div>
      <div style={{ display:'grid', gridTemplateColumns: columns.map(c => c.w || '1fr').join(' '), columnGap:18, marginTop:4 }}>
        {rows.flatMap((r, ri) => columns.map((c, ci) => (
          <div key={`${ri}-${ci}`} style={{
            color: c.color ? c.color(r[c.key], r) : 'var(--text)',
            opacity: ri % 2 === 0 ? 1 : .85,
            padding:'2px 0',
            textAlign: c.align || 'left',
            whiteSpace:'nowrap', overflow:'hidden', textOverflow:'ellipsis',
          }}>{c.fmt ? c.fmt(r[c.key], r) : r[c.key]}</div>
        )))}
      </div>
    </div>
  );
}

// ErrorRecord (PowerShell's signature multi-line error)
function PSErrorRecord({ category, target, exception, fqid, hint, line }) {
  return (
    <div style={{
      margin:'4px 0 8px 24px', padding:'8px 12px',
      borderLeft:'2px solid var(--error)', background:'rgba(224,72,72,.04)',
      fontFamily:'var(--font-mono)', fontSize:11, lineHeight:1.65, color:'var(--text)',
    }}>
      <div style={{ color:'var(--error)', marginBottom:4 }}>
        <span style={{ fontFamily:'var(--font-sans)', fontSize:8, fontWeight:600, letterSpacing:'1.5px', marginRight:8 }}>ERROR</span>
        <span style={{ color:'var(--text-bright)' }}>{exception}</span>
      </div>
      {line && (
        <div style={{ color:'var(--text-dim)', marginTop:4, paddingLeft:0 }}>
          <span style={{ color:'var(--text-xfaint)' }}>at line:</span> <span style={{ color:'var(--text)' }}>{line}</span>
        </div>
      )}
      <div style={{ display:'grid', gridTemplateColumns:'auto 1fr', columnGap:14, rowGap:1, marginTop:4 }}>
        <span style={{ color:'var(--text-xfaint)', fontFamily:'var(--font-sans)', fontSize:8.5, letterSpacing:'1.5px' }}>CATEGORY</span>
        <span style={{ color:'var(--text-dim)' }}>{category}</span>
        <span style={{ color:'var(--text-xfaint)', fontFamily:'var(--font-sans)', fontSize:8.5, letterSpacing:'1.5px' }}>TARGET</span>
        <span style={{ color:'var(--text-dim)' }}>{target}</span>
        <span style={{ color:'var(--text-xfaint)', fontFamily:'var(--font-sans)', fontSize:8.5, letterSpacing:'1.5px' }}>FQID</span>
        <span style={{ color:'var(--text-dim)' }}>{fqid}</span>
      </div>
      {hint && (
        <div style={{
          marginTop:8, paddingTop:6, borderTop:'1px solid var(--faint)',
          fontFamily:'var(--font-serif)', fontStyle:'italic', fontSize:11.5,
          color:'var(--text-dim)',
        }}>
          <span style={{ color:'var(--clavis)', opacity:.7, marginRight:6 }}>›</span>
          {hint}
        </div>
      )}
    </div>
  );
}

// Progress bar — Write-Progress
function PSProgress({ activity, status, percent, secondsRemaining }) {
  return (
    <div style={{ margin:'4px 0 6px 24px', padding:'6px 0' }}>
      <div style={{ display:'flex', alignItems:'baseline', justifyContent:'space-between',
                    fontFamily:'var(--font-sans)', fontSize:9, letterSpacing:'1.5px',
                    textTransform:'uppercase', color:'var(--text-dim)' }}>
        <span><span style={{ color:'var(--clavis)' }}>▸</span> {activity}</span>
        <span style={{ fontFamily:'var(--font-mono)', textTransform:'none', letterSpacing:0 }}>
          {percent}% · {secondsRemaining}s remaining
        </span>
      </div>
      <div style={{ marginTop:5, position:'relative', height:3, background:'var(--faint)' }}>
        <div style={{ position:'absolute', left:0, top:0, bottom:0, width:`${percent}%`, background:'var(--clavis)' }} />
        <div style={{ position:'absolute', left:`${percent}%`, top:0, bottom:0, width:50,
                      background:'linear-gradient(90deg,var(--clavis),transparent)', opacity:.6 }} />
      </div>
      <div style={{ marginTop:4, fontFamily:'var(--font-mono)', fontSize:10, color:'var(--text)' }}>{status}</div>
    </div>
  );
}

// Object inspection — Format-List style
function PSObjectList({ typeName, fields }) {
  const labelW = Math.max(...fields.map(f => f.label.length));
  return (
    <div style={{
      margin:'4px 0 6px 24px', padding:'6px 12px',
      borderLeft:'1px solid var(--faint)',
      fontFamily:'var(--font-mono)', fontSize:11, lineHeight:1.7,
    }}>
      {typeName && (
        <div style={{
          fontFamily:'var(--font-sans)', fontSize:8.5, fontWeight:500, letterSpacing:'1.5px',
          textTransform:'uppercase', color:'var(--clavis)', opacity:.8, marginBottom:5,
        }}>{typeName}</div>
      )}
      {fields.map((f, i) => (
        <div key={i} style={{ display:'flex', gap:12 }}>
          <span style={{ color:'var(--text-dim)', minWidth: `${labelW+2}ch` }}>
            {f.label.padEnd(labelW)} :
          </span>
          <span style={{ color: f.color || 'var(--text-bright)' }}>{f.value}</span>
        </div>
      ))}
    </div>
  );
}

// Tab-completion popover
function CompletionPopover({ items, selected }) {
  return (
    <div style={{
      position:'absolute', left:0, bottom:'100%', marginBottom:4,
      minWidth:340, maxHeight:200, overflow:'auto',
      background:'var(--surface-2)', border:'1px solid var(--line)',
      boxShadow:'0 -4px 0 rgba(0,0,0,.4)',
      fontFamily:'var(--font-mono)', fontSize:10.5,
    }}>
      <div style={{
        padding:'4px 10px', borderBottom:'1px solid var(--faint)', background:'var(--surface)',
        fontFamily:'var(--font-sans)', fontSize:8, fontWeight:500, letterSpacing:'1.8px',
        textTransform:'uppercase', color:'var(--text-dim)',
        display:'flex', justifyContent:'space-between',
      }}>
        <span>Completions · {items.length}</span>
        <span style={{ color:'var(--text-xfaint)' }}>TAB ↹  ESC ✕</span>
      </div>
      {items.map((it, i) => (
        <div key={i} style={{
          display:'grid', gridTemplateColumns:'74px 1fr auto', columnGap:10,
          padding:'3px 10px', alignItems:'baseline',
          background: i === selected ? 'rgba(127,181,216,.06)' : 'transparent',
          borderLeft: `1.5px solid ${i === selected ? 'var(--clavis)' : 'transparent'}`,
        }}>
          <span style={{
            fontFamily:'var(--font-sans)', fontSize:8, fontWeight:500, letterSpacing:'1.3px',
            textTransform:'uppercase', color:'var(--text-faint)',
          }}>{it.kind}</span>
          <span style={{ color: i === selected ? 'var(--text-bright)' : 'var(--text)' }}>{it.text}</span>
          <span style={{ color:'var(--text-xfaint)', fontSize:9.5, fontFamily:'var(--font-serif)', fontStyle:'italic' }}>{it.hint}</span>
        </div>
      ))}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// History pane — sidebar
// ─────────────────────────────────────────────────────────────────────────────

function HistoryPane({ items, selected, onSelect }) {
  return (
    <div style={{
      width: 220, flexShrink: 0,
      borderRight: '1px solid var(--line)', background: 'var(--surface)',
      display:'flex', flexDirection:'column', overflow:'hidden',
    }}>
      <div style={{
        padding:'8px 14px', borderBottom:'1px solid var(--line)',
        fontFamily:'var(--font-sans)', fontSize:8.5, fontWeight:500, letterSpacing:'2px',
        textTransform:'uppercase', color:'var(--text-dim)',
        display:'flex', justifyContent:'space-between', alignItems:'center',
      }}>
        <span>History</span>
        <span style={{ color:'var(--text-xfaint)', fontFamily:'var(--font-mono)', letterSpacing:0 }}>{items.length}</span>
      </div>
      <div style={{ flex:1, overflow:'auto', padding:'4px 0' }}>
        {items.map((h, i) => (
          <div key={i} onClick={() => onSelect && onSelect(i)}
               style={{
                 padding:'5px 14px', cursor:'pointer',
                 borderLeft: `1.5px solid ${i === selected ? 'var(--clavis)' : 'transparent'}`,
                 background: i === selected ? 'rgba(127,181,216,.03)' : 'transparent',
               }}>
            <div style={{
              display:'flex', justifyContent:'space-between',
              fontFamily:'var(--font-mono)', fontSize:9, color:'var(--text-xfaint)', marginBottom:2,
            }}>
              <span>#{String(h.id).padStart(3,'0')}</span>
              <span>{h.t}</span>
              <span style={{ color: h.exit === 0 ? 'var(--ok)' : h.exit > 0 ? 'var(--error)' : 'var(--text-dim)' }}>
                {h.exit === 0 ? '✓' : h.exit > 0 ? '✕' : '·'}
              </span>
            </div>
            <div style={{
              fontFamily:'var(--font-mono)', fontSize:10.5, color: i === selected ? 'var(--text-bright)' : 'var(--text-dim)',
              whiteSpace:'nowrap', overflow:'hidden', textOverflow:'ellipsis',
            }}>{h.cmd}</div>
          </div>
        ))}
      </div>
      <div style={{
        padding:'6px 14px', borderTop:'1px solid var(--line)', background:'var(--bg)',
        fontFamily:'var(--font-mono)', fontSize:9, color:'var(--text-xfaint)',
      }}>
        ↑↓ recall · ⌃R search · F7 picker
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Sample console transcript
// ─────────────────────────────────────────────────────────────────────────────

const PROCESSES = [
  { name:'pwsh',           id:48211, cpu:'12.4%', mem:'248 MB', threads:14, start:'09:14:02' },
  { name:'corvus.host',    id:51904, cpu:' 4.1%', mem:'612 MB', threads:22, start:'09:14:05' },
  { name:'clavis.daemon',  id:51908, cpu:'31.7%', mem:'1.2 GB', threads:38, start:'09:14:05' },
  { name:'ndjson.gateway', id:52441, cpu:' 0.8%', mem:' 84 MB', threads: 6, start:'09:21:11' },
  { name:'dotnet',         id:53012, cpu:' 2.0%', mem:'196 MB', threads: 9, start:'09:34:48' },
  { name:'ssh-agent',      id:11204, cpu:' 0.0%', mem:'  6 MB', threads: 2, start:'08:02:00' },
];

const HISTORY_SAMPLE = [
  { id: 81, t:'09:14', exit: 0, cmd: 'cd C:\\src\\clavis' },
  { id: 82, t:'09:14', exit: 0, cmd: 'git status' },
  { id: 83, t:'09:15', exit: 0, cmd: 'Get-ChildItem -Recurse *.fsproj' },
  { id: 84, t:'09:18', exit: 1, cmd: 'dotnet build .\\Corvus.sln' },
  { id: 85, t:'09:22', exit: 0, cmd: 'Test-NdjsonStream -Path .\\fixtures\\sample.ndjson' },
  { id: 86, t:'09:30', exit: 0, cmd: 'Get-Process | Sort-Object CPU -Descending | Select -First 6' },
  { id: 87, t:'09:34', exit:-1, cmd: 'Invoke-WebRequest -Uri https://api.corvus.dev/...' },
  { id: 88, t:'09:35', exit: 0, cmd: 'Measure-Command { Parse-Stream $events }' },
  { id: 89, t:'09:38', exit: 2, cmd: 'Remove-Item -Recurse .\\out\\' },
  { id: 90, t:'09:41', exit: 0, cmd: 'Format-List -InputObject $session' },
  { id: 91, t:'09:48', exit: 0, cmd: '$env:CLAVIS_KEY = (Get-Secret CLAVIS_KEY)' },
  { id: 92, t:'10:02', exit: 0, cmd: 'Watch-NdjsonStream -Tail 200' },
];

const COMPLETIONS_SAMPLE = [
  { kind: 'cmdlet',   text: 'Get-ChildItem',      hint: 'Gets the items and child items in one or more locations' },
  { kind: 'cmdlet',   text: 'Get-Content',        hint: 'Gets the content of the item at the specified location' },
  { kind: 'cmdlet',   text: 'Get-Process',        hint: 'Gets the processes that are running on the local computer' },
  { kind: 'cmdlet',   text: 'Get-Service',        hint: 'Gets the services on the computer' },
  { kind: 'function', text: 'Get-CorvusSession',  hint: 'Returns the active Corvus runtime session' },
  { kind: 'alias',    text: 'gci → Get-ChildItem', hint: '' },
  { kind: 'history',  text: 'Get-Process | Sort-Object CPU -Descending', hint: '#86' },
];

// ─────────────────────────────────────────────────────────────────────────────
// Console body — composes all the blocks
// ─────────────────────────────────────────────────────────────────────────────

function ConsoleBody({ showCompletions, runningCommand, padX = 22 }) {
  const promptCommon = { user:'corvus', host:'NORDIC-RUNE', cwd:'C:\\src\\clavis', mode:'PS', git:{ branch:'main', dirty:3, ahead:1 } };

  return (
    <div style={{ flex:1, minHeight:0, overflowY:'auto', padding:`10px ${padX}px 14px`, background:'var(--bg)' }}>
      {/* ── Banner ──────────────────────────────────────────────────────── */}
      <div style={{
        padding:'8px 0 12px', borderBottom:'1px solid var(--faint)',
        marginBottom:14, fontFamily:'var(--font-mono)', fontSize:11, lineHeight:1.7, color:'var(--text-dim)',
      }}>
        <div style={{
          fontFamily:'var(--font-sans)', fontSize:9, fontWeight:600, letterSpacing:'2.5px',
          textTransform:'uppercase', color:'var(--clavis)', marginBottom:4,
        }}>PowerShell 7.4.6 · Clavis Edition</div>
        <div>Loaded profile <span style={{ color:'var(--text)' }}>$PROFILE.Clavis</span> in <span style={{ color:'var(--gold)' }}>184 ms</span> · <span style={{ color:'var(--ok)' }}>12 modules ready</span></div>
        <div style={{ color:'var(--text-xfaint)', marginTop:2 }}>Type <span style={{ color:'var(--clavis)' }}>help</span> for the cmdlet index, <span style={{ color:'var(--clavis)' }}>get-help &lt;name&gt;</span> for any cmdlet, <span style={{ color:'var(--clavis)' }}>about_*</span> for concepts.</div>
      </div>

      {/* ── Entry 1: Get-ChildItem ─────────────────────────────────────── */}
      <Prompt {...promptCommon} history={86} />
      <CommandLine line={'Get-ChildItem -Path .\\src -Filter *.fs -Recurse | Where-Object Length -gt 4kb'} />
      <PSTable
        columns={[
          { name:'Mode',   key:'mode',   w:'72px',  fmt:v => v, color: ()=>'var(--text-dim)' },
          { name:'Last Write',  key:'lw', w:'120px', color:()=>'var(--text-dim)' },
          { name:'Length', key:'len',   w:'80px',  align:'right', color:()=>'var(--gold)' },
          { name:'Name',   key:'name',  w:'1fr',   color:()=>'var(--text-bright)' },
        ]}
        rows={[
          { mode:'-a----', lw:'2026-04-22 14:08', len:'  6 213', name:'NdjsonParser.fs' },
          { mode:'-a----', lw:'2026-04-22 14:08', len:' 12 044', name:'StreamEvent.fs' },
          { mode:'-a----', lw:'2026-04-25 09:30', len:'  4 802', name:'Tokenizer.fs' },
          { mode:'-a----', lw:'2026-04-25 11:21', len:'  9 117', name:'SessionHost.fs' },
          { mode:'-a----', lw:'2026-04-26 17:52', len:' 18 290', name:'Renderer.fs' },
        ]}
      />

      {/* ── Entry 2: Get-Process ───────────────────────────────────────── */}
      <div style={{ height: 14 }} />
      <Prompt {...promptCommon} history={87} />
      <CommandLine line={'Get-Process pwsh, corvus.* | Sort-Object CPU -Descending | Format-Table -AutoSize'} />
      <PSTable
        columns={[
          { name:'PID',     key:'id',      w:'72px',  align:'right', color:()=>'var(--text-dim)' },
          { name:'CPU %',   key:'cpu',     w:'72px',  align:'right', color:(v)=> parseFloat(v) > 20 ? 'var(--warn)' : 'var(--text)' },
          { name:'Memory',  key:'mem',     w:'90px',  align:'right', color:()=>'var(--gold)' },
          { name:'Threads', key:'threads', w:'72px',  align:'right', color:()=>'var(--text-dim)' },
          { name:'Started', key:'start',   w:'90px',                color:()=>'var(--text-xfaint)' },
          { name:'Process', key:'name',    w:'1fr',                  color:()=>'var(--clavis)' },
        ]}
        rows={PROCESSES}
      />

      {/* ── Entry 3: an error ──────────────────────────────────────────── */}
      <div style={{ height: 14 }} />
      <Prompt {...promptCommon} history={88} />
      <CommandLine line={'Invoke-WebRequest -Uri "https://api.corvus.dev/v2/streams" -Headers $hdrs'} />
      <PSErrorRecord
        exception="The remote name could not be resolved: 'api.corvus.dev'"
        line='Invoke-WebRequest -Uri "https://api.corvus.dev/v2/streams" -Headers $hdrs'
        category="ResourceUnavailable: (System.Net.Http.HttpRequestException)"
        target="System.Net.Http.HttpClient"
        fqid="WebCmdletWebResponseException,Microsoft.PowerShell.Commands.InvokeWebRequestCommand"
        hint="DNS resolution failed. Check your connection, or set $env:HTTPS_PROXY if you are behind a corporate proxy."
      />

      {/* ── Entry 4: object — Format-List style ────────────────────────── */}
      <div style={{ height: 14 }} />
      <Prompt {...promptCommon} history={89} />
      <CommandLine line={'$session = Get-CorvusSession; $session | Format-List'} />
      <PSObjectList
        typeName="Corvus.Runtime.Session"
        fields={[
          { label:'Id',           value:'7f3a-e019-22d4-…',     color:'var(--text)' },
          { label:'StartedAt',    value:'2026-04-27T08:02:14Z', color:'var(--text)' },
          { label:'Host',         value:'NORDIC-RUNE',          color:'var(--clavis)' },
          { label:'PowerShell',   value:'7.4.6',                color:'var(--text)' },
          { label:'Modules',      value:'Corvus.Core, Corvus.Stream, Clavis.Profile',  color:'var(--gold)' },
          { label:'EventsParsed', value:'12 488',               color:'var(--ok)' },
          { label:'Errors',       value:'2',                    color:'var(--warn)' },
          { label:'Tail',         value:'$session.Stream | Select -Last 5', color:'var(--human)' },
        ]}
      />

      {/* ── Entry 5: streams + progress ────────────────────────────────── */}
      <div style={{ height: 14 }} />
      <Prompt {...promptCommon} history={90} />
      <CommandLine line={'Watch-NdjsonStream -Path .\\fixtures\\stream.ndjson -Tail 500 -Verbose -WarningAction Continue'} />
      <StreamLine stream="verbose">Opening stream at .\fixtures\stream.ndjson (read-only, shared)</StreamLine>
      <StreamLine stream="verbose">Tailing from offset 184 392 — 500 lines requested</StreamLine>
      <StreamLine stream="info">12 488 events parsed · 8 schemas observed · last event 1.2s ago</StreamLine>
      <StreamLine stream="warning">Schema drift on event tag <span style={{ color:'var(--text-bright)' }}>"tool.start"</span> — field <span style={{ color:'var(--text-bright)' }}>cwd</span> appeared at v=2026.04, expected v=2026.03</StreamLine>
      <StreamLine stream="warning">Throttling to 60 events/sec — buffer at 78%</StreamLine>
      <PSProgress activity="Watch-NdjsonStream" status="Reading frames · 384 / 500 · throughput 162 KB/s" percent={77} secondsRemaining={4} />
      <StreamLine stream="success">Tail complete — 500 events ingested in 3.1s</StreamLine>

      {/* ── Entry 6: live, currently running with caret ────────────────── */}
      <div style={{ height: 14 }} />
      <Prompt {...promptCommon} history={91} />
      <CommandLine line={runningCommand} caret completions={showCompletions ? COMPLETIONS_SAMPLE : null} />
    </div>
  );
}

function CommandLine({ line, caret = false, completions }) {
  // tokenize and render
  const toks = useMemoPS(() => tokenize(line || ''), [line]);
  return (
    <div style={{
      position:'relative',
      display:'flex', alignItems:'baseline', gap:10,
      padding:'2px 0 4px',
      fontFamily:'var(--font-mono)', fontSize:11, lineHeight:1.65,
    }}>
      <span style={{
        color:'var(--human)', fontFamily:'var(--font-serif)', fontSize:18, lineHeight:'14px', opacity:.55,
        transform:'translateY(2px)',
      }}>›</span>
      <span style={{ flex:1, whiteSpace:'pre-wrap' }}>
        <Tokens tokens={toks} />
        {caret && <Caret />}
      </span>
      {completions && (
        <div style={{ position:'absolute', left:24, bottom:'100%' }}>
          <CompletionPopover items={completions} selected={2} />
        </div>
      )}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Status bar — bottom — context-rich
// ─────────────────────────────────────────────────────────────────────────────

function PSStatusBar({ runtime, parsing }) {
  const cell = (label, value, color) => (
    <span style={{ display:'inline-flex', alignItems:'baseline', gap:6 }}>
      <span style={{ color:'var(--text-xfaint)', fontFamily:'var(--font-sans)', fontSize:8, fontWeight:500, letterSpacing:'1.5px', textTransform:'uppercase' }}>{label}</span>
      <span style={{ color: color || 'var(--text)', fontFamily:'var(--font-mono)', fontSize:9.5 }}>{value}</span>
    </span>
  );
  return (
    <div style={{
      display:'flex', alignItems:'center', gap:18, height:24,
      padding:'0 18px', background:'var(--surface)', borderTop:'1px solid var(--line)',
      flexShrink:0,
    }}>
      <span style={{ display:'inline-flex', alignItems:'center', gap:6 }}>
        <PulseDot kind={parsing ? 'working' : 'idle'} size={4} />
        <span style={{ fontFamily:'var(--font-sans)', fontSize:8, fontWeight:600, letterSpacing:'2px', textTransform:'uppercase', color:'var(--text)' }}>
          {parsing ? 'Parsing' : 'Ready'}
        </span>
      </span>
      <span style={{ color:'var(--text-faint)' }}>│</span>
      {cell('exec', 'RemoteSigned', 'var(--gold)')}
      {cell('host', '7.4.6', 'var(--clavis)')}
      {cell('cwd', 'C:\\src\\clavis', 'var(--text)')}
      <span style={{ flex:1 }} />
      {cell('jobs', '2', 'var(--info)')}
      {cell('hist', '92', 'var(--text-dim)')}
      {cell('mem', '248 MB', 'var(--text-dim)')}
      {cell('runtime', runtime, 'var(--ok)')}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Top — the assembled console
// ─────────────────────────────────────────────────────────────────────────────

function PowerShellConsole() {
  const [showCompletions, setShowCompletions] = useStatePS(true);
  const [running, setRunning] = useStatePS('Get-ChildItem -Filter *.fs -Recurse | Where-Object Length -gt ');
  const [tabs] = useStatePS([
    { id:'pwsh-1',  name:'pwsh',           status:'working', meta:'corvus@nordic-rune' },
    { id:'pwsh-2',  name:'pwsh',           status:'idle',    meta:'logs · S:' },
    { id:'wsl',     name:'wsl · debian',   status:'idle',    meta:'~/clavis' },
    { id:'azure',   name:'cloud · azure',  status:'waiting', meta:'subscription · prod-eu' },
  ]);
  const [active, setActive] = useStatePS('pwsh-1');
  const [histSel, setHistSel] = useStatePS(11);

  return (
    <div className="clavis-root" style={{
      width:'100%', height:'100%', display:'flex', flexDirection:'column',
      background:'var(--bg)', overflow:'hidden', position:'relative',
    }}>
      {/* Title bar */}
      <TitleBar name="POWERSHELL" meta="pwsh 7.4.6 · clavis profile" status="working" scan>
        <span style={{
          fontFamily:'var(--font-sans)', fontSize:8, fontWeight:500, letterSpacing:'1.5px',
          textTransform:'uppercase', color:'var(--text-faint)', marginRight:14,
        }}>
          <span style={{ color:'var(--text-dim)' }}>SESSION</span> · 7f3a-e019
        </span>
        <span style={{
          display:'inline-flex', alignItems:'center', gap:8, marginRight:10,
          fontFamily:'var(--font-mono)', fontSize:9, color:'var(--text-faint)',
        }}>
          <span>↻ split</span><span>⊞ pane</span><span>⌃,</span>
        </span>
      </TitleBar>

      {/* Tab strip */}
      <TabBar tabs={tabs} active={active} onSelect={setActive} />

      {/* Body — sidebar + main */}
      <div style={{ flex:1, minHeight:0, display:'flex' }}>
        <HistoryPane items={HISTORY_SAMPLE} selected={histSel} onSelect={setHistSel} />
        <div style={{ flex:1, minWidth:0, display:'flex', flexDirection:'column', position:'relative' }}>
          <Breadcrumb parts={['NORDIC-RUNE','C:','src','clavis']} padX={18} />
          <ConsoleBody showCompletions={showCompletions} runningCommand={running} padX={22} />
        </div>
      </div>

      {/* Status + hint bar */}
      <PSStatusBar runtime="01:48:22" parsing={true} />
      <HintBar
        padX={18}
        hints={[
          { keys:['TAB'],     label:'complete' },
          { keys:['↑','↓'],   label:'history' },
          { keys:['⌃','R'],   label:'reverse-i-search' },
          { keys:['F7'],      label:'history picker' },
          { keys:['F8'],      label:'prefix search' },
          { keys:['⌃','C'],   label:'cancel' },
          { keys:['⌃','L'],   label:'clear' },
          { keys:['⌃','+'],   label:'split' },
        ]}
      />

      <ResizeHandle />
    </div>
  );
}

Object.assign(window, {
  PowerShellConsole, tokenize, Tokens, Prompt, CommandLine,
  PSTable, PSErrorRecord, PSProgress, PSObjectList, PSStatusBar,
  CompletionPopover, HistoryPane, ConsoleBody, StreamLine,
});
