// Live smoke test of the NimbleWrite API against real NVIDIA NIM models.
const BASE = 'http://127.0.0.1:4317/api';
const post = async (path, body) => {
  const t0 = Date.now();
  const res = await fetch(BASE + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: res.status, ms: Date.now() - t0, json };
};
const which = process.argv[2] || 'all';
const goals = { audience: 'knowledgeable', formality: 'neutral', domain: 'general', intent: [] };

if (which === 'all' || which === 'check') {
  const paragraphs = [
    { id: 'a', text: 'Their going to the libary tomorow, and me and him was planning to finish the report. In my opinion, I think that the the project is very good but it need alot of improvement in order to be successfull.' },
    { id: 'b', text: 'The report was written by the team in a hurry. We should of checked it more carefully, irregardless of the deadline. Send me the files asap because the client is really really angry and we has no time.' },
    { id: 'c', text: 'Roxy organised the colour palette for NimbleWrite and we travelled to London to present it.' },
  ];
  const r = await post('/ai/check', { paragraphs, options: { dialect: 'en-GB', goals, dictionary: ['Roxy', 'NimbleWrite'], categories: ['correctness', 'clarity', 'engagement', 'delivery'] } });
  console.log(`CHECK HTTP ${r.status} in ${r.ms}ms (model ${r.json.model}, nim ${r.json.ms}ms)`);
  if (r.status !== 200) console.log(r.json);
  for (const res of r.json.results ?? []) {
    const p = paragraphs.find((x) => x.id === res.id).text;
    for (const i of res.issues) {
      const ok = p.slice(i.offset, i.offset + i.length) === i.original ? 'ok' : 'OFFSET-MISMATCH';
      console.log(`  [${res.id}] ${i.category.padEnd(11)} ${i.title.padEnd(28)} "${i.original}" -> ${JSON.stringify(i.replacements)} ${ok}\n        ${i.explanation}`);
    }
  }
}

if (which === 'all' || which === 'misc') {
  const tone = await post('/ai/tone', { text: 'Hi team, I just wanted to quickly check whether maybe we could possibly push the deadline? Sorry to bother you, I know everyone is busy.' });
  console.log(`\nTONE HTTP ${tone.status} ${tone.ms}ms`, JSON.stringify(tone.json));
  const syn = await post('/ai/synonyms', { word: 'happy', sentence: 'I am happy to help with the launch.', dialect: 'en-GB' });
  console.log(`SYNONYMS HTTP ${syn.status} ${syn.ms}ms`, JSON.stringify(syn.json));
}

async function stream(body) {
  const t0 = Date.now();
  const res = await fetch(BASE + '/ai/write', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const reader = res.body.getReader(); const dec = new TextDecoder(); let buf = ''; let out = ''; let first = 0; let meta = ''; let err = '';
  for (;;) {
    const { done, value } = await reader.read(); if (done) break;
    buf += dec.decode(value, { stream: true });
    let i;
    while ((i = buf.indexOf('\n\n')) >= 0) {
      const block = buf.slice(0, i); buf = buf.slice(i + 2);
      const ev = /event: (\w+)/.exec(block)?.[1]; const data = JSON.parse(/data: (.*)/.exec(block)?.[1] ?? 'null');
      if (ev === 'delta') { if (!first) first = Date.now() - t0; out += data.t; }
      if (ev === 'meta') meta = data.model; if (ev === 'error') err = data.message;
    }
  }
  return { out, first, total: Date.now() - t0, meta, err };
}

if (which === 'all' || which === 'write') {
  for (const body of [
    { task: 'shorten', text: 'In my opinion, I think that it is really very important that we all make sure to take the time to carefully review the document before we send it out to the client, because otherwise there might be mistakes.' },
    { task: 'confident', text: 'I just think maybe we could sort of try a new approach, if that is okay with everyone?' },
    { task: 'paraphrase', mode: 'academic', text: 'Kids who read a lot do better at school.' },
    { task: 'humanize', text: 'In today\'s fast-paced digital landscape, effective communication plays a crucial role. Moreover, it serves as a testament to our commitment to excellence, fostering collaboration and showcasing innovation.' },
    { task: 'translate', targetLanguage: 'French', text: 'The report is late because the data was messy. Sorry!' },
  ]) {
    const r = await stream(body);
    console.log(`\nWRITE ${body.task}${body.mode ? '/' + body.mode : ''} [${r.meta}] first token ${r.first}ms, total ${r.total}ms ${r.err ? 'ERROR ' + r.err : ''}\n  ${r.out.replace(/\n/g, '\n  ')}`);
  }
}

if (which === 'all' || which === 'agents') {
  const essay = 'Social media has changed how teenagers communicate. Studies show that 95% of teens use YouTube and that heavy use is linked to anxiety. In 2004 Facebook launched and quickly took over. Schools should therefore ban phones entirely, since it is obvious that this would fix the problem.';
  const rr = await post('/ai/agents/reader', { text: essay, reader: 'a sceptical head teacher' });
  console.log(`\nREADER HTTP ${rr.status} ${rr.ms}ms`, JSON.stringify(rr.json).slice(0, 700));
  const gr = await post('/ai/agents/grader', { text: essay, rubric: 'Argumentative essay, 300 words. Clear thesis, evidence with citations, counter-arguments.', level: 'secondary school (GCSE)' });
  console.log(`GRADER HTTP ${gr.status} ${gr.ms}ms`, JSON.stringify(gr.json).slice(0, 700));
  const cl = await post('/ai/agents/claims', { text: essay });
  console.log(`CLAIMS HTTP ${cl.status} ${cl.ms}ms`, JSON.stringify(cl.json).slice(0, 700));
  const det = await post('/ai/agents/detect', { text: 'In today\'s fast-paced digital landscape, effective communication plays a crucial role in organisational success. Moreover, it serves as a testament to our unwavering commitment to excellence. Furthermore, by fostering collaboration, teams can seamlessly navigate complex challenges. Additionally, leveraging robust tools enhances productivity.\n\nhonestly i nearly binned the whole draft last night. the cat sat on my keyboard twice, the tea went cold, and the intro still reads like a tax form. but the middle bit about the bus strike? that one I actually like.' });
  console.log(`DETECT HTTP ${det.status} ${det.ms}ms`, JSON.stringify(det.json).slice(0, 900));
}

if (which === 'all' || which === 'tools') {
  const doi = await post('/citation/lookup', { input: 'https://doi.org/10.1038/nature14539' });
  console.log(`\nCITATION DOI HTTP ${doi.status} ${doi.ms}ms`);
  for (const f of doi.json.formatted ?? []) console.log(`  ${f.style}: ${f.text}`);
  if (doi.status !== 200) console.log(doi.json);
  const url = await post('/citation/lookup', { input: 'https://www.bbc.co.uk/news/technology' });
  console.log(`CITATION URL HTTP ${url.status} ${url.ms}ms`);
  for (const f of url.json.formatted ?? []) console.log(`  ${f.style}: ${f.text}`);
  if (url.status !== 200) console.log(url.json);
  const plag = await post('/plagiarism', { text: 'The report was written by the team in a hurry. We should of checked it more carefully, irregardless of the deadline. Totally new sentence here that nobody wrote before.', includeDocs: true, web: true, sources: [] });
  console.log(`PLAGIARISM HTTP ${plag.status} ${plag.ms}ms`, JSON.stringify(plag.json).slice(0, 600));
}
