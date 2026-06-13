// CLAVIS shared primitives + content. Loaded as Babel JSX.
// Exposes components on window so other Babel scripts can use them.

const { useState, useEffect, useRef, useCallback, useMemo } = React;

// ── Icons ────────────────────────────────────────────────────
function IconClock() {
  return (
    <svg viewBox="0 0 10 10" fill="none" stroke="currentColor" strokeWidth="1" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="5" cy="5" r="4"/><path d="M5 3 L5 5 L6.5 6"/>
    </svg>
  );
}
function IconTokens() {
  return (
    <svg viewBox="0 0 10 10" fill="none" stroke="currentColor" strokeWidth="1">
      <circle cx="3.5" cy="5" r="2"/><circle cx="6.5" cy="5" r="2"/>
    </svg>
  );
}
function IconLines() {
  return (
    <svg viewBox="0 0 10 10" fill="none" stroke="currentColor" strokeWidth="1" strokeLinecap="round">
      <line x1="2" y1="3" x2="8" y2="3"/><line x1="2" y1="5" x2="8" y2="5"/><line x1="2" y1="7" x2="6" y2="7"/>
    </svg>
  );
}

// ── Title bar ────────────────────────────────────────────────
function TitleBar({ name, meta, status = 'working', onClose }) {
  return (
    <div className="cv-titlebar">
      <span className="cv-grip">
        {[0,1,2].map(i => (
          <span key={i} className="cv-grip-row">
            <span className="cv-grip-dot"/><span className="cv-grip-dot"/>
          </span>
        ))}
      </span>
      <span className="cv-name">{name}</span>
      {meta && <span className="cv-meta">{meta}</span>}
      <span className={`cv-dot ${status}`}/>
      <span style={{flex:1}}/>
      <span className="cv-close" onClick={onClose}><span className="cv-x"/></span>
    </div>
  );
}

// ── Tab bar ───────────────────────────────────────────────────
function TabBar({ tabs, activeId, onSelect }) {
  return (
    <div className="cv-tabs">
      {tabs.map(t => {
        const active = t.id === activeId;
        return (
          <div key={t.id} className={`cv-tab ${active ? 'active' : ''}`} onClick={() => onSelect && onSelect(t.id)}>
            <span className={`cv-tab-status ${t.status || 'idle'}`}/>
            <span>{t.name}</span>
            {active && t.status === 'working' && <div className="cv-tab-scan"/>}
          </div>
        );
      })}
    </div>
  );
}

// ── Workspace bar / rail ─────────────────────────────────────
const WORKSPACES = [
  { fk: 'F1', name: 'Corvus',        color: 'var(--clavis)', status: 'working' },
  { fk: 'F2', name: 'Auth Refactor', color: 'var(--human)',  status: 'waiting' },
  { fk: 'F3', name: 'UI Widgets',    color: 'var(--info)',   status: 'idle' },
  { fk: 'F4', name: 'Deploy',        color: 'var(--gold)',   status: 'idle' },
  { fk: 'F5', name: 'Notes',         color: 'var(--text-dim)', status: 'idle' },
];
function WorkspaceBar({ activeIdx, onSelect, vertical = false }) {
  const cls = vertical ? 'cv-wsrail' : 'cv-wsbar';
  return (
    <div className={cls}>
      {WORKSPACES.map((w, i) => {
        const active = i === activeIdx;
        return (
          <div key={i} className={`cv-wsi ${active ? 'active' : ''} ${w.status}`} onClick={() => onSelect && onSelect(i)}>
            <span className="cv-wsi-fk">{w.fk}</span>
            <span className={`cv-wsi-st ${w.status}`}/>
            {!vertical && <span>{w.name}</span>}
            <div className="cv-wsi-bar" style={{ background: w.color }}/>
            <div className="cv-wsi-scan"/>
          </div>
        );
      })}
    </div>
  );
}

// ── Message block ────────────────────────────────────────────
function MessageBlock({ role, keyword, summary, stats, body, gutterPx = 120, density = 'comfy' }) {
  const isHuman = role === 'human';
  const padY = density === 'compact' ? '7px' : density === 'comfy' ? '11px' : '14px';
  const padX = density === 'compact' ? '20px' : '28px';
  return (
    <div className="cv-msg" style={{ gridTemplateColumns: `${gutterPx}px 1fr`, padding: `${padY} ${padX}` }}>
      <div className="cv-gutter">
        <div className={`cv-gut-keyword ${isHuman ? 'h' : ''}`}>{keyword}</div>
        {summary && <div className="cv-gut-summary">{summary}</div>}
        {stats && stats.length > 0 && (
          <div className="cv-gut-stats">
            {stats.map((s, i) => (
              <span key={i} className="cv-gut-stat">
                {s.icon === 'clock' ? <IconClock/> : s.icon === 'lines' ? <IconLines/> : <IconTokens/>}
                {s.value}
              </span>
            ))}
          </div>
        )}
      </div>
      <div className={isHuman ? 'cv-body-h' : 'cv-body-a'}>{body}</div>
    </div>
  );
}

