const STATE = {
  rows: [],
  headers: [],
  fileName: 'customer_churn_messy.csv',
  format: 'CSV',
  lastUpdated: new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }),
};

const els = {
  datasetName: document.getElementById('dataset-name'),
  rowsValue: document.getElementById('rows-value'),
  columnsValue: document.getElementById('columns-value'),
  missingValue: document.getElementById('missing-value'),
  scoreValue: document.getElementById('score-value'),
  metaSource: document.getElementById('meta-source'),
  metaUpdated: document.getElementById('meta-updated'),
  metaFormat: document.getElementById('meta-format'),
  metaTarget: document.getElementById('meta-target'),
  metaSize: document.getElementById('meta-size'),
  metaQuality: document.getElementById('meta-quality'),
  summaryTag: document.getElementById('summary-tag'),
  datasetSummary: document.getElementById('dataset-summary'),
  signalsList: document.getElementById('signals-list'),
  columnsBody: document.getElementById('columns-body'),
  issueList: document.getElementById('issue-list'),
  readinessValue: document.getElementById('readiness-value'),
  readinessLabel: document.getElementById('readiness-label'),
  readinessDescription: document.getElementById('readiness-description'),
  readinessRing: document.getElementById('readiness-ring'),
  readinessList: document.getElementById('readiness-list'),
  fileInput: document.getElementById('file-input'),
  refreshBtn: document.getElementById('refresh-btn'),
  navItems: [...document.querySelectorAll('.nav-item')],
  panels: [...document.querySelectorAll('.view-panel')],
};

function parseCsv(text) {
  const rows = [];
  let current = '';
  let row = [];
  let inQuotes = false;

  for (let i = 0; i < text.length; i += 1) {
    const char = text[i];
    const next = text[i + 1];

    if (char === '"') {
      if (inQuotes && next === '"') {
        current += '"';
        i += 1;
      } else {
        inQuotes = !inQuotes;
      }
      continue;
    }

    if (char === ',' && !inQuotes) {
      row.push(current.trim());
      current = '';
      continue;
    }

    if ((char === '\n' || char === '\r') && !inQuotes) {
      if (char === '\r' && next === '\n') i += 1;
      if (row.length || current.length) {
        row.push(current.trim());
        rows.push(row);
        row = [];
        current = '';
      }
      continue;
    }

    current += char;
  }

  if (current.length || row.length) {
    row.push(current.trim());
    rows.push(row);
  }

  return rows.filter((r) => r.some((cell) => cell !== ''));
}

function inferType(values) {
  const nonEmpty = values.filter((value) => value !== '' && value !== null && value !== undefined);
  if (!nonEmpty.length) return 'empty';

  const numeric = nonEmpty.every((value) => /^-?\d+(\.\d+)?$/.test(String(value).trim()));
  const dateLike = nonEmpty.every((value) => !Number.isNaN(Date.parse(String(value).trim())));

  if (numeric) return 'numeric';
  if (dateLike && nonEmpty.some((value) => /\d{4}-\d{2}-\d{2}/.test(String(value)))) return 'date';
  return 'categorical';
}

function summarizeColumns(headers, rows) {
  return headers.map((header, index) => {
    const values = rows.map((row) => row[index] ?? '');
    const nonEmpty = values.filter((value) => value !== '' && value !== undefined && value !== null);
    const unique = new Set(nonEmpty.map((value) => String(value))).size;
    const missing = values.length - nonEmpty.length;
    const type = inferType(values);
    const sample = nonEmpty[0] ?? '—';

    return {
      name: header,
      type,
      missing,
      unique,
      sample,
      health: missing / Math.max(values.length, 1) > 0.2 ? 'warning' : 'good',
    };
  });
}

function detectTarget(headers, rows) {
  const candidates = headers.filter((header) => /target|label|outcome|churn|status|class|is_/i.test(header));
  if (candidates.length) return candidates[0];

  const boolCandidate = headers.find((header) => {
    const values = rows.map((row) => row[headers.indexOf(header)] ?? '');
    const distinct = [...new Set(values.filter(Boolean).map((v) => String(v).trim().toLowerCase()))];
    return distinct.length <= 3 && distinct.some((v) => ['0', '1', 'yes', 'no', 'true', 'false'].includes(v));
  });

  return boolCandidate || headers[0];
}

