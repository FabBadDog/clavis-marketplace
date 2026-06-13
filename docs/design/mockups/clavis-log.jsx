// clavis-log.jsx — A panel that displays an NDJSON event stream as a
// beautiful, human-readable log. The wire format is full-fidelity JSON
// (one event per line); the panel renders each event as typeset prose with
// timestamp · sigil · subject · phrase · meta.
//
// We intentionally keep the source NDJSON visible at the bottom (collapsible)
// so the dual register — machine line vs editorial line — is the point.

const { useState: _useState, useMemo: _useMemo, useRef: _useRef, useEffect: _useEffect } = React;

// ─────────────────────────────────────────────────────────────────────────────
// Source NDJSON — a realistic streaming session.
// One line = one JSON object. The renderer below interprets each by `event`.
// ─────────────────────────────────────────────────────────────────────────────

const NDJSON_LINES = [
  '{"t":"2026-04-27T14:22:03.118Z","event":"session.open","session":"s_8f2a","model":"claude-opus-4.6","workspace":"corvus"}',
  '{"t":"2026-04-27T14:22:03.402Z","event":"context.load","files":2,"tokens":1284,"paths":["src/Corvus/Types.fs","src/Corvus/SessionApi.fs"]}',
  '{"t":"2026-04-27T14:22:04.011Z","event":"message","role":"human","tokens":42,"text":"Implement the NdjsonParser module in Corvus. Handle malformed lines with Result types and support async streaming over IAsyncEnumerable<string>."}',
  '{"t":"2026-04-27T14:22:04.290Z","event":"turn.begin","turn":4,"budget":{"tokens":200000,"used":56112}}',
  '{"t":"2026-04-27T14:22:07.402Z","event":"message","role":"agent","tokens":312,"latency_ms":3112,"text":"I\'ll create the parser module. Let me first read the existing types to ensure StreamEvent aligns."}',
  '{"t":"2026-04-27T14:22:07.880Z","event":"tool.call","tool":"Read","args":{"path":"src/Corvus/Types.fs"},"id":"t_01"}',
  '{"t":"2026-04-27T14:22:08.281Z","event":"tool.return","id":"t_01","ok":true,"bytes":3612,"duration_ms":401}',
  '{"t":"2026-04-27T14:22:08.510Z","event":"tool.call","tool":"Read","args":{"path":"src/Corvus/SessionApi.fs"},"id":"t_02"}',
  '{"t":"2026-04-27T14:22:08.821Z","event":"tool.return","id":"t_02","ok":true,"bytes":2104,"duration_ms":311}',
  '{"t":"2026-04-27T14:22:09.012Z","event":"tool.call","tool":"Grep","args":{"q":"StreamEvent","glob":"src/**/*.fs"},"id":"t_03"}',
  '{"t":"2026-04-27T14:22:09.214Z","event":"tool.return","id":"t_03","ok":true,"matches":14,"files":6,"duration_ms":202}',
  '{"t":"2026-04-27T14:22:13.918Z","event":"message","role":"agent","tokens":584,"latency_ms":4704,"text":"Three functions: tryParseLine for a single record, parseStream over IAsyncEnumerable, and parseBatch for testing."}',
  '{"t":"2026-04-27T14:22:14.221Z","event":"tool.call","tool":"Write","args":{"path":"src/Corvus/NdjsonParser.fs","lines":48},"id":"t_04"}',
  '{"t":"2026-04-27T14:22:15.412Z","event":"tool.return","id":"t_04","ok":true,"bytes":1842,"duration_ms":1191}',
  '{"t":"2026-04-27T14:22:15.612Z","event":"tool.call","tool":"Edit","args":{"path":"src/Corvus/Corvus.fsproj","insertions":1},"id":"t_05"}',
  '{"t":"2026-04-27T14:22:15.918Z","event":"tool.return","id":"t_05","ok":true,"duration_ms":306}',
  '{"t":"2026-04-27T14:22:16.108Z","event":"tool.call","tool":"Bash","args":{"cmd":"dotnet build src/Corvus"},"id":"t_06"}',
  '{"t":"2026-04-27T14:22:20.214Z","event":"tool.progress","id":"t_06","stream":"stdout","line":"Build succeeded. 0 Warning(s) 0 Error(s)"}',
  '{"t":"2026-04-27T14:22:20.219Z","event":"tool.return","id":"t_06","ok":true,"duration_ms":4106,"exit":0}',
  '{"t":"2026-04-27T14:22:20.402Z","event":"permission.request","id":"p_01","summary":"Create tests/fixtures/sample.ndjson","reason":"Tests reference a missing fixture file."}',
  '{"t":"2026-04-27T14:22:21.110Z","event":"usage","tokens_in":58,"tokens_out":968,"cost_usd":0.0184,"context_pct":28}',
];

