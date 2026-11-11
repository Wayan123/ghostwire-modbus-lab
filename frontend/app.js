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

function renderLog(scenarioId) {
  const log = incidentLogs.get(scenarioId);
  const body = $('log-body');
  body.replaceChildren();
  $('log-note').textContent = `Simulated capture. ${text(log.capture_note, '')}`;
  const flagged = verifiedTransaction.get(scenarioId);
  for (const entry of Array.isArray(log.entries) ? log.entries : []) {
    const row = document.createElement('tr');
    const seq = addText(row, 'td', text(entry.sequence));
    if (flagged !== undefined && entry.transaction_id === flagged) {
      row.dataset.flag = 'unauthorized';
      row.tabIndex = 0;
      row.setAttribute('aria-label', `Sequence ${text(entry.sequence)}: unauthorized write, verified by your answer`);
      seq.append(document.createElement('br'));
      addText(seq, 'strong', '⚠ Unauthorized write', 'flag');
    }
    addText(row, 'td', text(entry.time));
    addText(row, 'td', text(entry.client));
    addText(row, 'td', text(entry.transaction_id));
    addText(row, 'td', text(entry.unit_id));
    addText(row, 'td', `${String(entry.function_code).padStart(2, '0')} · ${text(entry.function_name)}`);
    addText(row, 'td', text(entry.address));
    addText(row, 'td', text(entry.values));
    const framesCell = document.createElement('td');
    const details = document.createElement('details');
    addText(details, 'summary', `Frames for transaction ${text(entry.transaction_id)}`);
    const frames = document.createElement('div');
    details.append(frames);
    details.addEventListener('toggle', () => { if (details.open && !frames.childElementCount) renderFrames(frames, entry.frames, `log-${entry.sequence}`); });
    framesCell.append(details);
    row.append(framesCell);
    const actionCell = document.createElement('td');
    const select = addText(actionCell, 'button', 'Select');
    select.type = 'button';
    select.className = 'secondary';
    select.setAttribute('aria-label', `Select transaction ${text(entry.transaction_id)} from ${text(entry.client)} for your answer`);
    select.addEventListener('click', () => {
      $('answer-transaction').value = entry.transaction_id;
      $('answer-client').value = entry.client;
      $('log-message').textContent = `Transaction ${text(entry.transaction_id)} from ${text(entry.client)} copied into the answer form.`;
    });
    row.append(actionCell);
    body.append(row);
  }
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
  $('answer-verified').textContent = ok ? 'Verified by the simulated capture: this transaction ID and client label match the write.' : 'Not verified by the simulated capture.';
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
    attempts.push(result);
    answerHistory.set(scenario.id, attempts);
    if (result.verified === true) verifiedTransaction.set(scenario.id, result.answer.transaction_id);
    renderAnswer(result);
    if (incidentLogs.has(scenario.id)) renderLog(scenario.id);
    renderAnswerHistory();
    $('test-message').textContent = result.verified === true
      ? 'Answer verified by the simulated capture. The matching log row is now marked "Unauthorized write".'
      : 'Answer not verified by the simulated capture. Review the log and try again.';
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
$('retry-load').addEventListener('click', loadScenarios);
$('print-report').addEventListener('click', () => window.print());
$('test-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  if (running) return;
  validateScale();
  if (!$('test-form').reportValidity()) return;
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
