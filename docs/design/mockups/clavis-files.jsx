// clavis-files.jsx — Two-column (dual-pane) file manager in CLAVIS register.
// Files = messages. The pane is a typeset document, not a folder window.

const { useState: useStateF } = React;

// ─────────────────────────────────────────────────────────────────────────────
// DATA — two locations, with rows that have a "kind", "size", optional "note"
// ─────────────────────────────────────────────────────────────────────────────

const LEFT = {
  host: 'NORDIC-RUNE',
  mount: 'local',
  path: ['~', 'work', 'corvus', 'src', 'NdjsonParser'],
  free: '218.4 GB free',
  total: '402 items',
  rows: [
    { id:'up',       kind:'up',     name:'..',                                                                       },
    { id:'r1',  k:'folder', name:'archive',              size:'14 items', mod:'Mar · 02', note:'older protocol drafts' },
    { id:'r2',  k:'folder', name:'fixtures',             size:'62 items', mod:'Wed · 10:11',                          },
    { id:'r3',  k:'folder', name:'tests',                size:'18 items', mod:'Today · 09:42', note:'8 failing locally', warn:true },
    { id:'r4',  k:'fs',     name:'NdjsonParser.fs',      size:'6.3 KB',   mod:'Today · 11:14', note:'editing now', live:true, sel:true },
    { id:'r5',  k:'fs',     name:'StreamEvent.fs',       size:'2.1 KB',   mod:'Today · 09:51', sel:true },
    { id:'r6',  k:'fs',     name:'ParseError.fs',        size:'612 B',    mod:'Today · 09:50', sel:true },
    { id:'r7',  k:'fs',     name:'Telemetry.fs',         size:'4.8 KB',   mod:'Tue · 16:02',                          },
    { id:'r8',  k:'fsproj', name:'NdjsonParser.fsproj',  size:'1.2 KB',   mod:'Mon · 14:30', note:'targets net8.0'    },
    { id:'r9',  k:'json',   name:'paket.dependencies',   size:'418 B',    mod:'Mon · 14:30',                          },
    { id:'r10', k:'md',     name:'README.md',            size:'3.0 KB',   mod:'Sun · 22:18', note:'\u2018On NDJSON\u2019 \u2014 pinned' },
    { id:'r11', k:'log',    name:'.corvus.lock',         size:'94 B',     mod:'Today · 11:09', dim:true              },
  ],
};

const RIGHT = {
  host: 'corvus.archive',
  mount: 'remote · sftp',
  path: ['/', 'srv', 'corvus', 'releases', '2026.04'],
  free: '1.81 TB free',
  total: '1,204 items',
  rows: [
    { id:'up',       kind:'up', name:'..',                                                                          },
    { id:'q1',  k:'folder', name:'2026.03',              size:'318 items', mod:'Apr · 01', note:'last release'        },
    { id:'q2',  k:'folder', name:'2026.04',              size:'  4 items', mod:'Today · 11:02', note:'staging', live:true },
    { id:'q3',  k:'folder', name:'_build',               size:' 92 items', mod:'Today · 10:55',                       },
    { id:'q4',  k:'sig',    name:'manifest.sig',         size:'  256 B',   mod:'Today · 10:55', note:'ed25519',        },
    { id:'q5',  k:'json',   name:'manifest.json',        size:' 8.4 KB',   mod:'Today · 10:55',                       },
    { id:'q6',  k:'tar',    name:'corvus-2026.04.tar.zst',size:'42.1 MB',  mod:'Today · 10:54', note:'verified \u00b7 sha256 ok' },
    { id:'q7',  k:'tar',    name:'corvus-2026.04.sources.tar.zst', size:'18.6 MB', mod:'Today · 10:54' },
    { id:'q8',  k:'md',     name:'CHANGELOG.md',         size:' 7.2 KB',   mod:'Today · 10:53', note:'\u2018added: parseStream\u2019' },
    { id:'q9',  k:'log',    name:'.publish.lock',        size:'   94 B',   mod:'Today · 11:01', dim:true             },
  ],
};

