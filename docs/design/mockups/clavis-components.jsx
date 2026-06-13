// clavis-components.jsx — shared CLAVIS primitives used by every variation.
// Exports to window so other Babel scripts can read them.

const { useState, useEffect, useRef, useMemo } = React;

// ─────────────────────────────────────────────────────────────────────────────
// Atoms
// ─────────────────────────────────────────────────────────────────────────────

function Grip() {
  return (
    <span className="cl-grip" aria-hidden="true">
      {[0,1,2].map(r=>(
        <span key={r} className="cl-grip-row">
          <span className="cl-grip-dot" /><span className="cl-grip-dot" />
        </span>
      ))}
    </span>
  );
}

function PulseDot({ kind = 'working', size = 5 }) {
  const cls = {working:'',waiting:'warn',error:'error',idle:'idle',human:'human',ok:'ok'}[kind] || '';
  const sz = size === 4 ? 's4' : size === 6 ? 's6' : '';
  return <span className={`cl-dot ${cls} ${sz}`} />;
}

function Caret() { return <span className="cl-caret" />; }

function Beam({ vertical = false, color = 'var(--clavis)', height, top, bottom }) {
  const style = { background: `linear-gradient(${vertical?'180':'90'}deg,transparent,${color},transparent)` };
  if (height) style.height = height;
  if (top != null) style.top = top;
  if (bottom != null) style.bottom = bottom;
  return <span className={vertical ? 'cl-beam-v' : 'cl-beam'} style={style} />;
}

// SVG glyphs (stroke only, 1px, currentColor)
const Glyph = {
  clock: (p={}) => (
    <svg viewBox="0 0 10 10" width={p.size||10} height={p.size||10} fill="none"
         stroke="currentColor" strokeWidth="1" strokeLinecap="round" strokeLinejoin="round" style={{display:'block',opacity:p.opacity||.7,...p.style}}>
      <circle cx="5" cy="5" r="4" /><path d="M5 3 L5 5 L6.5 6" />
    </svg>
  ),
  tokens: (p={}) => (
    <svg viewBox="0 0 10 10" width={p.size||10} height={p.size||10} fill="none"
         stroke="currentColor" strokeWidth="1" style={{display:'block',opacity:p.opacity||.7,...p.style}}>
      <circle cx="3.5" cy="5" r="2" /><circle cx="6.5" cy="5" r="2" />
    </svg>
  ),
  chev: (p={}) => (
    <svg viewBox="0 0 10 10" width={p.size||10} height={p.size||10} fill="none"
         stroke="currentColor" strokeWidth="1" strokeLinecap="round" strokeLinejoin="round" style={{display:'block',...p.style}}>
      <path d={p.dir==='left'?'M6 2 L3 5 L6 8':p.dir==='down'?'M2 4 L5 7 L8 4':'M4 2 L7 5 L4 8'} />
    </svg>
  ),
  diamond: (p={}) => (
    <svg viewBox="0 0 10 10" width={p.size||10} height={p.size||10} fill="none"
         stroke="currentColor" strokeWidth="1" style={{display:'block',...p.style}}>
      <path d="M5 1 L9 5 L5 9 L1 5 Z" />
    </svg>
  ),
  check: (p={}) => (
    <svg viewBox="0 0 10 10" width={p.size||10} height={p.size||10} fill="none"
         stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" style={{display:'block',...p.style}}>
      <path d="M2 5.2 L4.2 7.4 L8 3.2" />
    </svg>
  ),
};

// ─────────────────────────────────────────────────────────────────────────────
// Window chrome
// ─────────────────────────────────────────────────────────────────────────────