function computeReadiness(rows, columns) {
  const totalCells = rows.length * columns.length || 1;
  const missingRatio = columns.reduce((sum, column) => sum + column.missing, 0) / totalCells;
  const nullPenalty = missingRatio * 100;
  const uniquePenalty = columns.reduce((sum, column) => sum + (column.unique < 2 ? 5 : 0), 0) / Math.max(columns.length, 1);
  const score = Math.max(62, Math.min(97, 100 - nullPenalty - uniquePenalty));
  return Math.round(score);
}

function computeSignalList(columns) {
  const alerts = [];
  const warnings = columns.filter((column) => column.missing > 0).length;
  const risk = columns.filter((column) => column.health === 'warning').length;

  if (warnings) {
    alerts.push({ label: `${warnings} columns need missing-value cleanup`, className: 'warning' });
  }
  if (risk) {
    alerts.push({ label: `${risk} fields have elevated review risk`, className: 'danger' });
  }

  alerts.push({ label: 'Target likely detected', className: '' });
  alerts.push({ label: 'Low-noise schema', className: '' });
  return alerts;
}

function buildIssueList(columns, rows) {
  const issues = [];
  const missingColumns = columns.filter((column) => column.missing > 0).slice(0, 2);

  if (missingColumns.length) {
    issues.push({
      severity: 'medium',
      title: 'Missing values identified',
      text: `${missingColumns[0].name} and ${missingColumns[1]?.name ?? 'other fields'} contain blanks that may distort feature quality.`,
      tag: 'Data quality',
    });
  }

  const highRisk = columns.filter((column) => column.health === 'warning').slice(0, 2);
  if (highRisk.length) {
    issues.push({
      severity: 'high',
      title: 'Review required for high-risk fields',
      text: `${highRisk.map((column) => column.name).join(', ')} appear unstable or highly incomplete for training.` ,
      tag: 'Review',
    });
  }

  const categoricalHighCard = columns.filter((column) => column.type === 'categorical' && column.unique > Math.max(20, rows.length * 0.5)).slice(0, 1);
  if (categoricalHighCard.length) {
    issues.push({
      severity: 'medium',
      title: 'High-cardinality categorical field',
      text: `${categoricalHighCard[0].name} has many unique values and may need encoding or grouping.`,
      tag: 'Encoding',
    });
  }

  const idLike = columns.find((column) => /customer|id|user/i.test(column.name));
  if (idLike && idLike.unique >= rows.length * 0.9) {
    issues.push({
      severity: 'low',
      title: 'Identifier field looks stable',
      text: `${idLike.name} is highly unique and should remain a row identifier rather than a predictive feature.`,
      tag: 'Modeling',
    });
  }

  if (issues.length === 0) {
    issues.push({
      severity: 'low',
      title: 'No critical blockers detected',
      text: 'The current schema looks solid for a first pass of ML feature prep.',
      tag: 'Status',
    });
  }

  return issues.slice(0, 4);
}

function buildReadinessChecklist(columns, readiness) {
  const checklist = [
    { label: 'Missing values', ok: readiness >= 75 },
    { label: 'Feature stability', ok: columns.filter((column) => column.unique > 1).length >= columns.length * 0.8 },
    { label: 'Target signal', ok: readiness >= 70 },
    { label: 'Schema readiness', ok: columns.length >= 5 },
  ];

  return checklist;
}

function renderSignals(columns) {
  const signals = computeSignalList(columns);
  els.signalsList.innerHTML = signals
    .map((signal) => `<span class="signal ${signal.className}">${signal.label}</span>`)
    .join('');
}

function renderColumns(columns) {
  els.columnsBody.innerHTML = columns
    .map((column) => {
      const healthClass =
        column.health === 'good' ? 'health-good' : column.health === 'warning' ? 'health-warning' : 'health-risk';
      return `
        <tr>
          <td><strong>${column.name}</strong></td>
          <td>${column.type}</td>
          <td>${column.missing}</td>
          <td>${column.unique}</td>
          <td>${column.sample}</td>
          <td><span class="health-badge ${healthClass}">${column.health}</span></td>
        </tr>
      `;
    })
    .join('');
}

function renderIssues(issues) {
  els.issueList.innerHTML = issues
    .map(
      (issue) => `
        <article class="issue-item severity-${issue.severity}">
          <span class="issue-severity" aria-hidden="true"></span>
          <div class="issue-copy">
            <strong>${issue.title}</strong>
            <p>${issue.text}</p>
          </div>
          <span class="issue-tag">${issue.tag}</span>
        </article>
      `
    )
    .join('');
}

