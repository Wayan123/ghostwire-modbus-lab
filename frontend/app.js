'use strict';

const $ = (id) => document.getElementById(id);
const fields = ['unit_id', 'function_code', 'address', 'quantity', 'scale'];
const categories = [
  ['observations', 'Observations'],
  ['possible_causes', 'Possible causes'],
  ['training_hints', 'Training hints'],
  ['next_steps', 'Next steps'],
];
let scenarios = [];
let history = [];
const caseHistory = new Map();
let running = false;

function text(value, fallback = 'Not returned') {
  if (value === null || value === undefined || value === '') return fallback;
  if (Array.isArray(value)) return value.length ? value.map((item) => text(item)).join(', ') : fallback;
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}
function engineeringValue(result) {
  const value = result.engineering_value;
  if (value === null || value === undefined || value === '') return 'Not returned';
  return `${text(value)}${result.unit ? ` ${text(result.unit)}` : ''}`;
}

function validateScale() {
  const input = $('scale');
  input.setCustomValidity(input.value !== '' && Number(input.value) <= 0 ? 'Scale must be greater than zero.' : '');
}

// Client-side bounds mirror the server whitelist for POST /api/test. The server still validates every field.
const fieldRules = {
  unit_id: [1, 247, true, 'Unit ID must be a whole number from 1 to 247.'],
  address: [0, 65535, true, 'Address must be a whole number from 0 to 65535.'],
  quantity: [1, 125, true, 'Quantity must be a whole number from 1 to 125.'],
  scale: [0, 1000000, false, 'Scale must be a number greater than 0 and at most 1,000,000.'],
};

function requestProblem() {
  for (const [field, [min, max, integer, message]] of Object.entries(fieldRules)) {
    const raw = $(field).value.trim();
    const value = Number(raw);
    const ok = raw !== '' && Number.isFinite(value) && value <= max && (integer ? Number.isInteger(value) && value >= min : value > min);
    if (!ok) return { field, message };
  }
  if (Number($('address').value) + Number($('quantity').value) > 65536) {
    return { field: 'quantity', message: 'Address plus quantity must not go past register 65535.' };
  }
  return null;
}

function setInvalid(field, invalid) {
  const input = $(field);
  const described = (input.getAttribute('aria-describedby') || '').split(' ').filter((id) => id && id !== 'test-message');
  if (invalid) {
    input.setAttribute('aria-invalid', 'true');
    described.push('test-message');
  } else {
    input.removeAttribute('aria-invalid');
  }
  if (described.length) input.setAttribute('aria-describedby', described.join(' '));
  else input.removeAttribute('aria-describedby');
}

function clearInvalid() {
  for (const field of Object.keys(fieldRules)) setInvalid(field, false);
}

function verifiedText(value) {
  if (value === true) return 'Verified by this simulated test.';
  if (value === false) return 'Not verified by this simulated test.';
  return text(value, 'No verification result returned.');
}

function sourceText(value) {
  if (value === 'openai-compatible') return 'Source: AI provider';
  if (value === 'deterministic') return 'Source: deterministic fallback';
  return `Source: ${text(value, 'not specified')}`;
}

function fillList(element, values) {
  element.replaceChildren();
  const items = Array.isArray(values) ? values : (values == null ? [] : [values]);
  for (const item of items.length ? items : ['None returned.']) {
    const li = document.createElement('li');
    li.textContent = text(item);
    element.append(li);
  }
}

function addText(parent, tag, value, className) {
  const node = document.createElement(tag);
  node.textContent = value;
  if (className) node.className = className;
  parent.append(node);
  return node;
}

function renderDocumentation(documentation) {
  const container = $('documentation');
  container.replaceChildren();
  const entries = Array.isArray(documentation) ? documentation : [documentation];
  for (const entry of entries) addText(container, 'p', text(entry, 'No documentation returned.'));
}