// ── Tool block ───────────────────────────────────────────────
function ToolBlock({ name, target, duration, padX = 28 }) {
  return (
    <div className="cv-tool" style={{ paddingLeft: padX, paddingRight: padX }}>
      <span className="cv-tool-ok">✓</span>
      <span className="cv-tool-name">{name}</span>
      <span className="cv-tool-target">{target}</span>
      {duration && <span className="cv-tool-dur">{duration}</span>}
    </div>
  );
}

// ── Status bar ───────────────────────────────────────────────
function StatusBar({ items = [], rightItems = [], working = false }) {
  return (
    <div className="cv-statusbar">
      {working && <span className="cv-st-dot"/>}
      {items.map((s, i) => <span key={i}>{s}</span>)}
      <span className="sp"/>
      {rightItems.map((s, i) => <span key={`r${i}`}>{s}</span>)}
    </div>
  );
}

// ── Keyboard hints ───────────────────────────────────────────
function Key({ children }) { return <span className="k">{children}</span>; }
function KeyHints({ items }) {
  return (
    <div className="cv-keyhints">
      {items.map((it, i) => (
        <span key={i}>
          {it.keys.map((k, j) => <React.Fragment key={j}><Key>{k}</Key>{j < it.keys.length-1 ? ' ' : ''}</React.Fragment>)}{' '}{it.label}
        </span>
      ))}
    </div>
  );
}

// ── Input bar ────────────────────────────────────────────────
function InputBar({ value, onChange, onSubmit, placeholder = 'Type a message…', autoFocus = false }) {
  const inputRef = useRef(null);
  return (
    <div className="cv-inputbar">
      <span className="cv-input-mark">›</span>
      <input
        ref={inputRef}
        className="cv-input-text"
        value={value}
        autoFocus={autoFocus}
        placeholder={placeholder}
        onChange={(e) => onChange && onChange(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && !e.shiftKey && onSubmit) {
            e.preventDefault();
            onSubmit();
          }
        }}
      />
      <span className="cv-caret"/>
    </div>
  );
}

// ── Inline widget (permission / question prompt) ─────────────
function InlineWidget({ kind = 'warn', label, question, options, selectedIdx, onSelect }) {
  return (
    <div className={`cv-iw ${kind}`}>
      <div className="cv-iw-head">
        <span style={{fontSize:'8px'}}>▲</span>{label}
      </div>
      <div className="cv-iw-q">{question}</div>
      <div className="cv-iw-opts">
        {options.map((o, i) => (
          <div key={i} className={`cv-iw-opt ${i === selectedIdx ? 'sel' : ''}`} onClick={() => onSelect && onSelect(i)}>
            <span className="cv-iw-radio"/>{o}
          </div>
        ))}
      </div>
    </div>
  );
}

// ── List item ────────────────────────────────────────────────
function ListItem({ label, selected, onClick, role = 'agent', meta, arrow, compact = false, icon }) {
  return (
    <div className={`cv-li ${selected ? 'sel' : ''} ${role === 'human' ? 'h' : ''} ${compact ? 'compact' : ''}`} onClick={onClick}>
      {icon && <span className="cv-li-icon">{icon}</span>}
      <span>{label}</span>
      {meta && <span className="cv-li-meta">{meta}</span>}
      {arrow && <span className="cv-li-arrow">›</span>}
    </div>
  );
}

