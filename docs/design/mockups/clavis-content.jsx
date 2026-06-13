// clavis-content.jsx — Realistic Corvus F#/WPF coding session content.
// Defines the messages/tools/widgets the variations render.

const CONVERSATION = [
  {
    type: 'message',
    role: 'human',
    keyword: 'Implement',
    summary: 'Parser module with Result types',
    stats: [{ glyph: 'tokens', value: '42' }],
    body: <span>Implement the <span className="cl-code">NdjsonParser</span> module in Corvus. Handle malformed lines with Result types and support async streaming over <span className="cl-code">IAsyncEnumerable&lt;string&gt;</span>.</span>,
  },
  {
    type: 'message',
    role: 'agent',
    keyword: 'Explore',
    summary: 'Read existing types for alignment',
    stats: [{ glyph: 'clock', value: '3.1s' }, { glyph: 'tokens', value: '312' }],
    body: <span>I'll create the parser module. Let me first read the existing types to ensure the <span className="cl-code">StreamEvent</span> union aligns with what the parser produces.</span>,
  },
  { type: 'tool', name: 'Read', target: 'src/Corvus/Types.fs', duration: '0.4s' },
  { type: 'tool', name: 'Read', target: 'src/Corvus/SessionApi.fs', duration: '0.3s' },
  { type: 'tool', name: 'Grep', target: '"StreamEvent" — 14 matches in 6 files', duration: '0.2s' },
  {
    type: 'message',
    role: 'agent',
    keyword: 'Build',
    summary: 'Three functions, async-friendly',
    stats: [{ glyph: 'clock', value: '4.7s' }, { glyph: 'tokens', value: '584' }],
    body: <span>The parser exposes three functions: <span className="cl-code">tryParseLine</span> for a single record, <span className="cl-code">parseStream</span> over <span className="cl-code">IAsyncEnumerable</span>, and <span className="cl-code">parseBatch</span> for testing. Result types let the caller decide whether a malformed line aborts or is logged and skipped.</span>,
  },
  { type: 'tool', name: 'Write', target: 'src/Corvus/NdjsonParser.fs — new, 48 lines', duration: '1.2s' },
  { type: 'tool', name: 'Edit', target: 'src/Corvus/Corvus.fsproj — +1 line', duration: '0.3s' },
  { type: 'tool', name: 'Bash', target: 'dotnet build src/Corvus', duration: '4.1s', scan: true },
  {
    type: 'widget',
    kind: 'warn',
    label: 'Waiting for input',
    question: 'Tests reference a fixture file that doesn\'t exist yet. Generate it?',
    options: [
      'Yes — create tests/fixtures/sample.ndjson with 5 representative events',
      'No — skip; the user will provide it',
      'Use an inline string literal in the test instead',
    ],
    selected: 0,
  },
];

window.CONVERSATION = CONVERSATION;