function selectScenario() {
  const scenario = scenarios.find((item) => item.id === $('scenario').value);
  if (!scenario) return;
  $('scenario-summary').textContent = text(scenario.summary, '');
  renderDocumentation(scenario.documentation);
  $('history-scenario').textContent = `Scenario: ${text(scenario.title)}`;
  history = caseHistory.get(scenario.id) || [];
  const latest = history[history.length - 1];
  for (const field of fields) $(field).value = latest?.request?.[field] ?? scenario.initial[field];
  validateScale();
  clearInvalid();
  if (latest) {
    renderResult(latest);
  } else {
    $('result').hidden = true;
    $('empty-result').hidden = false;
    $('result-status').textContent = 'Awaiting read';
    $('result-status').dataset.state = '';
  }
  $('test-message').dataset.state = '';
  $('test-message').textContent = '';
  $('run-test').textContent = latest ? 'Retest simulated read' : 'Run simulated read';
  renderHistory();
  const security = scenario.category === 'security';
  $('incident-panel').hidden = !security;
  if (security) showIncident(scenario);
}

async function api(path, options) {
  const response = await fetch(path, options);
  let data;
  try { data = await response.json(); } catch { throw new Error(`Server returned an unreadable response (HTTP ${response.status}).`); }
  if (!response.ok) {
    const detail = data.detail || data.error || data.message;
    throw new Error(detail ? text(detail) : `Request failed (HTTP ${response.status}).`);
  }
  return data;
}

async function loadScenarios() {
  $('load-message').hidden = false;
  $('load-message').textContent = 'Loading scenarios…';
  $('load-message').dataset.state = '';
  $('retry-load').hidden = true;
  $('setup-content').hidden = true;
  try {
    const data = await api('/api/scenarios');
    if (!Array.isArray(data.scenarios) || !data.scenarios.length) throw new Error('No scenarios were returned.');
    scenarios = data.scenarios;
    $('scenario').replaceChildren();
    const groups = [['troubleshooting', 'Troubleshooting'], ['security', 'Security incidents']];
    for (const [category, label] of groups) {
      const members = scenarios.filter((scenario) => scenario.category === category);
      if (!members.length) continue;
      const group = document.createElement('optgroup');
      group.label = label;
      for (const scenario of members) {
        const option = document.createElement('option');
        option.value = scenario.id;
        option.textContent = scenario.difficulty ? `${text(scenario.title)} · ${text(scenario.difficulty)}` : text(scenario.title);
        group.append(option);
      }
      $('scenario').append(group);
    }
    for (const scenario of scenarios.filter((item) => !groups.some(([category]) => category === item.category))) {
      const option = document.createElement('option');
      option.value = scenario.id;
      option.textContent = text(scenario.title);
      $('scenario').append(option);
    }
    selectScenario();
    $('setup-content').hidden = false;
    $('load-message').hidden = true;
    showTour();
  } catch (error) {
    $('load-message').textContent = `Could not load scenarios: ${error.message}`;
    $('load-message').dataset.state = 'error';
    $('retry-load').hidden = false;
  }
}

function renderResult(result) {
  $('empty-result').hidden = true;
  $('result').hidden = false;
  $('result-status').textContent = `Status: ${text(result.status, 'unknown')}`;
  $('result-status').dataset.state = result.status === 'response' ? 'success' : (result.status === 'exception' || result.status === 'error' ? 'error' : '');
  $('engineering-value').textContent = engineeringValue(result);
  $('raw-registers').textContent = text(result.raw_registers);
  for (const [key] of categories) fillList($(key), result[key]);
  $('verified').textContent = verifiedText(result.verified);
  $('verified').parentElement.dataset.state = result.verified === true ? 'success' : '';
  $('explanation').textContent = text(result.explanation, 'No explanation returned.');
  $('explanation-source').textContent = sourceText(result.explanation_source);
  renderFrames($('frames'), result.frames, 'read');
}