// ── Realistic conversation content (Corvus / NdjsonParser) ──
const STREAM = [
  { t:'msg', role:'human', keyword:'Implement', summary:'Parser module with Result types',
    stats:[{ icon:'tokens', value:'42' }],
    body:<>Implement the <span className="cv-code">NdjsonParser</span> module in Corvus. Handle malformed lines with <span className="cv-code">Result</span> types and support async streaming.</> },
  { t:'msg', role:'agent', keyword:'Explore', summary:'Read existing types for alignment',
    stats:[{ icon:'clock', value:'3.1s' }, { icon:'tokens', value:'312' }],
    body:<>I'll create the parser module. Let me read the existing types to ensure the <span className="cv-code">StreamEvent</span> union aligns with the parser output, then write the parser, then build to verify.</> },
  { t:'tool', name:'Read', target:<><span className="cv-path">src/Corvus/Types.fs</span></>, duration:'0.4s' },
  { t:'tool', name:'Read', target:<><span className="cv-path">src/Corvus/SessionApi.fs</span></>, duration:'0.3s' },
  { t:'msg', role:'agent', keyword:'Build', summary:'Three functions, compiling',
    stats:[{ icon:'clock', value:'4.7s' }, { icon:'tokens', value:'584' }, { icon:'lines', value:'+48' }],
    body:<>The parser exposes three functions: <span className="cv-code">tryParseLine</span>, <span className="cv-code">parseStream</span> for <span className="cv-code">AsyncSeq&lt;string&gt;</span>, and <span className="cv-code">parseBatch</span>. Building to verify.</> },
  { t:'tool', name:'Write', target:<><span className="cv-path">src/Corvus/NdjsonParser.fs</span> — new, 48 lines</>, duration:'1.2s' },
  { t:'tool', name:'Bash', target:<>dotnet build src/Corvus</>, duration:'6.8s' },
  { t:'msg', role:'agent', keyword:'Reply', summary:'Build clean, ready for tests',
    stats:[{ icon:'clock', value:'2.4s' }, { icon:'tokens', value:'198' }],
    body:<>Build is clean — zero warnings. The malformed-line case returns <span className="cv-code">Error of ParseError</span> carrying the raw line and position. Want me to add property tests next, or wire this into <span className="cv-code">SessionApi.streamEvents</span>?</> },
];

// ── File tree content ────────────────────────────────────────
const FILE_TREE = [
  { id:'src', label:'src', kind:'folder' },
  { id:'corvus', label:'Corvus', kind:'folder', depth:1 },
  { id:'types', label:'Types.fs', kind:'file', depth:2, meta:'1.2 KB' },
  { id:'parser', label:'NdjsonParser.fs', kind:'file', depth:2, meta:'48 L', live:true },
  { id:'session', label:'SessionApi.fs', kind:'file', depth:2, meta:'412 B' },
  { id:'process', label:'ProcessManager.fs', kind:'file', depth:2, meta:'2.1 KB' },
  { id:'corvusfs', label:'Corvus.fs', kind:'file', depth:2, meta:'612 B' },
  { id:'ui', label:'Clavis.UI', kind:'folder', depth:1 },
  { id:'mainwin', label:'MainWindow.xaml', kind:'file', depth:2, meta:'4.4 KB' },
  { id:'tests', label:'tests', kind:'folder' },
];

// ── Code preview lines for editor variants ──────────────────
const CODE_LINES = [
  { n:1,  c:'module Corvus.NdjsonParser' },
  { n:2,  c:'' },
  { n:3,  c:'open System' },
  { n:4,  c:'open FSharp.Control' },
  { n:5,  c:'' },
  { n:6,  c:'type ParseError =' },
  { n:7,  c:'    { Line: string; Position: int; Message: string }' },
  { n:8,  c:'' },
  { n:9,  c:'let tryParseLine (raw: string) : Result<StreamEvent, ParseError> =', live:true },
  { n:10, c:'    if String.IsNullOrWhiteSpace raw then', live:true },
  { n:11, c:'        Error { Line = raw; Position = 0; Message = "empty line" }', live:true },
  { n:12, c:'    else', live:true },
  { n:13, c:'        try', live:true },
  { n:14, c:'            JsonSerializer.Deserialize<StreamEvent>(raw)' },
  { n:15, c:'            |> Ok' },
  { n:16, c:'        with ex ->' },
  { n:17, c:'            Error { Line = raw; Position = 0; Message = ex.Message }' },
  { n:18, c:'' },
  { n:19, c:'let parseStream (lines: AsyncSeq<string>) : AsyncSeq<Result<StreamEvent, ParseError>> =' },
  { n:20, c:'    lines |> AsyncSeq.map tryParseLine' },
];

// Expose globals
Object.assign(window, {
  TitleBar, TabBar, WorkspaceBar, WORKSPACES,
  MessageBlock, ToolBlock, StatusBar, Key, KeyHints, InputBar,
  InlineWidget, ListItem,
  IconClock, IconTokens, IconLines,
  STREAM, FILE_TREE, CODE_LINES,
});