function TitleBar({ name, meta, status = 'working', onClose, scan = false, children }) {
  return (
    <div style={{
      position:'relative',
      display:'flex',alignItems:'center',height:28,padding:'0 10px 0 14px',
      background:'var(--surface)',borderBottom:'1px solid var(--line)',
      color:'var(--text-dim)',cursor:'grab',userSelect:'none',
      flexShrink:0,
    }}>
      <Grip />
      <span style={{marginLeft:12,fontSize:9,fontWeight:500,letterSpacing:'2.5px',textTransform:'uppercase'}}>{name}</span>
      {meta && <span style={{marginLeft:10,fontSize:9,fontWeight:400,opacity:.5,letterSpacing:'.3px'}}>· {meta}</span>}
      <span style={{marginLeft:10}}><PulseDot kind={status} /></span>
      <span style={{flex:1}} />
      {children}
      <button onClick={onClose} style={{display:'flex',alignItems:'center',justifyContent:'center',width:22,height:22,opacity:.3,color:'inherit'}}
              onMouseEnter={e=>e.currentTarget.style.opacity=.8}
              onMouseLeave={e=>e.currentTarget.style.opacity=.3}>
        <span className="cl-x" />
      </button>
      {scan && <span style={{position:'absolute',left:0,bottom:-1,right:0,height:1.5,overflow:'hidden'}}><Beam /></span>}
    </div>
  );
}