let frameIds = 0;
// Renders frames returned by the server. Every byte and value comes from the response; textContent only.
function renderFrames(container, frames, label) {
  container.replaceChildren();
  if (!Array.isArray(frames) || !frames.length) {
    addText(container, 'p', 'No frame was returned: the request bytes did not reach the simulator.', 'muted');
    return;
  }
  for (const frame of frames) {
    const id = `frame-${label}-${++frameIds}`;
    const block = document.createElement('section');
    block.className = 'frame';
    block.setAttribute('aria-labelledby', `${id}-title`);
    const heading = addText(block, 'h4', frame.direction === 'request' ? 'Request sent to simulator' : 'Response from simulator');
    heading.id = `${id}-title`;
    const hex = document.createElement('code');
    hex.className = 'hex';
    hex.setAttribute('aria-label', `${heading.textContent}, hexadecimal bytes`);
    const bytes = text(frame.hex, '').split(' ').filter(Boolean);
    bytes.forEach((byte, index) => {
      if (index) hex.append(' ');
      const span = document.createElement('span');
      span.dataset.i = String(index);
      span.textContent = byte;
      hex.append(span);
    });
    block.append(hex);
    const table = document.createElement('table');
    table.className = 'frame-table';
    const caption = addText(table, 'caption', `${heading.textContent}: decoded fields (simulated)`);
    caption.className = 'visually-hidden';
    const head = document.createElement('thead');
    const headRow = document.createElement('tr');
    for (const name of ['Field', 'Bytes (hex)', 'Decoded value']) {
      const th = addText(headRow, 'th', name);
      th.scope = 'col';
    }
    head.append(headRow);
    table.append(head);
    const body = document.createElement('tbody');
    const spans = Array.from(hex.querySelectorAll('span'));
    (Array.isArray(frame.fields) ? frame.fields : []).forEach((field, index) => {
      const offset = Number(field?.bytes?.offset);
      const length = Number(field?.bytes?.length);
      const row = document.createElement('tr');
      row.tabIndex = 0;
      const nameCell = addText(row, 'th', text(field.name));
      nameCell.scope = 'row';
      const bytesCell = document.createElement('td');
      addText(bytesCell, 'code', bytes.slice(offset, offset + length).join(' '));
      const range = addText(bytesCell, 'span', length > 1 ? ` bytes ${offset}–${offset + length - 1}` : ` byte ${offset}`, 'range');
      range.id = `${id}-range-${index}`;
      row.setAttribute('aria-describedby', range.id);
      row.append(bytesCell);
      addText(row, 'td', text(field.value));
      const highlight = (on) => {
        spans.forEach((span, i) => span.classList.toggle('hl', on && i >= offset && i < offset + length));
        row.classList.toggle('active', on);
      };
      row.addEventListener('mouseenter', () => highlight(true));
      row.addEventListener('mouseleave', () => highlight(false));
      row.addEventListener('focusin', () => highlight(true));
      row.addEventListener('focusout', () => highlight(false));
      body.append(row);
    });
    if (frame.decode_error) {
      const row = document.createElement('tr');
      row.className = 'decode-error';
      const cell = addText(row, 'td', `Decode error: ${text(frame.decode_error)}`);
      cell.colSpan = 3;
      body.append(row);
    }
    table.append(body);
    block.append(table);
    container.append(block);
  }
}
function renderComparison() {
  const container = $('comparison-results');
  container.replaceChildren();
  const hasComparison = history.length >= 2;
  $('comparison-empty').hidden = hasComparison;
  container.hidden = !hasComparison;
  if (!hasComparison) return;

  const previous = history[history.length - 2];
  const current = history[history.length - 1];
  for (const [label, entry, number] of [
    ['Previous', previous, history.length - 1],
    ['Current', current, history.length],
  ]) {
    const section = document.createElement('section');
    section.className = 'comparison-read';
    addText(section, 'h3', `${label} · read ${number}`);
    const request = entry.request || {};
    addText(section, 'p', `Request: unit ${text(request.unit_id)}, function ${text(request.function_code)}, address ${text(request.address)}, quantity ${text(request.quantity)}, scale ${text(request.scale)}.`);
    addText(section, 'p', `Status: ${text(entry.status, 'unknown')}. Raw registers: ${text(entry.raw_registers)}. Engineering value: ${engineeringValue(entry)} (simulated).`);
    addText(section, 'p', verifiedText(entry.verified));
    container.append(section);
  }
  const changed = fields.filter((field) => previous.request?.[field] !== current.request?.[field]);
  const labels = { unit_id: 'Unit ID', function_code: 'Function code', address: 'Address', quantity: 'Quantity', scale: 'Scale' };
  addText(container, 'p', changed.length
    ? `Changed settings: ${changed.map((field) => `${labels[field]} (${text(previous.request?.[field])} to ${text(current.request?.[field])})`).join(', ')}.`
    : 'No request settings changed between these reads.', 'comparison-changes');
}