// ─────────────────────────────────────────────────────────────────────────────
// Interpreter — turn a parsed event into the parts the row renderer needs.
//   sigil   — single character or short glyph indicating event family
//   color   — sigil color
//   subject — short caps-tracked label, like a stage direction
//   phrase  — italic editorial sentence describing what happened
//   trail   — monospace technical tail (paths, durations, exit codes)
// ─────────────────────────────────────────────────────────────────────────────

const FAMILIES = {
  'session.open':       { sigil:'◇', color:'var(--clavis)', subject:'Session opened' },
  'context.load':       { sigil:'·', color:'var(--text-dim)', subject:'Context loaded' },
  'turn.begin':         { sigil:'▸', color:'var(--text-faint)', subject:'Turn began' },
  'message.human':      { sigil:'›', color:'var(--human)', subject:'Human spoke' },
  'message.agent':      { sigil:'‹', color:'var(--clavis)', subject:'Agent replied' },
  'tool.call':          { sigil:'⟢', color:'var(--text-dim)', subject:'Tool invoked' },
  'tool.progress':      { sigil:'·', color:'var(--text-faint)', subject:'Tool streaming' },
  'tool.return.ok':     { sigil:'✓', color:'var(--ok)', subject:'Tool returned' },
  'tool.return.err':    { sigil:'✕', color:'var(--error)', subject:'Tool failed' },
  'permission.request': { sigil:'▲', color:'var(--warn)', subject:'Permission requested' },
  'usage':              { sigil:'◌', color:'var(--text-dim)', subject:'Usage tallied' },
};

function interpret(ev) {
  const f = ev.event === 'tool.return'
    ? FAMILIES[ev.ok ? 'tool.return.ok' : 'tool.return.err']
    : ev.event === 'message'
      ? FAMILIES[`message.${ev.role}`]
      : FAMILIES[ev.event] || { sigil:'·', color:'var(--text-dim)', subject:ev.event };

  let phrase = null, trail = null;

  switch (ev.event) {
    case 'session.open':
      phrase = <>opened <em>{ev.workspace}</em> with <Code>{ev.model}</Code></>;
      trail = ev.session;
      break;
    case 'context.load':
      phrase = <>brought <em>{ev.files}</em> files into working memory</>;
      trail = `${ev.tokens.toLocaleString()} tokens · ${ev.paths.join(', ')}`;
      break;
    case 'turn.begin':
      phrase = <>began turn <em>{ev.turn}</em></>;
      trail = `${ev.budget.used.toLocaleString()} / ${ev.budget.tokens.toLocaleString()} tokens used`;
      break;
    case 'message':
      if (ev.role === 'human') {
        phrase = <em style={{color:'var(--text-bright)'}}>“{truncate(ev.text, 96)}”</em>;
        trail = `${ev.tokens} tokens in`;
      } else {
        phrase = <em>{truncate(ev.text, 110)}</em>;
        trail = `${ev.tokens} tokens · ${(ev.latency_ms/1000).toFixed(2)}s`;
      }
      break;
    case 'tool.call':
      phrase = <>called <Code>{ev.tool}</Code> on <Code>{primaryArg(ev.args)}</Code></>;
      trail = `id ${ev.id}`;
      break;
    case 'tool.progress':
      phrase = <em style={{color:'var(--text-faint)'}}>{truncate(ev.line, 96)}</em>;
      trail = `${ev.id} · ${ev.stream}`;
      break;
    case 'tool.return':
      if (ev.ok) {
        phrase = <>finished cleanly{ev.bytes ? <> with <em>{formatBytes(ev.bytes)}</em></> : null}{typeof ev.matches === 'number' ? <>, <em>{ev.matches}</em> matches in <em>{ev.files}</em> files</> : null}</>;
        trail = `${(ev.duration_ms/1000).toFixed(2)}s${ev.exit != null ? ` · exit ${ev.exit}` : ''}`;
      } else {
        phrase = <>returned an error</>;
        trail = `${(ev.duration_ms/1000).toFixed(2)}s · ${ev.id}`;
      }
      break;
    case 'permission.request':
      phrase = <em style={{color:'var(--warn)',opacity:.95}}>{ev.summary}</em>;
      trail = ev.reason;
      break;
    case 'usage':
      phrase = <>logged <em>{ev.tokens_out.toLocaleString()}</em> tokens out, <em>${ev.cost_usd.toFixed(4)}</em></>;
      trail = `context ${ev.context_pct}%`;
      break;
    default:
      phrase = <em>{ev.event}</em>;
  }

  return { ...f, phrase, trail };
}

