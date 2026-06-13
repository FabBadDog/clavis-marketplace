// clavis-variations.jsx — six wide-ranging takes on the CLAVIS workspace.
// Each variation receives the same `state` (status, density, gutterWidth, …)
// from the host and renders a complete window. Designed to be focusable
// (full-bleed) inside the design canvas.

const TABS = [
  { id:'corvus',  name:'Corvus',         status:'working' },
  { id:'auth',    name:'Auth Refactor',  status:'waiting' },
  { id:'ui',      name:'UI Widgets',     status:'idle' },
  { id:'deploy',  name:'Deploy',         status:'error'  },
];

const WORKSPACES = [
  { id:'f1', fkey:'F1', name:'Corvus',        status:'working', color:'var(--clavis)' },
  { id:'f2', fkey:'F2', name:'Auth Refactor', status:'waiting', color:'var(--human)' },
  { id:'f3', fkey:'F3', name:'UI Widgets',    status:'idle',    color:'var(--info)' },
  { id:'f4', fkey:'F4', name:'Deploy',        status:'idle',    color:'var(--gold)' },
  { id:'f5', fkey:'F5', name:'Notes',         status:'idle',    color:'var(--ok)'   },
];

const HINTS = [
  { keys:['↑','↓'], label:'navigate' },
  { keys:['↵'],     label:'open'     },
  { keys:['⌫'],     label:'back'     },
  { keys:['⎋'],     label:'close'    },
  { keys:['/'],     label:'filter'   },
];

const STATUS_ITEMS = (state) => [
  { label: 'Turn 4', dot: state.status === 'working' ? 'working' : state.status },
  '$0.18',
  'Context 28%',
  'main · 3 dirty',
  '_spacer',
  '+48 lines',
];

const FILES = [
  { id:'src',     name:'src',                 type:'folder', depth:0 },
  { id:'corvus',  name:'Corvus',              type:'folder', depth:1 },
  { id:'types',   name:'Types.fs',            type:'file',   depth:2 },
  { id:'ndjson',  name:'NdjsonParser.fs',     type:'file',   depth:2, live:true },
  { id:'session', name:'SessionApi.fs',       type:'file',   depth:2 },
  { id:'process', name:'ProcessManager.fs',   type:'file',   depth:2, dirty:true },
  { id:'corvfs',  name:'Corvus.fs',           type:'file',   depth:2 },
  { id:'tests',   name:'tests',               type:'folder', depth:0 },
  { id:'parser',  name:'ParserTests.fs',      type:'file',   depth:1 },
];

// Helper to render conversation array given props that vary per variation
function ConversationStream({ items, gutterWidth, density, typeMode, padX = 28, scanOnTools = false, scanOnRows = false }) {
  return (
    <>
      {items.map((it, i) => {
        if (it.type === 'message') {
          return (
            <div key={i} style={{position:'relative'}}>
              {scanOnRows && it.role === 'agent' && (
                <span style={{position:'absolute',top:0,left:padX,right:padX,height:1,overflow:'hidden',opacity:.4}}><Beam /></span>
              )}
              <MessageRow
                role={it.role} keyword={it.keyword} summary={it.summary} stats={it.stats}
                gutterWidth={gutterWidth} density={density} typeMode={typeMode} padX={padX}>
                {it.role === 'human' ? <HumanBody>{it.body}</HumanBody> : <AgentBody>{it.body}</AgentBody>}
              </MessageRow>
            </div>
          );
        }
        if (it.type === 'tool') {
          return <ToolRow key={i} name={it.name} target={it.target} duration={it.duration} scan={scanOnTools && it.scan} padX={padX} />;
        }
        if (it.type === 'widget') {
          return <InlineWidget key={i} kind={it.kind} label={it.label} question={it.question} options={it.options} selected={it.selected} padX={padX} />;
        }
        return null;
      })}
    </>
  );
}

// Common interactive shell — input + send + caret etc.
function useConversation(initial) {
  const [items, setItems] = useState(initial);
  const [draft, setDraft] = useState('');
  const send = () => {
    const t = draft.trim();
    if (!t) return;
    setItems(s => [...s, {
      type:'message', role:'human', keyword:'Ask',
      summary:'New question', stats:[{glyph:'tokens',value:String(Math.max(8, Math.round(t.length/4)))}],
      body:<span>{t}</span>,
    }]);
    setDraft('');
    // Fake agent reply after a beat
    setTimeout(() => {
      setItems(s => [...s, {
        type:'message', role:'agent', keyword:'Reply',
        summary:'Acknowledged',
        stats:[{glyph:'clock',value:'0.4s'},{glyph:'tokens',value:'24'}],
        body:<span>Working on it.</span>,
      }]);
    }, 350);
  };
  return { items, draft, setDraft, send };
}