function renderHistory() {
  const list = $('history-list');
  list.replaceChildren();
  $('history-empty').hidden = history.length > 0;
  $('print-report').disabled = history.length === 0;
  renderComparison();
  history.forEach((entry, index) => {
    const item = document.createElement('li');
    const details = document.createElement('details');
    const summary = document.createElement('summary');
    summary.textContent = `Read ${index + 1} · ${text(entry.status, 'unknown')} · ${verifiedText(entry.verified)}`;
    details.append(summary);
    const body = document.createElement('div');
    body.className = 'history-detail';
    const request = entry.request || {};
    addText(body, 'p', `Request: unit ${text(request.unit_id)}, function ${text(request.function_code)}, address ${text(request.address)}, quantity ${text(request.quantity)}, scale ${text(request.scale)}.`);
    addText(body, 'p', `Raw registers: ${text(entry.raw_registers)}. Engineering value: ${engineeringValue(entry)} (simulated).`);
    for (const frame of Array.isArray(entry.frames) ? entry.frames : []) {
      addText(body, 'p', `${frame.direction === 'request' ? 'Request frame' : 'Response frame'} (simulated, hex): ${text(frame.hex)}${frame.decode_error ? ` · Decode error: ${text(frame.decode_error)}` : ''}`, 'hex-line');
    }
    for (const [key, label] of categories) {
      addText(body, 'h3', label);
      const ul = document.createElement('ul');
      fillList(ul, entry[key]);
      body.append(ul);
    }
    addText(body, 'h3', `Explanation · ${sourceText(entry.explanation_source)}`);
    addText(body, 'p', text(entry.explanation, 'No explanation returned.'));
    details.append(body);
    item.append(details);
    list.append(item);
  });
  renderAnswerHistory();
}

let expandedBeforePrint = [];
const historyDetails = () => [...$('history-list').querySelectorAll('details'), ...$('answer-history').querySelectorAll('details')];
window.addEventListener('beforeprint', () => {
  expandedBeforePrint = historyDetails().map((details) => details.open);
  historyDetails().forEach((details) => { details.open = true; });
});
window.addEventListener('afterprint', () => {
  historyDetails().forEach((details, index) => { details.open = expandedBeforePrint[index] || false; });
});

// ---- Security incident: simulated traffic log and answer ----
const incidentLogs = new Map();
const answerHistory = new Map();
const verifiedTransaction = new Map();
let answering = false;

function currentScenario() { return scenarios.find((item) => item.id === $('scenario').value); }

async function showIncident(scenario) {
  $('answer-result').hidden = true;
  const attempts = answerHistory.get(scenario.id) || [];
  if (attempts.length) renderAnswer(attempts[attempts.length - 1]);
  renderAnswerHints(scenario.id);
  $('incident-task').textContent = text(scenario.incident?.task, '');
  if (incidentLogs.has(scenario.id)) { renderLog(scenario.id); return; }
  $('log-body').replaceChildren();
  $('log-note').textContent = 'Loading the simulated capture…';
  try {
    const log = await api(`/api/incident/${encodeURIComponent(scenario.id)}/log`);
    incidentLogs.set(scenario.id, log);
    if (currentScenario()?.id === scenario.id) renderLog(scenario.id);
  } catch (error) {
    $('log-note').textContent = `Could not load the simulated traffic log: ${error.message}`;
  }
}

function flagLabel(scenarioId) {
  return text(scenarios.find((item) => item.id === scenarioId)?.incident?.flag, '⚠ Answer verified');
}