function truncate(s, n) { if (!s) return ''; return s.length > n ? s.slice(0, n-1) + '…' : s; }
function primaryArg(a) { if (!a) return ''; return a.path || a.cmd || a.q || Object.values(a)[0]; }
function formatBytes(b) { if (b < 1024) return `${b} B`; if (b < 1024*1024) return `${(b/1024).toFixed(1)} kB`; return `${(b/1024/1024).toFixed(1)} MB`; }
function Code({ children }) { return <span className="cl-code" style={{fontSize:10.5}}>{children}</span>; }

// ─────────────────────────────────────────────────────────────────────────────
// Row — the unit of the log. A grid: time | sigil | subject | phrase | trail
// ─────────────────────────────────────────────────────────────────────────────

function LogRow({ line, ev, parsed, expanded, onToggle, density, showRaw }) {
  const padY = density === 'dense' ? 5 : density === 'compact' ? 7 : 9;
  // Format time as HH:MM:SS.sss
  const d = new Date(ev.t);
  const hms = d.toISOString().slice(11, 23);

  return (
    <div onClick={onToggle}
      style={{
        position:'relative',
        display:'grid',
        gridTemplateColumns:'92px 14px 150px 1fr auto',
        columnGap:14,
        alignItems:'baseline',
        padding:`${padY}px 28px`,
        borderBottom:'1px solid var(--faint)',
        cursor:'pointer',
        background: expanded ? 'rgba(127,181,216,.03)' : 'transparent',
      }}
    >
      {/* Line number + time */}
      <div style={{fontFamily:'var(--font-mono)',fontSize:9.5,color:'var(--text-faint)',textAlign:'right',letterSpacing:'.2px'}}>
        <span style={{color:'var(--text-xfaint)',marginRight:6}}>{String(line).padStart(3,'0')}</span>
        {hms}
      </div>
      {/* Sigil */}
      <div style={{
        fontFamily:'var(--font-mono)',fontSize:11,
        color: parsed.color,
        textAlign:'center',lineHeight:1,
        fontWeight:400,
      }}>{parsed.sigil}</div>
      {/* Subject */}
      <div style={{
        fontFamily:'var(--font-sans)',fontSize:9,fontWeight:600,
        letterSpacing:'2px',textTransform:'uppercase',
        color: parsed.color, opacity:.9,
        whiteSpace:'nowrap',
      }}>{parsed.subject}</div>
      {/* Phrase */}
      <div style={{
        fontFamily:'var(--font-serif)',fontSize:13.5,fontWeight:400,
        color:'var(--text)',lineHeight:1.45,
        minWidth:0,
      }}>
        {parsed.phrase}
      </div>
      {/* Trail */}
      <div style={{
        fontFamily:'var(--font-mono)',fontSize:9.5,fontWeight:400,
        color:'var(--text-dim)',opacity:.7,
        textAlign:'right',whiteSpace:'nowrap',
        maxWidth:240,overflow:'hidden',textOverflow:'ellipsis',
      }}>
        {parsed.trail}
      </div>

      {/* Expanded raw NDJSON */}
      {expanded && (
        <div style={{
          gridColumn:'2 / -1',
          marginTop:8,padding:'8px 12px',
          background:'var(--surface)',borderLeft:`1.5px solid ${parsed.color}`,
          fontFamily:'var(--font-mono)',fontSize:10,fontWeight:400,
          color:'var(--text-bright)',lineHeight:1.6,
          whiteSpace:'pre-wrap',wordBreak:'break-word',
        }}>
          {prettyJSON(ev)}
        </div>
      )}
    </div>
  );
}

function prettyJSON(o) {
  // Compact-ish pretty: each top-level key on its own line, nested objects on one line.
  const keys = Object.keys(o);
  return keys.map(k => {
    const v = o[k];
    let val;
    if (typeof v === 'string') val = JSON.stringify(v);
    else if (v && typeof v === 'object') val = JSON.stringify(v);
    else val = String(v);
    return `  ${JSON.stringify(k)}: ${val}`;
  }).join(',\n').replace(/^/, '{\n') + '\n}';
}

// ─────────────────────────────────────────────────────────────────────────────
// Filter chips — let the panel feel alive
// ─────────────────────────────────────────────────────────────────────────────