const KIND_LABEL = {
  folder:'FOLDER', fs:'F#', fsproj:'PROJ', json:'JSON', md:'PROSE',
  log:'LOCK', sig:'SIG', tar:'ARCHIVE', img:'IMG', up:'',
};

// ─────────────────────────────────────────────────────────────────────────────
// PANE — a typeset directory listing
// ─────────────────────────────────────────────────────────────────────────────

function PaneHeader({ host, mount, path, isActive, scan }) {
  return (
    <div style={{position:'relative',background:'var(--surface)',borderBottom:'1px solid var(--line)',flexShrink:0}}>
      {/* host strip */}
      <div style={{display:'flex',alignItems:'center',gap:10,padding:'7px 14px 6px',
                   borderBottom:'1px solid var(--xfaint)'}}>
        <PulseDot kind={isActive ? 'working' : 'idle'} size={4} />
        <span style={{fontFamily:'var(--font-sans)',fontSize:9,fontWeight:600,letterSpacing:'2.5px',
                      textTransform:'uppercase',color: isActive ? 'var(--text-bright)' : 'var(--text-dim)'}}>
          {host}
        </span>
        <span style={{fontFamily:'var(--font-sans)',fontSize:8,fontWeight:400,letterSpacing:'1.5px',
                      textTransform:'uppercase',color:'var(--text-faint)'}}>
          · {mount}
        </span>
        <span style={{flex:1}} />
        {isActive && (
          <span style={{fontFamily:'var(--font-display)',fontSize:8,letterSpacing:'2px',
                        color:'var(--clavis)',opacity:.85}}>FOCUS</span>
        )}
      </div>
      {/* path */}
      <div style={{display:'flex',alignItems:'center',padding:'6px 14px',gap:0,flexWrap:'wrap'}}>
        {path.map((p,i)=>(
          <React.Fragment key={i}>
            <span style={{fontFamily:'var(--font-mono)',fontSize:11,
                          color: i===path.length-1
                            ? (isActive ? 'var(--text-bright)' : 'var(--text)')
                            : 'var(--text-faint)',
                          fontWeight: i===path.length-1 ? 500 : 300}}>
              {p}
            </span>
            {i < path.length-1 && (
              <span style={{padding:'0 6px',fontFamily:'var(--font-mono)',fontSize:10,color:'var(--text-xfaint)'}}>/</span>
            )}
          </React.Fragment>
        ))}
        {isActive && <span style={{marginLeft:8}}><Caret /></span>}
      </div>
      {scan && isActive && (
        <span style={{position:'absolute',left:0,right:0,bottom:-1,height:1.5,overflow:'hidden'}}><Beam /></span>
      )}
    </div>
  );
}

function PaneFooter({ free, total, sel }) {
  return (
    <div style={{display:'flex',alignItems:'center',gap:14,padding:'5px 14px',
                 background:'var(--surface)',borderTop:'1px solid var(--faint)',
                 fontFamily:'var(--font-sans)',fontSize:8.5,letterSpacing:'1px',
                 textTransform:'uppercase',color:'var(--text-faint)',fontWeight:400,flexShrink:0}}>
      <span style={{display:'inline-flex',alignItems:'center',gap:6}}>
        <PulseDot kind="ok" size={4} />{free}
      </span>
      <span style={{opacity:.5}}>· {total}</span>
      <span style={{flex:1}} />
      {sel && (
        <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--clavis)',textTransform:'none',letterSpacing:'.3px'}}>
          {sel}
        </span>
      )}
    </div>
  );
}

