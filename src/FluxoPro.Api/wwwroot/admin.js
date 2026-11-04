const $ = selector => document.querySelector(selector);
const currency = value => Number(value).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const date = value => new Date(value).toLocaleString('pt-BR');
let currentPage = 1;
let currentTotal = 0;

async function api(path, options = {}) {
  const response = await fetch('/api/admin' + path, {
    credentials: 'same-origin',
    ...options,
    headers: { ...(options.body ? { 'Content-Type': 'application/json' } : {}), 'X-FluxoPro-Admin': '1', ...(options.headers || {}) }
  });
  if (response.status === 401) { showLogin(); throw new Error('Sessão encerrada. Entre novamente.'); }
  if (!response.ok) {
    let detail = null;
    try { detail = await response.json(); } catch { /* resposta vazia */ }
    throw new Error(detail?.erro || detail?.detail || `Erro HTTP ${response.status}`);
  }
  return response.status === 204 ? null : response;
}

function showLogin() { $('#login-screen').hidden = false; $('#admin-layout').hidden = true; }
function showAdmin() { $('#login-screen').hidden = true; $('#admin-layout').hidden = false; showSection('overview'); }
function statusBadge(status) {
  const badge = document.createElement('span'); badge.className = 'status';
  if (status === 'Concluída') badge.classList.add('done');
  if (status === 'Falha') badge.classList.add('failed');
  if (status === 'Em processamento') badge.classList.add('processing');
  badge.textContent = status; return badge;
}
function td(text) { const cell = document.createElement('td'); cell.textContent = text; return cell; }
function rowFor(quote, recent = false) {
  const row = document.createElement('tr');
  row.append(td(quote.nome), td(quote.servico));
  if (!recent) row.append(td(currency(quote.valorEstimado)));
  row.append(td(date(quote.criadoEm)));
  const status = document.createElement('td'); status.append(statusBadge(quote.status)); row.append(status);
  const action = document.createElement('td'); const button = document.createElement('button');
  button.type = 'button'; button.textContent = 'Detalhes';
  button.addEventListener('click', async () => { await showSection('quotes'); await openDetail(quote.id); });
  action.append(button); row.append(action); return row;
}
async function loadOverview() {
  const [statsResponse, quotesResponse] = await Promise.all([api('/overview'), api('/quotes?page=1')]);
  const stats = await statsResponse.json(); const quotes = await quotesResponse.json();
  $('#stat-total').textContent = stats.Total;
  $('#stat-done').textContent = stats['Concluída'];
  $('#stat-pending').textContent = stats['Recebida'] + stats['Em processamento'];
  $('#stat-failed').textContent = stats['Falha'];
  $('#recent-rows').replaceChildren(...quotes.items.slice(0, 5).map(q => rowFor(q, true)));
}
async function loadQuotes() {
  const params = new URLSearchParams({ page: String(currentPage), search: $('#search').value.trim(), status: $('#status-filter').value });
  const response = await api('/quotes?' + params);
  const result = await response.json(); currentTotal = result.total;
  $('#quote-rows').replaceChildren(...result.items.map(q => rowFor(q)));
  $('#page-info').textContent = `Página ${currentPage} · ${currentTotal} pedido(s)`;
  $('#prev-page').disabled = currentPage <= 1;
  $('#next-page').disabled = currentPage * 20 >= currentTotal;
  if (!result.items.length) {
    const row = document.createElement('tr'); const cell = td('Nenhuma solicitação encontrada.'); cell.colSpan = 6; row.append(cell); $('#quote-rows').append(row);
  }
}
function field(label, value) {
  const item = document.createElement('div'), caption = document.createElement('span'), strong = document.createElement('strong');
  caption.textContent = label; strong.textContent = value || '—'; item.append(caption, strong); return item;
}
async function openDetail(id) {
  const response = await api('/quotes/' + id); const { quote, history } = await response.json();
  const grid = document.createElement('div'); grid.className = 'detail-grid';
  grid.append(field('Protocolo', quote.id), field('Status', quote.status), field('Nome', quote.nome), field('E-mail', quote.email), field('Telefone', quote.telefone), field('Serviço', quote.servico), field('Quantidade', String(quote.quantidade)), field('Valor estimado', currency(quote.valorEstimado)), field('Descrição', quote.descricao), field('Último erro', quote.ultimoErro));
  $('#detail-content').replaceChildren(grid);
  const entries = history.map(entry => {
    const li = document.createElement('li'); li.textContent = entry.origem === 'pdf' ? 'PDF gerado' : entry.de === entry.para ? `${entry.para} · ${entry.origem}` : `${entry.de ? entry.de + ' para ' : ''}${entry.para} · ${entry.origem}`;
    const small = document.createElement('small'); small.textContent = `${date(entry.em)}${entry.detalhe ? ' · ' + entry.detalhe : ''}`; li.append(small); return li;
  }); $('#history-list').replaceChildren(...entries);
  $('#detail-actions').replaceChildren();
  if (quote.status === 'Falha' && !quote.pdfGerado) {
    const retry = document.createElement('button'); retry.type = 'button'; retry.className = 'secondary-button'; retry.textContent = 'Reenviar solicitação';
    retry.addEventListener('click', async () => { try { await api('/quotes/' + id + '/retry', { method: 'POST' }); await openDetail(id); await loadQuotes(); } catch (error) { alert(error.message); } });
    $('#detail-actions').append(retry);
  }
  $('#quote-detail').hidden = false;
  $('#quote-detail').scrollIntoView({ behavior: 'smooth', block: 'start' });
}
async function loadServices() {
  const response = await api('/services'); const services = await response.json(); const list = $('#service-list'); list.replaceChildren();
  for (const service of services) {
    const row = document.createElement('div'); row.className = 'settings-row';
    const name = document.createElement('strong'); name.textContent = service.nome;
    const priceLabel = document.createElement('label'); priceLabel.textContent = 'Preço unitário (R$)';
    const price = document.createElement('input'); price.type = 'number'; price.min = '0.01'; price.max = '1000000'; price.step = '0.01'; price.value = service.preco; priceLabel.append(price);
    const activeLabel = document.createElement('label'); activeLabel.className = 'check'; activeLabel.textContent = 'Ativo';
    const active = document.createElement('input'); active.type = 'checkbox'; active.checked = service.ativo; activeLabel.prepend(active);
    const save = document.createElement('button'); save.type = 'button'; save.className = 'secondary-button'; save.textContent = 'Salvar';
    save.addEventListener('click', async () => {
      try { await api('/services/' + encodeURIComponent(service.nome), { method: 'PUT', body: JSON.stringify({ preco: Number(price.value), ativo: active.checked }) }); $('#service-message').textContent = `${service.nome} atualizado.`; $('#service-message').className = 'inline-message success'; }
      catch (error) { $('#service-message').textContent = error.message; $('#service-message').className = 'inline-message error'; }
    }); row.append(name, priceLabel, activeLabel, save); list.append(row);
  }
}
async function loadTemplates() {
  const response = await api('/templates'); const templates = await response.json(); const list = $('#template-list'); list.replaceChildren();
  for (const template of templates) {
    const card = document.createElement('div'); card.className = 'template-card';
    const heading = document.createElement('h3'); heading.textContent = template.chave === 'cliente' ? 'Confirmação ao cliente' : 'Aviso à equipe';
    const subjectLabel = document.createElement('label'); subjectLabel.textContent = 'Assunto'; const subject = document.createElement('input'); subject.maxLength = 150; subject.value = template.assunto; subjectLabel.append(subject);
    const bodyLabel = document.createElement('label'); bodyLabel.textContent = 'Mensagem'; const body = document.createElement('textarea'); body.maxLength = 2000; body.value = template.corpo; bodyLabel.append(body);
    const save = document.createElement('button'); save.className = 'secondary-button'; save.type = 'button'; save.textContent = 'Salvar modelo';
    save.addEventListener('click', async () => {
      try { await api('/templates/' + template.chave, { method: 'PUT', body: JSON.stringify({ assunto: subject.value, corpo: body.value }) }); $('#template-message').textContent = 'Modelo salvo.'; $('#template-message').className = 'inline-message success'; }
      catch (error) { $('#template-message').textContent = error.message; $('#template-message').className = 'inline-message error'; }
    }); card.append(heading, subjectLabel, bodyLabel, save); list.append(card);
  }
}
async function showSection(name) {
  document.querySelectorAll('.section').forEach(section => { section.hidden = section.id !== 'section-' + name; });
  document.querySelectorAll('[data-section]').forEach(button => button.classList.toggle('active', button.dataset.section === name));
  const titles = { overview: ['Visão geral', 'Acompanhe os pedidos e o andamento da automação.'], quotes: ['Solicitações', 'Busque pedidos e consulte o histórico de cada um.'], services: ['Serviços e preços', 'Configure os valores usados nos próximos orçamentos.'], templates: ['E-mails', 'Edite as mensagens enviadas pelo workflow.'] };
  $('#page-title').textContent = titles[name][0]; $('#page-subtitle').textContent = titles[name][1];
  try {
    if (name === 'overview') await loadOverview();
    if (name === 'quotes') await loadQuotes();
    if (name === 'services') await loadServices();
    if (name === 'templates') await loadTemplates();
  } catch (error) { console.error(error); }
}