const FILTER_GROUPS = [
  { id:'all',     label:'All',         match: () => true },
  { id:'msg',     label:'Messages',    match: e => e.event === 'message' },
  { id:'tools',   label:'Tools',       match: e => e.event.startsWith('tool.') },
  { id:'system',  label:'System',      match: e => ['session.open','context.load','turn.begin','usage'].includes(e.event) },
  { id:'attn',    label:'Attention',   match: e => e.event === 'permission.request' || (e.event === 'tool.return' && !e.ok) },
];

function FilterChips({ active, onPick, counts }) {
  return (
    <div style={{display:'flex',gap:6,padding:'8px 22px 10px',borderBottom:'1px solid var(--faint)',alignItems:'center'}}>
      <span style={{fontFamily:'var(--font-sans)',fontSize:8,fontWeight:600,letterSpacing:'2px',textTransform:'uppercase',color:'var(--text-faint)',marginRight:8}}>Filter</span>
      {FILTER_GROUPS.map(g => {
        const isActive = g.id === active;
        return (
          <button key={g.id} onClick={()=>onPick(g.id)}
            style={{
              display:'inline-flex',alignItems:'center',gap:6,
              padding:'3px 9px',
              border:`1px solid ${isActive ? 'var(--clavis)' : 'var(--faint)'}`,
              background: isActive ? 'rgba(127,181,216,.06)' : 'transparent',
              color: isActive ? 'var(--text-bright)' : 'var(--text-dim)',
              fontFamily:'var(--font-sans)',fontSize:10,fontWeight:400,letterSpacing:'.3px',
            }}>
            {g.label}
            <span style={{fontFamily:'var(--font-mono)',fontSize:9,opacity:.5}}>{counts[g.id]}</span>
          </button>
        );
      })}
      <span style={{flex:1}} />
      <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--text-faint)',opacity:.7,display:'inline-flex',alignItems:'center',gap:6}}>
        <PulseDot kind="working" size={4} /> live tail
      </span>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Main panel
// ─────────────────────────────────────────────────────────────────────────────

function VariationLog({ state }) {
  const events = _useMemo(() => NDJSON_LINES.map(l => ({ raw: l, parsed: JSON.parse(l) })), []);
  const [filter, setFilter] = _useState('all');
  const [expanded, setExpanded] = _useState(() => new Set());
  const [showRawPane, setShowRawPane] = _useState(false);

  const counts = _useMemo(() => {
    const c = {};
    for (const g of FILTER_GROUPS) c[g.id] = events.filter(e => g.match(e.parsed)).length;
    return c;
  }, [events]);

  const visible = _useMemo(() => {
    const g = FILTER_GROUPS.find(x => x.id === filter);
    return events.map((e, i) => ({ ...e, idx: i+1 })).filter(e => g.match(e.parsed));
  }, [events, filter]);

  const toggle = (i) => setExpanded(s => { const n = new Set(s); n.has(i) ? n.delete(i) : n.add(i); return n; });

  return (
    <div className="clavis-root" style={{
      width:'100%',height:'100%',display:'flex',flexDirection:'column',
      background:'var(--bg)',position:'relative',overflow:'hidden',
    }}>
      <TitleBar name="CLAVIS · LOG" meta={`session s_8f2a · ${events.length} events`} status={state.status} scan={state.beam==='titlebar'} />

      {/* Header strip — title, range, source toggle */}
      <div style={{
        display:'flex',alignItems:'baseline',gap:14,
        padding:'14px 22px 12px',borderBottom:'1px solid var(--faint)',
      }}>
        <span style={{fontFamily:'var(--font-display)',fontSize:14,letterSpacing:'2px',color:'var(--text-bright)',fontWeight:500}}>EVENT STREAM</span>
        <span style={{fontFamily:'var(--font-serif)',fontStyle:'italic',fontSize:13,color:'var(--text-faint)'}}>
          ndjson · 14:22:03 → 14:22:21
        </span>
        <span style={{flex:1}} />
        <button onClick={()=>setShowRawPane(s=>!s)}
          style={{
            fontFamily:'var(--font-sans)',fontSize:9,fontWeight:500,letterSpacing:'1.5px',textTransform:'uppercase',
            color: showRawPane ? 'var(--clavis)' : 'var(--text-dim)',
            border:`1px solid ${showRawPane ? 'var(--clavis)' : 'var(--faint)'}`,
            padding:'3px 8px',
          }}>
          {showRawPane ? '◼ wire' : '◻ wire'}
        </button>
      </div>

      <FilterChips active={filter} onPick={setFilter} counts={counts} />

      {/* Body — grid header + rows + (optional) wire pane */}
      <div style={{flex:1,minHeight:0,display:'grid',gridTemplateRows:'auto 1fr',gridTemplateColumns: showRawPane ? '1.4fr 1fr' : '1fr', gap: showRawPane ? 1 : 0, background:'var(--line)', overflow:'hidden'}}>
        {/* Column header */}
        <div style={{
          gridColumn: showRawPane ? '1 / 2' : '1 / -1',
          background:'var(--surface)',
          display:'grid',gridTemplateColumns:'92px 14px 150px 1fr auto',
          columnGap:14,padding:'5px 28px',
          borderBottom:'1px solid var(--line)',
          fontFamily:'var(--font-sans)',fontSize:8,fontWeight:600,letterSpacing:'2px',textTransform:'uppercase',color:'var(--text-faint)',
        }}>
          <span style={{textAlign:'right'}}># · time</span>
          <span />
          <span>event</span>
          <span>narrative</span>
          <span style={{textAlign:'right'}}>detail</span>
        </div>

        {showRawPane && (
          <div style={{
            gridColumn:'2 / 3',gridRow:'1 / 2',
            background:'var(--surface)',
            padding:'5px 14px',borderBottom:'1px solid var(--line)',
            fontFamily:'var(--font-sans)',fontSize:8,fontWeight:600,letterSpacing:'2px',textTransform:'uppercase',color:'var(--text-faint)',
            display:'flex',alignItems:'center',gap:8,
          }}>
            <span>wire · ndjson</span>
            <PulseDot kind="working" size={4} />
            <span style={{flex:1}} />
            <span style={{fontFamily:'var(--font-mono)',fontSize:9,letterSpacing:0,textTransform:'none',color:'var(--text-dim)',opacity:.7}}>{events.length} lines</span>
          </div>
        )}

        {/* Rendered log */}
        <div style={{gridColumn: showRawPane ? '1 / 2' : '1 / -1', gridRow:'2 / 3', background:'var(--bg)',overflow:'auto',minHeight:0}}>
          {visible.map(({ raw, parsed: parsedEv, idx }) => (
            <LogRow
              key={idx}
              line={idx}
              ev={parsedEv}
              parsed={interpret(parsedEv)}
              expanded={expanded.has(idx)}
              onToggle={()=>toggle(idx)}
              density={state.density}
            />
          ))}
          {visible.length === 0 && (
            <div style={{padding:'40px 28px',fontFamily:'var(--font-serif)',fontStyle:'italic',color:'var(--text-faint)'}}>
              No events match this filter.
            </div>
          )}
        </div>

        {/* Raw NDJSON pane */}
        {showRawPane && (
          <div style={{gridColumn:'2 / 3',gridRow:'2 / 3',background:'var(--surface-2)',overflow:'auto',minHeight:0,
                       fontFamily:'var(--font-mono)',fontSize:10,lineHeight:1.65,color:'var(--text)',padding:'6px 0'}}>
            {events.map(({ raw }, i) => {
              const isHi = expanded.has(i+1);
              return (
                <div key={i} style={{
                  display:'grid',gridTemplateColumns:'34px 1fr',
                  padding:'1px 14px 1px 0',
                  background: isHi ? 'rgba(127,181,216,.05)' : 'transparent',
                  borderLeft: isHi ? '2px solid var(--clavis)' : '2px solid transparent',
                }}>
                  <span style={{color:'var(--text-xfaint)',textAlign:'right',paddingRight:8,userSelect:'none'}}>{String(i+1).padStart(3,'0')}</span>
                  <span style={{color:'var(--text-dim)',whiteSpace:'pre',overflow:'hidden',textOverflow:'ellipsis'}}>{raw}</span>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Bottom strip — totals */}
      <div style={{
        display:'flex',alignItems:'center',gap:18,
        padding:'5px 22px',background:'var(--surface)',borderTop:'1px solid var(--faint)',
        fontFamily:'var(--font-sans)',fontSize:8.5,letterSpacing:'.8px',color:'var(--text-faint)',flexShrink:0,
      }}>
        <span style={{display:'inline-flex',alignItems:'center',gap:6}}><PulseDot kind={state.status} size={4} /> tail</span>
        <span>events {events.length}</span>
        <span>tools 6</span>
        <span>turns 1</span>
        <span style={{flex:1}} />
        <span>↑↓ navigate</span>
        <span>↵ expand</span>
        <span>/ filter</span>
      </div>
      <ResizeHandle />
    </div>
  );
}

Object.assign(window, { VariationLog });