// Status as text from the captured entry; the exception name comes from the server-decoded response frame.
function statusLabel(entry) {
  if (entry.status === 'response') return 'Response';
  if (entry.status === 'timeout') return 'Timeout (no reply)';
  if (entry.status === 'exception') {
    const response = (Array.isArray(entry.frames) ? entry.frames : []).find((frame) => frame.direction === 'response');
    const field = (response?.fields || []).find((item) => item.name === 'Exception code');
    return `Exception ${text(field?.value ?? entry.exception_code)}`;
  }
  return text(entry.status, 'Unknown');
}

function renderLog(scenarioId) {
  const log = incidentLogs.get(scenarioId);
  const body = $('log-body');
  body.replaceChildren();
  $('log-note').textContent = `Simulated capture. ${text(log.capture_note, '')}`;
  const flagged = verifiedTransaction.get(scenarioId);
  const flag = flagLabel(scenarioId);
  for (const entry of Array.isArray(log.entries) ? log.entries : []) {
    const row = document.createElement('tr');
    row.dataset.status = text(entry.status, '');
    const seq = addText(row, 'td', text(entry.sequence));
    if (flagged !== undefined && entry.transaction_id === flagged) {
      row.dataset.flag = 'unauthorized';
      row.tabIndex = 0;
      row.setAttribute('aria-label', `Sequence ${text(entry.sequence)}: ${flag.replace(/^⚠\s*/, '')}, verified by your answer`);
      seq.append(document.createElement('br'));
      addText(seq, 'strong', flag, 'flag');
    }
    const timeCell = document.createElement('td');
    const iso = text(entry.time);
    const clock = /T(\d{2}:\d{2}:\d{2})/.exec(iso);
    if (clock) {
      // Short visible time; the full ISO timestamp is in the title and read by screen readers.
      const time = addText(timeCell, 'time', clock[1]);
      time.dateTime = iso;
      time.title = iso;
      time.setAttribute('aria-hidden', 'true');
      addText(timeCell, 'span', iso, 'visually-hidden');
    } else {
      timeCell.textContent = iso;
    }
    row.append(timeCell);
    addText(row, 'td', text(entry.client));
    addText(row, 'td', text(entry.transaction_id));
    addText(row, 'td', text(entry.unit_id));
    addText(row, 'td', `${String(entry.function_code).padStart(2, '0')} · ${text(entry.function_name)}`, 'wrap');
    addText(row, 'td', statusLabel(entry), 'wrap');
    addText(row, 'td', text(entry.address));
    addText(row, 'td', text(entry.values, 'None'));
    // Frames open in a full-width row below the entry instead of inside a narrow cell.
    const framesRow = document.createElement('tr');
    framesRow.className = 'frames-row';
    framesRow.id = `log-frames-${text(entry.sequence)}`;
    framesRow.hidden = true;
    const framesCell = document.createElement('td');
    framesCell.colSpan = 10;
    framesRow.append(framesCell);
    const toggleCell = document.createElement('td');
    const toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'secondary';
    toggle.setAttribute('aria-expanded', 'false');
    toggle.setAttribute('aria-controls', framesRow.id);
    const setToggleLabel = (open) => {
      toggle.replaceChildren(open ? 'Hide frames' : 'Show frames');
      addText(toggle, 'span', ` for transaction ${text(entry.transaction_id)}`, 'visually-hidden');
    };
    setToggleLabel(false);
    toggle.addEventListener('click', () => {
      const open = framesRow.hidden;
      if (open && !framesCell.childElementCount) {
        addText(framesCell, 'p', `Frames for transaction ${text(entry.transaction_id)} (simulated capture)`, 'frames-title');
        const frames = document.createElement('div');
        framesCell.append(frames);
        renderFrames(frames, entry.frames, `log-${entry.sequence}`);
      }
      framesRow.hidden = !open;
      toggle.setAttribute('aria-expanded', String(open));
      setToggleLabel(open);
    });
    toggleCell.append(toggle);
    row.append(toggleCell);
    body.append(row, framesRow);
  }
}