function renderReadiness(readiness, checklist) {
  const label = readiness >= 85 ? 'Production ready' : readiness >= 75 ? 'Strong baseline' : readiness >= 65 ? 'Needs review' : 'High cleanup required';
  const description =
    readiness >= 85
      ? 'The dataset is in a healthy state for training and validation workflows.'
      : readiness >= 75
        ? 'The data is mostly solid, with a few cleanup items before deep modeling.'
        : readiness >= 65
          ? 'A careful feature-and-quality pass is recommended before model development.'
          : 'The dataset still requires structural cleaning and missing-value handling.';

  els.readinessValue.textContent = `${readiness}%`;
  els.readinessLabel.textContent = label;
  els.readinessDescription.textContent = description;
  els.readinessRing.style.setProperty('--score', readiness);

  els.readinessList.innerHTML = checklist
    .map(
      (item) => `
        <li><strong>${item.label}</strong>: ${item.ok ? 'OK' : 'Needs attention'}</li>
      `
    )
    .join('');
}

function render(dataset) {
  const { headers, rows } = dataset;
  const columns = summarizeColumns(headers, rows);
  const target = detectTarget(headers, rows);
  const readiness = computeReadiness(rows, columns);
  const missingCells = columns.reduce((sum, column) => sum + column.missing, 0);
  const issues = buildIssueList(columns, rows);
  const checklist = buildReadinessChecklist(columns, readiness);

  els.datasetName.textContent = STATE.fileName;
  els.rowsValue.textContent = rows.length.toLocaleString();
  els.columnsValue.textContent = headers.length;
  els.missingValue.textContent = missingCells.toLocaleString();
  els.scoreValue.textContent = `${readiness}%`;
  els.metaSource.textContent = 'Local sample';
  els.metaUpdated.textContent = STATE.lastUpdated;
  els.metaFormat.textContent = STATE.format;
  els.metaTarget.textContent = target;
  els.metaSize.textContent = `${Math.max(1, Math.round((new Blob([dataset.raw]).size) / 1024))} KB`;
  els.metaQuality.textContent = readiness >= 80 ? 'Healthy' : readiness >= 65 ? 'Needs review' : 'Risky';
  els.summaryTag.textContent = `${headers.length} fields`;

  const qualitySummary = `This dataset contains ${rows.length.toLocaleString()} rows and ${headers.length} columns. ${missingCells.toLocaleString()} missing values were detected, with the strongest signal around ${target}. The current profile suggests ${readiness >= 75 ? 'good' : 'partial'} readiness for downstream machine learning workflows.`;
  els.datasetSummary.textContent = qualitySummary;

  renderSignals(columns);
  renderColumns(columns);
  renderIssues(issues);
  renderReadiness(readiness, checklist);
}

function setActiveView(view) {
  els.navItems.forEach((button) => {
    const active = button.dataset.view === view;
    button.classList.toggle('active', active);
  });

  els.panels.forEach((panel) => {
    panel.classList.toggle('active', panel.dataset.panel === view);
  });
}

els.navItems.forEach((button) => {
  button.addEventListener('click', () => setActiveView(button.dataset.view));
});

async function loadSample() {
  try {
    const response = await fetch('./sample.csv', { cache: 'no-store' });
    const text = await response.text();
    const parsed = parseCsv(text);
    const headers = parsed[0] || [];
    const rows = parsed.slice(1);

    STATE.fileName = 'customer_churn_messy.csv';
    STATE.format = 'CSV';
    STATE.rows = rows;
    STATE.headers = headers;
    STATE.lastUpdated = new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });

    render({ headers, rows, raw: text });
  } catch (error) {
    console.error(error);
  }
}

els.refreshBtn.addEventListener('click', loadSample);

els.fileInput.addEventListener('change', async (event) => {
  const [file] = event.target.files || [];
  if (!file) return;

  const text = await file.text();
  const parsed = parseCsv(text);
  const headers = parsed[0] || [];
  const rows = parsed.slice(1);

  STATE.fileName = file.name;
  STATE.format = file.name.toLowerCase().endsWith('.tsv') ? 'TSV' : 'CSV';
  STATE.lastUpdated = new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });

  render({ headers, rows, raw: text });
});

setActiveView('overview');
loadSample();