$('#login-form').addEventListener('submit', async event => {
  event.preventDefault(); const button = event.currentTarget.querySelector('button'); button.disabled = true;
  try { await api('/login', { method: 'POST', body: JSON.stringify({ password: $('#password').value }) }); $('#password').value = ''; $('#login-message').textContent = ''; showAdmin(); }
  catch (error) { $('#login-message').textContent = error.message; $('#login-message').className = 'inline-message error'; }
  finally { button.disabled = false; }
});
document.querySelectorAll('[data-section]').forEach(button => button.addEventListener('click', () => showSection(button.dataset.section)));
$('#filter-button').addEventListener('click', () => { currentPage = 1; loadQuotes(); });
$('#search').addEventListener('keydown', event => { if (event.key === 'Enter') { currentPage = 1; loadQuotes(); } });
$('#prev-page').addEventListener('click', () => { if (currentPage > 1) { currentPage--; loadQuotes(); } });
$('#next-page').addEventListener('click', () => { if (currentPage * 20 < currentTotal) { currentPage++; loadQuotes(); } });
$('#close-detail').addEventListener('click', () => { $('#quote-detail').hidden = true; });
$('#logout').addEventListener('click', async () => { try { await api('/logout', { method: 'POST' }); } finally { showLogin(); } });
$('#export-csv').addEventListener('click', async () => {
  try {
    const params = new URLSearchParams({ search: $('#search').value.trim(), status: $('#status-filter').value });
    const response = await api('/export.csv?' + params); const url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a'); link.href = url; link.download = 'solicitacoes-fluxopro.csv'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (error) { alert(error.message); }
});
api('/session').then(() => showAdmin()).catch(() => showLogin());