// Progressive training hints, kept client-side: none for the first 2 unverified answers,
// hint 1 from the 3rd, hint 2 from the 4th. Hidden once an answer is verified. They never name the row.
// The hint text is case-specific and comes from the scenario's `incident` field.
const hintThreshold = 3;

function answerHints(scenarioId) {
  const hints = scenarios.find((item) => item.id === scenarioId)?.incident?.hints;
  return Array.isArray(hints) ? hints : [];
}

function renderAnswerHints(scenarioId) {
  const hints = answerHints(scenarioId);
  const attempts = answerHistory.get(scenarioId) || [];
  const wrong = attempts.filter((entry) => entry.verified !== true).length;
  const shown = attempts.some((entry) => entry.verified === true) ? 0 : Math.max(0, Math.min(hints.length, wrong - hintThreshold + 1));
  const list = $('answer-hint-list');
  list.replaceChildren();
  hints.slice(0, shown).forEach((hint, index) => addText(list, 'li', `Training hint ${index + 1}: ${hint}`));
  $('answer-hints').hidden = shown === 0;
  return shown;
}

function renderAnswer(result) {
  $('answer-result').hidden = false;
  const ok = result.verified === true;
  $('answer-status').textContent = ok ? 'Status: answer verified' : 'Status: answer not verified';
  $('answer-status').dataset.state = ok ? 'success' : 'error';
  for (const key of ['observations', 'possible_causes', 'training_hints', 'next_steps']) fillList($(`answer-${key}`), result[key]);
  const mitigations = Array.isArray(result.mitigations) ? result.mitigations : [];
  $('mitigations-section').hidden = mitigations.length === 0;
  fillList($('answer-mitigations'), mitigations);
  $('answer-verified').textContent = ok ? 'Verified by the simulated capture: this transaction ID and client label match the evidence for this case.' : 'Not verified by the simulated capture.';
  $('answer-verified').parentElement.dataset.state = ok ? 'success' : '';
  $('answer-explanation').textContent = text(result.explanation, 'No explanation returned.');
  $('answer-explanation-source').textContent = sourceText(result.explanation_source);
}

function renderAnswerHistory() {
  const list = $('answer-history');
  list.replaceChildren();
  const scenario = currentScenario();
  const attempts = scenario ? (answerHistory.get(scenario.id) || []) : [];
  $('answer-history-title').hidden = attempts.length === 0;
  attempts.forEach((entry, index) => {
    const item = document.createElement('li');
    const details = document.createElement('details');
    addText(details, 'summary', `Answer ${index + 1} · transaction ${text(entry.answer?.transaction_id)} · client ${text(entry.answer?.client)} · ${entry.verified === true ? 'Verified by the simulated capture.' : 'Not verified by the simulated capture.'}`);
    const body = document.createElement('div');
    body.className = 'history-detail';
    for (const [key, label] of [...categories, ['mitigations', 'Next steps · real-world guidance, not verified by this simulation']]) {
      if (key === 'mitigations' && !(entry.mitigations || []).length) continue;
      addText(body, 'h3', label);
      const ul = document.createElement('ul');
      fillList(ul, entry[key]);
      body.append(ul);
    }
    addText(body, 'h3', `Explanation · ${sourceText(entry.explanation_source)}`);
    addText(body, 'p', text(entry.explanation, 'No explanation returned.'));
    details.append(body);
    item.append(details);
    list.append(item);
  });
  if (attempts.length) {
    $('print-report').disabled = false;
    $('history-empty').hidden = true;
  }
}