// ─────────────────────────────────────────────────────────────────────────────
// 1) SPEC — by-the-book reference. Single window, top tabs, 120px gutter.
// ─────────────────────────────────────────────────────────────────────────────

function VariationSpec({ state }) {
  const { items, draft, setDraft, send } = useConversation(window.CONVERSATION);
  const [active, setActive] = useState('corvus');
  const tabs = useMemo(() => TABS.map(t => t.id === 'corvus' ? { ...t, status: state.status } : t), [state.status]);
  return (
    <div className="clavis-root" style={{position:'relative',width:'100%',height:'100%',display:'flex',flexDirection:'column',background:'var(--bg)'}}>
      <TitleBar name="CLAVIS" meta={`opus 4.6 · ${state.status}`} status={state.status} scan={state.beam==='titlebar'} />
      <TabBar tabs={tabs} active={active} onSelect={setActive} />
      <Breadcrumb parts={['clavis','src','Corvus','NdjsonParser.fs']} />
      <div style={{flex:1,minHeight:0,overflow:'auto'}}>
        <ConversationStream items={items} gutterWidth={state.gutterWidth} density={state.density} typeMode="sans" />
      </div>
      <InputBar value={draft} onChange={setDraft} onSubmit={send} />
      <HintBar hints={HINTS} />
      <StatusBar items={STATUS_ITEMS(state)} />
      <ResizeHandle />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// 2) MARGIN — wide narrative gutter. Mostly serif. The "editorial" take.
// ─────────────────────────────────────────────────────────────────────────────

function VariationMargin({ state }) {
  const { items, draft, setDraft, send } = useConversation(window.CONVERSATION);
  return (
    <div className="clavis-root" style={{width:'100%',height:'100%',display:'flex',flexDirection:'column',background:'var(--bg)',position:'relative'}}>
      <TitleBar name="CLAVIS · CORVUS" meta="opus 4.6" status={state.status} />
      {/* Architectural breadcrumb: italic Garamond current segment + depth dots */}
      <div style={{padding:'14px 40px 12px',borderBottom:'1px solid var(--faint)',display:'flex',alignItems:'baseline',gap:14}}>
        <span style={{fontFamily:'var(--font-sans)',fontSize:9,letterSpacing:'2px',textTransform:'uppercase',color:'var(--text-faint)'}}>clavis › src › Corvus ›</span>
        <span style={{fontFamily:'var(--font-serif)',fontStyle:'italic',fontSize:18,fontWeight:400,color:'var(--text-bright)'}}>NdjsonParser</span>
        <span style={{flex:1}} />
        <span style={{display:'flex',gap:5}}>
          {[0,1,2,3,4].map(i => (
            <span key={i} style={{width:3,height:3,background: i<3 ? 'var(--clavis)' : 'var(--text-xfaint)',opacity: i<3 ? .8 : 1}} />
          ))}
        </span>
      </div>
      <div style={{flex:1,minHeight:0,overflow:'auto'}}>
        <ConversationStream items={items} gutterWidth={160} density="comfortable" typeMode="serif" padX={40} />
      </div>
      <InputBar value={draft} onChange={setDraft} onSubmit={send} padX={40} />
      <HintBar hints={HINTS} padX={40} />
      <StatusBar items={STATUS_ITEMS(state)} padX={40} />
      <ResizeHandle />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// 3) TILED — file tree | conversation | live editor. Compact density.
// ─────────────────────────────────────────────────────────────────────────────

function VariationTiled({ state }) {
  const { items, draft, setDraft, send } = useConversation(window.CONVERSATION);
  const [sel, setSel] = useState('ndjson');
  return (
    <div className="clavis-root" style={{width:'100%',height:'100%',display:'flex',flexDirection:'column',background:'var(--bg)',position:'relative'}}>
      <TitleBar name="CLAVIS" meta={`3 panes · ${state.status}`} status={state.status} scan />
      {/* Three columns separated by 1px */}
      <div style={{flex:1,minHeight:0,display:'grid',gridTemplateColumns:'180px 1fr 1.05fr',background:'var(--line)',gap:1,overflow:'hidden'}}>
        {/* File tree */}
        <div style={{background:'var(--bg)',display:'flex',flexDirection:'column',minHeight:0}}>
          <div style={{padding:'5px 14px',background:'var(--surface-2)',borderBottom:'1px solid var(--line)',
                       fontFamily:'var(--font-sans)',fontSize:8,fontWeight:500,letterSpacing:'2px',
                       textTransform:'uppercase',color:'var(--text-dim)'}}>Files</div>
          <div style={{flex:1,minHeight:0,overflow:'auto'}}>
            <FileTree items={FILES} selected={sel} onSelect={setSel} />
          </div>
        </div>
        {/* Conversation */}
        <div style={{background:'var(--bg)',display:'flex',flexDirection:'column',minHeight:0}}>
          <div style={{padding:'5px 16px',background:'var(--surface-2)',borderBottom:'1px solid var(--line)',
                       fontFamily:'var(--font-sans)',fontSize:8,fontWeight:500,letterSpacing:'2px',
                       textTransform:'uppercase',color:'var(--text-dim)',display:'flex',alignItems:'center',gap:8}}>
            <span>Conversation</span>
            <PulseDot kind={state.status} size={4} />
          </div>
          <div style={{flex:1,minHeight:0,overflow:'auto'}}>
            <ConversationStream items={items} gutterWidth={80} density="compact" typeMode="sans" padX={16} />
          </div>
          <InputBar value={draft} onChange={setDraft} onSubmit={send} padX={16} />
        </div>
        {/* Editor */}
        <div style={{background:'var(--bg)',display:'flex',flexDirection:'column',minHeight:0,position:'relative'}}>
          <div style={{padding:'5px 16px',background:'var(--surface-2)',borderBottom:'1px solid var(--line)',
                       fontFamily:'var(--font-sans)',fontSize:8,fontWeight:500,letterSpacing:'2px',
                       textTransform:'uppercase',color:'var(--text-dim)',display:'flex',alignItems:'center',gap:8}}>
            <span>NdjsonParser.fs</span>
            <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--text-faint)',opacity:.7,letterSpacing:0,textTransform:'none'}}>F# · 22 lines</span>
            <span style={{flex:1}} />
            <span style={{display:'inline-flex',alignItems:'center',gap:4,color:'var(--clavis)',opacity:.8,fontFamily:'var(--font-display)',fontSize:8,letterSpacing:'1.5px'}}>
              <PulseDot size={4} /> LIVE — LINES 11–14
            </span>
          </div>
          <CodeEditor liveLines />
          <div style={{padding:'4px 16px',background:'var(--surface)',borderTop:'1px solid var(--faint)',
                       fontFamily:'var(--font-sans)',fontSize:8.5,letterSpacing:'.8px',color:'var(--text-faint)',
                       display:'flex',gap:14,alignItems:'center'}}>
            <span style={{display:'inline-flex',alignItems:'center',gap:6}}><PulseDot size={4} /> Claude editing</span>
            <span>F# 7.0</span>
            <span>UTF-8</span>
            <span style={{flex:1}} />
            <span>+18 −0</span>
          </div>
        </div>
      </div>
      <HintBar hints={HINTS} padX={16} />
      <StatusBar items={STATUS_ITEMS(state)} padX={16} />
      <ResizeHandle />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// 4) SPINE — vertical workspace bar on the left, no top tabs.
// ─────────────────────────────────────────────────────────────────────────────

function VariationSpine({ state }) {
  const { items, draft, setDraft, send } = useConversation(window.CONVERSATION);
  const [activeWs, setActiveWs] = useState('f1');
  const ws = useMemo(() => WORKSPACES.map(w => w.id === 'f1' ? { ...w, status: state.status } : w), [state.status]);
  return (
    <div className="clavis-root" style={{width:'100%',height:'100%',display:'flex',background:'var(--bg)',position:'relative'}}>
      <WorkspaceBar workspaces={ws} active={activeWs} onSelect={setActiveWs} vertical />
      <div style={{flex:1,minWidth:0,display:'flex',flexDirection:'column'}}>
        <TitleBar name="CORVUS" meta={`${state.status} · F1`} status={state.status} scan={state.beam==='titlebar'} />
        <div style={{padding:'10px 28px 12px',borderBottom:'1px solid var(--faint)',display:'flex',alignItems:'baseline',gap:14}}>
          <span style={{fontFamily:'var(--font-display)',fontSize:14,letterSpacing:'2px',color:'var(--text-bright)',fontWeight:500}}>NDJSON PARSER</span>
          <span style={{fontFamily:'var(--font-mono)',fontSize:10,color:'var(--text-faint)'}}>src/Corvus/NdjsonParser.fs</span>
          <span style={{flex:1}} />
          <span style={{fontFamily:'var(--font-mono)',fontSize:9,color:'var(--text-faint)',display:'inline-flex',alignItems:'center',gap:6}}>
            <PulseDot size={4} kind={state.status} /> turn 4 · 28% ctx
          </span>
        </div>
        <div style={{flex:1,minHeight:0,overflow:'auto'}}>
          <ConversationStream items={items} gutterWidth={state.gutterWidth} density={state.density} typeMode="sans" padX={28} />
        </div>
        <InputBar value={draft} onChange={setDraft} onSubmit={send} padX={28} />
        <HintBar hints={HINTS} padX={28} />
        <StatusBar items={STATUS_ITEMS(state)} padX={28} />
      </div>
      <ResizeHandle />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// 5) BEAM — pushes the living animation rule everywhere it can earn its place.
//    Scan beams on the panel, on each agent row, and on the active tool row.
// ─────────────────────────────────────────────────────────────────────────────

function VariationBeam({ state }) {
  const { items, draft, setDraft, send } = useConversation(window.CONVERSATION);
  const [active, setActive] = useState('corvus');
  const tabs = useMemo(() => TABS.map(t => t.id === 'corvus' ? { ...t, status: state.status } : t), [state.status]);
  return (
    <div className="clavis-root" style={{width:'100%',height:'100%',display:'flex',flexDirection:'column',background:'var(--bg)',position:'relative'}}>
      <TitleBar name="CLAVIS" meta="all alive" status={state.status} scan />
      <TabBar tabs={tabs} active={active} onSelect={setActive} />
      {/* Beam at the very top of the conversation panel — communicates "session working" */}
      <div style={{position:'relative',height:2,background:'var(--xfaint)',overflow:'hidden',flexShrink:0}}>
        {state.status === 'working' && <Beam />}
      </div>
      <div style={{flex:1,minHeight:0,overflow:'auto'}}>
        <ConversationStream items={items} gutterWidth={state.gutterWidth} density={state.density} typeMode="sans" scanOnRows scanOnTools />
      </div>
      {/* Living edge between input and conversation */}
      <div style={{position:'relative',height:1.5,background:'var(--xfaint)',overflow:'hidden',flexShrink:0}}>
        {state.status === 'working' && <Beam />}
      </div>
      <InputBar value={draft} onChange={setDraft} onSubmit={send} />
      <HintBar hints={HINTS} />
      <StatusBar items={STATUS_ITEMS(state)} />
      <ResizeHandle />
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// 6) TYPE — Garamond-forward editorial register.
//    Keywords become italic serif; tool names stay caps but smaller; section
//    rules use real serif type for headings.
// ─────────────────────────────────────────────────────────────────────────────

function VariationType({ state }) {
  const { items, draft, setDraft, send } = useConversation(window.CONVERSATION);
  return (
    <div className="clavis-root" style={{width:'100%',height:'100%',display:'flex',flexDirection:'column',background:'var(--bg)',position:'relative'}}>
      <div style={{position:'relative',display:'flex',alignItems:'baseline',gap:14,padding:'18px 40px 16px',
                   borderBottom:'1px solid var(--line)',background:'var(--surface)'}}>
        <span style={{fontFamily:'var(--font-serif)',fontStyle:'italic',fontSize:22,fontWeight:400,color:'var(--text-bright)',letterSpacing:0}}>Corvus</span>
        <span style={{fontFamily:'var(--font-sans)',fontSize:9,letterSpacing:'2.5px',textTransform:'uppercase',color:'var(--text-dim)'}}>opus 4.6 · turn 4</span>
        <span style={{flex:1}} />
        <PulseDot kind={state.status} />
        <span style={{fontFamily:'var(--font-sans)',fontSize:9,letterSpacing:'2.5px',textTransform:'uppercase',color:'var(--text-dim)'}}>{state.status}</span>
        <span style={{marginLeft:14,opacity:.3,color:'var(--text-dim)'}}><span className="cl-x" /></span>
      </div>
      {/* A pull-quote-style intro that frames the conversation */}
      <div style={{padding:'18px 40px',borderBottom:'1px solid var(--faint)'}}>
        <div style={{fontFamily:'var(--font-serif)',fontSize:11,fontStyle:'italic',color:'var(--text-faint)',letterSpacing:'.5px',textTransform:'uppercase',marginBottom:6}}>Session opened · 14:22</div>
        <div style={{fontFamily:'var(--font-serif)',fontSize:15,fontStyle:'italic',fontWeight:400,color:'var(--text)',lineHeight:1.5,maxWidth:520}}>
          A parser for NDJSON streams, with Result types for malformed lines and async support over the existing <span style={{fontFamily:'var(--font-mono)',fontStyle:'normal',fontSize:12,color:'var(--text-bright)'}}>StreamEvent</span> union.
        </div>
      </div>
      <div style={{flex:1,minHeight:0,overflow:'auto'}}>
        <ConversationStream items={items} gutterWidth={state.gutterWidth} density={state.density} typeMode="serif" padX={40} />
      </div>
      <InputBar value={draft} onChange={setDraft} onSubmit={send} padX={40} />
      <HintBar hints={HINTS} padX={40} />
      <StatusBar items={STATUS_ITEMS(state)} padX={40} />
      <ResizeHandle />
    </div>
  );
}

Object.assign(window, {
  VariationSpec, VariationMargin, VariationTiled,
  VariationSpine, VariationBeam, VariationType,
});
