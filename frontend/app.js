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
  for (const field of fields) $(field).value = scenario.initial[field];
  validateScale();
  history = [];
  $('result').hidden = true;
  $('empty-result').hidden = false;
  $('result-status').textContent = 'Awaiting read';
  $('result-status').dataset.state = '';
  $('test-message').dataset.state = '';
  $('test-message').textContent = '';
  $('run-test').textContent = 'Run simulated read';
  renderHistory();
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
    for (const scenario of scenarios) {
      const option = document.createElement('option');
      option.value = scenario.id;
      option.textContent = scenario.title;
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
}

function renderHistory() {
  const list = $('history-list');
  list.replaceChildren();
  $('history-empty').hidden = history.length > 0;
  $('print-report').disabled = history.length === 0;
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
}

let expandedBeforePrint = [];
window.addEventListener('beforeprint', () => {
  expandedBeforePrint = Array.from($('history-list').querySelectorAll('details')).map((details) => details.open);
  $('history-list').querySelectorAll('details').forEach ( (details) => { details.open = true; });
});
window.addEventListener('afterprint', () => {
  $('history-list').querySelectorAll('details').forEach((details, index) => { details.open = expandedBeforePrint[index] || false; });
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
    history.push(result);
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