$('answer-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  if (answering) return;
  if (!$('answer-form').reportValidity()) return;
  const scenario = currentScenario();
  if (!scenario || scenario.category !== 'security') return;
  const request = { scenario_id: scenario.id, transaction_id: Number($('answer-transaction').value), client: $('answer-client').value.trim() };
  answering = true;
  $('answer-fields').disabled = true;
  // Not `disabled`: a disabled button would drop keyboard focus while the result is announced.
  $('submit-answer').setAttribute('aria-disabled', 'true');
  $('test-message').dataset.state = '';
  $('test-message').textContent = 'Checking your answer against the simulated capture…';
  try {
    const result = await api('/api/incident/answer', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
    if (!result.answer) result.answer = request;
    const attempts = answerHistory.get(scenario.id) || [];
    const hintsBefore = renderAnswerHints(scenario.id);
    attempts.push(result);
    answerHistory.set(scenario.id, attempts);
    if (result.verified === true) verifiedTransaction.set(scenario.id, result.answer.transaction_id);
    renderAnswer(result);
    if (incidentLogs.has(scenario.id)) renderLog(scenario.id);
    renderAnswerHistory();
    const hintsAfter = renderAnswerHints(scenario.id);
    $('test-message').textContent = result.verified === true
      ? `Answer verified by the simulated capture. The matching log row is now marked "${flagLabel(scenario.id).replace(/^⚠\s*/, '')}".`
      : `Answer not verified by the simulated capture. Review the log and try again.${hintsAfter > hintsBefore ? ` Training hint ${hintsAfter}: ${answerHints(scenario.id)[hintsAfter - 1]}` : ''}`;
  } catch (error) {
    $('test-message').textContent = `Answer failed: ${error.message}`;
    $('test-message').dataset.state = 'error';
  } finally {
    answering = false;
    $('answer-fields').disabled = false;
    $('submit-answer').removeAttribute('aria-disabled');
  }
});

$('scenario').addEventListener('change', selectScenario);
$('scale').addEventListener('input', validateScale);
for (const field of Object.keys(fieldRules)) $(field).addEventListener('input', () => setInvalid(field, false));

// Guided tour banner for first-time visitors. Dismissal is remembered in localStorage when available.
const tourKey = 'ghostwire.tourDismissed';
const tourScenario = 'unit-id';
function tourDismissed() {
  try { return localStorage.getItem(tourKey) === '1'; } catch { return false; }
}
function showTour() {
  $('tour').hidden = tourDismissed() || !scenarios.some((item) => item.id === tourScenario);
}
$('tour-start').addEventListener('click', () => {
  if (running || !scenarios.some((item) => item.id === tourScenario)) return;
  $('scenario').value = tourScenario;
  selectScenario();
  $('test-message').textContent = 'The Silent Sensor is selected. Run the initial simulated read, then inspect the evidence.';
  $('scenario').focus();
});
$('tour-dismiss').addEventListener('click', () => {
  try { localStorage.setItem(tourKey, '1'); } catch { /* Storage unavailable: dismiss for this page only. */ }
  $('tour').hidden = true;
  $('scenario').focus();
});
$('retry-load').addEventListener('click', loadScenarios);
$('print-report').addEventListener('click', () => window.print());
$('test-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  if (running) return;
  validateScale();
  const problem = requestProblem();
  clearInvalid();
  if (problem) {
    setInvalid(problem.field, true);
    $('test-message').textContent = `${problem.message} Nothing was sent.`;
    $('test-message').dataset.state = 'error';
    $(problem.field).focus();
    return;
  }
  const request = { scenario_id: $('scenario').value };
  for (const field of fields) request[field] = Number($(field).value);
  running = true;
  $('scenario').disabled = true;
  $('request-fields').disabled = true;
  $('run-test').disabled = true;
  $('test-message').textContent = 'Reading the local simulator…';
  $('test-message').dataset.state = '';
  try {
    const result = await api('/api/test', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
    if (!result.request) result.request = request;
    history.push(result);
    caseHistory.set(request.scenario_id, history);
    renderResult(result);
    renderHistory();
    $('run-test').textContent = 'Retest simulated read';
    $('test-message').textContent = 'Read complete. Adjust a setting and retest to compare results.';
  } catch (error) {
    $('test-message').textContent = `Read failed: ${error.message} Check the request and try again.`;
    $('test-message').dataset.state = 'error';
  } finally {
    running = false;
    $('scenario').disabled = false;
    $('request-fields').disabled = false;
    $('run-test').disabled = false;
  }
});

loadScenarios();