function FileRow({ row, density, gutterWidth, isCursor, pinned }) {
  if (row.kind === 'up') {
    return (
      <div style={{display:'grid',gridTemplateColumns:`${gutterWidth}px 1fr`,columnGap:18,
                   padding:`${density==='dense'?5:density==='compact'?7:9}px 16px`,
                   borderBottom:'1px solid var(--faint)',
                   color:'var(--text-faint)',cursor:'pointer'}}>
        <span style={{textAlign:'right',fontFamily:'var(--font-display)',fontSize:8,letterSpacing:'2px',
                      color:'var(--text-xfaint)'}}>UP</span>
        <span style={{fontFamily:'var(--font-mono)',fontSize:11,color:'var(--text-dim)'}}>
          ..&nbsp;<span style={{color:'var(--text-xfaint)'}}>parent</span>
        </span>
      </div>
    );
  }
  const padY = density==='dense'?5:density==='compact'?7:9;
  const kindColor = row.live ? 'var(--clavis)'
                  : row.warn ? 'var(--warn)'
                  : row.k === 'folder' ? 'var(--text-bright)'
                  : 'var(--text-faint)';
  const kindWeight = row.live ? 600 : row.k === 'folder' ? 500 : 400;
  const nameColor = row.dim ? 'var(--text-faint)'
                  : row.sel ? 'var(--text-bright)'
                  : isCursor ? 'var(--text-bright)'
                  : row.k === 'folder' ? 'var(--text)'
                  : 'var(--text)';
  return (
    <div style={{
      position:'relative',
      display:'grid',gridTemplateColumns:`${gutterWidth}px 1fr`,columnGap:18,
      padding:`${padY}px 16px`,
      borderBottom:'1px solid var(--faint)',
      borderLeft: isCursor ? '1.5px solid var(--clavis)'
                : row.sel  ? '1.5px solid var(--human)'
                : '1.5px solid transparent',
      background: isCursor ? 'rgba(127,181,216,.04)'
                : row.sel  ? 'rgba(94,212,196,.025)'
                : row.live ? 'rgba(127,181,216,.02)'
                : 'transparent',
      alignItems:'baseline',
    }}>
      {/* gutter: kind + size + note */}
      <div style={{textAlign:'right',paddingTop:1}}>
        <span style={{fontFamily:'var(--font-display)',fontSize:8,fontWeight:kindWeight,
                      letterSpacing:'2px',color:kindColor,opacity: row.dim?.4:.95}}>
          {KIND_LABEL[row.k] || ''}
        </span>
        {row.size && (
          <div style={{fontFamily:'var(--font-mono)',fontSize:9,fontWeight:400,
                       color:'var(--text-faint)',marginTop:3,letterSpacing:'.2px'}}>{row.size}</div>
        )}
      </div>
      {/* body: name + meta */}
      <div style={{minWidth:0}}>
        <div style={{display:'flex',alignItems:'baseline',gap:8,flexWrap:'wrap'}}>
          <span style={{fontFamily:'var(--font-mono)',fontSize:row.k==='folder'?12:11.5,
                        fontWeight: row.k==='folder' ? 500 : 400,
                        color: nameColor,
                        opacity: row.dim ? .55 : 1,
                        textDecoration: row.dim ? 'none' : 'none',
                        letterSpacing:'.1px'}}>
            {row.k==='folder' && <span style={{color:'var(--text-faint)',marginRight:4}}>›</span>}
            {row.name}
          </span>
          {row.live && (
            <span style={{fontFamily:'var(--font-display)',fontSize:7,letterSpacing:'1.5px',
                          color:'var(--clavis)',opacity:.9,padding:'1px 5px',
                          border:'1px solid rgba(127,181,216,.3)'}}>LIVE</span>
          )}
          {row.sel && !row.live && (
            <span style={{fontFamily:'var(--font-display)',fontSize:7,letterSpacing:'1.5px',
                          color:'var(--human)',opacity:.9}}>· SEL</span>
          )}
          {row.warn && (
            <span style={{width:5,height:5,background:'var(--warn)',display:'inline-block'}} />
          )}
        </div>
        {(row.note || row.mod) && (
          <div style={{display:'flex',alignItems:'baseline',gap:10,marginTop:3,flexWrap:'wrap'}}>
            {row.note && (
              <span style={{fontFamily:'var(--font-serif)',fontStyle:'italic',fontSize:11.5,
                            fontWeight:400,color: row.warn?'var(--warn)':'var(--text-dim)',opacity:.85}}>
                {row.note}
              </span>
            )}
            {row.mod && (
              <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--text-faint)',opacity:.7,
                            marginLeft:'auto'}}>
                {row.mod}
              </span>
            )}
          </div>
        )}
      </div>
      {/* row-level scanning beam for the live row in active pane */}
      {row.live && pinned && (
        <span style={{position:'absolute',left:0,right:0,bottom:0,height:1,overflow:'hidden',opacity:.5}}>
          <Beam />
        </span>
      )}
    </div>
  );
}