function ResizeHandle() {
  return (
    <div style={{position:'absolute',bottom:0,right:0,width:14,height:14,opacity:.2,color:'var(--text-dim)',pointerEvents:'none'}}>
      <span style={{position:'absolute',bottom:2,right:2,width:1,height:8,background:'currentColor',transform:'rotate(-45deg)',transformOrigin:'bottom right'}} />
      <span style={{position:'absolute',bottom:2,right:5,width:1,height:5,background:'currentColor',transform:'rotate(-45deg)',transformOrigin:'bottom right'}} />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Tab bar
// ─────────────────────────────────────────────────────────────────────────────

function TabBar({ tabs, active, onSelect, scanOnActive = true }) {
  return (
    <div style={{
      display:'flex',alignItems:'stretch',height:28,
      background:'var(--surface)',borderBottom:'1px solid var(--line)',
      flexShrink:0,
    }}>
      {tabs.map(t => {
        const isActive = t.id === active;
        return (
          <div key={t.id} onClick={()=>onSelect && onSelect(t.id)}
               style={{
                 position:'relative',display:'flex',alignItems:'center',gap:6,
                 padding:'0 14px',fontSize:11,
                 color: isActive ? 'var(--text-bright)' : 'var(--text-dim)',
                 background: isActive ? 'var(--bg)' : 'transparent',
                 borderRight:'1px solid var(--xfaint)',
                 cursor:'pointer',
                 marginBottom: isActive ? -1 : 0,
                 borderBottom: isActive ? '1.5px solid var(--clavis)' : 'none',
               }}>
            <PulseDot kind={t.status||'idle'} />
            <span>{t.name}</span>
            {t.meta && <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--text-faint)',marginLeft:2}}>{t.meta}</span>}
            {isActive && t.status === 'working' && scanOnActive && (
              <span style={{position:'absolute',left:0,bottom:0,right:0,height:1.5,overflow:'hidden'}}><Beam /></span>
            )}
          </div>
        );
      })}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Message block — the heart of CLAVIS
// ─────────────────────────────────────────────────────────────────────────────

function MessageRow({ role, keyword, summary, stats, children, gutterWidth = 120, density = 'comfortable', typeMode = 'sans', padX = 28 }) {
  const rolColor = role === 'human' ? 'var(--human)' : 'var(--clavis)';
  const padY = density === 'compact' ? 8 : density === 'dense' ? 6 : 11;
  const keywordEl = typeMode === 'serif'
    ? <span style={{fontFamily:'var(--font-serif)',fontStyle:'italic',fontSize:14,fontWeight:400,color:rolColor,opacity:.95,lineHeight:1.1,textTransform:'lowercase',letterSpacing:0}}>{keyword}</span>
    : <span style={{fontFamily:'var(--font-sans)',fontSize:9,fontWeight:600,letterSpacing:'2px',textTransform:'uppercase',color:rolColor,opacity:.85,lineHeight:1.2}}>{keyword}</span>;
  return (
    <div style={{
      display:'grid',gridTemplateColumns:`${gutterWidth}px 1fr`,columnGap:22,
      padding:`${padY}px ${padX}px`,
      borderBottom:'1px solid var(--faint)',
      alignItems:'start',
    }}>
      <div style={{textAlign:'right',paddingTop:2}}>
        {keywordEl}
        {summary && <div style={{fontFamily:'var(--font-sans)',fontSize:10,fontWeight:300,color:'var(--text-dim)',lineHeight:1.4,marginTop:4}}>{summary}</div>}
        {stats && stats.length > 0 && (
          <div style={{display:'flex',gap:10,justifyContent:'flex-end',alignItems:'center',
                       fontFamily:'var(--font-mono)',fontSize:9,fontWeight:400,
                       color:'var(--text-dim)',opacity:.6,marginTop:6}}>
            {stats.map((s,i)=>(
              <span key={i} style={{display:'inline-flex',alignItems:'center',gap:4}}>
                {s.glyph === 'clock' ? Glyph.clock() : s.glyph === 'tokens' ? Glyph.tokens() : null}
                {s.value}
              </span>
            ))}
          </div>
        )}
      </div>
      <div>{children}</div>
    </div>
  );
}

function HumanBody({ children }) {
  return <div style={{fontFamily:'var(--font-serif)',fontSize:13,fontWeight:400,fontStyle:'italic',color:'var(--text-bright)',lineHeight:1.55}}>{children}</div>;
}
function AgentBody({ children }) {
  return <div style={{fontFamily:'var(--font-sans)',fontSize:13,fontWeight:300,color:'var(--text)',lineHeight:1.75,letterSpacing:'.1px'}}>{children}</div>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tool block
// ─────────────────────────────────────────────────────────────────────────────

function ToolRow({ name, target, duration, scan = false, padX = 28, status = 'ok' }) {
  const checkColor = status === 'ok' ? 'var(--clavis)' : status === 'error' ? 'var(--error)' : 'var(--warn)';
  return (
    <div style={{position:'relative',display:'flex',alignItems:'center',gap:8,
                 padding:`4px ${padX}px`,
                 fontFamily:'var(--font-mono)',fontSize:10.5,fontWeight:400,color:'var(--text-dim)'}}>
      <span style={{color:checkColor,opacity:.6,fontSize:9}}>✓</span>
      <span style={{fontFamily:'var(--font-sans)',fontSize:9,fontWeight:500,letterSpacing:'1.5px',textTransform:'uppercase'}}>{name}</span>
      <span>{target}</span>
      {duration && <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--text-dim)',opacity:.5,marginLeft:'auto'}}>{duration}</span>}
      {scan && <span style={{position:'absolute',left:padX,right:padX,bottom:0,height:1,overflow:'hidden',opacity:.5}}><Beam /></span>}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Input bar — interactive
// ─────────────────────────────────────────────────────────────────────────────

function InputBar({ value, onChange, onSubmit, padX = 22, placeholder = '' }) {
  const ref = useRef(null);
  return (
    <div style={{display:'flex',alignItems:'center',gap:10,
                 padding:`10px ${padX}px`,borderTop:'1px solid var(--line)',
                 flexShrink:0,background:'var(--bg)'}}>
      <span style={{fontFamily:'var(--font-serif)',fontSize:20,color:'var(--human)',opacity:.35,lineHeight:1}}>›</span>
      <input
        ref={ref}
        value={value}
        onChange={e => onChange && onChange(e.target.value)}
        onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); onSubmit && onSubmit(); }}}
        placeholder={placeholder}
        style={{
          flex:1,background:'transparent',border:'none',outline:'none',
          fontFamily:'var(--font-sans)',fontSize:13,fontWeight:300,
          color:'var(--text-bright)',letterSpacing:'.1px',
          caretColor:'var(--human)',
        }}
      />
      {!value && <Caret />}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Status & keyboard hint bars
// ─────────────────────────────────────────────────────────────────────────────

function StatusBar({ items, padX = 22 }) {
  return (
    <div style={{display:'flex',alignItems:'center',gap:14,
                 padding:`4px ${padX}px`,background:'var(--surface)',
                 borderTop:'1px solid var(--faint)',
                 fontFamily:'var(--font-sans)',fontSize:8.5,color:'var(--text-faint)',
                 letterSpacing:'.8px',fontWeight:400,flexShrink:0}}>
      {items.map((it,i) => (
        it === '_spacer'
          ? <span key={i} style={{flex:1}} />
          : <span key={i} style={{display:'inline-flex',alignItems:'center',gap:6}}>{it.dot && <PulseDot kind={it.dot} size={4} />}{it.label || it}</span>
      ))}
    </div>
  );
}

function HintBar({ hints, padX = 22 }) {
  return (
    <div style={{display:'flex',gap:14,padding:`5px ${padX}px`,
                 borderTop:'1px solid var(--line)',background:'var(--bg)',
                 fontFamily:'var(--font-sans)',fontSize:8.5,color:'var(--text-faint)',
                 letterSpacing:'.8px',fontWeight:400,flexShrink:0}}>
      {hints.map((h,i) => (
        <span key={i}>
          {h.keys.map((k,j) => (
            <span key={j} style={{display:'inline-block',border:'1px solid var(--faint)',padding:'0 4px',fontFamily:'var(--font-mono)',fontSize:7.5,color:'var(--text-dim)',margin:'0 1px'}}>{k}</span>
          ))}
          {' '}{h.label}
        </span>
      ))}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Workspace bar — horizontal or vertical spine
// ─────────────────────────────────────────────────────────────────────────────

function WorkspaceBar({ workspaces, active, onSelect, vertical = false }) {
  if (vertical) {
    return (
      <div style={{
        width:48,display:'flex',flexDirection:'column',
        background:'var(--surface)',borderRight:'1px solid var(--line)',
        flexShrink:0,
      }}>
        {workspaces.map(w => {
          const isActive = w.id === active;
          return (
            <div key={w.id} onClick={()=>onSelect && onSelect(w.id)}
                 style={{position:'relative',display:'flex',flexDirection:'column',
                         alignItems:'center',justifyContent:'center',gap:3,
                         padding:'10px 0',cursor:'pointer',
                         color: isActive ? 'var(--text-bright)' : 'var(--text-faint)',
                         borderBottom:'1px solid var(--xfaint)'}}>
              <span style={{fontFamily:'var(--font-sans)',fontSize:7,letterSpacing:'.5px',color:'var(--text-faint)',opacity: isActive ? .6 : .4}}>{w.fkey}</span>
              <PulseDot kind={w.status||'idle'} size={4} />
              <span style={{fontFamily:'var(--font-sans)',fontSize:8.5,fontWeight:500,letterSpacing:'1.2px',textTransform:'uppercase',writingMode:'vertical-rl',transform:'rotate(180deg)',marginTop:4}}>{w.name}</span>
              {isActive && <span style={{position:'absolute',left:0,top:0,bottom:0,width:2,background:w.color}} />}
              {isActive && w.status === 'working' && <span style={{position:'absolute',right:0,top:0,bottom:0,width:1.5,overflow:'hidden'}}><Beam vertical /></span>}
            </div>
          );
        })}
      </div>
    );
  }
  return (
    <div style={{display:'flex',height:28,background:'var(--surface)',borderBottom:'1px solid var(--line)',flexShrink:0}}>
      {workspaces.map(w => {
        const isActive = w.id === active;
        return (
          <div key={w.id} onClick={()=>onSelect && onSelect(w.id)}
               style={{position:'relative',display:'flex',alignItems:'center',gap:6,
                       padding:'0 14px',cursor:'pointer',fontSize:10,
                       color: isActive ? 'var(--text-bright)' : 'var(--text-faint)',
                       borderLeft:'1px solid var(--xfaint)'}}>
            <span style={{fontFamily:'var(--font-sans)',fontSize:7,letterSpacing:'.5px',color:'var(--text-faint)',opacity: isActive ? .6 : .4}}>{w.fkey}</span>
            <PulseDot kind={w.status||'idle'} size={4} />
            <span>{w.name}</span>
            {isActive && <span style={{position:'absolute',bottom:0,left:0,right:0,height:2,background:w.color}} />}
            {isActive && w.status === 'working' && <span style={{position:'absolute',bottom:2,left:0,right:0,height:1,overflow:'hidden'}}><Beam /></span>}
          </div>
        );
      })}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Inline widget — permission/question
// ─────────────────────────────────────────────────────────────────────────────

function InlineWidget({ kind = 'warn', icon = '▲', label, question, options = [], selected = 0, onSelect, padX = 22 }) {
  const borderColor = kind === 'warn' ? 'rgba(224,160,48,.18)' : kind === 'error' ? 'rgba(224,72,72,.18)' : 'var(--faint)';
  const headColor = kind === 'warn' ? 'var(--warn)' : kind === 'error' ? 'var(--error)' : 'var(--text-dim)';
  return (
    <div style={{margin:`8px ${padX}px`,border:`1px solid ${borderColor}`,background:'var(--surface)'}}>
      <div style={{display:'flex',alignItems:'center',gap:6,padding:'5px 10px',borderBottom:`1px solid var(--faint)`,
                   fontFamily:'var(--font-sans)',fontSize:8.5,fontWeight:500,letterSpacing:'1.2px',
                   textTransform:'uppercase',color:headColor,opacity:.7}}>
        <span style={{fontSize:8}}>{icon}</span>{label}
      </div>
      {question && (
        <div style={{padding:'7px 10px',fontSize:11,color:'var(--text-bright)',borderBottom:'1px solid var(--faint)',fontFamily:'var(--font-sans)',fontWeight:300}}>{question}</div>
      )}
      <div style={{padding:'4px 10px'}}>
        {options.map((o,i) => (
          <div key={i} onClick={()=>onSelect && onSelect(i)}
               style={{display:'flex',alignItems:'center',gap:8,padding:'4px 0',
                       fontSize:10.5,fontFamily:'var(--font-sans)',fontWeight:300,
                       color: i===selected ? 'var(--text-bright)' : 'var(--text-dim)',cursor:'pointer'}}>
            <span style={{position:'relative',width:8,height:8,border:`1px solid ${i===selected?'var(--clavis)':'var(--text-dim)'}`,flexShrink:0}}>
              {i===selected && <span style={{position:'absolute',top:1.5,left:1.5,width:3,height:3,background:'var(--clavis)'}} />}
            </span>
            {o}
          </div>
        ))}
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Breadcrumb
// ─────────────────────────────────────────────────────────────────────────────

function Breadcrumb({ parts, padX = 22 }) {
  return (
    <div style={{display:'flex',alignItems:'center',padding:`6px ${padX}px`,
                 borderBottom:'1px solid var(--faint)',
                 fontFamily:'var(--font-sans)',fontSize:10,flexShrink:0}}>
      {parts.map((p,i) => (
        <React.Fragment key={i}>
          <span style={{padding:'0 6px',color: i===parts.length-1 ? 'var(--text-bright)' : 'var(--text-faint)',fontWeight: i===parts.length-1 ? 500 : 300}}>{p}</span>
          {i < parts.length-1 && <span style={{color:'var(--text-xfaint)',fontFamily:'var(--font-mono)',fontSize:9}}>›</span>}
        </React.Fragment>
      ))}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// File tree (compact list) — used in tiled variation
// ─────────────────────────────────────────────────────────────────────────────

function FileTree({ items, selected, onSelect }) {
  return (
    <div style={{padding:'6px 0'}}>
      {items.map(it => {
        const isSel = it.id === selected;
        return (
          <div key={it.id} onClick={()=>onSelect && onSelect(it.id)}
               style={{display:'flex',alignItems:'center',gap:6,padding:'4px 16px',cursor:'pointer',
                       borderLeft: `1.5px solid ${isSel ? 'var(--clavis)' : 'transparent'}`,
                       background: isSel ? 'rgba(127,181,216,.025)' : 'transparent',
                       fontFamily:'var(--font-sans)',fontSize:11.5,fontWeight:300,
                       color: isSel ? 'var(--text-bright)' : 'var(--text-dim)',
                       paddingLeft: 14 + (it.depth||0)*12}}>
            <span style={{opacity:.6,fontFamily:'var(--font-mono)',fontSize:10,width:8,display:'inline-block',color: it.type==='folder' ? 'var(--text-faint)' : 'var(--text-xfaint)'}}>
              {it.type === 'folder' ? '›' : '◇'}
            </span>
            <span>{it.name}</span>
            {it.live && (
              <span style={{marginLeft:'auto',fontFamily:'var(--font-display)',fontSize:7,letterSpacing:'1px',color:'var(--clavis)',opacity:.8}}>LIVE</span>
            )}
            {it.dirty && !it.live && (
              <span style={{marginLeft:'auto',width:4,height:4,background:'var(--warn)',display:'inline-block'}} />
            )}
          </div>
        );
      })}
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Code editor (mini, for live-edit demo)
// ─────────────────────────────────────────────────────────────────────────────

const FSHARP_SAMPLE = [
  { n:1,  text: 'module Corvus.NdjsonParser', kind:'kw' },
  { n:2,  text: '' },
  { n:3,  text: 'open System', kind:'kw' },
  { n:4,  text: 'open System.IO', kind:'kw' },
  { n:5,  text: 'open Corvus.Types', kind:'kw' },
  { n:6,  text: '' },
  { n:7,  text: 'type ParseError =', kind:'kw' },
  { n:8,  text: '    | Malformed of line: string * reason: string' },
  { n:9,  text: '    | UnknownEvent of tag: string' },
  { n:10, text: '' },
  { n:11, text: 'let tryParseLine (line: string) : Result<StreamEvent, ParseError> =', kind:'kw', live:true },
  { n:12, text: '    if String.IsNullOrWhiteSpace line then', live:true },
  { n:13, text: '        Error (Malformed (line, "empty"))', live:true },
  { n:14, text: '    else', live:true },
  { n:15, text: '        try' },
  { n:16, text: '            let json = JsonDocument.Parse line' },
  { n:17, text: '            StreamEvent.OfJson json.RootElement |> Ok' },
  { n:18, text: '        with ex -> Error (Malformed (line, ex.Message))' },
  { n:19, text: '' },
  { n:20, text: 'let parseStream (stream: IAsyncEnumerable<string>) =', kind:'kw' },
  { n:21, text: '    stream' },
  { n:22, text: '    |> AsyncSeq.map tryParseLine' },
];

function CodeEditor({ liveLines = true }) {
  return (
    <div style={{flex:1,minHeight:0,overflow:'auto',background:'var(--bg)',
                 fontFamily:'var(--font-mono)',fontSize:11,lineHeight:1.7,
                 color:'var(--text)',padding:'8px 0'}}>
      {FSHARP_SAMPLE.map(ln => {
        const isLive = liveLines && ln.live;
        return (
          <div key={ln.n} style={{
            display:'flex',padding:'0 16px 0 0',position:'relative',
            background: isLive ? 'rgba(127,181,216,.025)' : 'transparent',
            borderRight: isLive ? '2px dotted var(--clavis)' : 'none',
          }}>
            <span style={{width:38,paddingRight:12,textAlign:'right',color:'var(--text-xfaint)',userSelect:'none',flexShrink:0}}>{ln.n}</span>
            <span style={{
              color: ln.kind==='kw' ? 'var(--text-bright)' : 'var(--text)',
              fontWeight: ln.kind==='kw' ? 400 : 300,
              whiteSpace:'pre',
            }}>{ln.text || '\u00A0'}</span>
            {isLive && ln.n === 11 && (
              <span style={{position:'absolute',right:4,top:0,fontFamily:'var(--font-display)',fontSize:7,letterSpacing:'1px',color:'var(--clavis)',opacity:.7}}>LIVE</span>
            )}
          </div>
        );
      })}
    </div>
  );
}

// Export everything
Object.assign(window, {
  Grip, PulseDot, Caret, Beam, Glyph,
  TitleBar, ResizeHandle, TabBar,
  MessageRow, HumanBody, AgentBody, ToolRow,
  InputBar, StatusBar, HintBar, WorkspaceBar,
  InlineWidget, Breadcrumb, FileTree, CodeEditor,
});