function Pane({ data, isActive, density, gutterWidth, beamOn, cursorId }) {
  const sel = data.rows.filter(r=>r.sel);
  const selSummary = sel.length
    ? `${sel.length} sel · ${sel.reduce((acc,r)=>acc + (parseFloat(r.size)||0),0).toFixed(1)} ${sel[0].size?.includes('KB')?'KB':''}`
    : null;
  return (
    <div style={{display:'flex',flexDirection:'column',flex:1,minWidth:0,
                 background: isActive ? 'var(--bg)' : 'rgba(0,0,0,.55)',
                 transition:'background .2s'}}>
      <PaneHeader host={data.host} mount={data.mount} path={data.path} isActive={isActive} scan={beamOn} />
      <div style={{flex:1,overflowY:'auto',minHeight:0}}>
        {data.rows.map(r => (
          <FileRow key={r.id} row={r} density={density} gutterWidth={gutterWidth}
                   isCursor={isActive && r.id === cursorId} pinned={isActive && beamOn} />
        ))}
      </div>
      <PaneFooter free={data.free} total={data.total} sel={selSummary} />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// CENTER SPINE — transfer log
// ─────────────────────────────────────────────────────────────────────────────

const TRANSFER_LOG = [
  { t:'11:14:02.118', dir:'›',   verb:'copy',     from:'NdjsonParser.fs',     to:'2026.04/_build/',    bytes:'6.3 KB',  state:'queued'  },
  { t:'11:14:02.119', dir:'›',   verb:'copy',     from:'StreamEvent.fs',      to:'2026.04/_build/',    bytes:'2.1 KB',  state:'queued'  },
  { t:'11:14:02.119', dir:'›',   verb:'copy',     from:'ParseError.fs',       to:'2026.04/_build/',    bytes:'  612 B', state:'queued'  },
  { t:'11:13:58.402', dir:'✓',   verb:'verify',   from:'manifest.sig',        to:'',                   bytes:'',        state:'ok',  hint:'ed25519 · valid' },
  { t:'11:13:51.118', dir:'✓',   verb:'pull',     from:'CHANGELOG.md',        to:'~/work/corvus/',     bytes:'7.2 KB',  state:'ok'      },
  { t:'11:13:44.001', dir:'!',   verb:'conflict', from:'paket.dependencies',  to:'2026.04/',           bytes:'418 B',   state:'warn',hint:'remote newer · resolved manually' },
  { t:'11:13:31.219', dir:'✓',   verb:'mkdir',    from:'2026.04/_build',      to:'',                   bytes:'',        state:'ok'      },
];

function TransferLogRow({ ev }) {
  const dirColor = ev.state==='ok' ? 'var(--ok)'
                 : ev.state==='warn' ? 'var(--warn)'
                 : ev.state==='error' ? 'var(--error)'
                 : 'var(--clavis)';
  return (
    <div style={{padding:'5px 14px',borderBottom:'1px solid var(--faint)',
                 fontFamily:'var(--font-mono)',fontSize:10,lineHeight:1.5}}>
      <div style={{display:'flex',alignItems:'baseline',gap:8}}>
        <span style={{color:'var(--text-xfaint)',fontSize:9}}>{ev.t}</span>
        <span style={{color:dirColor,fontSize:11,opacity:.9}}>{ev.dir}</span>
        <span style={{fontFamily:'var(--font-sans)',fontSize:8.5,fontWeight:600,
                      letterSpacing:'1.5px',textTransform:'uppercase',color:'var(--text-bright)'}}>
          {ev.verb}
        </span>
        {ev.bytes && (
          <span style={{marginLeft:'auto',color:'var(--text-faint)',fontSize:9}}>{ev.bytes}</span>
        )}
      </div>
      <div style={{display:'flex',alignItems:'baseline',gap:6,marginTop:2,paddingLeft:60,minWidth:0}}>
        <span style={{color:'var(--text)'}}>{ev.from}</span>
        {ev.to && <span style={{color:'var(--text-xfaint)'}}>→</span>}
        {ev.to && <span style={{color:'var(--text-dim)'}}>{ev.to}</span>}
      </div>
      {ev.hint && (
        <div style={{paddingLeft:60,marginTop:2,fontFamily:'var(--font-serif)',fontStyle:'italic',
                     fontSize:11,color: ev.state==='warn'?'var(--warn)':'var(--text-dim)',opacity:.85}}>
          {ev.hint}
        </div>
      )}
    </div>
  );
}

function TransferSpine({ active, queueCount }) {
  return (
    <div style={{width:300,flexShrink:0,display:'flex',flexDirection:'column',
                 background:'var(--surface)',borderLeft:'1px solid var(--line)',
                 borderRight:'1px solid var(--line)'}}>
      {/* header */}
      <div style={{position:'relative',padding:'7px 14px 6px',borderBottom:'1px solid var(--line)'}}>
        <div style={{display:'flex',alignItems:'center',gap:8}}>
          <PulseDot kind={active?'working':'idle'} size={4} />
          <span style={{fontFamily:'var(--font-sans)',fontSize:9,fontWeight:600,letterSpacing:'2.5px',
                        textTransform:'uppercase',color:'var(--text-bright)'}}>
            transfer
          </span>
          <span style={{fontFamily:'var(--font-sans)',fontSize:8,letterSpacing:'1.5px',
                        textTransform:'uppercase',color:'var(--text-faint)'}}>
            · NDJSON
          </span>
          <span style={{flex:1}} />
          <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--clavis)'}}>
            {queueCount} queued
          </span>
        </div>
        <div style={{marginTop:6,fontFamily:'var(--font-serif)',fontStyle:'italic',
                     fontSize:11.5,color:'var(--text-dim)',lineHeight:1.4}}>
          three files staged for copy. press <span className="cl-code">F5</span> to commit.
        </div>
        {active && (
          <span style={{position:'absolute',left:0,right:0,bottom:-1,height:1.5,overflow:'hidden'}}><Beam /></span>
        )}
      </div>

      {/* progress block — current/queued */}
      <div style={{padding:'10px 14px',borderBottom:'1px solid var(--faint)',background:'var(--bg)'}}>
        <div style={{display:'flex',alignItems:'baseline',gap:8,marginBottom:5}}>
          <span style={{fontFamily:'var(--font-display)',fontSize:8,letterSpacing:'2px',color:'var(--clavis)'}}>STAGED</span>
          <span style={{flex:1}} />
          <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--text-dim)'}}>9.0 KB total</span>
        </div>
        <div style={{position:'relative',height:3,background:'var(--faint)',marginBottom:8}}>
          <div style={{position:'absolute',left:0,top:0,bottom:0,width:'0%',background:'var(--clavis)'}} />
          <span style={{position:'absolute',left:0,top:0,bottom:0,width:'100%',overflow:'hidden',opacity:.6}}><Beam /></span>
        </div>
        <div style={{fontFamily:'var(--font-sans)',fontSize:9,letterSpacing:'1.5px',
                     textTransform:'uppercase',color:'var(--text-faint)'}}>
          dest: <span style={{color:'var(--text-dim)'}}>corvus.archive</span>
          <span style={{marginLeft:8,color:'var(--text-xfaint)'}}>·</span>
          <span style={{marginLeft:8}}>encrypt: <span style={{color:'var(--ok)'}}>on</span></span>
        </div>
      </div>

      {/* feed */}
      <div style={{flex:1,overflowY:'auto',minHeight:0}}>
        {TRANSFER_LOG.map((ev,i) => <TransferLogRow key={i} ev={ev} />)}
      </div>

      {/* tiny footer in spine */}
      <div style={{padding:'5px 14px',borderTop:'1px solid var(--line)',
                   fontFamily:'var(--font-sans)',fontSize:8,letterSpacing:'1.2px',
                   textTransform:'uppercase',color:'var(--text-xfaint)'}}>
        feed · stream-1 · 482 events today
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// COMMAND BAR (bottom) — Clavis input as a file-manager command line
// ─────────────────────────────────────────────────────────────────────────────

function CommandBar({ value, onChange }) {
  return (
    <div style={{display:'flex',alignItems:'center',gap:12,padding:'9px 16px',
                 borderTop:'1px solid var(--line)',background:'var(--bg)',flexShrink:0}}>
      <span style={{fontFamily:'var(--font-sans)',fontSize:8.5,fontWeight:600,letterSpacing:'2px',
                    textTransform:'uppercase',color:'var(--text-faint)'}}>›</span>
      <span style={{fontFamily:'var(--font-mono)',fontSize:11,color:'var(--clavis)'}}>copy</span>
      <span style={{fontFamily:'var(--font-mono)',fontSize:11,color:'var(--text)'}}>{value}</span>
      <Caret />
      <span style={{flex:1}} />
      <span style={{fontFamily:'var(--font-serif)',fontStyle:'italic',fontSize:11,color:'var(--text-dim)',opacity:.7}}>
        type to filter, or speak a verb — <span className="cl-code">copy</span>, <span className="cl-code">link</span>, <span className="cl-code">diff</span>, <span className="cl-code">archive</span>
      </span>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// ROOT
// ─────────────────────────────────────────────────────────────────────────────

function ClavisFiles({ tweaks }) {
  const t = tweaks || {};
  const density = t.density || 'comfortable';
  const gutterWidth = Number(t.gutterWidth || 110);
  const focus = t.focus || 'left';
  const beamOn = t.beam !== 'off';
  const queueCount = 3;

  const [cursorL, setCursorL] = useStateF('r4');
  const [cursorR, setCursorR] = useStateF('q3');

  return (
    <div className="clavis-root" style={{display:'flex',flexDirection:'column',height:'100%',width:'100%'}}>
      {/* Title bar */}
      <TitleBar name="files" meta="dual-pane · session 04" status="working" scan={beamOn && t.beam==='titlebar'} onClose={()=>{}}>
        <span style={{display:'flex',alignItems:'center',gap:14,marginRight:10,
                      fontFamily:'var(--font-sans)',fontSize:8.5,letterSpacing:'1.5px',
                      textTransform:'uppercase',color:'var(--text-faint)',fontWeight:400}}>
          <span>session · <span style={{color:'var(--text-dim)'}}>corvus</span></span>
          <span>vault · <span style={{color:'var(--ok)'}}>unlocked</span></span>
        </span>
      </TitleBar>

      {/* Main two-column body with center transfer spine */}
      <div style={{flex:1,display:'flex',minHeight:0}}>
        <Pane data={LEFT}  isActive={focus==='left'}  density={density} gutterWidth={gutterWidth}
              beamOn={beamOn && (t.beam==='pane' || t.beam==='tab')} cursorId={cursorL} />
        <TransferSpine active={beamOn && t.beam==='spine'} queueCount={queueCount} />
        <Pane data={RIGHT} isActive={focus==='right'} density={density} gutterWidth={gutterWidth}
              beamOn={beamOn && (t.beam==='pane' || t.beam==='tab')} cursorId={cursorR} />
      </div>

      {/* Command bar */}
      <CommandBar value=" *.fs → corvus.archive:2026.04/_build/" />

      {/* Hint bar */}
      <HintBar hints={[
        { keys:['Tab'],         label:'switch pane' },
        { keys:['Space'],       label:'select' },
        { keys:['F5'],          label:'copy' },
        { keys:['F6'],          label:'move' },
        { keys:['F7'],          label:'mkdir' },
        { keys:['F8'],          label:'delete' },
        { keys:['/'],           label:'filter' },
        { keys:[':'],           label:'command' },
        { keys:['?'],           label:'help' },
      ]} padX={16} />

      {/* Status bar */}
      <StatusBar items={[
        { label:'corvus', dot:'working' },
        'session · 04',
        'pane · ' + (focus==='left'?'L':'R'),
        '_spacer',
        '482 events today',
        'free · 218.4 GB',
        'sftp · 12 ms',
      ]} padX={16} />
    </div>
  );
}

window.ClavisFiles = ClavisFiles;
